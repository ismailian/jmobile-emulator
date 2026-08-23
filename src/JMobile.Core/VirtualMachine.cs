using JMobile.Core.ClassFile;
using JMobile.Core.Execution;
using JMobile.Core.Loading;
using JMobile.Core.Model;
using JMobile.Core.Native;

namespace JMobile.Core;

/// <summary>Configuration for a <see cref="VirtualMachine"/> instance.</summary>
public sealed class VirtualMachineOptions
{
    /// <summary>Where guest classes and resources are loaded from.</summary>
    public IClassSource ClassSource { get; set; } = EmptyClassSource.Instance;

    /// <summary>Runtime library modules to install, in any order.</summary>
    public IReadOnlyList<IRuntimeModule> Modules { get; set; } = [];

    /// <summary>Receives <c>System.out</c> writes.</summary>
    public Action<string>? StandardOut { get; set; }

    /// <summary>Receives <c>System.err</c> writes and uncaught exception reports.</summary>
    public Action<string>? StandardError { get; set; }

    /// <summary>Values returned by <c>System.getProperty</c>.</summary>
    public Dictionary<string, string> SystemProperties { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// A Java virtual machine instance: the class registry, the loader, the object allocator and
/// the boundary across which host code calls into Java.
/// </summary>
/// <remarks>
/// One VM owns one guest program. Running a second game creates a second VM rather than
/// resetting this one, which means a game that leaves threads running or corrupts its own
/// static state cannot affect the next game launched.
/// </remarks>
public sealed partial class VirtualMachine
{
    private readonly Dictionary<string, JavaClass> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JavaString> _internedStrings = new(StringComparer.Ordinal);
    private readonly HashSet<string> _classesBeingLoaded = new(StringComparer.Ordinal);
    private readonly Dictionary<char, JavaClass> _primitiveClasses = [];
    private readonly RuntimeRegistry _registry = new();
    private readonly Lock _loadGate = new();
    private readonly Lock _internGate = new();
    private readonly List<JavaThread> _threads = [];

    private int _loadedClassCount;

    public VirtualMachine(VirtualMachineOptions options)
    {
        Options = options;
        ClassSource = options.ClassSource;

        foreach (IRuntimeModule module in options.Modules)
            module.Register(_registry);

        Bootstrap();
    }

    public VirtualMachineOptions Options { get; }

    /// <summary>Services the host provides to the runtime library, such as the display.</summary>
    public ServiceContainer Services { get; } = new();

    /// <summary>Where guest classes come from. Replaceable so one VM can host a new suite.</summary>
    public IClassSource ClassSource { get; set; }

    /// <summary>Set when the VM is shutting down; long-running guest loops observe it and stop.</summary>
    public volatile bool ShuttingDown;

    /// <summary>Number of classes loaded so far, shown in the diagnostics panel.</summary>
    public int LoadedClassCount => _loadedClassCount;

    // Well-known classes, resolved once during bootstrap so the hot paths never look them up.
    public JavaClass ObjectClass { get; private set; } = null!;

    public JavaClass StringClass { get; private set; } = null!;

    public JavaClass ClassClass { get; private set; } = null!;

    public JavaClass ThrowableClass { get; private set; } = null!;

    /// <summary>Slot of <c>Throwable.detailMessage</c>, cached to avoid a lookup per throw.</summary>
    internal int ThrowableMessageSlot { get; private set; }

    // ---------------------------------------------------------------------------------
    // Bootstrap
    // ---------------------------------------------------------------------------------

    private void Bootstrap()
    {
        ObjectClass = LoadClass(WellKnown.Object);
        ClassClass = LoadClass(WellKnown.Class);
        StringClass = LoadClass(WellKnown.String);
        ThrowableClass = LoadClass(WellKnown.Throwable);

        JavaField? message = ThrowableClass.FindField("detailMessage", "Ljava/lang/String;");
        ThrowableMessageSlot = message?.SlotIndex ?? 0;
    }

    // ---------------------------------------------------------------------------------
    // Class loading
    // ---------------------------------------------------------------------------------

    /// <summary>Loads and links a class, throwing if it cannot be found.</summary>
    public JavaClass LoadClass(string internalName)
    {
        return TryLoadClass(internalName)
               ?? throw new TypeLoadException($"Class not found: {Descriptor.ToDisplayName(internalName)}");
    }

    /// <summary>Loads and links a class, returning null when it cannot be found.</summary>
    public JavaClass? TryLoadClass(string internalName)
    {
        // Fast path: already loaded. Taken without the loader lock because the dictionary is
        // only ever added to, and readers are protected by the lock during mutation below.
        lock (_loadGate)
        {
            if (_classes.TryGetValue(internalName, out JavaClass? existing)) return existing;
        }

        lock (_loadGate)
        {
            if (_classes.TryGetValue(internalName, out JavaClass? existing)) return existing;

            if (!_classesBeingLoaded.Add(internalName))
                throw new TypeLoadException(
                    $"Circular class definition involving {Descriptor.ToDisplayName(internalName)}.");

            try
            {
                JavaClass? loaded = CreateClass(internalName);
                if (loaded is null) return null;

                _classes[internalName] = loaded;
                _loadedClassCount++;
                return loaded;
            }
            finally
            {
                _classesBeingLoaded.Remove(internalName);
            }
        }
    }

    /// <summary>Returns a class only if it is already loaded, never triggering a load.</summary>
    public JavaClass? FindLoadedClass(string internalName)
    {
        lock (_loadGate)
        {
            return _classes.GetValueOrDefault(internalName);
        }
    }

    private JavaClass? CreateClass(string internalName)
    {
        if (internalName.Length == 0) return null;

        // Arrays are synthesised rather than loaded.
        //
        // Primitives deliberately are not reachable from here. A bare single-character name is
        // a perfectly ordinary class name -- obfuscators emit classes called `a`, `b` and `e`
        // constantly -- and treating one as a primitive descriptor would make `new e()` fail
        // with "cannot instantiate". Primitive types are only ever created as the component of
        // an array class, which is the only place they can legitimately appear.
        if (internalName[0] == '[') return CreateArrayClass(internalName);

        // The runtime library wins over the suite: a game must not be able to shadow
        // java.lang.String with a class of its own.
        if (_registry.TryGet(internalName, out Action<ClassBuilder> configure))
            return BuildNativeClass(internalName, configure);

        if (ClassSource.TryGetClassBytes(internalName, out byte[] bytes))
        {
            ClassFileData data = ClassFileReader.Read(bytes);
            if (!string.Equals(data.Name, internalName, StringComparison.Ordinal))
            {
                throw new ClassFormatException(
                    $"Class file for {internalName} declares itself as {data.Name}.");
            }
            return LinkClass(data);
        }

        return null;
    }

    // ---------------------------------------------------------------------------------
    // Linking bytecode classes
    // ---------------------------------------------------------------------------------

    private JavaClass LinkClass(ClassFileData data)
    {
        JavaClass? super = data.SuperName is null ? null : LoadClass(data.SuperName);

        var interfaces = data.InterfaceNames.Length == 0
            ? []
            : new JavaClass[data.InterfaceNames.Length];
        for (int i = 0; i < data.InterfaceNames.Length; i++)
            interfaces[i] = LoadClass(data.InterfaceNames[i]);

        var type = new JavaClass(data.Name, data.Flags)
        {
            Super = super,
            Interfaces = interfaces,
            Pool = data.Pool,
            SourceFile = data.SourceFile,

            // Host-backed state is inherited. A game that writes `class Foo extends Thread`, or
            // extends Canvas, GameCanvas, InputStream or Throwable, must still get the instance
            // representation the runtime library needs, or every native method on the base class
            // fails the moment it downcasts its receiver.
            Allocator = super?.Allocator,
        };

        LayOutFields(type, data.Fields.Select(field =>
            (field.Name, field.Descriptor, field.Flags, field.ConstantValueIndex)).ToArray());

        var methods = new JavaMethod[data.Methods.Length];
        for (int i = 0; i < data.Methods.Length; i++)
        {
            MethodData source = data.Methods[i];
            methods[i] = new JavaMethod(type, source.Name, source.Descriptor, source.Flags)
            {
                MaxStack = source.MaxStack,
                MaxLocals = Math.Max(source.MaxLocals, 0),
                RawCode = source.Code,
                RawExceptionTable = source.ExceptionTable,
                LineNumbers = source.LineNumbers,
            };
        }
        type.DeclaredMethods = methods;

        type.BuildTypeHierarchy();
        BuildVirtualTable(type);
        ApplyConstantValues(type, data);

        // A class with no static initializer has nothing to run, so mark it ready immediately
        // and spare every later access the initialization barrier.
        if (type.FindDeclaredMethod("<clinit>", "()V") is null && (super is null || super.IsInitialized))
            type.MarkInitialized();

        return type;
    }

    /// <summary>
    /// Assigns storage slots. Instance fields continue the numbering of the superclass so that
    /// a subclass instance can be read through a superclass field reference without adjustment.
    /// </summary>
    private static void LayOutFields(
        JavaClass type,
        (string Name, string Descriptor, AccessFlags Flags, int ConstantValueIndex)[] declarations)
    {
        int instanceSlot = type.Super?.InstanceFieldCount ?? 0;
        int staticSlot = 0;

        var fields = declarations.Length == 0 ? [] : new JavaField[declarations.Length];
        for (int i = 0; i < declarations.Length; i++)
        {
            var declaration = declarations[i];
            var field = new JavaField(type, declaration.Name, declaration.Descriptor, declaration.Flags)
            {
                ConstantValueIndex = declaration.ConstantValueIndex,
            };

            field.SlotIndex = field.IsStatic ? staticSlot++ : instanceSlot++;
            fields[i] = field;
        }

        type.DeclaredFields = fields;
        type.InstanceFieldCount = instanceSlot;
        type.StaticFields = staticSlot == 0 ? [] : new JavaValue[staticSlot];
    }

    /// <summary>
    /// Builds the virtual dispatch table by copying the superclass table and then either
    /// overriding an inherited slot or appending a new one for each declared virtual method.
    /// </summary>
    private static void BuildVirtualTable(JavaClass type)
    {
        JavaMethod[] inherited = type.Super?.VTable ?? [];
        var table = new List<JavaMethod>(inherited.Length + type.DeclaredMethods.Length);
        table.AddRange(inherited);

        foreach (JavaMethod method in type.DeclaredMethods)
        {
            if (!IsVirtual(method)) continue;

            int slot = -1;
            for (int i = 0; i < table.Count; i++)
            {
                if (string.Equals(table[i].Key, method.Key, StringComparison.Ordinal))
                {
                    slot = i;
                    break;
                }
            }

            if (slot >= 0)
            {
                table[slot] = method;
                method.VTableIndex = slot;
            }
            else
            {
                method.VTableIndex = table.Count;
                table.Add(method);
            }
        }

        type.VTable = table.Count == 0 ? [] : table.ToArray();
    }

    private static bool IsVirtual(JavaMethod method) =>
        !method.IsStatic &&
        !method.Flags.IsPrivate() &&
        !method.IsConstructor &&
        !method.IsClassInitializer;

    /// <summary>Applies ConstantValue attributes, which give static finals their value.</summary>
    private static void ApplyConstantValues(JavaClass type, ClassFileData data)
    {
        ConstantPool pool = data.Pool;

        foreach (JavaField field in type.DeclaredFields)
        {
            if (!field.IsStatic || field.ConstantValueIndex == 0) continue;

            int index = field.ConstantValueIndex;
            type.StaticFields[field.SlotIndex] = pool.TagAt(index) switch
            {
                ConstantTag.Integer => JavaValue.Int(pool.IntValue(index)),
                ConstantTag.Float => JavaValue.Float(pool.FloatValue(index)),
                ConstantTag.Long => JavaValue.Long(pool.LongValue(index)),
                ConstantTag.Double => JavaValue.Double(pool.DoubleValue(index)),
                // String constants are deferred: interning during linking would require the
                // string class to be linked, which is not guaranteed this early.
                _ => JavaValue.Null,
            };
        }
    }

    /// <summary>
    /// Fills in static final String fields, which cannot be initialised during linking because
    /// they need <c>java.lang.String</c> to already exist.
    /// </summary>
    private void ApplyStringConstants(JavaClass type)
    {
        if (type.Pool is not { } pool) return;

        foreach (JavaField field in type.DeclaredFields)
        {
            if (!field.IsStatic || field.ConstantValueIndex == 0) continue;
            if (pool.TagAt(field.ConstantValueIndex) != ConstantTag.String) continue;

            type.StaticFields[field.SlotIndex] =
                JavaValue.Ref(Intern(pool.StringValue(field.ConstantValueIndex)));
        }
    }

    // ---------------------------------------------------------------------------------
    // Building runtime library classes
    // ---------------------------------------------------------------------------------

    private JavaClass BuildNativeClass(string internalName, Action<ClassBuilder> configure)
    {
        var builder = new ClassBuilder(internalName);
        configure(builder);

        JavaClass? super = builder.SuperName.Length == 0 ? null : LoadClass(builder.SuperName);

        var interfaces = builder.Interfaces.Count == 0 ? [] : new JavaClass[builder.Interfaces.Count];
        for (int i = 0; i < interfaces.Length; i++)
            interfaces[i] = LoadClass(builder.Interfaces[i]);

        var type = new JavaClass(internalName, builder.Flags)
        {
            Super = super,
            Interfaces = interfaces,
            Allocator = builder.Allocator ?? super?.Allocator,
        };

        LayOutFields(type, builder.Fields
            .Select(field => (field.Name, field.Descriptor, field.Flags, 0))
            .ToArray());

        var methods = new JavaMethod[builder.Methods.Count];
        for (int i = 0; i < methods.Length; i++)
        {
            ClassBuilder.MethodDeclaration declaration = builder.Methods[i];
            var method = new JavaMethod(type, declaration.Name, declaration.Descriptor, declaration.Flags)
            {
                Native = declaration.Body,
            };

            // Native frames still need locals for their incoming arguments so that JavaArgs can
            // read them, but they never push operands.
            method.MaxLocals = method.ArgumentSlots;
            method.MaxStack = 0;
            methods[i] = method;
        }
        type.DeclaredMethods = methods;

        type.BuildTypeHierarchy();
        BuildVirtualTable(type);

        // Apply the initial values declared on the builder's static fields.
        int index = 0;
        foreach (ClassBuilder.FieldDeclaration declaration in builder.Fields)
        {
            JavaField field = type.DeclaredFields[index++];
            if (field.IsStatic) type.StaticFields[field.SlotIndex] = declaration.Initial;
        }

        // Most runtime classes are ready the moment they are built. The few that need to
        // construct objects for their static fields, such as System.out, declare a native
        // static initializer and go through the normal initialization barrier instead.
        if (type.FindDeclaredMethod("<clinit>", "()V") is null) type.MarkInitialized();

        return type;
    }

    // ---------------------------------------------------------------------------------
    // Array and primitive classes
    // ---------------------------------------------------------------------------------

    private JavaClass CreateArrayClass(string internalName)
    {
        // Everything after the opening bracket is a type *descriptor*, not a class name, so here
        // a single character really does mean a primitive.
        string componentName = internalName[1..];
        JavaClass component = componentName.Length == 1
            ? GetPrimitiveClass(componentName[0])
            : LoadClass(Descriptor.ToInternalName(componentName));

        return new JavaClass(internalName, AccessFlags.Public | AccessFlags.Final)
        {
            Super = ObjectClass,
            IsArray = true,
            ComponentType = component,
            ArrayElementType = Descriptor.FieldTypeOf(internalName[1]),
        }.Also(type =>
        {
            type.BuildTypeHierarchy();
            type.VTable = ObjectClass.VTable;
            type.MarkInitialized();
        });
    }

    /// <summary>
    /// Returns the pseudo-class standing for a primitive type, creating it once.
    /// </summary>
    /// <remarks>
    /// These are cached rather than rebuilt because array assignability compares component types
    /// by reference: <c>int[]</c> is assignable from <c>int[]</c> only if both name the same
    /// <c>int</c>. They are kept out of the main class registry so that a guest class genuinely
    /// named <c>I</c> can never collide with the descriptor for <c>int</c>.
    /// </remarks>
    private JavaClass GetPrimitiveClass(char descriptorChar)
    {
        if (_primitiveClasses.TryGetValue(descriptorChar, out JavaClass? cached)) return cached;

        var created = new JavaClass(
            descriptorChar.ToString(),
            AccessFlags.Public | AccessFlags.Final | AccessFlags.Abstract)
        {
            IsPrimitive = true,
        }.Also(type =>
        {
            type.BuildTypeHierarchy();
            type.MarkInitialized();
        });

        _primitiveClasses[descriptorChar] = created;
        return created;
    }

    /// <summary>Returns the array class whose elements are <paramref name="componentType"/>.</summary>
    public JavaClass GetArrayClassOf(JavaClass componentType)
    {
        string name = componentType.IsPrimitive || componentType.IsArray
            ? "[" + componentType.Name
            : "[L" + componentType.Name + ";";
        return LoadClass(name);
    }

    // ---------------------------------------------------------------------------------
    // Initialization
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Runs a class's static initializer if it has not run yet, following the ordering rules of
    /// JVMS 5.5: superclasses first, at most once, and recursion from the initializer itself is
    /// allowed rather than deadlocking.
    /// </summary>
    public void EnsureInitialized(JavaThread thread, JavaClass type)
    {
        if (type.IsInitialized) return;

        if (!type.TryBeginInitialization(Thread.CurrentThread, out _)) return;

        Exception? failure = null;
        try
        {
            if (type.Super is { } super) EnsureInitialized(thread, super);

            ApplyStringConstants(type);

            if (type.FindDeclaredMethod("<clinit>", "()V") is { } initializer)
                Interpreter.CallMethod(thread, initializer, []);
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            type.EndInitialization(failure);
        }

        if (failure is not null)
        {
            // A failed initializer surfaces to guest code as ExceptionInInitializerError in a
            // real JVM. CLDC games handle neither, so the original throwable is more useful.
            if (failure is JavaThrowable) throw failure;
            throw new InvalidOperationException(
                $"Static initializer of {type.DisplayName} failed: {failure.Message}", failure);
        }
    }

    // ---------------------------------------------------------------------------------
    // Threads
    // ---------------------------------------------------------------------------------

    /// <summary>Creates a Java thread and registers it with the VM.</summary>
    public JavaThread CreateThread(string name)
    {
        var thread = new JavaThread(this, name);
        lock (_threads)
        {
            _threads.Add(thread);
        }
        return thread;
    }

    /// <summary>Deregisters a thread that has finished running.</summary>
    public void RemoveThread(JavaThread thread)
    {
        lock (_threads)
        {
            _threads.Remove(thread);
        }
    }

    /// <summary>Every live Java thread, used to stop the VM cleanly.</summary>
    public IReadOnlyList<JavaThread> Threads
    {
        get
        {
            lock (_threads)
            {
                return _threads.ToArray();
            }
        }
    }

    /// <summary>Asks every running thread to unwind at the next safe point.</summary>
    public void RequestShutdown()
    {
        ShuttingDown = true;
        foreach (JavaThread thread in Threads)
        {
            thread.AbortRequested = true;
            thread.InterruptRequested = true;
            try
            {
                thread.HostThread?.Interrupt();
            }
            catch (ThreadStateException)
            {
                // The thread finished between the null check and the interrupt; nothing to do.
            }
        }
    }
}

/// <summary>Small helper that keeps object construction expressions readable.</summary>
internal static class ObjectInitializationExtensions
{
    public static T Also<T>(this T value, Action<T> configure)
    {
        configure(value);
        return value;
    }
}
