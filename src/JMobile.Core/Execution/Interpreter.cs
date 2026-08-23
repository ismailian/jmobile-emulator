using System.Runtime.CompilerServices;
using JMobile.Core.ClassFile;
using JMobile.Core.Model;
using JMobile.Core.Native;

namespace JMobile.Core.Execution;

/// <summary>
/// The bytecode execution engine.
/// </summary>
/// <remarks>
/// The design decisions that matter for speed all live here:
/// <list type="bullet">
/// <item><b>One loop, no host recursion.</b> A Java call pushes a frame and continues the same
/// loop rather than recursing into C#. Guest recursion depth is therefore bounded by the Java
/// stack the thread owns, not by the host stack, and a call costs no host prologue.</item>
/// <item><b>Hot state in locals.</b> The program counter, stack pointer, locals base and
/// instruction array live in C# locals for the duration of a frame, so the register allocator
/// can keep them in registers. They are published back into the frame only at the boundaries
/// where something else might look: calls, throws and returns.</item>
/// <item><b>Arguments are never copied.</b> A callee's locals begin exactly where its arguments
/// already sit on the caller's operand stack.</item>
/// <item><b>Resolution happens once per site.</b> Constant pool entries resolve on first
/// execution into the instruction's cache field, and virtual and interface targets are
/// remembered per call site.</item>
/// </list>
/// </remarks>
public static class Interpreter
{
    private static readonly Instruction[] NativeBody = [];

    // ---------------------------------------------------------------------------------
    // Entry points
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Calls a Java method from host code. Arguments are supplied as values and copied onto the
    /// thread's stack; this is the only place a call copies anything.
    /// </summary>
    public static JavaValue CallMethod(JavaThread thread, JavaMethod method, ReadOnlySpan<JavaValue> arguments)
    {
        ArgumentNullException.ThrowIfNull(method);

        VirtualMachine vm = thread.Vm;
        thread.Reserve(method.FrameSize + 16);

        int entryFrameCount = thread.FrameCount;

        if (method.IsNative)
        {
            int index = thread.PushEntryFrame(method, NativeBody, arguments);
            JavaObject? monitor = method.IsSynchronized ? MonitorTarget(thread, method, index) : null;
            if (monitor is not null)
            {
                Monitor.Enter(monitor.Monitor);
                thread.Frames[index].LockedMonitor = monitor;
            }

            try
            {
                var args = new JavaArgs(
                    thread.Primitives,
                    thread.References,
                    thread.Frames[index].LocalsBase,
                    method.ParameterSlotOffsets,
                    !method.IsStatic);

                return method.Native!(thread, args);
            }
            finally
            {
                if (monitor is not null) Monitor.Exit(monitor.Monitor);
                if (thread.FrameCount > entryFrameCount) thread.PopFrame();
            }
        }

        if (method.IsAbstract)
            throw vm.NewAbstractMethodError(thread, method.ToString());

        Instruction[] code = method.EnsureCompiled(vm);
        int frameIndex = thread.PushEntryFrame(method, code, arguments);

        if (method.IsSynchronized)
        {
            JavaObject? monitor = MonitorTarget(thread, method, frameIndex);
            if (monitor is not null)
            {
                Monitor.Enter(monitor.Monitor);
                thread.Frames[frameIndex].LockedMonitor = monitor;
            }
        }

        return Execute(thread, entryFrameCount);
    }

    private static JavaObject? MonitorTarget(JavaThread thread, JavaMethod method, int frameIndex)
    {
        if (!method.IsStatic)
            return thread.References[thread.Frames[frameIndex].LocalsBase] as JavaObject;

        return thread.Vm.GetClassMirror(method.DeclaringClass);
    }

    // ---------------------------------------------------------------------------------
    // The dispatch loop
    // ---------------------------------------------------------------------------------

    private static JavaValue Execute(JavaThread thread, int entryFrameCount)
    {
        VirtualMachine vm = thread.Vm;

        long[] p = thread.Primitives;
        object?[] r = thread.References;

        int frameIndex = thread.FrameCount - 1;
        Instruction[] code = thread.Frames[frameIndex].Code;
        JavaMethod method = thread.Frames[frameIndex].Method;
        int pc = thread.Frames[frameIndex].Pc;
        int sp = thread.Frames[frameIndex].Sp;
        int locals = thread.Frames[frameIndex].LocalsBase;

        while (true)
        {
            try
            {
                while (true)
                {
                    ref Instruction ins = ref code[pc++];

                    switch (ins.Code)
                    {
                        case Op.Nop:
                            break;

                        // -- constants --------------------------------------------------------
                        case Op.AConstNull:
                            r[sp] = null;
                            p[sp] = 0;
                            sp++;
                            break;

                        case Op.Const32:
                            p[sp++] = ins.A;
                            break;

                        case Op.Const64:
                            p[sp] = ins.Wide;
                            sp += 2;
                            break;

                        case Op.ConstRef:
                        {
                            // Class literals arrive here carrying their name and are replaced by
                            // the mirror the first time they are reached.
                            if (ins.Cache is not JavaObject constant)
                                constant = ResolveConstantReference(thread, ref ins);

                            r[sp] = constant;
                            p[sp] = 0;
                            sp++;
                            break;
                        }

                        // -- local variables --------------------------------------------------
                        case Op.Load32:
                            p[sp++] = p[locals + ins.A];
                            break;

                        case Op.Load64:
                            p[sp] = p[locals + ins.A];
                            sp += 2;
                            break;

                        case Op.LoadRef:
                        {
                            int source = locals + ins.A;
                            r[sp] = r[source];
                            // The primitive half travels too, because a return address produced
                            // by jsr is stored and reloaded through these same instructions.
                            p[sp] = p[source];
                            sp++;
                            break;
                        }

                        case Op.Store32:
                            p[locals + ins.A] = p[--sp];
                            break;

                        case Op.Store64:
                            sp -= 2;
                            p[locals + ins.A] = p[sp];
                            break;

                        case Op.StoreRef:
                        {
                            --sp;
                            int destination = locals + ins.A;
                            r[destination] = r[sp];
                            p[destination] = p[sp];
                            r[sp] = null;
                            break;
                        }

                        case Op.IInc:
                        {
                            int slot = locals + ins.A;
                            p[slot] = (int)p[slot] + ins.B;
                            break;
                        }

                        // -- array loads ------------------------------------------------------
                        case Op.ArrayLoadInt:
                        {
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<int>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            p[sp++] = array.Items[index];
                            break;
                        }

                        case Op.ArrayLoadFloat:
                        {
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<float>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            p[sp++] = BitConverter.SingleToInt32Bits(array.Items[index]);
                            break;
                        }

                        case Op.ArrayLoadLong:
                        {
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<long>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            p[sp] = array.Items[index];
                            sp += 2;
                            break;
                        }

                        case Op.ArrayLoadDouble:
                        {
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<double>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            p[sp] = BitConverter.DoubleToInt64Bits(array.Items[index]);
                            sp += 2;
                            break;
                        }

                        case Op.ArrayLoadRef:
                        {
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<JavaObject?>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            r[sp] = array.Items[index];
                            p[sp] = 0;
                            sp++;
                            break;
                        }

                        case Op.ArrayLoadByte:
                        {
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<sbyte>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            p[sp++] = array.Items[index];
                            break;
                        }

                        case Op.ArrayLoadChar:
                        {
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<char>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            p[sp++] = array.Items[index];
                            break;
                        }

                        case Op.ArrayLoadShort:
                        {
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<short>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            p[sp++] = array.Items[index];
                            break;
                        }

                        // -- array stores -----------------------------------------------------
                        case Op.ArrayStoreInt:
                        {
                            int value = (int)p[--sp];
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<int>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            array.Items[index] = value;
                            break;
                        }

                        case Op.ArrayStoreFloat:
                        {
                            float value = BitConverter.Int32BitsToSingle((int)p[--sp]);
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<float>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            array.Items[index] = value;
                            break;
                        }

                        case Op.ArrayStoreLong:
                        {
                            sp -= 2;
                            long value = p[sp];
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<long>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            array.Items[index] = value;
                            break;
                        }

                        case Op.ArrayStoreDouble:
                        {
                            sp -= 2;
                            double value = BitConverter.Int64BitsToDouble(p[sp]);
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<double>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            array.Items[index] = value;
                            break;
                        }

                        case Op.ArrayStoreRef:
                        {
                            var value = r[--sp] as JavaObject;
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<JavaObject?>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);

                            // Java arrays are covariant, so the store has to be checked even
                            // though the bytecode type-checked at compile time.
                            if (value is not null)
                            {
                                JavaClass? component = array.Class.ComponentType;
                                if (component is not null && !component.IsAssignableFrom(value.Class))
                                    throw vm.NewArrayStoreException(thread, value.Class);
                            }

                            array.Items[index] = value;
                            break;
                        }

                        case Op.ArrayStoreByte:
                        {
                            var value = (sbyte)p[--sp];
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<sbyte>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            array.Items[index] = value;
                            break;
                        }

                        case Op.ArrayStoreChar:
                        {
                            var value = (char)p[--sp];
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<char>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            array.Items[index] = value;
                            break;
                        }

                        case Op.ArrayStoreShort:
                        {
                            var value = (short)p[--sp];
                            int index = (int)p[--sp];
                            var array = r[--sp] as JavaArray<short>;
                            if (array is null) throw NullArray(thread, r[sp]);
                            if ((uint)index >= (uint)array.Items.Length)
                                throw vm.NewArrayIndexOutOfBoundsException(thread, index, array.Items.Length);
                            array.Items[index] = value;
                            break;
                        }

                        case Op.ArrayLength:
                        {
                            var array = r[sp - 1] as JavaArray;
                            if (array is null) throw NullArray(thread, r[sp - 1]);
                            p[sp - 1] = array.Length;
                            r[sp - 1] = null;
                            break;
                        }

                        // -- operand stack ----------------------------------------------------
                        case Op.Pop:
                            sp--;
                            break;

                        case Op.Pop2:
                            sp -= 2;
                            break;

                        case Op.Dup:
                            p[sp] = p[sp - 1];
                            r[sp] = r[sp - 1];
                            sp++;
                            break;

                        case Op.DupX1:
                        {
                            long topPrimitive = p[sp - 1];
                            object? topReference = r[sp - 1];
                            p[sp] = topPrimitive;
                            r[sp] = topReference;
                            p[sp - 1] = p[sp - 2];
                            r[sp - 1] = r[sp - 2];
                            p[sp - 2] = topPrimitive;
                            r[sp - 2] = topReference;
                            sp++;
                            break;
                        }

                        case Op.DupX2:
                        {
                            long topPrimitive = p[sp - 1];
                            object? topReference = r[sp - 1];
                            p[sp] = topPrimitive;
                            r[sp] = topReference;
                            p[sp - 1] = p[sp - 2];
                            r[sp - 1] = r[sp - 2];
                            p[sp - 2] = p[sp - 3];
                            r[sp - 2] = r[sp - 3];
                            p[sp - 3] = topPrimitive;
                            r[sp - 3] = topReference;
                            sp++;
                            break;
                        }

                        case Op.Dup2:
                            p[sp] = p[sp - 2];
                            r[sp] = r[sp - 2];
                            p[sp + 1] = p[sp - 1];
                            r[sp + 1] = r[sp - 1];
                            sp += 2;
                            break;

                        case Op.Dup2X1:
                        {
                            long a1 = p[sp - 1], a2 = p[sp - 2], a3 = p[sp - 3];
                            object? o1 = r[sp - 1], o2 = r[sp - 2], o3 = r[sp - 3];
                            p[sp + 1] = a1;
                            r[sp + 1] = o1;
                            p[sp] = a2;
                            r[sp] = o2;
                            p[sp - 1] = a3;
                            r[sp - 1] = o3;
                            p[sp - 2] = a1;
                            r[sp - 2] = o1;
                            p[sp - 3] = a2;
                            r[sp - 3] = o2;
                            sp += 2;
                            break;
                        }

                        case Op.Dup2X2:
                        {
                            long a1 = p[sp - 1], a2 = p[sp - 2], a3 = p[sp - 3], a4 = p[sp - 4];
                            object? o1 = r[sp - 1], o2 = r[sp - 2], o3 = r[sp - 3], o4 = r[sp - 4];
                            p[sp + 1] = a1;
                            r[sp + 1] = o1;
                            p[sp] = a2;
                            r[sp] = o2;
                            p[sp - 1] = a3;
                            r[sp - 1] = o3;
                            p[sp - 2] = a4;
                            r[sp - 2] = o4;
                            p[sp - 3] = a1;
                            r[sp - 3] = o1;
                            p[sp - 4] = a2;
                            r[sp - 4] = o2;
                            sp += 2;
                            break;
                        }

                        case Op.Swap:
                        {
                            (p[sp - 1], p[sp - 2]) = (p[sp - 2], p[sp - 1]);
                            (r[sp - 1], r[sp - 2]) = (r[sp - 2], r[sp - 1]);
                            break;
                        }

                        // -- integer arithmetic -----------------------------------------------
                        case Op.IAdd:
                            sp--;
                            p[sp - 1] = (int)p[sp - 1] + (int)p[sp];
                            break;

                        case Op.ISub:
                            sp--;
                            p[sp - 1] = (int)p[sp - 1] - (int)p[sp];
                            break;

                        case Op.IMul:
                            sp--;
                            p[sp - 1] = (int)p[sp - 1] * (int)p[sp];
                            break;

                        case Op.IDiv:
                        {
                            int divisor = (int)p[sp - 1];
                            int dividend = (int)p[sp - 2];
                            if (divisor == 0) throw vm.NewArithmeticException(thread, "/ by zero");
                            sp--;
                            // Java defines Integer.MIN_VALUE / -1 as itself, where the CLR would
                            // raise OverflowException, so that one case is handled by negation.
                            p[sp - 1] = divisor == -1 ? unchecked(-dividend) : dividend / divisor;
                            break;
                        }

                        case Op.IRem:
                        {
                            int divisor = (int)p[sp - 1];
                            int dividend = (int)p[sp - 2];
                            if (divisor == 0) throw vm.NewArithmeticException(thread, "/ by zero");
                            sp--;
                            p[sp - 1] = divisor == -1 ? 0 : dividend % divisor;
                            break;
                        }

                        case Op.INeg:
                            p[sp - 1] = unchecked(-(int)p[sp - 1]);
                            break;

                        case Op.IShl:
                            sp--;
                            p[sp - 1] = (int)p[sp - 1] << ((int)p[sp] & 31);
                            break;

                        case Op.IShr:
                            sp--;
                            p[sp - 1] = (int)p[sp - 1] >> ((int)p[sp] & 31);
                            break;

                        case Op.IUShr:
                            sp--;
                            p[sp - 1] = (int)((uint)(int)p[sp - 1] >> ((int)p[sp] & 31));
                            break;

                        case Op.IAnd:
                            sp--;
                            p[sp - 1] = (int)p[sp - 1] & (int)p[sp];
                            break;

                        case Op.IOr:
                            sp--;
                            p[sp - 1] = (int)p[sp - 1] | (int)p[sp];
                            break;

                        case Op.IXor:
                            sp--;
                            p[sp - 1] = (int)p[sp - 1] ^ (int)p[sp];
                            break;

                        // -- long arithmetic --------------------------------------------------
                        case Op.LAdd:
                            sp -= 2;
                            p[sp - 2] = unchecked(p[sp - 2] + p[sp]);
                            break;

                        case Op.LSub:
                            sp -= 2;
                            p[sp - 2] = unchecked(p[sp - 2] - p[sp]);
                            break;

                        case Op.LMul:
                            sp -= 2;
                            p[sp - 2] = unchecked(p[sp - 2] * p[sp]);
                            break;

                        case Op.LDiv:
                        {
                            long divisor = p[sp - 2];
                            long dividend = p[sp - 4];
                            if (divisor == 0) throw vm.NewArithmeticException(thread, "/ by zero");
                            sp -= 2;
                            p[sp - 2] = divisor == -1 ? unchecked(-dividend) : dividend / divisor;
                            break;
                        }

                        case Op.LRem:
                        {
                            long divisor = p[sp - 2];
                            long dividend = p[sp - 4];
                            if (divisor == 0) throw vm.NewArithmeticException(thread, "/ by zero");
                            sp -= 2;
                            p[sp - 2] = divisor == -1 ? 0 : dividend % divisor;
                            break;
                        }

                        case Op.LNeg:
                            p[sp - 2] = unchecked(-p[sp - 2]);
                            break;

                        case Op.LShl:
                            sp--;
                            p[sp - 2] = p[sp - 2] << ((int)p[sp] & 63);
                            break;

                        case Op.LShr:
                            sp--;
                            p[sp - 2] = p[sp - 2] >> ((int)p[sp] & 63);
                            break;

                        case Op.LUShr:
                            sp--;
                            p[sp - 2] = (long)((ulong)p[sp - 2] >> ((int)p[sp] & 63));
                            break;

                        case Op.LAnd:
                            sp -= 2;
                            p[sp - 2] = p[sp - 2] & p[sp];
                            break;

                        case Op.LOr:
                            sp -= 2;
                            p[sp - 2] = p[sp - 2] | p[sp];
                            break;

                        case Op.LXor:
                            sp -= 2;
                            p[sp - 2] = p[sp - 2] ^ p[sp];
                            break;

                        // -- float arithmetic -------------------------------------------------
                        case Op.FAdd:
                            sp--;
                            p[sp - 1] = BitConverter.SingleToInt32Bits(Float(p[sp - 1]) + Float(p[sp]));
                            break;

                        case Op.FSub:
                            sp--;
                            p[sp - 1] = BitConverter.SingleToInt32Bits(Float(p[sp - 1]) - Float(p[sp]));
                            break;

                        case Op.FMul:
                            sp--;
                            p[sp - 1] = BitConverter.SingleToInt32Bits(Float(p[sp - 1]) * Float(p[sp]));
                            break;

                        case Op.FDiv:
                            sp--;
                            p[sp - 1] = BitConverter.SingleToInt32Bits(Float(p[sp - 1]) / Float(p[sp]));
                            break;

                        case Op.FRem:
                            sp--;
                            p[sp - 1] = BitConverter.SingleToInt32Bits(Float(p[sp - 1]) % Float(p[sp]));
                            break;

                        case Op.FNeg:
                            p[sp - 1] = BitConverter.SingleToInt32Bits(-Float(p[sp - 1]));
                            break;

                        // -- double arithmetic ------------------------------------------------
                        case Op.DAdd:
                            sp -= 2;
                            p[sp - 2] = BitConverter.DoubleToInt64Bits(Double(p[sp - 2]) + Double(p[sp]));
                            break;

                        case Op.DSub:
                            sp -= 2;
                            p[sp - 2] = BitConverter.DoubleToInt64Bits(Double(p[sp - 2]) - Double(p[sp]));
                            break;

                        case Op.DMul:
                            sp -= 2;
                            p[sp - 2] = BitConverter.DoubleToInt64Bits(Double(p[sp - 2]) * Double(p[sp]));
                            break;

                        case Op.DDiv:
                            sp -= 2;
                            p[sp - 2] = BitConverter.DoubleToInt64Bits(Double(p[sp - 2]) / Double(p[sp]));
                            break;

                        case Op.DRem:
                            sp -= 2;
                            p[sp - 2] = BitConverter.DoubleToInt64Bits(Double(p[sp - 2]) % Double(p[sp]));
                            break;

                        case Op.DNeg:
                            p[sp - 2] = BitConverter.DoubleToInt64Bits(-Double(p[sp - 2]));
                            break;

                        // -- conversions ------------------------------------------------------
                        // .NET Core made float-to-integer conversions saturate and map NaN to
                        // zero, which is exactly what the JVM specifies, so plain casts suffice.
                        case Op.I2L:
                            sp++;
                            break;

                        case Op.I2F:
                            p[sp - 1] = BitConverter.SingleToInt32Bits((int)p[sp - 1]);
                            break;

                        case Op.I2D:
                            p[sp - 1] = BitConverter.DoubleToInt64Bits((int)p[sp - 1]);
                            sp++;
                            break;

                        case Op.L2I:
                            sp--;
                            p[sp - 1] = (int)p[sp - 1];
                            break;

                        case Op.L2F:
                            sp--;
                            p[sp - 1] = BitConverter.SingleToInt32Bits(p[sp - 1]);
                            break;

                        case Op.L2D:
                            p[sp - 2] = BitConverter.DoubleToInt64Bits(p[sp - 2]);
                            break;

                        case Op.F2I:
                            p[sp - 1] = (int)Float(p[sp - 1]);
                            break;

                        case Op.F2L:
                            p[sp - 1] = (long)Float(p[sp - 1]);
                            sp++;
                            break;

                        case Op.F2D:
                            p[sp - 1] = BitConverter.DoubleToInt64Bits(Float(p[sp - 1]));
                            sp++;
                            break;

                        case Op.D2I:
                            sp--;
                            p[sp - 1] = (int)Double(p[sp - 1]);
                            break;

                        case Op.D2L:
                            p[sp - 2] = (long)Double(p[sp - 2]);
                            break;

                        case Op.D2F:
                            sp--;
                            p[sp - 1] = BitConverter.SingleToInt32Bits((float)Double(p[sp - 1]));
                            break;

                        case Op.I2B:
                            p[sp - 1] = (sbyte)p[sp - 1];
                            break;

                        case Op.I2C:
                            p[sp - 1] = (char)p[sp - 1];
                            break;

                        case Op.I2S:
                            p[sp - 1] = (short)p[sp - 1];
                            break;

                        // -- comparisons ------------------------------------------------------
                        case Op.LCmp:
                        {
                            long right = p[sp - 2];
                            long left = p[sp - 4];
                            sp -= 3;
                            p[sp - 1] = left < right ? -1 : left > right ? 1 : 0;
                            break;
                        }

                        case Op.FCmpL:
                        case Op.FCmpG:
                        {
                            float right = Float(p[sp - 1]);
                            float left = Float(p[sp - 2]);
                            sp--;
                            // The L and G forms differ only in which way an unordered (NaN)
                            // comparison resolves.
                            p[sp - 1] = float.IsNaN(left) || float.IsNaN(right)
                                ? ins.Code == Op.FCmpG ? 1 : -1
                                : left < right ? -1 : left > right ? 1 : 0;
                            break;
                        }

                        case Op.DCmpL:
                        case Op.DCmpG:
                        {
                            double right = Double(p[sp - 2]);
                            double left = Double(p[sp - 4]);
                            sp -= 3;
                            p[sp - 1] = double.IsNaN(left) || double.IsNaN(right)
                                ? ins.Code == Op.DCmpG ? 1 : -1
                                : left < right ? -1 : left > right ? 1 : 0;
                            break;
                        }

                        // -- conditional branches ---------------------------------------------
                        case Op.IfEq:
                            if ((int)p[--sp] == 0) pc = ins.A;
                            break;

                        case Op.IfNe:
                            if ((int)p[--sp] != 0) pc = ins.A;
                            break;

                        case Op.IfLt:
                            if ((int)p[--sp] < 0) pc = ins.A;
                            break;

                        case Op.IfGe:
                            if ((int)p[--sp] >= 0) pc = ins.A;
                            break;

                        case Op.IfGt:
                            if ((int)p[--sp] > 0) pc = ins.A;
                            break;

                        case Op.IfLe:
                            if ((int)p[--sp] <= 0) pc = ins.A;
                            break;

                        case Op.IfICmpEq:
                            sp -= 2;
                            if ((int)p[sp] == (int)p[sp + 1]) pc = ins.A;
                            break;

                        case Op.IfICmpNe:
                            sp -= 2;
                            if ((int)p[sp] != (int)p[sp + 1]) pc = ins.A;
                            break;

                        case Op.IfICmpLt:
                            sp -= 2;
                            if ((int)p[sp] < (int)p[sp + 1]) pc = ins.A;
                            break;

                        case Op.IfICmpGe:
                            sp -= 2;
                            if ((int)p[sp] >= (int)p[sp + 1]) pc = ins.A;
                            break;

                        case Op.IfICmpGt:
                            sp -= 2;
                            if ((int)p[sp] > (int)p[sp + 1]) pc = ins.A;
                            break;

                        case Op.IfICmpLe:
                            sp -= 2;
                            if ((int)p[sp] <= (int)p[sp + 1]) pc = ins.A;
                            break;

                        case Op.IfACmpEq:
                            sp -= 2;
                            if (ReferenceEquals(r[sp], r[sp + 1])) pc = ins.A;
                            break;

                        case Op.IfACmpNe:
                            sp -= 2;
                            if (!ReferenceEquals(r[sp], r[sp + 1])) pc = ins.A;
                            break;

                        case Op.IfNull:
                            if (r[--sp] is null) pc = ins.A;
                            break;

                        case Op.IfNonNull:
                            if (r[--sp] is not null) pc = ins.A;
                            break;

                        case Op.Goto:
                            // A backward branch is the only place a guest loop can be running
                            // without ever returning, so shutdown is observed here.
                            if (ins.A < pc && thread.AbortRequested)
                                throw new VirtualMachineShutdownException("Execution aborted.");
                            pc = ins.A;
                            break;

                        case Op.Jsr:
                            // The return address is a primitive; astore and aload carry both
                            // halves of a slot precisely so that it survives being stored.
                            p[sp] = pc;
                            r[sp] = null;
                            sp++;
                            pc = ins.A;
                            break;

                        case Op.Ret:
                            pc = (int)p[locals + ins.A];
                            break;

                        case Op.TableSwitch:
                            pc = Unsafe.As<SwitchTable>(ins.Cache!).SelectDense((int)p[--sp]);
                            break;

                        case Op.LookupSwitch:
                            pc = Unsafe.As<SwitchTable>(ins.Cache!).SelectSparse((int)p[--sp]);
                            break;

                        // -- field access -----------------------------------------------------
                        case Op.GetField32:
                        {
                            JavaField field = ins.Cache as JavaField
                                              ?? ResolveFieldSafely(thread, ref ins, method, false, frameIndex, pc, sp, ref p, ref r);
                            if (r[sp - 1] is not JavaObject target) throw NullField(thread, field);
                            p[sp - 1] = target.Fields[field.SlotIndex].Primitive;
                            r[sp - 1] = null;
                            break;
                        }

                        case Op.GetField64:
                        {
                            JavaField field = ins.Cache as JavaField
                                              ?? ResolveFieldSafely(thread, ref ins, method, false, frameIndex, pc, sp, ref p, ref r);
                            if (r[sp - 1] is not JavaObject target) throw NullField(thread, field);
                            p[sp - 1] = target.Fields[field.SlotIndex].Primitive;
                            r[sp - 1] = null;
                            sp++;
                            break;
                        }

                        case Op.GetFieldRef:
                        {
                            JavaField field = ins.Cache as JavaField
                                              ?? ResolveFieldSafely(thread, ref ins, method, false, frameIndex, pc, sp, ref p, ref r);
                            if (r[sp - 1] is not JavaObject target) throw NullField(thread, field);
                            r[sp - 1] = target.Fields[field.SlotIndex].Reference;
                            p[sp - 1] = 0;
                            break;
                        }

                        case Op.PutField32:
                        case Op.PutFieldBoolean:
                        case Op.PutFieldByte:
                        case Op.PutFieldChar:
                        case Op.PutFieldShort:
                        {
                            JavaField field = ins.Cache as JavaField
                                              ?? ResolveFieldSafely(thread, ref ins, method, false, frameIndex, pc, sp, ref p, ref r);
                            sp -= 2;
                            if (r[sp] is not JavaObject target) throw NullField(thread, field);
                            target.Fields[field.SlotIndex] = JavaValue.Raw(Narrow(ins.Code, p[sp + 1]), null);
                            r[sp] = null;
                            break;
                        }

                        case Op.PutField64:
                        {
                            JavaField field = ins.Cache as JavaField
                                              ?? ResolveFieldSafely(thread, ref ins, method, false, frameIndex, pc, sp, ref p, ref r);
                            sp -= 3;
                            if (r[sp] is not JavaObject target) throw NullField(thread, field);
                            target.Fields[field.SlotIndex] = JavaValue.Raw(p[sp + 1], null);
                            r[sp] = null;
                            break;
                        }

                        case Op.PutFieldRef:
                        {
                            JavaField field = ins.Cache as JavaField
                                              ?? ResolveFieldSafely(thread, ref ins, method, false, frameIndex, pc, sp, ref p, ref r);
                            sp -= 2;
                            if (r[sp] is not JavaObject target) throw NullField(thread, field);
                            target.Fields[field.SlotIndex] = JavaValue.Raw(0, r[sp + 1]);
                            r[sp] = null;
                            r[sp + 1] = null;
                            break;
                        }

                        case Op.GetStatic32:
                        {
                            JavaField field = ins.Cache as JavaField
                                              ?? ResolveFieldSafely(thread, ref ins, method, true, frameIndex, pc, sp, ref p, ref r);
                            p[sp++] = field.DeclaringClass.StaticFields[field.SlotIndex].Primitive;
                            break;
                        }

                        case Op.GetStatic64:
                        {
                            JavaField field = ins.Cache as JavaField
                                              ?? ResolveFieldSafely(thread, ref ins, method, true, frameIndex, pc, sp, ref p, ref r);
                            p[sp] = field.DeclaringClass.StaticFields[field.SlotIndex].Primitive;
                            sp += 2;
                            break;
                        }

                        case Op.GetStaticRef:
                        {
                            JavaField field = ins.Cache as JavaField
                                              ?? ResolveFieldSafely(thread, ref ins, method, true, frameIndex, pc, sp, ref p, ref r);
                            r[sp] = field.DeclaringClass.StaticFields[field.SlotIndex].Reference;
                            p[sp] = 0;
                            sp++;
                            break;
                        }

                        case Op.PutStatic32:
                        case Op.PutStaticBoolean:
                        case Op.PutStaticByte:
                        case Op.PutStaticChar:
                        case Op.PutStaticShort:
                        {
                            JavaField field = ins.Cache as JavaField
                                              ?? ResolveFieldSafely(thread, ref ins, method, true, frameIndex, pc, sp, ref p, ref r);
                            sp--;
                            field.DeclaringClass.StaticFields[field.SlotIndex] =
                                JavaValue.Raw(Narrow(ins.Code, p[sp]), null);
                            break;
                        }

                        case Op.PutStatic64:
                        {
                            JavaField field = ins.Cache as JavaField
                                              ?? ResolveFieldSafely(thread, ref ins, method, true, frameIndex, pc, sp, ref p, ref r);
                            sp -= 2;
                            field.DeclaringClass.StaticFields[field.SlotIndex] =
                                JavaValue.Raw(p[sp], null);
                            break;
                        }

                        case Op.PutStaticRef:
                        {
                            JavaField field = ins.Cache as JavaField
                                              ?? ResolveFieldSafely(thread, ref ins, method, true, frameIndex, pc, sp, ref p, ref r);
                            sp--;
                            field.DeclaringClass.StaticFields[field.SlotIndex] =
                                JavaValue.Raw(0, r[sp]);
                            r[sp] = null;
                            break;
                        }

                        // -- allocation and type tests ----------------------------------------
                        case Op.New:
                        {
                            JavaClass type = ins.Cache as JavaClass
                                             ?? ResolveClassSafely(thread, ref ins, method, true, frameIndex, pc, sp, ref p, ref r);
                            r[sp] = vm.NewInstance(type);
                            p[sp] = 0;
                            sp++;
                            break;
                        }

                        case Op.NewArray:
                        {
                            int length = (int)p[sp - 1];
                            if (length < 0) throw vm.NewNegativeArraySizeException(thread, length);

                            JavaClass arrayClass = ins.Cache as JavaClass ?? ResolvePrimitiveArrayClass(vm, ref ins);
                            r[sp - 1] = vm.NewArray(arrayClass, length);
                            p[sp - 1] = 0;
                            break;
                        }

                        case Op.ANewArray:
                        {
                            int length = (int)p[sp - 1];
                            if (length < 0) throw vm.NewNegativeArraySizeException(thread, length);

                            JavaClass componentType = ins.Cache as JavaClass
                                                      ?? ResolveClassSafely(thread, ref ins, method, false, frameIndex, pc, sp, ref p, ref r);
                            r[sp - 1] = vm.NewArray(vm.GetArrayClassOf(componentType), length);
                            p[sp - 1] = 0;
                            break;
                        }

                        case Op.MultiANewArray:
                        {
                            JavaClass arrayClass = ins.Cache as JavaClass
                                                   ?? ResolveClassSafely(thread, ref ins, method, false, frameIndex, pc, sp, ref p, ref r);
                            int dimensions = ins.B;
                            sp -= dimensions;

                            Span<int> lengths = stackalloc int[dimensions];
                            for (int i = 0; i < dimensions; i++)
                            {
                                lengths[i] = (int)p[sp + i];
                                if (lengths[i] < 0) throw vm.NewNegativeArraySizeException(thread, lengths[i]);
                            }

                            r[sp] = vm.NewMultiArray(arrayClass, lengths);
                            p[sp] = 0;
                            sp++;
                            break;
                        }

                        case Op.CheckCast:
                        {
                            if (r[sp - 1] is JavaObject value)
                            {
                                JavaClass type = ins.Cache as JavaClass
                                                 ?? ResolveClassSafely(thread, ref ins, method, false, frameIndex, pc, sp, ref p, ref r);
                                if (!type.IsAssignableFrom(value.Class))
                                    throw vm.NewClassCastException(thread, value.Class, type);
                            }
                            // Casting null always succeeds, so the null case skips resolution too.
                            break;
                        }

                        case Op.InstanceOf:
                        {
                            JavaClass type = ins.Cache as JavaClass
                                             ?? ResolveClassSafely(thread, ref ins, method, false, frameIndex, pc, sp, ref p, ref r);
                            bool result = r[sp - 1] is JavaObject value && type.IsAssignableFrom(value.Class);
                            r[sp - 1] = null;
                            p[sp - 1] = result ? 1 : 0;
                            break;
                        }

                        // -- monitors ---------------------------------------------------------
                        case Op.MonitorEnter:
                        {
                            if (r[--sp] is not JavaObject target)
                                throw vm.NewNullPointerException(thread, "monitorenter on null");

                            thread.Frames[frameIndex].Pc = pc;
                            thread.Frames[frameIndex].Sp = sp;
                            Monitor.Enter(target.Monitor);
                            break;
                        }

                        case Op.MonitorExit:
                        {
                            if (r[--sp] is not JavaObject target)
                                throw vm.NewNullPointerException(thread, "monitorexit on null");

                            try
                            {
                                Monitor.Exit(target.Monitor);
                            }
                            catch (SynchronizationLockException)
                            {
                                throw vm.NewIllegalMonitorStateException(thread);
                            }
                            break;
                        }

                        case Op.AThrow:
                        {
                            if (r[sp - 1] is not JavaObject throwable)
                                throw vm.NewNullPointerException(thread, "throw of null");

                            thread.Frames[frameIndex].Pc = pc;
                            thread.Frames[frameIndex].Sp = sp;
                            throw vm.Rethrow(thread, throwable);
                        }

                        // -- invocation -------------------------------------------------------
                        case Op.InvokeStatic:
                        {
                            CallSite site = ins.Cache as CallSite
                                            ?? ResolveCallSiteSafely(thread, ref ins, method, Op.InvokeStatic, frameIndex, pc, sp, ref p, ref r);
                            JavaMethod target = site.Resolved;

                            thread.Frames[frameIndex].Pc = pc;
                            thread.Frames[frameIndex].Sp = sp;

                            if (target.IsNative)
                            {
                                sp = InvokeNative(thread, target, sp, ref p, ref r);
                                break;
                            }

                            frameIndex = PushCall(thread, target, sp, ref p, ref r);
                            code = target.CompiledCode!;
                            method = target;
                            pc = 0;
                            locals = thread.Frames[frameIndex].LocalsBase;
                            sp = thread.Frames[frameIndex].Sp;
                            break;
                        }

                        case Op.InvokeSpecial:
                        {
                            CallSite site = ins.Cache as CallSite
                                            ?? ResolveCallSiteSafely(thread, ref ins, method, Op.InvokeSpecial, frameIndex, pc, sp, ref p, ref r);
                            JavaMethod target = site.Resolved;

                            if (r[sp - target.ArgumentSlots] is null)
                                throw vm.NewNullPointerException(thread, Describe(target));

                            thread.Frames[frameIndex].Pc = pc;
                            thread.Frames[frameIndex].Sp = sp;

                            if (target.IsNative)
                            {
                                sp = InvokeNative(thread, target, sp, ref p, ref r);
                                break;
                            }

                            frameIndex = PushCall(thread, target, sp, ref p, ref r);
                            code = target.CompiledCode!;
                            method = target;
                            pc = 0;
                            locals = thread.Frames[frameIndex].LocalsBase;
                            sp = thread.Frames[frameIndex].Sp;
                            break;
                        }

                        case Op.InvokeVirtual:
                        case Op.InvokeInterface:
                        {
                            CallSite site = ins.Cache as CallSite
                                            ?? ResolveCallSiteSafely(thread, ref ins, method, ins.Code, frameIndex, pc, sp, ref p, ref r);

                            if (r[sp - site.Resolved.ArgumentSlots] is not JavaObject receiver)
                                throw vm.NewNullPointerException(thread, Describe(site.Resolved));

                            JavaMethod target = Dispatch(thread, site, receiver.Class);

                            thread.Frames[frameIndex].Pc = pc;
                            thread.Frames[frameIndex].Sp = sp;

                            if (target.IsNative)
                            {
                                sp = InvokeNative(thread, target, sp, ref p, ref r);
                                break;
                            }

                            frameIndex = PushCall(thread, target, sp, ref p, ref r);
                            code = target.CompiledCode!;
                            method = target;
                            pc = 0;
                            locals = thread.Frames[frameIndex].LocalsBase;
                            sp = thread.Frames[frameIndex].Sp;
                            break;
                        }

                        // -- returns ----------------------------------------------------------
                        case Op.Return:
                        {
                            ReleaseMonitor(thread, frameIndex);
                            int returnSlot = thread.Frames[frameIndex].LocalsBase;
                            thread.PopFrame();

                            if (thread.FrameCount == entryFrameCount) return JavaValue.Void;

                            frameIndex = thread.FrameCount - 1;
                            code = thread.Frames[frameIndex].Code;
                            method = thread.Frames[frameIndex].Method;
                            pc = thread.Frames[frameIndex].Pc;
                            locals = thread.Frames[frameIndex].LocalsBase;
                            sp = returnSlot;
                            break;
                        }

                        case Op.Return32:
                        {
                            long value = p[sp - 1];
                            ReleaseMonitor(thread, frameIndex);
                            int returnSlot = thread.Frames[frameIndex].LocalsBase;
                            thread.PopFrame();

                            if (thread.FrameCount == entryFrameCount) return JavaValue.Raw(value, null);

                            frameIndex = thread.FrameCount - 1;
                            code = thread.Frames[frameIndex].Code;
                            method = thread.Frames[frameIndex].Method;
                            pc = thread.Frames[frameIndex].Pc;
                            locals = thread.Frames[frameIndex].LocalsBase;
                            sp = returnSlot;
                            p[sp++] = value;
                            break;
                        }

                        case Op.Return64:
                        {
                            long value = p[sp - 2];
                            ReleaseMonitor(thread, frameIndex);
                            int returnSlot = thread.Frames[frameIndex].LocalsBase;
                            thread.PopFrame();

                            if (thread.FrameCount == entryFrameCount) return JavaValue.Raw(value, null);

                            frameIndex = thread.FrameCount - 1;
                            code = thread.Frames[frameIndex].Code;
                            method = thread.Frames[frameIndex].Method;
                            pc = thread.Frames[frameIndex].Pc;
                            locals = thread.Frames[frameIndex].LocalsBase;
                            sp = returnSlot;
                            p[sp] = value;
                            sp += 2;
                            break;
                        }

                        case Op.ReturnRef:
                        {
                            object? value = r[sp - 1];
                            ReleaseMonitor(thread, frameIndex);
                            int returnSlot = thread.Frames[frameIndex].LocalsBase;
                            thread.PopFrame();

                            if (thread.FrameCount == entryFrameCount) return JavaValue.Raw(0, value);

                            frameIndex = thread.FrameCount - 1;
                            code = thread.Frames[frameIndex].Code;
                            method = thread.Frames[frameIndex].Method;
                            pc = thread.Frames[frameIndex].Pc;
                            locals = thread.Frames[frameIndex].LocalsBase;
                            sp = returnSlot;
                            r[sp] = value;
                            p[sp] = 0;
                            sp++;
                            break;
                        }

                        default:
                            throw new InvalidOperationException(
                                $"Interpreter reached unimplemented opcode {ins.Code} in {method}.");
                    }
                }
            }
            catch (JavaThrowable error)
            {
                // Publish the position the throw happened at so the handler search can see it.
                // Frames are always resumed at the instruction after the one that faulted, so
                // the search subtracts one; this keeps callers and the throwing frame uniform.
                //
                // The guard matters when a native method called back into Java and the
                // exception escaped that nested execution: frames above ours may still be on
                // the stack, and their positions are not ours to overwrite.
                if (thread.FrameCount == frameIndex + 1)
                {
                    thread.Frames[frameIndex].Pc = pc;
                    thread.Frames[frameIndex].Sp = sp;
                }

                if (!DispatchException(thread, error, entryFrameCount)) throw;

                frameIndex = thread.FrameCount - 1;
                p = thread.Primitives;
                r = thread.References;
                code = thread.Frames[frameIndex].Code;
                method = thread.Frames[frameIndex].Method;
                pc = thread.Frames[frameIndex].Pc;
                sp = thread.Frames[frameIndex].Sp;
                locals = thread.Frames[frameIndex].LocalsBase;
            }
        }
    }

    // ---------------------------------------------------------------------------------
    // Call helpers
    // ---------------------------------------------------------------------------------

    /// <summary>Pushes a bytecode frame and re-reads the stack arrays in case they grew.</summary>
    private static int PushCall(
        JavaThread thread,
        JavaMethod target,
        int callerSp,
        ref long[] primitives,
        ref object?[] references)
    {
        Instruction[] code = target.EnsureCompiled(thread.Vm);
        int index = thread.PushFrame(target, code, callerSp);

        primitives = thread.Primitives;
        references = thread.References;

        if (target.IsSynchronized)
        {
            JavaObject? monitor = MonitorTarget(thread, target, index);
            if (monitor is not null)
            {
                Monitor.Enter(monitor.Monitor);
                thread.Frames[index].LockedMonitor = monitor;
            }
        }

        return index;
    }

    /// <summary>
    /// Runs a native method and leaves its result on the caller's operand stack.
    /// </summary>
    /// <remarks>
    /// A frame is pushed even though the body is C#, so that a native method appears in Java
    /// stack traces and so that anything it calls back into starts above its own arguments.
    /// </remarks>
    private static int InvokeNative(
        JavaThread thread,
        JavaMethod target,
        int callerSp,
        ref long[] primitives,
        ref object?[] references)
    {
        int index = thread.PushFrame(target, NativeBody, callerSp);
        primitives = thread.Primitives;
        references = thread.References;

        int argumentBase = thread.Frames[index].LocalsBase;

        JavaObject? monitor = null;
        if (target.IsSynchronized)
        {
            monitor = MonitorTarget(thread, target, index);
            if (monitor is not null)
            {
                Monitor.Enter(monitor.Monitor);
                thread.Frames[index].LockedMonitor = monitor;
            }
        }

        JavaValue result;
        try
        {
            var args = new JavaArgs(
                primitives,
                references,
                argumentBase,
                target.ParameterSlotOffsets,
                !target.IsStatic);

            result = target.Native!(thread, args);
        }
        finally
        {
            if (monitor is not null) Monitor.Exit(monitor.Monitor);
        }

        // A native body may have pushed and popped frames of its own, or grown the stack.
        primitives = thread.Primitives;
        references = thread.References;
        thread.PopFrame();

        int sp = argumentBase;
        switch (target.ReturnKind)
        {
            case ValueKind.Void:
                break;

            case ValueKind.Long:
            case ValueKind.Double:
                primitives[sp] = result.Primitive;
                sp += 2;
                break;

            case ValueKind.Reference:
                references[sp] = result.Reference;
                primitives[sp] = 0;
                sp++;
                break;

            default:
                primitives[sp] = result.Primitive;
                sp++;
                break;
        }

        return sp;
    }

    /// <summary>
    /// Selects the method a virtual or interface call site actually reaches, consulting the
    /// site's monomorphic inline cache first.
    /// </summary>
    private static JavaMethod Dispatch(JavaThread thread, CallSite site, JavaClass receiverClass)
    {
        if (ReferenceEquals(receiverClass, site.CachedClass)) return site.CachedTarget!;

        JavaMethod resolved = site.Resolved;
        JavaMethod? target;

        int vtableIndex = resolved.VTableIndex;
        if (vtableIndex >= 0 && vtableIndex < receiverClass.VTable.Length &&
            string.Equals(receiverClass.VTable[vtableIndex].Key, site.Key, StringComparison.Ordinal))
        {
            // The common case: the receiver inherits the same table layout, so the slot the
            // call was resolved against is still the right one.
            target = receiverClass.VTable[vtableIndex];
        }
        else
        {
            // Interface calls and unrelated hierarchies fall back to a full lookup, which is
            // memoised on the receiver class and then cached on this site.
            target = receiverClass.FindMethod(site.Key);
        }

        if (target is null || target.IsAbstract)
        {
            throw thread.Vm.NewAbstractMethodError(
                thread, $"{receiverClass.DisplayName}.{site.Key}");
        }

        site.CachedClass = receiverClass;
        site.CachedTarget = target;
        return target;
    }

    private static void ReleaseMonitor(JavaThread thread, int frameIndex)
    {
        if (thread.Frames[frameIndex].LockedMonitor is not { } monitor) return;

        thread.Frames[frameIndex].LockedMonitor = null;
        Monitor.Exit(monitor.Monitor);
    }

    // ---------------------------------------------------------------------------------
    // Exception dispatch
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Unwinds frames looking for a handler. Returns true when execution should resume in the
    /// frame now on top, false when the exception escapes past the entry frame.
    /// </summary>
    private static bool DispatchException(JavaThread thread, JavaThrowable error, int entryFrameCount)
    {
        VirtualMachine vm = thread.Vm;
        JavaClass thrownClass = error.Throwable.Class;

        while (thread.FrameCount > entryFrameCount)
        {
            int index = thread.FrameCount - 1;
            ref Frame frame = ref thread.Frames[index];
            JavaMethod method = frame.Method;

            // Frames resume after the faulting instruction, so the guarded range is tested
            // against the instruction that actually threw.
            int faultingIndex = frame.Pc - 1;

            if (method is not null && !method.IsNative)
            {
                foreach (ExceptionHandler handler in method.GetHandlers(vm))
                {
                    if (faultingIndex < handler.StartIndex || faultingIndex >= handler.EndIndex) continue;
                    if (!handler.Matches(vm, thrownClass)) continue;

                    // The handler expects an empty stack holding only the throwable.
                    frame.Sp = frame.StackBase;
                    thread.References[frame.Sp] = error.Throwable;
                    thread.Primitives[frame.Sp] = 0;
                    frame.Sp++;
                    frame.Pc = handler.HandlerIndex;
                    return true;
                }
            }

            ReleaseMonitor(thread, index);
            thread.PopFrame();
        }

        return false;
    }

    // ---------------------------------------------------------------------------------
    // Lazy resolution
    // ---------------------------------------------------------------------------------

    // ---------------------------------------------------------------------------------
    // Re-entrant resolution
    //
    // Resolving a constant pool entry can run a class initializer, and that initializer
    // executes on this same thread. Two things then go wrong unless the interpreter hands over
    // cleanly first:
    //
    //   * the nested call computes where to put its frame from `Frames[depth - 1].Sp`, which is
    //     stale while the dispatch loop is holding the real stack pointer in a local -- so it
    //     lands on top of the operand stack we are in the middle of using; and
    //   * pushing those frames may grow the stack, reallocating both arrays, leaving the cached
    //     `primitives` and `references` locals pointing at the abandoned ones.
    //
    // The wrappers below publish the position before resolving and re-read the arrays after.
    // They run once per call site, so the steady-state cost is nil.
    // ---------------------------------------------------------------------------------

    private static JavaField ResolveFieldSafely(
        JavaThread thread,
        ref Instruction instruction,
        JavaMethod owner,
        bool isStatic,
        int frameIndex,
        int pc,
        int sp,
        ref long[] primitives,
        ref object?[] references)
    {
        thread.Frames[frameIndex].Pc = pc;
        thread.Frames[frameIndex].Sp = sp;

        JavaField field = ResolveField(thread, ref instruction, owner, isStatic);

        primitives = thread.Primitives;
        references = thread.References;
        return field;
    }

    private static JavaClass ResolveClassSafely(
        JavaThread thread,
        ref Instruction instruction,
        JavaMethod owner,
        bool initialize,
        int frameIndex,
        int pc,
        int sp,
        ref long[] primitives,
        ref object?[] references)
    {
        thread.Frames[frameIndex].Pc = pc;
        thread.Frames[frameIndex].Sp = sp;

        JavaClass type = ResolveClass(thread, ref instruction, owner, initialize);

        primitives = thread.Primitives;
        references = thread.References;
        return type;
    }

    private static CallSite ResolveCallSiteSafely(
        JavaThread thread,
        ref Instruction instruction,
        JavaMethod owner,
        Op kind,
        int frameIndex,
        int pc,
        int sp,
        ref long[] primitives,
        ref object?[] references)
    {
        thread.Frames[frameIndex].Pc = pc;
        thread.Frames[frameIndex].Sp = sp;

        CallSite site = ResolveCallSite(thread, ref instruction, owner, kind);

        primitives = thread.Primitives;
        references = thread.References;
        return site;
    }

    /// <summary>
    /// Resolves a field reference and caches it on the instruction.
    /// </summary>
    /// <remarks>
    /// Only the storage slot is resolved here; the access width was already baked into the
    /// opcode at compile time. The cache is a single reference write, so another thread racing
    /// through the same instruction either sees the resolved field or resolves it again, and
    /// both outcomes are correct.
    /// </remarks>
    private static JavaField ResolveField(
        JavaThread thread,
        ref Instruction instruction,
        JavaMethod owner,
        bool isStatic)
    {
        VirtualMachine vm = thread.Vm;
        ConstantPool pool = owner.DeclaringClass.Pool!;
        int index = instruction.A;

        string className = pool.MemberClassName(index);
        string name = pool.MemberName(index);
        string descriptor = pool.MemberDescriptor(index);

        JavaClass? declaring = vm.TryLoadClass(className)
                               ?? throw vm.NewNoClassDefFoundError(thread, className);

        JavaField field = declaring.FindField(name, descriptor)
                          ?? throw vm.NewNoSuchFieldError(
                              thread, $"{Descriptor.ToDisplayName(className)}.{name}");

        if (isStatic)
        {
            if (!field.IsStatic)
                throw vm.NewException(thread, WellKnown.IncompatibleClassChangeError, field.ToString());

            // Reading or writing a static forces the declaring class to be initialised, which
            // must complete before the resolved field is published to other threads.
            vm.EnsureInitialized(thread, field.DeclaringClass);
        }
        else if (field.IsStatic)
        {
            throw vm.NewException(thread, WellKnown.IncompatibleClassChangeError, field.ToString());
        }

        instruction.Cache = field;
        return field;
    }

    private static JavaClass ResolveClass(
        JavaThread thread,
        ref Instruction instruction,
        JavaMethod owner,
        bool initialize)
    {
        VirtualMachine vm = thread.Vm;
        ConstantPool pool = owner.DeclaringClass.Pool!;
        string name = pool.ClassName(instruction.A);

        JavaClass type = vm.TryLoadClass(name)
                         ?? throw vm.NewNoClassDefFoundError(thread, name);

        if (initialize) vm.EnsureInitialized(thread, type);

        instruction.Cache = type;
        return type;
    }

    /// <summary>Maps a <c>newarray</c> type code to its array class.</summary>
    private static JavaClass ResolvePrimitiveArrayClass(VirtualMachine vm, ref Instruction instruction)
    {
        string name = instruction.A switch
        {
            RawOpcode.ArrayTypeCode.Boolean => "[Z",
            RawOpcode.ArrayTypeCode.Char => "[C",
            RawOpcode.ArrayTypeCode.Float => "[F",
            RawOpcode.ArrayTypeCode.Double => "[D",
            RawOpcode.ArrayTypeCode.Byte => "[B",
            RawOpcode.ArrayTypeCode.Short => "[S",
            RawOpcode.ArrayTypeCode.Int => "[I",
            RawOpcode.ArrayTypeCode.Long => "[J",
            _ => throw new ClassFormatException($"Unknown newarray type code {instruction.A}."),
        };

        JavaClass arrayClass = vm.LoadClass(name);
        instruction.Cache = arrayClass;
        return arrayClass;
    }

    private static JavaObject ResolveConstantReference(JavaThread thread, ref Instruction instruction)
    {
        // Only class literals reach here unresolved; string literals were interned at compile
        // time and are already objects.
        if (instruction.Cache is not string className)
            throw new InvalidOperationException("Reference constant has no resolvable payload.");

        VirtualMachine vm = thread.Vm;
        JavaClass type = vm.TryLoadClass(className)
                         ?? throw vm.NewNoClassDefFoundError(thread, className);

        JavaObject mirror = vm.GetClassMirror(type);
        instruction.Cache = mirror;
        return mirror;
    }

    private static CallSite ResolveCallSite(
        JavaThread thread,
        ref Instruction instruction,
        JavaMethod owner,
        Op kind)
    {
        VirtualMachine vm = thread.Vm;
        ConstantPool pool = owner.DeclaringClass.Pool!;
        int index = instruction.A;

        string className = pool.MemberClassName(index);
        string name = pool.MemberName(index);
        string descriptor = pool.MemberDescriptor(index);
        string key = name + descriptor;

        JavaClass declaring = vm.TryLoadClass(className)
                              ?? throw vm.NewNoClassDefFoundError(thread, className);

        JavaMethod? target = declaring.FindMethod(key);

        // invokespecial on a superclass constructor or method resolves against the named class
        // itself, so a miss here is a genuinely missing method rather than a dispatch failure.
        if (target is null)
        {
            throw vm.NewNoSuchMethodError(
                thread, $"{Descriptor.ToDisplayName(className)}.{name}{descriptor}");
        }

        if (kind == Op.InvokeStatic)
        {
            if (!target.IsStatic)
                throw vm.NewException(thread, WellKnown.IncompatibleClassChangeError, target.ToString());

            vm.EnsureInitialized(thread, target.DeclaringClass);
        }

        var site = new CallSite(target, key);
        instruction.Cache = site;
        return site;
    }

    // ---------------------------------------------------------------------------------
    // Small helpers
    // ---------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Float(long bits) => BitConverter.Int32BitsToSingle((int)bits);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Double(long bits) => BitConverter.Int64BitsToDouble(bits);

    /// <summary>Narrows a stack int to the declared width of the field being written.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Narrow(Op op, long value) => op switch
    {
        Op.PutFieldBoolean or Op.PutStaticBoolean => value & 1,
        Op.PutFieldByte or Op.PutStaticByte => (sbyte)value,
        Op.PutFieldChar or Op.PutStaticChar => (char)value,
        Op.PutFieldShort or Op.PutStaticShort => (short)value,
        _ => (int)value,
    };

    private static JavaThrowable NullArray(JavaThread thread, object? actual) =>
        actual is null
            ? thread.Vm.NewNullPointerException(thread, "array access on null")
            : thread.Vm.NewException(thread, WellKnown.IncompatibleClassChangeError,
                $"expected an array but found {actual.GetType().Name}");

    private static JavaThrowable NullField(JavaThread thread, JavaField field) =>
        thread.Vm.NewNullPointerException(thread, $"field access on null: {field}");

    private static string Describe(JavaMethod method) =>
        $"call to {method.DeclaringClass.DisplayName}.{method.Name} on null";
}
