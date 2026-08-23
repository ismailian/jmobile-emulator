namespace JMobile.Core.Model;

/// <summary>
/// The <c>java.lang.Class</c> object that guest code sees, holding a direct pointer back to the
/// VM's own class representation.
/// </summary>
/// <remarks>
/// Mirrors are created on demand and cached on <see cref="JavaClass.Mirror"/>, so the identity
/// comparison guest code performs with <c>==</c> between two <c>getClass()</c> results behaves
/// exactly as it does on a real JVM.
/// </remarks>
public sealed class JavaClassMirror(JavaClass type, JavaClass represented) : JavaObject(type, 0)
{
    /// <summary>The type this mirror describes.</summary>
    public readonly JavaClass Represented = represented;

    public override string ToString() => $"class {Represented.DisplayName}";
}
