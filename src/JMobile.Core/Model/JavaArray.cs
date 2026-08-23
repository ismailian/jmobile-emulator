using JMobile.Core.ClassFile;

namespace JMobile.Core.Model;

/// <summary>
/// Base class for Java arrays. The concrete subclass wraps a plain CLR array of the matching
/// primitive type, which means array element access compiles down to an ordinary CLR array
/// access and lets native methods hand buffers such as pixel data straight to the host with
/// no marshalling.
/// </summary>
public abstract class JavaArray(JavaClass type) : JavaObject(type, 0)
{
    public abstract int Length { get; }

    /// <summary>The declared element type, which drives the <c>*aload</c> / <c>*astore</c> family.</summary>
    public FieldType ElementType => Class.ArrayElementType;
}

/// <summary>An array whose elements are stored in a CLR array of <typeparamref name="T"/>.</summary>
public sealed class JavaArray<T>(JavaClass type, T[] items) : JavaArray(type)
{
    /// <summary>The backing storage. Exposed directly so hot paths avoid an accessor call.</summary>
    public readonly T[] Items = items;

    public JavaArray(JavaClass type, int length) : this(type, new T[length]) { }

    public override int Length => Items.Length;

    public override string ToString() => $"{Class.DisplayName}[{Items.Length}]";
}

/// <summary>
/// Factory helpers that pick the right concrete array representation for an element type.
/// Java <c>boolean[]</c> and <c>byte[]</c> share a representation because the bytecode
/// instructions that access them (<c>baload</c> / <c>bastore</c>) are also shared.
/// </summary>
public static class JavaArrayFactory
{
    public static JavaArray Create(JavaClass arrayClass, int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

        return arrayClass.ArrayElementType switch
        {
            FieldType.Boolean or FieldType.Byte => new JavaArray<sbyte>(arrayClass, length),
            FieldType.Char => new JavaArray<char>(arrayClass, length),
            FieldType.Short => new JavaArray<short>(arrayClass, length),
            FieldType.Int => new JavaArray<int>(arrayClass, length),
            FieldType.Long => new JavaArray<long>(arrayClass, length),
            FieldType.Float => new JavaArray<float>(arrayClass, length),
            FieldType.Double => new JavaArray<double>(arrayClass, length),
            _ => new JavaArray<JavaObject?>(arrayClass, length),
        };
    }
}
