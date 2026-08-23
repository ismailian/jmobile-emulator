namespace JMobile.Runtime.Graphics;

/// <summary>
/// Anchor bits from <c>javax.microedition.lcdui.Graphics</c>. A draw call combines one
/// horizontal and one vertical bit to say which point of the drawn thing lands on the
/// coordinates given.
/// </summary>
public static class Anchor
{
    public const int HCenter = 1;
    public const int VCenter = 2;
    public const int Left = 4;
    public const int Right = 8;
    public const int Top = 16;
    public const int Bottom = 32;
    public const int Baseline = 64;

    /// <summary>Resolves the horizontal anchor into the left edge of the drawn content.</summary>
    public static int ResolveX(int x, int width, int anchor)
    {
        if ((anchor & Right) != 0) return x - width;
        if ((anchor & HCenter) != 0) return x - (width / 2);
        return x;
    }

    /// <summary>Resolves the vertical anchor into the top edge of the drawn content.</summary>
    public static int ResolveY(int y, int height, int anchor, int ascent = 0)
    {
        if ((anchor & Bottom) != 0) return y - height;
        if ((anchor & VCenter) != 0) return y - (height / 2);
        if ((anchor & Baseline) != 0) return y - ascent;
        return y;
    }
}

/// <summary>
/// The eight rigid transforms <c>Sprite</c> and <c>Graphics.drawRegion</c> support: the four
/// rotations, each optionally mirrored.
/// </summary>
public static class SpriteTransform
{
    public const int None = 0;
    public const int MirrorRotate180 = 1;
    public const int Mirror = 2;
    public const int Rotate180 = 3;
    public const int MirrorRotate270 = 4;
    public const int Rotate90 = 5;
    public const int Rotate270 = 6;
    public const int MirrorRotate90 = 7;

    /// <summary>True when the transform exchanges the width and height of the region.</summary>
    public static bool SwapsAxes(int transform) => transform switch
    {
        Rotate90 or Rotate270 or MirrorRotate90 or MirrorRotate270 => true,
        _ => false,
    };
}

/// <summary>Stroke styles for <c>Graphics.setStrokeStyle</c>.</summary>
public static class StrokeStyle
{
    public const int Solid = 0;
    public const int Dotted = 1;
}
