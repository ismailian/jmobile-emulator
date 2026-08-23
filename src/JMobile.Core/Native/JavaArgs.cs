using System.Runtime.CompilerServices;
using JMobile.Core.Model;

namespace JMobile.Core.Native;

/// <summary>
/// The argument window a native method sees, projected directly onto the calling thread's
/// stack. Reading an argument is an array index, so calling into the runtime library costs
/// no marshalling and no allocation.
/// </summary>
/// <remarks>
/// Arguments are addressed by <b>declared parameter index</b>, not by stack slot. Long and
/// double parameters occupy two slots each, so a native author who indexed by slot would have
/// to track that by hand and would silently read the wrong argument the moment a signature
/// changed. The slot offsets are precomputed once per method by
/// <see cref="ClassFile.Descriptor.ParameterSlotOffsets"/>.
/// </remarks>
public readonly ref struct JavaArgs
{
    private readonly Span<long> _primitives;
    private readonly Span<object?> _references;
    private readonly int[] _slotOffsets;
    private readonly int _firstParameterSlot;

    internal JavaArgs(
        Span<long> primitives,
        Span<object?> references,
        int localsBase,
        int[] slotOffsets,
        bool hasReceiver)
    {
        _primitives = primitives;
        _references = references;
        _slotOffsets = slotOffsets;
        _firstParameterSlot = localsBase + (hasReceiver ? 1 : 0);
        ReceiverSlot = hasReceiver ? localsBase : -1;
    }

    /// <summary>Slot of the receiver, or -1 for a static method.</summary>
    private int ReceiverSlot { get; }

    /// <summary>Number of declared parameters, excluding the receiver.</summary>
    public int Count => _slotOffsets.Length;

    /// <summary>True when this is an instance method and a receiver is present.</summary>
    public bool HasReceiver => ReceiverSlot >= 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int SlotOf(int parameterIndex) => _firstParameterSlot + _slotOffsets[parameterIndex];

    /// <summary>The receiver of an instance method call.</summary>
    public JavaObject This
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (JavaObject)_references[ReceiverSlot]!;
    }

    /// <summary>The receiver of an instance method call, or null when the call was on null.</summary>
    public JavaObject? ThisOrNull
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ReceiverSlot < 0 ? null : _references[ReceiverSlot] as JavaObject;
    }

    /// <summary>The receiver downcast to a host-backed representation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Self<T>() where T : JavaObject => (T)_references[ReceiverSlot]!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Int(int parameterIndex) => (int)_primitives[SlotOf(parameterIndex)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Bool(int parameterIndex) => _primitives[SlotOf(parameterIndex)] != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public char Char(int parameterIndex) => (char)_primitives[SlotOf(parameterIndex)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Byte(int parameterIndex) => (byte)_primitives[SlotOf(parameterIndex)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short Short(int parameterIndex) => (short)_primitives[SlotOf(parameterIndex)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long Long(int parameterIndex) => _primitives[SlotOf(parameterIndex)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Float(int parameterIndex) =>
        BitConverter.Int32BitsToSingle((int)_primitives[SlotOf(parameterIndex)]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Double(int parameterIndex) =>
        BitConverter.Int64BitsToDouble(_primitives[SlotOf(parameterIndex)]);

    /// <summary>The raw reference argument, which may be Java <c>null</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JavaObject? Object(int parameterIndex) => _references[SlotOf(parameterIndex)] as JavaObject;

    /// <summary>A reference argument downcast to a host-backed representation, preserving null.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? Object<T>(int parameterIndex) where T : JavaObject =>
        _references[SlotOf(parameterIndex)] as T;

    /// <summary>A string argument as a CLR string, or null when the argument was Java <c>null</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? String(int parameterIndex) =>
        (_references[SlotOf(parameterIndex)] as JavaString)?.Value;

    /// <summary>An array argument with its element storage already unwrapped.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T[]? Array<T>(int parameterIndex) =>
        (_references[SlotOf(parameterIndex)] as JavaArray<T>)?.Items;
}
