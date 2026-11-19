using JMobile.Runtime.Host;

namespace JMobile.App.Controls;

/// <summary>
/// The on-screen handset keypad.
/// </summary>
/// <remarks>
/// Games hard-code key codes constantly, and a player who does not know that <c>7</c> is the
/// weapon-switch key has no way to discover it from a keyboard. A visible keypad makes every
/// input the game can receive reachable and labelled, which is the difference between a game
/// being playable and merely running.
/// </remarks>
public sealed class KeypadControl : Control
{
    /// <summary>One key on the pad.</summary>
    private sealed record Key(int Code, string Label, int Row, int Column, int Span = 1, bool Wide = false);

    private static readonly Key[] KeyLayout =
    [
        new(KeyCodes.Up, "▲", 0, 1),
        new(KeyCodes.Left, "◀", 1, 0),
        new(KeyCodes.Fire, "OK", 1, 1),
        new(KeyCodes.Right, "▶", 1, 2),
        new(KeyCodes.Down, "▼", 2, 1),

        new(KeyCodes.Num1, "1", 3, 0),
        new(KeyCodes.Num2, "2", 3, 1),
        new(KeyCodes.Num3, "3", 3, 2),
        new(KeyCodes.Num4, "4", 4, 0),
        new(KeyCodes.Num5, "5", 4, 1),
        new(KeyCodes.Num6, "6", 4, 2),
        new(KeyCodes.Num7, "7", 5, 0),
        new(KeyCodes.Num8, "8", 5, 1),
        new(KeyCodes.Num9, "9", 5, 2),
        new(KeyCodes.Star, "*", 6, 0),
        new(KeyCodes.Num0, "0", 6, 1),
        new(KeyCodes.Pound, "#", 6, 2),
    ];

    // The soft keys are not on the pad: the bar beneath the screen already shows them, with the
    // labels the running MIDlet actually gave them, which is far more use than "Soft 1".
    private const int Columns = 3;
    private const int Rows = 7;

    private readonly Dictionary<int, Rectangle> _bounds = [];
    private readonly HashSet<int> _held = [];
    private int _hovered;
    private int _pressed;

    public KeypadControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Surface;
        MinimumSize = new Size(160, 200);
        Cursor = Cursors.Hand;
    }

    /// <summary>Raised when a key goes down, with the handset key code.</summary>
    public event Action<int>? KeyPressedDown;

    /// <summary>Raised when a key comes up.</summary>
    public event Action<int>? KeyReleasedUp;

    /// <summary>Shows a key as held because the keyboard is holding it.</summary>
    public void SetHeld(int code, bool held)
    {
        bool changed = held ? _held.Add(code) : _held.Remove(code);
        if (changed) Invalidate();
    }

    /// <summary>Clears every held indicator, used when a game is paused or closed.</summary>
    public void ClearHeld()
    {
        if (_held.Count == 0) return;

        _held.Clear();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayOut();
    }

    private void LayOut()
    {
        _bounds.Clear();
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

        // The d-pad and the number grid are separate clusters, as on a handset, with a wider gap
        // between them than between keys. Keys keep a sensible aspect rather than stretching to
        // fill a tall window, and the whole pad is centred in whatever space is left.
        const int padding = 18;
        int width = ClientSize.Width - (padding * 2);
        int height = ClientSize.Height - (padding * 2);

        int gap = Math.Clamp(width / 22, 6, 14);
        int sectionGap = gap * 2;
        int cellWidth = (width - (gap * (Columns - 1))) / Columns;

        // Seven rows: three d-pad rows a touch taller than the four number rows.
        int numberHeight = (height - sectionGap - (gap * 5)) * 100 / ((3 * 108) + (4 * 100));
        numberHeight = Math.Min(numberHeight, cellWidth * 82 / 100);
        int dpadHeight = numberHeight * 108 / 100;
        if (cellWidth <= 0 || numberHeight <= 4) return;

        int total = (3 * dpadHeight) + (4 * numberHeight) + (gap * 5) + sectionGap;
        int top = padding + Math.Max(0, (height - total) / 2);

        foreach (Key key in KeyLayout)
        {
            int x = padding + (key.Column * (cellWidth + gap));
            int keyWidth = (cellWidth * key.Span) + (gap * (key.Span - 1));

            Rectangle bounds;
            if (key.Row < 3)
            {
                bounds = new Rectangle(x, top + (key.Row * (dpadHeight + gap)), keyWidth, dpadHeight);
            }
            else
            {
                int numberTop = top + (3 * dpadHeight) + (2 * gap) + sectionGap;
                bounds = new Rectangle(x, numberTop + ((key.Row - 3) * (numberHeight + gap)), keyWidth, numberHeight);
            }

            _bounds[key.Code] = bounds;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_bounds.Count == 0) LayOut();

        Graphics graphics = e.Graphics;
        graphics.Clear(BackColor);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        foreach (Key key in KeyLayout)
        {
            if (!_bounds.TryGetValue(key.Code, out Rectangle bounds)) continue;

            bool down = _pressed == key.Code || _held.Contains(key.Code);
            bool hot = _hovered == key.Code;
            bool direction = key.Row < 3 && key.Code != KeyCodes.Fire;
            const int radius = 12;

            if (down)
            {
                Theme.DrawGlow(graphics, bounds, radius, Theme.Accent, 3);
                Theme.FillRoundedGradient(graphics, bounds, radius, Theme.KeyFaceDown, Theme.DpadBottom);
                Theme.DrawRounded(graphics, bounds, radius, Theme.AccentBright, 1.5f);
            }
            else if (key.Row < 3)
            {
                // The d-pad is tinted so it reads as one control; OK sits a shade darker inside it.
                Color top = direction ? Theme.DpadTop : Color.FromArgb(0x1A, 0x36, 0x63);
                Color bottom = direction ? Theme.DpadBottom : Color.FromArgb(0x12, 0x27, 0x49);
                if (hot) top = Theme.KeyFaceDown;

                Theme.FillRoundedGradient(graphics, bounds, radius, top, bottom);
                Theme.DrawRounded(graphics, bounds, radius, hot ? Theme.AccentBright : Theme.DpadEdge);
            }
            else
            {
                Theme.FillRoundedGradient(graphics, bounds, radius,
                    hot ? Theme.KeyFaceHot : Color.FromArgb(0x16, 0x29, 0x48), Theme.KeyFace);
                Theme.DrawRounded(graphics, bounds, radius, hot ? Theme.LineStrong : Theme.KeyEdge);
            }

            if (direction)
            {
                DrawArrow(graphics, bounds, key.Code);
            }
            else
            {
                Font font = key.Label.Length > 1 ? Theme.KeyFontSmall : Theme.KeyFont;
                Theme.DrawText(graphics, key.Label, font, Theme.Text, bounds, TextFormatFlags.HorizontalCenter);
            }
        }
    }

    /// <summary>Draws a solid triangle; font arrows render at inconsistent sizes and baselines.</summary>
    private static void DrawArrow(Graphics graphics, Rectangle bounds, int code)
    {
        float size = Math.Min(bounds.Width, bounds.Height) * 0.13f;
        float cx = bounds.X + (bounds.Width / 2f);
        float cy = bounds.Y + (bounds.Height / 2f);

        PointF[] points = code switch
        {
            KeyCodes.Up => [new(cx, cy - size), new(cx + (size * 1.15f), cy + (size * 0.8f)), new(cx - (size * 1.15f), cy + (size * 0.8f))],
            KeyCodes.Down => [new(cx, cy + size), new(cx - (size * 1.15f), cy - (size * 0.8f)), new(cx + (size * 1.15f), cy - (size * 0.8f))],
            KeyCodes.Left => [new(cx - size, cy), new(cx + (size * 0.8f), cy - (size * 1.15f)), new(cx + (size * 0.8f), cy + (size * 1.15f))],
            _ => [new(cx + size, cy), new(cx - (size * 0.8f), cy + (size * 1.15f)), new(cx - (size * 0.8f), cy - (size * 1.15f))],
        };

        System.Drawing.Drawing2D.SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Theme.Text);
        graphics.FillPolygon(brush, points);
        graphics.SmoothingMode = previous;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        int hit = HitTest(e.Location);
        if (hit == _hovered) return;

        _hovered = hit;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hovered == 0) return;
        _hovered = 0;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        int hit = HitTest(e.Location);
        if (hit == 0) return;

        _pressed = hit;
        Invalidate();
        KeyPressedDown?.Invoke(hit);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_pressed == 0) return;

        int released = _pressed;
        _pressed = 0;
        Invalidate();
        KeyReleasedUp?.Invoke(released);
    }

    private int HitTest(Point point)
    {
        foreach (KeyValuePair<int, Rectangle> entry in _bounds)
        {
            if (entry.Value.Contains(point)) return entry.Key;
        }
        return 0;
    }
}
