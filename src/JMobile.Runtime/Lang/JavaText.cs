using System.Globalization;

namespace JMobile.Runtime.Lang;

/// <summary>
/// Text conversions that must match Java's output rather than .NET's.
/// </summary>
/// <remarks>
/// These differences are small but they are visible on screen. Java prints <c>1.0</c> where
/// .NET prints <c>1</c>, switches to scientific notation at different thresholds, and hashes
/// strings with a specific polynomial that games occasionally persist to a record store.
/// </remarks>
public static class JavaText
{
    /// <summary>
    /// Java's <c>String.hashCode</c>: <c>s[0]*31^(n-1) + s[1]*31^(n-2) + ...</c>
    /// </summary>
    /// <remarks>
    /// Reproduced exactly because it is observable. A game that saves a hash into a record
    /// store and compares it on the next run would silently lose its save data if this
    /// returned .NET's hash instead.
    /// </remarks>
    public static int StringHashCode(string value)
    {
        int hash = 0;
        foreach (char character in value) hash = unchecked((hash * 31) + character);
        return hash;
    }

    /// <summary>Formats a double the way <c>Double.toString</c> does.</summary>
    public static string DoubleToString(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        if (value == 0d) return 1 / value < 0 ? "-0.0" : "0.0";

        double magnitude = Math.Abs(value);

        // Java uses plain notation between 10^-3 and 10^7 and scientific notation outside it.
        if (magnitude >= 1e-3 && magnitude < 1e7)
        {
            string plain = value.ToString("R", CultureInfo.InvariantCulture);
            if (plain.Contains('E') || plain.Contains('e'))
                plain = value.ToString("0.0###############", CultureInfo.InvariantCulture);
            return plain.Contains('.') ? plain : plain + ".0";
        }

        return Scientific(value, 17);
    }

    /// <summary>Formats a float the way <c>Float.toString</c> does.</summary>
    public static string FloatToString(float value)
    {
        if (float.IsNaN(value)) return "NaN";
        if (float.IsPositiveInfinity(value)) return "Infinity";
        if (float.IsNegativeInfinity(value)) return "-Infinity";
        if (value == 0f) return 1 / value < 0 ? "-0.0" : "0.0";

        float magnitude = Math.Abs(value);

        if (magnitude >= 1e-3f && magnitude < 1e7f)
        {
            string plain = value.ToString("R", CultureInfo.InvariantCulture);
            if (plain.Contains('E') || plain.Contains('e'))
                plain = value.ToString("0.0#######", CultureInfo.InvariantCulture);
            return plain.Contains('.') ? plain : plain + ".0";
        }

        return Scientific(value, 9);
    }

    /// <summary>Renders in Java's <c>d.dddEn</c> form, which differs from .NET's <c>E+00n</c>.</summary>
    private static string Scientific(double value, int significantDigits)
    {
        string formatted = value.ToString("E" + (significantDigits - 1), CultureInfo.InvariantCulture);
        int marker = formatted.IndexOf('E');
        string mantissa = formatted[..marker].TrimEnd('0');
        if (mantissa.EndsWith('.')) mantissa += "0";

        int exponent = int.Parse(formatted[(marker + 1)..], CultureInfo.InvariantCulture);
        return mantissa + "E" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Parses an integer in a given radix, matching <c>Integer.parseInt</c>.</summary>
    public static bool TryParseInt(string? text, int radix, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text) || radix < 2 || radix > 36) return false;

        int index = 0;
        bool negative = false;
        if (text[0] is '-' or '+')
        {
            negative = text[0] == '-';
            index = 1;
            if (text.Length == 1) return false;
        }

        long accumulator = 0;
        for (; index < text.Length; index++)
        {
            int digit = DigitValue(text[index]);
            if (digit < 0 || digit >= radix) return false;

            accumulator = (accumulator * radix) + digit;
            if (accumulator > (negative ? 2147483648L : int.MaxValue)) return false;
        }

        value = negative ? (int)-accumulator : (int)accumulator;
        return true;
    }

    /// <summary>Parses a long in a given radix, matching <c>Long.parseLong</c>.</summary>
    public static bool TryParseLong(string? text, int radix, out long value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text) || radix < 2 || radix > 36) return false;

        int index = 0;
        bool negative = false;
        if (text[0] is '-' or '+')
        {
            negative = text[0] == '-';
            index = 1;
            if (text.Length == 1) return false;
        }

        ulong accumulator = 0;
        ulong limit = negative ? 9223372036854775808UL : long.MaxValue;

        for (; index < text.Length; index++)
        {
            int digit = DigitValue(text[index]);
            if (digit < 0 || digit >= radix) return false;

            ulong next = (accumulator * (ulong)radix) + (ulong)digit;
            if (next < accumulator || next > limit) return false;
            accumulator = next;
        }

        value = negative ? unchecked(-(long)accumulator) : (long)accumulator;
        return true;
    }

    private static int DigitValue(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'a' and <= 'z' => character - 'a' + 10,
        >= 'A' and <= 'Z' => character - 'A' + 10,
        _ => -1,
    };

    /// <summary>Renders an integer in a radix, matching <c>Integer.toString(int, int)</c>.</summary>
    public static string IntToString(int value, int radix)
    {
        if (radix is < 2 or > 36) radix = 10;
        if (radix == 10) return value.ToString(CultureInfo.InvariantCulture);
        if (value == 0) return "0";

        bool negative = value < 0;
        // Negated into a long so that Integer.MIN_VALUE does not overflow.
        long magnitude = negative ? -(long)value : value;

        Span<char> digits = stackalloc char[33];
        int position = digits.Length;

        while (magnitude > 0)
        {
            digits[--position] = "0123456789abcdefghijklmnopqrstuvwxyz"[(int)(magnitude % radix)];
            magnitude /= radix;
        }

        if (negative) digits[--position] = '-';
        return new string(digits[position..]);
    }

    /// <summary>Renders a long in a radix, matching <c>Long.toString(long, int)</c>.</summary>
    public static string LongToString(long value, int radix)
    {
        if (radix is < 2 or > 36) radix = 10;
        if (radix == 10) return value.ToString(CultureInfo.InvariantCulture);
        if (value == 0) return "0";

        bool negative = value < 0;
        ulong magnitude = negative ? (ulong)(-(value + 1)) + 1 : (ulong)value;

        Span<char> digits = stackalloc char[65];
        int position = digits.Length;

        while (magnitude > 0)
        {
            digits[--position] = "0123456789abcdefghijklmnopqrstuvwxyz"[(int)(magnitude % (ulong)radix)];
            magnitude /= (ulong)radix;
        }

        if (negative) digits[--position] = '-';
        return new string(digits[position..]);
    }

    /// <summary>Unsigned hexadecimal, as <c>Integer.toHexString</c> produces.</summary>
    public static string ToHexString(int value) => ((uint)value).ToString("x", CultureInfo.InvariantCulture);

    public static string ToHexString(long value) => ((ulong)value).ToString("x", CultureInfo.InvariantCulture);

    public static string ToOctalString(int value) => Convert.ToString((uint)value, 8);

    public static string ToBinaryString(int value) => Convert.ToString((uint)value, 2);
}
