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
    private bool _flashing;

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
        AllowDrop = true;
    }

    /// <summary>How the phone screen is fitted into the available area.</summary>
    [DefaultValue(ScalingMode.IntegerFit)]
    public ScalingMode Scaling { get; set; } = ScalingMode.IntegerFit;

    /// <summary>Draw the handset body and bezel around the screen.</summary>
    [DefaultValue(true)]
    public bool ShowSkin { get; set; } = true;

    /// <summary>
    /// Briefly lights the frame, which is how vibration is shown: a desktop cannot shake.
    /// </summary>
    public void Flash()
    {
        _flashing = true;
        Invalidate();

        var timer = new System.Windows.Forms.Timer { Interval = 140 };
        timer.Tick += (_, _) =>
        {
            _flashing = false;
            Invalidate();
            timer.Stop();
            timer.Dispose();
        };
        timer.Start();
    }

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
        graphics.Clear(Parent?.BackColor ?? Theme.Background);

        if (_surface is null || _bitmap is null)
        {
            DrawPlaceholder(graphics);
            return;
        }

        Rectangle target = ComputeScreenBounds(_surface.Width, _surface.Height);
        ScreenBounds = target;

        if (ShowSkin) DrawFrame(graphics, Rectangle.Inflate(target, FrameInset, FrameInset));

        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.SmoothingMode = SmoothingMode.None;

        // The bitmap wraps the live buffer, so this is the only copy the frame ever gets.
        lock (_bitmap)
        {
            graphics.DrawImage(_bitmap, target);
        }

        graphics.PixelOffsetMode = PixelOffsetMode.Default;
    }

    // Space between the screen and the edge of its glowing frame, and between the frame and the control.
    private const int FrameInset = 12;
    private const int FrameMargin = 6;

    private Rectangle ComputeScreenBounds(int width, int height)
    {
        int margin = ShowSkin ? FrameInset + FrameMargin : 2;
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

    /// <summary>The glowing frame the screen sits in: a soft halo, a dark well and a bright edge.</summary>
    private void DrawFrame(Graphics graphics, Rectangle frame)
    {
        Color edge = _flashing ? Theme.Warning : Color.FromArgb(0x3A, 0x72, 0xD0);

        Theme.DrawGlow(graphics, frame, 18, _flashing ? Theme.Warning : Theme.Accent, FrameMargin - 1);
        Theme.FillRoundedGradient(graphics, frame, 18, Color.FromArgb(0x10, 0x20, 0x3C), Color.FromArgb(0x0A, 0x16, 0x2C));
        Theme.DrawRounded(graphics, frame, 18, edge, 1.6f);

        var well = Rectangle.Inflate(frame, -(FrameInset - 3), -(FrameInset - 3));
        Theme.FillRounded(graphics, well, 6, Theme.ScreenWell);
        Theme.DrawRounded(graphics, well, 6, Color.FromArgb(0x1C, 0x34, 0x5E));
    }

    private void DrawPlaceholder(Graphics graphics)
    {
        var frame = new Rectangle(FrameMargin, FrameMargin,
            ClientSize.Width - (FrameMargin * 2) - 1, ClientSize.Height - (FrameMargin * 2) - 1);
        if (frame.Width <= 40 || frame.Height <= 40) return;

        DrawFrame(graphics, frame);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        int cx = frame.X + (frame.Width / 2);
        int top = frame.Y + (frame.Height / 2) - 90;

        Theme.DrawLogoMark(graphics, new RectangleF(cx - 40, top, 80, 86), Theme.LineStrong);
        Theme.DrawText(graphics, "No game running", Theme.TitleFont, Theme.TextMuted,
            new Rectangle(frame.X, top + 104, frame.Width, 28), TextFormatFlags.HorizontalCenter);
        Theme.DrawText(graphics, "Pick a game from the library and press Play, or drop a .jar here.",
            Theme.UiFont, Theme.TextFaint,
            new Rectangle(frame.X + 20, top + 136, frame.Width - 40, 24), TextFormatFlags.HorizontalCenter);
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
