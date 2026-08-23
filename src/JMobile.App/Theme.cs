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
/// The palette is deliberately dark and low-contrast around the edges: the emulated screen is
/// the brightest thing in the window, and everything around it should recede so a 176x208 phone
/// display reads as the subject rather than competing with the chrome.
/// </para>
/// </remarks>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(0x12, 0x15, 0x19);
    public static readonly Color Surface = Color.FromArgb(0x1A, 0x1F, 0x25);
    public static readonly Color SurfaceRaised = Color.FromArgb(0x22, 0x28, 0x30);
    public static readonly Color Line = Color.FromArgb(0x2C, 0x34, 0x3D);

    public static readonly Color Text = Color.FromArgb(0xE6, 0xEA, 0xEF);
    public static readonly Color TextMuted = Color.FromArgb(0x93, 0x9E, 0xAB);
    public static readonly Color TextFaint = Color.FromArgb(0x62, 0x6C, 0x78);

    public static readonly Color Accent = Color.FromArgb(0x3F, 0xB9, 0xC2);
    public static readonly Color AccentDim = Color.FromArgb(0x1C, 0x4E, 0x54);
    public static readonly Color Warning = Color.FromArgb(0xDC, 0x9A, 0x3E);
    public static readonly Color Danger = Color.FromArgb(0xD9, 0x5F, 0x5F);

    /// <summary>The handset body, drawn around the emulated screen.</summary>
    public static readonly Color ShellBody = Color.FromArgb(0x27, 0x2D, 0x35);

    public static readonly Color ShellBezel = Color.FromArgb(0x15, 0x19, 0x1E);

    public static readonly Color KeyFace = Color.FromArgb(0x30, 0x38, 0x42);
    public static readonly Color KeyFaceHot = Color.FromArgb(0x3C, 0x46, 0x52);
    public static readonly Color KeyFaceDown = Color.FromArgb(0x2A, 0x63, 0x6A);

    public static Font UiFont { get; } = new("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

    public static Font UiFontBold { get; } = new("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);

    public static Font TitleFont { get; } = new("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Point);

    public static Font SmallFont { get; } = new("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);

    public static Font MonoFont { get; } = new("Consolas", 8.5f, FontStyle.Regular, GraphicsUnit.Point);

    public static Font KeyFont { get; } = new("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);

    /// <summary>Builds a rounded-rectangle path, used for cards, keys and the handset body.</summary>
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

    /// <summary>Applies the theme to a plain WinForms control tree.</summary>
    public static void Apply(Control control)
    {
        control.BackColor = Background;
        control.ForeColor = Text;
        control.Font = UiFont;
    }
}
