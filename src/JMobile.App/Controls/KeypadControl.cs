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

        BackColor = Theme.Background;
        MinimumSize = new Size(160, 200);
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

        const int gap = 4;
        int cellWidth = (ClientSize.Width - (gap * (Columns + 1))) / Columns;
        int cellHeight = (ClientSize.Height - (gap * (Rows + 1))) / Rows;
        if (cellWidth <= 0 || cellHeight <= 0) return;

        foreach (Key key in KeyLayout)
        {
            int x = gap + (key.Column * (cellWidth + gap));
            int y = gap + (key.Row * (cellHeight + gap));
            int width = (cellWidth * key.Span) + (gap * (key.Span - 1));

            _bounds[key.Code] = new Rectangle(x, y, width, cellHeight);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_bounds.Count == 0) LayOut();

        Graphics graphics = e.Graphics;
        graphics.Clear(Theme.Background);

        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        foreach (Key key in KeyLayout)
        {
            if (!_bounds.TryGetValue(key.Code, out Rectangle bounds)) continue;

            bool down = _pressed == key.Code || _held.Contains(key.Code);
            Color face = down ? Theme.KeyFaceDown
                : _hovered == key.Code ? Theme.KeyFaceHot
                : Theme.KeyFace;

            Theme.FillRounded(graphics, bounds, 5, face);
            Theme.DrawRounded(graphics, bounds, 5, down ? Theme.Accent : Theme.Line);

            using var brush = new SolidBrush(down ? Color.White : Theme.Text);
            Font font = key.Label.Length > 2 ? Theme.SmallFont : Theme.KeyFont;
            graphics.DrawString(key.Label, font, brush, bounds, format);
        }
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
