using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace JMobile.App.Controls;

/// <summary>Base for the painted controls: double buffered, and painted over the parent's colour.</summary>
public abstract class PaintedControl : Control
{
    protected PaintedControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
    }

    protected bool Hovered { get; private set; }

    protected bool Pressed { get; private set; }

    /// <summary>Clears to the parent's background, so rounded corners blend into whatever is behind.</summary>
    protected Graphics BeginPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(Parent?.BackColor ?? Theme.Background);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return graphics;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Hovered = false;
        Pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Pressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected static int MeasureText(string text, Font font) =>
        text.Length == 0 ? 0 : TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding).Width;
}

/// <summary>
/// A rounded button drawn to match the rest of the shell, with an optional icon and dropdown arrow.
/// </summary>
/// <remarks>
/// The stock button ignores <c>BackColor</c> under visual styles and renders a light chrome
/// button regardless of the theme, so it is painted here instead.
/// </remarks>
public sealed class FlatButton : PaintedControl
{
    private const int ChevronWidth = 40;
    private bool _downInChevron;
    private string? _glyph;
    private bool _showChevron;

    public FlatButton(string text, string? glyph = null)
    {
        SetStyle(ControlStyles.Selectable, true);

        Text = text;
        _glyph = glyph;
        Size = new Size(96, 46);
        Cursor = Cursors.Hand;
        Font = Theme.ButtonFont;
    }

    /// <summary>Draws the button in the accent colour, for the primary action on a screen.</summary>
    [DefaultValue(false)]
    public bool IsPrimary { get; set; }

    /// <summary>Draws no face until hovered, for icon buttons that sit directly on a surface.</summary>
    [DefaultValue(false)]
    public bool Borderless { get; set; }

    /// <summary>Corner radius in pixels.</summary>
    [DefaultValue(10)]
    public int Radius { get; set; } = 10;

    [DefaultValue(null)]
    public string? Glyph
    {
        get => _glyph;
        set
        {
            _glyph = value;
            Invalidate();
        }
    }

    /// <summary>Shows a dropdown arrow at the right edge; clicking it raises <see cref="ChevronClicked"/>.</summary>
    [DefaultValue(false)]
    public bool ShowChevron
    {
        get => _showChevron;
        set
        {
            _showChevron = value;
            Invalidate();
        }
    }

    /// <summary>Raised instead of <c>Click</c> when the dropdown arrow was the part clicked.</summary>
    public event EventHandler? ChevronClicked;

    /// <summary>The width the button needs to show its icon, text and arrow without trimming.</summary>
    public int PreferredWidth
    {
        get
        {
            int content = MeasureText(Text, Font);
            if (_glyph is not null) content += content > 0 ? 30 : 20;
            int width = content + (Text.Length > 0 ? 44 : 26);
            if (_showChevron) width += ChevronWidth - 12;
            return width;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = BeginPaint(e);

        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        bool active = Enabled && (Hovered || Focused && ShowFocusCues);

        if (IsPrimary && Enabled)
        {
            Theme.DrawGlow(graphics, bounds, Radius, Theme.Accent, 2);
            Color top = Pressed ? Theme.PrimaryBottom : active ? Theme.AccentBright : Theme.PrimaryTop;
            Theme.FillRoundedGradient(graphics, bounds, Radius, top, Theme.PrimaryBottom);
            Theme.DrawRounded(graphics, bounds, Radius, Color.FromArgb(0x5A, 0x95, 0xFF));
        }
        else if (!Borderless || active || Pressed)
        {
            Color face = !Enabled ? Theme.Surface
                : Pressed ? Theme.AccentDim
                : active ? Theme.SurfaceHover
                : Theme.SurfaceRaised;

            Theme.FillRoundedGradient(graphics, bounds, Radius, face, Darken(face));
            Theme.DrawRounded(graphics, bounds, Radius,
                !Enabled ? Theme.Line : active ? Theme.LineStrong : Theme.Line);
        }

        Color ink = Enabled ? Theme.Text : Theme.TextFaint;
        int contentRight = _showChevron ? Width - ChevronWidth + 6 : Width;

        int textWidth = MeasureText(Text, Font);
        int glyphWidth = _glyph is null ? 0 : 20;
        int gap = glyphWidth > 0 && textWidth > 0 ? 10 : 0;
        int total = glyphWidth + gap + textWidth;
        int left = (contentRight - total) / 2;

        if (_glyph is not null)
        {
            Theme.DrawGlyph(graphics, _glyph, Theme.IconFontMedium, ink,
                new RectangleF(left, 0, glyphWidth, Height));
        }

        if (textWidth > 0)
        {
            Theme.DrawText(graphics, Text, Font, ink,
                new Rectangle(left + glyphWidth + gap, 0, textWidth + 4, Height));
        }

        if (_showChevron)
        {
            Theme.DrawGlyph(graphics, Glyphs.ChevronDown, Theme.IconFontSmall,
                Enabled ? Theme.TextMuted : Theme.TextFaint,
                new RectangleF(Width - ChevronWidth, 0, ChevronWidth - 6, Height));
        }
    }

    private static Color Darken(Color color) =>
        Color.FromArgb(color.A, color.R * 85 / 100, color.G * 85 / 100, color.B * 85 / 100);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _downInChevron = _showChevron && e.X >= Width - ChevronWidth;
    }

    protected override void OnClick(EventArgs e)
    {
        if (_downInChevron)
        {
            _downInChevron = false;
            ChevronClicked?.Invoke(this, e);
            return;
        }

        base.OnClick(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode is Keys.Space or Keys.Enter) base.OnClick(EventArgs.Empty);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }
}

/// <summary>An on/off option shown as an icon and a label, lit when on.</summary>
public sealed class ToggleChip : PaintedControl
{
    private bool _checked;
    private bool _showText = true;

    public ToggleChip(string text, string? glyph)
    {
        Text = text;
        Glyph = glyph;
        Font = Theme.UiFont;
        Height = 40;
        Cursor = Cursors.Hand;
    }

    public string? Glyph { get; }

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>When false only the icon is drawn, for narrow windows. The label becomes a tooltip.</summary>
    [DefaultValue(true)]
    public bool ShowText
    {
        get => _showText;
        set
        {
            _showText = value;
            Invalidate();
        }
    }

    public event EventHandler? CheckedChanged;

    public int PreferredWidth =>
        16 + (Glyph is null ? 0 : 20) + (_showText ? MeasureText(Text, Font) + (Glyph is null ? 0 : 7) : 0);

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = BeginPaint(e);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        if (Hovered) Theme.FillRounded(graphics, bounds, 8, Theme.SurfaceRaised);

        Color glyphColor = _checked ? Theme.AccentBright : Theme.TextFaint;
        Color textColor = _checked ? Theme.Text : Theme.TextFaint;

        int x = 8;
        if (Glyph is not null)
        {
            Theme.DrawGlyph(graphics, Glyph, Theme.IconFontMedium, glyphColor, new RectangleF(x, 0, 20, Height));
            x += 27;
        }

        if (_showText) Theme.DrawText(graphics, Text, Font, textColor, new Rectangle(x, 0, Width - x, Height));
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
    }
}

/// <summary>
/// A themed dropdown: icon, current choice and an arrow, opening a themed menu of choices.
/// </summary>
/// <remarks>
/// Replaces <c>ComboBox</c>, which draws its button and list with the light system theme no
/// matter what colours it is given.
/// </remarks>
public sealed class DropDownSelector : PaintedControl
{
    private int _selectedIndex = -1;

    public DropDownSelector(string? glyph)
    {
        Glyph = glyph;
        Font = Theme.UiFont;
        Height = 46;
        Cursor = Cursors.Hand;
    }

    public string? Glyph { get; }

    public List<string> Items { get; } = [];

    public event EventHandler? SelectedIndexChanged;

    [DefaultValue(-1)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = value < 0 || value >= Items.Count ? -1 : value;
            if (clamped == _selectedIndex) return;

            _selectedIndex = clamped;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? SelectedItem
    {
        get => _selectedIndex >= 0 ? Items[_selectedIndex] : null;
        set => SelectedIndex = value is null ? -1 : Items.IndexOf(value);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = BeginPaint(e);
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);

        Color face = Pressed ? Theme.AccentDim : Hovered ? Theme.SurfaceHover : Theme.SurfaceRaised;
        Theme.FillRounded(graphics, bounds, 10, face);
        Theme.DrawRounded(graphics, bounds, 10, Hovered ? Theme.LineStrong : Theme.Line);

        int x = 14;
        if (Glyph is not null)
        {
            Theme.DrawGlyph(graphics, Glyph, Theme.IconFont(14f), Theme.Text, new RectangleF(x, 0, 22, Height));
            x += 34;
        }

        Theme.DrawText(graphics, SelectedItem ?? string.Empty, Font, Theme.Text,
            new Rectangle(x, 0, Math.Max(0, Width - x - 36), Height));

        Theme.DrawGlyph(graphics, Glyphs.ChevronDown, Theme.IconFontSmall, Theme.TextMuted,
            new RectangleF(Width - 34, 0, 22, Height));
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Items.Count == 0) return;

        ContextMenuStrip menu = Menus.Create();
        for (int i = 0; i < Items.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(Items[i]) { Checked = i == _selectedIndex };
            item.Click += (_, _) => SelectedIndex = index;
            menu.Items.Add(item);
        }

        menu.MinimumSize = new Size(Width, 0);
        Menus.ShowBelow(menu, this);
    }
}

/// <summary>Creates context menus that match the shell, instead of the light system menus.</summary>
public static class Menus
{
    public static ContextMenuStrip Create()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new ThemedMenuRenderer(),
            Font = Theme.UiFont,
            ForeColor = Theme.Text,
            BackColor = Theme.SurfaceRaised,
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Padding = new Padding(4),
        };

        menu.ItemAdded += (_, e) => e.Item!.Padding = new Padding(2, 5, 8, 5);

        // A menu is built per click, so it must go once it has closed.
        menu.Closed += (_, _) => menu.BeginInvoke(menu.Dispose);
        return menu;
    }

    public static void ShowBelow(ContextMenuStrip menu, Control anchor) =>
        menu.Show(anchor, new Point(0, anchor.Height + 4));

    public static void ShowAbove(ContextMenuStrip menu, Control anchor) =>
        menu.Show(anchor, new Point(0, -4), ToolStripDropDownDirection.AboveRight);

    private sealed class ThemedMenuRenderer() : ToolStripProfessionalRenderer(new Palette())
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextFaint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.TextMuted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Theme.DrawGlyph(e.Graphics, Glyphs.Check, Theme.IconFontSmall, Theme.AccentBright,
                e.ImageRectangle);
        }
    }

    private sealed class Palette : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.SurfaceRaised;
        public override Color ImageMarginGradientBegin => Theme.SurfaceRaised;
        public override Color ImageMarginGradientMiddle => Theme.SurfaceRaised;
        public override Color ImageMarginGradientEnd => Theme.SurfaceRaised;
        public override Color MenuBorder => Theme.LineStrong;
        public override Color MenuItemBorder => Theme.SurfaceHover;
        public override Color MenuItemSelected => Theme.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => Theme.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => Theme.SurfaceHover;
        public override Color MenuItemPressedGradientBegin => Theme.AccentDim;
        public override Color MenuItemPressedGradientEnd => Theme.AccentDim;
        public override Color SeparatorDark => Theme.Line;
        public override Color SeparatorLight => Theme.SurfaceRaised;
        public override Color CheckBackground => Theme.SurfaceRaised;
        public override Color CheckSelectedBackground => Theme.SurfaceHover;
        public override Color CheckPressedBackground => Theme.SurfaceHover;
    }
}

/// <summary>
/// A rounded panel with a thin edge, the container every region of the window sits in.
/// </summary>
public class CardPanel : Panel
{
    public CardPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Surface;
    }

    [DefaultValue(16)]
    public int Radius { get; set; } = 16;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Theme.Line;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(Parent?.BackColor ?? Theme.Background);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.FillRounded(graphics, bounds, Radius, BackColor);
        Theme.DrawRounded(graphics, bounds, Radius, BorderColor);
    }
}

/// <summary>A search field: a rounded well with a magnifier, around a borderless text box.</summary>
public sealed class SearchBox : PaintedControl
{
    private readonly TextBox _input = new();

    public SearchBox(string placeholder)
    {
        Height = 46;
        Cursor = Cursors.IBeam;

        _input.BorderStyle = BorderStyle.None;
        _input.BackColor = Theme.SurfaceRaised;
        _input.ForeColor = Theme.Text;
        _input.Font = new Font("Segoe UI", 10.5f);
        _input.PlaceholderText = placeholder;
        _input.TextChanged += (_, _) => TextEntered?.Invoke(_input.Text);
        _input.GotFocus += (_, _) => Invalidate();
        _input.LostFocus += (_, _) => Invalidate();

        Controls.Add(_input);
    }

    public event Action<string>? TextEntered;

    public void Clear() => _input.Clear();

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        int height = _input.PreferredHeight;
        _input.SetBounds(46, (Height - height) / 2, Math.Max(10, Width - 46 - 14), height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = BeginPaint(e);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        Theme.FillRounded(graphics, bounds, 12, Theme.SurfaceRaised);
        Theme.DrawRounded(graphics, bounds, 12, _input.Focused ? Theme.Accent : Theme.Line);
        Theme.DrawGlyph(graphics, Glyphs.Search, Theme.IconFont(13f), Theme.TextMuted,
            new RectangleF(12, 0, 24, Height));
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _input.Focus();
    }
}

/// <summary>The app's name and mark, drawn at the left of the header.</summary>
public sealed class LogoControl : PaintedControl
{
    private bool _compact;

    public LogoControl() => Size = new Size(290, 84);

    /// <summary>Draws just the mark, for narrow windows.</summary>
    [DefaultValue(false)]
    public bool Compact
    {
        get => _compact;
        set
        {
            _compact = value;
            Invalidate();
        }
    }

    public int PreferredWidth => _compact ? 72 : 290;

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = BeginPaint(e);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        Theme.DrawLogoMark(graphics, new RectangleF(6, 10, 60, 64));
        if (_compact) return;

        // The word mark is filled as a path so it can take a gradient like the mark beside it.
        using var path = new GraphicsPath();
        float emSize = Theme.LogoFont.SizeInPoints * graphics.DpiY / 72f;
        path.AddString("JMobile", Theme.LogoFont.FontFamily, (int)Theme.LogoFont.Style, emSize,
            new PointF(72, 8), StringFormat.GenericTypographic);

        RectangleF wordBounds = path.GetBounds();
        using var brush = new LinearGradientBrush(
            RectangleF.Inflate(wordBounds, 2, 2),
            Theme.AccentBright, Color.FromArgb(0x2F, 0x7B, 0xF5), LinearGradientMode.Vertical);
        graphics.FillPath(brush, path);

        Theme.DrawText(graphics, "Play J2ME Games Anywhere", Theme.UiFont, Theme.Text,
            new Rectangle(78, 52, Width - 78, 22));
    }
}

/// <summary>A titled header for a card, with an optional collapse arrow.</summary>
public sealed class CardHeader : PaintedControl
{
    private bool _collapsed;

    public CardHeader(string title, string glyph)
    {
        Text = title;
        Glyph = glyph;
        Height = 62;
        Cursor = Cursors.Hand;
    }

    public string Glyph { get; }

    [DefaultValue(false)]
    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value) return;
            _collapsed = value;
            Invalidate();
            CollapsedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CollapsedChanged;

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = BeginPaint(e);

        Theme.DrawGlyph(graphics, Glyph, Theme.IconFont(17f), Theme.Text, new RectangleF(18, 0, 30, Height));
        Theme.DrawText(graphics, Text, Theme.TitleFont, Theme.Text, new Rectangle(60, 0, Width - 110, Height));

        var chevron = new Rectangle(Width - 50, (Height - 34) / 2, 34, 34);
        if (Hovered) Theme.FillRounded(graphics, chevron, 8, Theme.SurfaceHover);
        Theme.DrawGlyph(graphics, _collapsed ? Glyphs.ChevronDown : Glyphs.ChevronUp,
            Theme.IconFontSmall, Theme.Text, chevron);

        if (!_collapsed)
        {
            using var pen = new Pen(Theme.Line);
            graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Collapsed = !Collapsed;
    }
}

/// <summary>A row of icon-and-value readouts, used for the running game's metrics.</summary>
public sealed class MetricsStrip : PaintedControl
{
    private (string Glyph, string Text)[] _items = [];

    public MetricsStrip()
    {
        Font = Theme.UiFont;
        Height = 46;
    }

    /// <summary>Draws a short vertical rule at the left edge, separating the strip from what precedes it.</summary>
    [DefaultValue(false)]
    public bool LeadingDivider { get; set; }

    public void SetItems((string Glyph, string Text)[] items)
    {
        if (items.AsSpan().SequenceEqual(_items)) return;

        _items = items;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = BeginPaint(e);

        int x = 0;
        if (LeadingDivider)
        {
            using var pen = new Pen(Theme.Line);
            graphics.DrawLine(pen, 0, Height / 4, 0, Height * 3 / 4);
            x = 24;
        }

        foreach ((string glyph, string text) in _items)
        {
            int textWidth = MeasureText(text, Font);
            if (x + 28 + textWidth > Width) break;

            Theme.DrawGlyph(graphics, glyph, Theme.IconFont(13f), Theme.TextMuted, new RectangleF(x, 0, 22, Height));
            Theme.DrawText(graphics, text, Font, Theme.TextMuted, new Rectangle(x + 28, 0, textWidth + 4, Height));
            x += 28 + textWidth + 32;
        }
    }
}

/// <summary>The status line's coloured dot and message.</summary>
public sealed class StatusIndicator : PaintedControl
{
    private Color _dotColor = Theme.Success;

    public StatusIndicator()
    {
        Font = Theme.UiFont;
        Height = 32;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color DotColor
    {
        get => _dotColor;
        set
        {
            if (_dotColor == value) return;
            _dotColor = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = BeginPaint(e);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        int cy = Height / 2;
        using (var halo = new SolidBrush(Color.FromArgb(50, _dotColor)))
            graphics.FillEllipse(halo, 0, cy - 9, 18, 18);
        using (var dot = new SolidBrush(_dotColor))
            graphics.FillEllipse(dot, 3, cy - 6, 12, 12);

        Theme.DrawText(graphics, Text, Font, Theme.Text, new Rectangle(28, 0, Width - 28, Height));
    }
}
