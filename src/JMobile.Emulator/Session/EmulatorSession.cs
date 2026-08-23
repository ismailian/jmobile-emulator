using System.Diagnostics;
using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Emulator.Suites;
using JMobile.Runtime;
using JMobile.Runtime.Graphics;
using JMobile.Runtime.Host;
using JMobile.Runtime.Midp;

namespace JMobile.Emulator.Session;

/// <summary>Lifecycle states a session moves through.</summary>
public enum SessionState
{
    Created,
    Starting,
    Running,
    Paused,
    Stopped,
    Failed,
}

/// <summary>A command the user can trigger, as surfaced to the shell's soft keys and menu.</summary>
/// <param name="Label">Text to show.</param>
/// <param name="Type">One of the <c>Command</c> type constants.</param>
/// <param name="Handle">The Java <c>Command</c> object to deliver.</param>
public readonly record struct SoftCommand(string Label, int Type, JavaObject Handle);

/// <summary>Everything needed to start one game.</summary>
public sealed class EmulatorSessionOptions
{
    public required MidletSuite Suite { get; init; }

    /// <summary>Which MIDlet to launch; defaults to the suite's first.</summary>
    public string? MidletClass { get; init; }

    public required DeviceProfile Device { get; init; }

    /// <summary>Directory this game's record stores live in.</summary>
    public required string SaveDirectory { get; init; }

    public IFontRasterizer? FontRasterizer { get; init; }

    public IAudioHost? Audio { get; init; }

    public INetworkHost? Network { get; init; }

    public IDeviceFeedbackHost? Feedback { get; init; }
}

/// <summary>
/// One running game: its virtual machine, its MIDP runtime, and the thread that delivers
/// callbacks into it.
/// </summary>
/// <remarks>
/// This is the facade the desktop shell talks to. The shell never sees a class loader, a
/// bytecode frame or a Java object; it starts a session, hands it key presses, and blits the
/// screen buffer when the session says a frame is ready.
/// <para>
/// A session owns its virtual machine outright and is never reused. Stopping a game and
/// starting another creates a new session, which is what guarantees that a game leaving threads
/// running or corrupting its statics cannot affect the next one.
/// </para>
/// </remarks>
public sealed class EmulatorSession : IDisposable
{
    private readonly EmulatorSessionOptions _options;
    private readonly Lock _stateGate = new();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    private Thread? _eventThread;
    private JavaThread? _eventJavaThread;
    private volatile bool _stopping;
    private long _framesPainted;
    private long _lastFrameStamp;
    private double _framesPerSecond;

    public EmulatorSession(EmulatorSessionOptions options)
    {
        _options = options;

        var vmOptions = new VirtualMachineOptions
        {
            ClassSource = options.Suite.Source,
            Modules = RuntimeLibrary.Midp20,
            StandardOut = text => Log(text, isError: false),
            StandardError = text => Log(text, isError: true),
        };

        Vm = new VirtualMachine(vmOptions);

        Runtime = new MidpRuntime(Vm, options.Device)
        {
            FontRasterizer = options.FontRasterizer ?? NullFontRasterizer.Instance,
            Storage = new FileStorageHost(options.SaveDirectory),
            Audio = options.Audio,
            Network = options.Network,
            Feedback = options.Feedback,
            SuiteProperties = options.Suite.Attributes,
        };

        Vm.Services.Register(Runtime);

        Runtime.FramePainted += OnFramePainted;
        Runtime.DestroyRequested += () => Stop();
        Runtime.DisplayableChanged += _ => RefreshCommands();
    }

    public VirtualMachine Vm { get; }

    public MidpRuntime Runtime { get; }

    public MidletSuite Suite => _options.Suite;

    public DeviceProfile Device => _options.Device;

    /// <summary>The phone screen. Blit directly out of <see cref="Surface.Pixels"/>.</summary>
    public Surface Screen => Runtime.Screen;

    public SessionState State { get; private set; } = SessionState.Created;

    /// <summary>Raised on the emulator thread whenever a frame has finished painting.</summary>
    public event Action? FrameReady;

    /// <summary>Raised for each line the guest writes to standard output or error.</summary>
    public event Action<string, bool>? Logged;

    public event Action<SessionState>? StateChanged;

    /// <summary>Raised when the set of soft commands changes, so the shell can relabel.</summary>
    public event Action<IReadOnlyList<SoftCommand>>? CommandsChanged;

    /// <summary>Commands attached to whatever is currently on screen.</summary>
    public IReadOnlyList<SoftCommand> Commands { get; private set; } = [];

    // ---------------------------------------------------------------------------------
    // Diagnostics
    // ---------------------------------------------------------------------------------

    public double FramesPerSecond => _framesPerSecond;

    public long FramesPainted => Interlocked.Read(ref _framesPainted);

    public int LoadedClassCount => Vm.LoadedClassCount;

    public TimeSpan Uptime => _uptime.Elapsed;

    /// <summary>Managed bytes currently allocated, a rough proxy for the guest's footprint.</summary>
    public long ManagedMemory => GC.GetTotalMemory(forceFullCollection: false);

    // ---------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------

    /// <summary>Starts the game on its own thread and returns immediately.</summary>
    public void Start()
    {
        lock (_stateGate)
        {
            if (State is not SessionState.Created) return;
            SetState(SessionState.Starting);
        }

        _eventThread = new Thread(EventLoop)
        {
            IsBackground = true,
            Name = "jmobile:event",
        };

        _eventThread.Start();
    }

    /// <summary>Delivers <c>pauseApp</c>, as a real handset does when a call arrives.</summary>
    public void Pause()
    {
        if (State != SessionState.Running) return;

        SetState(SessionState.Paused);
        Runtime.ResetInput();
        PostLifecycle("pauseApp", "()V");
    }

    public void Resume()
    {
        if (State != SessionState.Paused) return;

        SetState(SessionState.Running);
        PostLifecycle("startApp", "()V");
        Runtime.Events.PostRepaint(0, 0, Runtime.CanvasWidth, Runtime.CanvasHeight);
    }

    /// <summary>Stops the game, giving it a chance to save first.</summary>
    public void Stop()
    {
        lock (_stateGate)
        {
            if (_stopping || State is SessionState.Stopped) return;
            _stopping = true;
        }

        // destroyApp is where games flush their record stores, so it is given a real chance to
        // run before the machine is torn down, with a deadline so a hung game cannot block exit.
        try
        {
            if (Runtime.Midlet is not null && _eventJavaThread is not null && State == SessionState.Running)
            {
                Runtime.Events.Post(new MidpEvent(
                    MidpEventKind.Callback,
                    Argument: MakeLifecycleRunnable("destroyApp", "(Z)V", argument: 1)));

                var deadline = Stopwatch.StartNew();
                while (Runtime.Events.HasWork && deadline.Elapsed < TimeSpan.FromMilliseconds(750))
                    Thread.Sleep(10);
            }
        }
        catch (Exception error)
        {
            Log($"Error while shutting down: {error.Message}{Environment.NewLine}", isError: true);
        }

        Runtime.Events.Close();
        Vm.RequestShutdown();
        _options.Audio?.StopAll();

        _eventThread?.Join(TimeSpan.FromSeconds(2));
        SetState(SessionState.Stopped);
    }

    public void Dispose() => Stop();

    // ---------------------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------------------

    public void PressKey(int keyCode)
    {
        if (State == SessionState.Running) Runtime.PressKey(keyCode);
    }

    public void ReleaseKey(int keyCode)
    {
        if (State == SessionState.Running) Runtime.ReleaseKey(keyCode);
    }

    public void RepeatKey(int keyCode)
    {
        if (State == SessionState.Running) Runtime.RepeatKey(keyCode);
    }

    public void PointerPressed(int x, int y) => Pointer(MidpEventKind.PointerPressed, x, y);

    public void PointerReleased(int x, int y) => Pointer(MidpEventKind.PointerReleased, x, y);

    public void PointerDragged(int x, int y) => Pointer(MidpEventKind.PointerDragged, x, y);

    private void Pointer(MidpEventKind kind, int x, int y)
    {
        if (State == SessionState.Running && Device.HasPointerEvents) Runtime.MovePointer(kind, x, y);
    }

    /// <summary>Triggers one of the commands currently on screen.</summary>
    public void InvokeCommand(SoftCommand command)
    {
        if (State != SessionState.Running) return;

        Runtime.Events.Post(new MidpEvent(
            MidpEventKind.CommandAction,
            Target: Runtime.CurrentDisplayable,
            Argument: command.Handle));
    }

    // ---------------------------------------------------------------------------------
    // The event thread
    // ---------------------------------------------------------------------------------

    private void EventLoop()
    {
        JavaThread thread = Vm.CreateThread("event");
        _eventJavaThread = thread;

        try
        {
            LaunchMidlet(thread);
            SetState(SessionState.Running);

            while (!_stopping)
            {
                // A timeout rather than an indefinite wait, so shutdown is observed promptly
                // even when a game has gone quiet.
                if (!Runtime.Events.TryTake(50, out MidpEvent midpEvent)) continue;

                try
                {
                    MidpDispatcher.Deliver(thread, Runtime, midpEvent);
                }
                catch (JavaThrowable error)
                {
                    // One failed callback should not end the game. A great many J2ME titles
                    // throw during a callback and carry on perfectly well afterwards.
                    Report(error);
                }
            }
        }
        catch (VirtualMachineShutdownException)
        {
            // Normal: the game called notifyDestroyed or the user closed it.
        }
        catch (JavaThrowable error)
        {
            Report(error);
            SetState(SessionState.Failed);
        }
        catch (Exception error)
        {
            Log($"Emulator error: {error}{Environment.NewLine}", isError: true);
            SetState(SessionState.Failed);
        }
        finally
        {
            Vm.RemoveThread(thread);
            if (State != SessionState.Failed) SetState(SessionState.Stopped);
        }
    }

    /// <summary>Instantiates the MIDlet class and calls <c>startApp</c>.</summary>
    private void LaunchMidlet(JavaThread thread)
    {
        string className = ResolveMidletClass();

        JavaClass type = Vm.TryLoadClass(className)
                         ?? throw new InvalidOperationException(
                             $"MIDlet class '{className.Replace('/', '.')}' was not found in the suite.");

        Vm.EnsureInitialized(thread, type);

        JavaObject midlet = Vm.Construct(thread, type, "()V");
        Runtime.Midlet = midlet;

        Vm.CallVirtual(thread, midlet, "startApp", "()V");
    }

    private string ResolveMidletClass()
    {
        if (!string.IsNullOrWhiteSpace(_options.MidletClass))
            return _options.MidletClass.Replace('.', '/');

        if (Suite.Midlets.Count > 0) return Suite.Midlets[0].ClassName;

        // A suite whose manifest declares no MIDlets still usually contains one.
        IReadOnlyList<string> discovered = Suite.DiscoverMidletClasses();
        if (discovered.Count > 0) return discovered[0];

        throw new InvalidOperationException("The suite declares no MIDlet and none could be found in it.");
    }

    private void PostLifecycle(string method, string descriptor)
    {
        // Lifecycle callbacks go through the queue so they run on the event thread in order
        // with everything else, exactly as MIDP requires.
        if (MakeLifecycleRunnable(method, descriptor, argument: 0) is { } runnable)
            Runtime.Events.Post(new MidpEvent(MidpEventKind.Callback, Argument: runnable));
    }

    /// <summary>
    /// Wraps a lifecycle call as something the dispatcher can deliver.
    /// </summary>
    /// <remarks>
    /// The dispatcher's <c>Callback</c> event invokes <c>run()</c> on whatever object it is
    /// given, and a MIDlet is not a <c>Runnable</c>. A tiny adapter object bridges the two
    /// without special-casing lifecycle events in the dispatcher.
    /// </remarks>
    private JavaObject? MakeLifecycleRunnable(string method, string descriptor, int argument)
    {
        JavaObject? midlet = Runtime.Midlet;
        if (midlet is null || _eventJavaThread is null) return null;

        JavaClass adapterClass = Vm.LoadClass("javax/microedition/midlet/LifecycleAdapter");
        JavaObject adapter = Vm.NewInstance(adapterClass);
        adapter.SetField(0, JavaValue.Ref(midlet));
        adapter.SetField(1, JavaValue.Ref(Vm.NewString(method + descriptor)));
        adapter.SetField(2, JavaValue.Int(argument));
        return adapter;
    }

    private void RefreshCommands()
    {
        JavaObject? displayable = Runtime.CurrentDisplayable;
        if (displayable is null)
        {
            Commands = [];
            CommandsChanged?.Invoke(Commands);
            return;
        }

        var commands = new List<SoftCommand>();
        foreach (JavaObject command in LcduiModule.CommandsOf(displayable))
        {
            string label = VirtualMachine.ToHostString(command.GetField(0).Reference as JavaObject) ?? "Command";
            commands.Add(new SoftCommand(label, command.GetField(1).AsInt, command));
        }

        // Back and Exit belong on the right soft key; everything else queues on the left, which
        // is the convention every handset of the era followed.
        commands.Sort((left, right) => Rank(left.Type).CompareTo(Rank(right.Type)));

        Commands = commands;
        CommandsChanged?.Invoke(Commands);
    }

    private static int Rank(int commandType) => commandType switch
    {
        4 or 8 or 1 => 0,   // OK, ITEM, SCREEN
        5 => 1,             // HELP
        2 or 3 => 2,        // BACK, CANCEL
        6 or 7 => 3,        // STOP, EXIT
        _ => 1,
    };

    private void OnFramePainted()
    {
        Interlocked.Increment(ref _framesPainted);

        // A short exponential average: responsive enough to show a stall, smooth enough to read.
        long now = Stopwatch.GetTimestamp();
        long previous = Interlocked.Exchange(ref _lastFrameStamp, now);
        if (previous != 0)
        {
            double seconds = (now - previous) / (double)Stopwatch.Frequency;
            if (seconds > 0)
            {
                double instant = 1.0 / seconds;
                _framesPerSecond = _framesPerSecond == 0 ? instant : (_framesPerSecond * 0.9) + (instant * 0.1);
            }
        }

        FrameReady?.Invoke();
    }

    private void Report(JavaThrowable error)
    {
        var report = new System.Text.StringBuilder();
        report.Append("Uncaught ").AppendLine(error.Message);
        foreach (string frame in error.JavaStackTrace) report.Append("    at ").AppendLine(frame);
        Log(report.ToString(), isError: true);
    }

    private void Log(string text, bool isError) => Logged?.Invoke(text, isError);

    private void SetState(SessionState state)
    {
        if (State == state) return;

        State = state;
        StateChanged?.Invoke(state);
    }
}
