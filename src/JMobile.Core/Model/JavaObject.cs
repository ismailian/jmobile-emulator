using System.Runtime.CompilerServices;

namespace JMobile.Core.Model;

/// <summary>
/// The base of every Java heap value. Object identity, monitors and garbage collection are
/// all delegated to the CLR: a Java object is a CLR object, so the .NET collector traces
/// Java references for free and there is no second heap to scan or compact.
/// </summary>
/// <remarks>
/// Fields live in a single <see cref="JavaValue"/> array rather than in parallel primitive
/// and reference arrays. Objects are allocated far more often than their fields are written,
/// so one allocation per object beats saving a write barrier per field store. The operand
/// stack makes the opposite trade for the opposite reason.
/// </remarks>
public class JavaObject
{
    /// <summary>Field storage, indexed by <see cref="JavaField.SlotIndex"/>. Empty for arrays.</summary>
    internal readonly JavaValue[] Fields;

    private object? _monitor;

    public JavaObject(JavaClass type, int fieldCount)
    {
        Class = type;
        Fields = fieldCount == 0 ? [] : new JavaValue[fieldCount];
    }

    /// <summary>The exact runtime class of this object.</summary>
    public JavaClass Class { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JavaValue GetField(int slot) => Fields[slot];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetField(int slot, JavaValue value) => Fields[slot] = value;

    /// <summary>
    /// The CLR object used to implement <c>monitorenter</c>, <c>wait</c> and <c>notify</c> for
    /// this instance. Created on first use so that the overwhelming majority of objects, which
    /// are never synchronised on, do not carry the extra allocation.
    /// </summary>
    internal object Monitor
    {
        get
        {
            // Interlocked rather than a lock: two threads racing to create the monitor must
            // agree on the winner, or they would each synchronise on a different object.
            object? monitor = _monitor;
            if (monitor is not null) return monitor;

            var candidate = new object();
            return Interlocked.CompareExchange(ref _monitor, candidate, null) ?? candidate;
        }
    }

    /// <summary>Backs <c>Object.hashCode</c> with the CLR's identity hash.</summary>
    public int IdentityHashCode => RuntimeHelpers.GetHashCode(this);

    /// <summary>
    /// Backs <c>Object.wait</c>. Returns false when the wait timed out rather than being woken.
    /// </summary>
    /// <remarks>
    /// Exposed as an operation rather than by handing out the monitor object, so that the only
    /// way to synchronise on a Java object from anywhere in the emulator is through the same
    /// three methods the guest program itself uses.
    /// </remarks>
    public bool MonitorWait(int timeoutMilliseconds)
    {
        object monitor = Monitor;
        return timeoutMilliseconds <= 0
            ? System.Threading.Monitor.Wait(monitor)
            : System.Threading.Monitor.Wait(monitor, timeoutMilliseconds);
    }

    /// <summary>Backs <c>Object.notify</c>.</summary>
    public void MonitorNotify() => System.Threading.Monitor.Pulse(Monitor);

    /// <summary>Backs <c>Object.notifyAll</c>.</summary>
    public void MonitorNotifyAll() => System.Threading.Monitor.PulseAll(Monitor);

    /// <summary>True when the calling thread owns this object's monitor.</summary>
    public bool HoldsMonitor => System.Threading.Monitor.IsEntered(Monitor);

    public override string ToString() => $"{Class.DisplayName}@{IdentityHashCode:x}";
}
