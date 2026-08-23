using JMobile.Core;
using JMobile.Core.Model;
using JMobile.Core.Native;
using JMobile.Runtime.Graphics;

namespace JMobile.Runtime.Midp;

/// <summary>
/// Vendor extension APIs, principally Nokia's.
/// </summary>
/// <remarks>
/// A large share of J2ME games were built for Nokia handsets and call into
/// <c>com.nokia.mid.ui</c> directly — most often <c>FullCanvas</c> for a borderless screen and
/// <c>DirectGraphics</c> for the drawing operations MIDP 1.0 lacked. Without these classes those
/// games fail at class loading with <c>NoClassDefFoundError</c> before showing anything at all,
/// so supporting them is the difference between a large catalogue running and not running.
/// </remarks>
public sealed class VendorModule : IRuntimeModule
{
    public string Name => "vendor extensions";

    public void Register(RuntimeRegistry registry)
    {
        RegisterNokiaUi(registry);
        RegisterNokiaDeviceControl(registry);
        RegisterNokiaSound(registry);
    }

    private static void RegisterNokiaUi(RuntimeRegistry registry)
    {
        // FullCanvas is a Canvas that was always full-screen. Since this emulator has no
        // system-drawn chrome over a canvas, it needs nothing beyond the key constants.
        registry.Define("com/nokia/mid/ui/FullCanvas", builder => builder
            .Extends("javax/microedition/lcdui/Canvas")
            .AsAbstract()
            .Constant("KEY_UP_ARROW", -1)
            .Constant("KEY_DOWN_ARROW", -2)
            .Constant("KEY_LEFT_ARROW", -3)
            .Constant("KEY_RIGHT_ARROW", -4)
            .Constant("KEY_SOFTKEY1", -6)
            .Constant("KEY_SOFTKEY2", -7)
            .Constant("KEY_SOFTKEY3", -5)
            .Constant("KEY_SEND", -10)
            .Constant("KEY_END", -11)
            .Constructor("()V", static (_, _) => JavaValue.Void));

        registry.DefineInterface("com/nokia/mid/ui/DirectGraphics", builder => builder
            .Constant("TYPE_BYTE_1_GRAY", 1)
            .Constant("TYPE_USHORT_4444_ARGB", 4444)
            .Constant("TYPE_INT_888_RGB", 888)
            .Constant("TYPE_INT_8888_ARGB", 8888)
            .Constant("FLIP_HORIZONTAL", 8192)
            .Constant("FLIP_VERTICAL", 16384)
            .InterfaceMethod("setARGBColor", "(I)V")
            .InterfaceMethod("getAlphaComponent", "()I")
            .InterfaceMethod("getNativePixelFormat", "()I")
            .InterfaceMethod("fillPolygon", "([II[IIII)V")
            .InterfaceMethod("drawPolygon", "([II[IIII)V")
            .InterfaceMethod("fillTriangle", "(IIIIII)V")
            .InterfaceMethod("drawTriangle", "(IIIIII)V")
            .InterfaceMethod("drawImage", "(Ljavax/microedition/lcdui/Image;IIII)V")
            .InterfaceMethod("drawPixels", "([IZIIIIIIII)V")
            .InterfaceMethod("drawPixels", "([SZIIIIIIII)V")
            .InterfaceMethod("drawPixels", "([B[BIIIIIIII)V")
            .InterfaceMethod("getPixels", "([IIIIIIII)V")
            .InterfaceMethod("getPixels", "([SIIIIIIII)V"));

        registry.Define("com/nokia/mid/ui/DirectUtils", builder => builder
            .StaticMethod("getDirectGraphics",
                "(Ljavax/microedition/lcdui/Graphics;)Lcom/nokia/mid/ui/DirectGraphics;",
                static (thread, args) =>
                {
                    JavaObject wrapper = thread.Vm.NewInstance(
                        thread.Vm.LoadClass("com/nokia/mid/ui/DirectGraphicsImpl"));
                    wrapper.SetField(0, JavaValue.Ref(args.Object(0)));
                    return JavaValue.Ref(wrapper);
                })
            .StaticMethod("createImage", "(III)Ljavax/microedition/lcdui/Image;", static (thread, args) =>
            {
                int width = args.Int(0), height = args.Int(1);
                if (width <= 0 || height <= 0)
                    throw thread.Vm.NewIllegalArgumentException(thread, "image dimensions must be positive");

                // Unlike Image.createImage, this one takes an explicit fill colour with alpha.
                var surface = new Surface(width, height);
                surface.Clear(args.Int(2));
                surface.DetectAlpha();
                return JavaValue.Ref(GraphicsModule.NewImage(thread, surface, mutable: true));
            })
            .StaticMethod("createImage", "([BII)Ljavax/microedition/lcdui/Image;", static (thread, args) =>
            {
                var source = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "imageData");
                byte[] data = RuntimeSupport.ToHostBytes(source, args.Int(1), args.Int(2));

                try
                {
                    return JavaValue.Ref(GraphicsModule.NewImage(thread, PngDecoder.Decode(data), mutable: false));
                }
                catch (ImageFormatException error)
                {
                    throw thread.Vm.NewIllegalArgumentException(thread, error.Message);
                }
            }));

        registry.Define("com/nokia/mid/ui/DirectGraphicsImpl", builder => builder
            .Implements("com/nokia/mid/ui/DirectGraphics")
            .Field("graphics", "Ljavax/microedition/lcdui/Graphics;")
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Method("setARGBColor", "(I)V", static (_, args) =>
            {
                if (args.This.GetField(0).Reference is JavaGraphics graphics)
                    graphics.Context.SetColor(args.Int(0));
                return JavaValue.Void;
            })
            .Method("fillTriangle", "(IIIIII)V", static (_, args) =>
            {
                if (args.This.GetField(0).Reference is JavaGraphics graphics)
                {
                    graphics.Context.FillTriangle(
                        args.Int(0), args.Int(1), args.Int(2), args.Int(3), args.Int(4), args.Int(5));
                }
                return JavaValue.Void;
            })
            .Method("fillPolygon", "([II[IIII)V", static (thread, args) =>
            {
                FillPolygon(thread, args.This, args.Array<int>(0), args.Int(1),
                    args.Array<int>(2), args.Int(3), args.Int(4), args.Int(5));
                return JavaValue.Void;
            })
            .Method("drawPolygon", "([II[IIII)V", static (thread, args) =>
            {
                DrawPolygon(thread, args.This, args.Array<int>(0), args.Int(1),
                    args.Array<int>(2), args.Int(3), args.Int(4), args.Int(5));
                return JavaValue.Void;
            })
            .Method("drawImage", "(Ljavax/microedition/lcdui/Image;IIII)V", static (_, args) =>
            {
                if (args.This.GetField(0).Reference is not JavaGraphics graphics) return JavaValue.Void;
                if (args.Object<JavaImage>(0) is not { } image) return JavaValue.Void;

                // The manipulation argument carries Nokia's flip flags, which map onto the
                // standard sprite transforms.
                int manipulation = args.Int(4);
                int transform = manipulation switch
                {
                    8192 => SpriteTransform.Mirror,
                    16384 => SpriteTransform.MirrorRotate180,
                    24576 => SpriteTransform.Rotate180,
                    _ => SpriteTransform.None,
                };

                graphics.Context.DrawRegion(image.Surface, 0, 0, image.Width, image.Height,
                    transform, args.Int(1), args.Int(2), args.Int(3));
                return JavaValue.Void;
            })

            // drawPixels is how a great many Nokia-targeted games get anything on screen at all:
            // they keep their art as raw pixel arrays and blit it directly, never touching Image.
            .Method("drawPixels", "([IZIIIIIIII)V", static (_, args) =>
            {
                int[]? source = args.Array<int>(0);
                if (source is null) return JavaValue.Void;

                bool transparency = args.Bool(1);
                int format = args.Int(9);

                Surface? block = ReadBlock(
                    source, args.Int(2), args.Int(3), args.Int(6), args.Int(7),
                    (pixel, _) => transparency && format == 8888 ? pixel : PixelBlend.Opaque(pixel));

                Blit(args.This, block, args.Int(4), args.Int(5), args.Int(8));
                return JavaValue.Void;
            })
            .Method("drawPixels", "([SZIIIIIIII)V", static (_, args) =>
            {
                short[]? source = args.Array<short>(0);
                if (source is null) return JavaValue.Void;

                bool transparency = args.Bool(1);
                int format = args.Int(9);

                Surface? block = ReadBlock(
                    source, args.Int(2), args.Int(3), args.Int(6), args.Int(7),
                    (pixel, _) => Expand16(pixel, format, transparency));

                Blit(args.This, block, args.Int(4), args.Int(5), args.Int(8));
                return JavaValue.Void;
            })
            .Method("drawPixels", "([B[BIIIIIIII)V", static (_, args) =>
            {
                // The byte form carries one- to eight-bit indexed or grey data with a separate
                // transparency mask. It appears almost exclusively in monochrome-era code; the
                // grey interpretation below is enough to put something recognisable on screen.
                sbyte[]? source = args.Array<sbyte>(0);
                sbyte[]? mask = args.Array<sbyte>(1);
                if (source is null) return JavaValue.Void;

                Surface? block = ReadBlock(
                    source, args.Int(2), args.Int(3), args.Int(6), args.Int(7),
                    (pixel, index) =>
                    {
                        int grey = (byte)pixel;
                        bool visible = mask is null || index >= mask.Length || mask[index] != 0;
                        return PixelBlend.Pack(visible ? 255 : 0, grey, grey, grey);
                    });

                Blit(args.This, block, args.Int(4), args.Int(5), args.Int(8));
                return JavaValue.Void;
            })
            .Method("getPixels", "([IIIIIIII)V", static (_, args) =>
            {
                int[]? destination = args.Array<int>(0);
                if (destination is null || args.This.GetField(0).Reference is not JavaGraphics graphics)
                    return JavaValue.Void;

                ReadBack(graphics, destination, args.Int(1), args.Int(2),
                    args.Int(3), args.Int(4), args.Int(5), args.Int(6));
                return JavaValue.Void;
            })
            .Method("getPixels", "([SIIIIIIII)V", static (_, args) =>
            {
                short[]? destination = args.Array<short>(0);
                if (destination is null || args.This.GetField(0).Reference is not JavaGraphics graphics)
                    return JavaValue.Void;

                int format = args.Int(7);
                var scratch = new int[destination.Length];
                ReadBack(graphics, scratch, args.Int(1), args.Int(2),
                    args.Int(3), args.Int(4), args.Int(5), args.Int(6));

                for (int i = 0; i < destination.Length; i++) destination[i] = Narrow16(scratch[i], format);
                return JavaValue.Void;
            })
            .Method("getAlphaComponent", "()I", static (_, args) =>
                JavaValue.Int(args.This.GetField(0).Reference is JavaGraphics graphics
                    ? (int)((uint)graphics.Context.Color >> 24)
                    : 255))
            .Method("getNativePixelFormat", "()I", static (_, _) => JavaValue.Int(8888)));
    }

    /// <summary>Copies a caller-supplied pixel block into a surface the rasteriser can blit.</summary>
    private static Surface? ReadBlock<T>(
        T[] source,
        int offset,
        int scanLength,
        int width,
        int height,
        Func<T, int, int> convert)
    {
        if (width <= 0 || height <= 0) return null;

        var block = new Surface(width, height);
        for (int row = 0; row < height; row++)
        {
            // The scan length may be negative, which is how these APIs express a vertical flip.
            int rowStart = offset + (row * scanLength);

            for (int column = 0; column < width; column++)
            {
                int index = rowStart + column;
                if ((uint)index >= (uint)source.Length) continue;

                block.Pixels[(row * width) + column] = convert(source[index], index);
            }
        }

        block.DetectAlpha();
        return block;
    }

    private static void Blit(JavaObject self, Surface? block, int x, int y, int manipulation)
    {
        if (block is null || self.GetField(0).Reference is not JavaGraphics graphics) return;

        graphics.Context.Blit(block, 0, 0, block.Width, block.Height, x, y, ToTransform(manipulation));
    }

    /// <summary>Reads pixels back out of the drawing target.</summary>
    private static void ReadBack(
        JavaGraphics graphics,
        int[] destination,
        int offset,
        int scanLength,
        int x,
        int y,
        int width,
        int height)
    {
        Surface target = graphics.Context.Target;

        for (int row = 0; row < height; row++)
        {
            int sourceY = y + row;
            if ((uint)sourceY >= (uint)target.Height) continue;

            for (int column = 0; column < width; column++)
            {
                int sourceX = x + column;
                if ((uint)sourceX >= (uint)target.Width) continue;

                int index = offset + (row * scanLength) + column;
                if ((uint)index >= (uint)destination.Length) continue;

                destination[index] = target.GetPixel(sourceX, sourceY);
            }
        }
    }

    /// <summary>Expands one of the packed 16-bit formats into the runtime's 32-bit pixels.</summary>
    private static int Expand16(short packed, int format, bool transparency)
    {
        int value = (ushort)packed;

        if (format == 4444)
        {
            int alpha = ((value >> 12) & 0xF) * 17;
            int red = ((value >> 8) & 0xF) * 17;
            int green = ((value >> 4) & 0xF) * 17;
            int blue = (value & 0xF) * 17;
            return PixelBlend.Pack(transparency ? alpha : 255, red, green, blue);
        }

        // Otherwise 565, which has no alpha channel at all.
        int r5 = (value >> 11) & 0x1F;
        int g6 = (value >> 5) & 0x3F;
        int b5 = value & 0x1F;
        return PixelBlend.Pack(255, (r5 * 255) / 31, (g6 * 255) / 63, (b5 * 255) / 31);
    }

    private static short Narrow16(int argb, int format)
    {
        if (format == 4444)
        {
            int alpha = ((argb >> 24) & 0xFF) >> 4;
            int red = ((argb >> 16) & 0xFF) >> 4;
            int green = ((argb >> 8) & 0xFF) >> 4;
            int blue = (argb & 0xFF) >> 4;
            return (short)((alpha << 12) | (red << 8) | (green << 4) | blue);
        }

        int r5 = ((argb >> 16) & 0xFF) >> 3;
        int g6 = ((argb >> 8) & 0xFF) >> 2;
        int b5 = (argb & 0xFF) >> 3;
        return (short)((r5 << 11) | (g6 << 5) | b5);
    }

    /// <summary>
    /// Maps a Nokia manipulation value onto the equivalent sprite transform.
    /// </summary>
    /// <remarks>
    /// Nokia encodes rotation in degrees counter-clockwise, optionally combined with a flip bit,
    /// where MIDP names eight discrete transforms and rotates clockwise. The rotations are
    /// therefore mirrored across the mapping.
    /// </remarks>
    private static int ToTransform(int manipulation)
    {
        const int FlipHorizontal = 0x2000;
        const int FlipVertical = 0x4000;

        bool mirrored = (manipulation & FlipHorizontal) != 0;
        bool flipped = (manipulation & FlipVertical) != 0;
        int rotation = manipulation & ~(FlipHorizontal | FlipVertical);

        // A vertical flip is a horizontal mirror plus a half turn.
        if (flipped)
        {
            mirrored = !mirrored;
            rotation = (rotation + 180) % 360;
        }

        return rotation switch
        {
            90 => mirrored ? SpriteTransform.MirrorRotate270 : SpriteTransform.Rotate270,
            180 => mirrored ? SpriteTransform.MirrorRotate180 : SpriteTransform.Rotate180,
            270 => mirrored ? SpriteTransform.MirrorRotate90 : SpriteTransform.Rotate90,
            _ => mirrored ? SpriteTransform.Mirror : SpriteTransform.None,
        };
    }

    private static void FillPolygon(
        JMobile.Core.Execution.JavaThread thread,
        JavaObject self,
        int[]? xs,
        int xOffset,
        int[]? ys,
        int yOffset,
        int count,
        int argb)
    {
        if (self.GetField(0).Reference is not JavaGraphics graphics || xs is null || ys is null) return;
        if (count < 3) return;

        graphics.Context.SetColor(argb);

        // A convex-ish fan is enough for the shapes games draw through this API, and is what
        // most implementations of it did anyway.
        for (int i = 1; i + 1 < count; i++)
        {
            graphics.Context.FillTriangle(
                xs[xOffset], ys[yOffset],
                xs[xOffset + i], ys[yOffset + i],
                xs[xOffset + i + 1], ys[yOffset + i + 1]);
        }
    }

    private static void DrawPolygon(
        JMobile.Core.Execution.JavaThread thread,
        JavaObject self,
        int[]? xs,
        int xOffset,
        int[]? ys,
        int yOffset,
        int count,
        int argb)
    {
        if (self.GetField(0).Reference is not JavaGraphics graphics || xs is null || ys is null) return;
        if (count < 2) return;

        graphics.Context.SetColor(argb);
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            graphics.Context.DrawLine(
                xs[xOffset + i], ys[yOffset + i],
                xs[xOffset + next], ys[yOffset + next]);
        }
    }

    private static void RegisterNokiaDeviceControl(RuntimeRegistry registry)
    {
        registry.Define("com/nokia/mid/ui/DeviceControl", builder => builder
            .StaticMethod("setLights", "(II)V", static (thread, args) =>
            {
                if (args.Int(1) > 0) MidpRuntime.Of(thread.Vm).Feedback?.FlashBacklight(500);
                return JavaValue.Void;
            })
            .StaticMethod("flashLights", "(J)V", static (thread, args) =>
            {
                MidpRuntime.Of(thread.Vm).Feedback?.FlashBacklight((int)Math.Min(args.Long(0), int.MaxValue));
                return JavaValue.Void;
            })
            .StaticMethod("startVibra", "(IJ)V", static (thread, args) =>
            {
                MidpRuntime.Of(thread.Vm).Feedback?.Vibrate((int)Math.Min(args.Long(1), int.MaxValue));
                return JavaValue.Void;
            })
            .StaticMethod("stopVibra", "()V", static (_, _) => JavaValue.Void));
    }

    private static void RegisterNokiaSound(RuntimeRegistry registry)
    {
        registry.Define("com/nokia/mid/sound/Sound", builder => builder
            .Constant("FORMAT_TONE", 1)
            .Constant("FORMAT_WAV", 5)
            .Constant("SOUND_PLAYING", 0)
            .Constant("SOUND_STOPPED", 1)
            .Constant("SOUND_UNINITIALIZED", 3)
            .Field("frequency", "I")
            .Field("duration", "J")
            .Constructor("(IJ)V", static (_, args) =>
            {
                args.This.SetField(0, JavaValue.Int(args.Int(0)));
                args.This.SetField(1, JavaValue.Long(args.Long(1)));
                return JavaValue.Void;
            })
            .Constructor("([BI)V", static (_, _) => JavaValue.Void)
            .Method("play", "(I)V", static (thread, args) =>
            {
                int frequency = args.This.GetField(0).AsInt;
                long duration = args.This.GetField(1).AsLong;
                if (frequency <= 0) return JavaValue.Void;

                // Nokia's Sound takes a frequency in millihertz; MIDP tones take a MIDI note.
                int note = (int)Math.Clamp(
                    69 + (12 * Math.Log2(frequency / 1000.0 / 440.0)), 0, 127);

                MidpRuntime.Of(thread.Vm).Audio?.PlayTone(
                    note, (int)Math.Clamp(duration, 1, 10000), 60);
                return JavaValue.Void;
            })
            .NoOpMethod("stop", "()V")
            .NoOpMethod("release", "()V")
            .Method("getState", "()I", static (_, _) => JavaValue.Int(1))
            .NoOpMethod("setGain", "(I)V")
            .Method("getGain", "()I", static (_, _) => JavaValue.Int(128)));
    }
}
