using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;

namespace JMobile.Runtime;

/// <summary>
/// Helpers shared by every native method body in the runtime library.
/// </summary>
/// <remarks>
/// Native methods are terse by design, so the recurring shapes — turn a host string into a Java
/// one, reject a null argument the way Java would, copy between arrays — live here rather than
/// being spelled out several hundred times.
/// </remarks>
public static class RuntimeSupport
{
    /// <summary>Wraps a host string as a Java string, mapping null to Java <c>null</c>.</summary>
    public static JavaValue Str(JavaThread thread, string? value) =>
        value is null ? JavaValue.Null : JavaValue.Ref(thread.Vm.NewString(value));

    /// <summary>Reads a string argument, throwing <c>NullPointerException</c> if it is null.</summary>
    public static string RequireString(JavaThread thread, string? value, string parameterName) =>
        value ?? throw thread.Vm.NewNullPointerException(thread, parameterName + " is null");

    /// <summary>Reads a reference argument, throwing <c>NullPointerException</c> if it is null.</summary>
    public static T RequireRef<T>(JavaThread thread, T? value, string parameterName) where T : JavaObject =>
        value ?? throw thread.Vm.NewNullPointerException(thread, parameterName + " is null");

    /// <summary>Validates a region against a length the way the CLDC library does.</summary>
    public static void CheckRegion(JavaThread thread, int offset, int length, int available)
    {
        if (offset < 0 || length < 0 || offset > available - length)
        {
            throw thread.Vm.NewIndexOutOfBoundsException(
                thread, $"offset {offset}, length {length}, available {available}");
        }
    }

    /// <summary>
    /// Implements <c>System.arraycopy</c> for every array shape.
    /// </summary>
    /// <remarks>
    /// Once the two arrays are known to be the same concrete type, the copy is a single
    /// <see cref="Array.Copy"/> over the underlying CLR array, which is memmove. Games lean on
    /// this method hard for tile maps and sprite buffers, so it must not degrade into a loop.
    /// </remarks>
    public static void ArrayCopy(
        JavaThread thread,
        JavaObject? source,
        int sourceIndex,
        JavaObject? destination,
        int destinationIndex,
        int length)
    {
        VirtualMachine vm = thread.Vm;

        if (source is null || destination is null)
            throw vm.NewNullPointerException(thread, "arraycopy with a null array");

        if (source is not JavaArray sourceArray || destination is not JavaArray destinationArray)
            throw vm.NewArrayStoreException(thread, (source as JavaArray)?.Class ?? source.Class);

        if (length < 0 || sourceIndex < 0 || destinationIndex < 0 ||
            sourceIndex + length > sourceArray.Length ||
            destinationIndex + length > destinationArray.Length)
        {
            throw vm.NewArrayIndexOutOfBoundsException(thread, sourceIndex, sourceArray.Length);
        }

        if (length == 0) return;

        switch (sourceArray)
        {
            case JavaArray<sbyte> from when destinationArray is JavaArray<sbyte> to:
                Array.Copy(from.Items, sourceIndex, to.Items, destinationIndex, length);
                return;

            case JavaArray<char> from when destinationArray is JavaArray<char> to:
                Array.Copy(from.Items, sourceIndex, to.Items, destinationIndex, length);
                return;

            case JavaArray<short> from when destinationArray is JavaArray<short> to:
                Array.Copy(from.Items, sourceIndex, to.Items, destinationIndex, length);
                return;

            case JavaArray<int> from when destinationArray is JavaArray<int> to:
                Array.Copy(from.Items, sourceIndex, to.Items, destinationIndex, length);
                return;

            case JavaArray<long> from when destinationArray is JavaArray<long> to:
                Array.Copy(from.Items, sourceIndex, to.Items, destinationIndex, length);
                return;

            case JavaArray<float> from when destinationArray is JavaArray<float> to:
                Array.Copy(from.Items, sourceIndex, to.Items, destinationIndex, length);
                return;

            case JavaArray<double> from when destinationArray is JavaArray<double> to:
                Array.Copy(from.Items, sourceIndex, to.Items, destinationIndex, length);
                return;

            case JavaArray<JavaObject?> from when destinationArray is JavaArray<JavaObject?> to:
            {
                JavaClass? component = destinationArray.Class.ComponentType;

                // Reference arrays are covariant, so unless the destination is an Object[] each
                // element has to be checked. Skipping the check when it cannot fail keeps the
                // common Object[] case at memmove speed.
                if (component is null || component.Name == WellKnown.Object ||
                    ReferenceEquals(sourceArray.Class, destinationArray.Class))
                {
                    Array.Copy(from.Items, sourceIndex, to.Items, destinationIndex, length);
                    return;
                }

                for (int i = 0; i < length; i++)
                {
                    JavaObject? element = from.Items[sourceIndex + i];
                    if (element is not null && !component.IsAssignableFrom(element.Class))
                        throw vm.NewArrayStoreException(thread, element.Class);
                    to.Items[destinationIndex + i] = element;
                }
                return;
            }

            default:
                throw vm.NewArrayStoreException(thread, sourceArray.Class);
        }
    }

    /// <summary>Copies Java bytes out to host bytes, reinterpreting the sign.</summary>
    public static byte[] ToHostBytes(JavaArray<sbyte>? array, int offset, int length)
    {
        if (array is null) return [];

        var result = new byte[length];
        for (int i = 0; i < length; i++) result[i] = (byte)array.Items[offset + i];
        return result;
    }

    /// <summary>Copies host bytes into an existing Java byte array.</summary>
    public static void FromHostBytes(ReadOnlySpan<byte> source, JavaArray<sbyte> destination, int offset)
    {
        for (int i = 0; i < source.Length; i++) destination.Items[offset + i] = (sbyte)source[i];
    }

    /// <summary>
    /// Decodes bytes using the encodings CLDC devices actually shipped with.
    /// </summary>
    /// <remarks>
    /// ISO-8859-1 is the CLDC default and, importantly, is what a great deal of game data was
    /// authored in. Treating unknown encodings as ISO-8859-1 rather than failing keeps games
    /// running that name an encoding the desktop has never heard of.
    /// </remarks>
    public static string DecodeBytes(ReadOnlySpan<byte> data, string? encoding)
    {
        if (encoding is null) return Latin1(data);

        return encoding.ToUpperInvariant() switch
        {
            "UTF-8" or "UTF8" => System.Text.Encoding.UTF8.GetString(data),
            "US-ASCII" or "ASCII" => System.Text.Encoding.ASCII.GetString(data),
            "UTF-16BE" => System.Text.Encoding.BigEndianUnicode.GetString(data),
            "UTF-16LE" or "UTF-16" => System.Text.Encoding.Unicode.GetString(data),
            _ => Latin1(data),
        };
    }

    /// <summary>Encodes a string using the same set of encodings.</summary>
    public static byte[] EncodeString(string value, string? encoding)
    {
        if (encoding is null) return Latin1Bytes(value);

        return encoding.ToUpperInvariant() switch
        {
            "UTF-8" or "UTF8" => System.Text.Encoding.UTF8.GetBytes(value),
            "US-ASCII" or "ASCII" => System.Text.Encoding.ASCII.GetBytes(value),
            "UTF-16BE" => System.Text.Encoding.BigEndianUnicode.GetBytes(value),
            "UTF-16LE" or "UTF-16" => System.Text.Encoding.Unicode.GetBytes(value),
            _ => Latin1Bytes(value),
        };
    }

    private static string Latin1(ReadOnlySpan<byte> data)
    {
        Span<char> characters = data.Length <= 512 ? stackalloc char[data.Length] : new char[data.Length];
        for (int i = 0; i < data.Length; i++) characters[i] = (char)data[i];
        return new string(characters);
    }

    private static byte[] Latin1Bytes(string value)
    {
        var result = new byte[value.Length];
        for (int i = 0; i < value.Length; i++) result[i] = (byte)value[i];
        return result;
    }
}
