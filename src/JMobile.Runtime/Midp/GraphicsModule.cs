using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Core.Native;
using JMobile.Runtime.Graphics;

namespace JMobile.Runtime.Midp;

/// <summary>
/// <c>javax.microedition.lcdui.Graphics</c>, <c>Image</c> and <c>Font</c>.
/// </summary>
/// <remarks>
/// This is the surface a game spends nearly all its drawing time against, so every method here
/// resolves to a direct call into the software rasteriser with no intermediate allocation. The
/// Java objects are thin: a <c>Graphics</c> holds a rasteriser context, an <c>Image</c> holds a
/// pixel buffer, a <c>Font</c> holds a glyph atlas.
/// </remarks>
public sealed class GraphicsModule : IRuntimeModule
{
    public string Name => "javax.microedition.lcdui.Graphics";

    public void Register(RuntimeRegistry registry)
    {
        RegisterGraphics(registry);
        RegisterImage(registry);
        RegisterFont(registry);
    }

    // ---------------------------------------------------------------------------------
    // Graphics
    // ---------------------------------------------------------------------------------

    private static void RegisterGraphics(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/Graphics", builder => builder
            .AsFinal()
            .AllocatesWith(static type => new JavaGraphics(type, type.InstanceFieldCount))

            .Constant("HCENTER", Anchor.HCenter)
            .Constant("VCENTER", Anchor.VCenter)
            .Constant("LEFT", Anchor.Left)
            .Constant("RIGHT", Anchor.Right)
            .Constant("TOP", Anchor.Top)
            .Constant("BOTTOM", Anchor.Bottom)
            .Constant("BASELINE", Anchor.Baseline)
            .Constant("SOLID", StrokeStyle.Solid)
            .Constant("DOTTED", StrokeStyle.Dotted)

            // -- state --------------------------------------------------------------------
            .Method("setColor", "(I)V", static (_, args) =>
            {
                Context(args.This).SetColor(args.Int(0));
                return JavaValue.Void;
            })
            .Method("setColor", "(III)V", static (_, args) =>
            {
                Context(args.This).SetColor(args.Int(0), args.Int(1), args.Int(2));
                return JavaValue.Void;
            })
            .Method("getColor", "()I", static (_, args) => JavaValue.Int(Context(args.This).Color & 0x00FFFFFF))
            .Method("getRedComponent", "()I",
                static (_, args) => JavaValue.Int((Context(args.This).Color >> 16) & 0xFF))
            .Method("getGreenComponent", "()I",
                static (_, args) => JavaValue.Int((Context(args.This).Color >> 8) & 0xFF))
            .Method("getBlueComponent", "()I", static (_, args) => JavaValue.Int(Context(args.This).Color & 0xFF))
            .Method("setGrayScale", "(I)V", static (_, args) =>
            {
                Context(args.This).SetGrayScale(args.Int(0));
                return JavaValue.Void;
            })
            .Method("getGrayScale", "()I", static (_, args) =>
                JavaValue.Int(PixelBlend.ToGrey(Context(args.This).Color) & 0xFF))
            .Method("setStrokeStyle", "(I)V", static (_, args) =>
            {
                Context(args.This).Stroke = args.Int(0);
                return JavaValue.Void;
            })
            .Method("getStrokeStyle", "()I", static (_, args) => JavaValue.Int(Context(args.This).Stroke))
            .Method("translate", "(II)V", static (_, args) =>
            {
                Context(args.This).Translate(args.Int(0), args.Int(1));
                return JavaValue.Void;
            })
            .Method("getTranslateX", "()I", static (_, args) => JavaValue.Int(Context(args.This).TranslateX))
            .Method("getTranslateY", "()I", static (_, args) => JavaValue.Int(Context(args.This).TranslateY))

            // -- clipping -----------------------------------------------------------------
            .Method("setClip", "(IIII)V", static (_, args) =>
            {
                Context(args.This).SetClip(args.Int(0), args.Int(1), args.Int(2), args.Int(3));
                return JavaValue.Void;
            })
            .Method("clipRect", "(IIII)V", static (_, args) =>
            {
                Context(args.This).ClipRect(args.Int(0), args.Int(1), args.Int(2), args.Int(3));
                return JavaValue.Void;
            })
            .Method("getClipX", "()I", static (_, args) => JavaValue.Int(Context(args.This).ClipX))
            .Method("getClipY", "()I", static (_, args) => JavaValue.Int(Context(args.This).ClipY))
            .Method("getClipWidth", "()I", static (_, args) => JavaValue.Int(Context(args.This).ClipWidth))
            .Method("getClipHeight", "()I", static (_, args) => JavaValue.Int(Context(args.This).ClipHeight))

            // -- primitives ---------------------------------------------------------------
            .Method("drawLine", "(IIII)V", static (_, args) =>
            {
                Context(args.This).DrawLine(args.Int(0), args.Int(1), args.Int(2), args.Int(3));
                return JavaValue.Void;
            })
            .Method("fillRect", "(IIII)V", static (_, args) =>
            {
                Context(args.This).FillRect(args.Int(0), args.Int(1), args.Int(2), args.Int(3));
                return JavaValue.Void;
            })
            .Method("drawRect", "(IIII)V", static (_, args) =>
            {
                Context(args.This).DrawRect(args.Int(0), args.Int(1), args.Int(2), args.Int(3));
                return JavaValue.Void;
            })
            .Method("fillRoundRect", "(IIIIII)V", static (_, args) =>
            {
                Context(args.This).FillRoundRect(
                    args.Int(0), args.Int(1), args.Int(2), args.Int(3), args.Int(4), args.Int(5));
                return JavaValue.Void;
            })
            .Method("drawRoundRect", "(IIIIII)V", static (_, args) =>
            {
                Context(args.This).DrawRoundRect(
                    args.Int(0), args.Int(1), args.Int(2), args.Int(3), args.Int(4), args.Int(5));
                return JavaValue.Void;
            })
            .Method("fillArc", "(IIIIII)V", static (_, args) =>
            {
                Context(args.This).FillArc(
                    args.Int(0), args.Int(1), args.Int(2), args.Int(3), args.Int(4), args.Int(5));
                return JavaValue.Void;
            })
            .Method("drawArc", "(IIIIII)V", static (_, args) =>
            {
                Context(args.This).DrawArc(
                    args.Int(0), args.Int(1), args.Int(2), args.Int(3), args.Int(4), args.Int(5));
                return JavaValue.Void;
            })
            .Method("fillTriangle", "(IIIIII)V", static (_, args) =>
            {
                Context(args.This).FillTriangle(
                    args.Int(0), args.Int(1), args.Int(2), args.Int(3), args.Int(4), args.Int(5));
                return JavaValue.Void;
            })

            // -- text ---------------------------------------------------------------------
            .Method("setFont", "(Ljavax/microedition/lcdui/Font;)V", static (thread, args) =>
            {
                Context(args.This).Font = args.Object(0) is JavaFont font
                    ? font.Bitmap
                    : MidpRuntime.Of(thread.Vm).GetFont(0, 0, 0);
                return JavaValue.Void;
            })
            .Method("getFont", "()Ljavax/microedition/lcdui/Font;",
                static (thread, args) => JavaValue.Ref(FontModuleSupport.Wrap(thread, Context(args.This).Font)))
            .Method("drawString", "(Ljava/lang/String;III)V", static (thread, args) =>
            {
                EnsureFont(thread, args.This);
                Context(args.This).DrawString(args.String(0) ?? string.Empty,
                    args.Int(1), args.Int(2), args.Int(3));
                return JavaValue.Void;
            })
            .Method("drawSubstring", "(Ljava/lang/String;IIIII)V", static (thread, args) =>
            {
                EnsureFont(thread, args.This);
                string text = args.String(0) ?? string.Empty;
                int offset = args.Int(1), length = args.Int(2);
                RuntimeSupport.CheckRegion(thread, offset, length, text.Length);
                Context(args.This).DrawString(text.AsSpan(offset, length),
                    args.Int(3), args.Int(4), args.Int(5));
                return JavaValue.Void;
            })
            .Method("drawChar", "(CIII)V", static (thread, args) =>
            {
                EnsureFont(thread, args.This);
                Context(args.This).DrawChar(args.Char(0), args.Int(1), args.Int(2), args.Int(3));
                return JavaValue.Void;
            })
            .Method("drawChars", "([CIIIII)V", static (thread, args) =>
            {
                EnsureFont(thread, args.This);
                char[] source = args.Array<char>(0)
                                ?? throw thread.Vm.NewNullPointerException(thread, "data");
                int offset = args.Int(1), length = args.Int(2);
                RuntimeSupport.CheckRegion(thread, offset, length, source.Length);
                Context(args.This).DrawString(source.AsSpan(offset, length),
                    args.Int(3), args.Int(4), args.Int(5));
                return JavaValue.Void;
            })

            // -- images -------------------------------------------------------------------
            .Method("drawImage", "(Ljavax/microedition/lcdui/Image;III)V", static (thread, args) =>
            {
                var image = args.Object<JavaImage>(0)
                            ?? throw thread.Vm.NewNullPointerException(thread, "img");
                Context(args.This).DrawImage(image.Surface, args.Int(1), args.Int(2), args.Int(3));
                return JavaValue.Void;
            })
            .Method("drawRegion", "(Ljavax/microedition/lcdui/Image;IIIIIIII)V", static (thread, args) =>
            {
                var image = args.Object<JavaImage>(0)
                            ?? throw thread.Vm.NewNullPointerException(thread, "src");
                Context(args.This).DrawRegion(
                    image.Surface,
                    args.Int(1), args.Int(2), args.Int(3), args.Int(4),
                    args.Int(5),
                    args.Int(6), args.Int(7), args.Int(8));
                return JavaValue.Void;
            })
            .Method("copyArea", "(IIIIIII)V", static (_, args) =>
            {
                Context(args.This).CopyArea(
                    args.Int(0), args.Int(1), args.Int(2), args.Int(3),
                    args.Int(4), args.Int(5), args.Int(6));
                return JavaValue.Void;
            })
            .Method("drawRGB", "([IIIIIIIZ)V", static (thread, args) =>
            {
                int[] source = args.Array<int>(0)
                               ?? throw thread.Vm.NewNullPointerException(thread, "rgbData");
                Context(args.This).DrawRgb(
                    source, args.Int(1), args.Int(2),
                    args.Int(3), args.Int(4), args.Int(5), args.Int(6),
                    args.Bool(7));
                return JavaValue.Void;
            }));
    }

    private static GraphicsContext Context(JavaObject graphics) => ((JavaGraphics)graphics).Context;

    /// <summary>Gives a context a default font the first time text is drawn through it.</summary>
    private static void EnsureFont(JavaThread thread, JavaObject graphics)
    {
        GraphicsContext context = Context(graphics);
        context.Font ??= MidpRuntime.Of(thread.Vm).GetFont(0, 0, 0);
    }

    // ---------------------------------------------------------------------------------
    // Image
    // ---------------------------------------------------------------------------------

    private static void RegisterImage(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/Image", builder => builder
            .AsFinal()
            .AllocatesWith(static type => new JavaImage(type, type.InstanceFieldCount))
            .Method("getWidth", "()I", static (_, args) => JavaValue.Int(args.Self<JavaImage>().Width))
            .Method("getHeight", "()I", static (_, args) => JavaValue.Int(args.Self<JavaImage>().Height))
            .Method("isMutable", "()Z", static (_, args) => JavaValue.Bool(args.Self<JavaImage>().IsMutable))
            .Method("getGraphics", "()Ljavax/microedition/lcdui/Graphics;", static (thread, args) =>
            {
                var image = args.Self<JavaImage>();
                if (!image.IsMutable)
                    throw thread.Vm.NewIllegalStateException(thread, "image is immutable");

                return JavaValue.Ref(NewGraphics(thread, new GraphicsContext(image.Surface), isScreen: false));
            })
            .Method("getRGB", "([IIIIIII)V", static (thread, args) =>
            {
                int[] destination = args.Array<int>(0)
                                    ?? throw thread.Vm.NewNullPointerException(thread, "rgbData");
                CopyOut(thread, args.Self<JavaImage>().Surface, destination,
                    args.Int(1), args.Int(2), args.Int(3), args.Int(4), args.Int(5), args.Int(6));
                return JavaValue.Void;
            })

            .StaticMethod("createImage", "(II)Ljavax/microedition/lcdui/Image;", static (thread, args) =>
            {
                int width = args.Int(0), height = args.Int(1);
                if (width <= 0 || height <= 0)
                    throw thread.Vm.NewIllegalArgumentException(thread, "image dimensions must be positive");

                var surface = new Surface(width, height, hasAlpha: false);
                // A newly created mutable image starts white, as the specification requires.
                surface.Clear(unchecked((int)0xFFFFFFFF));
                return JavaValue.Ref(NewImage(thread, surface, mutable: true));
            })
            .StaticMethod("createImage", "([BII)Ljavax/microedition/lcdui/Image;", static (thread, args) =>
            {
                var source = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "imageData");
                RuntimeSupport.CheckRegion(thread, args.Int(1), args.Int(2), source.Length);

                byte[] data = RuntimeSupport.ToHostBytes(source, args.Int(1), args.Int(2));
                return JavaValue.Ref(NewImage(thread, Decode(thread, data), mutable: false));
            })
            .StaticMethod("createImage", "(Ljava/lang/String;)Ljavax/microedition/lcdui/Image;",
                static (thread, args) =>
                {
                    string path = RuntimeSupport.RequireString(thread, args.String(0), "name");
                    byte[]? data = thread.Vm.GetResource(path);
                    if (data is null)
                        throw thread.Vm.NewIOException(thread, "resource not found: " + path);

                    return JavaValue.Ref(NewImage(thread, Decode(thread, data), mutable: false));
                })
            .StaticMethod("createImage",
                "(Ljavax/microedition/lcdui/Image;)Ljavax/microedition/lcdui/Image;",
                static (thread, args) =>
                {
                    var source = args.Object<JavaImage>(0)
                                 ?? throw thread.Vm.NewNullPointerException(thread, "source");
                    return JavaValue.Ref(NewImage(thread, source.Surface.Clone(), mutable: false));
                })
            .StaticMethod("createImage",
                "(Ljavax/microedition/lcdui/Image;IIIII)Ljavax/microedition/lcdui/Image;",
                static (thread, args) =>
                {
                    var source = args.Object<JavaImage>(0)
                                 ?? throw thread.Vm.NewNullPointerException(thread, "image");

                    int x = args.Int(1), y = args.Int(2), width = args.Int(3), height = args.Int(4);
                    int transform = args.Int(5);
                    if (width <= 0 || height <= 0)
                        throw thread.Vm.NewIllegalArgumentException(thread, "region must be non-empty");

                    bool swaps = SpriteTransform.SwapsAxes(transform);
                    var extracted = new Surface(swaps ? height : width, swaps ? width : height);

                    var context = new GraphicsContext(extracted);
                    context.Blit(source.Surface, x, y, width, height, 0, 0, transform);
                    extracted.DetectAlpha();

                    return JavaValue.Ref(NewImage(thread, extracted, mutable: false));
                })
            .StaticMethod("createRGBImage", "([IIIZ)Ljavax/microedition/lcdui/Image;", static (thread, args) =>
            {
                int[] source = args.Array<int>(0)
                               ?? throw thread.Vm.NewNullPointerException(thread, "rgb");
                int width = args.Int(1), height = args.Int(2);
                bool processAlpha = args.Bool(3);

                if (width <= 0 || height <= 0 || source.Length < width * height)
                    throw thread.Vm.NewIllegalArgumentException(thread, "rgb array is too small");

                var surface = new Surface(width, height);
                if (processAlpha)
                {
                    source.AsSpan(0, width * height).CopyTo(surface.Pixels);
                    surface.DetectAlpha();
                }
                else
                {
                    for (int i = 0; i < width * height; i++) surface.Pixels[i] = PixelBlend.Opaque(source[i]);
                    surface.HasAlpha = false;
                }

                return JavaValue.Ref(NewImage(thread, surface, mutable: false));
            }));
    }

    private static Surface Decode(JavaThread thread, byte[] data)
    {
        try
        {
            return PngDecoder.Decode(data);
        }
        catch (ImageFormatException error)
        {
            throw thread.Vm.NewIllegalArgumentException(thread, "unsupported image data: " + error.Message);
        }
    }

    private static void CopyOut(
        JavaThread thread,
        Surface surface,
        int[] destination,
        int offset,
        int scanLength,
        int x,
        int y,
        int width,
        int height)
    {
        if (width <= 0 || height <= 0) return;

        if (x < 0 || y < 0 || x + width > surface.Width || y + height > surface.Height)
            throw thread.Vm.NewIllegalArgumentException(thread, "region is outside the image");

        for (int row = 0; row < height; row++)
        {
            int sourceIndex = ((y + row) * surface.Width) + x;
            int destinationIndex = offset + (row * scanLength);

            if (destinationIndex < 0 || destinationIndex + width > destination.Length)
                throw thread.Vm.NewArrayIndexOutOfBoundsException(thread, destinationIndex, destination.Length);

            surface.Pixels.AsSpan(sourceIndex, width).CopyTo(destination.AsSpan(destinationIndex));
        }
    }

    /// <summary>Wraps a surface as a Java <c>Image</c>.</summary>
    public static JavaObject NewImage(JavaThread thread, Surface surface, bool mutable)
    {
        JavaClass type = thread.Vm.LoadClass("javax/microedition/lcdui/Image");
        var image = (JavaImage)thread.Vm.NewInstance(type);
        image.Surface = surface;
        image.IsMutable = mutable;
        return image;
    }

    /// <summary>Wraps a rasteriser context as a Java <c>Graphics</c>.</summary>
    public static JavaObject NewGraphics(JavaThread thread, GraphicsContext context, bool isScreen)
    {
        JavaClass type = thread.Vm.LoadClass("javax/microedition/lcdui/Graphics");
        var graphics = (JavaGraphics)thread.Vm.NewInstance(type);
        graphics.Context = context;
        graphics.IsScreen = isScreen;
        return graphics;
    }

    // ---------------------------------------------------------------------------------
    // Font
    // ---------------------------------------------------------------------------------

    private static void RegisterFont(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/Font", builder => builder
            .AsFinal()
            .AllocatesWith(static type => new JavaFont(type, type.InstanceFieldCount))
            .Constant("FACE_SYSTEM", 0)
            .Constant("FACE_MONOSPACE", 32)
            .Constant("FACE_PROPORTIONAL", 64)
            .Constant("STYLE_PLAIN", 0)
            .Constant("STYLE_BOLD", 1)
            .Constant("STYLE_ITALIC", 2)
            .Constant("STYLE_UNDERLINED", 4)
            .Constant("SIZE_SMALL", 8)
            .Constant("SIZE_MEDIUM", 0)
            .Constant("SIZE_LARGE", 16)
            .Constant("FONT_STATIC_TEXT", 0)
            .Constant("FONT_INPUT_TEXT", 1)
            .Method("getHeight", "()I", static (_, args) => JavaValue.Int(args.Self<JavaFont>().Bitmap.Height))
            .Method("getBaselinePosition", "()I",
                static (_, args) => JavaValue.Int(args.Self<JavaFont>().Bitmap.Ascent))
            .Method("getFace", "()I", static (_, args) => JavaValue.Int(args.Self<JavaFont>().Face))
            .Method("getStyle", "()I", static (_, args) => JavaValue.Int(args.Self<JavaFont>().Style))
            .Method("getSize", "()I", static (_, args) => JavaValue.Int(args.Self<JavaFont>().Size))
            .Method("isBold", "()Z", static (_, args) => JavaValue.Bool((args.Self<JavaFont>().Style & 1) != 0))
            .Method("isItalic", "()Z", static (_, args) => JavaValue.Bool((args.Self<JavaFont>().Style & 2) != 0))
            .Method("isUnderlined", "()Z",
                static (_, args) => JavaValue.Bool((args.Self<JavaFont>().Style & 4) != 0))
            .Method("isPlain", "()Z", static (_, args) => JavaValue.Bool(args.Self<JavaFont>().Style == 0))
            .Method("charWidth", "(C)I",
                static (_, args) => JavaValue.Int(args.Self<JavaFont>().Bitmap.CharWidth(args.Char(0))))
            .Method("stringWidth", "(Ljava/lang/String;)I", static (_, args) =>
                JavaValue.Int(args.Self<JavaFont>().Bitmap.StringWidth(args.String(0) ?? string.Empty)))
            .Method("substringWidth", "(Ljava/lang/String;II)I", static (thread, args) =>
            {
                string text = args.String(0) ?? string.Empty;
                RuntimeSupport.CheckRegion(thread, args.Int(1), args.Int(2), text.Length);
                return JavaValue.Int(args.Self<JavaFont>().Bitmap
                    .StringWidth(text.AsSpan(args.Int(1), args.Int(2))));
            })
            .Method("charsWidth", "([CII)I", static (thread, args) =>
            {
                char[] source = args.Array<char>(0)
                                ?? throw thread.Vm.NewNullPointerException(thread, "ch");
                RuntimeSupport.CheckRegion(thread, args.Int(1), args.Int(2), source.Length);
                return JavaValue.Int(args.Self<JavaFont>().Bitmap
                    .StringWidth(source.AsSpan(args.Int(1), args.Int(2))));
            })
            .StaticMethod("getDefaultFont", "()Ljavax/microedition/lcdui/Font;", static (thread, _) =>
                JavaValue.Ref(FontModuleSupport.Create(thread, 0, 0, 0)))
            .StaticMethod("getFont", "(III)Ljavax/microedition/lcdui/Font;", static (thread, args) =>
                JavaValue.Ref(FontModuleSupport.Create(thread, args.Int(0), args.Int(1), args.Int(2))))
            .StaticMethod("getFont", "(I)Ljavax/microedition/lcdui/Font;", static (thread, _) =>
                JavaValue.Ref(FontModuleSupport.Create(thread, 0, 0, 0))));
    }
}

/// <summary>Creation and caching of the Java-visible <c>Font</c> objects.</summary>
internal static class FontModuleSupport
{
    /// <summary>Builds, or reuses, the <c>Font</c> object for a face, style and size.</summary>
    public static JavaObject Create(JavaThread thread, int face, int style, int size)
    {
        MidpRuntime runtime = MidpRuntime.Of(thread.Vm);
        Graphics.BitmapFont bitmap = runtime.GetFont(face, style, size);

        JavaClass type = thread.Vm.LoadClass("javax/microedition/lcdui/Font");
        var font = (JavaFont)thread.Vm.NewInstance(type);
        font.Bitmap = bitmap;
        font.Face = face;
        font.Style = style;
        font.Size = size;
        return font;
    }

    /// <summary>Wraps an already-rasterised font, for <c>Graphics.getFont</c>.</summary>
    public static JavaObject Wrap(JavaThread thread, Graphics.BitmapFont? bitmap)
    {
        if (bitmap is null) return Create(thread, 0, 0, 0);

        JavaClass type = thread.Vm.LoadClass("javax/microedition/lcdui/Font");
        var font = (JavaFont)thread.Vm.NewInstance(type);
        font.Bitmap = bitmap;
        font.Face = bitmap.Face;
        font.Style = bitmap.Style;
        font.Size = bitmap.Size;
        return font;
    }
}
