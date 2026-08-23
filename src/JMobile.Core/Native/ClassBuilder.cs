using JMobile.Core.ClassFile;
using JMobile.Core.Model;

namespace JMobile.Core.Native;

/// <summary>
/// Fluent builder for a class whose members are implemented in C# rather than in bytecode.
/// </summary>
/// <remarks>
/// The entire CLDC and MIDP class library is declared through this builder. That removes the
/// usual bootstrapping problem an emulator has: there is no need to ship a compiled
/// <c>rt.jar</c>, no dependency on a JDK at build time, and no licence question about
/// redistributing someone else's class library. It is also considerably faster, because a call
/// to <c>Vector.addElement</c> lands directly in C# instead of running interpreted bytecode.
/// </remarks>
public sealed class ClassBuilder
{
    private readonly List<FieldDeclaration> _fields = [];
    private readonly List<MethodDeclaration> _methods = [];
    private readonly List<string> _interfaces = [];

    internal ClassBuilder(string internalName)
    {
        InternalName = internalName;
    }

    internal string InternalName { get; }

    internal string SuperName { get; private set; } = "java/lang/Object";

    internal AccessFlags Flags { get; private set; } = AccessFlags.Public;

    internal Func<JavaClass, JavaObject>? Allocator { get; private set; }

    internal IReadOnlyList<string> Interfaces => _interfaces;

    internal IReadOnlyList<FieldDeclaration> Fields => _fields;

    internal IReadOnlyList<MethodDeclaration> Methods => _methods;

    internal readonly record struct FieldDeclaration(
        string Name,
        string Descriptor,
        AccessFlags Flags,
        JavaValue Initial);

    internal readonly record struct MethodDeclaration(
        string Name,
        string Descriptor,
        AccessFlags Flags,
        NativeMethodBody? Body);

    // ---------------------------------------------------------------------------------
    // Shape
    // ---------------------------------------------------------------------------------

    /// <summary>Sets the superclass. Only <c>java/lang/Object</c> may omit this.</summary>
    public ClassBuilder Extends(string superInternalName)
    {
        SuperName = superInternalName;
        return this;
    }

    /// <summary>Declares this type as the root of the hierarchy, which only Object is.</summary>
    public ClassBuilder ExtendsNothing()
    {
        SuperName = string.Empty;
        return this;
    }

    public ClassBuilder Implements(params string[] interfaceInternalNames)
    {
        _interfaces.AddRange(interfaceInternalNames);
        return this;
    }

    public ClassBuilder AsInterface()
    {
        Flags |= AccessFlags.Interface | AccessFlags.Abstract;
        return this;
    }

    public ClassBuilder AsAbstract()
    {
        Flags |= AccessFlags.Abstract;
        return this;
    }

    public ClassBuilder AsFinal()
    {
        Flags |= AccessFlags.Final;
        return this;
    }

    /// <summary>
    /// Supplies a factory for instances of this class, letting the runtime attach host state
    /// such as a pixel buffer or a CLR string to the Java object.
    /// </summary>
    public ClassBuilder AllocatesWith(Func<JavaClass, JavaObject> allocator)
    {
        Allocator = allocator;
        return this;
    }

    // ---------------------------------------------------------------------------------
    // Members
    // ---------------------------------------------------------------------------------

    /// <summary>Declares an instance field. Guest code may read and write it like any other.</summary>
    public ClassBuilder Field(string name, string descriptor, AccessFlags flags = AccessFlags.Public)
    {
        _fields.Add(new FieldDeclaration(name, descriptor, flags & ~AccessFlags.Static, JavaValue.Null));
        return this;
    }

    /// <summary>Declares a static field with an optional compile-time initial value.</summary>
    public ClassBuilder StaticField(
        string name,
        string descriptor,
        JavaValue initial = default,
        AccessFlags flags = AccessFlags.Public | AccessFlags.Static)
    {
        _fields.Add(new FieldDeclaration(name, descriptor, flags | AccessFlags.Static, initial));
        return this;
    }

    /// <summary>Declares a static final int constant, the shape most MIDP constants take.</summary>
    public ClassBuilder Constant(string name, int value) =>
        StaticField(name, "I", JavaValue.Int(value),
            AccessFlags.Public | AccessFlags.Static | AccessFlags.Final);

    public ClassBuilder Constant(string name, long value) =>
        StaticField(name, "J", JavaValue.Long(value),
            AccessFlags.Public | AccessFlags.Static | AccessFlags.Final);

    public ClassBuilder Constant(string name, double value) =>
        StaticField(name, "D", JavaValue.Double(value),
            AccessFlags.Public | AccessFlags.Static | AccessFlags.Final);

    public ClassBuilder Constant(string name, float value) =>
        StaticField(name, "F", JavaValue.Float(value),
            AccessFlags.Public | AccessFlags.Static | AccessFlags.Final);

    /// <summary>Declares a public instance method implemented in C#.</summary>
    public ClassBuilder Method(string name, string descriptor, NativeMethodBody body)
    {
        _methods.Add(new MethodDeclaration(name, descriptor, AccessFlags.Public | AccessFlags.Native, body));
        return this;
    }

    /// <summary>Declares a final instance method, which lets call sites bind without dispatch.</summary>
    public ClassBuilder FinalMethod(string name, string descriptor, NativeMethodBody body)
    {
        _methods.Add(new MethodDeclaration(
            name, descriptor, AccessFlags.Public | AccessFlags.Final | AccessFlags.Native, body));
        return this;
    }

    public ClassBuilder StaticMethod(string name, string descriptor, NativeMethodBody body)
    {
        _methods.Add(new MethodDeclaration(
            name, descriptor, AccessFlags.Public | AccessFlags.Static | AccessFlags.Native, body));
        return this;
    }

    /// <summary>Declares a constructor. Constructors are never virtual, so no vtable slot is used.</summary>
    public ClassBuilder Constructor(string descriptor, NativeMethodBody body)
    {
        _methods.Add(new MethodDeclaration(
            "<init>", descriptor, AccessFlags.Public | AccessFlags.Native, body));
        return this;
    }

    /// <summary>
    /// Declares a constructor that only needs to run the superclass constructor, which is the
    /// common case for the exception hierarchy and for simple value holders.
    /// </summary>
    public ClassBuilder DefaultConstructor() =>
        Constructor("()V", static (_, _) => JavaValue.Void);

    /// <summary>
    /// Declares a static initializer, for the rare runtime class whose static fields hold
    /// objects that cannot exist until the virtual machine is running.
    /// </summary>
    public ClassBuilder StaticInitializer(NativeMethodBody body)
    {
        _methods.Add(new MethodDeclaration(
            "<clinit>", "()V", AccessFlags.Static | AccessFlags.Native, body));
        return this;
    }

    /// <summary>Declares an abstract method that subclasses must implement.</summary>
    public ClassBuilder AbstractMethod(string name, string descriptor)
    {
        _methods.Add(new MethodDeclaration(
            name, descriptor, AccessFlags.Public | AccessFlags.Abstract, null));
        return this;
    }

    /// <summary>Declares an interface method.</summary>
    public ClassBuilder InterfaceMethod(string name, string descriptor) => AbstractMethod(name, descriptor);

    /// <summary>Declares a method that does nothing and returns void, for no-op lifecycle hooks.</summary>
    public ClassBuilder NoOpMethod(string name, string descriptor) =>
        Method(name, descriptor, static (_, _) => JavaValue.Void);
}
