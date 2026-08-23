namespace JMobile.Core.ClassFile;

/// <summary>One entry of a method's exception table (JVMS section 4.7.3).</summary>
/// <param name="StartPc">Inclusive bytecode offset at which the guarded region begins.</param>
/// <param name="EndPc">Exclusive bytecode offset at which the guarded region ends.</param>
/// <param name="HandlerPc">Bytecode offset of the handler.</param>
/// <param name="CatchTypeIndex">Constant pool index of the caught class, or 0 meaning "any" (finally).</param>
public readonly record struct ExceptionTableEntry(int StartPc, int EndPc, int HandlerPc, int CatchTypeIndex);

/// <summary>One entry of a LineNumberTable, used to build readable Java stack traces.</summary>
public readonly record struct LineNumberEntry(int StartPc, int LineNumber);

/// <summary>Parsed but unlinked field declaration.</summary>
public sealed class FieldData
{
    public required string Name { get; init; }

    public required string Descriptor { get; init; }

    public required AccessFlags Flags { get; init; }

    /// <summary>Constant pool index of a ConstantValue attribute, or 0 when absent.</summary>
    public int ConstantValueIndex { get; init; }
}

/// <summary>Parsed but unlinked method declaration, including its raw Code attribute.</summary>
public sealed class MethodData
{
    public required string Name { get; init; }

    public required string Descriptor { get; init; }

    public required AccessFlags Flags { get; init; }

    public int MaxStack { get; init; }

    public int MaxLocals { get; init; }

    /// <summary>Raw bytecode, or null for abstract and native methods.</summary>
    public byte[]? Code { get; init; }

    public ExceptionTableEntry[] ExceptionTable { get; init; } = [];

    public LineNumberEntry[] LineNumbers { get; init; } = [];
}

/// <summary>
/// The complete contents of one class file after parsing and before linking. Keeping the
/// parse result separate from the linked <see cref="Model.JavaClass"/> means parsing has no
/// dependency on the class loader, which in turn keeps both halves independently testable.
/// </summary>
public sealed class ClassFileData
{
    public required ConstantPool Pool { get; init; }

    public required AccessFlags Flags { get; init; }

    public required string Name { get; init; }

    /// <summary>Internal name of the superclass, null only for <c>java/lang/Object</c>.</summary>
    public string? SuperName { get; init; }

    public string[] InterfaceNames { get; init; } = [];

    public FieldData[] Fields { get; init; } = [];

    public MethodData[] Methods { get; init; } = [];

    public string? SourceFile { get; init; }

    public int MajorVersion { get; init; }

    public int MinorVersion { get; init; }
}
