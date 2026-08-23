using JMobile.Core;
using JMobile.Core.Native;

namespace JMobile.Runtime.Lang;

/// <summary>
/// The primitive wrapper types: Integer, Long, Short, Byte, Character, Boolean, Float, Double.
/// </summary>
/// <remarks>
/// Every wrapper stores its payload in field slot 0 of the instance, so the accessors are a
/// single indexed read. Java's number parsing and formatting differ from .NET's in ways that
/// show up on screen and in save files, so those go through <see cref="JavaText"/> rather than
/// the CLR conversions.
/// </remarks>
public sealed class WrapperModule : IRuntimeModule
{
    public string Name => "java.lang wrappers";

    /// <summary>Field slot every wrapper keeps its value in.</summary>
    private const int ValueSlot = 0;

    public void Register(RuntimeRegistry registry)
    {
        registry.Define("java/lang/Number", builder => builder
            .AsAbstract()
            .DefaultConstructor()
            .AbstractMethod("intValue", "()I")
            .AbstractMethod("longValue", "()J")
            .AbstractMethod("floatValue", "()F")
            .AbstractMethod("doubleValue", "()D"));

        RegisterInteger(registry);
        RegisterLong(registry);
        RegisterShortAndByte(registry);
        RegisterCharacter(registry);
        RegisterBoolean(registry);
        RegisterFloat(registry);
        RegisterDouble(registry);
    }

    private static void RegisterInteger(RuntimeRegistry registry)
    {
        registry.Define("java/lang/Integer", builder => builder
            .Extends("java/lang/Number")
            .AsFinal()
            .Field("value", "I")
            .Constant("MIN_VALUE", int.MinValue)
            .Constant("MAX_VALUE", int.MaxValue)
            .Constructor("(I)V", static (_, args) =>
            {
                args.This.SetField(ValueSlot, JavaValue.Int(args.Int(0)));
                return JavaValue.Void;
            })
            .Constructor("(Ljava/lang/String;)V", static (thread, args) =>
            {
                if (!JavaText.TryParseInt(args.String(0), 10, out int parsed))
                    throw thread.Vm.NewNumberFormatException(thread, args.String(0) ?? "null");
                args.This.SetField(ValueSlot, JavaValue.Int(parsed));
                return JavaValue.Void;
            })
            .Method("intValue", "()I", static (_, args) => args.This.GetField(ValueSlot))
            .Method("longValue", "()J", static (_, args) => JavaValue.Long(args.This.GetField(ValueSlot).AsInt))
            .Method("floatValue", "()F", static (_, args) => JavaValue.Float(args.This.GetField(ValueSlot).AsInt))
            .Method("doubleValue", "()D", static (_, args) => JavaValue.Double(args.This.GetField(ValueSlot).AsInt))
            .Method("byteValue", "()B", static (_, args) => JavaValue.Int((sbyte)args.This.GetField(ValueSlot).AsInt))
            .Method("shortValue", "()S", static (_, args) => JavaValue.Int((short)args.This.GetField(ValueSlot).AsInt))
            .Method("hashCode", "()I", static (_, args) => args.This.GetField(ValueSlot))
            .Method("equals", "(Ljava/lang/Object;)Z", static (_, args) =>
            {
                var other = args.Object(0);
                return JavaValue.Bool(
                    other is not null &&
                    other.Class.Name == "java/lang/Integer" &&
                    other.GetField(ValueSlot).AsInt == args.This.GetField(ValueSlot).AsInt);
            })
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, args.This.GetField(ValueSlot).AsInt.ToString()))
            .StaticMethod("parseInt", "(Ljava/lang/String;)I", static (thread, args) =>
            {
                if (!JavaText.TryParseInt(args.String(0), 10, out int parsed))
                    throw thread.Vm.NewNumberFormatException(thread, args.String(0) ?? "null");
                return JavaValue.Int(parsed);
            })
            .StaticMethod("parseInt", "(Ljava/lang/String;I)I", static (thread, args) =>
            {
                if (!JavaText.TryParseInt(args.String(0), args.Int(1), out int parsed))
                    throw thread.Vm.NewNumberFormatException(thread, args.String(0) ?? "null");
                return JavaValue.Int(parsed);
            })
            .StaticMethod("valueOf", "(Ljava/lang/String;)Ljava/lang/Integer;", static (thread, args) =>
            {
                if (!JavaText.TryParseInt(args.String(0), 10, out int parsed))
                    throw thread.Vm.NewNumberFormatException(thread, args.String(0) ?? "null");
                return JavaValue.Ref(thread.Vm.Construct(
                    thread, thread.Vm.LoadClass("java/lang/Integer"), "(I)V", JavaValue.Int(parsed)));
            })
            .StaticMethod("valueOf", "(I)Ljava/lang/Integer;", static (thread, args) =>
                JavaValue.Ref(thread.Vm.Construct(
                    thread, thread.Vm.LoadClass("java/lang/Integer"), "(I)V", JavaValue.Int(args.Int(0)))))
            .StaticMethod("toString", "(I)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Int(0).ToString()))
            .StaticMethod("toString", "(II)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, JavaText.IntToString(args.Int(0), args.Int(1))))
            .StaticMethod("toHexString", "(I)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, JavaText.ToHexString(args.Int(0))))
            .StaticMethod("toOctalString", "(I)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, JavaText.ToOctalString(args.Int(0))))
            .StaticMethod("toBinaryString", "(I)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, JavaText.ToBinaryString(args.Int(0)))));
    }

    private static void RegisterLong(RuntimeRegistry registry)
    {
        registry.Define("java/lang/Long", builder => builder
            .Extends("java/lang/Number")
            .AsFinal()
            .Field("value", "J")
            .Constant("MIN_VALUE", long.MinValue)
            .Constant("MAX_VALUE", long.MaxValue)
            .Constructor("(J)V", static (_, args) =>
            {
                args.This.SetField(ValueSlot, JavaValue.Long(args.Long(0)));
                return JavaValue.Void;
            })
            .Method("intValue", "()I", static (_, args) => JavaValue.Int((int)args.This.GetField(ValueSlot).AsLong))
            .Method("longValue", "()J", static (_, args) => args.This.GetField(ValueSlot))
            .Method("floatValue", "()F", static (_, args) => JavaValue.Float(args.This.GetField(ValueSlot).AsLong))
            .Method("doubleValue", "()D", static (_, args) => JavaValue.Double(args.This.GetField(ValueSlot).AsLong))
            .Method("hashCode", "()I", static (_, args) =>
            {
                long value = args.This.GetField(ValueSlot).AsLong;
                return JavaValue.Int((int)(value ^ (long)((ulong)value >> 32)));
            })
            .Method("equals", "(Ljava/lang/Object;)Z", static (_, args) =>
            {
                var other = args.Object(0);
                return JavaValue.Bool(
                    other is not null &&
                    other.Class.Name == "java/lang/Long" &&
                    other.GetField(ValueSlot).AsLong == args.This.GetField(ValueSlot).AsLong);
            })
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, args.This.GetField(ValueSlot).AsLong.ToString()))
            .StaticMethod("parseLong", "(Ljava/lang/String;)J", static (thread, args) =>
            {
                if (!JavaText.TryParseLong(args.String(0), 10, out long parsed))
                    throw thread.Vm.NewNumberFormatException(thread, args.String(0) ?? "null");
                return JavaValue.Long(parsed);
            })
            .StaticMethod("parseLong", "(Ljava/lang/String;I)J", static (thread, args) =>
            {
                if (!JavaText.TryParseLong(args.String(0), args.Int(1), out long parsed))
                    throw thread.Vm.NewNumberFormatException(thread, args.String(0) ?? "null");
                return JavaValue.Long(parsed);
            })
            .StaticMethod("toString", "(J)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Long(0).ToString()))
            .StaticMethod("toString", "(JI)Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, JavaText.LongToString(args.Long(0), args.Int(1))))
            .StaticMethod("toHexString", "(J)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, JavaText.ToHexString(args.Long(0)))));
    }

    private static void RegisterShortAndByte(RuntimeRegistry registry)
    {
        registry.Define("java/lang/Short", builder => builder
            .Extends("java/lang/Number")
            .AsFinal()
            .Field("value", "S")
            .Constant("MIN_VALUE", (int)short.MinValue)
            .Constant("MAX_VALUE", (int)short.MaxValue)
            .Constructor("(S)V", static (_, args) =>
            {
                args.This.SetField(ValueSlot, JavaValue.Int(args.Short(0)));
                return JavaValue.Void;
            })
            .Method("shortValue", "()S", static (_, args) => args.This.GetField(ValueSlot))
            .Method("intValue", "()I", static (_, args) => args.This.GetField(ValueSlot))
            .Method("longValue", "()J", static (_, args) => JavaValue.Long(args.This.GetField(ValueSlot).AsInt))
            .Method("floatValue", "()F", static (_, args) => JavaValue.Float(args.This.GetField(ValueSlot).AsInt))
            .Method("doubleValue", "()D", static (_, args) => JavaValue.Double(args.This.GetField(ValueSlot).AsInt))
            .Method("hashCode", "()I", static (_, args) => args.This.GetField(ValueSlot))
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, args.This.GetField(ValueSlot).AsInt.ToString()))
            .StaticMethod("parseShort", "(Ljava/lang/String;)S", static (thread, args) =>
            {
                if (!JavaText.TryParseInt(args.String(0), 10, out int parsed) ||
                    parsed is < short.MinValue or > short.MaxValue)
                {
                    throw thread.Vm.NewNumberFormatException(thread, args.String(0) ?? "null");
                }
                return JavaValue.Int((short)parsed);
            }));

        registry.Define("java/lang/Byte", builder => builder
            .Extends("java/lang/Number")
            .AsFinal()
            .Field("value", "B")
            .Constant("MIN_VALUE", (int)sbyte.MinValue)
            .Constant("MAX_VALUE", (int)sbyte.MaxValue)
            .Constructor("(B)V", static (_, args) =>
            {
                args.This.SetField(ValueSlot, JavaValue.Int((sbyte)args.Int(0)));
                return JavaValue.Void;
            })
            .Method("byteValue", "()B", static (_, args) => args.This.GetField(ValueSlot))
            .Method("intValue", "()I", static (_, args) => args.This.GetField(ValueSlot))
            .Method("longValue", "()J", static (_, args) => JavaValue.Long(args.This.GetField(ValueSlot).AsInt))
            .Method("floatValue", "()F", static (_, args) => JavaValue.Float(args.This.GetField(ValueSlot).AsInt))
            .Method("doubleValue", "()D", static (_, args) => JavaValue.Double(args.This.GetField(ValueSlot).AsInt))
            .Method("hashCode", "()I", static (_, args) => args.This.GetField(ValueSlot))
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, args.This.GetField(ValueSlot).AsInt.ToString()))
            .StaticMethod("parseByte", "(Ljava/lang/String;)B", static (thread, args) =>
            {
                if (!JavaText.TryParseInt(args.String(0), 10, out int parsed) ||
                    parsed is < sbyte.MinValue or > sbyte.MaxValue)
                {
                    throw thread.Vm.NewNumberFormatException(thread, args.String(0) ?? "null");
                }
                return JavaValue.Int((sbyte)parsed);
            }));
    }

    private static void RegisterCharacter(RuntimeRegistry registry)
    {
        registry.Define("java/lang/Character", builder => builder
            .AsFinal()
            .Field("value", "C")
            .Constant("MIN_VALUE", (int)char.MinValue)
            .Constant("MAX_VALUE", (int)char.MaxValue)
            .Constant("MIN_RADIX", 2)
            .Constant("MAX_RADIX", 36)
            .Constructor("(C)V", static (_, args) =>
            {
                args.This.SetField(ValueSlot, JavaValue.Int(args.Char(0)));
                return JavaValue.Void;
            })
            .Method("charValue", "()C", static (_, args) => args.This.GetField(ValueSlot))
            .Method("hashCode", "()I", static (_, args) => args.This.GetField(ValueSlot))
            .Method("equals", "(Ljava/lang/Object;)Z", static (_, args) =>
            {
                var other = args.Object(0);
                return JavaValue.Bool(
                    other is not null &&
                    other.Class.Name == "java/lang/Character" &&
                    other.GetField(ValueSlot).AsInt == args.This.GetField(ValueSlot).AsInt);
            })
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, ((char)args.This.GetField(ValueSlot).AsInt).ToString()))
            .StaticMethod("isDigit", "(C)Z", static (_, args) => JavaValue.Bool(char.IsDigit(args.Char(0))))
            .StaticMethod("isLowerCase", "(C)Z", static (_, args) => JavaValue.Bool(char.IsLower(args.Char(0))))
            .StaticMethod("isUpperCase", "(C)Z", static (_, args) => JavaValue.Bool(char.IsUpper(args.Char(0))))
            .StaticMethod("isLetter", "(C)Z", static (_, args) => JavaValue.Bool(char.IsLetter(args.Char(0))))
            .StaticMethod("isLetterOrDigit", "(C)Z",
                static (_, args) => JavaValue.Bool(char.IsLetterOrDigit(args.Char(0))))
            .StaticMethod("isSpace", "(C)Z", static (_, args) => JavaValue.Bool(char.IsWhiteSpace(args.Char(0))))
            .StaticMethod("isWhitespace", "(C)Z",
                static (_, args) => JavaValue.Bool(char.IsWhiteSpace(args.Char(0))))
            .StaticMethod("toLowerCase", "(C)C",
                static (_, args) => JavaValue.Int(char.ToLowerInvariant(args.Char(0))))
            .StaticMethod("toUpperCase", "(C)C",
                static (_, args) => JavaValue.Int(char.ToUpperInvariant(args.Char(0))))
            .StaticMethod("digit", "(CI)I", static (_, args) =>
            {
                char character = args.Char(0);
                int radix = args.Int(1);
                int digit = character switch
                {
                    >= '0' and <= '9' => character - '0',
                    >= 'a' and <= 'z' => character - 'a' + 10,
                    >= 'A' and <= 'Z' => character - 'A' + 10,
                    _ => -1,
                };
                return JavaValue.Int(digit >= 0 && digit < radix ? digit : -1);
            }));
    }

    private static void RegisterBoolean(RuntimeRegistry registry)
    {
        registry.Define("java/lang/Boolean", builder => builder
            .AsFinal()
            .Field("value", "Z")
            .Constructor("(Z)V", static (_, args) =>
            {
                args.This.SetField(ValueSlot, JavaValue.Bool(args.Bool(0)));
                return JavaValue.Void;
            })
            .Method("booleanValue", "()Z", static (_, args) => args.This.GetField(ValueSlot))
            .Method("hashCode", "()I",
                static (_, args) => JavaValue.Int(args.This.GetField(ValueSlot).AsBool ? 1231 : 1237))
            .Method("equals", "(Ljava/lang/Object;)Z", static (_, args) =>
            {
                var other = args.Object(0);
                return JavaValue.Bool(
                    other is not null &&
                    other.Class.Name == "java/lang/Boolean" &&
                    other.GetField(ValueSlot).AsBool == args.This.GetField(ValueSlot).AsBool);
            })
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, args.This.GetField(ValueSlot).AsBool ? "true" : "false"))
            .StaticMethod("valueOf", "(Ljava/lang/String;)Ljava/lang/Boolean;", static (thread, args) =>
            {
                bool parsed = string.Equals(args.String(0), "true", StringComparison.OrdinalIgnoreCase);
                return JavaValue.Ref(thread.Vm.Construct(
                    thread, thread.Vm.LoadClass("java/lang/Boolean"), "(Z)V", JavaValue.Bool(parsed)));
            }));
    }

    private static void RegisterFloat(RuntimeRegistry registry)
    {
        registry.Define("java/lang/Float", builder => builder
            .Extends("java/lang/Number")
            .AsFinal()
            .Field("value", "F")
            .Constant("MIN_VALUE", float.Epsilon)
            .Constant("MAX_VALUE", float.MaxValue)
            .Constant("NaN", float.NaN)
            .Constant("POSITIVE_INFINITY", float.PositiveInfinity)
            .Constant("NEGATIVE_INFINITY", float.NegativeInfinity)
            .Constructor("(F)V", static (_, args) =>
            {
                args.This.SetField(ValueSlot, JavaValue.Float(args.Float(0)));
                return JavaValue.Void;
            })
            .Constructor("(D)V", static (_, args) =>
            {
                args.This.SetField(ValueSlot, JavaValue.Float((float)args.Double(0)));
                return JavaValue.Void;
            })
            .Method("floatValue", "()F", static (_, args) => args.This.GetField(ValueSlot))
            .Method("doubleValue", "()D", static (_, args) => JavaValue.Double(args.This.GetField(ValueSlot).AsFloat))
            .Method("intValue", "()I", static (_, args) => JavaValue.Int((int)args.This.GetField(ValueSlot).AsFloat))
            .Method("longValue", "()J", static (_, args) => JavaValue.Long((long)args.This.GetField(ValueSlot).AsFloat))
            .Method("isNaN", "()Z", static (_, args) => JavaValue.Bool(float.IsNaN(args.This.GetField(ValueSlot).AsFloat)))
            .Method("hashCode", "()I", static (_, args) =>
                JavaValue.Int(BitConverter.SingleToInt32Bits(args.This.GetField(ValueSlot).AsFloat)))
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, JavaText.FloatToString(args.This.GetField(ValueSlot).AsFloat)))
            .StaticMethod("parseFloat", "(Ljava/lang/String;)F", static (thread, args) =>
            {
                if (!float.TryParse(args.String(0),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                {
                    throw thread.Vm.NewNumberFormatException(thread, args.String(0) ?? "null");
                }
                return JavaValue.Float(parsed);
            })
            .StaticMethod("toString", "(F)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, JavaText.FloatToString(args.Float(0))))
            .StaticMethod("isNaN", "(F)Z", static (_, args) => JavaValue.Bool(float.IsNaN(args.Float(0))))
            .StaticMethod("isInfinite", "(F)Z",
                static (_, args) => JavaValue.Bool(float.IsInfinity(args.Float(0))))
            .StaticMethod("floatToIntBits", "(F)I",
                static (_, args) => JavaValue.Int(BitConverter.SingleToInt32Bits(args.Float(0))))
            .StaticMethod("intBitsToFloat", "(I)F",
                static (_, args) => JavaValue.Float(BitConverter.Int32BitsToSingle(args.Int(0)))));
    }

    private static void RegisterDouble(RuntimeRegistry registry)
    {
        registry.Define("java/lang/Double", builder => builder
            .Extends("java/lang/Number")
            .AsFinal()
            .Field("value", "D")
            .Constant("MIN_VALUE", double.Epsilon)
            .Constant("MAX_VALUE", double.MaxValue)
            .Constant("NaN", double.NaN)
            .Constant("POSITIVE_INFINITY", double.PositiveInfinity)
            .Constant("NEGATIVE_INFINITY", double.NegativeInfinity)
            .Constructor("(D)V", static (_, args) =>
            {
                args.This.SetField(ValueSlot, JavaValue.Double(args.Double(0)));
                return JavaValue.Void;
            })
            .Method("doubleValue", "()D", static (_, args) => args.This.GetField(ValueSlot))
            .Method("floatValue", "()F", static (_, args) => JavaValue.Float((float)args.This.GetField(ValueSlot).AsDouble))
            .Method("intValue", "()I", static (_, args) => JavaValue.Int((int)args.This.GetField(ValueSlot).AsDouble))
            .Method("longValue", "()J", static (_, args) => JavaValue.Long((long)args.This.GetField(ValueSlot).AsDouble))
            .Method("isNaN", "()Z",
                static (_, args) => JavaValue.Bool(double.IsNaN(args.This.GetField(ValueSlot).AsDouble)))
            .Method("hashCode", "()I", static (_, args) =>
            {
                long bits = BitConverter.DoubleToInt64Bits(args.This.GetField(ValueSlot).AsDouble);
                return JavaValue.Int((int)(bits ^ (long)((ulong)bits >> 32)));
            })
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, JavaText.DoubleToString(args.This.GetField(ValueSlot).AsDouble)))
            .StaticMethod("parseDouble", "(Ljava/lang/String;)D", static (thread, args) =>
            {
                if (!double.TryParse(args.String(0),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                {
                    throw thread.Vm.NewNumberFormatException(thread, args.String(0) ?? "null");
                }
                return JavaValue.Double(parsed);
            })
            .StaticMethod("toString", "(D)Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, JavaText.DoubleToString(args.Double(0))))
            .StaticMethod("isNaN", "(D)Z", static (_, args) => JavaValue.Bool(double.IsNaN(args.Double(0))))
            .StaticMethod("isInfinite", "(D)Z",
                static (_, args) => JavaValue.Bool(double.IsInfinity(args.Double(0))))
            .StaticMethod("doubleToLongBits", "(D)J",
                static (_, args) => JavaValue.Long(BitConverter.DoubleToInt64Bits(args.Double(0))))
            .StaticMethod("longBitsToDouble", "(J)D",
                static (_, args) => JavaValue.Double(BitConverter.Int64BitsToDouble(args.Long(0)))));
    }
}
