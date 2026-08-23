using System.Buffers.Binary;
using JMobile.Core.ClassFile;
using JMobile.Core.Model;

namespace JMobile.Core.Execution;

/// <summary>
/// Translates class-file bytecode into the normalised instruction stream the interpreter runs.
/// </summary>
/// <remarks>
/// This runs once per method, the first time it executes, and does the work that would
/// otherwise be repeated on every single instruction dispatch: decoding multi-byte operands,
/// folding <c>wide</c> prefixes, collapsing the load/store/const opcode families, converting
/// branch targets from byte offsets into instruction indices, and interning string constants.
/// <para>
/// It is not a verifier. J2ME suites are almost universally obfuscated and frequently violate
/// constraints a strict verifier would reject while still executing correctly, so the
/// interpreter checks the things that matter for host safety (null receivers, array bounds,
/// casts) at run time and trusts the rest.
/// </para>
/// </remarks>
public static class CodeCompiler
{
    public static Instruction[] Compile(JavaMethod method, VirtualMachine vm)
    {
        byte[]? code = method.RawCode;
        if (code is null || code.Length == 0)
        {
            method.BytecodeToInstruction = [0];
            method.InstructionToBytecode = [];
            return [];
        }

        ConstantPool pool = method.DeclaringClass.Pool
            ?? throw new InvalidOperationException(
                $"{method} has bytecode but its declaring class has no constant pool.");

        var instructions = new List<Instruction>(code.Length / 2 + 4);
        var instructionToBytecode = new List<int>(instructions.Capacity);

        // One extra entry so that an exception table's exclusive end offset, which may equal the
        // code length, still maps to a valid instruction index.
        var bytecodeToInstruction = new int[code.Length + 1];
        Array.Fill(bytecodeToInstruction, -1);

        int offset = 0;
        while (offset < code.Length)
        {
            bytecodeToInstruction[offset] = instructions.Count;
            instructionToBytecode.Add(offset);

            int start = offset;
            Instruction instruction = Decode(code, ref offset, pool, vm, method);
            instructions.Add(instruction);

            if (offset <= start)
                throw new ClassFormatException($"{method}: instruction at {start} consumed no bytes.");
        }

        bytecodeToInstruction[code.Length] = instructions.Count;

        Instruction[] compiled = instructions.ToArray();
        RelocateBranches(compiled, bytecodeToInstruction, method);

        method.BytecodeToInstruction = bytecodeToInstruction;
        method.InstructionToBytecode = instructionToBytecode.ToArray();
        return compiled;
    }

    // ---------------------------------------------------------------------------------
    // Decoding
    // ---------------------------------------------------------------------------------

    private static Instruction Decode(
        byte[] code,
        ref int offset,
        ConstantPool pool,
        VirtualMachine vm,
        JavaMethod method)
    {
        int start = offset;
        byte opcode = code[offset++];

        switch (opcode)
        {
            case RawOpcode.Nop:
                return new Instruction(Op.Nop);

            case RawOpcode.AConstNull:
                return new Instruction(Op.AConstNull);

            // Every way of pushing a small integer collapses into one opcode with an operand.
            case >= RawOpcode.IConstM1 and <= RawOpcode.IConst5:
                return new Instruction(Op.Const32, opcode - RawOpcode.IConst0);

            case RawOpcode.BiPush:
                return new Instruction(Op.Const32, (sbyte)code[offset++]);

            case RawOpcode.SiPush:
            {
                short value = BinaryPrimitives.ReadInt16BigEndian(code.AsSpan(offset));
                offset += 2;
                return new Instruction(Op.Const32, value);
            }

            case RawOpcode.LConst0:
                return Const64(0L);

            case RawOpcode.LConst1:
                return Const64(1L);

            case RawOpcode.FConst0:
                return new Instruction(Op.Const32, BitConverter.SingleToInt32Bits(0f));

            case RawOpcode.FConst1:
                return new Instruction(Op.Const32, BitConverter.SingleToInt32Bits(1f));

            case RawOpcode.FConst2:
                return new Instruction(Op.Const32, BitConverter.SingleToInt32Bits(2f));

            case RawOpcode.DConst0:
                return Const64(BitConverter.DoubleToInt64Bits(0d));

            case RawOpcode.DConst1:
                return Const64(BitConverter.DoubleToInt64Bits(1d));

            case RawOpcode.Ldc:
                return LoadConstant(pool, code[offset++], vm);

            case RawOpcode.LdcW:
            {
                int index = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset));
                offset += 2;
                return LoadConstant(pool, index, vm);
            }

            case RawOpcode.Ldc2W:
            {
                int index = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset));
                offset += 2;
                long bits = pool.TagAt(index) == ConstantTag.Long
                    ? pool.LongValue(index)
                    : BitConverter.DoubleToInt64Bits(pool.DoubleValue(index));
                return Const64(bits);
            }

            // -- local variable access, with the indexed variants folded into one opcode ------
            case RawOpcode.ILoad or RawOpcode.FLoad:
                return new Instruction(Op.Load32, code[offset++]);

            case RawOpcode.LLoad or RawOpcode.DLoad:
                return new Instruction(Op.Load64, code[offset++]);

            case RawOpcode.ALoad:
                return new Instruction(Op.LoadRef, code[offset++]);

            case >= RawOpcode.ILoad0 and <= RawOpcode.ILoad3:
                return new Instruction(Op.Load32, opcode - RawOpcode.ILoad0);

            case >= RawOpcode.LLoad0 and <= RawOpcode.LLoad3:
                return new Instruction(Op.Load64, opcode - RawOpcode.LLoad0);

            case >= RawOpcode.FLoad0 and <= RawOpcode.FLoad3:
                return new Instruction(Op.Load32, opcode - RawOpcode.FLoad0);

            case >= RawOpcode.DLoad0 and <= RawOpcode.DLoad3:
                return new Instruction(Op.Load64, opcode - RawOpcode.DLoad0);

            case >= RawOpcode.ALoad0 and <= RawOpcode.ALoad3:
                return new Instruction(Op.LoadRef, opcode - RawOpcode.ALoad0);

            case RawOpcode.IStore or RawOpcode.FStore:
                return new Instruction(Op.Store32, code[offset++]);

            case RawOpcode.LStore or RawOpcode.DStore:
                return new Instruction(Op.Store64, code[offset++]);

            case RawOpcode.AStore:
                return new Instruction(Op.StoreRef, code[offset++]);

            case >= RawOpcode.IStore0 and <= RawOpcode.IStore3:
                return new Instruction(Op.Store32, opcode - RawOpcode.IStore0);

            case >= RawOpcode.LStore0 and <= RawOpcode.LStore3:
                return new Instruction(Op.Store64, opcode - RawOpcode.LStore0);

            case >= RawOpcode.FStore0 and <= RawOpcode.FStore3:
                return new Instruction(Op.Store32, opcode - RawOpcode.FStore0);

            case >= RawOpcode.DStore0 and <= RawOpcode.DStore3:
                return new Instruction(Op.Store64, opcode - RawOpcode.DStore0);

            case >= RawOpcode.AStore0 and <= RawOpcode.AStore3:
                return new Instruction(Op.StoreRef, opcode - RawOpcode.AStore0);

            // -- array access -----------------------------------------------------------------
            case RawOpcode.IALoad: return new Instruction(Op.ArrayLoadInt);
            case RawOpcode.LALoad: return new Instruction(Op.ArrayLoadLong);
            case RawOpcode.FALoad: return new Instruction(Op.ArrayLoadFloat);
            case RawOpcode.DALoad: return new Instruction(Op.ArrayLoadDouble);
            case RawOpcode.AALoad: return new Instruction(Op.ArrayLoadRef);
            case RawOpcode.BALoad: return new Instruction(Op.ArrayLoadByte);
            case RawOpcode.CALoad: return new Instruction(Op.ArrayLoadChar);
            case RawOpcode.SALoad: return new Instruction(Op.ArrayLoadShort);

            case RawOpcode.IAStore: return new Instruction(Op.ArrayStoreInt);
            case RawOpcode.LAStore: return new Instruction(Op.ArrayStoreLong);
            case RawOpcode.FAStore: return new Instruction(Op.ArrayStoreFloat);
            case RawOpcode.DAStore: return new Instruction(Op.ArrayStoreDouble);
            case RawOpcode.AAStore: return new Instruction(Op.ArrayStoreRef);
            case RawOpcode.BAStore: return new Instruction(Op.ArrayStoreByte);
            case RawOpcode.CAStore: return new Instruction(Op.ArrayStoreChar);
            case RawOpcode.SAStore: return new Instruction(Op.ArrayStoreShort);

            // -- operand stack ----------------------------------------------------------------
            case RawOpcode.Pop: return new Instruction(Op.Pop);
            case RawOpcode.Pop2: return new Instruction(Op.Pop2);
            case RawOpcode.Dup: return new Instruction(Op.Dup);
            case RawOpcode.DupX1: return new Instruction(Op.DupX1);
            case RawOpcode.DupX2: return new Instruction(Op.DupX2);
            case RawOpcode.Dup2: return new Instruction(Op.Dup2);
            case RawOpcode.Dup2X1: return new Instruction(Op.Dup2X1);
            case RawOpcode.Dup2X2: return new Instruction(Op.Dup2X2);
            case RawOpcode.Swap: return new Instruction(Op.Swap);

            // -- arithmetic -------------------------------------------------------------------
            case RawOpcode.IAdd: return new Instruction(Op.IAdd);
            case RawOpcode.ISub: return new Instruction(Op.ISub);
            case RawOpcode.IMul: return new Instruction(Op.IMul);
            case RawOpcode.IDiv: return new Instruction(Op.IDiv);
            case RawOpcode.IRem: return new Instruction(Op.IRem);
            case RawOpcode.INeg: return new Instruction(Op.INeg);
            case RawOpcode.IShl: return new Instruction(Op.IShl);
            case RawOpcode.IShr: return new Instruction(Op.IShr);
            case RawOpcode.IUShr: return new Instruction(Op.IUShr);
            case RawOpcode.IAnd: return new Instruction(Op.IAnd);
            case RawOpcode.IOr: return new Instruction(Op.IOr);
            case RawOpcode.IXor: return new Instruction(Op.IXor);

            case RawOpcode.LAdd: return new Instruction(Op.LAdd);
            case RawOpcode.LSub: return new Instruction(Op.LSub);
            case RawOpcode.LMul: return new Instruction(Op.LMul);
            case RawOpcode.LDiv: return new Instruction(Op.LDiv);
            case RawOpcode.LRem: return new Instruction(Op.LRem);
            case RawOpcode.LNeg: return new Instruction(Op.LNeg);
            case RawOpcode.LShl: return new Instruction(Op.LShl);
            case RawOpcode.LShr: return new Instruction(Op.LShr);
            case RawOpcode.LUShr: return new Instruction(Op.LUShr);
            case RawOpcode.LAnd: return new Instruction(Op.LAnd);
            case RawOpcode.LOr: return new Instruction(Op.LOr);
            case RawOpcode.LXor: return new Instruction(Op.LXor);

            case RawOpcode.FAdd: return new Instruction(Op.FAdd);
            case RawOpcode.FSub: return new Instruction(Op.FSub);
            case RawOpcode.FMul: return new Instruction(Op.FMul);
            case RawOpcode.FDiv: return new Instruction(Op.FDiv);
            case RawOpcode.FRem: return new Instruction(Op.FRem);
            case RawOpcode.FNeg: return new Instruction(Op.FNeg);

            case RawOpcode.DAdd: return new Instruction(Op.DAdd);
            case RawOpcode.DSub: return new Instruction(Op.DSub);
            case RawOpcode.DMul: return new Instruction(Op.DMul);
            case RawOpcode.DDiv: return new Instruction(Op.DDiv);
            case RawOpcode.DRem: return new Instruction(Op.DRem);
            case RawOpcode.DNeg: return new Instruction(Op.DNeg);

            case RawOpcode.IInc:
            {
                int local = code[offset++];
                int delta = (sbyte)code[offset++];
                return new Instruction(Op.IInc, local, delta);
            }

            // -- conversions ------------------------------------------------------------------
            case RawOpcode.I2L: return new Instruction(Op.I2L);
            case RawOpcode.I2F: return new Instruction(Op.I2F);
            case RawOpcode.I2D: return new Instruction(Op.I2D);
            case RawOpcode.L2I: return new Instruction(Op.L2I);
            case RawOpcode.L2F: return new Instruction(Op.L2F);
            case RawOpcode.L2D: return new Instruction(Op.L2D);
            case RawOpcode.F2I: return new Instruction(Op.F2I);
            case RawOpcode.F2L: return new Instruction(Op.F2L);
            case RawOpcode.F2D: return new Instruction(Op.F2D);
            case RawOpcode.D2I: return new Instruction(Op.D2I);
            case RawOpcode.D2L: return new Instruction(Op.D2L);
            case RawOpcode.D2F: return new Instruction(Op.D2F);
            case RawOpcode.I2B: return new Instruction(Op.I2B);
            case RawOpcode.I2C: return new Instruction(Op.I2C);
            case RawOpcode.I2S: return new Instruction(Op.I2S);

            case RawOpcode.LCmp: return new Instruction(Op.LCmp);
            case RawOpcode.FCmpL: return new Instruction(Op.FCmpL);
            case RawOpcode.FCmpG: return new Instruction(Op.FCmpG);
            case RawOpcode.DCmpL: return new Instruction(Op.DCmpL);
            case RawOpcode.DCmpG: return new Instruction(Op.DCmpG);

            // -- branches; targets stay in byte space until relocation ------------------------
            case RawOpcode.IfEq: return Branch(Op.IfEq, code, ref offset, start);
            case RawOpcode.IfNe: return Branch(Op.IfNe, code, ref offset, start);
            case RawOpcode.IfLt: return Branch(Op.IfLt, code, ref offset, start);
            case RawOpcode.IfGe: return Branch(Op.IfGe, code, ref offset, start);
            case RawOpcode.IfGt: return Branch(Op.IfGt, code, ref offset, start);
            case RawOpcode.IfLe: return Branch(Op.IfLe, code, ref offset, start);
            case RawOpcode.IfICmpEq: return Branch(Op.IfICmpEq, code, ref offset, start);
            case RawOpcode.IfICmpNe: return Branch(Op.IfICmpNe, code, ref offset, start);
            case RawOpcode.IfICmpLt: return Branch(Op.IfICmpLt, code, ref offset, start);
            case RawOpcode.IfICmpGe: return Branch(Op.IfICmpGe, code, ref offset, start);
            case RawOpcode.IfICmpGt: return Branch(Op.IfICmpGt, code, ref offset, start);
            case RawOpcode.IfICmpLe: return Branch(Op.IfICmpLe, code, ref offset, start);
            case RawOpcode.IfACmpEq: return Branch(Op.IfACmpEq, code, ref offset, start);
            case RawOpcode.IfACmpNe: return Branch(Op.IfACmpNe, code, ref offset, start);
            case RawOpcode.IfNull: return Branch(Op.IfNull, code, ref offset, start);
            case RawOpcode.IfNonNull: return Branch(Op.IfNonNull, code, ref offset, start);
            case RawOpcode.Goto: return Branch(Op.Goto, code, ref offset, start);
            case RawOpcode.Jsr: return Branch(Op.Jsr, code, ref offset, start);

            case RawOpcode.GotoW: return BranchWide(Op.Goto, code, ref offset, start);
            case RawOpcode.JsrW: return BranchWide(Op.Jsr, code, ref offset, start);

            case RawOpcode.Ret:
                return new Instruction(Op.Ret, code[offset++]);

            case RawOpcode.TableSwitch:
                return DecodeTableSwitch(code, ref offset, start);

            case RawOpcode.LookupSwitch:
                return DecodeLookupSwitch(code, ref offset, start);

            // -- returns ----------------------------------------------------------------------
            case RawOpcode.IReturn or RawOpcode.FReturn: return new Instruction(Op.Return32);
            case RawOpcode.LReturn or RawOpcode.DReturn: return new Instruction(Op.Return64);
            case RawOpcode.AReturn: return new Instruction(Op.ReturnRef);
            case RawOpcode.Return: return new Instruction(Op.Return);

            // -- field access; the width is known here, only the slot is resolved lazily ------
            case RawOpcode.GetStatic: return FieldRef(pool, code, ref offset, isStatic: true, isPut: false);
            case RawOpcode.PutStatic: return FieldRef(pool, code, ref offset, isStatic: true, isPut: true);
            case RawOpcode.GetField: return FieldRef(pool, code, ref offset, isStatic: false, isPut: false);
            case RawOpcode.PutField: return FieldRef(pool, code, ref offset, isStatic: false, isPut: true);

            // -- invocation -------------------------------------------------------------------
            case RawOpcode.InvokeVirtual: return MemberRef(Op.InvokeVirtual, code, ref offset);
            case RawOpcode.InvokeSpecial: return MemberRef(Op.InvokeSpecial, code, ref offset);
            case RawOpcode.InvokeStatic: return MemberRef(Op.InvokeStatic, code, ref offset);

            case RawOpcode.InvokeInterface:
            {
                int index = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset));
                // The count byte and the trailing zero are historical and carry no information
                // the interpreter needs; the descriptor already gives the argument size.
                offset += 4;
                return new Instruction(Op.InvokeInterface, index);
            }

            case RawOpcode.InvokeDynamic:
                throw new ClassFormatException(
                    $"{method}: invokedynamic is not part of CLDC and cannot appear in a MIDlet.");

            // -- allocation and type tests ----------------------------------------------------
            case RawOpcode.New: return MemberRef(Op.New, code, ref offset);
            case RawOpcode.ANewArray: return MemberRef(Op.ANewArray, code, ref offset);
            case RawOpcode.CheckCast: return MemberRef(Op.CheckCast, code, ref offset);
            case RawOpcode.InstanceOf: return MemberRef(Op.InstanceOf, code, ref offset);

            case RawOpcode.NewArray:
                return new Instruction(Op.NewArray, code[offset++]);

            case RawOpcode.MultiANewArray:
            {
                int index = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset));
                offset += 2;
                int dimensions = code[offset++];
                return new Instruction(Op.MultiANewArray, index, dimensions);
            }

            case RawOpcode.ArrayLength: return new Instruction(Op.ArrayLength);
            case RawOpcode.AThrow: return new Instruction(Op.AThrow);
            case RawOpcode.MonitorEnter: return new Instruction(Op.MonitorEnter);
            case RawOpcode.MonitorExit: return new Instruction(Op.MonitorExit);

            case RawOpcode.Wide:
                return DecodeWide(code, ref offset);

            default:
                throw new ClassFormatException(
                    $"{method}: unknown opcode 0x{opcode:X2} at bytecode offset {start}.");
        }
    }

    /// <summary>
    /// Decodes the <c>wide</c> prefix by folding it into the instruction it modifies, so the
    /// interpreter never sees a prefix at all.
    /// </summary>
    private static Instruction DecodeWide(byte[] code, ref int offset)
    {
        byte target = code[offset++];
        int local = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset));
        offset += 2;

        switch (target)
        {
            case RawOpcode.ILoad or RawOpcode.FLoad: return new Instruction(Op.Load32, local);
            case RawOpcode.LLoad or RawOpcode.DLoad: return new Instruction(Op.Load64, local);
            case RawOpcode.ALoad: return new Instruction(Op.LoadRef, local);
            case RawOpcode.IStore or RawOpcode.FStore: return new Instruction(Op.Store32, local);
            case RawOpcode.LStore or RawOpcode.DStore: return new Instruction(Op.Store64, local);
            case RawOpcode.AStore: return new Instruction(Op.StoreRef, local);
            case RawOpcode.Ret: return new Instruction(Op.Ret, local);

            case RawOpcode.IInc:
            {
                short delta = BinaryPrimitives.ReadInt16BigEndian(code.AsSpan(offset));
                offset += 2;
                return new Instruction(Op.IInc, local, delta);
            }

            default:
                throw new ClassFormatException($"Opcode 0x{target:X2} cannot be widened.");
        }
    }

    private static Instruction LoadConstant(ConstantPool pool, int index, VirtualMachine vm)
    {
        switch (pool.TagAt(index))
        {
            case ConstantTag.Integer:
                return new Instruction(Op.Const32, pool.IntValue(index));

            case ConstantTag.Float:
                return new Instruction(Op.Const32, BitConverter.SingleToInt32Bits(pool.FloatValue(index)));

            case ConstantTag.String:
                // Interning here means every later execution of this site is a bare reference
                // push, and it gives literals the identity semantics Java guarantees.
                return new Instruction(Op.ConstRef, cache: vm.Intern(pool.StringValue(index)));

            case ConstantTag.Class:
                // Resolution is deferred: the class name is carried in the cache slot and
                // replaced by the mirror the first time the instruction runs.
                return new Instruction(Op.ConstRef, cache: pool.ClassName(index));

            case ConstantTag.Long:
                return Const64(pool.LongValue(index));

            case ConstantTag.Double:
                return Const64(BitConverter.DoubleToInt64Bits(pool.DoubleValue(index)));

            default:
                throw new ClassFormatException($"Constant pool entry {index} is not loadable by ldc.");
        }
    }

    /// <summary>Packs a 64-bit constant into the instruction's two integer operands.</summary>
    private static Instruction Const64(long bits) =>
        new(Op.Const64, unchecked((int)bits), unchecked((int)(bits >> 32)));

    private static Instruction Branch(Op op, byte[] code, ref int offset, int instructionStart)
    {
        short delta = BinaryPrimitives.ReadInt16BigEndian(code.AsSpan(offset));
        offset += 2;
        return new Instruction(op, instructionStart + delta);
    }

    private static Instruction BranchWide(Op op, byte[] code, ref int offset, int instructionStart)
    {
        int delta = BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(offset));
        offset += 4;
        return new Instruction(op, instructionStart + delta);
    }

    private static Instruction MemberRef(Op op, byte[] code, ref int offset)
    {
        int index = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset));
        offset += 2;
        return new Instruction(op, index);
    }

    /// <summary>
    /// Emits a field access already specialised to the declared width of the field.
    /// </summary>
    /// <remarks>
    /// The field's descriptor is right there in the constant pool, so the interpreter never has
    /// to ask "how wide is this field?" at run time. Only the storage slot needs the owning
    /// class to be loaded, and that stays lazy.
    /// </remarks>
    private static Instruction FieldRef(
        ConstantPool pool,
        byte[] code,
        ref int offset,
        bool isStatic,
        bool isPut)
    {
        int index = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset));
        offset += 2;

        string descriptor = pool.MemberDescriptor(index);
        FieldType type = Descriptor.FieldTypeOf(descriptor[0]);

        Op op = isPut
            ? type switch
            {
                FieldType.Boolean => isStatic ? Op.PutStaticBoolean : Op.PutFieldBoolean,
                FieldType.Byte => isStatic ? Op.PutStaticByte : Op.PutFieldByte,
                FieldType.Char => isStatic ? Op.PutStaticChar : Op.PutFieldChar,
                FieldType.Short => isStatic ? Op.PutStaticShort : Op.PutFieldShort,
                FieldType.Long or FieldType.Double => isStatic ? Op.PutStatic64 : Op.PutField64,
                FieldType.Reference => isStatic ? Op.PutStaticRef : Op.PutFieldRef,
                _ => isStatic ? Op.PutStatic32 : Op.PutField32,
            }
            : type switch
            {
                FieldType.Long or FieldType.Double => isStatic ? Op.GetStatic64 : Op.GetField64,
                FieldType.Reference => isStatic ? Op.GetStaticRef : Op.GetFieldRef,
                _ => isStatic ? Op.GetStatic32 : Op.GetField32,
            };

        return new Instruction(op, index);
    }

    private static Instruction DecodeTableSwitch(byte[] code, ref int offset, int instructionStart)
    {
        // Switch payloads are aligned to a four-byte boundary relative to the start of the code.
        offset = (offset + 3) & ~3;

        int defaultTarget = instructionStart + BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(offset));
        offset += 4;
        int low = BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(offset));
        offset += 4;
        int high = BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(offset));
        offset += 4;

        if (high < low) throw new ClassFormatException("tableswitch has high < low.");

        int count = high - low + 1;
        var targets = new int[count];
        for (int i = 0; i < count; i++)
        {
            targets[i] = instructionStart + BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(offset));
            offset += 4;
        }

        var table = new SwitchTable
        {
            DefaultTarget = defaultTarget,
            Low = low,
            High = high,
            Targets = targets,
        };

        return new Instruction(Op.TableSwitch, cache: table);
    }

    private static Instruction DecodeLookupSwitch(byte[] code, ref int offset, int instructionStart)
    {
        offset = (offset + 3) & ~3;

        int defaultTarget = instructionStart + BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(offset));
        offset += 4;
        int count = BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(offset));
        offset += 4;

        if (count < 0) throw new ClassFormatException("lookupswitch has a negative pair count.");

        var keys = new int[count];
        var targets = new int[count];
        for (int i = 0; i < count; i++)
        {
            keys[i] = BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(offset));
            offset += 4;
            targets[i] = instructionStart + BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(offset));
            offset += 4;
        }

        var table = new SwitchTable
        {
            DefaultTarget = defaultTarget,
            Keys = keys,
            Targets = targets,
        };

        return new Instruction(Op.LookupSwitch, cache: table);
    }

    // ---------------------------------------------------------------------------------
    // Relocation
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Rewrites branch targets from bytecode offsets into instruction indices, so that taking a
    /// branch at run time is an assignment rather than a lookup.
    /// </summary>
    private static void RelocateBranches(Instruction[] instructions, int[] map, JavaMethod method)
    {
        for (int i = 0; i < instructions.Length; i++)
        {
            ref Instruction instruction = ref instructions[i];

            switch (instruction.Code)
            {
                case Op.IfEq or Op.IfNe or Op.IfLt or Op.IfGe or Op.IfGt or Op.IfLe:
                case Op.IfICmpEq or Op.IfICmpNe or Op.IfICmpLt or Op.IfICmpGe or Op.IfICmpGt or Op.IfICmpLe:
                case Op.IfACmpEq or Op.IfACmpNe or Op.IfNull or Op.IfNonNull:
                case Op.Goto or Op.Jsr:
                    instruction.A = Resolve(map, instruction.A, method);
                    break;

                case Op.TableSwitch or Op.LookupSwitch:
                {
                    var table = (SwitchTable)instruction.Cache!;
                    int[] targets = table.Targets;
                    for (int t = 0; t < targets.Length; t++)
                        targets[t] = Resolve(map, targets[t], method);

                    instruction.Cache = new SwitchTable
                    {
                        DefaultTarget = Resolve(map, table.DefaultTarget, method),
                        Low = table.Low,
                        High = table.High,
                        Keys = table.Keys,
                        Targets = targets,
                    };
                    break;
                }
            }
        }
    }

    private static int Resolve(int[] map, int bytecodeOffset, JavaMethod method)
    {
        if ((uint)bytecodeOffset >= (uint)map.Length || map[bytecodeOffset] < 0)
        {
            throw new ClassFormatException(
                $"{method}: branch to bytecode offset {bytecodeOffset}, which is not an instruction boundary.");
        }

        return map[bytecodeOffset];
    }
}
