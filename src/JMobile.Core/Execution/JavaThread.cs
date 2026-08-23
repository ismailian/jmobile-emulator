using System.Runtime.CompilerServices;
using JMobile.Core.Model;

namespace JMobile.Core.Execution;

/// <summary>One activation record. A struct, so a call pushes no heap object.</summary>
public struct Frame
{
    public JavaMethod Method;

    /// <summary>The decoded body, cached here so dispatch never dereferences the method.</summary>
    public Instruction[] Code;

    /// <summary>Absolute slot index of local variable 0.</summary>
    public int LocalsBase;

    /// <summary>Absolute slot index of operand stack position 0.</summary>
    public int StackBase;

    /// <summary>Absolute slot index of the next free operand stack position.</summary>
    public int Sp;

    /// <summary>Index of the next instruction to execute.</summary>
    public int Pc;

    /// <summary>Monitor held by a synchronized method, released on any exit path.</summary>
    public JavaObject? LockedMonitor;
}

/// <summary>
/// A Java thread of execution and the stack it runs on.
/// </summary>
/// <remarks>
/// The stack is two parallel arrays rather than one array of tagged values. Bytecode is
/// verified, so the static types already say which half of a slot is live; splitting them
/// means integer-heavy code, which is nearly all game code, touches only the
/// <see cref="Primitives"/> array and so never triggers a GC write barrier on a push. It also
/// lets the collector scan a dense reference array instead of striding over interleaved
/// payloads.
/// <para>
/// Calls overlap: a callee's locals begin at the slot where its arguments already sit on the
/// caller's operand stack, so passing arguments copies nothing at all.
/// </para>
/// </remarks>
public sealed class JavaThread
{
    /// <summary>Initial stack size in slots. Grows on demand.</summary>
    private const int InitialSlotCapacity = 8 * 1024;

    /// <summary>Ceiling on stack size; crossing it raises a Java <c>StackOverflowError</c>.</summary>
    private const int MaxSlotCapacity = 2 * 1024 * 1024;

    /// <summary>Ceiling on call depth, the other way a runaway recursion is caught.</summary>
    private const int MaxFrameDepth = 4096;

    internal long[] Primitives = new long[InitialSlotCapacity];
    internal object?[] References = new object?[InitialSlotCapacity];
    internal Frame[] Frames = new Frame[256];
    internal int FrameCount;

    public JavaThread(VirtualMachine vm, string name)
    {
        Vm = vm;
        Name = name;
    }

    public VirtualMachine Vm { get; }

    public string Name { get; internal set; }

    /// <summary>The <c>java.lang.Thread</c> instance this thread runs on behalf of.</summary>
    public JavaObject? ThreadObject { get; set; }

    /// <summary>The host thread executing this Java thread, once it has been started.</summary>
    public Thread? HostThread { get; set; }

    /// <summary>Set by <c>Thread.interrupt</c> and cleared when the interrupt is observed.</summary>
    public volatile bool InterruptRequested;

    /// <summary>Set when the VM is tearing down so that long-running guest loops can be cut short.</summary>
    public volatile bool AbortRequested;

    /// <summary>Number of frames currently on this thread's stack.</summary>
    public int Depth => FrameCount;

    /// <summary>Slot index just past the deepest live frame.</summary>
    internal int SlotTop => FrameCount == 0 ? 0 : Frames[FrameCount - 1].Sp;

    // ---------------------------------------------------------------------------------
    // Frame management
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Pushes an activation for <paramref name="method"/> whose arguments are already sitting
    /// at the top of the caller's operand stack.
    /// </summary>
    /// <returns>The index of the pushed frame.</returns>
    internal int PushFrame(JavaMethod method, Instruction[] code, int callerSp)
    {
        int depth = FrameCount;
        if (depth >= MaxFrameDepth) throw Vm.NewStackOverflowError(this);

        if (depth >= Frames.Length) Array.Resize(ref Frames, Frames.Length * 2);

        int localsBase = callerSp - method.ArgumentSlots;
        int stackBase = localsBase + method.MaxLocals;
        int required = stackBase + method.MaxStack;
        if (required > Primitives.Length) GrowStack(required);

        // Locals past the incoming arguments may still hold references from a previous call
        // at the same depth. Clearing them keeps those objects collectable.
        int uninitialised = method.MaxLocals - method.ArgumentSlots;
        if (uninitialised > 0)
            Array.Clear(References, localsBase + method.ArgumentSlots, uninitialised);

        ref Frame frame = ref Frames[depth];
        frame.Method = method;
        frame.Code = code;
        frame.LocalsBase = localsBase;
        frame.StackBase = stackBase;
        frame.Sp = stackBase;
        frame.Pc = 0;
        frame.LockedMonitor = null;

        FrameCount = depth + 1;
        return depth;
    }

    /// <summary>
    /// Pushes the frame used to enter Java from host code, where no caller stack exists yet.
    /// </summary>
    internal int PushEntryFrame(JavaMethod method, Instruction[] code, ReadOnlySpan<JavaValue> arguments)
    {
        int depth = FrameCount;
        if (depth >= MaxFrameDepth) throw Vm.NewStackOverflowError(this);
        if (depth >= Frames.Length) Array.Resize(ref Frames, Frames.Length * 2);

        int localsBase = depth == 0 ? 0 : Frames[depth - 1].Sp;
        int stackBase = localsBase + method.MaxLocals;
        int required = stackBase + method.MaxStack + 8;
        if (required > Primitives.Length) GrowStack(required);

        Array.Clear(References, localsBase, method.MaxLocals);

        // Arguments arrive as values rather than as stack contents, so here they are copied.
        // This only happens on a host-to-Java transition, never on a Java-to-Java call.
        //
        // Each argument goes to the slot its parameter actually occupies rather than to the
        // next slot along: a long or a double takes two, so writing them consecutively would
        // leave every parameter after the first wide one reading the wrong local.
        bool hasReceiver = !method.IsStatic;
        int[] offsets = method.ParameterSlotOffsets;
        int firstParameterSlot = localsBase + (hasReceiver ? 1 : 0);

        for (int i = 0; i < arguments.Length; i++)
        {
            int slot;
            if (hasReceiver && i == 0)
            {
                slot = localsBase;
            }
            else
            {
                int parameterIndex = hasReceiver ? i - 1 : i;
                slot = parameterIndex < offsets.Length
                    ? firstParameterSlot + offsets[parameterIndex]
                    : firstParameterSlot + parameterIndex;
            }

            Primitives[slot] = arguments[i].Primitive;
            References[slot] = arguments[i].Reference;
        }

        ref Frame frame = ref Frames[depth];
        frame.Method = method;
        frame.Code = code;
        frame.LocalsBase = localsBase;
        frame.StackBase = stackBase;
        frame.Sp = stackBase;
        frame.Pc = 0;
        frame.LockedMonitor = null;

        FrameCount = depth + 1;
        return depth;
    }

    /// <summary>Discards the top frame and scrubs its references so they stop being roots.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PopFrame()
    {
        int depth = FrameCount - 1;
        ref Frame frame = ref Frames[depth];

        int span = frame.StackBase + frame.Method.MaxStack - frame.LocalsBase;
        if (span > 0) Array.Clear(References, frame.LocalsBase, span);

        frame.Method = null!;
        frame.Code = null!;
        frame.LockedMonitor = null;
        FrameCount = depth;
    }

    private void GrowStack(int required)
    {
        if (required > MaxSlotCapacity) throw Vm.NewStackOverflowError(this);

        int capacity = Primitives.Length;
        while (capacity < required) capacity *= 2;
        if (capacity > MaxSlotCapacity) capacity = MaxSlotCapacity;

        Array.Resize(ref Primitives, capacity);
        Array.Resize(ref References, capacity);
    }

    /// <summary>
    /// Reserves stack room for a host-initiated call, so that pushing an entry frame after a
    /// native method has already consumed most of the stack cannot overflow silently.
    /// </summary>
    internal void Reserve(int slots)
    {
        int required = SlotTop + slots;
        if (required > Primitives.Length) GrowStack(required);
    }

    // ---------------------------------------------------------------------------------
    // Diagnostics
    // ---------------------------------------------------------------------------------

    /// <summary>Renders the Java call stack, newest frame first, for exception reporting.</summary>
    public string[] CaptureStackTrace()
    {
        int count = FrameCount;
        if (count == 0) return [];

        var trace = new string[count];
        for (int i = 0; i < count; i++)
        {
            ref Frame frame = ref Frames[count - 1 - i];
            JavaMethod method = frame.Method;
            if (method is null)
            {
                trace[i] = "<unknown>";
                continue;
            }

            int bytecodeOffset = -1;
            int[] map = method.InstructionToBytecode;
            if ((uint)frame.Pc < (uint)map.Length) bytecodeOffset = map[frame.Pc];

            int line = bytecodeOffset >= 0 ? method.LineNumberFor(bytecodeOffset) : -1;
            string location = method.DeclaringClass.SourceFile is { } source
                ? line >= 0 ? $"({source}:{line})" : $"({source})"
                : method.IsNative ? "(Native Method)" : "(Unknown Source)";

            trace[i] = $"{method.DeclaringClass.DisplayName}.{method.Name}{location}";
        }

        return trace;
    }

    public override string ToString() => $"JavaThread({Name}, depth={FrameCount})";
}
