using JMobile.Runtime.Graphics;

namespace JMobile.Runtime.Host;

/// <summary>A MIDP font request, which is the triple that identifies a font in that API.</summary>
public readonly record struct FontRequest(int Face, int Style, int Size);

/// <summary>
/// Turns a MIDP font request into a rasterised atlas.
/// </summary>
/// <remarks>
/// Glyph rasterisation is the one graphics operation this library does not do itself, because
/// doing it well means hinting and shaping outlines that the host platform already has a
/// tuned implementation of. The desktop shell supplies one built on GDI+; a headless test
/// process supplies <see cref="NullFontRasterizer"/> instead. Nothing else in the runtime
/// knows which is in use.
/// </remarks>
public interface IFontRasterizer
{
    BitmapFont Rasterize(FontRequest request);
}

/// <summary>
/// The rasteriser used when no host has registered a real one.
/// </summary>
/// <remarks>
/// It produces correctly sized but hollow glyphs, so text still occupies the right space and
/// layout code can be tested, while making it immediately obvious that font support is not
/// wired up rather than silently drawing nothing.
/// </remarks>
public sealed class NullFontRasterizer : IFontRasterizer
{
    public static readonly NullFontRasterizer Instance = new();

    private NullFontRasterizer() { }

    public BitmapFont Rasterize(FontRequest request)
    {
        int size = request.Size switch
        {
            8 => 10,   // SIZE_SMALL
            16 => 18,  // SIZE_LARGE
            _ => 13,   // SIZE_MEDIUM
        };

        const char first = ' ';
        const char last = '~';
        int count = last - first + 1;

        int cellWidth = Math.Max(3, size / 2);
        int cellHeight = size;

        var atlas = new Surface(cellWidth * count, cellHeight);
        var glyphs = new Glyph[count];

        for (int i = 0; i < count; i++)
        {
            int originX = i * cellWidth;
            glyphs[i] = new Glyph(
                X: (short)originX,
                Y: 0,
                Width: (short)cellWidth,
                Height: (short)cellHeight,
                OffsetX: 0,
                OffsetY: 0,
                Advance: (short)cellWidth);

            // Space stays blank; everything else gets an outline box.
            if (first + i == ' ') continue;

            for (int x = 1; x < cellWidth - 1; x++)
            {
                atlas.SetPixel(originX + x, 2, unchecked((int)0xFFFFFFFF));
                atlas.SetPixel(originX + x, cellHeight - 3, unchecked((int)0xFFFFFFFF));
            }

            for (int y = 2; y < cellHeight - 2; y++)
            {
                atlas.SetPixel(originX + 1, y, unchecked((int)0xFFFFFFFF));
                atlas.SetPixel(originX + cellWidth - 2, y, unchecked((int)0xFFFFFFFF));
            }
        }

        return new BitmapFont(atlas, glyphs, first, cellHeight, cellHeight - 3, 3)
        {
            Face = request.Face,
            Style = request.Style,
            Size = request.Size,
        };
    }
}
