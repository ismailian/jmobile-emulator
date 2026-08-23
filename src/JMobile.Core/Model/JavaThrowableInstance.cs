namespace JMobile.Core.Model;

/// <summary>
/// A <c>java.lang.Throwable</c> instance, which carries the Java-side stack trace captured at
/// construction time.
/// </summary>
/// <remarks>
/// The trace is host state rather than a Java array because nothing in guest code needs to see
/// its structure: CLDC's <c>Throwable</c> exposes only <c>printStackTrace</c>. Keeping it as a
/// CLR string array means capturing a trace costs one allocation instead of building a Java
/// array of Java strings that will almost always be discarded unread.
/// </remarks>
public sealed class JavaThrowableInstance(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    /// <summary>Formatted Java frames, newest first, or empty when the throw site is unknown.</summary>
    public string[] StackTrace { get; internal set; } = [];
}
