using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;

namespace JMobile.Runtime.Util;

/// <summary>A <c>java.util.Vector</c> backed by a CLR list.</summary>
public sealed class JavaVector(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public List<JavaObject?> Items { get; } = [];

    /// <summary>Honoured by <c>ensureCapacity</c>; growth policy is otherwise the CLR's.</summary>
    public int CapacityIncrement { get; set; }
}

/// <summary>
/// A <c>java.util.Hashtable</c>.
/// </summary>
/// <remarks>
/// This cannot be a CLR dictionary with a custom comparer, because Java key equality is defined
/// by the guest program's own <c>hashCode</c> and <c>equals</c>, which may be interpreted
/// bytecode that can throw and can re-enter the VM. Buckets are therefore managed explicitly, and
/// the two key types that account for nearly all real use — strings and boxed integers — take a
/// fast path that never calls into guest code at all.
/// </remarks>
public sealed class JavaHashtable(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    private readonly Dictionary<int, List<Entry>> _buckets = [];

    public sealed class Entry(JavaObject key, JavaObject? value)
    {
        public readonly JavaObject Key = key;
        public JavaObject? Value = value;
    }

    public int Count { get; private set; }

    /// <summary>Every entry, in no particular order, for the enumeration methods.</summary>
    public IEnumerable<Entry> Entries
    {
        get
        {
            foreach (List<Entry> bucket in _buckets.Values)
            {
                foreach (Entry entry in bucket) yield return entry;
            }
        }
    }

    public JavaObject? Get(JavaThread thread, JavaObject key)
    {
        int hash = HashOf(thread, key);
        if (!_buckets.TryGetValue(hash, out List<Entry>? bucket)) return null;

        foreach (Entry entry in bucket)
        {
            if (KeysEqual(thread, entry.Key, key)) return entry.Value;
        }
        return null;
    }

    public bool ContainsKey(JavaThread thread, JavaObject key)
    {
        if (!_buckets.TryGetValue(HashOf(thread, key), out List<Entry>? bucket)) return false;

        foreach (Entry entry in bucket)
        {
            if (KeysEqual(thread, entry.Key, key)) return true;
        }
        return false;
    }

    /// <summary>Inserts or replaces, returning the value that was displaced.</summary>
    public JavaObject? Put(JavaThread thread, JavaObject key, JavaObject? value)
    {
        int hash = HashOf(thread, key);
        if (!_buckets.TryGetValue(hash, out List<Entry>? bucket))
        {
            bucket = [];
            _buckets[hash] = bucket;
        }

        foreach (Entry entry in bucket)
        {
            if (!KeysEqual(thread, entry.Key, key)) continue;

            JavaObject? previous = entry.Value;
            entry.Value = value;
            return previous;
        }

        bucket.Add(new Entry(key, value));
        Count++;
        return null;
    }

    public JavaObject? Remove(JavaThread thread, JavaObject key)
    {
        int hash = HashOf(thread, key);
        if (!_buckets.TryGetValue(hash, out List<Entry>? bucket)) return null;

        for (int i = 0; i < bucket.Count; i++)
        {
            if (!KeysEqual(thread, bucket[i].Key, key)) continue;

            JavaObject? removed = bucket[i].Value;
            bucket.RemoveAt(i);
            if (bucket.Count == 0) _buckets.Remove(hash);
            Count--;
            return removed;
        }

        return null;
    }

    public void Clear()
    {
        _buckets.Clear();
        Count = 0;
    }

    /// <summary>
    /// Computes a key's hash, short-circuiting for the types the library implements itself.
    /// </summary>
    private static int HashOf(JavaThread thread, JavaObject key)
    {
        if (key is JavaString text) return Lang.JavaText.StringHashCode(text.Value);

        return thread.Vm.CallVirtual(thread, key, "hashCode", "()I").AsInt;
    }

    private static bool KeysEqual(JavaThread thread, JavaObject left, JavaObject right)
    {
        if (ReferenceEquals(left, right)) return true;

        if (left is JavaString leftText)
            return right is JavaString rightText && string.Equals(leftText.Value, rightText.Value, StringComparison.Ordinal);

        return thread.Vm.CallVirtual(thread, left, "equals", "(Ljava/lang/Object;)Z",
            JavaValue.Ref(right)).AsBool;
    }
}

/// <summary>
/// An <c>Enumeration</c> over a snapshot of a collection.
/// </summary>
/// <remarks>
/// The snapshot is taken when the enumeration is created. Java's own enumerations are not
/// snapshots, but they also do not promise anything useful when the collection is modified
/// during iteration, and a snapshot turns a class of crashes in old game code into correct,
/// if slightly stale, iteration.
/// </remarks>
public sealed class JavaEnumeration(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public JavaObject?[] Snapshot { get; set; } = [];

    public int Cursor { get; set; }

    public bool HasMore => Cursor < Snapshot.Length;
}

/// <summary>Java's linear congruential generator, reproduced bit for bit.</summary>
/// <remarks>
/// Games seed this deliberately — for level layouts, for enemy patterns, for daily challenges —
/// and expect the exact sequence the specification defines. A different generator would produce
/// a game that runs but plays wrong, which is harder to notice than a crash.
/// </remarks>
public sealed class JavaRandom(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    private const long Multiplier = 0x5DEECE66DL;
    private const long Addend = 0xBL;
    private const long Mask = (1L << 48) - 1;

    private long _seed;

    public void SetSeed(long seed) => _seed = (seed ^ Multiplier) & Mask;

    public void SeedFromClock() => SetSeed(DateTime.UtcNow.Ticks);

    public int Next(int bits)
    {
        _seed = ((_seed * Multiplier) + Addend) & Mask;
        return (int)((ulong)_seed >> (48 - bits));
    }

    public int NextInt() => Next(32);

    public int NextInt(int bound)
    {
        if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound));

        // A power of two can use the high bits directly, which is both faster and what the
        // specification mandates.
        if ((bound & -bound) == bound) return (int)((bound * (long)Next(31)) >> 31);

        int bits, value;
        do
        {
            bits = Next(31);
            value = bits % bound;
        }
        // Rejects the tail that would otherwise be over-represented.
        while (bits - value + (bound - 1) < 0);

        return value;
    }

    public long NextLong() => ((long)Next(32) << 32) + Next(32);

    public bool NextBoolean() => Next(1) != 0;

    public float NextFloat() => Next(24) / (float)(1 << 24);

    public double NextDouble() => (((long)Next(26) << 27) + Next(27)) * (1.0 / (1L << 53));
}
