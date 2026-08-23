using System.Drawing.Imaging;
using System.Drawing.Text;
using JMobile.Runtime.Graphics;
using JMobile.Runtime.Host;

namespace JMobile.App.Hosting;

/// <summary>
/// Rasterises MIDP fonts into glyph atlases using GDI+.
/// </summary>
/// <remarks>
/// The runtime library deliberately does not rasterise text itself: hinting outlines well is a
/// solved problem the host already has a tuned implementation of. This is the desktop side of
/// that contract. Each font is drawn once into a single atlas texture, after which every
/// <c>drawString</c> in the emulator is a blit.
/// <para>
/// Antialiasing is off. These fonts are being drawn at eight to sixteen pixels onto a phone
/// screen that is then magnified several times; grey edge pixels that look right at 1x turn into
/// visible mush at 4x, where hard edges stay crisp and match how the original hardware looked.
/// </para>
/// </remarks>
public sealed class GdiFontRasterizer : IFontRasterizer
{
    private const char FirstCharacter = ' ';
    private const char LastCharacter = 'ÿ';

    /// <summary>
    /// Faces chosen to resemble what MIDP asked for, falling back through what Windows ships.
    /// </summary>
    private static readonly string[] ProportionalFamilies = ["Segoe UI", "Tahoma", "Arial"];
    private static readonly string[] MonospaceFamilies = ["Consolas", "Courier New"];

    public BitmapFont Rasterize(FontRequest request)
    {
        float pointSize = request.Size switch
        {
            8 => 7.5f,    // SIZE_SMALL
            16 => 11.5f,  // SIZE_LARGE
            _ => 9f,      // SIZE_MEDIUM
        };

        FontStyle style = FontStyle.Regular;
        if ((request.Style & 1) != 0) style |= FontStyle.Bold;
        if ((request.Style & 2) != 0) style |= FontStyle.Italic;
        if ((request.Style & 4) != 0) style |= FontStyle.Underline;

        string[] families = request.Face == 32 ? MonospaceFamilies : ProportionalFamilies;

        using Font font = CreateFont(families, pointSize, style);
        return Build(font, request);
    }

    private static Font CreateFont(string[] families, float size, FontStyle style)
    {
        foreach (string family in families)
        {
            try
            {
                var candidate = new Font(family, size, style, GraphicsUnit.Point);
                // GDI+ silently substitutes when a family is missing, so the result is checked.
                if (string.Equals(candidate.FontFamily.Name, family, StringComparison.OrdinalIgnoreCase))
                    return candidate;

                candidate.Dispose();
            }
            catch (ArgumentException)
            {
                // The family exists but not in this style; try the next one.
            }
        }

        return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
    }

    private static BitmapFont Build(Font font, FontRequest request)
    {
        int count = LastCharacter - FirstCharacter + 1;

        using var measureBitmap = new Bitmap(1, 1);
        using var measure = System.Drawing.Graphics.FromImage(measureBitmap);
        measure.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        // Advances are measured by difference against a pair of sentinels rather than by
        // measuring the glyph alone. Typographic measurement reports zero for a space, which
        // would run every word in the game together, and it also drops the side bearings that
        // decide where the next glyph starts.
        const string Sentinel = "||";
        float sentinelWidth = measure.MeasureString(
            Sentinel, font, PointF.Empty, StringFormat.GenericTypographic).Width;

        var advances = new int[count];
        var cells = new int[count];
        int cellHeight = font.Height;
        int atlasWidth = 0;

        for (int i = 0; i < count; i++)
        {
            var character = (char)(FirstCharacter + i);

            float pairWidth = measure.MeasureString(
                "|" + character + "|", font, PointF.Empty, StringFormat.GenericTypographic).Width;
            advances[i] = Math.Max(1, (int)Math.Ceiling(pairWidth - sentinelWidth));

            // The cell may need to be wider than the advance: italics and script faces paint
            // outside their advance, and a cell sized to the advance alone would clip them.
            float inkWidth = measure.MeasureString(character.ToString(), font).Width;
            cells[i] = Math.Max(advances[i], (int)Math.Ceiling(inkWidth));

            atlasWidth += cells[i] + 1;
        }

        using var atlasBitmap = new Bitmap(atlasWidth, cellHeight, PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(atlasBitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

            using var brush = new SolidBrush(Color.White);

            int pen = 0;
            for (int i = 0; i < count; i++)
            {
                var character = (char)(FirstCharacter + i);
                graphics.DrawString(character.ToString(), font, brush,
                    new PointF(pen, 0), StringFormat.GenericTypographic);
                pen += cells[i] + 1;
            }
        }

        Surface atlas = ToSurface(atlasBitmap);
        var glyphs = new Glyph[count];

        int cursor = 0;
        for (int i = 0; i < count; i++)
        {
            glyphs[i] = new Glyph(
                X: (short)cursor,
                Y: 0,
                Width: (short)cells[i],
                Height: (short)cellHeight,
                OffsetX: 0,
                OffsetY: 0,
                Advance: (short)advances[i]);

            cursor += cells[i] + 1;
        }

        // GDI+ reports ascent in design units, which have to be scaled to the rendered size.
        FontFamily family = font.FontFamily;
        int designUnits = family.GetEmHeight(font.Style);
        int ascent = designUnits == 0
            ? cellHeight - 2
            : (int)Math.Round(family.GetCellAscent(font.Style) * font.GetHeight() / designUnits);

        return new BitmapFont(atlas, glyphs, FirstCharacter, cellHeight, ascent, cellHeight - ascent)
        {
            Face = request.Face,
            Style = request.Style,
            Size = request.Size,
        };
    }

    /// <summary>Copies a GDI+ bitmap into the runtime's own pixel representation.</summary>
    private static Surface ToSurface(Bitmap bitmap)
    {
        var surface = new Surface(bitmap.Width, bitmap.Height);

        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            // Format32bppArgb is laid out exactly as the runtime's 0xAARRGGBB, so each row is a
            // straight copy; the stride may still exceed the width, hence the row loop.
            for (int y = 0; y < bitmap.Height; y++)
            {
                nint rowStart = data.Scan0 + (y * data.Stride);
                System.Runtime.InteropServices.Marshal.Copy(
                    rowStart, surface.Pixels, y * bitmap.Width, bitmap.Width);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return surface;
    }
}
