using JMobile.Core.ClassFile;
using JMobile.Core.Execution;
using JMobile.Core.Native;

namespace JMobile.Core.Model;

/// <summary>A linked method: its declaration, its executable body, and its dispatch metadata.</summary>
public sealed class JavaMethod
{
    private Instruction[]? _compiled;
    private ExceptionHandler[]? _handlers;

    public JavaMethod(JavaClass declaringClass, string name, string descriptor, AccessFlags flags)
    {
        DeclaringClass = declaringClass;
        Name = name;
        Descriptor = descriptor;
        Flags = flags;
        Key = name + descriptor;

        ReturnKind = ClassFile.Descriptor.ReturnKind(descriptor);
        ParameterSlotOffsets = ClassFile.Descriptor.ParameterSlotOffsets(descriptor);
        int parameterSlots = ClassFile.Descriptor.ParameterSlots(descriptor);
        ArgumentSlots = parameterSlots + (flags.IsStatic() ? 0 : 1);
    }

    public JavaClass DeclaringClass { get; }

    public string Name { get; }

    public string Descriptor { get; }

    /// <summary>
    /// Name and descriptor concatenated. Precomputed because it is the key for every method
    /// lookup, and building it per lookup would allocate on the dispatch path.
    /// </summary>
    public string Key { get; }

    public AccessFlags Flags { get; }

    public ValueKind ReturnKind { get; }

    /// <summary>Total argument slots including the receiver, i.e. the size of the incoming frame.</summary>
    public int ArgumentSlots { get; }

    /// <summary>Per-parameter slot offsets, relative to the first parameter slot.</summary>
    public int[] ParameterSlotOffsets { get; }

    public bool IsStatic => Flags.IsStatic();

    public bool IsAbstract => Flags.IsAbstract();

    public bool IsSynchronized => Flags.IsSynchronized();

    public bool IsConstructor => Name.Length == 6 && Name == "<init>";

    public bool IsClassInitializer => Name.Length == 8 && Name == "<clinit>";

    /// <summary>Operand stack depth declared by the Code attribute.</summary>
    public int MaxStack { get; internal set; }

    /// <summary>Local variable slots declared by the Code attribute.</summary>
    public int MaxLocals { get; internal set; }

    /// <summary>Total stack slots one activation of this method needs.</summary>
    public int FrameSize => MaxLocals + MaxStack;

    /// <summary>Raw bytecode, or null for abstract and native methods.</summary>
    internal byte[]? RawCode { get; set; }

    internal ExceptionTableEntry[] RawExceptionTable { get; set; } = [];

    internal LineNumberEntry[] LineNumbers { get; set; } = [];

    /// <summary>
    /// The C# implementation for a native method, or null when the method runs as bytecode.
    /// </summary>
    public NativeMethodBody? Native { get; internal set; }

    public bool IsNative => Native is not null;

    /// <summary>Index in the declaring class's virtual dispatch table, or -1 when not virtual.</summary>
    public int VTableIndex { get; internal set; } = -1;

    /// <summary>
    /// Maps a bytecode offset to the index of the instruction decoded from it, or -1 where the
    /// offset falls inside a multi-byte instruction. Needed to translate exception tables.
    /// </summary>
    internal int[] BytecodeToInstruction { get; set; } = [];

    /// <summary>Inverse of <see cref="BytecodeToInstruction"/>, used to report stack trace lines.</summary>
    internal int[] InstructionToBytecode { get; set; } = [];

    /// <summary>
    /// The decoded instruction stream, produced on first execution.
    /// </summary>
    /// <remarks>
    /// Compilation is lazy because a typical MIDlet suite ships far more methods than any one
    /// play session executes; decoding all of them up front would add startup latency and
    /// memory for nothing.
    /// </remarks>
    public Instruction[]? CompiledCode => _compiled;

    internal Instruction[] EnsureCompiled(VirtualMachine vm)
    {
        // Double-checked: compilation is idempotent and pure, so a benign race that compiles
        // twice is harmless, while taking a lock on every call would not be.
        Instruction[]? compiled = Volatile.Read(ref _compiled);
        if (compiled is not null) return compiled;

        compiled = CodeCompiler.Compile(this, vm);
        Volatile.Write(ref _compiled, compiled);
        return compiled;
    }

    /// <summary>The exception handlers of this method, with catch types resolved.</summary>
    internal ExceptionHandler[] GetHandlers(VirtualMachine vm)
    {
        ExceptionHandler[]? handlers = Volatile.Read(ref _handlers);
        if (handlers is not null) return handlers;

        handlers = ExceptionHandler.Build(this, vm);
        Volatile.Write(ref _handlers, handlers);
        return handlers;
    }

    /// <summary>Maps a bytecode offset to a source line for stack traces, or -1 when unknown.</summary>
    public int LineNumberFor(int bytecodeOffset)
    {
        int line = -1;
        int bestStart = -1;
        foreach (LineNumberEntry entry in LineNumbers)
        {
            // The table is not required to be sorted, so keep the entry with the largest
            // start offset that still precedes the instruction we are reporting on.
            if (entry.StartPc <= bytecodeOffset && entry.StartPc > bestStart)
            {
                bestStart = entry.StartPc;
                line = entry.LineNumber;
            }
        }
        return line;
    }

    public override string ToString() => $"{DeclaringClass.Name}.{Name}{Descriptor}";
}
