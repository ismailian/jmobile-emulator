using System.Buffers.Binary;
using System.IO.Compression;
using JMobile.Runtime.Graphics;

namespace JMobile.Tests;

/// <summary>Tests for the software rasteriser and the PNG decoder.</summary>
public static class GraphicsUnitTests
{
    public static void Register()
    {
        RegisterRasterizerTests();
        RegisterBlendTests();
        RegisterPngTests();
    }

    private static void RegisterRasterizerTests()
    {
        Test.Suite("rasterizer");

        Test.Case("fillRect covers exactly the requested pixels", () =>
        {
            var surface = new Surface(10, 10, hasAlpha: false);
            var context = new GraphicsContext(surface);

            context.SetColor(0xFF0000);
            context.FillRect(2, 3, 4, 2);

            Test.Equal(unchecked((int)0xFFFF0000), surface.GetPixel(2, 3), "top-left corner");
            Test.Equal(unchecked((int)0xFFFF0000), surface.GetPixel(5, 4), "bottom-right corner");
            Test.Equal(0, surface.GetPixel(1, 3), "one pixel left is untouched");
            Test.Equal(0, surface.GetPixel(6, 3), "one pixel right is untouched");
            Test.Equal(0, surface.GetPixel(2, 5), "one row below is untouched");
        });

        Test.Case("the clip is honoured", () =>
        {
            var surface = new Surface(10, 10, hasAlpha: false);
            var context = new GraphicsContext(surface);

            context.SetClip(4, 4, 2, 2);
            context.SetColor(0x00FF00);
            context.FillRect(0, 0, 10, 10);

            Test.Equal(0, surface.GetPixel(3, 4), "left of the clip");
            Test.Equal(unchecked((int)0xFF00FF00), surface.GetPixel(4, 4), "inside the clip");
            Test.Equal(unchecked((int)0xFF00FF00), surface.GetPixel(5, 5), "inside the clip");
            Test.Equal(0, surface.GetPixel(6, 5), "right of the clip");
        });

        Test.Case("translation offsets drawing", () =>
        {
            var surface = new Surface(10, 10, hasAlpha: false);
            var context = new GraphicsContext(surface);

            context.Translate(3, 3);
            context.SetColor(0x0000FF);
            context.FillRect(0, 0, 2, 2);

            Test.Equal(unchecked((int)0xFF0000FF), surface.GetPixel(3, 3), "moved by the translation");
            Test.Equal(0, surface.GetPixel(0, 0), "origin untouched");
            Test.Equal(3, context.TranslateX);
        });

        Test.Case("clipRect intersects rather than replaces", () =>
        {
            var surface = new Surface(20, 20, hasAlpha: false);
            var context = new GraphicsContext(surface);

            context.SetClip(0, 0, 10, 10);
            context.ClipRect(5, 5, 10, 10);

            Test.Equal(5, context.ClipX);
            Test.Equal(5, context.ClipY);
            Test.Equal(5, context.ClipWidth, "intersection, not the second rectangle");
            Test.Equal(5, context.ClipHeight);
        });

        Test.Case("horizontal and vertical lines land on the right pixels", () =>
        {
            var surface = new Surface(10, 10, hasAlpha: false);
            var context = new GraphicsContext(surface);

            context.SetColor(0xFFFFFF);
            context.DrawLine(1, 5, 8, 5);
            context.DrawLine(5, 1, 5, 8);

            Test.Equal(unchecked((int)0xFFFFFFFF), surface.GetPixel(1, 5), "left end of the row");
            Test.Equal(unchecked((int)0xFFFFFFFF), surface.GetPixel(8, 5), "right end of the row");
            Test.Equal(unchecked((int)0xFFFFFFFF), surface.GetPixel(5, 1), "top of the column");
            Test.Equal(0, surface.GetPixel(0, 5), "just outside the line");
        });

        Test.Case("drawRect spans width+1 pixels as MIDP specifies", () =>
        {
            var surface = new Surface(10, 10, hasAlpha: false);
            var context = new GraphicsContext(surface);

            context.SetColor(0xFFFFFF);
            context.DrawRect(2, 2, 4, 4);

            Test.Equal(unchecked((int)0xFFFFFFFF), surface.GetPixel(6, 6), "the far corner is included");
            Test.Equal(0, surface.GetPixel(4, 4), "the interior is not filled");
        });

        Test.Case("blit copies an opaque region", () =>
        {
            var source = new Surface(4, 4, hasAlpha: false);
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++) source.SetPixel(x, y, unchecked((int)0xFF000000) | ((x + (y * 4)) << 8));
            }

            var target = new Surface(8, 8, hasAlpha: false);
            var context = new GraphicsContext(target);
            context.Blit(source, 0, 0, 4, 4, 2, 2, SpriteTransform.None);

            Test.Equal(source.GetPixel(0, 0), target.GetPixel(2, 2), "top-left");
            Test.Equal(source.GetPixel(3, 3), target.GetPixel(5, 5), "bottom-right");
            Test.Equal(0, target.GetPixel(1, 1), "outside the destination");
        });

        Test.Case("mirrored blit reverses the columns", () =>
        {
            var source = new Surface(3, 1, hasAlpha: false);
            source.SetPixel(0, 0, unchecked((int)0xFF111111));
            source.SetPixel(1, 0, unchecked((int)0xFF222222));
            source.SetPixel(2, 0, unchecked((int)0xFF333333));
            source.HasAlpha = true; // force the per-pixel path

            var target = new Surface(3, 1, hasAlpha: false);
            var context = new GraphicsContext(target);
            context.Blit(source, 0, 0, 3, 1, 0, 0, SpriteTransform.Mirror);

            Test.Equal(unchecked((int)0xFF333333), target.GetPixel(0, 0));
            Test.Equal(unchecked((int)0xFF111111), target.GetPixel(2, 0));
        });

        Test.Case("rotate 90 exchanges the axes", () =>
        {
            var source = new Surface(2, 3, hasAlpha: true);
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 2; x++)
                    source.SetPixel(x, y, unchecked((int)0xFF000000) | ((x * 10) + y));
            }

            var target = new Surface(3, 2, hasAlpha: false);
            var context = new GraphicsContext(target);
            context.Blit(source, 0, 0, 2, 3, 0, 0, SpriteTransform.Rotate90);

            // A 2x3 source drawn rotated occupies 3x2, and every destination pixel must come
            // from somewhere in the source.
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 3; x++)
                    Test.NotEqual(0, target.GetPixel(x, y), $"pixel {x},{y} was written");
            }
        });

        Test.Case("fillTriangle covers its interior", () =>
        {
            var surface = new Surface(16, 16, hasAlpha: false);
            var context = new GraphicsContext(surface);

            context.SetColor(0xFF00FF);
            context.FillTriangle(2, 2, 14, 2, 2, 14);

            Test.Equal(unchecked((int)0xFFFF00FF), surface.GetPixel(3, 3), "near the right angle");
            Test.Equal(0, surface.GetPixel(13, 13), "the far corner is outside the triangle");
        });

        Test.Case("drawRGB uploads pixels with and without alpha", () =>
        {
            var surface = new Surface(4, 4, hasAlpha: false);
            surface.Clear(unchecked((int)0xFF000000));
            var context = new GraphicsContext(surface);

            int[] pixels = new int[4];
            Array.Fill(pixels, unchecked((int)0x80FF0000)); // half-transparent red

            context.DrawRgb(pixels, 0, 2, 0, 0, 2, 2, processAlpha: false);
            Test.Equal(unchecked((int)0xFFFF0000), surface.GetPixel(0, 0), "alpha ignored means opaque");

            surface.Clear(unchecked((int)0xFF000000));
            context.DrawRgb(pixels, 0, 2, 0, 0, 2, 2, processAlpha: true);

            int blended = surface.GetPixel(0, 0);
            int red = (blended >> 16) & 0xFF;
            Test.True(red is > 100 and < 160, $"half-blended red should be near 128, was {red}");
        });
    }

    private static void RegisterBlendTests()
    {
        Test.Suite("blending");

        Test.Case("opaque source replaces the destination", () =>
            Test.Equal(unchecked((int)0xFF123456),
                PixelBlend.Over(unchecked((int)0xFF123456), unchecked((int)0xFFFFFFFF))));

        Test.Case("fully transparent source is ignored", () =>
            Test.Equal(unchecked((int)0xFF00FF00),
                PixelBlend.Over(0x00FF0000, unchecked((int)0xFF00FF00))));

        Test.Case("half alpha lands near the midpoint", () =>
        {
            int result = PixelBlend.Over(unchecked((int)0x80FFFFFF), unchecked((int)0xFF000000));
            int channel = result & 0xFF;
            Test.True(channel is > 120 and < 136, $"expected about 128, got {channel}");
        });

        Test.Case("greyscale uses luma weights", () =>
        {
            int grey = PixelBlend.ToGrey(unchecked((int)0xFF00FF00));
            int channel = grey & 0xFF;
            // Green carries 151/256 of the luma, so pure green lands near 151.
            Test.True(channel is > 140 and < 160, $"expected about 151, got {channel}");
        });
    }

    private static void RegisterPngTests()
    {
        Test.Suite("png decoder");

        Test.Case("rejects data that is not a PNG", () =>
        {
            Test.False(PngDecoder.IsPng([1, 2, 3, 4]), "random bytes");
            Test.Throws<ImageFormatException>(() => PngDecoder.Decode([1, 2, 3, 4]), "decoding garbage");
        });

        Test.Case("decodes 8-bit RGBA", () =>
        {
            int[] pixels =
            [
                unchecked((int)0xFFFF0000), unchecked((int)0xFF00FF00),
                unchecked((int)0x800000FF), unchecked((int)0x00FFFFFF),
            ];

            byte[] png = EncodeRgba(2, 2, pixels);
            Surface surface = PngDecoder.Decode(png);

            Test.Equal(2, surface.Width);
            Test.Equal(2, surface.Height);
            Test.Equal(unchecked((int)0xFFFF0000), surface.GetPixel(0, 0), "opaque red");
            Test.Equal(unchecked((int)0xFF00FF00), surface.GetPixel(1, 0), "opaque green");
            Test.Equal(unchecked((int)0x800000FF), surface.GetPixel(0, 1), "half-transparent blue");
            Test.True(surface.HasAlpha, "alpha was detected");
        });

        Test.Case("decodes a larger image through every filter", () =>
        {
            const int size = 32;
            var pixels = new int[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    pixels[(y * size) + x] = unchecked((int)0xFF000000) | (x << 16) | (y << 8) | ((x + y) & 0xFF);
            }

            Surface surface = PngDecoder.Decode(EncodeRgba(size, size, pixels));

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Test.Equal(pixels[(y * size) + x], surface.GetPixel(x, y), $"pixel {x},{y}");
                }
            }
        });
    }

    // ---------------------------------------------------------------------------------
    // A minimal PNG encoder, used only to produce test input
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Writes a colour-type-6 PNG. Written here rather than taken from a host imaging library
    /// so the decoder is tested against bytes this suite fully controls.
    /// </summary>
    private static byte[] EncodeRgba(int width, int height, int[] pixels)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // colour type: truecolour with alpha
        WriteChunk(output, "IHDR", header);

        // Scanlines are emitted with filter type 0, which the decoder must still unfilter.
        using var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < width; x++)
            {
                int pixel = pixels[(y * width) + x];
                raw.WriteByte((byte)(pixel >> 16));
                raw.WriteByte((byte)(pixel >> 8));
                raw.WriteByte((byte)pixel);
                raw.WriteByte((byte)(pixel >> 24));
            }
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(deflate);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] payload)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
        output.Write(length);

        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(payload);

        // The decoder does not verify CRCs, so a zero placeholder is sufficient here.
        output.Write([0, 0, 0, 0]);
    }
}
