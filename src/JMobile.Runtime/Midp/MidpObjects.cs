using JMobile.Core.Model;
using JMobile.Runtime.Graphics;
using JMobile.Runtime.Host;

namespace JMobile.Runtime.Midp;

/// <summary>
/// A <c>javax.microedition.lcdui.Image</c> and the pixel buffer behind it.
/// </summary>
/// <remarks>
/// Attaching the surface to the Java object rather than storing an index in a side table means
/// an image is collected exactly when the game drops its last reference, with no bookkeeping and
/// no leak. The same pattern carries every other host-backed MIDP type below.
/// </remarks>
public sealed class JavaImage(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public Surface Surface { get; set; } = null!;

    /// <summary>False for images created from resources, which MIDP forbids drawing into.</summary>
    public bool IsMutable { get; set; }

    public int Width => Surface.Width;

    public int Height => Surface.Height;
}

/// <summary>A <c>Graphics</c> handle bound to whichever surface it was obtained from.</summary>
public sealed class JavaGraphics(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public GraphicsContext Context { get; set; } = null!;

    /// <summary>
    /// True when this context draws to the screen. Only screen contexts respect the clip a
    /// paint callback was given, which is how partial repaints stay partial.
    /// </summary>
    public bool IsScreen { get; set; }
}

/// <summary>A <c>Font</c> and its rasterised atlas.</summary>
public sealed class JavaFont(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public BitmapFont Bitmap { get; set; } = null!;

    public int Face { get; set; }

    public int Style { get; set; }

    public int Size { get; set; }
}

/// <summary>A <c>RecordStore</c> and the records it holds.</summary>
public sealed class JavaRecordStore(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    /// <summary>Records by id. Ids are never reused, so a sparse map is the honest structure.</summary>
    public Dictionary<int, byte[]> Records { get; } = [];

    public string StoreName { get; set; } = string.Empty;

    public int NextRecordId { get; set; } = 1;

    public int Version { get; set; }

    public long LastModified { get; set; }

    public int OpenCount { get; set; }

    public bool IsOpen { get; set; }
}

/// <summary>A media <c>Player</c> and the host clip it drives.</summary>
public sealed class JavaPlayer(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public IAudioClip? Clip { get; set; }

    public string ContentType { get; set; } = string.Empty;

    /// <summary>Player state as defined by the MMAPI lifecycle constants.</summary>
    public int State { get; set; } = 100; // UNREALIZED

    public int LoopCount { get; set; } = 1;
}

/// <summary>A stream connection opened through the Generic Connection Framework.</summary>
public sealed class JavaConnection(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public string Url { get; set; } = string.Empty;

    public string RequestMethod { get; set; } = "GET";

    public List<KeyValuePair<string, string>> RequestHeaders { get; } = [];

    public MemoryStream RequestBody { get; } = new();

    public HttpResult? Response { get; set; }

    public int ResponseCursor { get; set; }

    public bool IsConnected { get; set; }
}

/// <summary>A byte stream handed to guest code, backed by a host stream.</summary>
public sealed class JavaStream(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public Stream? Underlying { get; set; }

    /// <summary>Position saved by <c>mark</c>, restored by <c>reset</c>.</summary>
    public long MarkPosition { get; set; }

    /// <summary>Where a print stream sends its text, when this stream is one.</summary>
    public Action<string>? TextSink { get; set; }

    /// <summary>Set for output streams that must be flushed somewhere when closed.</summary>
    public Action<byte[]>? OnClose { get; set; }

    public bool IsClosed { get; set; }
}
