using JMobile.Core.ClassFile;

namespace JMobile.Core.Model;

/// <summary>A linked field: a declaration plus the storage slot the linker assigned it.</summary>
public sealed class JavaField
{
    public JavaField(JavaClass declaringClass, string name, string descriptor, AccessFlags flags)
    {
        DeclaringClass = declaringClass;
        Name = name;
        Descriptor = descriptor;
        Flags = flags;
        Type = Descriptor.Length > 0
            ? ClassFile.Descriptor.FieldTypeOf(descriptor[0])
            : FieldType.Reference;
        Kind = ClassFile.Descriptor.KindOfFieldType(Type);
    }

    public JavaClass DeclaringClass { get; }

    public string Name { get; }

    public string Descriptor { get; }

    public AccessFlags Flags { get; }

    public FieldType Type { get; }

    public ValueKind Kind { get; }

    public bool IsStatic => Flags.IsStatic();

    /// <summary>
    /// Index into <see cref="JavaObject.Fields"/> for instance fields, or into
    /// <see cref="JavaClass.StaticFields"/> for static ones. Assigned during linking.
    /// </summary>
    public int SlotIndex { get; internal set; } = -1;

    /// <summary>Constant pool index of a ConstantValue attribute, or 0 when the field has none.</summary>
    internal int ConstantValueIndex { get; init; }

    public override string ToString() => $"{DeclaringClass.Name}.{Name}:{Descriptor}";
}
