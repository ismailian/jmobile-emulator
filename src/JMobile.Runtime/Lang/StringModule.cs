using System.Text;
using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Core.Native;

namespace JMobile.Runtime.Lang;

/// <summary>A <c>java.lang.StringBuffer</c> backed by a CLR <see cref="StringBuilder"/>.</summary>
public sealed class JavaStringBuffer(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public StringBuilder Builder { get; } = new();
}

/// <summary>
/// <c>java.lang.String</c> and <c>java.lang.StringBuffer</c>.
/// </summary>
/// <remarks>
/// These two classes get more traffic than anything else in the library. Every menu, every score
/// readout and every save file goes through them, and games written for phones with no string
/// pooling tend to build strings in loops. Backing them with CLR types means concatenation,
/// comparison and searching all run at CLR speed instead of as interpreted char-array loops.
/// </remarks>
public sealed class StringModule : IRuntimeModule
{
    public string Name => "java.lang.String";

    public void Register(RuntimeRegistry registry)
    {
        RegisterString(registry);
        RegisterStringBuffer(registry, WellKnown.StringBuffer);

        // StringBuilder is not part of CLDC, but every compiler newer than the ones these games
        // were built with emits it for string concatenation. Providing it costs one extra class
        // and means a suite built with a modern toolchain still runs.
        RegisterStringBuffer(registry, "java/lang/StringBuilder");
    }

    // ---------------------------------------------------------------------------------
    // String
    // ---------------------------------------------------------------------------------

    private static void RegisterString(RuntimeRegistry registry)
    {
        registry.Define(WellKnown.String, builder => builder
            .AsFinal()
            .AllocatesWith(static type => new JavaString(type, string.Empty))

            // -- constructors -------------------------------------------------------------
            .Constructor("()V", static (_, args) => Assign(args.This, string.Empty))
            .Constructor("(Ljava/lang/String;)V",
                static (_, args) => Assign(args.This, args.String(0) ?? string.Empty))
            .Constructor("([C)V", static (thread, args) =>
            {
                char[] source = args.Array<char>(0)
                                ?? throw thread.Vm.NewNullPointerException(thread, "chars");
                return Assign(args.This, new string(source));
            })
            .Constructor("([CII)V", static (thread, args) =>
            {
                char[] source = args.Array<char>(0)
                                ?? throw thread.Vm.NewNullPointerException(thread, "chars");
                RuntimeSupport.CheckRegion(thread, args.Int(1), args.Int(2), source.Length);
                return Assign(args.This, new string(source, args.Int(1), args.Int(2)));
            })
            .Constructor("([B)V", static (thread, args) =>
            {
                var source = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "bytes");
                return Assign(args.This, Decode(source, 0, source.Length, null));
            })
            .Constructor("([BII)V", static (thread, args) =>
            {
                var source = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "bytes");
                RuntimeSupport.CheckRegion(thread, args.Int(1), args.Int(2), source.Length);
                return Assign(args.This, Decode(source, args.Int(1), args.Int(2), null));
            })
            .Constructor("([BLjava/lang/String;)V", static (thread, args) =>
            {
                var source = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "bytes");
                return Assign(args.This, Decode(source, 0, source.Length, args.String(1)));
            })
            .Constructor("([BIILjava/lang/String;)V", static (thread, args) =>
            {
                var source = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "bytes");
                RuntimeSupport.CheckRegion(thread, args.Int(1), args.Int(2), source.Length);
                return Assign(args.This, Decode(source, args.Int(1), args.Int(2), args.String(3)));
            })
            .Constructor("(Ljava/lang/StringBuffer;)V", static (_, args) =>
                Assign(args.This, (args.Object(0) as JavaStringBuffer)?.Builder.ToString() ?? string.Empty))

            // -- inspection ---------------------------------------------------------------
            .Method("length", "()I", static (_, args) => JavaValue.Int(Self(args.This).Length))
            .Method("charAt", "(I)C", static (thread, args) =>
            {
                string value = Self(args.This);
                int index = args.Int(0);
                if ((uint)index >= (uint)value.Length)
                    throw thread.Vm.NewStringIndexOutOfBoundsException(thread, index);
                return JavaValue.Int(value[index]);
            })
            .Method("isEmpty", "()Z", static (_, args) => JavaValue.Bool(Self(args.This).Length == 0))
            .Method("hashCode", "()I",
                static (_, args) => JavaValue.Int(JavaText.StringHashCode(Self(args.This))))
            .Method("equals", "(Ljava/lang/Object;)Z", static (_, args) =>
                JavaValue.Bool(args.Object(0) is JavaString other &&
                               string.Equals(Self(args.This), other.Value, StringComparison.Ordinal)))
            .Method("equalsIgnoreCase", "(Ljava/lang/String;)Z", static (_, args) =>
                JavaValue.Bool(args.String(0) is { } other &&
                               string.Equals(Self(args.This), other, StringComparison.OrdinalIgnoreCase)))
            .Method("compareTo", "(Ljava/lang/String;)I", static (thread, args) =>
            {
                string other = RuntimeSupport.RequireString(thread, args.String(0), "anotherString");
                return JavaValue.Int(CompareJavaOrdinal(Self(args.This), other));
            })
            .Method("startsWith", "(Ljava/lang/String;)Z", static (_, args) =>
                JavaValue.Bool(args.String(0) is { } prefix &&
                               Self(args.This).StartsWith(prefix, StringComparison.Ordinal)))
            .Method("startsWith", "(Ljava/lang/String;I)Z", static (_, args) =>
            {
                string value = Self(args.This);
                string? prefix = args.String(0);
                int offset = args.Int(1);
                if (prefix is null || offset < 0 || offset + prefix.Length > value.Length)
                    return JavaValue.Bool(false);
                return JavaValue.Bool(string.CompareOrdinal(value, offset, prefix, 0, prefix.Length) == 0);
            })
            .Method("endsWith", "(Ljava/lang/String;)Z", static (_, args) =>
                JavaValue.Bool(args.String(0) is { } suffix &&
                               Self(args.This).EndsWith(suffix, StringComparison.Ordinal)))

            // -- searching ----------------------------------------------------------------
            .Method("indexOf", "(I)I",
                static (_, args) => JavaValue.Int(IndexOfChar(Self(args.This), args.Int(0), 0)))
            .Method("indexOf", "(II)I",
                static (_, args) => JavaValue.Int(IndexOfChar(Self(args.This), args.Int(0), args.Int(1))))
            .Method("indexOf", "(Ljava/lang/String;)I", static (thread, args) =>
                JavaValue.Int(Self(args.This).IndexOf(
                    RuntimeSupport.RequireString(thread, args.String(0), "str"), StringComparison.Ordinal)))
            .Method("indexOf", "(Ljava/lang/String;I)I", static (thread, args) =>
            {
                string value = Self(args.This);
                string needle = RuntimeSupport.RequireString(thread, args.String(0), "str");
                int from = Math.Clamp(args.Int(1), 0, value.Length);
                return JavaValue.Int(value.IndexOf(needle, from, StringComparison.Ordinal));
            })
            .Method("lastIndexOf", "(I)I", static (_, args) =>
            {
                string value = Self(args.This);
                return JavaValue.Int(LastIndexOfChar(value, args.Int(0), value.Length - 1));
            })
            .Method("lastIndexOf", "(II)I", static (_, args) =>
                JavaValue.Int(LastIndexOfChar(Self(args.This), args.Int(0), args.Int(1))))
            .Method("lastIndexOf", "(Ljava/lang/String;)I", static (thread, args) =>
                JavaValue.Int(Self(args.This).LastIndexOf(
                    RuntimeSupport.RequireString(thread, args.String(0), "str"), StringComparison.Ordinal)))

            // -- derivation ---------------------------------------------------------------
            .Method("substring", "(I)Ljava/lang/String;", static (thread, args) =>
            {
                string value = Self(args.This);
                int start = args.Int(0);
                if (start < 0 || start > value.Length)
                    throw thread.Vm.NewStringIndexOutOfBoundsException(thread, start);
                return RuntimeSupport.Str(thread, value[start..]);
            })
            .Method("substring", "(II)Ljava/lang/String;", static (thread, args) =>
            {
                string value = Self(args.This);
                int start = args.Int(0);
                int end = args.Int(1);
                if (start < 0 || end > value.Length || start > end)
                    throw thread.Vm.NewStringIndexOutOfBoundsException(thread, start < 0 ? start : end);
                return RuntimeSupport.Str(thread, value[start..end]);
            })
            .Method("concat", "(Ljava/lang/String;)Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread,
                    Self(args.This) + RuntimeSupport.RequireString(thread, args.String(0), "str")))
            .Method("replace", "(CC)Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, Self(args.This).Replace(args.Char(0), args.Char(1))))
            .Method("trim", "()Ljava/lang/String;", static (thread, args) =>
            {
                // Java trims every character at or below U+0020, not the Unicode whitespace set.
                string value = Self(args.This);
                int start = 0, end = value.Length;
                while (start < end && value[start] <= ' ') start++;
                while (end > start && value[end - 1] <= ' ') end--;
                return RuntimeSupport.Str(thread, value[start..end]);
            })
            .Method("toLowerCase", "()Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, Self(args.This).ToLowerInvariant()))
            .Method("toUpperCase", "()Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, Self(args.This).ToUpperInvariant()))
            .Method("toString", "()Ljava/lang/String;", static (_, args) => JavaValue.Ref(args.This))
            .Method("intern", "()Ljava/lang/String;",
                static (thread, args) => JavaValue.Ref(thread.Vm.Intern(Self(args.This))))

            // -- conversion ---------------------------------------------------------------
            .Method("toCharArray", "()[C",
                static (thread, args) => JavaValue.Ref(thread.Vm.NewCharArray(Self(args.This))))
            .Method("getChars", "(II[CI)V", static (thread, args) =>
            {
                string value = Self(args.This);
                int start = args.Int(0), end = args.Int(1), destinationStart = args.Int(3);
                char[] destination = args.Array<char>(2)
                                     ?? throw thread.Vm.NewNullPointerException(thread, "dst");

                if (start < 0 || end > value.Length || start > end ||
                    destinationStart < 0 || destinationStart + (end - start) > destination.Length)
                {
                    throw thread.Vm.NewIndexOutOfBoundsException(thread, "getChars out of range");
                }

                value.AsSpan(start, end - start).CopyTo(destination.AsSpan(destinationStart));
                return JavaValue.Void;
            })
            .Method("getBytes", "()[B", static (thread, args) =>
                JavaValue.Ref(thread.Vm.NewByteArray(RuntimeSupport.EncodeString(Self(args.This), null))))
            .Method("getBytes", "(Ljava/lang/String;)[B", static (thread, args) =>
                JavaValue.Ref(thread.Vm.NewByteArray(
                    RuntimeSupport.EncodeString(Self(args.This), args.String(0)))))

            // -- factories ----------------------------------------------------------------
            .StaticMethod("valueOf", "(I)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Int(0).ToString()))
            .StaticMethod("valueOf", "(J)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Long(0).ToString()))
            .StaticMethod("valueOf", "(Z)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Bool(0) ? "true" : "false"))
            .StaticMethod("valueOf", "(C)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Char(0).ToString()))
            .StaticMethod("valueOf", "(F)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, JavaText.FloatToString(args.Float(0))))
            .StaticMethod("valueOf", "(D)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, JavaText.DoubleToString(args.Double(0))))
            .StaticMethod("valueOf", "([C)Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, new string(args.Array<char>(0) ?? [])))
            .StaticMethod("valueOf", "([CII)Ljava/lang/String;", static (thread, args) =>
            {
                char[] source = args.Array<char>(0) ?? [];
                RuntimeSupport.CheckRegion(thread, args.Int(1), args.Int(2), source.Length);
                return RuntimeSupport.Str(thread, new string(source, args.Int(1), args.Int(2)));
            })
            .StaticMethod("valueOf", "(Ljava/lang/Object;)Ljava/lang/String;", static (thread, args) =>
            {
                JavaObject? target = args.Object(0);
                if (target is null) return RuntimeSupport.Str(thread, "null");
                if (target is JavaString direct) return JavaValue.Ref(direct);

                // Anything else goes through its own toString, which may be guest bytecode.
                return thread.Vm.CallVirtual(thread, target, "toString", "()Ljava/lang/String;");
            }));
    }

    private static string Self(JavaObject receiver) => ((JavaString)receiver).Value;

    private static JavaValue Assign(JavaObject receiver, string value)
    {
        ((JavaString)receiver).Value = value;
        return JavaValue.Void;
    }

    private static string Decode(JavaArray<sbyte> source, int offset, int length, string? encoding)
    {
        Span<byte> bytes = length <= 512 ? stackalloc byte[length] : new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)source.Items[offset + i];
        return RuntimeSupport.DecodeBytes(bytes, encoding);
    }

    /// <summary>
    /// Java compares strings by UTF-16 code unit difference and returns that difference, not a
    /// normalised -1/0/1, and callers occasionally use the magnitude.
    /// </summary>
    private static int CompareJavaOrdinal(string left, string right)
    {
        int shared = Math.Min(left.Length, right.Length);
        for (int i = 0; i < shared; i++)
        {
            int difference = left[i] - right[i];
            if (difference != 0) return difference;
        }
        return left.Length - right.Length;
    }

    private static int IndexOfChar(string value, int character, int from)
    {
        if (from < 0) from = 0;
        if (from >= value.Length || character is < 0 or > char.MaxValue) return -1;
        return value.IndexOf((char)character, from);
    }

    private static int LastIndexOfChar(string value, int character, int from)
    {
        if (value.Length == 0 || character is < 0 or > char.MaxValue) return -1;
        if (from >= value.Length) from = value.Length - 1;
        if (from < 0) return -1;
        return value.LastIndexOf((char)character, from);
    }

    // ---------------------------------------------------------------------------------
    // StringBuffer
    // ---------------------------------------------------------------------------------

    private static void RegisterStringBuffer(RuntimeRegistry registry, string internalName)
    {
        string self = "L" + internalName + ";";
        registry.Define(internalName, builder => builder
            .AsFinal()
            .AllocatesWith(static type => new JavaStringBuffer(type, type.InstanceFieldCount))
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Constructor("(I)V", static (_, args) =>
            {
                args.Self<JavaStringBuffer>().Builder.EnsureCapacity(Math.Max(0, args.Int(0)));
                return JavaValue.Void;
            })
            .Constructor("(Ljava/lang/String;)V", static (_, args) =>
            {
                args.Self<JavaStringBuffer>().Builder.Append(args.String(0) ?? string.Empty);
                return JavaValue.Void;
            })

            // Every append returns the receiver, which is what makes chained concatenation work
            // and is also what javac compiles the + operator into.
            .Method("append", DescriptorOf("(Ljava/lang/String;)", self),
                static (_, args) => Append(args.This, args.String(0) ?? "null"))
            .Method("append", DescriptorOf("(I)", self),
                static (_, args) => Append(args.This, args.Int(0).ToString()))
            .Method("append", DescriptorOf("(J)", self),
                static (_, args) => Append(args.This, args.Long(0).ToString()))
            .Method("append", DescriptorOf("(Z)", self),
                static (_, args) => Append(args.This, args.Bool(0) ? "true" : "false"))
            .Method("append", DescriptorOf("(C)", self), static (_, args) =>
            {
                args.Self<JavaStringBuffer>().Builder.Append(args.Char(0));
                return JavaValue.Ref(args.This);
            })
            .Method("append", DescriptorOf("(F)", self),
                static (_, args) => Append(args.This, JavaText.FloatToString(args.Float(0))))
            .Method("append", DescriptorOf("(D)", self),
                static (_, args) => Append(args.This, JavaText.DoubleToString(args.Double(0))))
            .Method("append", DescriptorOf("([C)", self),
                static (_, args) => Append(args.This, new string(args.Array<char>(0) ?? [])))
            .Method("append", DescriptorOf("([CII)", self), static (thread, args) =>
            {
                char[] source = args.Array<char>(0) ?? [];
                RuntimeSupport.CheckRegion(thread, args.Int(1), args.Int(2), source.Length);
                args.Self<JavaStringBuffer>().Builder.Append(source, args.Int(1), args.Int(2));
                return JavaValue.Ref(args.This);
            })
            .Method("append", DescriptorOf("(Ljava/lang/Object;)", self), static (thread, args) =>
            {
                JavaObject? target = args.Object(0);
                string text = target switch
                {
                    null => "null",
                    JavaString direct => direct.Value,
                    _ => VirtualMachine.ToHostString(
                             thread.Vm.CallVirtual(thread, target, "toString", "()Ljava/lang/String;")
                                 .Reference as JavaObject) ?? "null",
                };
                return Append(args.This, text);
            })

            .Method("insert", DescriptorOf("(ILjava/lang/String;)", self), static (thread, args) =>
            {
                StringBuilder target = args.Self<JavaStringBuffer>().Builder;
                int offset = args.Int(0);
                if (offset < 0 || offset > target.Length)
                    throw thread.Vm.NewStringIndexOutOfBoundsException(thread, offset);
                target.Insert(offset, args.String(1) ?? "null");
                return JavaValue.Ref(args.This);
            })
            .Method("insert", DescriptorOf("(IC)", self), static (thread, args) =>
            {
                StringBuilder target = args.Self<JavaStringBuffer>().Builder;
                int offset = args.Int(0);
                if (offset < 0 || offset > target.Length)
                    throw thread.Vm.NewStringIndexOutOfBoundsException(thread, offset);
                target.Insert(offset, args.Char(1));
                return JavaValue.Ref(args.This);
            })
            .Method("delete", DescriptorOf("(II)", self), static (thread, args) =>
            {
                StringBuilder target = args.Self<JavaStringBuffer>().Builder;
                int start = args.Int(0);
                int end = Math.Min(args.Int(1), target.Length);
                if (start < 0 || start > end)
                    throw thread.Vm.NewStringIndexOutOfBoundsException(thread, start);
                target.Remove(start, end - start);
                return JavaValue.Ref(args.This);
            })
            .Method("deleteCharAt", DescriptorOf("(I)", self), static (thread, args) =>
            {
                StringBuilder target = args.Self<JavaStringBuffer>().Builder;
                int index = args.Int(0);
                if ((uint)index >= (uint)target.Length)
                    throw thread.Vm.NewStringIndexOutOfBoundsException(thread, index);
                target.Remove(index, 1);
                return JavaValue.Ref(args.This);
            })
            .Method("reverse", DescriptorOf("()", self), static (_, args) =>
            {
                StringBuilder target = args.Self<JavaStringBuffer>().Builder;
                string reversed = new(target.ToString().Reverse().ToArray());
                target.Clear().Append(reversed);
                return JavaValue.Ref(args.This);
            })
            .Method("setCharAt", "(IC)V", static (thread, args) =>
            {
                StringBuilder target = args.Self<JavaStringBuffer>().Builder;
                int index = args.Int(0);
                if ((uint)index >= (uint)target.Length)
                    throw thread.Vm.NewStringIndexOutOfBoundsException(thread, index);
                target[index] = args.Char(1);
                return JavaValue.Void;
            })
            .Method("charAt", "(I)C", static (thread, args) =>
            {
                StringBuilder target = args.Self<JavaStringBuffer>().Builder;
                int index = args.Int(0);
                if ((uint)index >= (uint)target.Length)
                    throw thread.Vm.NewStringIndexOutOfBoundsException(thread, index);
                return JavaValue.Int(target[index]);
            })
            .Method("length", "()I", static (_, args) => JavaValue.Int(args.Self<JavaStringBuffer>().Builder.Length))
            .Method("capacity", "()I",
                static (_, args) => JavaValue.Int(args.Self<JavaStringBuffer>().Builder.Capacity))
            .Method("ensureCapacity", "(I)V", static (_, args) =>
            {
                args.Self<JavaStringBuffer>().Builder.EnsureCapacity(Math.Max(0, args.Int(0)));
                return JavaValue.Void;
            })
            .Method("setLength", "(I)V", static (thread, args) =>
            {
                int length = args.Int(0);
                if (length < 0) throw thread.Vm.NewStringIndexOutOfBoundsException(thread, length);

                StringBuilder target = args.Self<JavaStringBuffer>().Builder;
                // Growing pads with NUL, which is what Java specifies here.
                if (length > target.Length) target.Append('\0', length - target.Length);
                else target.Length = length;
                return JavaValue.Void;
            })
            .Method("toString", "()Ljava/lang/String;",
                static (thread, args) =>
                    RuntimeSupport.Str(thread, args.Self<JavaStringBuffer>().Builder.ToString())));
    }


    /// <summary>Builds a builder-style descriptor that returns the declaring class itself.</summary>
    private static string DescriptorOf(string parameters, string returnType) => parameters + returnType;

    private static JavaValue Append(JavaObject receiver, string text)
    {
        ((JavaStringBuffer)receiver).Builder.Append(text);
        return JavaValue.Ref(receiver);
    }
}
