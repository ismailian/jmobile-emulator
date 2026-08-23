namespace JMobile.Core.Native;

/// <summary>
/// A group of related runtime-library classes that can be plugged into a virtual machine.
/// </summary>
/// <remarks>
/// Splitting the class library into modules keeps the core free of any knowledge of MIDP. The
/// VM knows how to run bytecode and how to build a class from a <see cref="ClassBuilder"/>;
/// it has no idea that <c>javax.microedition.lcdui</c> exists. Swapping the display module for
/// a headless one, or adding a vendor extension API that a particular game needs, is a matter
/// of registering a different module rather than editing the VM.
/// </remarks>
public interface IRuntimeModule
{
    /// <summary>Human-readable module name, used in startup diagnostics.</summary>
    string Name { get; }

    /// <summary>Declares this module's classes with the registry.</summary>
    void Register(RuntimeRegistry registry);
}

/// <summary>
/// Collects the class definitions contributed by runtime modules.
/// </summary>
/// <remarks>
/// Definitions are stored as deferred configuration callbacks rather than being built eagerly.
/// That makes registration order irrelevant: a module may declare a class whose superclass is
/// contributed by a module registered later, because nothing is built until the VM actually
/// resolves the name, at which point the superclass is resolved recursively.
/// </remarks>
public sealed class RuntimeRegistry
{
    private readonly Dictionary<string, Action<ClassBuilder>> _definitions = new(StringComparer.Ordinal);

    /// <summary>Declares a class. The callback runs the first time the class is resolved.</summary>
    public void Define(string internalName, Action<ClassBuilder> configure)
    {
        _definitions[internalName] = configure;
    }

    /// <summary>Declares an interface with no members beyond those the callback adds.</summary>
    public void DefineInterface(string internalName, Action<ClassBuilder>? configure = null)
    {
        Define(internalName, builder =>
        {
            builder.AsInterface();
            configure?.Invoke(builder);
        });
    }

    /// <summary>
    /// Declares a throwable class. The exception hierarchy is large, uniform and entirely
    /// mechanical, so it is declared with one call per type rather than by hand.
    /// </summary>
    public void DefineThrowable(string internalName, string superInternalName)
    {
        Define(internalName, builder => builder
            .Extends(superInternalName)
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Constructor("(Ljava/lang/String;)V", static (thread, args) =>
            {
                args.This.SetField(ThrowableMessageSlot, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            }));
    }

    /// <summary>
    /// Slot of <c>Throwable.detailMessage</c>. It is slot 0 because <c>java.lang.Throwable</c>
    /// declares it first and extends <c>Object</c>, which has no fields of its own.
    /// </summary>
    public const int ThrowableMessageSlot = 0;

    public bool Contains(string internalName) => _definitions.ContainsKey(internalName);

    public bool TryGet(string internalName, out Action<ClassBuilder> configure) =>
        _definitions.TryGetValue(internalName, out configure!);

    public IEnumerable<string> DefinedClassNames => _definitions.Keys;

    public int Count => _definitions.Count;
}
