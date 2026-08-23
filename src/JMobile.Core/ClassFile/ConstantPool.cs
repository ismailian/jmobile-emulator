namespace JMobile.Core.ClassFile;

/// <summary>Constant pool entry tags (JVMS section 4.4).</summary>
public static class ConstantTag
{
    public const byte Utf8 = 1;
    public const byte Integer = 3;
    public const byte Float = 4;
    public const byte Long = 5;
    public const byte Double = 6;
    public const byte Class = 7;
    public const byte String = 8;
    public const byte FieldRef = 9;
    public const byte MethodRef = 10;
    public const byte InterfaceMethodRef = 11;
    public const byte NameAndType = 12;
    public const byte MethodHandle = 15;
    public const byte MethodType = 16;
    public const byte Dynamic = 17;
    public const byte InvokeDynamic = 18;
    public const byte Module = 19;
    public const byte Package = 20;
}

/// <summary>
/// A class's constant pool, stored as a struct-of-arrays rather than as an array of
/// polymorphic entry objects. One pool of a few hundred entries therefore costs four
/// small arrays instead of a few hundred heap objects, which matters because a mid-sized
/// MIDlet suite loads hundreds of classes.
/// </summary>
/// <remarks>
/// UTF-8 entries are decoded eagerly because practically all of them are read during
/// linking anyway. Everything requiring class loading (Class, String, and the member
/// reference kinds) is resolved lazily and memoised in <see cref="_resolved"/>, so a
/// constant is only ever resolved once no matter how many call sites reference it.
/// </remarks>
public sealed class ConstantPool
{
    private readonly byte[] _tags;
    private readonly int[] _a;
    private readonly int[] _b;
    private readonly long[] _numbers;
    private readonly string?[] _strings;
    private readonly object?[] _resolved;

    public ConstantPool(int count)
    {
        Count = count;
        _tags = new byte[count];
        _a = new int[count];
        _b = new int[count];
        _numbers = new long[count];
        _strings = new string?[count];
        _resolved = new object?[count];
    }

    /// <summary>Entry count including the unused index 0 (JVMS numbers constants from 1).</summary>
    public int Count { get; }

    public byte TagAt(int index) => (uint)index < (uint)Count ? _tags[index] : (byte)0;

    internal void Set(int index, byte tag, int a = 0, int b = 0)
    {
        _tags[index] = tag;
        _a[index] = a;
        _b[index] = b;
    }

    internal void SetNumber(int index, byte tag, long value)
    {
        _tags[index] = tag;
        _numbers[index] = value;
    }

    internal void SetString(int index, string value)
    {
        _tags[index] = ConstantTag.Utf8;
        _strings[index] = value;
    }

    /// <summary>Raw first operand of the entry: a name index, class index, or similar.</summary>
    public int First(int index) => _a[index];

    /// <summary>Raw second operand of the entry, e.g. the name-and-type index of a member reference.</summary>
    public int Second(int index) => _b[index];

    public string Utf8(int index)
    {
        Require(index, ConstantTag.Utf8);
        return _strings[index]!;
    }

    public int IntValue(int index)
    {
        Require(index, ConstantTag.Integer);
        return (int)_numbers[index];
    }

    public float FloatValue(int index)
    {
        Require(index, ConstantTag.Float);
        return BitConverter.Int32BitsToSingle((int)_numbers[index]);
    }

    public long LongValue(int index)
    {
        Require(index, ConstantTag.Long);
        return _numbers[index];
    }

    public double DoubleValue(int index)
    {
        Require(index, ConstantTag.Double);
        return BitConverter.Int64BitsToDouble(_numbers[index]);
    }

    /// <summary>The internal name a CONSTANT_Class entry points at, e.g. <c>java/lang/Object</c>.</summary>
    public string ClassName(int index)
    {
        Require(index, ConstantTag.Class);
        return Utf8(_a[index]);
    }

    /// <summary>The literal text a CONSTANT_String entry points at.</summary>
    public string StringValue(int index)
    {
        Require(index, ConstantTag.String);
        return Utf8(_a[index]);
    }

    /// <summary>The internal name of the class declaring the member a reference entry names.</summary>
    public string MemberClassName(int index)
    {
        RequireMember(index);
        return ClassName(_a[index]);
    }

    /// <summary>The simple name of the member a reference entry names.</summary>
    public string MemberName(int index)
    {
        RequireMember(index);
        return Utf8(_a[_b[index]]);
    }

    /// <summary>The type descriptor of the member a reference entry names.</summary>
    public string MemberDescriptor(int index)
    {
        RequireMember(index);
        return Utf8(_b[_b[index]]);
    }

    /// <summary>True when the reference entry was declared as an interface method reference.</summary>
    public bool IsInterfaceMember(int index) => _tags[index] == ConstantTag.InterfaceMethodRef;

    /// <summary>Reads the memoised resolution of an entry, or null if it has not been resolved yet.</summary>
    public object? GetResolved(int index) => _resolved[index];

    /// <summary>Memoises the resolution of an entry so later call sites reuse it.</summary>
    public void SetResolved(int index, object value) => _resolved[index] = value;

    /// <summary>
    /// Reports whether the entry is one of the loadable constant kinds that <c>ldc</c> accepts.
    /// </summary>
    public bool IsLoadable(int index) => _tags[index] is
        ConstantTag.Integer or ConstantTag.Float or ConstantTag.Long or ConstantTag.Double or
        ConstantTag.String or ConstantTag.Class;

    private void Require(int index, byte tag)
    {
        if ((uint)index >= (uint)Count || _tags[index] != tag)
            throw new ClassFormatException(
                $"Constant pool index {index} is not a {NameOf(tag)} (found {NameOf(TagAt(index))}).");
    }

    private void RequireMember(int index)
    {
        byte tag = TagAt(index);
        if (tag is not (ConstantTag.FieldRef or ConstantTag.MethodRef or ConstantTag.InterfaceMethodRef))
            throw new ClassFormatException(
                $"Constant pool index {index} is not a member reference (found {NameOf(tag)}).");
    }

    private static string NameOf(byte tag) => tag switch
    {
        ConstantTag.Utf8 => "Utf8",
        ConstantTag.Integer => "Integer",
        ConstantTag.Float => "Float",
        ConstantTag.Long => "Long",
        ConstantTag.Double => "Double",
        ConstantTag.Class => "Class",
        ConstantTag.String => "String",
        ConstantTag.FieldRef => "Fieldref",
        ConstantTag.MethodRef => "Methodref",
        ConstantTag.InterfaceMethodRef => "InterfaceMethodref",
        ConstantTag.NameAndType => "NameAndType",
        _ => $"tag {tag}",
    };
}
