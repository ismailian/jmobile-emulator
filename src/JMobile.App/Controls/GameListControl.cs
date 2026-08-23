using System.Drawing.Drawing2D;
using JMobile.Emulator.Library;

namespace JMobile.App.Controls;

/// <summary>One row in the library, pairing a catalogue entry with its decoded icon.</summary>
public sealed class GameListItem(LibraryEntry entry)
{
    public LibraryEntry Entry { get; } = entry;

    /// <summary>Suite icon, decoded once when the row is built.</summary>
    public Bitmap? Icon { get; set; }
}

/// <summary>
/// The scrollable list of installed games.
/// </summary>
/// <remarks>
/// Drawn by hand rather than built on <c>ListView</c>. Owner-drawing a ListView to look like
/// anything other than Windows Explorer means fighting the control at every step, while a
/// painted control this simple is a couple of hundred lines and behaves exactly as intended,
/// including the keyboard navigation and hover states a games launcher needs.
/// </remarks>
public sealed class GameListControl : Control
{
    private const int RowHeight = 58;
    private const int IconSize = 40;

    private readonly List<GameListItem> _items = [];
    private int _selectedIndex = -1;
    private int _hoveredIndex = -1;
    private int _scrollOffset;
    private string _filter = string.Empty;

    public GameListControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        BackColor = Theme.Surface;
        TabStop = true;
    }

    /// <summary>Rows currently shown, after filtering.</summary>
    private List<GameListItem> VisibleItems => _filter.Length == 0
        ? _items
        : _items.FindAll(item =>
            item.Entry.Title.Contains(_filter, StringComparison.CurrentCultureIgnoreCase) ||
            item.Entry.Vendor.Contains(_filter, StringComparison.CurrentCultureIgnoreCase));

    public LibraryEntry? SelectedEntry
    {
        get
        {
            List<GameListItem> visible = VisibleItems;
            return (uint)_selectedIndex < (uint)visible.Count ? visible[_selectedIndex].Entry : null;
        }
    }

    public event Action<LibraryEntry?>? SelectionChanged;

    /// <summary>Raised on double click or Enter: the user wants to play this game.</summary>
    public event Action<LibraryEntry>? ItemActivated;

    /// <summary>Replaces the contents, preserving the selection where the same game is still present.</summary>
    public void SetItems(IEnumerable<GameListItem> items)
    {
        string? previousId = SelectedEntry?.Id;

        foreach (GameListItem item in _items) item.Icon?.Dispose();
        _items.Clear();
        _items.AddRange(items);

        List<GameListItem> visible = VisibleItems;
        _selectedIndex = previousId is null
            ? (visible.Count > 0 ? 0 : -1)
            : visible.FindIndex(item => item.Entry.Id == previousId);

        if (_selectedIndex < 0 && visible.Count > 0) _selectedIndex = 0;

        _scrollOffset = 0;
        Invalidate();
        SelectionChanged?.Invoke(SelectedEntry);
    }

    /// <summary>Applies a text filter to the visible rows.</summary>
    public void SetFilter(string filter)
    {
        _filter = filter.Trim();
        _scrollOffset = 0;

        List<GameListItem> visible = VisibleItems;
        if (_selectedIndex >= visible.Count) _selectedIndex = visible.Count - 1;
        if (_selectedIndex < 0 && visible.Count > 0) _selectedIndex = 0;

        Invalidate();
        SelectionChanged?.Invoke(SelectedEntry);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(Theme.Surface);

        List<GameListItem> visible = VisibleItems;
        if (visible.Count == 0)
        {
            DrawEmptyState(graphics);
            return;
        }

        int first = Math.Max(0, _scrollOffset / RowHeight);
        int last = Math.Min(visible.Count - 1, (_scrollOffset + ClientSize.Height) / RowHeight);

        for (int i = first; i <= last; i++)
        {
            var bounds = new Rectangle(0, (i * RowHeight) - _scrollOffset, ClientSize.Width, RowHeight);
            DrawRow(graphics, visible[i], bounds, i == _selectedIndex, i == _hoveredIndex);
        }

        DrawScrollbar(graphics, visible.Count);
    }

    private void DrawRow(Graphics graphics, GameListItem item, Rectangle bounds, bool selected, bool hovered)
    {
        if (selected)
        {
            Theme.FillRounded(graphics,
                new Rectangle(4, bounds.Y + 2, bounds.Width - 14, RowHeight - 4), 6, Theme.AccentDim);
        }
        else if (hovered)
        {
            Theme.FillRounded(graphics,
                new Rectangle(4, bounds.Y + 2, bounds.Width - 14, RowHeight - 4), 6, Theme.SurfaceRaised);
        }

        var iconBounds = new Rectangle(14, bounds.Y + ((RowHeight - IconSize) / 2), IconSize, IconSize);

        if (item.Icon is { } icon)
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;

            // Icons are tiny and square-ish; fit them without distorting the aspect ratio.
            float scale = Math.Min((float)IconSize / icon.Width, (float)IconSize / icon.Height);
            var drawWidth = (int)(icon.Width * scale);
            var drawHeight = (int)(icon.Height * scale);

            graphics.DrawImage(icon, new Rectangle(
                iconBounds.X + ((IconSize - drawWidth) / 2),
                iconBounds.Y + ((IconSize - drawHeight) / 2),
                drawWidth,
                drawHeight));
        }
        else
        {
            Theme.FillRounded(graphics, iconBounds, 6, Theme.SurfaceRaised);

            using var initialBrush = new SolidBrush(Theme.TextFaint);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            string initial = item.Entry.Title.Length > 0 ? item.Entry.Title[..1].ToUpperInvariant() : "?";
            graphics.DrawString(initial, Theme.TitleFont, initialBrush, iconBounds, format);
        }

        int textLeft = iconBounds.Right + 12;
        int textWidth = Math.Max(20, bounds.Width - textLeft - 20);

        using var titleBrush = new SolidBrush(item.Entry.JarExists ? Theme.Text : Theme.Danger);
        using var subtitleBrush = new SolidBrush(Theme.TextMuted);
        using var trim = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };

        graphics.DrawString(item.Entry.Title, Theme.UiFontBold, titleBrush,
            new RectangleF(textLeft, bounds.Y + 11, textWidth, 18), trim);

        string subtitle = item.Entry.JarExists
            ? Describe(item.Entry)
            : "File is missing";

        graphics.DrawString(subtitle, Theme.SmallFont, subtitleBrush,
            new RectangleF(textLeft, bounds.Y + 30, textWidth, 16), trim);
    }

    private static string Describe(LibraryEntry entry)
    {
        if (entry.PlayCount == 0) return entry.Vendor;

        TimeSpan played = TimeSpan.FromSeconds(entry.PlaySeconds);
        string duration = played.TotalHours >= 1
            ? $"{(int)played.TotalHours}h {played.Minutes}m played"
            : played.TotalMinutes >= 1
                ? $"{(int)played.TotalMinutes}m played"
                : "just started";

        return $"{entry.Vendor}  ·  {duration}";
    }

    private void DrawEmptyState(Graphics graphics)
    {
        using var brush = new SolidBrush(Theme.TextFaint);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        string message = _items.Count == 0
            ? "Drop a .jar file here to add a game"
            : "No games match that search";

        graphics.DrawString(message, Theme.UiFont, brush,
            new Rectangle(10, 0, ClientSize.Width - 20, ClientSize.Height), format);
    }

    private void DrawScrollbar(Graphics graphics, int count)
    {
        int contentHeight = count * RowHeight;
        if (contentHeight <= ClientSize.Height) return;

        int trackHeight = ClientSize.Height - 8;
        var thumbHeight = (int)Math.Max(24, (long)trackHeight * ClientSize.Height / contentHeight);
        int travel = trackHeight - thumbHeight;
        int maxScroll = contentHeight - ClientSize.Height;
        int thumbTop = 4 + (maxScroll == 0 ? 0 : travel * _scrollOffset / maxScroll);

        Theme.FillRounded(graphics,
            new Rectangle(ClientSize.Width - 7, thumbTop, 4, thumbHeight), 2, Theme.Line);
    }

    // ---------------------------------------------------------------------------------
    // Interaction
    // ---------------------------------------------------------------------------------

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Scroll(-e.Delta / 120 * RowHeight);
    }

    private void Scroll(int delta)
    {
        int contentHeight = VisibleItems.Count * RowHeight;
        int maxScroll = Math.Max(0, contentHeight - ClientSize.Height);

        int updated = Math.Clamp(_scrollOffset + delta, 0, maxScroll);
        if (updated == _scrollOffset) return;

        _scrollOffset = updated;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        int index = IndexAt(e.Y);
        if (index == _hoveredIndex) return;

        _hoveredIndex = index;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoveredIndex < 0) return;
        _hoveredIndex = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        int index = IndexAt(e.Y);
        if (index < 0 || index == _selectedIndex) return;

        _selectedIndex = index;
        Invalidate();
        SelectionChanged?.Invoke(SelectedEntry);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (SelectedEntry is { } entry) ItemActivated?.Invoke(entry);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Enter or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        List<GameListItem> visible = VisibleItems;
        if (visible.Count == 0) return;

        switch (e.KeyCode)
        {
            case Keys.Up:
                Select(_selectedIndex - 1);
                break;

            case Keys.Down:
                Select(_selectedIndex + 1);
                break;

            case Keys.PageUp:
                Select(_selectedIndex - Math.Max(1, ClientSize.Height / RowHeight));
                break;

            case Keys.PageDown:
                Select(_selectedIndex + Math.Max(1, ClientSize.Height / RowHeight));
                break;

            case Keys.Home:
                Select(0);
                break;

            case Keys.End:
                Select(visible.Count - 1);
                break;

            case Keys.Enter:
                if (SelectedEntry is { } entry) ItemActivated?.Invoke(entry);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void Select(int index)
    {
        List<GameListItem> visible = VisibleItems;
        int clamped = Math.Clamp(index, 0, visible.Count - 1);
        if (clamped == _selectedIndex) return;

        _selectedIndex = clamped;
        EnsureVisible(clamped);
        Invalidate();
        SelectionChanged?.Invoke(SelectedEntry);
    }

    private void EnsureVisible(int index)
    {
        int top = index * RowHeight;
        int bottom = top + RowHeight;

        if (top < _scrollOffset) _scrollOffset = top;
        else if (bottom > _scrollOffset + ClientSize.Height) _scrollOffset = bottom - ClientSize.Height;
    }

    private int IndexAt(int y)
    {
        int index = (y + _scrollOffset) / RowHeight;
        return (uint)index < (uint)VisibleItems.Count ? index : -1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (GameListItem item in _items) item.Icon?.Dispose();
            _items.Clear();
        }

        base.Dispose(disposing);
    }
}
