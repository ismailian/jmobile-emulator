using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using JMobile.Emulator.Library;
using JMobile.Runtime.Graphics;

namespace JMobile.App.Controls;

/// <summary>
/// Presents the emulated phone screen.
/// </summary>
/// <remarks>
/// Presenting a frame copies nothing. The runtime's pixel buffer is a plain <c>int[]</c>, which
/// is pinned once and wrapped in a GDI+ bitmap that points straight at it, so the rasteriser
/// writes into exactly the memory the window blits from. All that remains per frame is the
/// scaled blit itself.
/// <para>
/// Scaling uses nearest-neighbour interpolation. These are pixel-art games designed for screens
/// a couple of hundred pixels wide; smoothing them is not a neutral choice, it destroys the art.
/// </para>
/// </remarks>
public sealed class PhoneScreenControl : Control
{
    private Surface? _surface;
    private Bitmap? _bitmap;
    private GCHandle _pinned;
    private volatile bool _dirty;

    public PhoneScreenControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        BackColor = Theme.Background;
        TabStop = true;
    }

    /// <summary>How the phone screen is fitted into the available area.</summary>
    [DefaultValue(ScalingMode.IntegerFit)]
    public ScalingMode Scaling { get; set; } = ScalingMode.IntegerFit;

    /// <summary>Draw the handset body and bezel around the screen.</summary>
    [DefaultValue(true)]
    public bool ShowSkin { get; set; } = true;

    /// <summary>The rectangle the screen currently occupies, for hit-testing pointer events.</summary>
    public Rectangle ScreenBounds { get; private set; }

    /// <summary>Points this control at a runtime surface, pinning it for the blit path.</summary>
    public void Attach(Surface? surface)
    {
        if (ReferenceEquals(_surface, surface)) return;

        Release();
        _surface = surface;

        if (surface is not null)
        {
            // Pinned for as long as the surface is attached. The array is large and long-lived,
            // so it is already in the large object heap and pinning costs the collector nothing.
            _pinned = GCHandle.Alloc(surface.Pixels, GCHandleType.Pinned);
            _bitmap = new Bitmap(
                surface.Width,
                surface.Height,
                surface.Width * 4,
                PixelFormat.Format32bppRgb,
                _pinned.AddrOfPinnedObject());
        }

        Invalidate();
    }

    /// <summary>
    /// Marks the screen as changed. Safe to call from the emulator thread.
    /// </summary>
    /// <remarks>
    /// This only sets a flag; the UI thread repaints on its own timer. Marshalling an invalidate
    /// per frame would flood the message queue whenever a game outruns the display, and the
    /// extra repaints would all be discarded anyway.
    /// </remarks>
    public void MarkDirty() => _dirty = true;

    /// <summary>Repaints if a new frame has arrived since the last one. Call from the UI thread.</summary>
    public void PresentIfDirty()
    {
        if (!_dirty) return;

        _dirty = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(Theme.Background);

        if (_surface is null || _bitmap is null)
        {
            DrawPlaceholder(graphics);
            return;
        }

        Rectangle target = ComputeScreenBounds(_surface.Width, _surface.Height);
        ScreenBounds = target;

        if (ShowSkin) DrawSkin(graphics, target);

        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.SmoothingMode = SmoothingMode.None;

        // The bitmap wraps the live buffer, so this is the only copy the frame ever gets.
        lock (_bitmap)
        {
            graphics.DrawImage(_bitmap, target);
        }
    }

    private Rectangle ComputeScreenBounds(int width, int height)
    {
        int margin = ShowSkin ? 18 : 4;
        int availableWidth = Math.Max(1, ClientSize.Width - (margin * 2));
        int availableHeight = Math.Max(1, ClientSize.Height - (margin * 2));

        double scale = Scaling switch
        {
            ScalingMode.Original => 1.0,
            ScalingMode.Stretch => Math.Min((double)availableWidth / width, (double)availableHeight / height),
            // Whole multiples only, so every source pixel becomes the same number of screen
            // pixels and the art stays even.
            _ => Math.Max(1, (int)Math.Min((double)availableWidth / width, (double)availableHeight / height)),
        };

        var scaledWidth = (int)Math.Round(width * scale);
        var scaledHeight = (int)Math.Round(height * scale);

        return new Rectangle(
            (ClientSize.Width - scaledWidth) / 2,
            (ClientSize.Height - scaledHeight) / 2,
            Math.Max(1, scaledWidth),
            Math.Max(1, scaledHeight));
    }

    private static void DrawSkin(Graphics graphics, Rectangle screen)
    {
        var body = Rectangle.Inflate(screen, 14, 14);
        Theme.FillRounded(graphics, body, 10, Theme.ShellBody);

        var bezel = Rectangle.Inflate(screen, 3, 3);
        Theme.FillRounded(graphics, bezel, 3, Theme.ShellBezel);

        // A single highlight along the top edge reads as moulded plastic without being fussy.
        using var highlight = new LinearGradientBrush(
            new Rectangle(body.X, body.Y, body.Width, 3),
            Color.FromArgb(60, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            LinearGradientMode.Vertical);

        graphics.FillRectangle(highlight, body.X + 8, body.Y + 1, body.Width - 16, 2);
    }

    private void DrawPlaceholder(Graphics graphics)
    {
        var bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);

        using var brush = new SolidBrush(Theme.TextFaint);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        graphics.DrawString("No game running", Theme.UiFont, brush, bounds, format);
    }

    private void Release()
    {
        _bitmap?.Dispose();
        _bitmap = null;

        if (_pinned.IsAllocated) _pinned.Free();
        _surface = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Release();
        base.Dispose(disposing);
    }

    /// <summary>Converts a control-relative point into phone screen coordinates.</summary>
    public Point? ToScreenPoint(Point clientPoint)
    {
        if (_surface is null || ScreenBounds.Width == 0 || ScreenBounds.Height == 0) return null;
        if (!ScreenBounds.Contains(clientPoint)) return null;

        int x = (clientPoint.X - ScreenBounds.X) * _surface.Width / ScreenBounds.Width;
        int y = (clientPoint.Y - ScreenBounds.Y) * _surface.Height / ScreenBounds.Height;

        return new Point(
            Math.Clamp(x, 0, _surface.Width - 1),
            Math.Clamp(y, 0, _surface.Height - 1));
    }

    /// <summary>Arrow keys must reach the game, so the control claims them from the dialog logic.</summary>
    protected override bool IsInputKey(Keys keyData) => true;
}
