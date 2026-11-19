using System.ComponentModel;
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

/// <summary>The orders the library can be listed in.</summary>
public enum GameSortOrder
{
    RecentlyPlayed,
    Name,
    MostPlayed,
    RecentlyAdded,
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
    private const int RowHeight = 90;
    private const int IconSize = 58;

    private static readonly Font HeadingFont = new("Segoe UI", 11f);

    private readonly List<GameListItem> _items = [];
    private int _selectedIndex = -1;
    private int _hoveredIndex = -1;
    private int _scrollOffset;
    private string _filter = string.Empty;
    private GameSortOrder _sortOrder = GameSortOrder.RecentlyPlayed;

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
        Font = Theme.UiFont;
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

    /// <summary>The order rows are listed in.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GameSortOrder SortOrder
    {
        get => _sortOrder;
        set
        {
            if (_sortOrder == value) return;

            string? selectedId = SelectedEntry?.Id;
            _sortOrder = value;
            Sort();

            _selectedIndex = Math.Max(0, VisibleItems.FindIndex(item => item.Entry.Id == selectedId));
            if (VisibleItems.Count == 0) _selectedIndex = -1;
            _scrollOffset = 0;
            Invalidate();
        }
    }

    private void Sort()
    {
        Comparison<GameListItem> comparison = _sortOrder switch
        {
            GameSortOrder.Name => (a, b) =>
                StringComparer.CurrentCultureIgnoreCase.Compare(a.Entry.Title, b.Entry.Title),
            GameSortOrder.MostPlayed => (a, b) => b.Entry.PlaySeconds.CompareTo(a.Entry.PlaySeconds),
            GameSortOrder.RecentlyAdded => (a, b) => b.Entry.AddedAt.CompareTo(a.Entry.AddedAt),
            _ => (a, b) => (b.Entry.LastPlayedAt ?? DateTimeOffset.MinValue)
                .CompareTo(a.Entry.LastPlayedAt ?? DateTimeOffset.MinValue),
        };

        // A stable sort, so ties keep the library's own order.
        List<GameListItem> sorted = [.. _items.Order(Comparer<GameListItem>.Create(comparison))];
        _items.Clear();
        _items.AddRange(sorted);
    }

    /// <summary>Replaces the contents, preserving the selection where the same game is still present.</summary>
    public void SetItems(IEnumerable<GameListItem> items)
    {
        string? previousId = SelectedEntry?.Id;

        foreach (GameListItem item in _items) item.Icon?.Dispose();
        _items.Clear();
        _items.AddRange(items);
        Sort();

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
        graphics.Clear(BackColor);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        List<GameListItem> visible = VisibleItems;

        int first = Math.Max(0, _scrollOffset / RowHeight);
        int last = Math.Min(visible.Count - 1, (_scrollOffset + ClientSize.Height) / RowHeight);

        for (int i = first; i <= last; i++)
        {
            var bounds = new Rectangle(0, (i * RowHeight) - _scrollOffset, ClientSize.Width, RowHeight);
            DrawRow(graphics, visible[i], bounds, i == _selectedIndex, i == _hoveredIndex);
        }

        DrawScrollbar(graphics, visible.Count);

        // Whatever room is left under the rows holds a hint about what to do next.
        int contentBottom = (visible.Count * RowHeight) - _scrollOffset;
        DrawEmptyState(graphics, new Rectangle(0, contentBottom, ClientSize.Width, ClientSize.Height - contentBottom));
    }

    private void DrawRow(Graphics graphics, GameListItem item, Rectangle bounds, bool selected, bool hovered)
    {
        var card = new Rectangle(bounds.X + 3, bounds.Y + 4, bounds.Width - 7, RowHeight - 9);

        if (selected)
        {
            Theme.DrawGlow(graphics, card, 12, Theme.Accent, 3);
            Theme.FillRoundedGradient(graphics, card, 12,
                Color.FromArgb(0x22, 0x55, 0xAE), Color.FromArgb(0x16, 0x3B, 0x80));
            Theme.DrawRounded(graphics, card, 12, Focused ? Theme.AccentBright : Color.FromArgb(0x4A, 0x86, 0xF0), 1.5f);
        }
        else
        {
            Theme.FillRounded(graphics, card, 12, hovered ? Theme.SurfaceHover : Theme.SurfaceRaised);
            Theme.DrawRounded(graphics, card, 12, hovered ? Theme.LineStrong : Theme.Line);
        }

        var iconBounds = new Rectangle(card.X + 14, card.Y + ((card.Height - IconSize) / 2), IconSize, IconSize);
        Theme.FillRounded(graphics, iconBounds, 9, Theme.ScreenWell);
        Theme.DrawRounded(graphics, iconBounds, 9, selected ? Color.FromArgb(0x3A, 0x6E, 0xC8) : Theme.Line);

        if (item.Icon is { } icon)
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;

            // Icons are tiny and square-ish; fit them without distorting the aspect ratio.
            const int inner = IconSize - 12;
            float scale = Math.Min((float)inner / icon.Width, (float)inner / icon.Height);
            var drawWidth = (int)(icon.Width * scale);
            var drawHeight = (int)(icon.Height * scale);

            graphics.DrawImage(icon, new Rectangle(
                iconBounds.X + ((IconSize - drawWidth) / 2),
                iconBounds.Y + ((IconSize - drawHeight) / 2),
                drawWidth,
                drawHeight));

            graphics.PixelOffsetMode = PixelOffsetMode.Default;
        }
        else
        {
            string initial = item.Entry.Title.Length > 0 ? item.Entry.Title[..1].ToUpperInvariant() : "?";
            Theme.DrawText(graphics, initial, Theme.TitleFont, Theme.TextMuted, iconBounds,
                TextFormatFlags.HorizontalCenter);
        }

        int textLeft = iconBounds.Right + 16;
        int textWidth = Math.Max(20, card.Right - textLeft - 40);

        Theme.DrawText(graphics, item.Entry.Title, Theme.TitleFont,
            item.Entry.JarExists ? Theme.Text : Theme.Danger,
            new Rectangle(textLeft, card.Y + 12, textWidth, 26));

        string subtitle = item.Entry.JarExists ? Describe(item.Entry) : "File is missing";
        Theme.DrawText(graphics, subtitle, Theme.SmallFont, selected ? Theme.Text : Theme.TextMuted,
            new Rectangle(textLeft, card.Y + 38, textWidth, 20));

        Theme.DrawGlyph(graphics, Glyphs.ChevronRight, Theme.IconFontSmall,
            selected ? Theme.Text : Theme.TextFaint,
            new RectangleF(card.Right - 38, card.Y, 24, card.Height));
    }

    private static string Describe(LibraryEntry entry)
    {
        if (entry.PlayCount == 0) return entry.Vendor;

        TimeSpan played = TimeSpan.FromSeconds(entry.PlaySeconds);
        string duration = played.TotalHours >= 1
            ? $"{(int)played.TotalHours}h {played.Minutes}m played"
            : played.TotalMinutes >= 1
                ? $"{(int)played.TotalMinutes}m played"
                : "Just started";

        return $"{entry.Vendor}  •  {duration}";
    }

    private void DrawEmptyState(Graphics graphics, Rectangle area)
    {
        // Only worth drawing when there is room for the whole message; a sliver of it is noise.
        if (area.Height < 190) return;

        (string heading, string detail) = _items.Count == 0
            ? ("No games yet", "Click “Add Game” or drop .jar files\nhere to start playing.")
            : VisibleItems.Count == 0
                ? ("No games match", "Try a different search.")
                : ("No more games here", "Click “Add Game” to load\nyour JAR files.");

        int top = area.Y + ((area.Height - 170) / 2);
        int cx = area.X + (area.Width / 2);

        Theme.DrawLogoMark(graphics, new RectangleF(cx - 36, top, 72, 76), Theme.LineStrong);

        Theme.DrawText(graphics, heading, HeadingFont, Theme.TextMuted,
            new Rectangle(area.X + 10, top + 90, area.Width - 20, 26), TextFormatFlags.HorizontalCenter);

        TextRenderer.DrawText(graphics, detail, Theme.UiFont,
            new Rectangle(area.X + 10, top + 120, area.Width - 20, 50), Theme.TextFaint,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
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
            new Rectangle(ClientSize.Width - 4, thumbTop, 4, thumbHeight), 2, Theme.LineStrong);
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
