using System.Buffers.Binary;
using System.IO.Compression;

namespace JMobile.Runtime.Graphics;

/// <summary>Raised when image bytes are not a PNG this decoder can read.</summary>
public sealed class ImageFormatException(string message) : Exception(message);

/// <summary>
/// A PNG decoder producing <see cref="Surface"/> directly.
/// </summary>
/// <remarks>
/// MIDP 2.0 requires PNG and nothing else, so every sprite sheet, tile set and menu background
/// in a J2ME game arrives in this format. Decoding it here rather than calling out to the host
/// imaging stack keeps the runtime library free of platform dependencies and, more practically,
/// avoids the colour-management and premultiplication surprises that host decoders introduce —
/// a game's transparent colour key must survive decoding exactly as authored.
/// <para>
/// All five colour types, all bit depths, transparency chunks and Adam7 interlacing are
/// supported. Sixteen-bit samples are reduced to eight, which is what the phone hardware these
/// games targeted would have done anyway.
/// </para>
/// </remarks>
public static class PngDecoder
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // Adam7 pass geometry: where each pass starts and how far it steps.
    private static ReadOnlySpan<int> PassStartX => [0, 4, 0, 2, 0, 1, 0];
    private static ReadOnlySpan<int> PassStartY => [0, 0, 4, 0, 2, 0, 1];
    private static ReadOnlySpan<int> PassStepX => [8, 8, 4, 4, 2, 2, 1];
    private static ReadOnlySpan<int> PassStepY => [8, 8, 8, 4, 4, 2, 2];

    public static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[..8].SequenceEqual(Signature);

    public static Surface Decode(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data)) throw new ImageFormatException("Data does not start with a PNG signature.");

        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        int[]? transparentSample = null;
        var compressed = new MemoryStream();

        int offset = 8;
        bool sawHeader = false;

        while (offset + 8 <= data.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
            if (length < 0 || offset + 12 + length > data.Length)
                throw new ImageFormatException("PNG chunk length runs past the end of the data.");

            ReadOnlySpan<byte> type = data.Slice(offset + 4, 4);
            ReadOnlySpan<byte> payload = data.Slice(offset + 8, length);
            offset += 12 + length;

            if (Is(type, "IHDR"))
            {
                if (length < 13) throw new ImageFormatException("IHDR chunk is truncated.");
                width = BinaryPrimitives.ReadInt32BigEndian(payload);
                height = BinaryPrimitives.ReadInt32BigEndian(payload[4..]);
                bitDepth = payload[8];
                colorType = payload[9];
                interlace = payload[12];
                sawHeader = true;

                if (width <= 0 || height <= 0)
                    throw new ImageFormatException($"PNG has invalid dimensions {width}x{height}.");
                if (payload[10] != 0)
                    throw new ImageFormatException("PNG uses an unknown compression method.");
            }
            else if (Is(type, "PLTE"))
            {
                palette = payload.ToArray();
            }
            else if (Is(type, "tRNS"))
            {
                if (colorType == 3)
                {
                    paletteAlpha = payload.ToArray();
                }
                else if (colorType == 0 && length >= 2)
                {
                    transparentSample = [BinaryPrimitives.ReadUInt16BigEndian(payload)];
                }
                else if (colorType == 2 && length >= 6)
                {
                    transparentSample =
                    [
                        BinaryPrimitives.ReadUInt16BigEndian(payload),
                        BinaryPrimitives.ReadUInt16BigEndian(payload[2..]),
                        BinaryPrimitives.ReadUInt16BigEndian(payload[4..]),
                    ];
                }
            }
            else if (Is(type, "IDAT"))
            {
                // Image data may be split across any number of chunks that must be
                // concatenated before inflating; the split points are not stream boundaries.
                compressed.Write(payload);
            }
            else if (Is(type, "IEND"))
            {
                break;
            }
        }

        if (!sawHeader) throw new ImageFormatException("PNG has no IHDR chunk.");
        if (compressed.Length == 0) throw new ImageFormatException("PNG has no image data.");

        byte[] raw = Inflate(compressed);

        int channels = ChannelCount(colorType);
        int bitsPerPixel = channels * bitDepth;

        var surface = new Surface(width, height);

        if (interlace == 0)
        {
            DecodePass(raw, 0, surface, 0, 0, 1, 1, width, height,
                bitDepth, colorType, channels, bitsPerPixel, palette, paletteAlpha, transparentSample);
        }
        else if (interlace == 1)
        {
            int consumed = 0;
            for (int pass = 0; pass < 7; pass++)
            {
                int passWidth = CeilDiv(width - PassStartX[pass], PassStepX[pass]);
                int passHeight = CeilDiv(height - PassStartY[pass], PassStepY[pass]);
                if (passWidth <= 0 || passHeight <= 0) continue;

                consumed += DecodePass(raw, consumed, surface,
                    PassStartX[pass], PassStartY[pass], PassStepX[pass], PassStepY[pass],
                    passWidth, passHeight,
                    bitDepth, colorType, channels, bitsPerPixel, palette, paletteAlpha, transparentSample);
            }
        }
        else
        {
            throw new ImageFormatException($"PNG uses unknown interlace method {interlace}.");
        }

        surface.DetectAlpha();
        return surface;
    }

    /// <summary>
    /// Unfilters and expands one interlace pass, returning how many compressed bytes it used.
    /// </summary>
    private static int DecodePass(
        byte[] raw,
        int rawOffset,
        Surface surface,
        int startX,
        int startY,
        int stepX,
        int stepY,
        int passWidth,
        int passHeight,
        int bitDepth,
        int colorType,
        int channels,
        int bitsPerPixel,
        byte[]? palette,
        byte[]? paletteAlpha,
        int[]? transparentSample)
    {
        int bytesPerRow = CeilDiv(passWidth * bitsPerPixel, 8);
        int filterUnit = Math.Max(1, bitsPerPixel / 8);

        var current = new byte[bytesPerRow];
        var previous = new byte[bytesPerRow];

        int consumed = 0;
        for (int row = 0; row < passHeight; row++)
        {
            if (rawOffset + consumed >= raw.Length)
                throw new ImageFormatException("PNG image data ended before the last scanline.");

            byte filter = raw[rawOffset + consumed++];
            int available = Math.Min(bytesPerRow, raw.Length - (rawOffset + consumed));
            if (available < bytesPerRow) throw new ImageFormatException("PNG scanline is truncated.");

            Array.Copy(raw, rawOffset + consumed, current, 0, bytesPerRow);
            consumed += bytesPerRow;

            Unfilter(filter, current, previous, filterUnit);
            ExpandRow(current, surface, startX + 0, startY + (row * stepY), stepX, passWidth,
                bitDepth, colorType, channels, palette, paletteAlpha, transparentSample);

            (previous, current) = (current, previous);
        }

        return consumed;
    }

    /// <summary>Reverses one of the five PNG scanline filters in place.</summary>
    private static void Unfilter(byte filter, byte[] line, byte[] previous, int unit)
    {
        switch (filter)
        {
            case 0:
                break;

            case 1: // Sub: each byte was stored as a delta from the pixel to its left.
                for (int i = unit; i < line.Length; i++)
                    line[i] = (byte)(line[i] + line[i - unit]);
                break;

            case 2: // Up: delta from the byte directly above.
                for (int i = 0; i < line.Length; i++)
                    line[i] = (byte)(line[i] + previous[i]);
                break;

            case 3: // Average: delta from the mean of left and above.
                for (int i = 0; i < line.Length; i++)
                {
                    int left = i >= unit ? line[i - unit] : 0;
                    line[i] = (byte)(line[i] + ((left + previous[i]) >> 1));
                }
                break;

            case 4: // Paeth: delta from whichever neighbour best predicts the value.
                for (int i = 0; i < line.Length; i++)
                {
                    int left = i >= unit ? line[i - unit] : 0;
                    int above = previous[i];
                    int upperLeft = i >= unit ? previous[i - unit] : 0;
                    line[i] = (byte)(line[i] + Paeth(left, above, upperLeft));
                }
                break;

            default:
                throw new ImageFormatException($"PNG uses unknown scanline filter {filter}.");
        }
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        int estimate = left + above - upperLeft;
        int distanceLeft = Math.Abs(estimate - left);
        int distanceAbove = Math.Abs(estimate - above);
        int distanceUpperLeft = Math.Abs(estimate - upperLeft);

        if (distanceLeft <= distanceAbove && distanceLeft <= distanceUpperLeft) return left;
        return distanceAbove <= distanceUpperLeft ? above : upperLeft;
    }

    /// <summary>Turns one unfiltered scanline into ARGB pixels on the surface.</summary>
    private static void ExpandRow(
        byte[] line,
        Surface surface,
        int startX,
        int y,
        int stepX,
        int passWidth,
        int bitDepth,
        int colorType,
        int channels,
        byte[]? palette,
        byte[]? paletteAlpha,
        int[]? transparentSample)
    {
        if (y < 0 || y >= surface.Height) return;

        int[] pixels = surface.Pixels;
        int rowStart = y * surface.Width;
        Span<int> samples = stackalloc int[4];

        for (int i = 0; i < passWidth; i++)
        {
            int x = startX + (i * stepX);
            if (x >= surface.Width) break;

            ReadSamples(line, i, bitDepth, channels, samples);

            int argb = colorType switch
            {
                0 => Grey(samples[0], bitDepth, transparentSample),
                2 => Truecolor(samples, bitDepth, transparentSample),
                3 => Indexed(samples[0], palette, paletteAlpha),
                4 => GreyAlpha(samples, bitDepth),
                6 => TruecolorAlpha(samples, bitDepth),
                _ => throw new ImageFormatException($"PNG uses unknown colour type {colorType}."),
            };

            pixels[rowStart + x] = argb;
        }
    }

    /// <summary>Reads one pixel's channel samples, handling sub-byte packing.</summary>
    private static void ReadSamples(byte[] line, int pixelIndex, int bitDepth, int channels, Span<int> samples)
    {
        switch (bitDepth)
        {
            case 8:
            {
                int offset = pixelIndex * channels;
                for (int c = 0; c < channels; c++) samples[c] = line[offset + c];
                break;
            }

            case 16:
            {
                int offset = pixelIndex * channels * 2;
                // The low byte is discarded; phones of this era were 16- or 18-bit displays.
                for (int c = 0; c < channels; c++) samples[c] = line[offset + (c * 2)];
                break;
            }

            default:
            {
                // 1, 2 and 4 bits per sample, packed most-significant first.
                int bitPosition = pixelIndex * channels * bitDepth;
                int mask = (1 << bitDepth) - 1;
                for (int c = 0; c < channels; c++)
                {
                    int bit = bitPosition + (c * bitDepth);
                    int shift = 8 - bitDepth - (bit & 7);
                    samples[c] = (line[bit >> 3] >> shift) & mask;
                }
                break;
            }
        }
    }

    private static int Scale(int sample, int bitDepth) => bitDepth switch
    {
        16 or 8 => sample,
        4 => sample * 17,
        2 => sample * 85,
        1 => sample * 255,
        _ => sample,
    };

    private static int Grey(int sample, int bitDepth, int[]? transparent)
    {
        int value = Scale(sample, bitDepth);
        int alpha = transparent is not null && transparent[0] == sample ? 0 : 255;
        return PixelBlend.Pack(alpha, value, value, value);
    }

    private static int GreyAlpha(ReadOnlySpan<int> samples, int bitDepth)
    {
        int value = Scale(samples[0], bitDepth);
        int alpha = Scale(samples[1], bitDepth);
        return PixelBlend.Pack(alpha, value, value, value);
    }

    private static int Truecolor(ReadOnlySpan<int> samples, int bitDepth, int[]? transparent)
    {
        bool clear = transparent is not null &&
                     transparent[0] == samples[0] &&
                     transparent[1] == samples[1] &&
                     transparent[2] == samples[2];

        return PixelBlend.Pack(
            clear ? 0 : 255,
            Scale(samples[0], bitDepth),
            Scale(samples[1], bitDepth),
            Scale(samples[2], bitDepth));
    }

    private static int TruecolorAlpha(ReadOnlySpan<int> samples, int bitDepth) =>
        PixelBlend.Pack(
            Scale(samples[3], bitDepth),
            Scale(samples[0], bitDepth),
            Scale(samples[1], bitDepth),
            Scale(samples[2], bitDepth));

    private static int Indexed(int index, byte[]? palette, byte[]? paletteAlpha)
    {
        if (palette is null) throw new ImageFormatException("Indexed PNG has no palette.");

        int offset = index * 3;
        if (offset + 2 >= palette.Length) return 0;

        int alpha = paletteAlpha is not null && index < paletteAlpha.Length ? paletteAlpha[index] : 255;
        return PixelBlend.Pack(alpha, palette[offset], palette[offset + 1], palette[offset + 2]);
    }

    private static byte[] Inflate(MemoryStream compressed)
    {
        compressed.Position = 0;
        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflater.CopyTo(output);
        return output.ToArray();
    }

    private static int ChannelCount(int colorType) => colorType switch
    {
        0 => 1,
        2 => 3,
        3 => 1,
        4 => 2,
        6 => 4,
        _ => throw new ImageFormatException($"PNG uses unknown colour type {colorType}."),
    };

    private static int CeilDiv(int value, int divisor) => value <= 0 ? 0 : ((value + divisor - 1) / divisor);

    private static bool Is(ReadOnlySpan<byte> type, string name) =>
        type[0] == name[0] && type[1] == name[1] && type[2] == name[2] && type[3] == name[3];
}
