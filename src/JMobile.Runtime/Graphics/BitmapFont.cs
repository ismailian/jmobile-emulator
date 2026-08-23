namespace JMobile.Runtime.Graphics;

/// <summary>Placement of one glyph inside a font atlas.</summary>
/// <param name="X">Left edge of the glyph within the atlas.</param>
/// <param name="Y">Top edge of the glyph within the atlas.</param>
/// <param name="Width">Glyph width in pixels.</param>
/// <param name="Height">Glyph height in pixels.</param>
/// <param name="OffsetX">Horizontal offset from the pen position to the glyph's left edge.</param>
/// <param name="OffsetY">Vertical offset from the line's top to the glyph's top edge.</param>
/// <param name="Advance">Distance the pen moves after drawing this glyph.</param>
public readonly record struct Glyph(
    short X,
    short Y,
    short Width,
    short Height,
    short OffsetX,
    short OffsetY,
    short Advance);

/// <summary>
/// A font rasterised once into a single coverage atlas.
/// </summary>
/// <remarks>
/// Text in a MIDlet is drawn constantly — score counters, menus, dialogue — so glyphs are
/// rasterised once at startup and drawn afterwards as pure blits. The atlas stores coverage in
/// the alpha channel only; the colour comes from the graphics context at draw time, which means
/// one atlas serves every colour the game ever uses.
/// </remarks>
public sealed class BitmapFont
{
    private readonly Glyph[] _glyphs;
    private readonly char _first;
    private readonly Glyph _fallback;

    public BitmapFont(
        Surface atlas,
        Glyph[] glyphs,
        char firstCharacter,
        int height,
        int ascent,
        int descent)
    {
        Atlas = atlas;
        _glyphs = glyphs;
        _first = firstCharacter;
        Height = height;
        Ascent = ascent;
        Descent = descent;
        _fallback = glyphs.Length > 0 ? glyphs[0] : default;
    }

    /// <summary>Coverage atlas: alpha carries the glyph shape, colour is applied when drawing.</summary>
    public Surface Atlas { get; }

    /// <summary>Line height, which is what <c>Font.getHeight</c> reports.</summary>
    public int Height { get; }

    public int Ascent { get; }

    public int Descent { get; }

    /// <summary>Nominal MIDP face this font was built for, kept so the same request is reused.</summary>
    public int Face { get; init; }

    public int Style { get; init; }

    public int Size { get; init; }

    public bool HasGlyph(char character)
    {
        int index = character - _first;
        return (uint)index < (uint)_glyphs.Length;
    }

    public Glyph GetGlyph(char character)
    {
        int index = character - _first;
        return (uint)index < (uint)_glyphs.Length ? _glyphs[index] : _fallback;
    }

    public int CharWidth(char character) => GetGlyph(character).Advance;

    public int StringWidth(ReadOnlySpan<char> text)
    {
        int total = 0;
        foreach (char character in text) total += GetGlyph(character).Advance;
        return total;
    }
}
