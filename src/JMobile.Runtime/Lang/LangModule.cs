using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Core.Native;
using JMobile.Runtime.Host;
using JMobile.Runtime.Midp;

namespace JMobile.Runtime.Lang;

/// <summary>
/// A <c>java.lang.Thread</c> instance together with the interpreter thread that runs it.
/// </summary>
public sealed class JavaThreadObject(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public JavaThread? Execution { get; set; }

    public Thread? Host { get; set; }

    public string Name { get; set; } = "Thread";

    public int Priority { get; set; } = 5;

    public volatile bool Started;

    public volatile bool Finished;
}

/// <summary>
/// The core of <c>java.lang</c>: Object, Class, System, Runtime, Math, Thread and the throwable
/// hierarchy.
/// </summary>
/// <remarks>
/// Everything here is a native method rather than interpreted bytecode. That is not just a
/// speed decision: implementing <c>Object</c> and <c>Class</c> in bytecode would require the
/// virtual machine to load a class library before it can load a class library.
/// </remarks>
public sealed class LangModule : IRuntimeModule
{
    public string Name => "java.lang";

    public void Register(RuntimeRegistry registry)
    {
        RegisterObject(registry);
        RegisterClass(registry);
        RegisterMath(registry);
        RegisterSystem(registry);
        RegisterRuntime(registry);
        RegisterThread(registry);
        RegisterThrowables(registry);
    }

    // ---------------------------------------------------------------------------------
    // Object
    // ---------------------------------------------------------------------------------

    private static void RegisterObject(RuntimeRegistry registry)
    {
        registry.Define(WellKnown.Object, builder => builder
            .ExtendsNothing()
            .DefaultConstructor()
            .Method("hashCode", "()I", static (_, args) => JavaValue.Int(args.This.IdentityHashCode))
            .Method("equals", "(Ljava/lang/Object;)Z",
                static (_, args) => JavaValue.Bool(ReferenceEquals(args.This, args.Object(0))))
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread,
                    $"{args.This.Class.DisplayName}@{args.This.IdentityHashCode:x}"))
            .Method("getClass", "()Ljava/lang/Class;",
                static (thread, args) => JavaValue.Ref(thread.Vm.GetClassMirror(args.This.Class)))
            .Method("notify", "()V", static (thread, args) =>
            {
                RequireMonitor(thread, args.This);
                args.This.MonitorNotify();
                return JavaValue.Void;
            })
            .Method("notifyAll", "()V", static (thread, args) =>
            {
                RequireMonitor(thread, args.This);
                args.This.MonitorNotifyAll();
                return JavaValue.Void;
            })
            .Method("wait", "()V", static (thread, args) => Wait(thread, args.This, 0))
            .Method("wait", "(J)V", static (thread, args) => Wait(thread, args.This, args.Long(0)))
            .Method("wait", "(JI)V", static (thread, args) => Wait(thread, args.This, args.Long(0))));

        registry.DefineInterface(WellKnown.Runnable, builder =>
            builder.InterfaceMethod("run", "()V"));
    }

    private static void RequireMonitor(JavaThread thread, JavaObject target)
    {
        if (!target.HoldsMonitor) throw thread.Vm.NewIllegalMonitorStateException(thread);
    }

    private static JavaValue Wait(JavaThread thread, JavaObject target, long timeout)
    {
        RequireMonitor(thread, target);

        try
        {
            // A zero timeout means wait indefinitely, but the emulator must still be able to
            // shut a game down, so an indefinite wait is polled rather than truly unbounded.
            if (timeout <= 0)
            {
                while (!thread.AbortRequested && !target.MonitorWait(250))
                {
                    if (thread.InterruptRequested) break;
                }
            }
            else
            {
                target.MonitorWait((int)Math.Min(timeout, int.MaxValue));
            }
        }
        catch (ThreadInterruptedException)
        {
            thread.InterruptRequested = false;
            throw thread.Vm.NewInterruptedException(thread);
        }

        if (thread.AbortRequested) throw new VirtualMachineShutdownException("Wait aborted at shutdown.");

        if (thread.InterruptRequested)
        {
            thread.InterruptRequested = false;
            throw thread.Vm.NewInterruptedException(thread);
        }

        return JavaValue.Void;
    }

    // ---------------------------------------------------------------------------------
    // Class
    // ---------------------------------------------------------------------------------

    private static void RegisterClass(RuntimeRegistry registry)
    {
        registry.Define(WellKnown.Class, builder => builder
            .AsFinal()
            .Method("getName", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, Represented(args.This).DisplayName))
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
            {
                JavaClass type = Represented(args.This);
                return RuntimeSupport.Str(thread,
                    (type.IsInterface ? "interface " : "class ") + type.DisplayName);
            })
            .Method("isInterface", "()Z",
                static (_, args) => JavaValue.Bool(Represented(args.This).IsInterface))
            .Method("isArray", "()Z",
                static (_, args) => JavaValue.Bool(Represented(args.This).IsArray))
            .Method("isInstance", "(Ljava/lang/Object;)Z",
                static (_, args) => JavaValue.Bool(Represented(args.This).IsInstance(args.Object(0))))
            .Method("isAssignableFrom", "(Ljava/lang/Class;)Z", static (_, args) =>
            {
                if (args.Object(0) is not JavaClassMirror other) return JavaValue.Bool(false);
                return JavaValue.Bool(Represented(args.This).IsAssignableFrom(other.Represented));
            })
            .Method("newInstance", "()Ljava/lang/Object;", static (thread, args) =>
            {
                JavaClass type = Represented(args.This);
                if (type.IsAbstract || type.IsInterface || type.IsArray)
                {
                    throw thread.Vm.NewException(thread, "java/lang/InstantiationException",
                        type.DisplayName);
                }
                return JavaValue.Ref(thread.Vm.Construct(thread, type, "()V"));
            })
            .Method("getResourceAsStream", "(Ljava/lang/String;)Ljava/io/InputStream;",
                static (thread, args) =>
                {
                    string? path = args.String(0);
                    if (path is null) return JavaValue.Null;

                    byte[]? data = thread.Vm.GetResource(path);
                    return data is null
                        ? JavaValue.Null
                        : JavaValue.Ref(Io.IoModule.NewByteArrayInputStream(thread, data));
                })
            .StaticMethod("forName", "(Ljava/lang/String;)Ljava/lang/Class;", static (thread, args) =>
            {
                string name = RuntimeSupport.RequireString(thread, args.String(0), "className");
                JavaClass? type = thread.Vm.TryLoadClass(name.Replace('.', '/'));
                if (type is null)
                    throw thread.Vm.NewException(thread, WellKnown.ClassNotFoundException, name);

                thread.Vm.EnsureInitialized(thread, type);
                return JavaValue.Ref(thread.Vm.GetClassMirror(type));
            }));
    }

    private static JavaClass Represented(JavaObject mirror) =>
        mirror is JavaClassMirror typed
            ? typed.Represented
            : throw new InvalidOperationException("Receiver is not a class mirror.");

    // ---------------------------------------------------------------------------------
    // Math
    // ---------------------------------------------------------------------------------

    private static void RegisterMath(RuntimeRegistry registry)
    {
        registry.Define("java/lang/Math", builder => builder
            .AsFinal()
            .Constant("E", Math.E)
            .Constant("PI", Math.PI)
            .StaticMethod("abs", "(I)I", static (_, args) => JavaValue.Int(Math.Abs(args.Int(0))))
            .StaticMethod("abs", "(J)J", static (_, args) => JavaValue.Long(Math.Abs(args.Long(0))))
            .StaticMethod("abs", "(F)F", static (_, args) => JavaValue.Float(Math.Abs(args.Float(0))))
            .StaticMethod("abs", "(D)D", static (_, args) => JavaValue.Double(Math.Abs(args.Double(0))))
            .StaticMethod("max", "(II)I", static (_, args) => JavaValue.Int(Math.Max(args.Int(0), args.Int(1))))
            .StaticMethod("max", "(JJ)J", static (_, args) => JavaValue.Long(Math.Max(args.Long(0), args.Long(1))))
            .StaticMethod("max", "(FF)F", static (_, args) => JavaValue.Float(Math.Max(args.Float(0), args.Float(1))))
            .StaticMethod("max", "(DD)D", static (_, args) => JavaValue.Double(Math.Max(args.Double(0), args.Double(1))))
            .StaticMethod("min", "(II)I", static (_, args) => JavaValue.Int(Math.Min(args.Int(0), args.Int(1))))
            .StaticMethod("min", "(JJ)J", static (_, args) => JavaValue.Long(Math.Min(args.Long(0), args.Long(1))))
            .StaticMethod("min", "(FF)F", static (_, args) => JavaValue.Float(Math.Min(args.Float(0), args.Float(1))))
            .StaticMethod("min", "(DD)D", static (_, args) => JavaValue.Double(Math.Min(args.Double(0), args.Double(1))))
            .StaticMethod("sqrt", "(D)D", static (_, args) => JavaValue.Double(Math.Sqrt(args.Double(0))))
            .StaticMethod("sin", "(D)D", static (_, args) => JavaValue.Double(Math.Sin(args.Double(0))))
            .StaticMethod("cos", "(D)D", static (_, args) => JavaValue.Double(Math.Cos(args.Double(0))))
            .StaticMethod("tan", "(D)D", static (_, args) => JavaValue.Double(Math.Tan(args.Double(0))))
            .StaticMethod("asin", "(D)D", static (_, args) => JavaValue.Double(Math.Asin(args.Double(0))))
            .StaticMethod("acos", "(D)D", static (_, args) => JavaValue.Double(Math.Acos(args.Double(0))))
            .StaticMethod("atan", "(D)D", static (_, args) => JavaValue.Double(Math.Atan(args.Double(0))))
            .StaticMethod("atan2", "(DD)D",
                static (_, args) => JavaValue.Double(Math.Atan2(args.Double(0), args.Double(1))))
            .StaticMethod("pow", "(DD)D",
                static (_, args) => JavaValue.Double(Math.Pow(args.Double(0), args.Double(1))))
            .StaticMethod("exp", "(D)D", static (_, args) => JavaValue.Double(Math.Exp(args.Double(0))))
            .StaticMethod("log", "(D)D", static (_, args) => JavaValue.Double(Math.Log(args.Double(0))))
            .StaticMethod("ceil", "(D)D", static (_, args) => JavaValue.Double(Math.Ceiling(args.Double(0))))
            .StaticMethod("floor", "(D)D", static (_, args) => JavaValue.Double(Math.Floor(args.Double(0))))
            .StaticMethod("toRadians", "(D)D",
                static (_, args) => JavaValue.Double(args.Double(0) * Math.PI / 180.0))
            .StaticMethod("toDegrees", "(D)D",
                static (_, args) => JavaValue.Double(args.Double(0) * 180.0 / Math.PI))
            .StaticMethod("random", "()D", static (_, _) => JavaValue.Double(Random.Shared.NextDouble())));
    }

    // ---------------------------------------------------------------------------------
    // System
    // ---------------------------------------------------------------------------------

    private static void RegisterSystem(RuntimeRegistry registry)
    {
        registry.Define(WellKnown.System, builder => builder
            .AsFinal()
            .StaticField("out", "Ljava/io/PrintStream;")
            .StaticField("err", "Ljava/io/PrintStream;")
            .StaticInitializer(static (thread, _) =>
            {
                // System.out and System.err hold objects, which cannot exist until the VM is
                // running, so they are built here rather than declared as constants.
                JavaClass system = thread.Vm.LoadClass(WellKnown.System);
                VirtualMachineOptions options = thread.Vm.Options;

                system.StaticFields[0] = JavaValue.Ref(
                    Io.IoModule.NewPrintStream(thread, options.StandardOut ?? Console.Write));
                system.StaticFields[1] = JavaValue.Ref(
                    Io.IoModule.NewPrintStream(thread, options.StandardError ?? Console.Error.Write));

                return JavaValue.Void;
            })
            .StaticMethod("arraycopy", "(Ljava/lang/Object;ILjava/lang/Object;II)V",
                static (thread, args) =>
                {
                    RuntimeSupport.ArrayCopy(
                        thread, args.Object(0), args.Int(1), args.Object(2), args.Int(3), args.Int(4));
                    return JavaValue.Void;
                })
            .StaticMethod("currentTimeMillis", "()J",
                static (_, _) => JavaValue.Long(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
            .StaticMethod("identityHashCode", "(Ljava/lang/Object;)I",
                static (_, args) => JavaValue.Int(args.Object(0)?.IdentityHashCode ?? 0))
            .StaticMethod("getProperty", "(Ljava/lang/String;)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, GetProperty(thread, args.String(0))))
            .StaticMethod("gc", "()V", static (_, _) =>
            {
                // Deliberately a no-op. A MIDlet calling System.gc() was asking a phone with two
                // megabytes of heap to reclaim memory; forcing a full collection here would only
                // cause a frame hitch on a machine that has memory to spare.
                return JavaValue.Void;
            })
            .StaticMethod("exit", "(I)V", static (thread, _) =>
            {
                MidpRuntime.Of(thread.Vm).NotifyDestroyRequested();
                throw new VirtualMachineShutdownException("System.exit was called.");
            }));
    }

    private static string? GetProperty(JavaThread thread, string? key)
    {
        if (key is null) return null;

        if (thread.Vm.Options.SystemProperties.TryGetValue(key, out string? configured))
            return configured;

        MidpRuntime? runtime = thread.Vm.Services.Get<MidpRuntime>();
        DeviceProfile? device = runtime?.Device;

        return key switch
        {
            "microedition.platform" => device?.Platform ?? "j2me",
            "microedition.encoding" => device?.Encoding ?? "ISO-8859-1",
            "microedition.configuration" => "CLDC-1.1",
            "microedition.profiles" => "MIDP-2.0",
            "microedition.locale" => device?.Locale ?? "en-US",
            "microedition.commports" => "",
            "microedition.hostname" => "localhost",
            _ => null,
        };
    }

    // ---------------------------------------------------------------------------------
    // Runtime
    // ---------------------------------------------------------------------------------

    private static void RegisterRuntime(RuntimeRegistry registry)
    {
        registry.Define("java/lang/Runtime", builder => builder
            .StaticField("instance", "Ljava/lang/Runtime;")
            .StaticMethod("getRuntime", "()Ljava/lang/Runtime;", static (thread, _) =>
            {
                JavaClass type = thread.Vm.LoadClass("java/lang/Runtime");
                if (type.StaticFields[0].Reference is JavaObject existing) return JavaValue.Ref(existing);

                JavaObject created = thread.Vm.NewInstance(type);
                type.StaticFields[0] = JavaValue.Ref(created);
                return JavaValue.Ref(created);
            })
            .Method("totalMemory", "()J", static (thread, _) =>
                JavaValue.Long(MidpRuntime.Of(thread.Vm).Device.HeapSize))
            .Method("freeMemory", "()J", static (thread, _) =>
            {
                // Games use the free/total ratio to decide how much to cache. Reporting a
                // plausible fraction of the emulated heap keeps those heuristics sane, where
                // reporting the desktop's real free memory would make them cache wildly.
                long total = MidpRuntime.Of(thread.Vm).Device.HeapSize;
                long used = Math.Min(total / 2, GC.GetTotalMemory(forceFullCollection: false) / 8);
                return JavaValue.Long(Math.Max(total / 8, total - used));
            })
            .Method("gc", "()V", static (_, _) => JavaValue.Void)
            .Method("exit", "(I)V", static (thread, _) =>
            {
                MidpRuntime.Of(thread.Vm).NotifyDestroyRequested();
                throw new VirtualMachineShutdownException("Runtime.exit was called.");
            }));
    }

    // ---------------------------------------------------------------------------------
    // Thread
    // ---------------------------------------------------------------------------------

    private static void RegisterThread(RuntimeRegistry registry)
    {
        registry.Define(WellKnown.Thread, builder => builder
            .Implements(WellKnown.Runnable)
            .AllocatesWith(static type => new JavaThreadObject(type, type.InstanceFieldCount))
            .Field("target", "Ljava/lang/Runnable;")
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Constructor("(Ljava/lang/Runnable;)V", static (_, args) =>
            {
                args.This.SetField(0, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .Constructor("(Ljava/lang/String;)V", static (_, args) =>
            {
                if (args.This is JavaThreadObject self) self.Name = args.String(0) ?? "Thread";
                return JavaValue.Void;
            })
            .Method("run", "()V", static (thread, args) =>
            {
                // The default body runs the Runnable given to the constructor. A subclass that
                // overrides run() never reaches here.
                if (args.This.GetField(0).Reference is JavaObject target)
                    thread.Vm.CallVirtual(thread, target, "run", "()V");
                return JavaValue.Void;
            })
            .Method("start", "()V", static (thread, args) => Start(thread, args.Self<JavaThreadObject>()))
            .Method("isAlive", "()Z", static (_, args) =>
            {
                var self = args.Self<JavaThreadObject>();
                return JavaValue.Bool(self.Started && !self.Finished);
            })
            .Method("join", "()V", static (thread, args) => Join(thread, args.Self<JavaThreadObject>(), 0))
            .Method("join", "(J)V",
                static (thread, args) => Join(thread, args.Self<JavaThreadObject>(), args.Long(0)))
            .Method("interrupt", "()V", static (_, args) =>
            {
                var self = args.Self<JavaThreadObject>();
                if (self.Execution is { } execution) execution.InterruptRequested = true;
                try
                {
                    self.Host?.Interrupt();
                }
                catch (ThreadStateException)
                {
                    // The thread had already finished; an interrupt is meaningless now.
                }
                return JavaValue.Void;
            })
            .Method("getPriority", "()I", static (_, args) => JavaValue.Int(args.Self<JavaThreadObject>().Priority))
            .Method("setPriority", "(I)V", static (_, args) =>
            {
                args.Self<JavaThreadObject>().Priority = Math.Clamp(args.Int(0), 1, 10);
                return JavaValue.Void;
            })
            .Method("getName", "()Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Self<JavaThreadObject>().Name))
            .Method("setName", "(Ljava/lang/String;)V", static (_, args) =>
            {
                args.Self<JavaThreadObject>().Name = args.String(0) ?? "Thread";
                return JavaValue.Void;
            })
            .Constant("MIN_PRIORITY", 1)
            .Constant("NORM_PRIORITY", 5)
            .Constant("MAX_PRIORITY", 10)
            .StaticMethod("sleep", "(J)V", static (thread, args) =>
            {
                long milliseconds = args.Long(0);
                if (milliseconds < 0)
                    throw thread.Vm.NewIllegalArgumentException(thread, "negative sleep duration");

                try
                {
                    // Sliced so that shutting the emulator down does not have to wait out a
                    // game that decided to sleep for thirty seconds.
                    long remaining = milliseconds;
                    while (remaining > 0 && !thread.AbortRequested)
                    {
                        int slice = (int)Math.Min(remaining, 100);
                        Thread.Sleep(slice);
                        remaining -= slice;

                        if (thread.InterruptRequested)
                        {
                            thread.InterruptRequested = false;
                            throw thread.Vm.NewInterruptedException(thread);
                        }
                    }
                }
                catch (ThreadInterruptedException)
                {
                    thread.InterruptRequested = false;
                    throw thread.Vm.NewInterruptedException(thread);
                }

                if (thread.AbortRequested)
                    throw new VirtualMachineShutdownException("Sleep aborted at shutdown.");

                return JavaValue.Void;
            })
            .StaticMethod("yield", "()V", static (_, _) =>
            {
                Thread.Yield();
                return JavaValue.Void;
            })
            .StaticMethod("currentThread", "()Ljava/lang/Thread;", static (thread, _) =>
            {
                if (thread.ThreadObject is { } existing) return JavaValue.Ref(existing);

                // A thread created by the host rather than by the guest still needs a Thread
                // object the moment the game asks for one.
                JavaObject created = thread.Vm.NewInstance(thread.Vm.LoadClass(WellKnown.Thread));
                if (created is JavaThreadObject typed)
                {
                    typed.Execution = thread;
                    typed.Host = Thread.CurrentThread;
                    typed.Name = thread.Name;
                    typed.Started = true;
                }
                thread.ThreadObject = created;
                return JavaValue.Ref(created);
            })
            .StaticMethod("activeCount", "()I",
                static (thread, _) => JavaValue.Int(thread.Vm.Threads.Count)));
    }

    /// <summary>
    /// Starts a guest thread on a real host thread.
    /// </summary>
    /// <remarks>
    /// Guest threads map one-to-one onto CLR threads rather than being multiplexed. Games use
    /// threads for exactly two things — a render loop and a loader — so the count stays in
    /// single digits, and real threads give correct blocking behaviour for <c>sleep</c>,
    /// <c>wait</c> and I/O for free.
    /// </remarks>
    private static JavaValue Start(JavaThread caller, JavaThreadObject self)
    {
        if (self.Started)
            throw caller.Vm.NewIllegalStateException(caller, "thread already started");

        self.Started = true;
        VirtualMachine vm = caller.Vm;

        JavaThread execution = vm.CreateThread(self.Name);
        execution.ThreadObject = self;
        self.Execution = execution;

        var host = new Thread(() =>
        {
            try
            {
                vm.CallVirtual(execution, self, "run", "()V");
            }
            catch (JavaThrowable error)
            {
                ReportUncaught(vm, self.Name, error);
            }
            catch (VirtualMachineShutdownException)
            {
                // Expected while the emulator is closing.
            }
            catch (ThreadInterruptedException)
            {
                // Expected when the thread is interrupted during shutdown.
            }
            catch (Exception error)
            {
                vm.Options.StandardError?.Invoke(
                    $"Internal error on thread {self.Name}: {error}{Environment.NewLine}");
            }
            finally
            {
                self.Finished = true;
                vm.RemoveThread(execution);
            }
        })
        {
            IsBackground = true,
            Name = "midlet:" + self.Name,
        };

        self.Host = host;
        execution.HostThread = host;
        host.Start();

        return JavaValue.Void;
    }

    private static void ReportUncaught(VirtualMachine vm, string threadName, JavaThrowable error)
    {
        var report = new System.Text.StringBuilder();
        report.Append("Uncaught exception on thread ").Append(threadName).Append(": ")
            .AppendLine(error.Message);
        foreach (string frame in error.JavaStackTrace) report.Append("\tat ").AppendLine(frame);

        (vm.Options.StandardError ?? Console.Error.Write).Invoke(report.ToString());
    }

    private static JavaValue Join(JavaThread caller, JavaThreadObject self, long timeout)
    {
        Thread? host = self.Host;
        if (host is null) return JavaValue.Void;

        try
        {
            if (timeout <= 0)
            {
                while (host.IsAlive && !caller.AbortRequested) host.Join(100);
            }
            else
            {
                host.Join((int)Math.Min(timeout, int.MaxValue));
            }
        }
        catch (ThreadInterruptedException)
        {
            caller.InterruptRequested = false;
            throw caller.Vm.NewInterruptedException(caller);
        }

        return JavaValue.Void;
    }

    // ---------------------------------------------------------------------------------
    // Throwables
    // ---------------------------------------------------------------------------------

    private static void RegisterThrowables(RuntimeRegistry registry)
    {
        registry.Define(WellKnown.Throwable, builder => builder
            .AllocatesWith(static type => new JavaThrowableInstance(type, type.InstanceFieldCount))
            .Field("detailMessage", "Ljava/lang/String;")
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Constructor("(Ljava/lang/String;)V", static (_, args) =>
            {
                args.This.SetField(0, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .Method("getMessage", "()Ljava/lang/String;",
                static (_, args) => JavaValue.Ref(args.This.GetField(0).Reference))
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
            {
                string? message = VirtualMachine.ToHostString(args.This.GetField(0).Reference as JavaObject);
                string name = args.This.Class.DisplayName;
                return RuntimeSupport.Str(thread, message is null ? name : name + ": " + message);
            })
            .Method("printStackTrace", "()V", static (thread, args) =>
            {
                var report = new System.Text.StringBuilder();
                string? message = VirtualMachine.ToHostString(args.This.GetField(0).Reference as JavaObject);
                report.Append(args.This.Class.DisplayName);
                if (message is not null) report.Append(": ").Append(message);
                report.AppendLine();

                if (args.This is JavaThrowableInstance instance)
                {
                    foreach (string frame in instance.StackTrace) report.Append("\tat ").AppendLine(frame);
                }

                (thread.Vm.Options.StandardError ?? Console.Error.Write).Invoke(report.ToString());
                return JavaValue.Void;
            }));

        // The rest of the hierarchy is pure structure: each type adds only the identity that
        // makes a catch clause select it.
        registry.DefineThrowable(WellKnown.Error, WellKnown.Throwable);
        registry.DefineThrowable(WellKnown.Exception, WellKnown.Throwable);
        registry.DefineThrowable(WellKnown.RuntimeException, WellKnown.Exception);

        registry.DefineThrowable(WellKnown.VirtualMachineError, WellKnown.Error);
        registry.DefineThrowable(WellKnown.OutOfMemoryError, WellKnown.VirtualMachineError);
        registry.DefineThrowable(WellKnown.StackOverflowError, WellKnown.VirtualMachineError);
        registry.DefineThrowable(WellKnown.InternalError, WellKnown.VirtualMachineError);
        registry.DefineThrowable("java/lang/LinkageError", WellKnown.Error);
        registry.DefineThrowable(WellKnown.NoClassDefFoundError, "java/lang/LinkageError");
        registry.DefineThrowable(WellKnown.IncompatibleClassChangeError, "java/lang/LinkageError");
        registry.DefineThrowable(WellKnown.NoSuchMethodError, WellKnown.IncompatibleClassChangeError);
        registry.DefineThrowable(WellKnown.NoSuchFieldError, WellKnown.IncompatibleClassChangeError);
        registry.DefineThrowable(WellKnown.AbstractMethodError, WellKnown.IncompatibleClassChangeError);

        registry.DefineThrowable(WellKnown.ClassNotFoundException, WellKnown.Exception);
        registry.DefineThrowable("java/lang/InstantiationException", WellKnown.Exception);
        registry.DefineThrowable("java/lang/IllegalAccessException", WellKnown.Exception);
        registry.DefineThrowable(WellKnown.InterruptedException, WellKnown.Exception);

        registry.DefineThrowable(WellKnown.NullPointerException, WellKnown.RuntimeException);
        registry.DefineThrowable(WellKnown.ArithmeticException, WellKnown.RuntimeException);
        registry.DefineThrowable(WellKnown.ClassCastException, WellKnown.RuntimeException);
        registry.DefineThrowable(WellKnown.ArrayStoreException, WellKnown.RuntimeException);
        registry.DefineThrowable(WellKnown.IllegalArgumentException, WellKnown.RuntimeException);
        registry.DefineThrowable(WellKnown.NumberFormatException, WellKnown.IllegalArgumentException);
        registry.DefineThrowable(WellKnown.IllegalStateException, WellKnown.RuntimeException);
        registry.DefineThrowable(WellKnown.IllegalMonitorStateException, WellKnown.RuntimeException);
        registry.DefineThrowable(WellKnown.IndexOutOfBoundsException, WellKnown.RuntimeException);
        registry.DefineThrowable(WellKnown.ArrayIndexOutOfBoundsException, WellKnown.IndexOutOfBoundsException);
        registry.DefineThrowable(WellKnown.StringIndexOutOfBoundsException, WellKnown.IndexOutOfBoundsException);
        registry.DefineThrowable(WellKnown.NegativeArraySizeException, WellKnown.RuntimeException);
        registry.DefineThrowable(WellKnown.UnsupportedOperationException, WellKnown.RuntimeException);
        registry.DefineThrowable("java/lang/SecurityException", WellKnown.RuntimeException);
    }
}
