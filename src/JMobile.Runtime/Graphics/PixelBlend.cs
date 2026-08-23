using System.Runtime.CompilerServices;

namespace JMobile.Runtime.Graphics;

/// <summary>Pixel-level colour arithmetic shared by every drawing operation.</summary>
public static class PixelBlend
{
    public const int OpaqueMask = unchecked((int)0xFF000000);

    /// <summary>
    /// Composites a translucent source pixel over an opaque destination.
    /// </summary>
    /// <remarks>
    /// The two colour halves are blended in parallel: red and blue occupy alternate bytes and
    /// cannot overflow into one another, so one multiply handles both. The alpha is first
    /// rescaled from 0..255 to 0..256 so the divide becomes a shift without the systematic
    /// darkening a plain <c>&gt;&gt; 8</c> would introduce.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Over(int source, int destination)
    {
        int alpha = (int)((uint)source >> 24);
        if (alpha == 0xFF) return source;
        if (alpha == 0) return destination;

        alpha += alpha >> 7;
        int inverse = 256 - alpha;

        int redBlue = ((((source & 0x00FF00FF) * alpha) + ((destination & 0x00FF00FF) * inverse)) >> 8)
                      & 0x00FF00FF;
        int green = ((((source & 0x0000FF00) * alpha) + ((destination & 0x0000FF00) * inverse)) >> 8)
                    & 0x0000FF00;

        return OpaqueMask | redBlue | green;
    }

    /// <summary>Multiplies a colour by a constant opacity, used for translucent sprite draws.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ScaleAlpha(int argb, int opacity)
    {
        int alpha = (int)((uint)argb >> 24) * opacity / 255;
        return (alpha << 24) | (argb & 0x00FFFFFF);
    }

    /// <summary>Forces a colour opaque, which is what <c>Graphics.setColor</c> produces.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Opaque(int rgb) => OpaqueMask | (rgb & 0x00FFFFFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Pack(int red, int green, int blue) =>
        OpaqueMask | ((red & 0xFF) << 16) | ((green & 0xFF) << 8) | (blue & 0xFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Pack(int alpha, int red, int green, int blue) =>
        ((alpha & 0xFF) << 24) | ((red & 0xFF) << 16) | ((green & 0xFF) << 8) | (blue & 0xFF);

    /// <summary>
    /// Reduces a colour to what a given display could actually show, so that a game checking
    /// <c>Display.numColors</c> and a game trusting its own dithering both look right.
    /// </summary>
    public static int Quantise(int argb, int bitsPerChannel)
    {
        if (bitsPerChannel >= 8) return argb;

        int shift = 8 - bitsPerChannel;
        int max = (1 << bitsPerChannel) - 1;

        int red = ((argb >> 16) & 0xFF) >> shift;
        int green = ((argb >> 8) & 0xFF) >> shift;
        int blue = (argb & 0xFF) >> shift;

        // Rescale back across the full range so that the top level stays pure white.
        red = red * 255 / max;
        green = green * 255 / max;
        blue = blue * 255 / max;

        return (argb & OpaqueMask) | (red << 16) | (green << 8) | blue;
    }

    /// <summary>Converts a colour to greyscale using the luma weights CLDC devices used.</summary>
    public static int ToGrey(int argb)
    {
        int red = (argb >> 16) & 0xFF;
        int green = (argb >> 8) & 0xFF;
        int blue = argb & 0xFF;
        int grey = ((red * 77) + (green * 151) + (blue * 28)) >> 8;
        return (argb & OpaqueMask) | (grey << 16) | (grey << 8) | grey;
    }
}
