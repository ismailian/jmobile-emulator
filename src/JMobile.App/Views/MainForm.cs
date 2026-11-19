using System.Diagnostics;
using System.Runtime.InteropServices;
using JMobile.App.Controls;
using JMobile.App.Hosting;
using JMobile.Emulator.Library;
using JMobile.Emulator.Session;
using JMobile.Emulator.Suites;
using JMobile.Runtime.Host;

namespace JMobile.App.Views;

/// <summary>
/// The application window: the library on the left, the screen in the middle, the keypad on the right.
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
    private const int Gutter = 18;
    private const int ButtonHeight = 48;

    private static readonly Color StatusBarColor = Color.FromArgb(0x0B, 0x16, 0x29);

    private readonly EmulatorController _controller;
    private readonly System.Windows.Forms.Timer _presentTimer = new() { Interval = 16 };
    private readonly HashSet<int> _heldKeys = [];
    private IReadOnlyList<SoftCommand> _commands = [];

    // Set while the shell is syncing the device box to a running session, so that reflecting
    // the session's profile is not mistaken for the user choosing a new default.
    private bool _syncingDevice;

    // Chrome
    private readonly Panel _header = new();
    private readonly LogoControl _logo = new();
    private readonly Panel _body = new();
    private readonly Panel _statusBar = new();

    // Library
    private readonly CardPanel _sidebar = new();
    private readonly SearchBox _search = new("Search games...");
    private readonly FlatButton _filterButton = new(string.Empty, Glyphs.Filter);
    private readonly GameListControl _games = new();
    private readonly FlatButton _addButton = new("Add Game", Glyphs.Add) { IsPrimary = true };
    private readonly FlatButton _removeButton = new("Remove", Glyphs.Delete);

    // Playback
    private readonly Panel _center = new();
    private readonly PhoneScreenControl _screen = new();
    private readonly Panel _bottomBar = new();
    private readonly FlatButton _softLeft = new(string.Empty, Glyphs.Phone);
    private readonly FlatButton _softRight = new(string.Empty) { IsPrimary = true };
    private readonly MetricsStrip _metrics = new();
    private readonly FlatButton _playButton = new("Play", Glyphs.Play);
    private readonly FlatButton _pauseButton = new("Pause", Glyphs.Pause);
    private readonly FlatButton _stopButton = new("Stop", Glyphs.Stop);

    // Keypad column
    private readonly Panel _rightColumn = new();
    private readonly CardPanel _keypadCard = new();
    private readonly CardHeader _keypadHeader = new("Virtual Keypad", Glyphs.Keyboard);
    private readonly KeypadControl _keypad = new();
    private readonly Panel _rightSoftHost = new();

    // Options
    private readonly DropDownSelector _deviceBox = new(Glyphs.Phone);
    private readonly DropDownSelector _scaleBox = new(Glyphs.Fit);
    private readonly ToggleChip _skinToggle = new("Skin", Glyphs.Palette);
    private readonly ToggleChip _keypadToggle = new("Keypad", Glyphs.Keypad);
    private readonly ToggleChip _audioToggle = new("Audio", Glyphs.Volume);
    private readonly ToggleChip _diagnosticsToggle = new("Log", Glyphs.List);
    private readonly FlatButton _settingsButton = new(string.Empty, Glyphs.Settings) { Borderless = true };

    // Diagnostics and status
    private readonly Panel _diagnosticsHost = new();
    private readonly TextBox _log = new();
    private readonly StatusIndicator _status = new();
    private readonly DropDownSelector _languageBox = new(Glyphs.Globe);
    private readonly FlatButton _infoButton = new(string.Empty, Glyphs.Info) { Borderless = true };
    private readonly ToolTip _tips = new();

    public MainForm(EmulatorController controller)
    {
        _controller = controller;

        Text = "JMobile — Java ME Emulator";
        Icon = Theme.CreateAppIcon();
        MinimumSize = new Size(1000, 700);

        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(1540, area.Width * 92 / 100), Math.Min(1020, area.Height * 92 / 100));
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        Theme.Apply(this);

        BuildHeader();
        BuildSidebar();
        BuildKeypadColumn();
        BuildCenter();
        BuildStatusBar();

        _body.Dock = DockStyle.Fill;
        _body.Padding = new Padding(Gutter, 4, Gutter, Gutter);

        // Docked children are laid out in reverse order of addition, so the fill goes in first.
        _body.Controls.Add(_center);
        _body.Controls.Add(new Panel { Dock = DockStyle.Left, Width = Gutter });
        _body.Controls.Add(_sidebar);
        _body.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Gutter });
        _body.Controls.Add(_rightColumn);

        Controls.Add(_body);
        Controls.Add(_header);
        Controls.Add(_statusBar);

        WireEvents();
        ApplyKeypadVisibility();
        RefreshLibrary();
        SetSoftCommands([]);
        UpdateCommandStates();

        _presentTimer.Tick += (_, _) => OnPresentTick();
        _presentTimer.Start();
    }

    // ---------------------------------------------------------------------------------
    // Window chrome
    // ---------------------------------------------------------------------------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // A dark title bar, and on Windows 11 one tinted to the window, so the frame does not
        // sit as a white strip above a dark app. Older systems ignore the attributes they lack.
        const int useImmersiveDarkMode = 20;
        const int captionColor = 35;

        int enabled = 1;
        DwmSetWindowAttribute(Handle, useImmersiveDarkMode, ref enabled, sizeof(int));

        int color = ColorTranslator.ToWin32(Theme.Background);
        DwmSetWindowAttribute(Handle, captionColor, ref color, sizeof(int));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        // Side columns take a share of the width within limits, so the screen keeps the most room.
        int width = ClientSize.Width;
        _sidebar.Width = Math.Clamp(width * 24 / 100, 270, 390);
        _rightColumn.Width = Math.Clamp(width * 20 / 100, 250, 320);
    }

    // ---------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------

    private void BuildHeader()
    {
        _header.Dock = DockStyle.Top;
        _header.Height = 100;

        foreach (DeviceProfile profile in DeviceProfile.BuiltIn) _deviceBox.Items.Add(profile.Name);
        _deviceBox.SelectedItem = _controller.Settings.DeviceProfileName;
        if (_deviceBox.SelectedIndex < 0 && _deviceBox.Items.Count > 0) _deviceBox.SelectedIndex = 0;

        _scaleBox.Items.AddRange(["Original", "Fit (whole)", "Fit (smooth)"]);
        _scaleBox.SelectedIndex = (int)_controller.Settings.Scaling;

        _skinToggle.Checked = _controller.Settings.ShowPhoneSkin;
        _keypadToggle.Checked = true;
        _audioToggle.Checked = _controller.Settings.AudioEnabled;
        _diagnosticsToggle.Checked = _controller.Settings.ShowDiagnostics;

        _tips.SetToolTip(_deviceBox, "Handset to emulate");
        _tips.SetToolTip(_scaleBox, "How the screen is scaled");
        _tips.SetToolTip(_skinToggle, "Show the frame around the screen");
        _tips.SetToolTip(_keypadToggle, "Show the virtual keypad");
        _tips.SetToolTip(_audioToggle, "Play game sound");
        _tips.SetToolTip(_diagnosticsToggle, "Show the diagnostics log");
        _tips.SetToolTip(_settingsButton, "Settings");

        _header.Controls.AddRange([
            _logo, _addButton, _playButton, _pauseButton, _stopButton, _deviceBox, _scaleBox,
            _skinToggle, _keypadToggle, _audioToggle, _diagnosticsToggle, _settingsButton,
        ]);

        _header.Resize += (_, _) => LayoutHeader();
    }

    /// <summary>
    /// Places the header's controls left to right, giving up labels and then the word mark as the
    /// window narrows, rather than letting anything overlap or fall off the edge.
    /// </summary>
    private void LayoutHeader()
    {
        const int gap = 12;
        int width = _header.ClientSize.Width;
        int y = ((_header.Height - ButtonHeight) / 2) + 2;

        int buttonsWidth = _addButton.PreferredWidth + _playButton.PreferredWidth +
                           Math.Max(_pauseButton.PreferredWidth, 124) + _stopButton.PreferredWidth + (gap * 4);
        ToggleChip[] toggles = [_skinToggle, _keypadToggle, _audioToggle, _diagnosticsToggle];

        int RightClusterWidth() => toggles.Sum(toggle => toggle.PreferredWidth + 4) + 48;

        const int minimumSelectors = 200 + 140 + gap;

        foreach (ToggleChip toggle in toggles) toggle.ShowText = true;
        _logo.Compact = false;

        if (Gutter + _logo.PreferredWidth + buttonsWidth + minimumSelectors + gap + RightClusterWidth() + Gutter > width)
        {
            foreach (ToggleChip toggle in toggles) toggle.ShowText = false;
        }

        if (Gutter + _logo.PreferredWidth + buttonsWidth + minimumSelectors + gap + RightClusterWidth() + Gutter > width)
        {
            _logo.Compact = true;
        }

        _logo.SetBounds(Gutter - 6, (_header.Height - _logo.Height) / 2, _logo.PreferredWidth, _logo.Height);
        int x = _logo.Right + 8;

        foreach (FlatButton button in new[] { _addButton, _playButton, _pauseButton, _stopButton })
        {
            int buttonWidth = button == _pauseButton ? Math.Max(button.PreferredWidth, 124) : button.PreferredWidth;
            button.SetBounds(x, y, buttonWidth, ButtonHeight);
            x += buttonWidth + gap;
        }

        // Right cluster: the settings cog at the edge, toggles to its left.
        int right = width - Gutter;
        _settingsButton.SetBounds(right - 44, y + 2, 44, 44);
        right -= 48;

        for (int i = toggles.Length - 1; i >= 0; i--)
        {
            int toggleWidth = toggles[i].PreferredWidth;
            toggles[i].SetBounds(right - toggleWidth, y + 4, toggleWidth, 40);
            right -= toggleWidth + 4;
        }

        // The selectors share what is left, the device list taking the larger part.
        int available = Math.Max(0, right - gap - x);
        int scaleWidth = Math.Clamp(available * 36 / 100, 120, 160);
        int deviceWidth = Math.Clamp(available - scaleWidth - gap, 150, 290);

        _deviceBox.SetBounds(x, y, deviceWidth, ButtonHeight);
        _scaleBox.SetBounds(_deviceBox.Right + gap, y, scaleWidth, ButtonHeight);
    }

    private void BuildSidebar()
    {
        _sidebar.Dock = DockStyle.Left;
        _sidebar.Width = 340;
        _sidebar.Padding = new Padding(14, 16, 14, 16);
        _sidebar.BorderColor = Theme.LineStrong;

        var searchRow = new Panel { Dock = DockStyle.Top, Height = 46 };
        _filterButton.Dock = DockStyle.Right;
        _filterButton.Width = 50;
        _tips.SetToolTip(_filterButton, "Sort games");
        _search.Dock = DockStyle.Fill;
        searchRow.Controls.Add(_search);
        searchRow.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 12 });
        searchRow.Controls.Add(_filterButton);

        _games.Dock = DockStyle.Fill;
        _games.AllowDrop = true;

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 62 };
        _removeButton.SetBounds(6, 12, 150, ButtonHeight);
        footer.Controls.Add(_removeButton);

        _sidebar.Controls.Add(_games);
        _sidebar.Controls.Add(footer);
        _sidebar.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 14 });
        _sidebar.Controls.Add(searchRow);
    }

    private void BuildKeypadColumn()
    {
        _rightColumn.Dock = DockStyle.Right;
        _rightColumn.Width = 310;

        // The soft keys are not on the pad: the buttons beneath it and the screen show them,
        // with the labels the running MIDlet actually gave them.
        _keypadCard.Dock = DockStyle.Fill;
        _keypadCard.BorderColor = Theme.LineStrong;
        _keypadCard.Padding = new Padding(1, 1, 1, 8);

        _keypadHeader.Dock = DockStyle.Top;
        _keypad.Dock = DockStyle.Fill;

        _keypadCard.Controls.Add(_keypad);
        _keypadCard.Controls.Add(_keypadHeader);

        _rightSoftHost.Dock = DockStyle.Bottom;
        _rightSoftHost.Height = 12 + 2 + ButtonHeight + 2;
        _rightSoftHost.Padding = new Padding(0, 14, 0, 0);
        _softRight.Dock = DockStyle.Fill;
        _rightSoftHost.Controls.Add(_softRight);

        _rightColumn.Controls.Add(_keypadCard);
        _rightColumn.Controls.Add(_rightSoftHost);
    }

    private void BuildCenter()
    {
        _center.Dock = DockStyle.Fill;

        _screen.Dock = DockStyle.Fill;
        _screen.Scaling = _controller.Settings.Scaling;
        _screen.ShowSkin = _controller.Settings.ShowPhoneSkin;

        _bottomBar.Dock = DockStyle.Bottom;
        _bottomBar.Height = _rightSoftHost.Height;
        _metrics.LeadingDivider = true;
        _tips.SetToolTip(_softLeft, "Left soft key (Q)");
        _tips.SetToolTip(_softRight, "Right soft key (E)");
        _bottomBar.Controls.AddRange([_softLeft, _metrics]);
        _bottomBar.Layout += (_, _) => LayoutBottomBar();

        // The log sits between the screen and the soft keys, in a card of its own.
        _diagnosticsHost.Dock = DockStyle.Bottom;
        _diagnosticsHost.Height = 190;
        _diagnosticsHost.Padding = new Padding(0, 14, 0, 0);
        _diagnosticsHost.Visible = _controller.Settings.ShowDiagnostics;

        var logCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 8, 10), Radius = 12 };
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Dock = DockStyle.Fill;
        _log.BackColor = Theme.Surface;
        _log.ForeColor = Theme.TextMuted;
        _log.Font = Theme.MonoFont;
        _log.BorderStyle = BorderStyle.None;
        logCard.Controls.Add(_log);
        _diagnosticsHost.Controls.Add(logCard);

        _center.Controls.Add(_screen);
        _center.Controls.Add(_diagnosticsHost);
        _center.Controls.Add(_bottomBar);
    }

    private void LayoutBottomBar()
    {
        int y = _bottomBar.Height - ButtonHeight - 2;
        int x = 0;
        int right = _bottomBar.ClientSize.Width;

        if (_softRight.Parent == _bottomBar && _softRight.Visible)
        {
            int width = Math.Max(_softRight.PreferredWidth, 170);
            _softRight.SetBounds(right - width, y, width, ButtonHeight);
            right -= width + 16;
        }

        if (_softLeft.Visible)
        {
            int width = Math.Max(_softLeft.PreferredWidth, 170);
            _softLeft.SetBounds(0, y, width, ButtonHeight);
            x = width + 24;
        }

        _metrics.LeadingDivider = _softLeft.Visible;
        _metrics.SetBounds(x, y, Math.Max(0, right - x), ButtonHeight);
        _metrics.Invalidate();
    }

    private void BuildStatusBar()
    {
        _statusBar.Dock = DockStyle.Bottom;
        _statusBar.Height = 62;
        _statusBar.BackColor = StatusBarColor;
        _statusBar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Line);
            e.Graphics.DrawLine(pen, 0, 0, _statusBar.Width, 0);
        };

        _status.Text = "Ready";

        // The shell is English only for now; the selector is where translations will appear.
        _languageBox.Items.Add("English");
        _languageBox.SelectedIndex = 0;
        _tips.SetToolTip(_languageBox, "Language");
        _tips.SetToolTip(_infoButton, "About JMobile");

        _statusBar.Controls.AddRange([_status, _languageBox, _infoButton]);
        _statusBar.Resize += (_, _) =>
        {
            int height = _statusBar.Height;
            _status.SetBounds(Gutter + 6, (height - _status.Height) / 2, Math.Max(100, _statusBar.Width - 360), _status.Height);
            _infoButton.SetBounds(_statusBar.Width - Gutter - 44, (height - 44) / 2, 44, 44);
            _languageBox.SetBounds(_infoButton.Left - 12 - 170, (height - 44) / 2, 170, 44);
        };
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
        _filterButton.Click += (_, _) => ShowSortMenu();
        _settingsButton.Click += (_, _) => ShowSettingsMenu();
        _infoButton.Click += (_, _) => ShowAbout();

        _search.TextEntered += _games.SetFilter;
        _games.SelectionChanged += _ => UpdateCommandStates();
        _games.ItemActivated += _ => LaunchSelected();

        _controller.Library.Changed += () => BeginInvoke(RefreshLibrary);
        _controller.Logged += (text, isError) => BeginInvoke(() => AppendLog(text, isError));
        _controller.SessionChanged += session => BeginInvoke(() => OnSessionChanged(session));
        _controller.Feedback.VibrateRequested += _ => BeginInvoke(_screen.Flash);

        _softLeft.Click += (_, _) => InvokeSoftCommand(0);
        _softLeft.ChevronClicked += (_, _) => ShowCommandMenu();
        _softRight.Click += (_, _) => InvokeSoftCommand(_commands.Count - 1);

        _keypad.KeyPressedDown += code => _controller.Session?.PressKey(code);
        _keypad.KeyReleasedUp += code => _controller.Session?.ReleaseKey(code);
        _keypadHeader.CollapsedChanged += (_, _) => ApplyKeypadVisibility();

        _screen.KeyDown += OnScreenKeyDown;
        _screen.KeyUp += OnScreenKeyUp;
        _screen.MouseDown += OnScreenMouseDown;
        _screen.MouseUp += OnScreenMouseUp;
        _screen.MouseMove += OnScreenMouseMove;
        _screen.Click += (_, _) => _screen.Focus();

        _deviceBox.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingDevice || _deviceBox.SelectedItem is not { } name) return;

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

        _keypadToggle.CheckedChanged += (_, _) => ApplyKeypadVisibility();

        _audioToggle.CheckedChanged += (_, _) =>
        {
            _controller.Settings.AudioEnabled = _audioToggle.Checked;
            _controller.SaveSettings();
        };

        _diagnosticsToggle.CheckedChanged += (_, _) =>
        {
            _controller.Settings.ShowDiagnostics = _diagnosticsToggle.Checked;
            _diagnosticsHost.Visible = _diagnosticsToggle.Checked;
            _controller.SaveSettings();
        };

        // Files can be dropped anywhere a player is likely to aim: the window, the list or the screen.
        foreach (Control target in new Control[] { this, _games, _screen })
        {
            target.DragEnter += (_, e) =>
            {
                if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
            };

            target.DragDrop += (_, e) => DropFiles(e.Data?.GetData(DataFormats.FileDrop) as string[]);
        }

        FormClosing += (_, _) => _presentTimer.Stop();
    }

    /// <summary>
    /// Shows or hides the keypad, keeping the right soft key reachable either way: it sits under
    /// the pad when the pad is shown, and moves beside the left soft key when it is not.
    /// </summary>
    private void ApplyKeypadVisibility()
    {
        bool showColumn = _keypadToggle.Checked;
        _rightColumn.Visible = showColumn;

        if (showColumn)
        {
            bool collapsed = _keypadHeader.Collapsed;
            _keypad.Visible = !collapsed;
            _keypadCard.Dock = collapsed ? DockStyle.Top : DockStyle.Fill;
            if (collapsed) _keypadCard.Height = _keypadHeader.Height + 2;

            if (_softRight.Parent != _rightSoftHost)
            {
                _bottomBar.Controls.Remove(_softRight);
                _softRight.Dock = DockStyle.Fill;
                _rightSoftHost.Controls.Add(_softRight);
            }
        }
        else if (_softRight.Parent != _bottomBar)
        {
            _rightSoftHost.Controls.Remove(_softRight);
            _softRight.Dock = DockStyle.None;
            _bottomBar.Controls.Add(_softRight);
        }

        _bottomBar.PerformLayout();
    }

    private void ShowSortMenu()
    {
        ContextMenuStrip menu = Menus.Create();

        foreach ((GameSortOrder order, string label) in new[]
                 {
                     (GameSortOrder.RecentlyPlayed, "Recently played"),
                     (GameSortOrder.Name, "Name"),
                     (GameSortOrder.MostPlayed, "Most played"),
                     (GameSortOrder.RecentlyAdded, "Recently added"),
                 })
        {
            var item = new ToolStripMenuItem(label) { Checked = _games.SortOrder == order };
            item.Click += (_, _) => _games.SortOrder = order;
            menu.Items.Add(item);
        }

        Menus.ShowBelow(menu, _filterButton);
    }

    private void ShowSettingsMenu()
    {
        ContextMenuStrip menu = Menus.Create();
        EmulatorSettings settings = _controller.Settings;

        var network = new ToolStripMenuItem("Allow games to use the network") { Checked = settings.NetworkEnabled };
        network.Click += (_, _) =>
        {
            settings.NetworkEnabled = !settings.NetworkEnabled;
            _controller.Network.IsEnabled = settings.NetworkEnabled;
            _controller.SaveSettings();
        };

        var copy = new ToolStripMenuItem("Copy added games into the library") { Checked = settings.CopyImportedGames };
        copy.Click += (_, _) =>
        {
            settings.CopyImportedGames = !settings.CopyImportedGames;
            _controller.SaveSettings();
        };

        var folder = new ToolStripMenuItem("Open data folder");
        folder.Click += (_, _) =>
            Process.Start(new ProcessStartInfo { FileName = _controller.Paths.Root, UseShellExecute = true });

        menu.Items.AddRange([network, copy, new ToolStripSeparator(), folder]);
        menu.Show(_settingsButton, new Point(_settingsButton.Width, _settingsButton.Height + 4),
            ToolStripDropDownDirection.BelowLeft);
    }

    private void ShowAbout()
    {
        string version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        MessageBox.Show(this,
            $"JMobile {version}\nA Java ME (MIDP 2.0 / CLDC 1.1) emulator.\n\n" +
            "D-pad: arrow keys or W A S D\nFire: Enter or Space\nSoft keys: Q and E\n" +
            "Keypad: number keys, [ for *, ] for #",
            "About JMobile", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        _screen.Focus();
    }

    private void OnSessionChanged(EmulatorSession? session)
    {
        _screen.Attach(session?.Screen);
        SetSoftCommands(session?.Commands ?? []);

        if (session is not null)
        {
            session.FrameReady += _screen.MarkDirty;
            session.CommandsChanged += commands => BeginInvoke(() => SetSoftCommands(commands));
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

    /// <summary>
    /// Labels the soft keys with the running MIDlet's commands.
    /// </summary>
    /// <remarks>
    /// On a handset these two keys were labelled by whatever the application had put there, and
    /// MIDlets rely on that: a game attaches an "Exit" or "Menu" command and expects the player
    /// to find it. The left key takes the first command and the right key the last; the arrow on
    /// the left key lists every command, so ones in between are reachable too.
    /// </remarks>
    private void SetSoftCommands(IReadOnlyList<SoftCommand> commands)
    {
        _commands = commands;

        _softLeft.Visible = commands.Count >= 1;
        _softLeft.Text = commands.Count >= 1 ? commands[0].Label : string.Empty;
        _softLeft.ShowChevron = commands.Count >= 2;

        _softRight.Visible = commands.Count >= 2;
        string rightLabel = commands.Count >= 2 ? commands[^1].Label : string.Empty;
        _softRight.Text = rightLabel;
        _softRight.Glyph = IsLeaveCommand(rightLabel) ? Glyphs.SignOut : null;

        _bottomBar.PerformLayout();
    }

    private static bool IsLeaveCommand(string label) =>
        label.Contains("exit", StringComparison.OrdinalIgnoreCase) ||
        label.Contains("quit", StringComparison.OrdinalIgnoreCase) ||
        label.Contains("back", StringComparison.OrdinalIgnoreCase);

    private void InvokeSoftCommand(int index)
    {
        if ((uint)index >= (uint)_commands.Count) return;

        _controller.Session?.InvokeCommand(_commands[index]);
        _screen.Focus();
    }

    private void ShowCommandMenu()
    {
        if (_commands.Count == 0) return;

        ContextMenuStrip menu = Menus.Create();
        for (int i = 0; i < _commands.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(_commands[i].Label);
            item.Click += (_, _) => InvokeSoftCommand(index);
            menu.Items.Add(item);
        }

        Menus.ShowAbove(menu, _softLeft);
    }

    private void UpdateCommandStates()
    {
        EmulatorSession? session = _controller.Session;
        bool running = session is { State: SessionState.Running or SessionState.Paused };
        bool paused = session?.State == SessionState.Paused;

        _playButton.Enabled = _games.SelectedEntry is not null;
        _stopButton.Enabled = running;
        _pauseButton.Enabled = running;
        _pauseButton.Text = paused ? "Resume" : "Pause";
        _pauseButton.Glyph = paused ? Glyphs.Play : Glyphs.Pause;
        _removeButton.Enabled = _games.SelectedEntry is not null;

        _status.Text = session is null
            ? _games.SelectedEntry is { } selected ? $"Ready — {selected.Title}" : "Ready"
            : $"{session.Suite.Name} — {session.State}";

        _status.DotColor = session?.State switch
        {
            SessionState.Paused or SessionState.Starting or SessionState.Created => Theme.Warning,
            SessionState.Failed => Theme.Danger,
            _ => Theme.Success,
        };

        _status.Invalidate();
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
            _metrics.SetItems([]);
            return;
        }

        _metrics.SetItems([
            (Glyphs.Speed, $"{session.FramesPerSecond:F1} fps"),
            (Glyphs.Package, $"{session.LoadedClassCount} Classes"),
            (Glyphs.Memory, $"{session.ManagedMemory / (1024 * 1024)} MB"),
            (Glyphs.Clock, $"{session.Uptime:hh\\:mm\\:ss}"),
        ]);
    }

    private void AppendLog(string text, bool isError)
    {
        // The log is a diagnostic, not a record. Trimming keeps a game that logs every frame
        // from growing the buffer without bound.
        if (_log.TextLength > 60000) _log.Clear();

        _log.AppendText(isError ? "! " + text : text);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _presentTimer.Dispose();
            _tips.Dispose();
            _controller.Dispose();
        }

        base.Dispose(disposing);
    }
}
