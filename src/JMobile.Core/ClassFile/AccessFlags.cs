namespace JMobile.Core.ClassFile;

/// <summary>Access and property modifiers as encoded in the class file (JVMS sections 4.1, 4.5, 4.6).</summary>
[Flags]
public enum AccessFlags : ushort
{
    None = 0x0000,
    Public = 0x0001,
    Private = 0x0002,
    Protected = 0x0004,
    Static = 0x0008,
    Final = 0x0010,

    /// <summary>On a class this is ACC_SUPER; on a method the same bit means ACC_SYNCHRONIZED.</summary>
    SuperOrSynchronized = 0x0020,

    Volatile = 0x0040,
    Bridge = 0x0040,
    Transient = 0x0080,
    VarArgs = 0x0080,
    Native = 0x0100,
    Interface = 0x0200,
    Abstract = 0x0400,
    Strict = 0x0800,
    Synthetic = 0x1000,
    Annotation = 0x2000,
    Enum = 0x4000,
}

public static class AccessFlagsExtensions
{
    public static bool Has(this AccessFlags flags, AccessFlags flag) => (flags & flag) != 0;

    public static bool IsStatic(this AccessFlags flags) => (flags & AccessFlags.Static) != 0;

    public static bool IsAbstract(this AccessFlags flags) => (flags & AccessFlags.Abstract) != 0;

    public static bool IsNative(this AccessFlags flags) => (flags & AccessFlags.Native) != 0;

    public static bool IsInterface(this AccessFlags flags) => (flags & AccessFlags.Interface) != 0;

    public static bool IsFinal(this AccessFlags flags) => (flags & AccessFlags.Final) != 0;

    public static bool IsPrivate(this AccessFlags flags) => (flags & AccessFlags.Private) != 0;

    public static bool IsSynchronized(this AccessFlags flags) => (flags & AccessFlags.SuperOrSynchronized) != 0;
}
