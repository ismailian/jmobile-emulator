using JMobile.Core;
using JMobile.Core.Model;
using JMobile.Runtime.Graphics;
using JMobile.Runtime.Host;

namespace JMobile.Runtime.Midp;

/// <summary>
/// The mutable state of one running MIDlet: its screen, its current displayable, its event
/// queue and the host facilities it has been granted.
/// </summary>
/// <remarks>
/// This is registered as a service on the virtual machine, which is how native method bodies
/// reach it. It is the one piece of shared state the MIDP layer has, and keeping it in a single
/// object rather than in statics is what allows two games to run in the same process without
/// interfering — each has its own VM and its own runtime.
/// </remarks>
public sealed class MidpRuntime
{
    private readonly Dictionary<FontRequest, BitmapFont> _fontCache = [];
    private readonly Lock _fontGate = new();
    private int _keyStates;
    private int _latchedKeyStates;

    public MidpRuntime(VirtualMachine vm, DeviceProfile device)
    {
        Vm = vm;
        Device = device;
        Screen = new Surface(device.ScreenWidth, device.ScreenHeight, hasAlpha: false);
        Screen.Clear(PixelBlend.OpaqueMask);
        ScreenGraphics = new GraphicsContext(Screen);
    }

    public VirtualMachine Vm { get; }

    public DeviceProfile Device { get; }

    /// <summary>The phone screen. The desktop shell blits straight out of this buffer.</summary>
    public Surface Screen { get; private set; }

    /// <summary>The context all screen painting goes through, reused across frames.</summary>
    public GraphicsContext ScreenGraphics { get; private set; }

    /// <summary>
    /// The Java <c>Graphics</c> object handed to <c>paint</c>, created once and reused.
    /// </summary>
    /// <remarks>
    /// Allocating one per frame would be legal but wasteful, and at sixty frames a second on a
    /// game that also allocates would measurably increase collection pressure.
    /// </remarks>
    public JavaObject? ScreenGraphicsObject { get; set; }

    public MidpEventQueue Events { get; } = new();

    /// <summary>The singleton <c>Display</c> the MIDlet obtained, once it has asked for one.</summary>
    public JavaObject? Display { get; set; }

    /// <summary>The MIDlet instance itself.</summary>
    public JavaObject? Midlet { get; set; }

    /// <summary>Whatever is on screen right now, or null before the first <c>setCurrent</c>.</summary>
    public JavaObject? CurrentDisplayable { get; set; }

    /// <summary>Raised after each painted frame so the shell can present it.</summary>
    public event Action? FramePainted;

    /// <summary>Raised when the MIDlet asks to be closed, so the shell can tear down.</summary>
    public event Action? DestroyRequested;

    /// <summary>Raised when the current displayable changes, so the shell can relabel soft keys.</summary>
    public event Action<JavaObject?>? DisplayableChanged;

    public IFontRasterizer FontRasterizer { get; set; } = NullFontRasterizer.Instance;

    public IStorageHost? Storage { get; set; }

    /// <summary>
    /// Record stores opened during this session, keyed by name.
    /// </summary>
    /// <remarks>
    /// MIDP requires that opening the same store twice yields the same object, so open stores
    /// have to be remembered. They live here rather than in a static so that two games running
    /// in the same process cannot see each other's saves.
    /// </remarks>
    public Dictionary<string, JavaRecordStore> OpenRecordStores { get; } = new(StringComparer.Ordinal);

    public IAudioHost? Audio { get; set; }

    public INetworkHost? Network { get; set; }

    public IDeviceFeedbackHost? Feedback { get; set; }

    /// <summary>Suite attributes from the JAD and manifest, read by <c>getAppProperty</c>.</summary>
    public IReadOnlyDictionary<string, string> SuiteProperties { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------------------------
    // Screen
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Resizes the screen, which happens when the user switches device profile mid-session.
    /// </summary>
    public void ResizeScreen(int width, int height)
    {
        Screen = new Surface(width, height, hasAlpha: false);
        Screen.Clear(PixelBlend.OpaqueMask);
        ScreenGraphics = new GraphicsContext(Screen);
    }

    /// <summary>Signals that a frame has been fully painted and is ready to present.</summary>
    public void NotifyFramePainted() => FramePainted?.Invoke();

    public void NotifyDestroyRequested() => DestroyRequested?.Invoke();

    public void NotifyDisplayableChanged(JavaObject? displayable) => DisplayableChanged?.Invoke(displayable);

    /// <summary>Area a canvas may draw into, which excludes any reserved soft-key bar.</summary>
    public int CanvasWidth => Screen.Width;

    public int CanvasHeight => Screen.Height - Device.SoftKeyBarHeight;

    // ---------------------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Records a key going down and queues the callback.
    /// </summary>
    /// <remarks>
    /// The state bits are updated on the calling thread rather than on the event thread so that
    /// a <c>GameCanvas</c> polling <c>getKeyStates</c> sees a press even if the event queue is
    /// backed up, which is exactly the situation a dropped frame creates.
    /// </remarks>
    public void PressKey(int keyCode)
    {
        int bit = DeviceProfile.GetKeyStateBit(Device.GetGameAction(keyCode));
        if (bit != 0)
        {
            Interlocked.Or(ref _keyStates, bit);
            Interlocked.Or(ref _latchedKeyStates, bit);
        }

        Events.Post(new MidpEvent(MidpEventKind.KeyPressed, keyCode));
    }

    public void ReleaseKey(int keyCode)
    {
        int bit = DeviceProfile.GetKeyStateBit(Device.GetGameAction(keyCode));
        if (bit != 0) Interlocked.And(ref _keyStates, ~bit);

        Events.Post(new MidpEvent(MidpEventKind.KeyReleased, keyCode));
    }

    public void RepeatKey(int keyCode)
    {
        if (Device.HasRepeatEvents) Events.Post(new MidpEvent(MidpEventKind.KeyRepeated, keyCode));
    }

    public void MovePointer(MidpEventKind kind, int x, int y) => Events.Post(new MidpEvent(kind, x, y));

    /// <summary>
    /// Reads and clears the latched key state for <c>GameCanvas.getKeyStates</c>.
    /// </summary>
    /// <remarks>
    /// The specification says a key pressed and released between two polls must still be
    /// observed once. That is why presses latch: without it, a tap shorter than a frame would
    /// vanish, and on a game running at fifteen frames a second taps often are.
    /// </remarks>
    public int TakeKeyStates()
    {
        int latched = Interlocked.Exchange(ref _latchedKeyStates, 0);
        return _keyStates | latched;
    }

    /// <summary>Clears all input state, used when a game is paused or replaced.</summary>
    public void ResetInput()
    {
        Interlocked.Exchange(ref _keyStates, 0);
        Interlocked.Exchange(ref _latchedKeyStates, 0);
    }

    // ---------------------------------------------------------------------------------
    // Fonts
    // ---------------------------------------------------------------------------------

    /// <summary>Rasterises a font once and reuses it for every later request of the same triple.</summary>
    public BitmapFont GetFont(int face, int style, int size)
    {
        var request = new FontRequest(face, style, size);

        lock (_fontGate)
        {
            if (_fontCache.TryGetValue(request, out BitmapFont? cached)) return cached;

            BitmapFont created = FontRasterizer.Rasterize(request);
            _fontCache[request] = created;
            return created;
        }
    }

    /// <summary>Drops rasterised fonts, used when the host swaps in a different rasteriser.</summary>
    public void ClearFontCache()
    {
        lock (_fontGate)
        {
            _fontCache.Clear();
        }
    }

    // ---------------------------------------------------------------------------------
    // Convenience
    // ---------------------------------------------------------------------------------

    /// <summary>Resolves the runtime from a virtual machine, for use inside native methods.</summary>
    public static MidpRuntime Of(VirtualMachine vm) => vm.Services.Require<MidpRuntime>();
}
