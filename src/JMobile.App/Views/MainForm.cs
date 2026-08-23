using JMobile.App.Controls;
using JMobile.App.Hosting;
using JMobile.Emulator.Library;
using JMobile.Emulator.Session;
using JMobile.Emulator.Suites;
using JMobile.Runtime.Host;

namespace JMobile.App.Views;

/// <summary>
/// The application window: a library on the left, the handset on the right.
/// </summary>
/// <remarks>
/// The form is the view in a model-view-presenter split. It builds its controls, forwards user
/// gestures to <see cref="EmulatorController"/>, and paints what the controller reports; it
/// holds no emulator state of its own.
/// <para>
/// Frames are pulled, not pushed. The emulator thread only sets a flag when it finishes
/// painting, and a timer on the UI thread repaints at display rate. Marshalling every frame
/// across threads would flood the message queue whenever a game runs faster than the window can
/// present, and every one of those extra repaints would be discarded.
/// </para>
/// </remarks>
public sealed class MainForm : Form
{
    private readonly EmulatorController _controller;
    private readonly System.Windows.Forms.Timer _presentTimer = new() { Interval = 16 };
    private readonly HashSet<int> _heldKeys = [];

    // Set while the shell is syncing the device box to a running session, so that reflecting
    // the session's profile is not mistaken for the user choosing a new default.
    private bool _syncingDevice;

    // Chrome
    private readonly Panel _toolbar = new();
    private readonly Panel _sidebar = new();
    private readonly Panel _playArea = new();
    private readonly Panel _statusBar = new();

    // Library
    private readonly SearchBox _search = new("Search games");
    private readonly GameListControl _games = new();
    private readonly FlatButton _addButton = new("Add game");
    private readonly FlatButton _removeButton = new("Remove");

    // Playback
    private readonly PhoneScreenControl _screen = new();
    private readonly SoftKeyBar _softKeys = new();
    private readonly KeypadControl _keypad = new();
    private readonly FlatButton _playButton = new("Play");
    private readonly FlatButton _stopButton = new("Stop");
    private readonly FlatButton _pauseButton = new("Pause");

    // Options
    private readonly ComboBox _deviceBox = new();
    private readonly ComboBox _scaleBox = new();
    private readonly CheckBox _skinToggle = new();
    private readonly CheckBox _audioToggle = new();
    private readonly CheckBox _keypadToggle = new();
    private readonly CheckBox _diagnosticsToggle = new();

    // Diagnostics
    private readonly Panel _diagnosticsPanel = new();
    private readonly TextBox _log = new();
    private readonly Label _statusLabel = new();
    private readonly Label _metricsLabel = new();

    public MainForm(EmulatorController controller)
    {
        _controller = controller;

        Text = "JMobile — Java ME Emulator";
        MinimumSize = new Size(980, 660);
        Size = new Size(1240, 820);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        Theme.Apply(this);

        BuildToolbar();
        BuildSidebar();
        BuildPlayArea();
        BuildStatusBar();

        // Docked children are laid out in reverse order of addition, so the fill goes in first.
        Controls.Add(_playArea);
        Controls.Add(_sidebar);
        Controls.Add(_toolbar);
        Controls.Add(_statusBar);

        WireEvents();
        RefreshLibrary();
        UpdateCommandStates();

        _presentTimer.Tick += (_, _) => OnPresentTick();
        _presentTimer.Start();
    }

    // ---------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------

    private void BuildToolbar()
    {
        _toolbar.Dock = DockStyle.Top;
        _toolbar.Height = 52;
        _toolbar.BackColor = Theme.Surface;
        _toolbar.Padding = new Padding(14, 11, 14, 11);

        var title = new Label
        {
            Text = "JMobile",
            Font = Theme.TitleFont,
            ForeColor = Theme.Accent,
            AutoSize = true,
            Location = new Point(16, 14),
        };

        _addButton.IsPrimary = true;
        _addButton.Location = new Point(110, 11);
        _addButton.Size = new Size(96, 30);

        _playButton.Location = new Point(216, 11);
        _playButton.Size = new Size(72, 30);

        _pauseButton.Location = new Point(294, 11);
        _pauseButton.Size = new Size(72, 30);

        _stopButton.Location = new Point(372, 11);
        _stopButton.Size = new Size(72, 30);

        StyleCombo(_deviceBox);
        _deviceBox.Location = new Point(462, 13);
        _deviceBox.Width = 190;
        foreach (DeviceProfile profile in DeviceProfile.BuiltIn) _deviceBox.Items.Add(profile.Name);
        _deviceBox.SelectedItem = _controller.Settings.DeviceProfileName;
        if (_deviceBox.SelectedIndex < 0 && _deviceBox.Items.Count > 0) _deviceBox.SelectedIndex = 0;

        StyleCombo(_scaleBox);
        _scaleBox.Location = new Point(662, 13);
        _scaleBox.Width = 120;
        _scaleBox.Items.AddRange(["Original", "Fit (whole)", "Fit (smooth)"]);
        _scaleBox.SelectedIndex = (int)_controller.Settings.Scaling;

        StyleCheck(_skinToggle, "Skin", _controller.Settings.ShowPhoneSkin, new Point(796, 17));
        StyleCheck(_keypadToggle, "Keypad", true, new Point(858, 17));
        StyleCheck(_audioToggle, "Audio", _controller.Settings.AudioEnabled, new Point(936, 17));
        StyleCheck(_diagnosticsToggle, "Log", _controller.Settings.ShowDiagnostics, new Point(1006, 17));

        _toolbar.Controls.AddRange([
            title, _addButton, _playButton, _pauseButton, _stopButton,
            _deviceBox, _scaleBox, _skinToggle, _keypadToggle, _audioToggle, _diagnosticsToggle,
        ]);
    }

    private static void StyleCombo(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Theme.SurfaceRaised;
        combo.ForeColor = Theme.Text;
        combo.Font = Theme.UiFont;
    }

    private static void StyleCheck(CheckBox check, string text, bool value, Point location)
    {
        check.Text = text;
        check.Checked = value;
        check.AutoSize = true;
        check.Location = location;
        check.ForeColor = Theme.TextMuted;
        check.Font = Theme.UiFont;
        check.FlatStyle = FlatStyle.Flat;
    }

    private void BuildSidebar()
    {
        _sidebar.Dock = DockStyle.Left;
        _sidebar.Width = 300;
        _sidebar.BackColor = Theme.Surface;
        _sidebar.Padding = new Padding(10, 10, 10, 10);

        _search.Dock = DockStyle.Top;

        var searchSpacer = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Theme.Surface };

        _games.Dock = DockStyle.Fill;

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 42, BackColor = Theme.Surface };
        _removeButton.Location = new Point(0, 8);
        _removeButton.Size = new Size(92, 28);
        footer.Controls.Add(_removeButton);

        _sidebar.Controls.Add(_games);
        _sidebar.Controls.Add(footer);
        _sidebar.Controls.Add(searchSpacer);
        _sidebar.Controls.Add(_search);
    }

    private void BuildPlayArea()
    {
        _playArea.Dock = DockStyle.Fill;
        _playArea.BackColor = Theme.Background;

        _screen.Dock = DockStyle.Fill;
        _screen.Scaling = _controller.Settings.Scaling;
        _screen.ShowSkin = _controller.Settings.ShowPhoneSkin;

        _softKeys.Dock = DockStyle.Bottom;

        // The keypad sits beside the screen rather than under it. Docked below, it took the
        // vertical space that decides the integer scale factor, and a 176x208 handset screen
        // ended up rendering at 1x in a window with room for 3x.
        _keypad.Dock = DockStyle.Right;
        _keypad.Width = 236;

        _diagnosticsPanel.Dock = DockStyle.Bottom;
        _diagnosticsPanel.Height = 150;
        _diagnosticsPanel.BackColor = Theme.Surface;
        _diagnosticsPanel.Padding = new Padding(10, 8, 10, 8);
        _diagnosticsPanel.Visible = _controller.Settings.ShowDiagnostics;

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Dock = DockStyle.Fill;
        _log.BackColor = Theme.Background;
        _log.ForeColor = Theme.TextMuted;
        _log.Font = Theme.MonoFont;
        _log.BorderStyle = BorderStyle.None;
        _diagnosticsPanel.Controls.Add(_log);

        _playArea.Controls.Add(_screen);
        _playArea.Controls.Add(_softKeys);
        _playArea.Controls.Add(_keypad);
        _playArea.Controls.Add(_diagnosticsPanel);
    }

    private void BuildStatusBar()
    {
        _statusBar.Dock = DockStyle.Bottom;
        _statusBar.Height = 26;
        _statusBar.BackColor = Theme.Surface;

        _statusLabel.Text = "Ready";
        _statusLabel.ForeColor = Theme.TextMuted;
        _statusLabel.Font = Theme.SmallFont;
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(14, 6);

        _metricsLabel.ForeColor = Theme.TextFaint;
        _metricsLabel.Font = Theme.MonoFont;
        _metricsLabel.AutoSize = true;
        _metricsLabel.Location = new Point(360, 6);

        _statusBar.Controls.AddRange([_statusLabel, _metricsLabel]);
    }

    // ---------------------------------------------------------------------------------
    // Wiring
    // ---------------------------------------------------------------------------------

    private void WireEvents()
    {
        _addButton.Click += (_, _) => BrowseForGame();
        _removeButton.Click += (_, _) => RemoveSelected();
        _playButton.Click += (_, _) => LaunchSelected();
        _stopButton.Click += (_, _) => StopSession();
        _pauseButton.Click += (_, _) => TogglePause();

        _search.TextEntered += _games.SetFilter;
        _games.SelectionChanged += _ => UpdateCommandStates();
        _games.ItemActivated += _ => LaunchSelected();

        _controller.Library.Changed += () => BeginInvoke(RefreshLibrary);
        _controller.Logged += (text, isError) => BeginInvoke(() => AppendLog(text, isError));
        _controller.SessionChanged += session => BeginInvoke(() => OnSessionChanged(session));
        _controller.Feedback.VibrateRequested += _ => BeginInvoke(FlashScreenBorder);

        _softKeys.CommandInvoked += command => _controller.Session?.InvokeCommand(command);

        _keypad.KeyPressedDown += code => _controller.Session?.PressKey(code);
        _keypad.KeyReleasedUp += code => _controller.Session?.ReleaseKey(code);

        _screen.KeyDown += OnScreenKeyDown;
        _screen.KeyUp += OnScreenKeyUp;
        _screen.MouseDown += OnScreenMouseDown;
        _screen.MouseUp += OnScreenMouseUp;
        _screen.MouseMove += OnScreenMouseMove;
        _screen.Click += (_, _) => _screen.Focus();

        _deviceBox.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingDevice || _deviceBox.SelectedItem is not string name) return;

            _controller.Settings.DeviceProfileName = name;
            _controller.SaveSettings();
        };

        _scaleBox.SelectedIndexChanged += (_, _) =>
        {
            _controller.Settings.Scaling = (ScalingMode)Math.Max(0, _scaleBox.SelectedIndex);
            _screen.Scaling = _controller.Settings.Scaling;
            _screen.Invalidate();
            _controller.SaveSettings();
        };

        _skinToggle.CheckedChanged += (_, _) =>
        {
            _controller.Settings.ShowPhoneSkin = _skinToggle.Checked;
            _screen.ShowSkin = _skinToggle.Checked;
            _screen.Invalidate();
            _controller.SaveSettings();
        };

        _keypadToggle.CheckedChanged += (_, _) => _keypad.Visible = _keypadToggle.Checked;

        _audioToggle.CheckedChanged += (_, _) =>
        {
            _controller.Settings.AudioEnabled = _audioToggle.Checked;
            _controller.SaveSettings();
        };

        _diagnosticsToggle.CheckedChanged += (_, _) =>
        {
            _controller.Settings.ShowDiagnostics = _diagnosticsToggle.Checked;
            _diagnosticsPanel.Visible = _diagnosticsToggle.Checked;
            _controller.SaveSettings();
        };

        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };

        DragDrop += (_, e) => DropFiles(e.Data?.GetData(DataFormats.FileDrop) as string[]);
        FormClosing += (_, _) => _presentTimer.Stop();
    }

    // ---------------------------------------------------------------------------------
    // Library actions
    // ---------------------------------------------------------------------------------

    private void BrowseForGame()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Add a Java ME game",
            Filter = "MIDlet suites (*.jar)|*.jar|All files (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        DropFiles(dialog.FileNames);
    }

    private void DropFiles(string[]? paths)
    {
        if (paths is null) return;

        var failures = new List<string>();

        foreach (string path in paths)
        {
            if (!path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{Path.GetFileName(path)} is not a .jar file");
                continue;
            }

            try
            {
                _controller.AddGame(path);
            }
            catch (Exception error)
            {
                failures.Add($"{Path.GetFileName(path)}: {error.Message}");
            }
        }

        if (failures.Count == 0) return;

        MessageBox.Show(this,
            string.Join(Environment.NewLine, failures),
            failures.Count == 1 ? "Could not add that game" : "Some games could not be added",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void RemoveSelected()
    {
        if (_games.SelectedEntry is not { } entry) return;

        DialogResult answer = MessageBox.Show(
            this,
            $"Remove \"{entry.Title}\" from the library?\n\nChoose Yes to delete its save data too, " +
            "or No to keep the saves in case you add the game again.",
            "Remove game",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (answer == DialogResult.Cancel) return;
        _controller.RemoveGame(entry, deleteSaves: answer == DialogResult.Yes);
    }

    private void RefreshLibrary()
    {
        var items = new List<GameListItem>();

        foreach (LibraryEntry entry in _controller.Library.Entries)
        {
            var item = new GameListItem(entry);

            // The icon lives inside the suite, so a missing or unreadable JAR simply has none.
            try
            {
                if (entry.JarExists && MidletSuite.Open(entry.JarPath).IconData is { } iconBytes)
                    item.Icon = DecodeIcon(iconBytes);
            }
            catch (Exception)
            {
                item.Icon = null;
            }

            items.Add(item);
        }

        _games.SetItems(items);
        UpdateCommandStates();
    }

    /// <summary>Decodes a suite icon through the runtime's own decoder, then into a GDI+ bitmap.</summary>
    private static Bitmap? DecodeIcon(byte[] data)
    {
        if (!JMobile.Runtime.Graphics.PngDecoder.IsPng(data)) return null;

        JMobile.Runtime.Graphics.Surface surface = JMobile.Runtime.Graphics.PngDecoder.Decode(data);
        var bitmap = new Bitmap(surface.Width, surface.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        System.Drawing.Imaging.BitmapData locked = bitmap.LockBits(
            new Rectangle(0, 0, surface.Width, surface.Height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            for (int y = 0; y < surface.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    surface.Pixels, y * surface.Width,
                    locked.Scan0 + (y * locked.Stride), surface.Width);
            }
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }

        return bitmap;
    }

    // ---------------------------------------------------------------------------------
    // Session actions
    // ---------------------------------------------------------------------------------

    private void LaunchSelected()
    {
        if (_games.SelectedEntry is not { } entry) return;

        if (!entry.JarExists)
        {
            MessageBox.Show(this,
                $"The file for \"{entry.Title}\" is no longer at:\n{entry.JarPath}\n\n" +
                "Add the game again to point the library at its new location.",
                "File missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _controller.Launch(entry);
            _screen.Focus();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Could not start the game",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopSession()
    {
        _controller.Stop();
        _heldKeys.Clear();
        _keypad.ClearHeld();
    }

    private void TogglePause()
    {
        if (_controller.Session is not { } session) return;

        if (session.State == SessionState.Paused) session.Resume();
        else session.Pause();

        _heldKeys.Clear();
        _keypad.ClearHeld();
        UpdateCommandStates();
    }

    private void OnSessionChanged(EmulatorSession? session)
    {
        _screen.Attach(session?.Screen);
        _softKeys.SetCommands(session?.Commands ?? []);

        if (session is not null)
        {
            session.FrameReady += _screen.MarkDirty;
            session.CommandsChanged += commands => BeginInvoke(() => _softKeys.SetCommands(commands));
            session.StateChanged += _ => BeginInvoke(UpdateCommandStates);

            // A suite that declares the screen it was drawn for overrides the default, so show
            // which handset is actually being emulated rather than the one that was selected.
            _syncingDevice = true;
            _deviceBox.SelectedItem = session.Device.Name;
            _syncingDevice = false;
        }
        else
        {
            _syncingDevice = true;
            _deviceBox.SelectedItem = _controller.Settings.DeviceProfileName;
            _syncingDevice = false;
        }

        UpdateCommandStates();
    }

    private void UpdateCommandStates()
    {
        EmulatorSession? session = _controller.Session;
        bool running = session is { State: SessionState.Running or SessionState.Paused };

        _playButton.Enabled = _games.SelectedEntry is not null;
        _stopButton.Enabled = running;
        _pauseButton.Enabled = running;
        _pauseButton.Text = session?.State == SessionState.Paused ? "Resume" : "Pause";
        _removeButton.Enabled = _games.SelectedEntry is not null;

        _statusLabel.Text = session is null
            ? _games.SelectedEntry is { } selected ? $"Ready — {selected.Title}" : "Ready"
            : $"{session.Suite.Name} — {session.State}";
    }

    // ---------------------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------------------

    private void OnScreenKeyDown(object? sender, KeyEventArgs e)
    {
        if (_controller.Session is not { } session) return;

        int code = KeyMap.ToHandsetKey(e.KeyCode);
        if (code == 0) return;

        e.Handled = true;
        e.SuppressKeyPress = true;

        // Windows repeats KeyDown while a key is held; MIDP wants one press then repeats.
        if (_heldKeys.Add(code))
        {
            session.PressKey(code);
            _keypad.SetHeld(code, true);
        }
        else
        {
            session.RepeatKey(code);
        }
    }

    private void OnScreenKeyUp(object? sender, KeyEventArgs e)
    {
        if (_controller.Session is not { } session) return;

        int code = KeyMap.ToHandsetKey(e.KeyCode);
        if (code == 0 || !_heldKeys.Remove(code)) return;

        e.Handled = true;
        session.ReleaseKey(code);
        _keypad.SetHeld(code, false);
    }

    private void OnScreenMouseDown(object? sender, MouseEventArgs e)
    {
        if (_screen.ToScreenPoint(e.Location) is { } point)
            _controller.Session?.PointerPressed(point.X, point.Y);
    }

    private void OnScreenMouseUp(object? sender, MouseEventArgs e)
    {
        if (_screen.ToScreenPoint(e.Location) is { } point)
            _controller.Session?.PointerReleased(point.X, point.Y);
    }

    private void OnScreenMouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (_screen.ToScreenPoint(e.Location) is { } point)
            _controller.Session?.PointerDragged(point.X, point.Y);
    }

    // ---------------------------------------------------------------------------------
    // Presentation
    // ---------------------------------------------------------------------------------

    private void OnPresentTick()
    {
        _screen.PresentIfDirty();

        if (_controller.Session is not { } session)
        {
            _metricsLabel.Text = string.Empty;
            return;
        }

        _metricsLabel.Text =
            $"{session.FramesPerSecond,5:F1} fps   " +
            $"{session.LoadedClassCount,4} classes   " +
            $"{session.ManagedMemory / (1024 * 1024),4} MB   " +
            $"{session.Uptime:hh\\:mm\\:ss}";
    }

    private void AppendLog(string text, bool isError)
    {
        // The log is a diagnostic, not a record. Trimming keeps a game that logs every frame
        // from growing the buffer without bound.
        if (_log.TextLength > 60000) _log.Clear();

        _log.AppendText(isError ? "! " + text : text);
    }

    private void FlashScreenBorder()
    {
        // Vibration has no desktop equivalent, so it is shown rather than felt.
        _screen.BackColor = Theme.AccentDim;
        var timer = new System.Windows.Forms.Timer { Interval = 120 };
        timer.Tick += (_, _) =>
        {
            _screen.BackColor = Theme.Background;
            timer.Stop();
            timer.Dispose();
        };
        timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _presentTimer.Dispose();
            _controller.Dispose();
        }

        base.Dispose(disposing);
    }
}
