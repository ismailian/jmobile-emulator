using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JMobile.Core.ClassFile;
using JMobile.Core.Execution;
using JMobile.Core.Model;

namespace JMobile.Core;

/// <summary>
/// Allocation, string handling, exception construction and the host-to-Java call boundary.
/// </summary>
public sealed partial class VirtualMachine
{
    // ---------------------------------------------------------------------------------
    // Allocation
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Allocates an instance with its fields zeroed, exactly as <c>new</c> does.
    /// </summary>
    /// <remarks>
    /// Classes whose instances carry host state, such as strings and images, supply an
    /// allocator through the class builder. Everything else gets a plain object, so the
    /// interpreter's <c>new</c> handler does not need to know which classes are special.
    /// </remarks>
    public JavaObject NewInstance(JavaClass type)
    {
        if (type.IsAbstract || type.IsInterface)
            throw new InvalidOperationException($"Cannot instantiate {type.DisplayName}.");

        return type.Allocator is { } allocator
            ? allocator(type)
            : new JavaObject(type, type.InstanceFieldCount);
    }

    /// <summary>Allocates a one-dimensional array of the given array class.</summary>
    public JavaArray NewArray(JavaClass arrayClass, int length) =>
        JavaArrayFactory.Create(arrayClass, length);

    /// <summary>
    /// Allocates a multi-dimensional array, filling every dimension but the last.
    /// </summary>
    public JavaArray NewMultiArray(JavaClass arrayClass, ReadOnlySpan<int> lengths)
    {
        int length = lengths[0];
        JavaArray array = JavaArrayFactory.Create(arrayClass, length);

        if (lengths.Length > 1 && array is JavaArray<JavaObject?> references)
        {
            JavaClass componentClass = arrayClass.ComponentType!;
            ReadOnlySpan<int> remaining = lengths[1..];
            for (int i = 0; i < length; i++)
                references.Items[i] = NewMultiArray(componentClass, remaining);
        }

        return array;
    }

    /// <summary>Wraps host bytes as a Java <c>byte[]</c> without copying twice.</summary>
    public JavaArray<sbyte> NewByteArray(ReadOnlySpan<byte> data)
    {
        var array = new JavaArray<sbyte>(LoadClass(WellKnown.ByteArray), data.Length);
        // A Java byte is signed while a host byte is not; the bit patterns are identical, so
        // reinterpreting the span is both correct and free.
        data.CopyTo(MemoryMarshal.AsBytes(array.Items.AsSpan()));
        return array;
    }

    /// <summary>Creates an empty Java <c>byte[]</c> of the requested size.</summary>
    public JavaArray<sbyte> NewByteArray(int length) =>
        new(LoadClass(WellKnown.ByteArray), length);

    public JavaArray<int> NewIntArray(int length) => new(LoadClass(WellKnown.IntArray), length);

    public JavaArray<char> NewCharArray(int length) => new(LoadClass(WellKnown.CharArray), length);

    public JavaArray<char> NewCharArray(ReadOnlySpan<char> data)
    {
        var array = new JavaArray<char>(LoadClass(WellKnown.CharArray), data.Length);
        data.CopyTo(array.Items);
        return array;
    }

    /// <summary>Creates a Java <c>Object[]</c>, the backing store for the collection classes.</summary>
    public JavaArray<JavaObject?> NewObjectArray(int length) =>
        new(LoadClass(WellKnown.ObjectArray), length);

    /// <summary>Creates a Java <c>String[]</c> from host strings.</summary>
    public JavaArray<JavaObject?> NewStringArray(IReadOnlyList<string> values)
    {
        var array = new JavaArray<JavaObject?>(LoadClass(WellKnown.StringArray), values.Count);
        for (int i = 0; i < values.Count; i++) array.Items[i] = NewString(values[i]);
        return array;
    }

    // ---------------------------------------------------------------------------------
    // Strings
    // ---------------------------------------------------------------------------------

    /// <summary>Creates a fresh, uninterned Java string.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JavaString NewString(string value) => new(StringClass, value);

    /// <summary>Creates a Java string, or Java <c>null</c> when the host value is null.</summary>
    public JavaObject? NewStringOrNull(string? value) => value is null ? null : NewString(value);

    /// <summary>
    /// Returns the canonical Java string for a host string, matching <c>String.intern</c>.
    /// </summary>
    /// <remarks>
    /// Every string literal in the guest program goes through here exactly once, at method
    /// compilation time, so the interpreter's <c>ldc</c> of a string is a plain reference push.
    /// </remarks>
    public JavaString Intern(string value)
    {
        lock (_internGate)
        {
            if (_internedStrings.TryGetValue(value, out JavaString? existing)) return existing;

            var created = new JavaString(StringClass, value);
            _internedStrings[value] = created;
            return created;
        }
    }

    /// <summary>Reads a Java string argument as a host string, mapping Java <c>null</c> to null.</summary>
    public static string? ToHostString(JavaObject? value) => (value as JavaString)?.Value;

    // ---------------------------------------------------------------------------------
    // Class mirrors
    // ---------------------------------------------------------------------------------

    /// <summary>Returns the <c>java.lang.Class</c> instance for a type, creating it once.</summary>
    public JavaObject GetClassMirror(JavaClass type)
    {
        if (type.Mirror is { } existing) return existing;

        lock (_loadGate)
        {
            if (type.Mirror is { } raced) return raced;

            var mirror = new JavaClassMirror(ClassClass, type);
            type.Mirror = mirror;
            return mirror;
        }
    }

    // ---------------------------------------------------------------------------------
    // Resources
    // ---------------------------------------------------------------------------------

    /// <summary>Reads a suite resource, as <c>Class.getResourceAsStream</c> does.</summary>
    public byte[]? GetResource(string path) =>
        ClassSource.TryGetResource(path, out byte[] bytes) ? bytes : null;

    // ---------------------------------------------------------------------------------
    // Exceptions
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Builds a Java throwable and wraps it for propagation through the host stack.
    /// </summary>
    /// <remarks>
    /// Returning the exception rather than throwing it lets call sites write
    /// <c>throw vm.NewException(...)</c>, which keeps control flow visible to the reader and to
    /// the compiler's definite-assignment analysis.
    /// </remarks>
    public JavaThrowable NewException(JavaThread thread, string className, string? message = null)
    {
        JavaClass? type = TryLoadClass(className);

        // If even the exception class is missing something is badly wrong with the runtime
        // library, and reporting that plainly beats recursing while trying to report it.
        if (type is null)
        {
            return new JavaThrowable(
                new JavaObject(ObjectClass, 0),
                $"{Descriptor.ToDisplayName(className)}: {message} (exception class not found)");
        }

        JavaObject throwable = NewInstance(type);
        if (message is not null && ThrowableMessageSlot < throwable.Fields.Length)
            throwable.SetField(ThrowableMessageSlot, JavaValue.Ref(NewString(message)));

        string[] trace = thread.CaptureStackTrace();
        if (throwable is JavaThrowableInstance instance) instance.StackTrace = trace;

        string description = message is null
            ? type.DisplayName
            : $"{type.DisplayName}: {message}";

        return new JavaThrowable(throwable, description) { JavaStackTrace = trace };
    }

    /// <summary>Wraps an existing Java throwable object for propagation.</summary>
    public JavaThrowable Rethrow(JavaThread thread, JavaObject throwable)
    {
        string? message = null;
        if (ThrowableMessageSlot < throwable.Fields.Length)
            message = ToHostString(throwable.GetField(ThrowableMessageSlot).Reference as JavaObject);

        string description = message is null
            ? throwable.Class.DisplayName
            : $"{throwable.Class.DisplayName}: {message}";

        string[] trace = throwable is JavaThrowableInstance instance && instance.StackTrace.Length > 0
            ? instance.StackTrace
            : thread.CaptureStackTrace();

        return new JavaThrowable(throwable, description) { JavaStackTrace = trace };
    }

    public JavaThrowable NewNullPointerException(JavaThread thread, string? message = null) =>
        NewException(thread, WellKnown.NullPointerException, message);

    public JavaThrowable NewArithmeticException(JavaThread thread, string message) =>
        NewException(thread, WellKnown.ArithmeticException, message);

    public JavaThrowable NewClassCastException(JavaThread thread, JavaClass from, JavaClass to) =>
        NewException(thread, WellKnown.ClassCastException,
            $"{from.DisplayName} cannot be cast to {to.DisplayName}");

    public JavaThrowable NewArrayIndexOutOfBoundsException(JavaThread thread, int index, int length) =>
        NewException(thread, WellKnown.ArrayIndexOutOfBoundsException,
            $"Index {index} out of bounds for length {length}");

    public JavaThrowable NewNegativeArraySizeException(JavaThread thread, int size) =>
        NewException(thread, WellKnown.NegativeArraySizeException, size.ToString());

    public JavaThrowable NewArrayStoreException(JavaThread thread, JavaClass type) =>
        NewException(thread, WellKnown.ArrayStoreException, type.DisplayName);

    public JavaThrowable NewIllegalArgumentException(JavaThread thread, string message) =>
        NewException(thread, WellKnown.IllegalArgumentException, message);

    public JavaThrowable NewIllegalStateException(JavaThread thread, string message) =>
        NewException(thread, WellKnown.IllegalStateException, message);

    public JavaThrowable NewIndexOutOfBoundsException(JavaThread thread, string message) =>
        NewException(thread, WellKnown.IndexOutOfBoundsException, message);

    public JavaThrowable NewStringIndexOutOfBoundsException(JavaThread thread, int index) =>
        NewException(thread, WellKnown.StringIndexOutOfBoundsException, $"String index out of range: {index}");

    public JavaThrowable NewNumberFormatException(JavaThread thread, string message) =>
        NewException(thread, WellKnown.NumberFormatException, message);

    public JavaThrowable NewIOException(JavaThread thread, string message) =>
        NewException(thread, WellKnown.IOException, message);

    public JavaThrowable NewNoSuchElementException(JavaThread thread) =>
        NewException(thread, WellKnown.NoSuchElementException, null);

    public JavaThrowable NewInterruptedException(JavaThread thread) =>
        NewException(thread, WellKnown.InterruptedException, null);

    public JavaThrowable NewStackOverflowError(JavaThread thread) =>
        NewException(thread, WellKnown.StackOverflowError, null);

    public JavaThrowable NewOutOfMemoryError(JavaThread thread) =>
        NewException(thread, WellKnown.OutOfMemoryError, null);

    public JavaThrowable NewNoClassDefFoundError(JavaThread thread, string className) =>
        NewException(thread, WellKnown.NoClassDefFoundError, Descriptor.ToDisplayName(className));

    public JavaThrowable NewNoSuchMethodError(JavaThread thread, string description) =>
        NewException(thread, WellKnown.NoSuchMethodError, description);

    public JavaThrowable NewNoSuchFieldError(JavaThread thread, string description) =>
        NewException(thread, WellKnown.NoSuchFieldError, description);

    public JavaThrowable NewAbstractMethodError(JavaThread thread, string description) =>
        NewException(thread, WellKnown.AbstractMethodError, description);

    public JavaThrowable NewIllegalMonitorStateException(JavaThread thread) =>
        NewException(thread, WellKnown.IllegalMonitorStateException, null);

    // ---------------------------------------------------------------------------------
    // Host to Java calls
    // ---------------------------------------------------------------------------------

    /// <summary>Invokes a static method from host code.</summary>
    public JavaValue CallStatic(JavaThread thread, JavaMethod method, params JavaValue[] arguments)
    {
        EnsureInitialized(thread, method.DeclaringClass);
        return Interpreter.CallMethod(thread, method, arguments);
    }

    /// <summary>Invokes an instance method virtually from host code.</summary>
    public JavaValue CallVirtual(
        JavaThread thread,
        JavaObject receiver,
        string name,
        string descriptor,
        params JavaValue[] arguments)
    {
        JavaMethod method = receiver.Class.FindMethod(name, descriptor)
            ?? throw new MissingMethodException(
                $"{receiver.Class.DisplayName} has no method {name}{descriptor}");

        var full = new JavaValue[arguments.Length + 1];
        full[0] = JavaValue.Ref(receiver);
        arguments.CopyTo(full, 1);

        return Interpreter.CallMethod(thread, method, full);
    }

    /// <summary>Invokes an instance method non-virtually, as <c>invokespecial</c> does.</summary>
    public JavaValue CallSpecial(
        JavaThread thread,
        JavaObject receiver,
        JavaMethod method,
        params JavaValue[] arguments)
    {
        var full = new JavaValue[arguments.Length + 1];
        full[0] = JavaValue.Ref(receiver);
        arguments.CopyTo(full, 1);

        return Interpreter.CallMethod(thread, method, full);
    }

    /// <summary>Allocates an object and runs the matching constructor.</summary>
    public JavaObject Construct(
        JavaThread thread,
        JavaClass type,
        string constructorDescriptor,
        params JavaValue[] arguments)
    {
        EnsureInitialized(thread, type);

        JavaObject instance = NewInstance(type);
        JavaMethod constructor = type.FindMethod("<init>", constructorDescriptor)
            ?? throw new MissingMethodException(
                $"{type.DisplayName} has no constructor {constructorDescriptor}");

        CallSpecial(thread, instance, constructor, arguments);
        return instance;
    }
}
