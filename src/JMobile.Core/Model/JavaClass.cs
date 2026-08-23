using System.Runtime.CompilerServices;
using JMobile.Core.ClassFile;

namespace JMobile.Core.Model;

/// <summary>Where a class sits in the initialization state machine of JVMS section 5.5.</summary>
public enum ClassState
{
    /// <summary>Parsed and linked, but the static initializer has not run.</summary>
    Linked = 0,

    /// <summary>Some thread is running the static initializer right now.</summary>
    Initializing = 1,

    /// <summary>The static initializer completed successfully.</summary>
    Initialized = 2,

    /// <summary>The static initializer threw; every later use must fail the same way.</summary>
    Failed = 3,
}

/// <summary>
/// A loaded and linked Java class, interface, array type or primitive type.
/// </summary>
/// <remarks>
/// Everything that a running program consults on a hot path is precomputed at link time:
/// field slots, the virtual dispatch table, the superclass chain used for type checks, and
/// the transitive interface set. The interpreter never walks a class hierarchy to answer a
/// question it could have answered with an array index.
/// </remarks>
public sealed class JavaClass
{
    private readonly Dictionary<string, JavaMethod?> _methodLookupCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JavaField?> _fieldLookupCache = new(StringComparer.Ordinal);

    private JavaClass[] _superChain = [];
    private HashSet<JavaClass>? _interfaceClosure;
    private volatile int _state;
    private Thread? _initializingThread;
    private Exception? _initializationFailure;

    internal JavaClass(string name, AccessFlags flags)
    {
        Name = name;
        DisplayName = Descriptor.ToDisplayName(name);
        Flags = flags;
    }

    /// <summary>Internal name, e.g. <c>java/lang/Object</c> or <c>[I</c>.</summary>
    public string Name { get; }

    /// <summary>Dotted name as Java source and <c>Class.getName</c> render it.</summary>
    public string DisplayName { get; }

    public AccessFlags Flags { get; internal set; }

    public JavaClass? Super { get; internal set; }

    public JavaClass[] Interfaces { get; internal set; } = [];

    public JavaField[] DeclaredFields { get; internal set; } = [];

    public JavaMethod[] DeclaredMethods { get; internal set; } = [];

    /// <summary>Constant pool, or null for classes synthesised by the runtime library.</summary>
    public ConstantPool? Pool { get; internal set; }

    /// <summary>Static field storage, indexed by <see cref="JavaField.SlotIndex"/>.</summary>
    public JavaValue[] StaticFields { get; internal set; } = [];

    /// <summary>Virtual dispatch table; index with <see cref="JavaMethod.VTableIndex"/>.</summary>
    public JavaMethod[] VTable { get; internal set; } = [];

    /// <summary>Total instance field slots including those inherited from superclasses.</summary>
    public int InstanceFieldCount { get; internal set; }

    /// <summary>Distance from <c>java.lang.Object</c>, which has depth 0.</summary>
    public int Depth { get; private set; }

    public string? SourceFile { get; internal set; }

    public bool IsInterface => Flags.IsInterface();

    public bool IsAbstract => Flags.IsAbstract();

    public bool IsArray { get; internal init; }

    public bool IsPrimitive { get; internal init; }

    /// <summary>Element type of an array class, i.e. <c>int</c> for <c>int[][]</c>.</summary>
    public JavaClass? ComponentType { get; internal init; }

    /// <summary>Storage shape of this array class's elements.</summary>
    public FieldType ArrayElementType { get; internal init; }

    /// <summary>The <c>java.lang.Class</c> instance mirroring this type, created on demand.</summary>
    public JavaObject? Mirror { get; internal set; }

    /// <summary>
    /// Extension point letting the runtime library attach host state to a class without the
    /// core needing to know it exists, for example the pixel buffer format behind an Image.
    /// </summary>
    public object? HostTag { get; set; }

    public ClassState State => (ClassState)_state;

    /// <summary>
    /// True when instances of this class carry a host-backed representation and therefore must
    /// be allocated by the runtime library rather than by a plain <c>new</c>.
    /// </summary>
    public Func<JavaClass, JavaObject>? Allocator { get; internal set; }

    // ---------------------------------------------------------------------------------
    // Linking
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Computes the superclass chain used for constant-time subclass tests. The chain is
    /// ordered from <c>java.lang.Object</c> down to this class, so "is A a subclass of B" is
    /// answered by a single indexed comparison rather than by walking parent pointers.
    /// </summary>
    internal void BuildTypeHierarchy()
    {
        if (Super is null)
        {
            Depth = 0;
            _superChain = [this];
        }
        else
        {
            Depth = Super.Depth + 1;
            _superChain = new JavaClass[Depth + 1];
            Array.Copy(Super._superChain, _superChain, Depth);
            _superChain[Depth] = this;
        }

        var closure = new HashSet<JavaClass>();
        if (Super is not null)
        {
            foreach (JavaClass parentInterface in Super.InterfaceClosure)
                closure.Add(parentInterface);
        }

        foreach (JavaClass directInterface in Interfaces)
        {
            closure.Add(directInterface);
            foreach (JavaClass inherited in directInterface.InterfaceClosure)
                closure.Add(inherited);
        }

        if (IsInterface) closure.Add(this);

        _interfaceClosure = closure;
    }

    /// <summary>Every interface this type implements, directly or transitively.</summary>
    public IReadOnlyCollection<JavaClass> InterfaceClosure =>
        _interfaceClosure ?? (IReadOnlyCollection<JavaClass>)Array.Empty<JavaClass>();

    // ---------------------------------------------------------------------------------
    // Type tests
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Implements the assignability rule behind <c>checkcast</c>, <c>instanceof</c> and
    /// <c>aastore</c>: can a value of <paramref name="other"/> be stored where this type is expected?
    /// </summary>
    public bool IsAssignableFrom(JavaClass other)
    {
        if (ReferenceEquals(this, other)) return true;

        // Everything, arrays and interfaces included, is assignable to Object.
        if (Super is null && !IsInterface && !IsArray && !IsPrimitive) return true;

        if (IsInterface)
            return other._interfaceClosure is not null && other._interfaceClosure.Contains(this);

        if (IsArray)
        {
            if (!other.IsArray) return false;

            JavaClass? mine = ComponentType;
            JavaClass? theirs = other.ComponentType;
            if (mine is null || theirs is null) return false;

            // Primitive element types must match exactly; reference elements are covariant,
            // which is the rule that makes aastore need a runtime store check.
            if (mine.IsPrimitive || theirs.IsPrimitive) return ReferenceEquals(mine, theirs);
            return mine.IsAssignableFrom(theirs);
        }

        if (other.IsArray || other.IsPrimitive) return false;

        // Constant-time subclass test: if this class sits at depth d, then any subclass of it
        // has this exact class at index d of its own chain.
        JavaClass[] chain = other._superChain;
        return Depth < chain.Length && ReferenceEquals(chain[Depth], this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsInstance(JavaObject? instance) => instance is not null && IsAssignableFrom(instance.Class);

    // ---------------------------------------------------------------------------------
    // Member lookup
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Resolves a method by name and descriptor across this class, its superclasses and its
    /// interfaces. Results, including misses, are memoised because resolution is driven by
    /// constant pool entries that are hit repeatedly.
    /// </summary>
    public JavaMethod? FindMethod(string key)
    {
        lock (_methodLookupCache)
        {
            if (_methodLookupCache.TryGetValue(key, out JavaMethod? cached)) return cached;
        }

        JavaMethod? found = SearchMethod(key);

        lock (_methodLookupCache)
        {
            _methodLookupCache[key] = found;
        }

        return found;
    }

    public JavaMethod? FindMethod(string name, string descriptor) => FindMethod(name + descriptor);

    private JavaMethod? SearchMethod(string key)
    {
        for (JavaClass? type = this; type is not null; type = type.Super)
        {
            foreach (JavaMethod method in type.DeclaredMethods)
            {
                if (string.Equals(method.Key, key, StringComparison.Ordinal)) return method;
            }
        }

        // Interface methods are searched last so a class implementation always wins.
        //
        // An abstract interface declaration is a legitimate result: resolving
        // `invokeinterface HttpConnection.close()` has to succeed even though every declaration
        // of close() up that interface chain is abstract, because the actual target is chosen
        // from the receiver afterwards. A concrete declaration is still preferred where one
        // exists, which is what makes a default method win over the abstract one it overrides.
        JavaMethod? abstractMatch = null;

        if (_interfaceClosure is not null)
        {
            foreach (JavaClass contract in _interfaceClosure)
            {
                foreach (JavaMethod method in contract.DeclaredMethods)
                {
                    if (!string.Equals(method.Key, key, StringComparison.Ordinal)) continue;

                    if (!method.IsAbstract) return method;
                    abstractMatch ??= method;
                }
            }
        }

        return abstractMatch;
    }

    /// <summary>Finds a method declared directly by this class, ignoring inheritance.</summary>
    public JavaMethod? FindDeclaredMethod(string name, string descriptor)
    {
        foreach (JavaMethod method in DeclaredMethods)
        {
            if (string.Equals(method.Name, name, StringComparison.Ordinal) &&
                string.Equals(method.Descriptor, descriptor, StringComparison.Ordinal))
            {
                return method;
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves a field by name and descriptor. Field resolution searches the class chain
    /// first and then interfaces, which is where interface constants live.
    /// </summary>
    public JavaField? FindField(string name, string descriptor)
    {
        string key = name + ":" + descriptor;

        lock (_fieldLookupCache)
        {
            if (_fieldLookupCache.TryGetValue(key, out JavaField? cached)) return cached;
        }

        JavaField? found = SearchField(name, descriptor);

        lock (_fieldLookupCache)
        {
            _fieldLookupCache[key] = found;
        }

        return found;
    }

    private JavaField? SearchField(string name, string descriptor)
    {
        for (JavaClass? type = this; type is not null; type = type.Super)
        {
            foreach (JavaField field in type.DeclaredFields)
            {
                if (string.Equals(field.Name, name, StringComparison.Ordinal) &&
                    string.Equals(field.Descriptor, descriptor, StringComparison.Ordinal))
                {
                    return field;
                }
            }
        }

        if (_interfaceClosure is not null)
        {
            foreach (JavaClass contract in _interfaceClosure)
            {
                foreach (JavaField field in contract.DeclaredFields)
                {
                    if (string.Equals(field.Name, name, StringComparison.Ordinal) &&
                        string.Equals(field.Descriptor, descriptor, StringComparison.Ordinal))
                    {
                        return field;
                    }
                }
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------------
    // Initialization
    // ---------------------------------------------------------------------------------

    /// <summary>Fast path predicate letting call sites skip the initialization barrier entirely.</summary>
    public bool IsInitialized
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _state == (int)ClassState.Initialized;
    }

    internal void MarkInitialized() => _state = (int)ClassState.Initialized;

    internal bool TryBeginInitialization(Thread current, out bool alreadyDone)
    {
        lock (this)
        {
            switch ((ClassState)_state)
            {
                case ClassState.Initialized:
                    alreadyDone = true;
                    return false;

                case ClassState.Failed:
                    alreadyDone = true;
                    throw _initializationFailure ??
                          new InvalidOperationException($"Class {DisplayName} failed to initialize.");

                case ClassState.Initializing when ReferenceEquals(_initializingThread, current):
                    // Recursive initialization from the class's own static initializer is
                    // explicitly legal and must not deadlock or re-run the initializer.
                    alreadyDone = true;
                    return false;

                case ClassState.Initializing:
                    while ((ClassState)_state == ClassState.Initializing)
                        Monitor.Wait(this);

                    alreadyDone = true;
                    if ((ClassState)_state == ClassState.Failed)
                    {
                        throw _initializationFailure ??
                              new InvalidOperationException($"Class {DisplayName} failed to initialize.");
                    }
                    return false;

                default:
                    _state = (int)ClassState.Initializing;
                    _initializingThread = current;
                    alreadyDone = false;
                    return true;
            }
        }
    }

    internal void EndInitialization(Exception? failure)
    {
        lock (this)
        {
            _state = failure is null ? (int)ClassState.Initialized : (int)ClassState.Failed;
            _initializationFailure = failure;
            _initializingThread = null;
            Monitor.PulseAll(this);
        }
    }

    public override string ToString() => DisplayName;
}
