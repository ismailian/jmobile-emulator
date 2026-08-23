using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Core.Native;
using JMobile.Runtime.Graphics;

namespace JMobile.Runtime.Midp;

/// <summary>A <c>GameCanvas</c> and the off-screen buffer it draws into.</summary>
public sealed class JavaGameCanvas(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public Surface? Buffer { get; set; }

    public GraphicsContext? BufferContext { get; set; }

    public JavaObject? BufferGraphics { get; set; }

    public bool SuppressKeyEvents { get; set; }
}

/// <summary>A <c>Sprite</c>: a framed image with a frame sequence, a transform and a hitbox.</summary>
public sealed class JavaSprite(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public Surface Source { get; set; } = null!;

    public int FrameWidth { get; set; }

    public int FrameHeight { get; set; }

    /// <summary>Frame indices the sprite cycles through; defaults to every frame in order.</summary>
    public int[] Sequence { get; set; } = [];

    public int SequenceIndex { get; set; }

    public int Transform { get; set; }

    /// <summary>Reference pixel, in un-transformed frame coordinates.</summary>
    public int ReferenceX { get; set; }

    public int ReferenceY { get; set; }

    public int CollisionX { get; set; }

    public int CollisionY { get; set; }

    public int CollisionWidth { get; set; }

    public int CollisionHeight { get; set; }

    /// <summary>Frames per row in the source image, used to locate a frame by index.</summary>
    public int Columns { get; set; } = 1;

    public int FrameCount => Sequence.Length;

    public int CurrentFrame => Sequence.Length == 0 ? 0 : Sequence[SequenceIndex];
}

/// <summary>A <c>TiledLayer</c>: a grid of tile indices over one tile sheet.</summary>
public sealed class JavaTiledLayer(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public Surface Source { get; set; } = null!;

    public int TileWidth { get; set; }

    public int TileHeight { get; set; }

    public int Columns { get; set; }

    public int Rows { get; set; }

    /// <summary>Cell contents: 0 is empty, positive is a static tile, negative is animated.</summary>
    public int[] Cells { get; set; } = [];

    /// <summary>Animated tile definitions, indexed by <c>-index - 1</c>.</summary>
    public List<int> AnimatedTiles { get; } = [];

    /// <summary>Tiles across the source image, which fixes how an index maps to a rectangle.</summary>
    public int SheetColumns { get; set; } = 1;
}

/// <summary>A <c>LayerManager</c>: an ordered set of layers and a view window over them.</summary>
public sealed class JavaLayerManager(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public List<JavaObject> Layers { get; } = [];

    public int ViewX { get; set; }

    public int ViewY { get; set; }

    public int ViewWidth { get; set; } = int.MaxValue;

    public int ViewHeight { get; set; } = int.MaxValue;
}

/// <summary>
/// <c>javax.microedition.lcdui.game</c>: the API MIDP 2.0 added specifically for games.
/// </summary>
/// <remarks>
/// This package is what most action games are actually built on. <c>GameCanvas</c> replaces the
/// callback-driven paint model with a buffer the game draws into and flushes when it chooses,
/// and polls input instead of waiting for events — which is why it also has the strictest
/// timing requirements of anything here.
/// </remarks>
public sealed class GameModule : IRuntimeModule
{
    public string Name => "javax.microedition.lcdui.game";

    // Layer field slots, in declaration order.
    private const int LayerXSlot = 0;
    private const int LayerYSlot = 1;
    private const int LayerWidthSlot = 2;
    private const int LayerHeightSlot = 3;
    private const int LayerVisibleSlot = 4;

    public void Register(RuntimeRegistry registry)
    {
        RegisterGameCanvas(registry);
        RegisterLayer(registry);
        RegisterSprite(registry);
        RegisterTiledLayer(registry);
        RegisterLayerManager(registry);
    }

    // ---------------------------------------------------------------------------------
    // GameCanvas
    // ---------------------------------------------------------------------------------

    private static void RegisterGameCanvas(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/game/GameCanvas", builder => builder
            .Extends("javax/microedition/lcdui/Canvas")
            .AsAbstract()
            .AllocatesWith(static type => new JavaGameCanvas(type, type.InstanceFieldCount))
            .Constant("UP_PRESSED", 0x0002)
            .Constant("LEFT_PRESSED", 0x0004)
            .Constant("RIGHT_PRESSED", 0x0020)
            .Constant("DOWN_PRESSED", 0x0040)
            .Constant("FIRE_PRESSED", 0x0100)
            .Constant("GAME_A_PRESSED", 0x0200)
            .Constant("GAME_B_PRESSED", 0x0400)
            .Constant("GAME_C_PRESSED", 0x0800)
            .Constant("GAME_D_PRESSED", 0x1000)
            .Constructor("(Z)V", static (_, args) =>
            {
                args.Self<JavaGameCanvas>().SuppressKeyEvents = args.Bool(0);
                return JavaValue.Void;
            })
            .Method("getGraphics", "()Ljavax/microedition/lcdui/Graphics;", static (thread, args) =>
            {
                var self = args.Self<JavaGameCanvas>();
                MidpRuntime runtime = MidpRuntime.Of(thread.Vm);
                EnsureBuffer(thread, self, runtime);

                // Each call is specified to return a context with default state, which games
                // rely on: they do not reset the clip or translation themselves.
                self.BufferContext!.ResetTo(self.Buffer!);
                return JavaValue.Ref(self.BufferGraphics!);
            })
            .Method("flushGraphics", "()V", static (thread, args) =>
            {
                Flush(thread, args.Self<JavaGameCanvas>(), null);
                return JavaValue.Void;
            })
            .Method("flushGraphics", "(IIII)V", static (thread, args) =>
            {
                Flush(thread, args.Self<JavaGameCanvas>(),
                    (args.Int(0), args.Int(1), args.Int(2), args.Int(3)));
                return JavaValue.Void;
            })
            .Method("getKeyStates", "()I",
                static (thread, _) => JavaValue.Int(MidpRuntime.Of(thread.Vm).TakeKeyStates()))
            .Method("paint", "(Ljavax/microedition/lcdui/Graphics;)V", static (thread, args) =>
            {
                // The default paint simply presents the buffer, so a GameCanvas that is repainted
                // by the system rather than by flushGraphics still shows its last frame.
                var self = args.Self<JavaGameCanvas>();
                if (self.Buffer is null) return JavaValue.Void;

                if (args.Object(0) is JavaGraphics graphics)
                    graphics.Context.DrawImage(self.Buffer, 0, 0, Anchor.Left | Anchor.Top);

                return JavaValue.Void;
            }));
    }

    private static void EnsureBuffer(JavaThread thread, JavaGameCanvas canvas, MidpRuntime runtime)
    {
        int width = runtime.CanvasWidth;
        int height = runtime.CanvasHeight;

        if (canvas.Buffer is { } existing && existing.Width == width && existing.Height == height) return;

        canvas.Buffer = new Surface(width, height, hasAlpha: false);
        canvas.Buffer.Clear(unchecked((int)0xFF000000));
        canvas.BufferContext = new GraphicsContext(canvas.Buffer);
        canvas.BufferGraphics = GraphicsModule.NewGraphics(thread, canvas.BufferContext, isScreen: false);
    }

    /// <summary>Copies the game's buffer onto the screen and presents the frame.</summary>
    private static void Flush(JavaThread thread, JavaGameCanvas canvas, (int X, int Y, int W, int H)? region)
    {
        if (canvas.Buffer is not { } buffer) return;

        MidpRuntime runtime = MidpRuntime.Of(thread.Vm);
        if (!ReferenceEquals(runtime.CurrentDisplayable, canvas)) return;

        GraphicsContext screen = runtime.ScreenGraphics;
        screen.ResetTo(runtime.Screen);

        if (region is { } area)
        {
            screen.SetClip(area.X, area.Y, area.W, area.H);
            screen.Blit(buffer, area.X, area.Y, area.W, area.H, area.X, area.Y, SpriteTransform.None);
        }
        else
        {
            screen.Blit(buffer, 0, 0, buffer.Width, buffer.Height, 0, 0, SpriteTransform.None);
        }

        runtime.NotifyFramePainted();
    }

    // ---------------------------------------------------------------------------------
    // Layer
    // ---------------------------------------------------------------------------------

    private static void RegisterLayer(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/game/Layer", builder => builder
            .AsAbstract()
            .Field("x", "I")
            .Field("y", "I")
            .Field("width", "I")
            .Field("height", "I")
            .Field("visible", "Z")
            .Constructor("()V", static (_, args) =>
            {
                args.This.SetField(LayerVisibleSlot, JavaValue.Bool(true));
                return JavaValue.Void;
            })
            .Method("setPosition", "(II)V", static (_, args) =>
            {
                args.This.SetField(LayerXSlot, JavaValue.Int(args.Int(0)));
                args.This.SetField(LayerYSlot, JavaValue.Int(args.Int(1)));
                return JavaValue.Void;
            })
            .Method("move", "(II)V", static (_, args) =>
            {
                args.This.SetField(LayerXSlot, JavaValue.Int(args.This.GetField(LayerXSlot).AsInt + args.Int(0)));
                args.This.SetField(LayerYSlot, JavaValue.Int(args.This.GetField(LayerYSlot).AsInt + args.Int(1)));
                return JavaValue.Void;
            })
            .Method("getX", "()I", static (_, args) => args.This.GetField(LayerXSlot))
            .Method("getY", "()I", static (_, args) => args.This.GetField(LayerYSlot))
            .Method("getWidth", "()I", static (_, args) => args.This.GetField(LayerWidthSlot))
            .Method("getHeight", "()I", static (_, args) => args.This.GetField(LayerHeightSlot))
            .Method("setVisible", "(Z)V", static (_, args) =>
            {
                args.This.SetField(LayerVisibleSlot, JavaValue.Bool(args.Bool(0)));
                return JavaValue.Void;
            })
            .Method("isVisible", "()Z", static (_, args) => args.This.GetField(LayerVisibleSlot))
            .AbstractMethod("paint", "(Ljavax/microedition/lcdui/Graphics;)V"));
    }

    // ---------------------------------------------------------------------------------
    // Sprite
    // ---------------------------------------------------------------------------------

    private static void RegisterSprite(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/game/Sprite", builder => builder
            .Extends("javax/microedition/lcdui/game/Layer")
            .AllocatesWith(static type => new JavaSprite(type, type.InstanceFieldCount))
            .Constant("TRANS_NONE", SpriteTransform.None)
            .Constant("TRANS_MIRROR", SpriteTransform.Mirror)
            .Constant("TRANS_MIRROR_ROT90", SpriteTransform.MirrorRotate90)
            .Constant("TRANS_MIRROR_ROT180", SpriteTransform.MirrorRotate180)
            .Constant("TRANS_MIRROR_ROT270", SpriteTransform.MirrorRotate270)
            .Constant("TRANS_ROT90", SpriteTransform.Rotate90)
            .Constant("TRANS_ROT180", SpriteTransform.Rotate180)
            .Constant("TRANS_ROT270", SpriteTransform.Rotate270)
            .Constructor("(Ljavax/microedition/lcdui/Image;)V", static (thread, args) =>
            {
                var image = args.Object<JavaImage>(0)
                            ?? throw thread.Vm.NewNullPointerException(thread, "image");
                Initialise(args.Self<JavaSprite>(), image.Surface, image.Width, image.Height);
                return JavaValue.Void;
            })
            .Constructor("(Ljavax/microedition/lcdui/Image;II)V", static (thread, args) =>
            {
                var image = args.Object<JavaImage>(0)
                            ?? throw thread.Vm.NewNullPointerException(thread, "image");
                int frameWidth = args.Int(1), frameHeight = args.Int(2);

                if (frameWidth <= 0 || frameHeight <= 0 ||
                    image.Width % frameWidth != 0 || image.Height % frameHeight != 0)
                {
                    throw thread.Vm.NewIllegalArgumentException(
                        thread, "frame size does not divide the image evenly");
                }

                Initialise(args.Self<JavaSprite>(), image.Surface, frameWidth, frameHeight);
                return JavaValue.Void;
            })
            .Constructor("(Ljavax/microedition/lcdui/game/Sprite;)V", static (thread, args) =>
            {
                var source = args.Object<JavaSprite>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "s");
                var self = args.Self<JavaSprite>();

                Initialise(self, source.Source, source.FrameWidth, source.FrameHeight);
                self.Sequence = (int[])source.Sequence.Clone();
                self.SequenceIndex = source.SequenceIndex;
                self.Transform = source.Transform;
                self.SetField(LayerXSlot, source.GetField(LayerXSlot));
                self.SetField(LayerYSlot, source.GetField(LayerYSlot));
                return JavaValue.Void;
            })
            .Method("setFrame", "(I)V", static (thread, args) =>
            {
                var self = args.Self<JavaSprite>();
                int index = args.Int(0);
                if ((uint)index >= (uint)self.Sequence.Length)
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, index.ToString());
                self.SequenceIndex = index;
                return JavaValue.Void;
            })
            .Method("getFrame", "()I", static (_, args) => JavaValue.Int(args.Self<JavaSprite>().SequenceIndex))
            .Method("getFrameSequenceLength", "()I",
                static (_, args) => JavaValue.Int(args.Self<JavaSprite>().Sequence.Length))
            .Method("getRawFrameCount", "()I", static (_, args) =>
            {
                var self = args.Self<JavaSprite>();
                if (self.FrameWidth == 0 || self.FrameHeight == 0) return JavaValue.Int(0);
                return JavaValue.Int((self.Source.Width / self.FrameWidth) *
                                     (self.Source.Height / self.FrameHeight));
            })
            .Method("nextFrame", "()V", static (_, args) =>
            {
                var self = args.Self<JavaSprite>();
                if (self.Sequence.Length > 0)
                    self.SequenceIndex = (self.SequenceIndex + 1) % self.Sequence.Length;
                return JavaValue.Void;
            })
            .Method("prevFrame", "()V", static (_, args) =>
            {
                var self = args.Self<JavaSprite>();
                if (self.Sequence.Length > 0)
                    self.SequenceIndex = (self.SequenceIndex + self.Sequence.Length - 1) % self.Sequence.Length;
                return JavaValue.Void;
            })
            .Method("setFrameSequence", "([I)V", static (thread, args) =>
            {
                var self = args.Self<JavaSprite>();
                int[]? sequence = args.Array<int>(0);

                if (sequence is null || sequence.Length == 0)
                {
                    // Passing null restores the default sequence of every frame in order.
                    self.Sequence = DefaultSequence(self);
                    self.SequenceIndex = 0;
                    return JavaValue.Void;
                }

                int frames = (self.Source.Width / Math.Max(1, self.FrameWidth)) *
                             (self.Source.Height / Math.Max(1, self.FrameHeight));
                foreach (int frame in sequence)
                {
                    if (frame < 0 || frame >= frames)
                        throw thread.Vm.NewArrayIndexOutOfBoundsException(thread, frame, frames);
                }

                self.Sequence = (int[])sequence.Clone();
                self.SequenceIndex = 0;
                return JavaValue.Void;
            })
            .Method("setTransform", "(I)V", static (_, args) =>
            {
                args.Self<JavaSprite>().Transform = args.Int(0);
                return JavaValue.Void;
            })
            .Method("setImage", "(Ljavax/microedition/lcdui/Image;II)V", static (thread, args) =>
            {
                var image = args.Object<JavaImage>(0)
                            ?? throw thread.Vm.NewNullPointerException(thread, "img");
                Initialise(args.Self<JavaSprite>(), image.Surface, args.Int(1), args.Int(2));
                return JavaValue.Void;
            })
            .Method("defineReferencePixel", "(II)V", static (_, args) =>
            {
                var self = args.Self<JavaSprite>();
                self.ReferenceX = args.Int(0);
                self.ReferenceY = args.Int(1);
                return JavaValue.Void;
            })
            .Method("setRefPixelPosition", "(II)V", static (_, args) =>
            {
                var self = args.Self<JavaSprite>();
                self.SetField(LayerXSlot, JavaValue.Int(args.Int(0) - self.ReferenceX));
                self.SetField(LayerYSlot, JavaValue.Int(args.Int(1) - self.ReferenceY));
                return JavaValue.Void;
            })
            .Method("getRefPixelX", "()I", static (_, args) =>
                JavaValue.Int(args.This.GetField(LayerXSlot).AsInt + args.Self<JavaSprite>().ReferenceX))
            .Method("getRefPixelY", "()I", static (_, args) =>
                JavaValue.Int(args.This.GetField(LayerYSlot).AsInt + args.Self<JavaSprite>().ReferenceY))
            .Method("defineCollisionRectangle", "(IIII)V", static (_, args) =>
            {
                var self = args.Self<JavaSprite>();
                self.CollisionX = args.Int(0);
                self.CollisionY = args.Int(1);
                self.CollisionWidth = args.Int(2);
                self.CollisionHeight = args.Int(3);
                return JavaValue.Void;
            })
            .Method("collidesWith", "(Ljavax/microedition/lcdui/game/Sprite;Z)Z", static (_, args) =>
            {
                var self = args.Self<JavaSprite>();
                var other = args.Object<JavaSprite>(0);
                if (other is null || !Visible(self) || !Visible(other)) return JavaValue.Bool(false);

                return JavaValue.Bool(Collides(self, other, args.Bool(1)));
            })
            .Method("collidesWith", "(Ljavax/microedition/lcdui/Image;IIZ)Z", static (_, args) =>
            {
                var self = args.Self<JavaSprite>();
                var image = args.Object<JavaImage>(0);
                if (image is null || !Visible(self)) return JavaValue.Bool(false);

                var target = (args.Int(1), args.Int(2), image.Width, image.Height);
                return JavaValue.Bool(RectanglesOverlap(HitBox(self), target));
            })
            .Method("collidesWith", "(Ljavax/microedition/lcdui/game/TiledLayer;Z)Z", static (_, args) =>
            {
                var self = args.Self<JavaSprite>();
                var layer = args.Object<JavaTiledLayer>(0);
                if (layer is null || !Visible(self) || !Visible(layer)) return JavaValue.Bool(false);

                return JavaValue.Bool(CollidesWithTiles(self, layer));
            })
            .Method("paint", "(Ljavax/microedition/lcdui/Graphics;)V", static (_, args) =>
            {
                var self = args.Self<JavaSprite>();
                if (!Visible(self) || args.Object(0) is not JavaGraphics graphics) return JavaValue.Void;

                int frame = self.CurrentFrame;
                int columns = Math.Max(1, self.Source.Width / Math.Max(1, self.FrameWidth));

                graphics.Context.Blit(
                    self.Source,
                    (frame % columns) * self.FrameWidth,
                    (frame / columns) * self.FrameHeight,
                    self.FrameWidth,
                    self.FrameHeight,
                    self.GetField(LayerXSlot).AsInt,
                    self.GetField(LayerYSlot).AsInt,
                    self.Transform);

                return JavaValue.Void;
            }));
    }

    private static void Initialise(JavaSprite sprite, Surface source, int frameWidth, int frameHeight)
    {
        sprite.Source = source;
        sprite.FrameWidth = frameWidth;
        sprite.FrameHeight = frameHeight;
        sprite.Columns = Math.Max(1, source.Width / Math.Max(1, frameWidth));
        sprite.Sequence = DefaultSequence(sprite);
        sprite.SequenceIndex = 0;
        sprite.CollisionX = 0;
        sprite.CollisionY = 0;
        sprite.CollisionWidth = frameWidth;
        sprite.CollisionHeight = frameHeight;

        sprite.SetField(LayerWidthSlot, JavaValue.Int(frameWidth));
        sprite.SetField(LayerHeightSlot, JavaValue.Int(frameHeight));
        sprite.SetField(LayerVisibleSlot, JavaValue.Bool(true));
    }

    private static int[] DefaultSequence(JavaSprite sprite)
    {
        if (sprite.FrameWidth <= 0 || sprite.FrameHeight <= 0) return [0];

        int count = (sprite.Source.Width / sprite.FrameWidth) * (sprite.Source.Height / sprite.FrameHeight);
        var sequence = new int[Math.Max(1, count)];
        for (int i = 0; i < sequence.Length; i++) sequence[i] = i;
        return sequence;
    }

    private static bool Visible(JavaObject layer) => layer.GetField(LayerVisibleSlot).AsBool;

    private static (int X, int Y, int W, int H) HitBox(JavaSprite sprite) =>
        (sprite.GetField(LayerXSlot).AsInt + sprite.CollisionX,
         sprite.GetField(LayerYSlot).AsInt + sprite.CollisionY,
         sprite.CollisionWidth,
         sprite.CollisionHeight);

    private static bool RectanglesOverlap((int X, int Y, int W, int H) a, (int X, int Y, int W, int H) b) =>
        a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    /// <summary>
    /// Sprite-to-sprite collision, optionally at pixel level.
    /// </summary>
    /// <remarks>
    /// The rectangle test runs first and rejects almost every pair, so the per-pixel loop only
    /// runs for sprites that are genuinely overlapping. That ordering is what keeps pixel-level
    /// collision affordable for a game testing dozens of sprites each frame.
    /// </remarks>
    private static bool Collides(JavaSprite left, JavaSprite right, bool pixelLevel)
    {
        var leftBox = HitBox(left);
        var rightBox = HitBox(right);
        if (!RectanglesOverlap(leftBox, rightBox)) return false;
        if (!pixelLevel) return true;

        int startX = Math.Max(leftBox.X, rightBox.X);
        int startY = Math.Max(leftBox.Y, rightBox.Y);
        int endX = Math.Min(leftBox.X + leftBox.W, rightBox.X + rightBox.W);
        int endY = Math.Min(leftBox.Y + leftBox.H, rightBox.Y + rightBox.H);

        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                if (IsOpaqueAt(left, x, y) && IsOpaqueAt(right, x, y)) return true;
            }
        }

        return false;
    }

    /// <summary>Tests whether a sprite has a non-transparent pixel at a screen position.</summary>
    private static bool IsOpaqueAt(JavaSprite sprite, int screenX, int screenY)
    {
        int localX = screenX - sprite.GetField(LayerXSlot).AsInt;
        int localY = screenY - sprite.GetField(LayerYSlot).AsInt;
        if (localX < 0 || localY < 0 || localX >= sprite.FrameWidth || localY >= sprite.FrameHeight)
            return false;

        int frame = sprite.CurrentFrame;
        int columns = Math.Max(1, sprite.Source.Width / Math.Max(1, sprite.FrameWidth));

        int sourceX = ((frame % columns) * sprite.FrameWidth) + localX;
        int sourceY = ((frame / columns) * sprite.FrameHeight) + localY;
        if (sourceX >= sprite.Source.Width || sourceY >= sprite.Source.Height) return false;

        return (sprite.Source.GetPixel(sourceX, sourceY) & unchecked((int)0xFF000000)) != 0;
    }

    private static bool CollidesWithTiles(JavaSprite sprite, JavaTiledLayer layer)
    {
        var box = HitBox(sprite);
        int layerX = layer.GetField(LayerXSlot).AsInt;
        int layerY = layer.GetField(LayerYSlot).AsInt;

        int firstColumn = Math.Max(0, (box.X - layerX) / Math.Max(1, layer.TileWidth));
        int firstRow = Math.Max(0, (box.Y - layerY) / Math.Max(1, layer.TileHeight));
        int lastColumn = Math.Min(layer.Columns - 1,
            (box.X + box.W - 1 - layerX) / Math.Max(1, layer.TileWidth));
        int lastRow = Math.Min(layer.Rows - 1,
            (box.Y + box.H - 1 - layerY) / Math.Max(1, layer.TileHeight));

        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                // Any non-empty cell in the overlapped range counts as a collision.
                if (layer.Cells[(row * layer.Columns) + column] != 0) return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------------------
    // TiledLayer
    // ---------------------------------------------------------------------------------

    private static void RegisterTiledLayer(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/game/TiledLayer", builder => builder
            .Extends("javax/microedition/lcdui/game/Layer")
            .AllocatesWith(static type => new JavaTiledLayer(type, type.InstanceFieldCount))
            .Constructor("(IILjavax/microedition/lcdui/Image;II)V", static (thread, args) =>
            {
                int columns = args.Int(0), rows = args.Int(1);
                var image = args.Object<JavaImage>(2)
                            ?? throw thread.Vm.NewNullPointerException(thread, "image");
                int tileWidth = args.Int(3), tileHeight = args.Int(4);

                if (columns <= 0 || rows <= 0 || tileWidth <= 0 || tileHeight <= 0 ||
                    image.Width % tileWidth != 0 || image.Height % tileHeight != 0)
                {
                    throw thread.Vm.NewIllegalArgumentException(
                        thread, "tile size does not divide the image evenly");
                }

                var self = args.Self<JavaTiledLayer>();
                self.Source = image.Surface;
                self.TileWidth = tileWidth;
                self.TileHeight = tileHeight;
                self.Columns = columns;
                self.Rows = rows;
                self.Cells = new int[columns * rows];
                self.SheetColumns = image.Width / tileWidth;

                self.SetField(LayerWidthSlot, JavaValue.Int(columns * tileWidth));
                self.SetField(LayerHeightSlot, JavaValue.Int(rows * tileHeight));
                self.SetField(LayerVisibleSlot, JavaValue.Bool(true));
                return JavaValue.Void;
            })
            .Method("getColumns", "()I", static (_, args) => JavaValue.Int(args.Self<JavaTiledLayer>().Columns))
            .Method("getRows", "()I", static (_, args) => JavaValue.Int(args.Self<JavaTiledLayer>().Rows))
            .Method("getCellWidth", "()I", static (_, args) => JavaValue.Int(args.Self<JavaTiledLayer>().TileWidth))
            .Method("getCellHeight", "()I",
                static (_, args) => JavaValue.Int(args.Self<JavaTiledLayer>().TileHeight))
            .Method("setCell", "(III)V", static (thread, args) =>
            {
                var self = args.Self<JavaTiledLayer>();
                int column = args.Int(0), row = args.Int(1);
                if ((uint)column >= (uint)self.Columns || (uint)row >= (uint)self.Rows)
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, $"cell {column},{row}");

                self.Cells[(row * self.Columns) + column] = args.Int(2);
                return JavaValue.Void;
            })
            .Method("getCell", "(II)I", static (thread, args) =>
            {
                var self = args.Self<JavaTiledLayer>();
                int column = args.Int(0), row = args.Int(1);
                if ((uint)column >= (uint)self.Columns || (uint)row >= (uint)self.Rows)
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, $"cell {column},{row}");

                return JavaValue.Int(self.Cells[(row * self.Columns) + column]);
            })
            .Method("fillCells", "(IIIII)V", static (thread, args) =>
            {
                var self = args.Self<JavaTiledLayer>();
                int column = args.Int(0), row = args.Int(1);
                int columns = args.Int(2), rows = args.Int(3), tile = args.Int(4);

                if (columns < 0 || rows < 0 ||
                    column < 0 || row < 0 ||
                    column + columns > self.Columns || row + rows > self.Rows)
                {
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, "fillCells is out of range");
                }

                for (int r = row; r < row + rows; r++)
                {
                    for (int c = column; c < column + columns; c++)
                        self.Cells[(r * self.Columns) + c] = tile;
                }
                return JavaValue.Void;
            })
            .Method("createAnimatedTile", "(I)I", static (_, args) =>
            {
                var self = args.Self<JavaTiledLayer>();
                self.AnimatedTiles.Add(args.Int(0));
                // Animated tiles are addressed by negative indices, counting down from -1.
                return JavaValue.Int(-self.AnimatedTiles.Count);
            })
            .Method("setAnimatedTile", "(II)V", static (thread, args) =>
            {
                var self = args.Self<JavaTiledLayer>();
                int index = -args.Int(0) - 1;
                if ((uint)index >= (uint)self.AnimatedTiles.Count)
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, "unknown animated tile");

                self.AnimatedTiles[index] = args.Int(1);
                return JavaValue.Void;
            })
            .Method("getAnimatedTile", "(I)I", static (thread, args) =>
            {
                var self = args.Self<JavaTiledLayer>();
                int index = -args.Int(0) - 1;
                if ((uint)index >= (uint)self.AnimatedTiles.Count)
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, "unknown animated tile");

                return JavaValue.Int(self.AnimatedTiles[index]);
            })
            .Method("paint", "(Ljavax/microedition/lcdui/Graphics;)V", static (_, args) =>
            {
                var self = args.Self<JavaTiledLayer>();
                if (!Visible(self) || args.Object(0) is not JavaGraphics graphics) return JavaValue.Void;

                PaintTiles(self, graphics.Context);
                return JavaValue.Void;
            }));
    }

    private static void PaintTiles(JavaTiledLayer layer, GraphicsContext context)
    {
        int originX = layer.GetField(LayerXSlot).AsInt;
        int originY = layer.GetField(LayerYSlot).AsInt;

        for (int row = 0; row < layer.Rows; row++)
        {
            for (int column = 0; column < layer.Columns; column++)
            {
                int cell = layer.Cells[(row * layer.Columns) + column];
                if (cell == 0) continue;

                // A negative cell names an animated tile, which resolves to a static one.
                if (cell < 0)
                {
                    int animated = -cell - 1;
                    if ((uint)animated >= (uint)layer.AnimatedTiles.Count) continue;
                    cell = layer.AnimatedTiles[animated];
                    if (cell == 0) continue;
                }

                int tile = cell - 1;
                context.Blit(
                    layer.Source,
                    (tile % layer.SheetColumns) * layer.TileWidth,
                    (tile / layer.SheetColumns) * layer.TileHeight,
                    layer.TileWidth,
                    layer.TileHeight,
                    originX + (column * layer.TileWidth),
                    originY + (row * layer.TileHeight),
                    SpriteTransform.None);
            }
        }
    }

    // ---------------------------------------------------------------------------------
    // LayerManager
    // ---------------------------------------------------------------------------------

    private static void RegisterLayerManager(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/game/LayerManager", builder => builder
            .AllocatesWith(static type => new JavaLayerManager(type, type.InstanceFieldCount))
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Method("append", "(Ljavax/microedition/lcdui/game/Layer;)V", static (thread, args) =>
            {
                JavaObject layer = RuntimeSupport.RequireRef(thread, args.Object(0), "l");
                var self = args.Self<JavaLayerManager>();
                self.Layers.Remove(layer);
                self.Layers.Add(layer);
                return JavaValue.Void;
            })
            .Method("insert", "(Ljavax/microedition/lcdui/game/Layer;I)V", static (thread, args) =>
            {
                JavaObject layer = RuntimeSupport.RequireRef(thread, args.Object(0), "l");
                var self = args.Self<JavaLayerManager>();
                self.Layers.Remove(layer);
                self.Layers.Insert(Math.Clamp(args.Int(1), 0, self.Layers.Count), layer);
                return JavaValue.Void;
            })
            .Method("remove", "(Ljavax/microedition/lcdui/game/Layer;)V", static (_, args) =>
            {
                args.Self<JavaLayerManager>().Layers.Remove(args.Object(0)!);
                return JavaValue.Void;
            })
            .Method("getLayerAt", "(I)Ljavax/microedition/lcdui/game/Layer;", static (thread, args) =>
            {
                var self = args.Self<JavaLayerManager>();
                int index = args.Int(0);
                if ((uint)index >= (uint)self.Layers.Count)
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, index.ToString());
                return JavaValue.Ref(self.Layers[index]);
            })
            .Method("getSize", "()I", static (_, args) => JavaValue.Int(args.Self<JavaLayerManager>().Layers.Count))
            .Method("setViewWindow", "(IIII)V", static (_, args) =>
            {
                var self = args.Self<JavaLayerManager>();
                self.ViewX = args.Int(0);
                self.ViewY = args.Int(1);
                self.ViewWidth = args.Int(2);
                self.ViewHeight = args.Int(3);
                return JavaValue.Void;
            })
            .Method("paint", "(Ljavax/microedition/lcdui/Graphics;II)V", static (thread, args) =>
            {
                var self = args.Self<JavaLayerManager>();
                if (args.Object(0) is not JavaGraphics graphics) return JavaValue.Void;

                GraphicsContext context = graphics.Context;

                // The view window scrolls the whole scene: layers are drawn in their own
                // coordinates and the translation maps them into the visible area.
                int savedClipX = context.ClipX, savedClipY = context.ClipY;
                int savedClipWidth = context.ClipWidth, savedClipHeight = context.ClipHeight;

                int x = args.Int(1), y = args.Int(2);
                if (self.ViewWidth != int.MaxValue) context.ClipRect(x, y, self.ViewWidth, self.ViewHeight);

                context.Translate(x - self.ViewX, y - self.ViewY);

                // Later layers sit on top, so painting runs back to front.
                for (int i = self.Layers.Count - 1; i >= 0; i--)
                {
                    JavaObject layer = self.Layers[i];
                    if (!Visible(layer)) continue;

                    thread.Vm.CallVirtual(thread, layer, "paint",
                        "(Ljavax/microedition/lcdui/Graphics;)V", JavaValue.Ref(graphics));
                }

                context.Translate(-(x - self.ViewX), -(y - self.ViewY));
                context.SetClip(savedClipX, savedClipY, savedClipWidth, savedClipHeight);
                return JavaValue.Void;
            }));
    }
}
