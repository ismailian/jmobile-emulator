using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Runtime.Graphics;

namespace JMobile.Runtime.Midp;

/// <summary>
/// Delivers queued callbacks into guest code.
/// </summary>
/// <remarks>
/// This is the bridge between the emulator's event loop and the MIDlet. It runs on one thread
/// only, which is what gives games the serial callback guarantee MIDP promises: a game may
/// mutate its world in <c>keyPressed</c> and read it in <c>paint</c> with no locking, because
/// those two calls can never overlap.
/// </remarks>
public static class MidpDispatcher
{
    /// <summary>Runs one queued callback to completion.</summary>
    public static void Deliver(JavaThread thread, MidpRuntime runtime, MidpEvent midpEvent)
    {
        JavaObject? target = midpEvent.Target ?? runtime.CurrentDisplayable;
        if (target is null && midpEvent.Kind != MidpEventKind.Callback) return;

        switch (midpEvent.Kind)
        {
            case MidpEventKind.Paint:
                Paint(thread, runtime, target!, midpEvent.A, midpEvent.B, midpEvent.C, midpEvent.D);
                break;

            case MidpEventKind.KeyPressed:
                DeliverKey(thread, runtime, target!, "keyPressed", midpEvent.A);
                break;

            case MidpEventKind.KeyReleased:
                DeliverKey(thread, runtime, target!, "keyReleased", midpEvent.A);
                break;

            case MidpEventKind.KeyRepeated:
                DeliverKey(thread, runtime, target!, "keyRepeated", midpEvent.A);
                break;

            case MidpEventKind.PointerPressed:
                DeliverPointer(thread, target!, "pointerPressed", midpEvent.A, midpEvent.B);
                break;

            case MidpEventKind.PointerReleased:
                DeliverPointer(thread, target!, "pointerReleased", midpEvent.A, midpEvent.B);
                break;

            case MidpEventKind.PointerDragged:
                DeliverPointer(thread, target!, "pointerDragged", midpEvent.A, midpEvent.B);
                break;

            case MidpEventKind.ShowNotify:
                CallIfPresent(thread, target!, "showNotify", "()V");
                break;

            case MidpEventKind.HideNotify:
                CallIfPresent(thread, target!, "hideNotify", "()V");
                break;

            case MidpEventKind.SizeChanged:
                CallIfPresent(thread, target!, "sizeChanged", "(II)V",
                    JavaValue.Int(midpEvent.A), JavaValue.Int(midpEvent.B));
                break;

            case MidpEventKind.CommandAction:
                DeliverCommand(thread, target!, midpEvent.Argument);
                break;

            case MidpEventKind.Callback:
                if (midpEvent.Argument is { } runnable)
                    thread.Vm.CallVirtual(thread, runnable, "run", "()V");
                break;
        }
    }

    /// <summary>Paints the whole of a displayable immediately, for <c>serviceRepaints</c>.</summary>
    public static void PaintNow(JavaThread thread, MidpRuntime runtime, JavaObject displayable) =>
        Paint(thread, runtime, displayable, 0, 0, runtime.CanvasWidth, runtime.CanvasHeight);

    // ---------------------------------------------------------------------------------
    // Painting
    // ---------------------------------------------------------------------------------

    private static void Paint(
        JavaThread thread,
        MidpRuntime runtime,
        JavaObject displayable,
        int x,
        int y,
        int width,
        int height)
    {
        if (!ReferenceEquals(runtime.CurrentDisplayable, displayable)) return;

        GraphicsContext context = runtime.ScreenGraphics;
        context.ResetTo(runtime.Screen);

        // A partial repaint must not be allowed to touch anything outside the requested region,
        // which is what makes incremental redraw actually save work.
        if (width > 0 && height > 0) context.SetClip(x, y, width, height);

        JavaObject graphics = runtime.ScreenGraphicsObject ??= GraphicsModule.NewGraphics(thread, context, isScreen: true);
        ((JavaGraphics)graphics).Context = context;

        if (IsCanvas(thread.Vm, displayable))
        {
            thread.Vm.CallVirtual(thread, displayable, "paint",
                "(Ljavax/microedition/lcdui/Graphics;)V", JavaValue.Ref(graphics));
        }
        else
        {
            // Form, List, TextBox and Alert have no paint method of their own; the runtime
            // draws them, exactly as a real handset's implementation would.
            ScreenRenderer.Render(thread, runtime, displayable, context);
        }

        runtime.NotifyFramePainted();
    }

    private static bool IsCanvas(VirtualMachine vm, JavaObject displayable)
    {
        JavaClass? canvas = vm.FindLoadedClass("javax/microedition/lcdui/Canvas");
        return canvas is not null && canvas.IsAssignableFrom(displayable.Class);
    }

    // ---------------------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------------------

    private static void DeliverKey(
        JavaThread thread,
        MidpRuntime runtime,
        JavaObject target,
        string method,
        int keyCode)
    {
        if (IsCanvas(thread.Vm, target))
        {
            thread.Vm.CallVirtual(thread, target, method, "(I)V", JavaValue.Int(keyCode));
            return;
        }

        // On a high-level screen the key drives the built-in widget instead.
        if (method == "keyPressed") ScreenRenderer.HandleKey(thread, runtime, target, keyCode);
    }

    private static void DeliverPointer(JavaThread thread, JavaObject target, string method, int x, int y)
    {
        CallIfPresent(thread, target, method, "(II)V", JavaValue.Int(x), JavaValue.Int(y));
    }

    /// <summary>Invokes a listener's <c>commandAction</c>, if one is attached.</summary>
    public static void DeliverCommand(JavaThread thread, JavaObject displayable, JavaObject? command)
    {
        if (command is null) return;
        if (displayable.GetField(LcduiModule.ListenerSlot).Reference is not JavaObject listener) return;

        thread.Vm.CallVirtual(thread, listener, "commandAction",
            "(Ljavax/microedition/lcdui/Command;Ljavax/microedition/lcdui/Displayable;)V",
            JavaValue.Ref(command),
            JavaValue.Ref(displayable));
    }

    /// <summary>Calls a method only if the receiver actually has one, ignoring it otherwise.</summary>
    private static void CallIfPresent(
        JavaThread thread,
        JavaObject target,
        string name,
        string descriptor,
        params JavaValue[] arguments)
    {
        if (target.Class.FindMethod(name, descriptor) is null) return;
        thread.Vm.CallVirtual(thread, target, name, descriptor, arguments);
    }
}
