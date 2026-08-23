namespace JMobile.Core.Model;

/// <summary>
/// A <c>java.lang.String</c> instance backed by a CLR string.
/// </summary>
/// <remarks>
/// Strings are the one class where mirroring the real Java implementation would be a serious
/// mistake. A faithful implementation stores a <c>char[]</c> plus offset and count, so every
/// <c>charAt</c>, <c>equals</c> and <c>substring</c> would run as interpreted bytecode over an
/// interpreted array. Backing the class with a CLR string instead turns the whole of
/// <c>java.lang.String</c> into native calls over an immutable, interned, vectorised type.
/// Since Java strings are immutable this substitution is unobservable to the guest program.
/// </remarks>
public sealed class JavaString(JavaClass type, string value) : JavaObject(type, 0)
{
    /// <summary>
    /// The character data.
    /// </summary>
    /// <remarks>
    /// Assigned once, either at allocation or by a <c>String</c> constructor running against a
    /// freshly allocated instance, and never again. It cannot be <c>readonly</c> because the
    /// bytecode sequence for <c>new String(...)</c> allocates the object before the constructor
    /// that gives it its contents runs; treat it as immutable everywhere else.
    /// </remarks>
    public string Value = value;

    public int Length => Value.Length;

    public override string ToString() => Value;
}
