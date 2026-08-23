using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Core.Native;
using JMobile.Runtime.Host;

namespace JMobile.Runtime.Midp;

/// <summary>
/// The MIDlet lifecycle and the core of <c>javax.microedition.lcdui</c>: Display, Displayable,
/// Canvas, Command and Ticker.
/// </summary>
/// <remarks>
/// Field slots are fixed by the order fields are declared on each class and are referenced
/// through the named constants below. Resolving them by name on every access would be correct
/// but wasteful, since these are read on the paint path.
/// </remarks>
public sealed class LcduiModule : IRuntimeModule
{
    public string Name => "javax.microedition.lcdui";

    // Displayable field slots, in declaration order.
    internal const int TitleSlot = 0;
    internal const int CommandsSlot = 1;
    internal const int ListenerSlot = 2;
    internal const int TickerSlot = 3;

    /// <summary>First slot a subclass of Displayable may use.</summary>
    internal const int DisplayableFieldCount = 4;

    public void Register(RuntimeRegistry registry)
    {
        RegisterMidlet(registry);
        RegisterCommand(registry);
        RegisterDisplayable(registry);
        RegisterDisplay(registry);
        RegisterCanvas(registry);
        RegisterTicker(registry);
    }

    // ---------------------------------------------------------------------------------
    // MIDlet
    // ---------------------------------------------------------------------------------

    private static void RegisterMidlet(RuntimeRegistry registry)
    {
        registry.DefineThrowable("javax/microedition/midlet/MIDletStateChangeException", WellKnown.Exception);

        registry.Define("javax/microedition/midlet/MIDlet", builder => builder
            .AsAbstract()
            .Constructor("()V", static (thread, args) =>
            {
                // The suite's MIDlet subclass is instantiated by the emulator, and this is the
                // first point at which the runtime learns which instance it is.
                MidpRuntime.Of(thread.Vm).Midlet = args.This;
                return JavaValue.Void;
            })
            .AbstractMethod("startApp", "()V")
            .AbstractMethod("pauseApp", "()V")
            .AbstractMethod("destroyApp", "(Z)V")
            .Method("getAppProperty", "(Ljava/lang/String;)Ljava/lang/String;", static (thread, args) =>
            {
                string? key = args.String(0);
                if (key is null) return JavaValue.Null;

                MidpRuntime runtime = MidpRuntime.Of(thread.Vm);
                return runtime.SuiteProperties.TryGetValue(key, out string? value)
                    ? RuntimeSupport.Str(thread, value)
                    : JavaValue.Null;
            })
            .Method("notifyDestroyed", "()V", static (thread, _) =>
            {
                MidpRuntime.Of(thread.Vm).NotifyDestroyRequested();
                return JavaValue.Void;
            })
            .Method("notifyPaused", "()V", static (_, _) => JavaValue.Void)
            .Method("resumeRequest", "()V", static (_, _) => JavaValue.Void)
            .Method("platformRequest", "(Ljava/lang/String;)Z", static (_, _) =>
                // Opening a URL or dialling a number is denied: a twenty-year-old game asking
                // the platform to launch something is never something the user asked for.
                JavaValue.Bool(false))
            .Method("checkPermission", "(Ljava/lang/String;)I", static (_, _) => JavaValue.Int(1)));

        // A Runnable that calls one method on one object. The event queue delivers Runnables,
        // and MIDlet lifecycle calls have to arrive through that same queue so they are ordered
        // correctly against paint and input; this adapter is what lets them.
        registry.Define("javax/microedition/midlet/LifecycleAdapter", builder => builder
            .Implements(WellKnown.Runnable)
            .Field("target", "Ljava/lang/Object;")
            .Field("member", "Ljava/lang/String;")
            .Field("argument", "I")
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Method("run", "()V", static (thread, args) =>
            {
                if (args.This.GetField(0).Reference is not JavaObject target) return JavaValue.Void;

                string? member = VirtualMachine.ToHostString(args.This.GetField(1).Reference as JavaObject);
                if (member is null) return JavaValue.Void;

                int split = member.IndexOf('(');
                if (split <= 0) return JavaValue.Void;

                string name = member[..split];
                string descriptor = member[split..];

                if (descriptor == "(Z)V")
                {
                    thread.Vm.CallVirtual(thread, target, name, descriptor,
                        JavaValue.Bool(args.This.GetField(2).AsInt != 0));
                }
                else
                {
                    thread.Vm.CallVirtual(thread, target, name, descriptor);
                }

                return JavaValue.Void;
            }));
    }

    // ---------------------------------------------------------------------------------
    // Command
    // ---------------------------------------------------------------------------------

    private static void RegisterCommand(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/Command", builder => builder
            .Field("label", "Ljava/lang/String;")
            .Field("type", "I")
            .Field("priority", "I")
            .Field("longLabel", "Ljava/lang/String;")
            .Constant("SCREEN", 1)
            .Constant("BACK", 2)
            .Constant("CANCEL", 3)
            .Constant("OK", 4)
            .Constant("HELP", 5)
            .Constant("STOP", 6)
            .Constant("EXIT", 7)
            .Constant("ITEM", 8)
            .Constructor("(Ljava/lang/String;II)V", static (_, args) =>
            {
                args.This.SetField(0, JavaValue.Ref(args.Object(0)));
                args.This.SetField(1, JavaValue.Int(args.Int(1)));
                args.This.SetField(2, JavaValue.Int(args.Int(2)));
                return JavaValue.Void;
            })
            .Constructor("(Ljava/lang/String;Ljava/lang/String;II)V", static (_, args) =>
            {
                args.This.SetField(0, JavaValue.Ref(args.Object(0)));
                args.This.SetField(3, JavaValue.Ref(args.Object(1)));
                args.This.SetField(1, JavaValue.Int(args.Int(2)));
                args.This.SetField(2, JavaValue.Int(args.Int(3)));
                return JavaValue.Void;
            })
            .Method("getLabel", "()Ljava/lang/String;",
                static (_, args) => JavaValue.Ref(args.This.GetField(0).Reference))
            .Method("getLongLabel", "()Ljava/lang/String;",
                static (_, args) => JavaValue.Ref(args.This.GetField(3).Reference))
            .Method("getCommandType", "()I", static (_, args) => args.This.GetField(1))
            .Method("getPriority", "()I", static (_, args) => args.This.GetField(2)));

        registry.DefineInterface("javax/microedition/lcdui/CommandListener", builder => builder
            .InterfaceMethod("commandAction",
                "(Ljavax/microedition/lcdui/Command;Ljavax/microedition/lcdui/Displayable;)V"));

        registry.DefineInterface("javax/microedition/lcdui/ItemCommandListener", builder => builder
            .InterfaceMethod("commandAction",
                "(Ljavax/microedition/lcdui/Command;Ljavax/microedition/lcdui/Item;)V"));
    }

    // ---------------------------------------------------------------------------------
    // Displayable
    // ---------------------------------------------------------------------------------

    private static void RegisterDisplayable(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/Displayable", builder => builder
            .AsAbstract()
            .Field("title", "Ljava/lang/String;")
            .Field("commands", "Ljava/util/Vector;")
            .Field("commandListener", "Ljavax/microedition/lcdui/CommandListener;")
            .Field("ticker", "Ljavax/microedition/lcdui/Ticker;")
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Method("getWidth", "()I", static (thread, _) =>
                JavaValue.Int(MidpRuntime.Of(thread.Vm).CanvasWidth))
            .Method("getHeight", "()I", static (thread, _) =>
                JavaValue.Int(MidpRuntime.Of(thread.Vm).CanvasHeight))
            .Method("isShown", "()Z", static (thread, args) =>
                JavaValue.Bool(ReferenceEquals(MidpRuntime.Of(thread.Vm).CurrentDisplayable, args.This)))
            .Method("getTitle", "()Ljava/lang/String;",
                static (_, args) => JavaValue.Ref(args.This.GetField(TitleSlot).Reference))
            .Method("setTitle", "(Ljava/lang/String;)V", static (thread, args) =>
            {
                args.This.SetField(TitleSlot, JavaValue.Ref(args.Object(0)));
                MidpRuntime.Of(thread.Vm).NotifyDisplayableChanged(args.This);
                return JavaValue.Void;
            })
            .Method("getTicker", "()Ljavax/microedition/lcdui/Ticker;",
                static (_, args) => JavaValue.Ref(args.This.GetField(TickerSlot).Reference))
            .Method("setTicker", "(Ljavax/microedition/lcdui/Ticker;)V", static (_, args) =>
            {
                args.This.SetField(TickerSlot, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .Method("addCommand", "(Ljavax/microedition/lcdui/Command;)V", static (thread, args) =>
            {
                JavaObject command = RuntimeSupport.RequireRef(thread, args.Object(0), "cmd");
                Util.JavaVector commands = EnsureCommands(thread, args.This);
                if (!commands.Items.Contains(command)) commands.Items.Add(command);

                MidpRuntime.Of(thread.Vm).NotifyDisplayableChanged(args.This);
                return JavaValue.Void;
            })
            .Method("removeCommand", "(Ljavax/microedition/lcdui/Command;)V", static (thread, args) =>
            {
                if (args.This.GetField(CommandsSlot).Reference is Util.JavaVector commands)
                    commands.Items.Remove(args.Object(0));

                MidpRuntime.Of(thread.Vm).NotifyDisplayableChanged(args.This);
                return JavaValue.Void;
            })
            .Method("setCommandListener", "(Ljavax/microedition/lcdui/CommandListener;)V",
                static (_, args) =>
                {
                    args.This.SetField(ListenerSlot, JavaValue.Ref(args.Object(0)));
                    return JavaValue.Void;
                })
            .Method("sizeChanged", "(II)V", static (_, _) => JavaValue.Void));

        registry.Define("javax/microedition/lcdui/Screen", builder => builder
            .Extends("javax/microedition/lcdui/Displayable")
            .AsAbstract()
            .Constructor("()V", static (_, _) => JavaValue.Void));
    }

    /// <summary>Lazily creates the command vector, so a displayable with no commands has none.</summary>
    internal static Util.JavaVector EnsureCommands(JavaThread thread, JavaObject displayable)
    {
        if (displayable.GetField(CommandsSlot).Reference is Util.JavaVector existing) return existing;

        var created = (Util.JavaVector)thread.Vm.NewInstance(thread.Vm.LoadClass("java/util/Vector"));
        displayable.SetField(CommandsSlot, JavaValue.Ref(created));
        return created;
    }

    /// <summary>The commands currently attached to a displayable, newest last.</summary>
    public static IReadOnlyList<JavaObject> CommandsOf(JavaObject displayable) =>
        displayable.GetField(CommandsSlot).Reference is Util.JavaVector commands
            ? commands.Items.OfType<JavaObject>().ToArray()
            : [];

    // ---------------------------------------------------------------------------------
    // Display
    // ---------------------------------------------------------------------------------

    private static void RegisterDisplay(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/Display", builder => builder
            .AsFinal()
            .Constant("LIST_ELEMENT", 1)
            .Constant("CHOICE_GROUP_ELEMENT", 2)
            .Constant("ALERT", 3)
            .Constant("COLOR_BACKGROUND", 0)
            .Constant("COLOR_FOREGROUND", 1)
            .Constant("COLOR_HIGHLIGHTED_BACKGROUND", 2)
            .Constant("COLOR_HIGHLIGHTED_FOREGROUND", 3)
            .Constant("COLOR_BORDER", 4)
            .Constant("COLOR_HIGHLIGHTED_BORDER", 5)
            .StaticMethod("getDisplay",
                "(Ljavax/microedition/midlet/MIDlet;)Ljavax/microedition/lcdui/Display;",
                static (thread, args) =>
                {
                    MidpRuntime runtime = MidpRuntime.Of(thread.Vm);
                    if (runtime.Display is { } existing) return JavaValue.Ref(existing);

                    // MIDP gives a MIDlet exactly one Display for its whole lifetime, and games
                    // compare the reference, so it is created once and cached.
                    JavaObject created = thread.Vm.NewInstance(
                        thread.Vm.LoadClass("javax/microedition/lcdui/Display"));
                    runtime.Display = created;
                    runtime.Midlet ??= args.Object(0);
                    return JavaValue.Ref(created);
                })
            .Method("getCurrent", "()Ljavax/microedition/lcdui/Displayable;",
                static (thread, _) => JavaValue.Ref(MidpRuntime.Of(thread.Vm).CurrentDisplayable))
            .Method("setCurrent", "(Ljavax/microedition/lcdui/Displayable;)V", static (thread, args) =>
            {
                SetCurrent(thread, args.Object(0));
                return JavaValue.Void;
            })
            .Method("setCurrent",
                "(Ljavax/microedition/lcdui/Alert;Ljavax/microedition/lcdui/Displayable;)V",
                static (thread, args) =>
                {
                    // The alert is shown now; the second argument is what follows when it is
                    // dismissed, which the alert itself remembers.
                    JavaObject? alert = args.Object(0);
                    if (alert is not null)
                    {
                        FormsModule.SetAlertNext(alert, args.Object(1));
                        SetCurrent(thread, alert);
                    }
                    else
                    {
                        SetCurrent(thread, args.Object(1));
                    }
                    return JavaValue.Void;
                })
            .Method("isColor", "()Z", static (thread, _) =>
                JavaValue.Bool(MidpRuntime.Of(thread.Vm).Device.IsColor))
            .Method("numColors", "()I", static (thread, _) =>
            {
                DeviceProfile device = MidpRuntime.Of(thread.Vm).Device;
                return JavaValue.Int(device.IsColor ? 1 << (device.ColorDepth * 3) : 1 << device.ColorDepth);
            })
            .Method("numAlphaLevels", "()I", static (_, _) => JavaValue.Int(256))
            .Method("getColor", "(I)I", static (_, args) => JavaValue.Int(args.Int(0) switch
            {
                0 => 0x000000, // background
                1 => 0xFFFFFF, // foreground
                2 => 0x2C5D8F, // highlighted background
                3 => 0xFFFFFF, // highlighted foreground
                4 => 0x606060, // border
                _ => 0x9DC3E6, // highlighted border
            }))
            .Method("getBestImageWidth", "(I)I", static (thread, _) =>
                JavaValue.Int(Math.Min(32, MidpRuntime.Of(thread.Vm).CanvasWidth)))
            .Method("getBestImageHeight", "(I)I", static (thread, _) =>
                JavaValue.Int(Math.Min(32, MidpRuntime.Of(thread.Vm).CanvasHeight)))
            .Method("callSerially", "(Ljava/lang/Runnable;)V", static (thread, args) =>
            {
                if (args.Object(0) is { } runnable)
                {
                    MidpRuntime.Of(thread.Vm).Events.Post(
                        new MidpEvent(MidpEventKind.Callback, Argument: runnable));
                }
                return JavaValue.Void;
            })
            .Method("flashBacklight", "(I)Z", static (thread, args) =>
            {
                MidpRuntime.Of(thread.Vm).Feedback?.FlashBacklight(args.Int(0));
                return JavaValue.Bool(true);
            })
            .Method("vibrate", "(I)Z", static (thread, args) =>
            {
                MidpRuntime.Of(thread.Vm).Feedback?.Vibrate(args.Int(0));
                return JavaValue.Bool(true);
            }));
    }

    /// <summary>
    /// Switches the visible displayable, delivering the hide and show callbacks in order.
    /// </summary>
    internal static void SetCurrent(JavaThread thread, JavaObject? next)
    {
        MidpRuntime runtime = MidpRuntime.Of(thread.Vm);
        JavaObject? previous = runtime.CurrentDisplayable;
        if (ReferenceEquals(previous, next)) return;

        if (previous is not null)
            runtime.Events.Post(new MidpEvent(MidpEventKind.HideNotify, Target: previous));

        runtime.CurrentDisplayable = next;
        runtime.ResetInput();

        if (next is not null)
        {
            runtime.Events.Post(new MidpEvent(MidpEventKind.ShowNotify, Target: next));
            runtime.Events.PostRepaint(0, 0, runtime.CanvasWidth, runtime.CanvasHeight);
        }

        runtime.NotifyDisplayableChanged(next);
    }

    // ---------------------------------------------------------------------------------
    // Canvas
    // ---------------------------------------------------------------------------------

    /// <summary>Canvas field slot for the full-screen flag.</summary>
    internal const int FullScreenSlot = DisplayableFieldCount;

    private static void RegisterCanvas(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/Canvas", builder => builder
            .Extends("javax/microedition/lcdui/Displayable")
            .AsAbstract()
            .Field("fullScreen", "Z")

            .Constant("KEY_NUM0", KeyCodes.Num0)
            .Constant("KEY_NUM1", KeyCodes.Num1)
            .Constant("KEY_NUM2", KeyCodes.Num2)
            .Constant("KEY_NUM3", KeyCodes.Num3)
            .Constant("KEY_NUM4", KeyCodes.Num4)
            .Constant("KEY_NUM5", KeyCodes.Num5)
            .Constant("KEY_NUM6", KeyCodes.Num6)
            .Constant("KEY_NUM7", KeyCodes.Num7)
            .Constant("KEY_NUM8", KeyCodes.Num8)
            .Constant("KEY_NUM9", KeyCodes.Num9)
            .Constant("KEY_STAR", KeyCodes.Star)
            .Constant("KEY_POUND", KeyCodes.Pound)
            .Constant("UP", GameAction.Up)
            .Constant("DOWN", GameAction.Down)
            .Constant("LEFT", GameAction.Left)
            .Constant("RIGHT", GameAction.Right)
            .Constant("FIRE", GameAction.Fire)
            .Constant("GAME_A", GameAction.GameA)
            .Constant("GAME_B", GameAction.GameB)
            .Constant("GAME_C", GameAction.GameC)
            .Constant("GAME_D", GameAction.GameD)

            .Constructor("()V", static (_, _) => JavaValue.Void)
            .AbstractMethod("paint", "(Ljavax/microedition/lcdui/Graphics;)V")

            // The input callbacks default to doing nothing; a game overrides the ones it wants.
            .NoOpMethod("keyPressed", "(I)V")
            .NoOpMethod("keyReleased", "(I)V")
            .NoOpMethod("keyRepeated", "(I)V")
            .NoOpMethod("pointerPressed", "(II)V")
            .NoOpMethod("pointerReleased", "(II)V")
            .NoOpMethod("pointerDragged", "(II)V")
            .NoOpMethod("showNotify", "()V")
            .NoOpMethod("hideNotify", "()V")

            .Method("isDoubleBuffered", "()Z", static (_, _) => JavaValue.Bool(true))
            .Method("hasPointerEvents", "()Z", static (thread, _) =>
                JavaValue.Bool(MidpRuntime.Of(thread.Vm).Device.HasPointerEvents))
            .Method("hasPointerMotionEvents", "()Z", static (thread, _) =>
                JavaValue.Bool(MidpRuntime.Of(thread.Vm).Device.HasPointerEvents))
            .Method("hasRepeatEvents", "()Z", static (thread, _) =>
                JavaValue.Bool(MidpRuntime.Of(thread.Vm).Device.HasRepeatEvents))
            .Method("getGameAction", "(I)I", static (thread, args) =>
                JavaValue.Int(MidpRuntime.Of(thread.Vm).Device.GetGameAction(args.Int(0))))
            .Method("getKeyCode", "(I)I", static (thread, args) =>
                JavaValue.Int(MidpRuntime.Of(thread.Vm).Device.GetKeyCode(args.Int(0))))
            .Method("getKeyName", "(I)Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, MidpRuntime.Of(thread.Vm).Device.GetKeyName(args.Int(0))))
            .Method("setFullScreenMode", "(Z)V", static (thread, args) =>
            {
                args.This.SetField(FullScreenSlot, JavaValue.Bool(args.Bool(0)));
                MidpRuntime runtime = MidpRuntime.Of(thread.Vm);
                if (ReferenceEquals(runtime.CurrentDisplayable, args.This))
                    runtime.Events.PostRepaint(0, 0, runtime.CanvasWidth, runtime.CanvasHeight);
                return JavaValue.Void;
            })
            .Method("repaint", "()V", static (thread, args) =>
            {
                MidpRuntime runtime = MidpRuntime.Of(thread.Vm);
                if (ReferenceEquals(runtime.CurrentDisplayable, args.This))
                    runtime.Events.PostRepaint(0, 0, runtime.CanvasWidth, runtime.CanvasHeight);
                return JavaValue.Void;
            })
            .Method("repaint", "(IIII)V", static (thread, args) =>
            {
                MidpRuntime runtime = MidpRuntime.Of(thread.Vm);
                if (ReferenceEquals(runtime.CurrentDisplayable, args.This))
                    runtime.Events.PostRepaint(args.Int(0), args.Int(1), args.Int(2), args.Int(3));
                return JavaValue.Void;
            })
            .Method("serviceRepaints", "()V", static (thread, args) =>
            {
                // A synchronous paint. Games call this when they have just changed state and
                // must see it on screen before continuing, typically around a level transition.
                MidpRuntime runtime = MidpRuntime.Of(thread.Vm);
                if (ReferenceEquals(runtime.CurrentDisplayable, args.This))
                    MidpDispatcher.PaintNow(thread, runtime, args.This);
                return JavaValue.Void;
            }));
    }

    // ---------------------------------------------------------------------------------
    // Ticker
    // ---------------------------------------------------------------------------------

    private static void RegisterTicker(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/lcdui/Ticker", builder => builder
            .Field("text", "Ljava/lang/String;")
            .Constructor("(Ljava/lang/String;)V", static (_, args) =>
            {
                args.This.SetField(0, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .Method("getString", "()Ljava/lang/String;",
                static (_, args) => JavaValue.Ref(args.This.GetField(0).Reference))
            .Method("setString", "(Ljava/lang/String;)V", static (_, args) =>
            {
                args.This.SetField(0, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            }));
    }
}
