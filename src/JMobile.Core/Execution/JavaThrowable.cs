using JMobile.Core.Model;

namespace JMobile.Core.Execution;

/// <summary>
/// Carries a Java exception through the host stack.
/// </summary>
/// <remarks>
/// Java exceptions ride on CLR exceptions rather than on a status flag checked after every
/// instruction. Throwing is rare and the CLR pays nothing for a try region that never fires,
/// so this keeps the cost entirely on the exceptional path. It also means an exception raised
/// deep inside a native runtime method unwinds correctly without every intermediate native
/// frame having to check and propagate a return code.
/// </remarks>
public sealed class JavaThrowable : Exception
{
    public JavaThrowable(JavaObject throwable, string message)
        : base(message)
    {
        Throwable = throwable;
    }

    /// <summary>The Java <c>Throwable</c> instance being propagated.</summary>
    public JavaObject Throwable { get; }

    /// <summary>Java-side stack trace captured at the throw point, for diagnostics.</summary>
    public string[] JavaStackTrace { get; internal set; } = [];

    public override string ToString() =>
        JavaStackTrace.Length == 0
            ? Message
            : Message + Environment.NewLine + string.Join(Environment.NewLine,
                JavaStackTrace.Select(frame => "\tat " + frame));
}

/// <summary>
/// Signals that the emulated program should stop, for example because the user closed the
/// game. Unlike <see cref="JavaThrowable"/> this is not catchable by guest code.
/// </summary>
public sealed class VirtualMachineShutdownException(string reason) : Exception(reason);
