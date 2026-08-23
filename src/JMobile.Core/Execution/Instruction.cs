using System.Runtime.InteropServices;
using JMobile.Core.Model;

namespace JMobile.Core.Execution;

/// <summary>
/// One decoded instruction. Twenty-four bytes, laid out so the fields the dispatch loop reads
/// first sit together in the same cache line as the opcode.
/// </summary>
/// <remarks>
/// <see cref="Cache"/> carries whatever the opcode needs: the resolved <see cref="JavaField"/>
/// for field access, a <see cref="CallSite"/> for invocations, a <see cref="JavaClass"/> for
/// allocation and type tests, a <see cref="SwitchTable"/> for switches, or the constant object
/// for <see cref="Op.ConstRef"/>. Sixty-four-bit constants are packed into <see cref="A"/> and
/// <see cref="B"/> so that no instruction ever needs a boxed value.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public struct Instruction
{
    /// <summary>Primary operand: local index, branch target, constant, or dimension count.</summary>
    public int A;

    /// <summary>Secondary operand: increment delta, or the high half of a 64-bit constant.</summary>
    public int B;

    /// <summary>Resolved metadata or inline cache for this site.</summary>
    public object? Cache;

    /// <summary>The normalised operation. Mutable so that field sites can self-specialise.</summary>
    public Op Code;

    public Instruction(Op code, int a = 0, int b = 0, object? cache = null)
    {
        Code = code;
        A = a;
        B = b;
        Cache = cache;
    }

    /// <summary>Reassembles a 64-bit constant from its two packed halves.</summary>
    public readonly long Wide => (uint)A | ((long)B << 32);

    public override readonly string ToString() => $"{Code} A={A} B={B}";
}

/// <summary>
/// Jump table for <c>tableswitch</c> and <c>lookupswitch</c>, with targets already converted
/// to instruction indices.
/// </summary>
public sealed class SwitchTable
{
    /// <summary>Instruction index taken when no case matches.</summary>
    public required int DefaultTarget { get; init; }

    /// <summary>Lowest case value, for the dense (tableswitch) form.</summary>
    public int Low { get; init; }

    /// <summary>Highest case value, for the dense form.</summary>
    public int High { get; init; }

    /// <summary>Case keys in ascending order, for the sparse (lookupswitch) form.</summary>
    public int[]? Keys { get; init; }

    /// <summary>Targets, indexed by <c>value - Low</c> for the dense form or parallel to
    /// <see cref="Keys"/> for the sparse form.</summary>
    public required int[] Targets { get; init; }

    /// <summary>Resolves a dense switch in constant time.</summary>
    public int SelectDense(int value) =>
        value < Low || value > High ? DefaultTarget : Targets[value - Low];

    /// <summary>
    /// Resolves a sparse switch by binary search. The class file format guarantees the keys
    /// are sorted, which is what makes the search valid.
    /// </summary>
    public int SelectSparse(int value)
    {
        int[] keys = Keys!;
        int low = 0, high = keys.Length - 1;
        while (low <= high)
        {
            int mid = (int)(((uint)low + (uint)high) >> 1);
            int key = keys[mid];
            if (key == value) return Targets[mid];
            if (key < value) low = mid + 1;
            else high = mid - 1;
        }
        return DefaultTarget;
    }
}

/// <summary>
/// Per-call-site invocation state: the statically resolved target plus a monomorphic inline
/// cache for the dynamically dispatched kinds.
/// </summary>
/// <remarks>
/// Virtual dispatch through a vtable index is already cheap, but interface dispatch is not:
/// without a cache it costs a dictionary lookup keyed on a string. Real game code is
/// overwhelmingly monomorphic at any given call site, so remembering the last receiver class
/// and its resolved target turns the common case into one reference comparison.
/// </remarks>
public sealed class CallSite(JavaMethod resolved, string key)
{
    /// <summary>The method named by the constant pool entry, before dynamic dispatch.</summary>
    public readonly JavaMethod Resolved = resolved;

    /// <summary>Name and descriptor of the callee, used when the inline cache misses.</summary>
    public readonly string Key = key;

    /// <summary>Receiver class the cache was populated for.</summary>
    public JavaClass? CachedClass;

    /// <summary>Target that <see cref="CachedClass"/> dispatches to.</summary>
    public JavaMethod? CachedTarget;
}
