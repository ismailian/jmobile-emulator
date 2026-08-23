using System.Runtime.CompilerServices;

namespace JMobile.Core.ClassFile;

/// <summary>
/// The five storage shapes a JVM value can take once erased to machine representation.
/// Byte, char, short and boolean all widen to <see cref="Int"/> on the operand stack;
/// they are only narrowed again when written back into a field or array.
/// </summary>
public enum ValueKind : byte
{
    Void = 0,
    Int = 1,
    Long = 2,
    Float = 3,
    Double = 4,
    Reference = 5,
}

/// <summary>
/// The exact declared type of a field or array element. Distinct from <see cref="ValueKind"/>
/// because a byte field must sign-extend on read and truncate on write even though it lives
/// on the operand stack as an int.
/// </summary>
public enum FieldType : byte
{
    Boolean = 0,
    Byte = 1,
    Char = 2,
    Short = 3,
    Int = 4,
    Long = 5,
    Float = 6,
    Double = 7,
    Reference = 8,
}

/// <summary>
/// Parsing and classification helpers for JVM type descriptors (JVMS section 4.3).
/// Every method here is allocation-free unless it must materialise a string.
/// </summary>
public static class Descriptor
{
    /// <summary>Number of operand-stack / local-variable slots a value of this kind occupies.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SlotCount(ValueKind kind) => kind is ValueKind.Long or ValueKind.Double ? 2 : 1;

    /// <summary>Maps the leading character of a descriptor to its erased storage shape.</summary>
    public static ValueKind KindOf(char descriptorHead) => descriptorHead switch
    {
        'B' or 'C' or 'I' or 'S' or 'Z' => ValueKind.Int,
        'J' => ValueKind.Long,
        'F' => ValueKind.Float,
        'D' => ValueKind.Double,
        'L' or '[' => ValueKind.Reference,
        'V' => ValueKind.Void,
        _ => throw new ClassFormatException($"Unknown type descriptor '{descriptorHead}'."),
    };

    /// <summary>Maps the leading character of a descriptor to its exact declared type.</summary>
    public static FieldType FieldTypeOf(char descriptorHead) => descriptorHead switch
    {
        'Z' => FieldType.Boolean,
        'B' => FieldType.Byte,
        'C' => FieldType.Char,
        'S' => FieldType.Short,
        'I' => FieldType.Int,
        'J' => FieldType.Long,
        'F' => FieldType.Float,
        'D' => FieldType.Double,
        'L' or '[' => FieldType.Reference,
        _ => throw new ClassFormatException($"Unknown field descriptor '{descriptorHead}'."),
    };

    public static ValueKind KindOfFieldType(FieldType type) => type switch
    {
        FieldType.Long => ValueKind.Long,
        FieldType.Float => ValueKind.Float,
        FieldType.Double => ValueKind.Double,
        FieldType.Reference => ValueKind.Reference,
        _ => ValueKind.Int,
    };

    /// <summary>Total slots consumed by the parameters of a method descriptor, excluding the receiver.</summary>
    public static int ParameterSlots(string methodDescriptor)
    {
        int slots = 0;
        int i = 1; // skip the opening parenthesis
        while (i < methodDescriptor.Length && methodDescriptor[i] != ')')
        {
            slots += SlotCount(KindOf(methodDescriptor[i]));
            i = SkipType(methodDescriptor, i);
        }
        return slots;
    }

    /// <summary>Number of declared parameters in a method descriptor.</summary>
    public static int ParameterCount(string methodDescriptor)
    {
        int count = 0;
        int i = 1;
        while (i < methodDescriptor.Length && methodDescriptor[i] != ')')
        {
            count++;
            i = SkipType(methodDescriptor, i);
        }
        return count;
    }

    /// <summary>
    /// Produces, for each declared parameter, the slot offset at which it starts relative to
    /// the first parameter slot. Native methods use this table so that authors can address
    /// arguments by parameter index rather than by slot.
    /// </summary>
    public static int[] ParameterSlotOffsets(string methodDescriptor)
    {
        int count = ParameterCount(methodDescriptor);
        if (count == 0) return [];

        var offsets = new int[count];
        int i = 1, index = 0, slot = 0;
        while (i < methodDescriptor.Length && methodDescriptor[i] != ')')
        {
            offsets[index++] = slot;
            slot += SlotCount(KindOf(methodDescriptor[i]));
            i = SkipType(methodDescriptor, i);
        }
        return offsets;
    }

    /// <summary>The erased shape of a method descriptor's return type.</summary>
    public static ValueKind ReturnKind(string methodDescriptor)
    {
        int close = methodDescriptor.LastIndexOf(')');
        if (close < 0 || close + 1 >= methodDescriptor.Length)
            throw new ClassFormatException($"Malformed method descriptor '{methodDescriptor}'.");
        return KindOf(methodDescriptor[close + 1]);
    }

    /// <summary>The return type descriptor of a method descriptor.</summary>
    public static string ReturnType(string methodDescriptor)
    {
        int close = methodDescriptor.LastIndexOf(')');
        return methodDescriptor[(close + 1)..];
    }

    /// <summary>Splits a method descriptor into its individual parameter descriptors.</summary>
    public static string[] ParameterTypes(string methodDescriptor)
    {
        int count = ParameterCount(methodDescriptor);
        if (count == 0) return [];

        var result = new string[count];
        int i = 1, index = 0;
        while (i < methodDescriptor.Length && methodDescriptor[i] != ')')
        {
            int end = SkipType(methodDescriptor, i);
            result[index++] = methodDescriptor[i..end];
            i = end;
        }
        return result;
    }

    /// <summary>Returns the index just past the type descriptor beginning at <paramref name="index"/>.</summary>
    public static int SkipType(string descriptor, int index)
    {
        while (index < descriptor.Length && descriptor[index] == '[') index++;
        if (index >= descriptor.Length) throw new ClassFormatException($"Truncated descriptor '{descriptor}'.");

        if (descriptor[index] == 'L')
        {
            int semi = descriptor.IndexOf(';', index);
            if (semi < 0) throw new ClassFormatException($"Unterminated object descriptor in '{descriptor}'.");
            return semi + 1;
        }
        return index + 1;
    }

    /// <summary>
    /// Converts a field descriptor into the internal class name a class loader understands.
    /// Object descriptors lose their L prefix and trailing semicolon; array descriptors are
    /// already in internal form and are returned unchanged.
    /// </summary>
    public static string ToInternalName(string fieldDescriptor)
    {
        if (fieldDescriptor.Length > 2 && fieldDescriptor[0] == 'L' && fieldDescriptor[^1] == ';')
            return fieldDescriptor[1..^1];
        return fieldDescriptor;
    }

    /// <summary>The inverse of <see cref="ToInternalName"/>.</summary>
    public static string ToFieldDescriptor(string internalName) =>
        internalName.Length > 0 && (internalName[0] == '[' || internalName.Length == 1)
            ? internalName
            : "L" + internalName + ";";

    /// <summary>Renders an internal name in the dotted form Java source and reflection use.</summary>
    public static string ToDisplayName(string internalName)
    {
        if (internalName.Length == 0) return internalName;
        if (internalName[0] != '[') return internalName.Replace('/', '.');

        int dims = 0;
        while (dims < internalName.Length && internalName[dims] == '[') dims++;

        string tail = internalName[dims..];
        string element = tail switch
        {
            "Z" => "boolean",
            "B" => "byte",
            "C" => "char",
            "S" => "short",
            "I" => "int",
            "J" => "long",
            "F" => "float",
            "D" => "double",
            _ => tail.Length > 2 && tail[0] == 'L' ? tail[1..^1].Replace('/', '.') : tail,
        };

        return element + string.Concat(Enumerable.Repeat("[]", dims));
    }
}
