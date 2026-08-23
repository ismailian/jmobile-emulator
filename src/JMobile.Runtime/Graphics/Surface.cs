using System.Runtime.CompilerServices;

namespace JMobile.Runtime.Graphics;

/// <summary>
/// A mutable pixel buffer in 32-bit ARGB, the single representation used for the phone screen,
/// for every <c>Image</c> a game creates, and for decoded PNG resources.
/// </summary>
/// <remarks>
/// Pixels are a plain <c>int[]</c> rather than an opaque handle. That is what lets the desktop
/// shell pin this array once and hand its address straight to GDI+ as a bitmap, so presenting a
/// frame copies nothing: the rasteriser writes into exactly the memory the window blits from.
/// </remarks>
public sealed class Surface
{
    public Surface(int width, int height, bool hasAlpha = true)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Surface dimensions must be positive.");

        Width = width;
        Height = height;
        HasAlpha = hasAlpha;
        Pixels = new int[width * height];
    }

    public Surface(int width, int height, int[] pixels, bool hasAlpha = true)
    {
        if (pixels.Length < width * height)
            throw new ArgumentException("Pixel buffer is smaller than the declared dimensions.", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
        HasAlpha = hasAlpha;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Packed 0xAARRGGBB pixels, row-major with no padding between rows.</summary>
    public int[] Pixels { get; }

    /// <summary>
    /// False when every pixel is known opaque, which lets blits skip per-pixel blending.
    /// </summary>
    public bool HasAlpha { get; set; }

    /// <summary>True for the buffer behind a mutable <c>Image</c>, which games may draw into.</summary>
    public bool IsMutable { get; init; } = true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPixel(int x, int y) => Pixels[(y * Width) + x];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPixel(int x, int y, int argb) => Pixels[(y * Width) + x] = argb;

    /// <summary>Fills the whole surface with one colour.</summary>
    public void Clear(int argb) => Array.Fill(Pixels, argb);

    /// <summary>One row of pixels, for row-at-a-time operations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<int> Row(int y) => Pixels.AsSpan(y * Width, Width);

    /// <summary>Creates an independent copy, as <c>Image.createImage(Image)</c> requires.</summary>
    public Surface Clone()
    {
        var copy = new Surface(Width, Height, (int[])Pixels.Clone(), HasAlpha);
        return copy;
    }

    /// <summary>Reports whether any pixel is translucent, so blits can take the opaque path.</summary>
    public bool DetectAlpha()
    {
        int[] pixels = Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            if ((pixels[i] & unchecked((int)0xFF000000)) != unchecked((int)0xFF000000))
            {
                HasAlpha = true;
                return true;
            }
        }

        HasAlpha = false;
        return false;
    }
}
