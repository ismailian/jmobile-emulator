using System.Runtime.CompilerServices;

namespace JMobile.Core;

/// <summary>
/// A single JVM value in its most general form: a 64-bit primitive payload and an
/// object payload. Exactly one of the two is meaningful for any given value, which
/// side is live being determined statically by the verified bytecode.
/// </summary>
/// <remarks>
/// This type is deliberately <b>not</b> used for the interpreter's operand stack.
/// The stack uses two parallel arrays (<c>long[]</c> and <c>object[]</c>) so that
/// integer-only code never touches a reference array and therefore never pays a GC
/// write barrier. <see cref="JavaValue"/> is for the low-frequency boundaries:
/// object fields, static fields, and native method arguments and return values.
/// </remarks>
public readonly struct JavaValue : IEquatable<JavaValue>
{
    /// <summary>Primitive payload. Floats and doubles are stored as raw IEEE-754 bits.</summary>
    public readonly long Primitive;

    /// <summary>Reference payload, or <see langword="null"/> for primitives and Java <c>null</c>.</summary>
    public readonly object? Reference;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JavaValue(long primitive, object? reference)
    {
        Primitive = primitive;
        Reference = reference;
    }

    /// <summary>The canonical zero value, which doubles as Java <c>null</c> and numeric zero.</summary>
    public static readonly JavaValue Null = default;

    /// <summary>The value returned by methods whose descriptor return type is <c>void</c>.</summary>
    public static readonly JavaValue Void = default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JavaValue Int(int value) => new(value, null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JavaValue Bool(bool value) => new(value ? 1 : 0, null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JavaValue Long(long value) => new(value, null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JavaValue Float(float value) => new(BitConverter.SingleToInt32Bits(value), null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JavaValue Double(double value) => new(BitConverter.DoubleToInt64Bits(value), null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JavaValue Ref(object? value) => new(0, value);

    /// <summary>Reconstructs a value straight from its raw halves, used by the interpreter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JavaValue Raw(long primitive, object? reference) => new(primitive, reference);

    public int AsInt
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (int)Primitive;
    }

    public bool AsBool
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Primitive != 0;
    }

    public long AsLong
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Primitive;
    }

    public float AsFloat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BitConverter.Int32BitsToSingle((int)Primitive);
    }

    public double AsDouble
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BitConverter.Int64BitsToDouble(Primitive);
    }

    public bool IsNullReference
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Reference is null;
    }

    public bool Equals(JavaValue other) =>
        Primitive == other.Primitive && ReferenceEquals(Reference, other.Reference);

    public override bool Equals(object? obj) => obj is JavaValue other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Primitive, Reference is null ? 0 : RuntimeHelpers.GetHashCode(Reference));

    public override string ToString() =>
        Reference is not null ? $"ref({Reference})" : $"prim({Primitive})";
}
