using System.Text;
using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Core.Native;
using JMobile.Runtime.Lang;
using JMobile.Runtime.Midp;

namespace JMobile.Runtime.Io;

/// <summary>
/// The <c>java.io</c> stream hierarchy.
/// </summary>
/// <remarks>
/// Streams are where the runtime library most often has to call back into guest code: a game
/// may pass its own <c>InputStream</c> subclass to a <c>DataInputStream</c>, and the wrapper
/// must then drive it through virtual dispatch. Where the underlying stream turns out to be one
/// of ours, the wrapper reads the host buffer directly instead, which matters because parsing a
/// level file byte by byte through the interpreter is otherwise noticeably slow.
/// </remarks>
public sealed class IoModule : IRuntimeModule
{
    public string Name => "java.io";

    /// <summary>Field slot in which stream wrappers keep the stream they wrap.</summary>
    private const int WrappedSlot = 0;

    public void Register(RuntimeRegistry registry)
    {
        RegisterExceptions(registry);
        RegisterInputStream(registry);
        RegisterOutputStream(registry);
        RegisterByteArrayStreams(registry);
        RegisterDataStreams(registry);
        RegisterPrintStream(registry);
        RegisterReadersAndWriters(registry);
    }

    // ---------------------------------------------------------------------------------
    // Factories used by other modules
    // ---------------------------------------------------------------------------------

    /// <summary>Creates a <c>ByteArrayInputStream</c> over host bytes, without copying twice.</summary>
    public static JavaObject NewByteArrayInputStream(JavaThread thread, byte[] data)
    {
        JavaClass type = thread.Vm.LoadClass("java/io/ByteArrayInputStream");
        var stream = (JavaStream)thread.Vm.NewInstance(type);
        stream.Underlying = new MemoryStream(data, writable: false);
        return stream;
    }

    /// <summary>Creates a <c>PrintStream</c> that forwards text to a host sink.</summary>
    public static JavaObject NewPrintStream(JavaThread thread, Action<string> sink)
    {
        JavaClass type = thread.Vm.LoadClass("java/io/PrintStream");
        var stream = (JavaStream)thread.Vm.NewInstance(type);
        stream.TextSink = sink;
        return stream;
    }

    // ---------------------------------------------------------------------------------
    // Exceptions
    // ---------------------------------------------------------------------------------

    private static void RegisterExceptions(RuntimeRegistry registry)
    {
        registry.DefineThrowable(WellKnown.IOException, WellKnown.Exception);
        registry.DefineThrowable("java/io/EOFException", WellKnown.IOException);
        registry.DefineThrowable("java/io/UnsupportedEncodingException", WellKnown.IOException);
        registry.DefineThrowable("java/io/InterruptedIOException", WellKnown.IOException);
        registry.DefineThrowable("java/io/UTFDataFormatException", WellKnown.IOException);
    }

    // ---------------------------------------------------------------------------------
    // InputStream
    // ---------------------------------------------------------------------------------

    private static void RegisterInputStream(RuntimeRegistry registry)
    {
        registry.Define(WellKnown.InputStream, builder => builder
            .AsAbstract()
            .AllocatesWith(static type => new JavaStream(type, type.InstanceFieldCount))
            .DefaultConstructor()
            .AbstractMethod("read", "()I")
            .Method("read", "([B)I", static (thread, args) =>
            {
                var target = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "b");
                return JavaValue.Int(ReadInto(thread, args.This, target, 0, target.Length));
            })
            .Method("read", "([BII)I", static (thread, args) =>
            {
                var target = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "b");
                RuntimeSupport.CheckRegion(thread, args.Int(1), args.Int(2), target.Length);
                return JavaValue.Int(ReadInto(thread, args.This, target, args.Int(1), args.Int(2)));
            })
            .Method("skip", "(J)J", static (thread, args) =>
            {
                long requested = args.Long(0);
                long skipped = 0;
                while (skipped < requested && ReadByte(thread, args.This) >= 0) skipped++;
                return JavaValue.Long(skipped);
            })
            .Method("available", "()I", static (_, args) =>
            {
                if (args.This is JavaStream { Underlying: { } stream })
                    return JavaValue.Int((int)Math.Min(int.MaxValue, stream.Length - stream.Position));
                return JavaValue.Int(0);
            })
            .Method("close", "()V", static (_, args) =>
            {
                if (args.This is JavaStream self)
                {
                    self.IsClosed = true;
                    self.Underlying?.Dispose();
                    self.Underlying = null;
                }
                return JavaValue.Void;
            })
            .Method("mark", "(I)V", static (_, args) =>
            {
                if (args.This is JavaStream { Underlying: { } stream } self)
                    self.MarkPosition = stream.Position;
                return JavaValue.Void;
            })
            .Method("reset", "()V", static (thread, args) =>
            {
                if (args.This is not JavaStream { Underlying: { } stream } self)
                    throw thread.Vm.NewIOException(thread, "mark/reset not supported");

                stream.Position = self.MarkPosition;
                return JavaValue.Void;
            })
            .Method("markSupported", "()Z",
                static (_, args) => JavaValue.Bool(args.This is JavaStream { Underlying: not null })));
    }

    /// <summary>
    /// Reads one byte from a stream that may be either ours or a guest subclass.
    /// </summary>
    private static int ReadByte(JavaThread thread, JavaObject stream)
    {
        if (stream is JavaStream { Underlying: { } underlying }) return underlying.ReadByte();

        // A guest subclass: its read() is bytecode, so it has to be dispatched virtually.
        return thread.Vm.CallVirtual(thread, stream, "read", "()I").AsInt;
    }

    private static int ReadInto(JavaThread thread, JavaObject stream, JavaArray<sbyte> target, int offset, int length)
    {
        if (length == 0) return 0;

        if (stream is JavaStream { Underlying: { } underlying })
        {
            // The fast path: one host read straight into the Java array's storage.
            Span<byte> scratch = length <= 1024 ? stackalloc byte[length] : new byte[length];
            int read = underlying.Read(scratch);
            if (read <= 0) return -1;

            for (int i = 0; i < read; i++) target.Items[offset + i] = (sbyte)scratch[i];
            return read;
        }

        int total = 0;
        while (total < length)
        {
            int value = thread.Vm.CallVirtual(thread, stream, "read", "()I").AsInt;
            if (value < 0) break;
            target.Items[offset + total] = (sbyte)value;
            total++;
        }

        return total == 0 ? -1 : total;
    }

    // ---------------------------------------------------------------------------------
    // OutputStream
    // ---------------------------------------------------------------------------------

    private static void RegisterOutputStream(RuntimeRegistry registry)
    {
        registry.Define(WellKnown.OutputStream, builder => builder
            .AsAbstract()
            .AllocatesWith(static type => new JavaStream(type, type.InstanceFieldCount))
            .DefaultConstructor()
            .AbstractMethod("write", "(I)V")
            .Method("write", "([B)V", static (thread, args) =>
            {
                var source = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "b");
                WriteFrom(thread, args.This, source, 0, source.Length);
                return JavaValue.Void;
            })
            .Method("write", "([BII)V", static (thread, args) =>
            {
                var source = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "b");
                RuntimeSupport.CheckRegion(thread, args.Int(1), args.Int(2), source.Length);
                WriteFrom(thread, args.This, source, args.Int(1), args.Int(2));
                return JavaValue.Void;
            })
            .Method("flush", "()V", static (_, args) =>
            {
                if (args.This is JavaStream { Underlying: { } stream }) stream.Flush();
                return JavaValue.Void;
            })
            .Method("close", "()V", static (_, args) =>
            {
                if (args.This is JavaStream self)
                {
                    self.IsClosed = true;
                    if (self.Underlying is MemoryStream memory) self.OnClose?.Invoke(memory.ToArray());
                    self.Underlying?.Flush();
                }
                return JavaValue.Void;
            }));
    }

    private static void WriteFrom(JavaThread thread, JavaObject stream, JavaArray<sbyte> source, int offset, int length)
    {
        if (stream is JavaStream { Underlying: { } underlying })
        {
            Span<byte> scratch = length <= 1024 ? stackalloc byte[length] : new byte[length];
            for (int i = 0; i < length; i++) scratch[i] = (byte)source.Items[offset + i];
            underlying.Write(scratch);
            return;
        }

        if (stream is JavaStream { TextSink: { } sink })
        {
            var builder = new StringBuilder(length);
            for (int i = 0; i < length; i++) builder.Append((char)(byte)source.Items[offset + i]);
            sink(builder.ToString());
            return;
        }

        for (int i = 0; i < length; i++)
            thread.Vm.CallVirtual(thread, stream, "write", "(I)V", JavaValue.Int(source.Items[offset + i]));
    }

    // ---------------------------------------------------------------------------------
    // Byte array streams
    // ---------------------------------------------------------------------------------

    private static void RegisterByteArrayStreams(RuntimeRegistry registry)
    {
        registry.Define("java/io/ByteArrayInputStream", builder => builder
            .Extends(WellKnown.InputStream)
            .AllocatesWith(static type => new JavaStream(type, type.InstanceFieldCount))
            .Constructor("([B)V", static (thread, args) =>
            {
                var source = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "buf");
                args.Self<JavaStream>().Underlying =
                    new MemoryStream(RuntimeSupport.ToHostBytes(source, 0, source.Length), writable: false);
                return JavaValue.Void;
            })
            .Constructor("([BII)V", static (thread, args) =>
            {
                var source = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "buf");
                RuntimeSupport.CheckRegion(thread, args.Int(1), args.Int(2), source.Length);
                args.Self<JavaStream>().Underlying = new MemoryStream(
                    RuntimeSupport.ToHostBytes(source, args.Int(1), args.Int(2)), writable: false);
                return JavaValue.Void;
            })
            .Method("read", "()I", static (_, args) =>
                JavaValue.Int(args.Self<JavaStream>().Underlying?.ReadByte() ?? -1)));

        registry.Define("java/io/ByteArrayOutputStream", builder => builder
            .Extends(WellKnown.OutputStream)
            .AllocatesWith(static type => new JavaStream(type, type.InstanceFieldCount))
            .Constructor("()V", static (_, args) =>
            {
                args.Self<JavaStream>().Underlying = new MemoryStream();
                return JavaValue.Void;
            })
            .Constructor("(I)V", static (_, args) =>
            {
                args.Self<JavaStream>().Underlying = new MemoryStream(Math.Max(0, args.Int(0)));
                return JavaValue.Void;
            })
            .Method("write", "(I)V", static (_, args) =>
            {
                args.Self<JavaStream>().Underlying?.WriteByte((byte)args.Int(0));
                return JavaValue.Void;
            })
            .Method("size", "()I",
                static (_, args) => JavaValue.Int((int)(args.Self<JavaStream>().Underlying?.Length ?? 0)))
            .Method("reset", "()V", static (_, args) =>
            {
                if (args.Self<JavaStream>().Underlying is MemoryStream memory) memory.SetLength(0);
                return JavaValue.Void;
            })
            .Method("toByteArray", "()[B", static (thread, args) =>
            {
                byte[] data = args.Self<JavaStream>().Underlying is MemoryStream memory
                    ? memory.ToArray()
                    : [];
                return JavaValue.Ref(thread.Vm.NewByteArray(data));
            })
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
            {
                byte[] data = args.Self<JavaStream>().Underlying is MemoryStream memory
                    ? memory.ToArray()
                    : [];
                return RuntimeSupport.Str(thread, RuntimeSupport.DecodeBytes(data, null));
            }));
    }

    // ---------------------------------------------------------------------------------
    // Data streams
    // ---------------------------------------------------------------------------------

    private static void RegisterDataStreams(RuntimeRegistry registry)
    {
        registry.DefineInterface("java/io/DataInput");
        registry.DefineInterface("java/io/DataOutput");

        registry.Define("java/io/DataInputStream", builder => builder
            .Extends(WellKnown.InputStream)
            .Implements("java/io/DataInput")
            .AllocatesWith(static type => new JavaStream(type, type.InstanceFieldCount))
            .Field("in", "Ljava/io/InputStream;")
            .Constructor("(Ljava/io/InputStream;)V", static (_, args) =>
            {
                args.This.SetField(WrappedSlot, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .Method("read", "()I", static (thread, args) => JavaValue.Int(NextByte(thread, args.This)))
            .Method("readByte", "()B", static (thread, args) => JavaValue.Int((sbyte)RequireByte(thread, args.This)))
            .Method("readUnsignedByte", "()I", static (thread, args) => JavaValue.Int(RequireByte(thread, args.This)))
            .Method("readBoolean", "()Z", static (thread, args) => JavaValue.Bool(RequireByte(thread, args.This) != 0))
            .Method("readShort", "()S", static (thread, args) =>
                JavaValue.Int((short)((RequireByte(thread, args.This) << 8) | RequireByte(thread, args.This))))
            .Method("readUnsignedShort", "()I", static (thread, args) =>
                JavaValue.Int((RequireByte(thread, args.This) << 8) | RequireByte(thread, args.This)))
            .Method("readChar", "()C", static (thread, args) =>
                JavaValue.Int((char)((RequireByte(thread, args.This) << 8) | RequireByte(thread, args.This))))
            .Method("readInt", "()I", static (thread, args) => JavaValue.Int(ReadInt32(thread, args.This)))
            .Method("readLong", "()J", static (thread, args) =>
                JavaValue.Long(((long)ReadInt32(thread, args.This) << 32) |
                               (uint)ReadInt32(thread, args.This)))
            .Method("readFloat", "()F",
                static (thread, args) => JavaValue.Float(BitConverter.Int32BitsToSingle(ReadInt32(thread, args.This))))
            .Method("readDouble", "()D", static (thread, args) =>
            {
                long bits = ((long)ReadInt32(thread, args.This) << 32) | (uint)ReadInt32(thread, args.This);
                return JavaValue.Double(BitConverter.Int64BitsToDouble(bits));
            })
            .Method("readFully", "([B)V", static (thread, args) =>
            {
                var target = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "b");
                ReadFully(thread, args.This, target, 0, target.Length);
                return JavaValue.Void;
            })
            .Method("readFully", "([BII)V", static (thread, args) =>
            {
                var target = args.Object<JavaArray<sbyte>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "b");
                RuntimeSupport.CheckRegion(thread, args.Int(1), args.Int(2), target.Length);
                ReadFully(thread, args.This, target, args.Int(1), args.Int(2));
                return JavaValue.Void;
            })
            .Method("readUTF", "()Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, ReadModifiedUtf8(thread, args.This)))
            .Method("skipBytes", "(I)I", static (thread, args) =>
            {
                int count = args.Int(0);
                int skipped = 0;
                while (skipped < count && NextByte(thread, args.This) >= 0) skipped++;
                return JavaValue.Int(skipped);
            })
            .Method("available", "()I", static (thread, args) =>
            {
                JavaObject? wrapped = args.This.GetField(WrappedSlot).Reference as JavaObject;
                return wrapped is null
                    ? JavaValue.Int(0)
                    : thread.Vm.CallVirtual(thread, wrapped, "available", "()I");
            })
            .Method("close", "()V", static (thread, args) =>
            {
                if (args.This.GetField(WrappedSlot).Reference is JavaObject wrapped)
                    thread.Vm.CallVirtual(thread, wrapped, "close", "()V");
                return JavaValue.Void;
            }));

        registry.Define("java/io/DataOutputStream", builder => builder
            .Extends(WellKnown.OutputStream)
            .Implements("java/io/DataOutput")
            .AllocatesWith(static type => new JavaStream(type, type.InstanceFieldCount))
            .Field("out", "Ljava/io/OutputStream;")
            .Constructor("(Ljava/io/OutputStream;)V", static (_, args) =>
            {
                args.This.SetField(WrappedSlot, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .Method("write", "(I)V", static (thread, args) =>
            {
                PutByte(thread, args.This, args.Int(0));
                return JavaValue.Void;
            })
            .Method("writeByte", "(I)V", static (thread, args) =>
            {
                PutByte(thread, args.This, args.Int(0));
                return JavaValue.Void;
            })
            .Method("writeBoolean", "(Z)V", static (thread, args) =>
            {
                PutByte(thread, args.This, args.Bool(0) ? 1 : 0);
                return JavaValue.Void;
            })
            .Method("writeShort", "(I)V", static (thread, args) =>
            {
                PutByte(thread, args.This, args.Int(0) >> 8);
                PutByte(thread, args.This, args.Int(0));
                return JavaValue.Void;
            })
            .Method("writeChar", "(I)V", static (thread, args) =>
            {
                PutByte(thread, args.This, args.Int(0) >> 8);
                PutByte(thread, args.This, args.Int(0));
                return JavaValue.Void;
            })
            .Method("writeInt", "(I)V", static (thread, args) =>
            {
                WriteInt32(thread, args.This, args.Int(0));
                return JavaValue.Void;
            })
            .Method("writeLong", "(J)V", static (thread, args) =>
            {
                long value = args.Long(0);
                WriteInt32(thread, args.This, (int)(value >> 32));
                WriteInt32(thread, args.This, (int)value);
                return JavaValue.Void;
            })
            .Method("writeFloat", "(F)V", static (thread, args) =>
            {
                WriteInt32(thread, args.This, BitConverter.SingleToInt32Bits(args.Float(0)));
                return JavaValue.Void;
            })
            .Method("writeDouble", "(D)V", static (thread, args) =>
            {
                long bits = BitConverter.DoubleToInt64Bits(args.Double(0));
                WriteInt32(thread, args.This, (int)(bits >> 32));
                WriteInt32(thread, args.This, (int)bits);
                return JavaValue.Void;
            })
            .Method("writeUTF", "(Ljava/lang/String;)V", static (thread, args) =>
            {
                WriteModifiedUtf8(thread, args.This, args.String(0) ?? string.Empty);
                return JavaValue.Void;
            })
            .Method("writeChars", "(Ljava/lang/String;)V", static (thread, args) =>
            {
                foreach (char character in args.String(0) ?? string.Empty)
                {
                    PutByte(thread, args.This, character >> 8);
                    PutByte(thread, args.This, character);
                }
                return JavaValue.Void;
            })
            .Method("flush", "()V", static (thread, args) =>
            {
                if (args.This.GetField(WrappedSlot).Reference is JavaObject wrapped)
                    thread.Vm.CallVirtual(thread, wrapped, "flush", "()V");
                return JavaValue.Void;
            })
            .Method("close", "()V", static (thread, args) =>
            {
                if (args.This.GetField(WrappedSlot).Reference is JavaObject wrapped)
                    thread.Vm.CallVirtual(thread, wrapped, "close", "()V");
                return JavaValue.Void;
            }));
    }

    private static int NextByte(JavaThread thread, JavaObject wrapper)
    {
        if (wrapper.GetField(WrappedSlot).Reference is not JavaObject wrapped)
            throw thread.Vm.NewIOException(thread, "stream is closed");

        return ReadByte(thread, wrapped);
    }

    private static int RequireByte(JavaThread thread, JavaObject wrapper)
    {
        int value = NextByte(thread, wrapper);
        if (value < 0) throw thread.Vm.NewException(thread, "java/io/EOFException", null);
        return value;
    }

    private static int ReadInt32(JavaThread thread, JavaObject wrapper) =>
        (RequireByte(thread, wrapper) << 24) |
        (RequireByte(thread, wrapper) << 16) |
        (RequireByte(thread, wrapper) << 8) |
        RequireByte(thread, wrapper);

    private static void ReadFully(JavaThread thread, JavaObject wrapper, JavaArray<sbyte> target, int offset, int length)
    {
        for (int i = 0; i < length; i++) target.Items[offset + i] = (sbyte)RequireByte(thread, wrapper);
    }

    private static void PutByte(JavaThread thread, JavaObject wrapper, int value)
    {
        if (wrapper.GetField(WrappedSlot).Reference is not JavaObject wrapped)
            throw thread.Vm.NewIOException(thread, "stream is closed");

        if (wrapped is JavaStream { Underlying: { } underlying })
        {
            underlying.WriteByte((byte)value);
            return;
        }

        thread.Vm.CallVirtual(thread, wrapped, "write", "(I)V", JavaValue.Int(value));
    }

    private static void WriteInt32(JavaThread thread, JavaObject wrapper, int value)
    {
        PutByte(thread, wrapper, value >> 24);
        PutByte(thread, wrapper, value >> 16);
        PutByte(thread, wrapper, value >> 8);
        PutByte(thread, wrapper, value);
    }

    /// <summary>
    /// Reads the length-prefixed modified-UTF8 form <c>DataInput.readUTF</c> defines, which is
    /// also the format record stores are usually written in.
    /// </summary>
    private static string ReadModifiedUtf8(JavaThread thread, JavaObject wrapper)
    {
        int length = (RequireByte(thread, wrapper) << 8) | RequireByte(thread, wrapper);
        var builder = new StringBuilder(length);

        int consumed = 0;
        while (consumed < length)
        {
            int first = RequireByte(thread, wrapper);
            consumed++;

            if ((first & 0x80) == 0)
            {
                builder.Append((char)first);
            }
            else if ((first & 0xE0) == 0xC0)
            {
                int second = RequireByte(thread, wrapper);
                consumed++;
                builder.Append((char)(((first & 0x1F) << 6) | (second & 0x3F)));
            }
            else
            {
                int second = RequireByte(thread, wrapper);
                int third = RequireByte(thread, wrapper);
                consumed += 2;
                builder.Append((char)(((first & 0x0F) << 12) | ((second & 0x3F) << 6) | (third & 0x3F)));
            }
        }

        return builder.ToString();
    }

    private static void WriteModifiedUtf8(JavaThread thread, JavaObject wrapper, string value)
    {
        var encoded = new List<byte>(value.Length + 8);
        foreach (char character in value)
        {
            if (character is > '\0' and <= '\u007F')
            {
                encoded.Add((byte)character);
            }
            else if (character <= '\u07FF')
            {
                encoded.Add((byte)(0xC0 | (character >> 6)));
                encoded.Add((byte)(0x80 | (character & 0x3F)));
            }
            else
            {
                encoded.Add((byte)(0xE0 | (character >> 12)));
                encoded.Add((byte)(0x80 | ((character >> 6) & 0x3F)));
                encoded.Add((byte)(0x80 | (character & 0x3F)));
            }
        }

        if (encoded.Count > 65535)
            throw thread.Vm.NewException(thread, "java/io/UTFDataFormatException", "encoded string too long");

        PutByte(thread, wrapper, encoded.Count >> 8);
        PutByte(thread, wrapper, encoded.Count);
        foreach (byte encodedByte in encoded) PutByte(thread, wrapper, encodedByte);
    }

    // ---------------------------------------------------------------------------------
    // PrintStream
    // ---------------------------------------------------------------------------------

    private static void RegisterPrintStream(RuntimeRegistry registry)
    {
        registry.Define("java/io/PrintStream", builder => builder
            .Extends(WellKnown.OutputStream)
            .AllocatesWith(static type => new JavaStream(type, type.InstanceFieldCount))
            .Constructor("(Ljava/io/OutputStream;)V", static (_, _) => JavaValue.Void)
            .Method("write", "(I)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), ((char)args.Int(0)).ToString());
                return JavaValue.Void;
            })
            .Method("print", "(Ljava/lang/String;)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), args.String(0) ?? "null");
                return JavaValue.Void;
            })
            .Method("print", "(I)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), args.Int(0).ToString());
                return JavaValue.Void;
            })
            .Method("print", "(J)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), args.Long(0).ToString());
                return JavaValue.Void;
            })
            .Method("print", "(C)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), args.Char(0).ToString());
                return JavaValue.Void;
            })
            .Method("print", "(Z)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), args.Bool(0) ? "true" : "false");
                return JavaValue.Void;
            })
            .Method("print", "(F)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), JavaText.FloatToString(args.Float(0)));
                return JavaValue.Void;
            })
            .Method("print", "(D)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), JavaText.DoubleToString(args.Double(0)));
                return JavaValue.Void;
            })
            .Method("print", "(Ljava/lang/Object;)V", static (thread, args) =>
            {
                Emit(args.Self<JavaStream>(), Describe(thread, args.Object(0)));
                return JavaValue.Void;
            })
            .Method("println", "()V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), Environment.NewLine);
                return JavaValue.Void;
            })
            .Method("println", "(Ljava/lang/String;)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), (args.String(0) ?? "null") + Environment.NewLine);
                return JavaValue.Void;
            })
            .Method("println", "(I)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), args.Int(0) + Environment.NewLine);
                return JavaValue.Void;
            })
            .Method("println", "(J)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), args.Long(0) + Environment.NewLine);
                return JavaValue.Void;
            })
            .Method("println", "(C)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), args.Char(0) + Environment.NewLine);
                return JavaValue.Void;
            })
            .Method("println", "(Z)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), (args.Bool(0) ? "true" : "false") + Environment.NewLine);
                return JavaValue.Void;
            })
            .Method("println", "(F)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), JavaText.FloatToString(args.Float(0)) + Environment.NewLine);
                return JavaValue.Void;
            })
            .Method("println", "(D)V", static (_, args) =>
            {
                Emit(args.Self<JavaStream>(), JavaText.DoubleToString(args.Double(0)) + Environment.NewLine);
                return JavaValue.Void;
            })
            .Method("println", "(Ljava/lang/Object;)V", static (thread, args) =>
            {
                Emit(args.Self<JavaStream>(), Describe(thread, args.Object(0)) + Environment.NewLine);
                return JavaValue.Void;
            })
            .Method("flush", "()V", static (_, _) => JavaValue.Void)
            .Method("close", "()V", static (_, _) => JavaValue.Void));
    }

    private static void Emit(JavaStream stream, string text) => stream.TextSink?.Invoke(text);

    private static string Describe(JavaThread thread, JavaObject? value)
    {
        if (value is null) return "null";
        if (value is JavaString direct) return direct.Value;

        JavaValue result = thread.Vm.CallVirtual(thread, value, "toString", "()Ljava/lang/String;");
        return VirtualMachine.ToHostString(result.Reference as JavaObject) ?? "null";
    }

    // ---------------------------------------------------------------------------------
    // Readers and writers
    // ---------------------------------------------------------------------------------

    private static void RegisterReadersAndWriters(RuntimeRegistry registry)
    {
        registry.Define("java/io/Reader", builder => builder
            .AsAbstract()
            .AllocatesWith(static type => new JavaStream(type, type.InstanceFieldCount))
            .DefaultConstructor()
            .AbstractMethod("read", "()I")
            .Method("read", "([CII)I", static (thread, args) =>
            {
                char[] target = args.Array<char>(0)
                                ?? throw thread.Vm.NewNullPointerException(thread, "cbuf");
                int offset = args.Int(1), length = args.Int(2);
                RuntimeSupport.CheckRegion(thread, offset, length, target.Length);

                int total = 0;
                while (total < length)
                {
                    int value = thread.Vm.CallVirtual(thread, args.This, "read", "()I").AsInt;
                    if (value < 0) break;
                    target[offset + total++] = (char)value;
                }
                return JavaValue.Int(total == 0 && length > 0 ? -1 : total);
            })
            .Method("close", "()V", static (_, _) => JavaValue.Void));

        registry.Define("java/io/InputStreamReader", builder => builder
            .Extends("java/io/Reader")
            .AllocatesWith(static type => new JavaStream(type, type.InstanceFieldCount))
            .Field("in", "Ljava/io/InputStream;")
            .Constructor("(Ljava/io/InputStream;)V", static (_, args) =>
            {
                args.This.SetField(WrappedSlot, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .Constructor("(Ljava/io/InputStream;Ljava/lang/String;)V", static (_, args) =>
            {
                args.This.SetField(WrappedSlot, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .Method("read", "()I", static (thread, args) => JavaValue.Int(NextByte(thread, args.This)))
            .Method("close", "()V", static (thread, args) =>
            {
                if (args.This.GetField(WrappedSlot).Reference is JavaObject wrapped)
                    thread.Vm.CallVirtual(thread, wrapped, "close", "()V");
                return JavaValue.Void;
            }));

        registry.Define("java/io/Writer", builder => builder
            .AsAbstract()
            .AllocatesWith(static type => new JavaStream(type, type.InstanceFieldCount))
            .DefaultConstructor()
            .AbstractMethod("write", "(I)V")
            .Method("write", "(Ljava/lang/String;)V", static (thread, args) =>
            {
                foreach (char character in args.String(0) ?? string.Empty)
                    thread.Vm.CallVirtual(thread, args.This, "write", "(I)V", JavaValue.Int(character));
                return JavaValue.Void;
            })
            .Method("flush", "()V", static (_, _) => JavaValue.Void)
            .Method("close", "()V", static (_, _) => JavaValue.Void));

        registry.Define("java/io/OutputStreamWriter", builder => builder
            .Extends("java/io/Writer")
            .AllocatesWith(static type => new JavaStream(type, type.InstanceFieldCount))
            .Field("out", "Ljava/io/OutputStream;")
            .Constructor("(Ljava/io/OutputStream;)V", static (_, args) =>
            {
                args.This.SetField(WrappedSlot, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .Constructor("(Ljava/io/OutputStream;Ljava/lang/String;)V", static (_, args) =>
            {
                args.This.SetField(WrappedSlot, JavaValue.Ref(args.Object(0)));
                return JavaValue.Void;
            })
            .Method("write", "(I)V", static (thread, args) =>
            {
                PutByte(thread, args.This, args.Int(0));
                return JavaValue.Void;
            }));
    }
}
