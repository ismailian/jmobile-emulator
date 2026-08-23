using System.ComponentModel;
using JMobile.Emulator.Session;

namespace JMobile.App.Controls;

/// <summary>
/// A flat button drawn to match the rest of the shell.
/// </summary>
/// <remarks>
/// The stock button ignores <c>BackColor</c> under visual styles and renders a light chrome
/// button regardless of the theme, so it is painted here instead.
/// </remarks>
public sealed class FlatButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public FlatButton(string text)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        Text = text;
        Size = new Size(96, 30);
        Cursor = Cursors.Hand;
        BackColor = Theme.SurfaceRaised;
    }

    /// <summary>Draws the button in the accent colour, for the primary action on a screen.</summary>
    [DefaultValue(false)]
    public bool IsPrimary { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(Parent?.BackColor ?? Theme.Background);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        Color face = !Enabled ? Theme.Surface
            : _pressed ? Theme.AccentDim
            : IsPrimary ? (_hovered ? Theme.Accent : Theme.AccentDim)
            : _hovered ? Theme.KeyFaceHot
            : Theme.SurfaceRaised;

        Color border = !Enabled ? Theme.Line
            : IsPrimary || _hovered ? Theme.Accent
            : Theme.Line;

        Color text = !Enabled ? Theme.TextFaint
            : IsPrimary && _hovered ? Color.FromArgb(0x10, 0x15, 0x19)
            : Theme.Text;

        Theme.FillRounded(graphics, bounds, 5, face);
        Theme.DrawRounded(graphics, bounds, 5, border);

        using var brush = new SolidBrush(text);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        graphics.DrawString(Text, Theme.UiFont, brush, bounds, format);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }
}

/// <summary>
/// The soft-key strip beneath the screen.
/// </summary>
/// <remarks>
/// On a handset these two keys were labelled by whatever the running application had put there,
/// and MIDlets rely on that: a game attaches an "Exit" or "Menu" command and expects the player
/// to find it. Showing the labels is what makes those commands discoverable here too.
/// </remarks>
public sealed class SoftKeyBar : Control
{
    private IReadOnlyList<SoftCommand> _commands = [];
    private int _hovered = -1;

    public SoftKeyBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        Height = 30;
        BackColor = Theme.Background;
    }

    /// <summary>Raised when the user activates one of the visible commands.</summary>
    public event Action<SoftCommand>? CommandInvoked;

    public void SetCommands(IReadOnlyList<SoftCommand> commands)
    {
        _commands = commands;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(Theme.Background);

        if (_commands.Count == 0) return;

        // Left key gets the first command, right key the last; anything between is reachable
        // from the menu button in the toolbar.
        DrawKey(graphics, 0, LeftBounds, "Q");
        if (_commands.Count > 1) DrawKey(graphics, _commands.Count - 1, RightBounds, "E");
    }

    private Rectangle LeftBounds => new(6, 3, Math.Min(150, (Width / 2) - 10), Height - 6);

    private Rectangle RightBounds
    {
        get
        {
            int width = Math.Min(150, (Width / 2) - 10);
            return new Rectangle(Width - width - 6, 3, width, Height - 6);
        }
    }

    private void DrawKey(Graphics graphics, int index, Rectangle bounds, string hint)
    {
        bool hovered = _hovered == index;

        Theme.FillRounded(graphics, bounds, 4, hovered ? Theme.KeyFaceHot : Theme.SurfaceRaised);
        Theme.DrawRounded(graphics, bounds, 4, hovered ? Theme.Accent : Theme.Line);

        using var textBrush = new SolidBrush(Theme.Text);
        using var hintBrush = new SolidBrush(Theme.TextFaint);
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
        };

        var textBounds = new Rectangle(bounds.X + 20, bounds.Y, bounds.Width - 24, bounds.Height);
        graphics.DrawString(_commands[index].Label, Theme.UiFont, textBrush, textBounds, format);

        graphics.DrawString(hint, Theme.SmallFont, hintBrush,
            new Rectangle(bounds.X + 4, bounds.Y, 16, bounds.Height),
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
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

        if (_hovered < 0) return;
        _hovered = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        int hit = HitTest(e.Location);
        if (hit >= 0) CommandInvoked?.Invoke(_commands[hit]);
    }

    private int HitTest(Point point)
    {
        if (_commands.Count == 0) return -1;
        if (LeftBounds.Contains(point)) return 0;
        if (_commands.Count > 1 && RightBounds.Contains(point)) return _commands.Count - 1;
        return -1;
    }
}

/// <summary>A text box themed to match the shell, with placeholder text.</summary>
public sealed class SearchBox : Panel
{
    private readonly TextBox _input = new();

    public SearchBox(string placeholder)
    {
        BackColor = Theme.SurfaceRaised;
        Padding = new Padding(9, 6, 9, 6);
        Height = 30;

        _input.BorderStyle = BorderStyle.None;
        _input.BackColor = Theme.SurfaceRaised;
        _input.ForeColor = Theme.Text;
        _input.Font = Theme.UiFont;
        _input.Dock = DockStyle.Fill;
        _input.PlaceholderText = placeholder;
        _input.TextChanged += (_, _) => TextEntered?.Invoke(_input.Text);

        Controls.Add(_input);
    }

    public event Action<string>? TextEntered;

    public void Clear() => _input.Clear();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Theme.DrawRounded(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), 4, Theme.Line);
    }
}
