using JMobile.Core.Model;

namespace JMobile.Runtime.Midp;

/// <summary>The kinds of callback MIDP delivers to a MIDlet.</summary>
public enum MidpEventKind
{
    /// <summary>Repaint a region of the current displayable.</summary>
    Paint,

    KeyPressed,
    KeyReleased,
    KeyRepeated,

    PointerPressed,
    PointerReleased,
    PointerDragged,

    /// <summary>The displayable became visible.</summary>
    ShowNotify,

    /// <summary>The displayable stopped being visible.</summary>
    HideNotify,

    /// <summary>A soft key or menu item was activated.</summary>
    CommandAction,

    /// <summary>A <c>Runnable</c> posted through <c>Display.callSerially</c>.</summary>
    Callback,

    /// <summary>The available drawing area changed.</summary>
    SizeChanged,
}

/// <summary>
/// One queued callback.
/// </summary>
/// <remarks>
/// A struct rather than a class: input events arrive at up to a few hundred per second and a
/// paint request arrives every frame, and none of them should cost an allocation.
/// </remarks>
/// <param name="Kind">Which callback to deliver.</param>
/// <param name="A">First operand: key code, pointer x, or clip left.</param>
/// <param name="B">Second operand: pointer y, or clip top.</param>
/// <param name="C">Clip width, where applicable.</param>
/// <param name="D">Clip height, where applicable.</param>
/// <param name="Target">Displayable the event belongs to, or null for the current one.</param>
/// <param name="Argument">Command or Runnable the event carries.</param>
public readonly record struct MidpEvent(
    MidpEventKind Kind,
    int A = 0,
    int B = 0,
    int C = 0,
    int D = 0,
    JavaObject? Target = null,
    JavaObject? Argument = null);

/// <summary>
/// The serial event queue every MIDlet callback is delivered from.
/// </summary>
/// <remarks>
/// MIDP guarantees that <c>paint</c>, <c>keyPressed</c>, <c>commandAction</c> and everything
/// posted through <c>callSerially</c> run one at a time on a single thread. Games depend on
/// that guarantee heavily: it is why a game can mutate its world in <c>keyPressed</c> without
/// any locking and still read it in <c>paint</c>. Reproducing it exactly is what keeps those
/// games from tearing or crashing here.
/// <para>
/// Repaint requests coalesce. A game that calls <c>repaint()</c> ten times before the next
/// frame should paint once, with the union of the requested regions.
/// </para>
/// </remarks>
public sealed class MidpEventQueue
{
    private readonly Queue<MidpEvent> _events = new(64);
    // A plain object rather than System.Threading.Lock, because this queue needs the
    // Wait/Pulse pair that only monitor-based locking provides.
    private readonly object _gate = new();

    private bool _repaintPending;
    private int _repaintLeft;
    private int _repaintTop;
    private int _repaintRight;
    private int _repaintBottom;
    private bool _closed;

    /// <summary>Number of events waiting, not counting a coalesced repaint.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _events.Count;
            }
        }
    }

    public void Post(MidpEvent midpEvent)
    {
        lock (_gate)
        {
            if (_closed) return;
            _events.Enqueue(midpEvent);
            Monitor.Pulse(_gate);
        }
    }

    /// <summary>Requests a repaint of a region, merging it with any repaint already pending.</summary>
    public void PostRepaint(int x, int y, int width, int height)
    {
        lock (_gate)
        {
            if (_closed) return;

            if (!_repaintPending)
            {
                _repaintPending = true;
                _repaintLeft = x;
                _repaintTop = y;
                _repaintRight = x + width;
                _repaintBottom = y + height;
            }
            else
            {
                _repaintLeft = Math.Min(_repaintLeft, x);
                _repaintTop = Math.Min(_repaintTop, y);
                _repaintRight = Math.Max(_repaintRight, x + width);
                _repaintBottom = Math.Max(_repaintBottom, y + height);
            }

            Monitor.Pulse(_gate);
        }
    }

    /// <summary>
    /// Takes the next callback, waiting up to <paramref name="timeoutMilliseconds"/> for one.
    /// </summary>
    /// <remarks>
    /// Input is drained ahead of painting. A frame that renders the world as it was before the
    /// last keypress is a frame of input lag, and on a game running at fifteen frames a second
    /// that is very visible.
    /// </remarks>
    public bool TryTake(int timeoutMilliseconds, out MidpEvent midpEvent)
    {
        lock (_gate)
        {
            while (true)
            {
                if (_closed)
                {
                    midpEvent = default;
                    return false;
                }

                if (_events.Count > 0)
                {
                    midpEvent = _events.Dequeue();
                    return true;
                }

                if (_repaintPending)
                {
                    _repaintPending = false;
                    midpEvent = new MidpEvent(
                        MidpEventKind.Paint,
                        _repaintLeft,
                        _repaintTop,
                        _repaintRight - _repaintLeft,
                        _repaintBottom - _repaintTop);
                    return true;
                }

                if (!Monitor.Wait(_gate, timeoutMilliseconds))
                {
                    midpEvent = default;
                    return false;
                }
            }
        }
    }

    /// <summary>True when a paint is outstanding, so the host can decide whether to idle.</summary>
    public bool HasWork
    {
        get
        {
            lock (_gate)
            {
                return _events.Count > 0 || _repaintPending;
            }
        }
    }

    /// <summary>Discards everything queued, used when switching away from a displayable.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
            _repaintPending = false;
        }
    }

    /// <summary>Permanently stops the queue and wakes anything waiting on it.</summary>
    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            _events.Clear();
            _repaintPending = false;
            Monitor.PulseAll(_gate);
        }
    }
}
