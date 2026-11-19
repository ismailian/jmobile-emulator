using System.Drawing.Drawing2D;

namespace JMobile.App;

/// <summary>
/// The application's visual language, in one place.
/// </summary>
/// <remarks>
/// WinForms has no styling system, so every colour and font would otherwise be scattered across
/// controls as literals. Collecting them here means the shell has a coherent look and changing
/// it is one edit rather than fifty.
/// <para>
/// The palette is a deep navy with a single blue accent. Panels are rounded cards with a thin
/// blue edge; the emulated screen sits in the brightest, glowing frame so it stays the subject.
/// </para>
/// </remarks>
public static class Theme
{
    // Surfaces, darkest to lightest.
    public static readonly Color Background = Color.FromArgb(0x08, 0x11, 0x20);
    public static readonly Color Surface = Color.FromArgb(0x0E, 0x1B, 0x30);
    public static readonly Color SurfaceRaised = Color.FromArgb(0x14, 0x24, 0x3E);
    public static readonly Color SurfaceHover = Color.FromArgb(0x1B, 0x30, 0x52);
    public static readonly Color ScreenWell = Color.FromArgb(0x04, 0x09, 0x13);

    public static readonly Color Line = Color.FromArgb(0x1E, 0x33, 0x55);
    public static readonly Color LineStrong = Color.FromArgb(0x2A, 0x4A, 0x7C);

    public static readonly Color Text = Color.FromArgb(0xEC, 0xF1, 0xF9);
    public static readonly Color TextMuted = Color.FromArgb(0x9C, 0xAC, 0xC6);
    public static readonly Color TextFaint = Color.FromArgb(0x5A, 0x6B, 0x88);

    public static readonly Color Accent = Color.FromArgb(0x3B, 0x82, 0xF6);
    public static readonly Color AccentBright = Color.FromArgb(0x6C, 0xA8, 0xFF);
    public static readonly Color AccentDim = Color.FromArgb(0x17, 0x3A, 0x72);
    public static readonly Color PrimaryTop = Color.FromArgb(0x2F, 0x74, 0xF0);
    public static readonly Color PrimaryBottom = Color.FromArgb(0x1C, 0x4E, 0xC8);

    public static readonly Color Success = Color.FromArgb(0x2E, 0xCC, 0x5E);
    public static readonly Color Warning = Color.FromArgb(0xF0, 0xA5, 0x3A);
    public static readonly Color Danger = Color.FromArgb(0xEF, 0x5B, 0x5B);
    public static readonly Color Flame = Color.FromArgb(0xF5, 0x8A, 0x1F);

    // Keypad faces: the d-pad is tinted blue, the number keys are plain navy.
    public static readonly Color DpadTop = Color.FromArgb(0x22, 0x50, 0x95);
    public static readonly Color DpadBottom = Color.FromArgb(0x17, 0x3A, 0x70);
    public static readonly Color DpadEdge = Color.FromArgb(0x33, 0x66, 0xB5);
    public static readonly Color KeyFace = Color.FromArgb(0x13, 0x24, 0x40);
    public static readonly Color KeyFaceHot = Color.FromArgb(0x1C, 0x33, 0x58);
    public static readonly Color KeyEdge = Color.FromArgb(0x25, 0x41, 0x6C);
    public static readonly Color KeyFaceDown = Color.FromArgb(0x2B, 0x6B, 0xD8);

    private const string UiFamily = "Segoe UI";

    public static Font UiFont { get; } = new(UiFamily, 10f, FontStyle.Regular, GraphicsUnit.Point);

    public static Font UiFontBold { get; } = new("Segoe UI Semibold", 10f, FontStyle.Regular, GraphicsUnit.Point);

    public static Font ButtonFont { get; } = new("Segoe UI Semibold", 10.5f, FontStyle.Regular, GraphicsUnit.Point);

    public static Font TitleFont { get; } = new("Segoe UI Semibold", 12f, FontStyle.Regular, GraphicsUnit.Point);

    public static Font LogoFont { get; } = new(UiFamily, 25f, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Point);

    public static Font SmallFont { get; } = new(UiFamily, 9f, FontStyle.Regular, GraphicsUnit.Point);

    public static Font MonoFont { get; } = new("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point);

    public static Font KeyFont { get; } = new("Segoe UI Semibold", 17f, FontStyle.Regular, GraphicsUnit.Point);

    public static Font KeyFontSmall { get; } = new("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Point);

    /// <summary>The system icon font, present on every Windows 10 and 11 install.</summary>
    public static Font IconFont(float size) => new("Segoe MDL2 Assets", size, FontStyle.Regular, GraphicsUnit.Point);

    public static Font IconFontMedium { get; } = IconFont(12f);

    public static Font IconFontSmall { get; } = IconFont(10f);

    /// <summary>Builds a rounded-rectangle path, used for cards, keys and buttons.</summary>
    public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>Fills a rounded rectangle, the shape most of this UI is made of.</summary>
    public static void FillRounded(Graphics graphics, Rectangle bounds, int radius, Color color)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using GraphicsPath path = RoundedRectangle(bounds, radius);
        using var brush = new SolidBrush(color);

        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.FillPath(brush, path);
        graphics.SmoothingMode = previous;
    }

    /// <summary>Fills a rounded rectangle with a top-to-bottom gradient.</summary>
    public static void FillRoundedGradient(Graphics graphics, Rectangle bounds, int radius, Color top, Color bottom)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using GraphicsPath path = RoundedRectangle(bounds, radius);
        using var brush = new LinearGradientBrush(
            new Rectangle(bounds.X, bounds.Y - 1, bounds.Width, bounds.Height + 2),
            top, bottom, LinearGradientMode.Vertical);

        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.FillPath(brush, path);
        graphics.SmoothingMode = previous;
    }

    public static void DrawRounded(Graphics graphics, Rectangle bounds, int radius, Color color, float width = 1f)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using GraphicsPath path = RoundedRectangle(bounds, radius);
        using var pen = new Pen(color, width);

        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawPath(pen, path);
        graphics.SmoothingMode = previous;
    }

    /// <summary>
    /// Draws a soft outer glow around a rounded rectangle, by stacking faint outlines that fade
    /// as they move outwards. GDI+ has no blur, and this is indistinguishable at these sizes.
    /// </summary>
    public static void DrawGlow(Graphics graphics, Rectangle bounds, int radius, Color color, int spread)
    {
        for (int i = spread; i >= 1; i--)
        {
            double t = 1.0 - ((i - 1) / (double)spread);
            var alpha = (int)(60 * t * t);
            DrawRounded(graphics, Rectangle.Inflate(bounds, i, i), radius + i,
                Color.FromArgb(Math.Clamp(alpha, 0, 255), color), 1.2f);
        }
    }

    /// <summary>Draws an icon-font glyph centred in a rectangle.</summary>
    public static void DrawGlyph(Graphics graphics, string glyph, Font font, Color color, RectangleF bounds)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        graphics.DrawString(glyph, font, brush, bounds, format);
    }

    /// <summary>Draws text vertically centred in a rectangle, left aligned and trimmed.</summary>
    public static void DrawText(Graphics graphics, string text, Font font, Color color, Rectangle bounds,
        TextFormatFlags alignment = TextFormatFlags.Left)
    {
        TextRenderer.DrawText(graphics, text, font, bounds, color,
            alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    /// <summary>
    /// Draws the JMobile mark: a steaming cup, in the accent blue with an orange flame of steam.
    /// </summary>
    /// <param name="graphics">Target surface.</param>
    /// <param name="bounds">Box to draw the mark into.</param>
    /// <param name="tint">When set, the whole mark is drawn in this one colour, for empty states.</param>
    public static void DrawLogoMark(Graphics graphics, RectangleF bounds, Color? tint = null)
    {
        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        float w = bounds.Width;
        float h = bounds.Height;
        float x = bounds.X;
        float y = bounds.Y;
        float stroke = Math.Max(1.4f, w / 16f);

        // Steam: two flame-like strokes rising from the cup.
        using (var flame = new Pen(tint ?? Flame, stroke * 1.15f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawBezier(flame,
                x + (w * 0.50f), y + (h * 0.50f),
                x + (w * 0.30f), y + (h * 0.36f),
                x + (w * 0.66f), y + (h * 0.22f),
                x + (w * 0.50f), y + (h * 0.02f));
            graphics.DrawBezier(flame,
                x + (w * 0.62f), y + (h * 0.48f),
                x + (w * 0.52f), y + (h * 0.38f),
                x + (w * 0.74f), y + (h * 0.28f),
                x + (w * 0.66f), y + (h * 0.16f));
        }

        using var cup = new LinearGradientBrush(
            new RectangleF(x, y + (h * 0.5f), w, h * 0.5f),
            tint ?? AccentBright, tint ?? Color.FromArgb(0x1F, 0x5F, 0xD6), LinearGradientMode.Vertical);
        using var pen = new Pen(cup, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        // Rim, bowl, handle and saucer as open strokes, which reads as a mark rather than clip art.
        graphics.DrawArc(pen, x + (w * 0.18f), y + (h * 0.52f), w * 0.60f, h * 0.12f, 0, 360);
        graphics.DrawArc(pen, x + (w * 0.18f), y + (h * 0.48f), w * 0.60f, h * 0.36f, 0, 180);
        graphics.DrawArc(pen, x + (w * 0.70f), y + (h * 0.60f), w * 0.20f, h * 0.14f, -90, 200);

        using var saucer = new Pen(cup, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(saucer, x + (w * 0.06f), y + (h * 0.80f), w * 0.86f, h * 0.16f, 15, 150);

        graphics.SmoothingMode = previous;
    }

    /// <summary>Renders the logo mark to a window icon, so the title bar and taskbar match the app.</summary>
    public static Icon CreateAppIcon()
    {
        using var bitmap = new Bitmap(64, 64);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            DrawLogoMark(graphics, new RectangleF(4, 2, 56, 60));
        }

        IntPtr handle = bitmap.GetHicon();
        using Icon temporary = Icon.FromHandle(handle);
        var icon = (Icon)temporary.Clone();
        DestroyIcon(handle);
        return icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Applies the theme to a plain WinForms control tree.</summary>
    public static void Apply(Control control)
    {
        control.BackColor = Background;
        control.ForeColor = Text;
        control.Font = UiFont;
    }
}

/// <summary>Glyphs from Segoe MDL2 Assets used across the shell.</summary>
public static class Glyphs
{
    public const string Add = "";
    public const string Play = "";
    public const string Pause = "";
    public const string Stop = "";
    public const string Phone = "";
    public const string Fit = "";
    public const string Settings = "";
    public const string Search = "";
    public const string Filter = "";
    public const string Delete = "";
    public const string ChevronRight = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";
    public const string Keyboard = "";
    public const string Keypad = "";
    public const string Volume = "";
    public const string Globe = "";
    public const string Info = "";
    public const string Clock = "";
    public const string Speed = "";
    public const string Package = "";
    public const string Memory = "";
    public const string SignOut = "";
    public const string Palette = "";
    public const string List = "";
    public const string Check = "";
}
