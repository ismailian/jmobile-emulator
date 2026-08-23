namespace JMobile.Core.ClassFile;

/// <summary>
/// Parses a <c>.class</c> byte stream into <see cref="ClassFileData"/>.
/// </summary>
/// <remarks>
/// The reader is deliberately permissive about attributes it does not understand: J2ME
/// suites are frequently processed by obfuscators and optimisers that emit unusual or
/// vendor-specific attributes, and rejecting a class over an attribute the interpreter
/// never reads would make the emulator fail on games that run fine everywhere else.
/// Structural errors that would make execution unsound are still rejected.
/// </remarks>
public static class ClassFileReader
{
    private const uint Magic = 0xCAFEBABE;

    public static ClassFileData Read(ReadOnlySpan<byte> bytes)
    {
        var reader = new BigEndianReader(bytes);

        uint magic = reader.ReadU4();
        if (magic != Magic)
            throw new ClassFormatException($"Bad magic 0x{magic:X8}; this is not a Java class file.");

        int minor = reader.ReadU2();
        int major = reader.ReadU2();

        ConstantPool pool = ReadConstantPool(ref reader);

        var flags = (AccessFlags)reader.ReadU2();

        int thisClassIndex = reader.ReadU2();
        string name = pool.ClassName(thisClassIndex);

        int superClassIndex = reader.ReadU2();
        string? superName = superClassIndex == 0 ? null : pool.ClassName(superClassIndex);

        int interfaceCount = reader.ReadU2();
        var interfaceNames = interfaceCount == 0 ? [] : new string[interfaceCount];
        for (int i = 0; i < interfaceCount; i++)
            interfaceNames[i] = pool.ClassName(reader.ReadU2());

        FieldData[] fields = ReadFields(ref reader, pool);
        MethodData[] methods = ReadMethods(ref reader, pool);
        string? sourceFile = ReadClassAttributes(ref reader, pool);

        return new ClassFileData
        {
            Pool = pool,
            Flags = flags,
            Name = name,
            SuperName = superName,
            InterfaceNames = interfaceNames,
            Fields = fields,
            Methods = methods,
            SourceFile = sourceFile,
            MajorVersion = major,
            MinorVersion = minor,
        };
    }

    private static ConstantPool ReadConstantPool(ref BigEndianReader reader)
    {
        int count = reader.ReadU2();
        if (count < 1) throw new ClassFormatException("Constant pool count must be at least 1.");

        var pool = new ConstantPool(count);

        for (int index = 1; index < count; index++)
        {
            byte tag = reader.ReadU1();
            switch (tag)
            {
                case ConstantTag.Utf8:
                    pool.SetString(index, reader.ReadModifiedUtf8(reader.ReadU2()));
                    break;

                case ConstantTag.Integer:
                case ConstantTag.Float:
                    pool.SetNumber(index, tag, reader.ReadS4());
                    break;

                case ConstantTag.Long:
                case ConstantTag.Double:
                    pool.SetNumber(index, tag, reader.ReadS8());
                    // Long and double constants occupy two pool entries; the second is unusable.
                    index++;
                    break;

                case ConstantTag.Class:
                case ConstantTag.String:
                case ConstantTag.MethodType:
                    pool.Set(index, tag, reader.ReadU2());
                    break;

                case ConstantTag.FieldRef:
                case ConstantTag.MethodRef:
                case ConstantTag.InterfaceMethodRef:
                case ConstantTag.NameAndType:
                case ConstantTag.Dynamic:
                case ConstantTag.InvokeDynamic:
                    pool.Set(index, tag, reader.ReadU2(), reader.ReadU2());
                    break;

                case ConstantTag.MethodHandle:
                    pool.Set(index, tag, reader.ReadU1(), reader.ReadU2());
                    break;

                case ConstantTag.Module:
                case ConstantTag.Package:
                    pool.Set(index, tag, reader.ReadU2());
                    break;

                default:
                    throw new ClassFormatException($"Unsupported constant pool tag {tag} at index {index}.");
            }
        }

        return pool;
    }

    private static FieldData[] ReadFields(ref BigEndianReader reader, ConstantPool pool)
    {
        int count = reader.ReadU2();
        if (count == 0)
        {
            return [];
        }

        var fields = new FieldData[count];
        for (int i = 0; i < count; i++)
        {
            var flags = (AccessFlags)reader.ReadU2();
            string name = pool.Utf8(reader.ReadU2());
            string descriptor = pool.Utf8(reader.ReadU2());

            int constantValueIndex = 0;
            int attributeCount = reader.ReadU2();
            for (int a = 0; a < attributeCount; a++)
            {
                string attributeName = pool.Utf8(reader.ReadU2());
                int length = (int)reader.ReadU4();
                int end = reader.Position + length;

                if (attributeName == "ConstantValue" && length == 2)
                    constantValueIndex = reader.ReadU2();

                reader.Seek(end);
            }

            fields[i] = new FieldData
            {
                Name = name,
                Descriptor = descriptor,
                Flags = flags,
                ConstantValueIndex = constantValueIndex,
            };
        }

        return fields;
    }

    private static MethodData[] ReadMethods(ref BigEndianReader reader, ConstantPool pool)
    {
        int count = reader.ReadU2();
        if (count == 0)
        {
            return [];
        }

        var methods = new MethodData[count];
        for (int i = 0; i < count; i++)
        {
            var flags = (AccessFlags)reader.ReadU2();
            string name = pool.Utf8(reader.ReadU2());
            string descriptor = pool.Utf8(reader.ReadU2());

            byte[]? code = null;
            int maxStack = 0, maxLocals = 0;
            ExceptionTableEntry[] handlers = [];
            LineNumberEntry[] lines = [];

            int attributeCount = reader.ReadU2();
            for (int a = 0; a < attributeCount; a++)
            {
                string attributeName = pool.Utf8(reader.ReadU2());
                int length = (int)reader.ReadU4();
                int end = reader.Position + length;

                if (attributeName == "Code")
                {
                    maxStack = reader.ReadU2();
                    maxLocals = reader.ReadU2();

                    int codeLength = (int)reader.ReadU4();
                    if (codeLength <= 0 || codeLength > 65535)
                        throw new ClassFormatException(
                            $"Method {name}{descriptor} declares an invalid code length of {codeLength}.");
                    code = reader.ReadBytes(codeLength);

                    int handlerCount = reader.ReadU2();
                    if (handlerCount > 0)
                    {
                        handlers = new ExceptionTableEntry[handlerCount];
                        for (int h = 0; h < handlerCount; h++)
                        {
                            handlers[h] = new ExceptionTableEntry(
                                StartPc: reader.ReadU2(),
                                EndPc: reader.ReadU2(),
                                HandlerPc: reader.ReadU2(),
                                CatchTypeIndex: reader.ReadU2());
                        }
                    }

                    lines = ReadCodeAttributes(ref reader, pool, end);
                }

                reader.Seek(end);
            }

            methods[i] = new MethodData
            {
                Name = name,
                Descriptor = descriptor,
                Flags = flags,
                MaxStack = maxStack,
                MaxLocals = maxLocals,
                Code = code,
                ExceptionTable = handlers,
                LineNumbers = lines,
            };
        }

        return methods;
    }

    /// <summary>Reads the attributes nested inside a Code attribute, keeping only the line table.</summary>
    private static LineNumberEntry[] ReadCodeAttributes(ref BigEndianReader reader, ConstantPool pool, int codeEnd)
    {
        LineNumberEntry[] lines = [];

        int attributeCount = reader.ReadU2();
        for (int a = 0; a < attributeCount && reader.Position < codeEnd; a++)
        {
            string attributeName = pool.Utf8(reader.ReadU2());
            int length = (int)reader.ReadU4();
            int end = reader.Position + length;

            if (attributeName == "LineNumberTable")
            {
                int entryCount = reader.ReadU2();
                if (entryCount > 0)
                {
                    lines = new LineNumberEntry[entryCount];
                    for (int e = 0; e < entryCount; e++)
                        lines[e] = new LineNumberEntry(reader.ReadU2(), reader.ReadU2());
                }
            }

            reader.Seek(end);
        }

        return lines;
    }

    private static string? ReadClassAttributes(ref BigEndianReader reader, ConstantPool pool)
    {
        string? sourceFile = null;

        int attributeCount = reader.ReadU2();
        for (int a = 0; a < attributeCount; a++)
        {
            string attributeName = pool.Utf8(reader.ReadU2());
            int length = (int)reader.ReadU4();
            int end = reader.Position + length;

            if (attributeName == "SourceFile" && length == 2)
                sourceFile = pool.Utf8(reader.ReadU2());

            reader.Seek(end);
        }

        return sourceFile;
    }
}
