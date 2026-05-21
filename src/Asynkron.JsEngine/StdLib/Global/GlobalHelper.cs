#region

using System.Globalization;
using System.Text;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static partial class GlobalHelper
{
    [JsHostFunction("parseInt", Length = 2d, DeletePrototype = true)]
    private static JsValue ParseInt(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.NaN;
        }

        var str = JsOps.ToJsString(args[0]) ?? "";
        str = str.Trim();
        if (str.Length == 0)
        {
            return JsValue.NaN;
        }

        // Handle sign first (before hex prefix detection)
        var sign = 1;
        if (str.StartsWith('-'))
        {
            sign = -1;
            str = str[1..];
        }
        else if (str.StartsWith('+'))
        {
            str = str[1..];
        }

        // Get radix - undefined means 0 (auto-detect)
        int radix;
        if (args.Count > 1 && !args[1].IsUndefined)
        {
            var radixNum = JsOps.ToNumber(args[1]);
            radix = double.IsNaN(radixNum) ? 0 : JsNumericConversions.ToInt32(radixNum);
        }
        else
        {
            radix = 0; // Auto-detect
        }

        // Handle radix 0 (auto-detect) or explicit radix 16 with hex prefix
        var stripPrefix = false;
        if (radix == 0)
        {
            if (str.Length >= 2 && str[0] == '0' && (str[1] == 'x' || str[1] == 'X'))
            {
                radix = 16;
                stripPrefix = true;
            }
            else
            {
                radix = 10;
            }
        }
        else if (radix == 16)
        {
            // For radix 16, optionally strip "0x" or "0X" prefix
            if (str.Length >= 2 && str[0] == '0' && (str[1] == 'x' || str[1] == 'X'))
            {
                stripPrefix = true;
            }
        }

        // Validate radix
        if (radix is < 2 or > 36)
        {
            return JsValue.NaN;
        }

        // Strip hex prefix if needed
        if (stripPrefix)
        {
            str = str[2..];
        }

        if (str.Length == 0)
        {
            return JsValue.NaN;
        }

        // Parse until we hit invalid character
        double result = 0;
        var hasDigits = false;
        foreach (var c in str)
        {
            int digit;
            if (c is >= '0' and <= '9')
            {
                digit = c - '0';
            }
            else if (c is >= 'A' and <= 'Z')
            {
                digit = c - 'A' + 10;
            }
            else if (c is >= 'a' and <= 'z')
            {
                digit = c - 'a' + 10;
            }
            else
            {
                break; // Stop at first invalid character
            }

            if (digit >= radix)
            {
                break;
            }

            result = result * radix + digit;
            hasDigits = true;
        }

        return hasDigits ? new JsValue(result * sign) : JsValue.NaN;
    }

    /// <summary>
    ///     Creates the global parseFloat function.
    /// </summary>
    [JsHostFunction("parseFloat", Length = 1d, DeletePrototype = true)]
    private static JsValue ParseFloat(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.NaN;
        }

        var str = JsOps.ToJsString(args[0]) ?? "";
        str = str.TrimStart();
        if (str.Length == 0)
        {
            return JsValue.NaN;
        }

        var index = 0;
        if (str[index] == '+' || str[index] == '-')
        {
            index++;
        }

        if (index < str.Length && str.AsSpan(index).StartsWith("Infinity", StringComparison.Ordinal))
        {
            return new JsValue(str[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity);
        }

        // JavaScript parseFloat allows partial parsing - parse the longest StrDecimalLiteral prefix
        var i = index;
        var hasDigits = false;

        // Parse digits before decimal point
        while (i < str.Length && str[i] is >= '0' and <= '9')
        {
            hasDigits = true;
            i++;
        }

        // Parse decimal point and digits after
        if (i < str.Length && str[i] == '.')
        {
            i++;
            while (i < str.Length && str[i] is >= '0' and <= '9')
            {
                hasDigits = true;
                i++;
            }
        }

        // Parse exponent
        if (i < str.Length && (str[i] == 'e' || str[i] == 'E'))
        {
            var j = i + 1;
            if (j < str.Length && (str[j] == '+' || str[j] == '-'))
            {
                j++;
            }

            var hasExpDigits = false;
            while (j < str.Length && str[j] is >= '0' and <= '9')
            {
                hasExpDigits = true;
                j++;
            }

            if (hasExpDigits)
            {
                i = j;
            }
        }

        if (!hasDigits)
        {
            return JsValue.NaN;
        }

        var parsed = str[..i];
        if (double.TryParse(parsed, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var result))
        {
            return new JsValue(result);
        }

        return JsValue.NaN;
    }

    /// <summary>
    ///     Creates the global isNaN function.
    /// </summary>
    [JsHostFunction("isNaN", Length = 1d)]
    private static JsValue IsNaN(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.True;
        }

        var value = args[0];

        // Per ECMAScript spec, isNaN must first convert the argument to Number using ToNumber
        var numericValue = JsOps.ToNumber(value);
        return new JsValue(double.IsNaN(numericValue));
    }

    [JsHostFunction("isFinite", Length = 1d)]
    private static JsValue IsFinite(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.False;
        }

        var value = args[0];
        var numericValue = JsOps.ToNumber(value);
        return new JsValue(!double.IsNaN(numericValue) && !double.IsInfinity(numericValue));
    }

    // Characters that are NOT encoded by encodeURI (uriReserved + uriUnescaped + '#')
    // uriReserved: ; / ? : @ & = + $ ,
    // uriUnescaped: A-Z a-z 0-9 - _ . ! ~ * ' ( )
    // Plus: #
    private static readonly HashSet<char> EncodeUriUnescaped =
    [
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
        'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
        'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
        '-', '_', '.', '!', '~', '*', '\'', '(', ')',
        ';', '/', '?', ':', '@', '&', '=', '+', '$', ',', '#'
    ];

    // Characters that are NOT encoded by encodeURIComponent (uriUnescaped only)
    // uriUnescaped: A-Z a-z 0-9 - _ . ! ~ * ' ( )
    private static readonly HashSet<char> EncodeUriComponentUnescaped =
    [
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
        'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
        'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
        '-', '_', '.', '!', '~', '*', '\'', '(', ')'
    ];

    [JsHostFunction("encodeURI", Length = 1d, DeletePrototype = true)]
    private static JsValue EncodeURI(IReadOnlyList<JsValue> args, RealmState realm)
    {
        var str = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? "" : "undefined";
        return EncodeUri(str, EncodeUriUnescaped, realm);
    }

    [JsHostFunction("encodeURIComponent", Length = 1d, DeletePrototype = true)]
    private static JsValue EncodeURIComponent(IReadOnlyList<JsValue> args, RealmState realm)
    {
        var str = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? "" : "undefined";
        return EncodeUri(str, EncodeUriComponentUnescaped, realm);
    }

    [JsHostFunction("decodeURI", Length = 1d, DeletePrototype = true)]
    private static JsValue DecodeURI(IReadOnlyList<JsValue> args, RealmState realm)
    {
        var str = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? "" : "undefined";
        return DecodeUri(str, preserveReservedEscapes: true, realm);
    }

    [JsHostFunction("decodeURIComponent", Length = 1d, DeletePrototype = true)]
    private static JsValue DecodeURIComponent(IReadOnlyList<JsValue> args, RealmState realm)
    {
        var str = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? "" : "undefined";
        return DecodeUri(str, preserveReservedEscapes: false, realm);
    }

    private static JsValue EncodeUri(string str, HashSet<char> unescapedSet, RealmState realm)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < str.Length; i++)
        {
            var c = str[i];

            if (unescapedSet.Contains(c))
            {
                sb.Append(c);
                continue;
            }

            // Handle surrogate pairs
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= str.Length || !char.IsLowSurrogate(str[i + 1]))
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                var codePoint = char.ConvertToUtf32(c, str[i + 1]);
                var bytes = Encoding.UTF8.GetBytes(char.ConvertFromUtf32(codePoint));
                foreach (var b in bytes)
                {
                    sb.Append('%');
                    sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }

                i++; // Skip the low surrogate
                continue;
            }

            if (char.IsLowSurrogate(c))
            {
                throw ThrowURIError("URI malformed", realm: realm);
            }

            // Encode the character as UTF-8 bytes
            var charBytes = Encoding.UTF8.GetBytes(c.ToString());
            foreach (var b in charBytes)
            {
                sb.Append('%');
                sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static JsValue DecodeUri(string str, bool preserveReservedEscapes, RealmState realm)
    {
        if (TryDecodeSinglePercentEncodedScalar(str, preserveReservedEscapes, realm, out var fastDecoded))
        {
            return fastDecoded;
        }

        var sb = new StringBuilder(str.Length);
        for (var i = 0; i < str.Length; i++)
        {
            var c = str[i];
            if (c != '%')
            {
                sb.Append(c);
                continue;
            }

            var start = i;
            var firstByte = ParsePercentEncodedByte(str, i, realm);
            var expectedBytes = GetUtf8SequenceLength(firstByte, realm);

            var nextIndex = i + 3;
            if (expectedBytes == 1)
            {
                var decoded = (char)firstByte;
                if (preserveReservedEscapes && IsDecodeUriReserved(decoded))
                {
                    sb.Append(str, start, nextIndex - start);
                }
                else
                {
                    sb.Append(decoded);
                }

                i = nextIndex - 1;
                continue;
            }

            var secondByte = ParseContinuationByte(str, nextIndex, realm);
            nextIndex += 3;
            var thirdByte = expectedBytes >= 3 ? ParseContinuationByte(str, nextIndex, realm) : (byte)0;
            if (expectedBytes >= 3)
            {
                nextIndex += 3;
            }

            var fourthByte = expectedBytes == 4 ? ParseContinuationByte(str, nextIndex, realm) : (byte)0;
            if (expectedBytes == 4)
            {
                nextIndex += 3;
            }

            AppendDecodedUtf8Sequence(sb, expectedBytes, firstByte, secondByte, thirdByte, fourthByte, realm);
            i = nextIndex - 1;
        }

        return sb.ToString();
    }

    private static bool TryDecodeSinglePercentEncodedScalar(
        string str,
        bool preserveReservedEscapes,
        RealmState realm,
        out string decoded)
    {
        decoded = string.Empty;

        if (str.Length is not (3 or 6 or 9 or 12) || str[0] != '%')
        {
            return false;
        }

        var byteCount = str.Length / 3;
        Span<byte> bytes = stackalloc byte[4];
        for (var i = 0; i < byteCount; i++)
        {
            var index = i * 3;
            if (str[index] != '%')
            {
                return false;
            }

            if (!TryParsePercentEncodedByte(str[index + 1], str[index + 2], out var parsed))
            {
                throw ThrowURIError("URI malformed", realm: realm);
            }

            bytes[i] = parsed;
        }

        if (byteCount == 1)
        {
            var singleByte = bytes[0];
            if (GetUtf8SequenceLength(singleByte, realm) != 1)
            {
                throw ThrowURIError("URI malformed", realm: realm);
            }

            var decodedChar = (char)singleByte;
            decoded = preserveReservedEscapes && IsDecodeUriReserved(decodedChar)
                ? str
                : decodedChar.ToString();
            return true;
        }

        var firstByte = bytes[0];
        var expectedBytes = GetUtf8SequenceLength(firstByte, realm);
        if (expectedBytes != byteCount)
        {
            return false;
        }

        var secondByte = bytes[1];
        var thirdByte = byteCount >= 3 ? bytes[2] : (byte)0;
        var fourthByte = byteCount == 4 ? bytes[3] : (byte)0;

        if (!IsContinuationByte(secondByte) || (byteCount >= 3 && !IsContinuationByte(thirdByte)) ||
            (byteCount == 4 && !IsContinuationByte(fourthByte)))
        {
            return false;
        }

        var codePoint = DecodeUtf8CodePoint(expectedBytes, firstByte, secondByte, thirdByte, fourthByte, realm);
        decoded = CreateStringFromCodePoint(codePoint);
        return true;
    }

    private static byte ParsePercentEncodedByte(string str, int index, RealmState realm)
    {
        if (index + 2 >= str.Length || str[index] != '%')
        {
            throw ThrowURIError("URI malformed", realm: realm);
        }

        var high = str[index + 1];
        var low = str[index + 2];
        var highNibble = HexValue(high);
        var lowNibble = HexValue(low);
        if (highNibble < 0 || lowNibble < 0)
        {
            throw ThrowURIError("URI malformed", realm: realm);
        }

        return (byte)((highNibble << 4) | lowNibble);
    }

    private static byte ParseContinuationByte(string str, int index, RealmState realm)
    {
        var continuationByte = ParsePercentEncodedByte(str, index, realm);
        if ((continuationByte & 0xC0) != 0x80)
        {
            throw ThrowURIError("URI malformed", realm: realm);
        }

        return continuationByte;
    }

    private static int GetUtf8SequenceLength(byte firstByte, RealmState realm)
    {
        if ((firstByte & 0x80) == 0)
        {
            return 1;
        }

        if ((firstByte & 0xE0) == 0xC0)
        {
            return 2;
        }

        if ((firstByte & 0xF0) == 0xE0)
        {
            return 3;
        }

        if ((firstByte & 0xF8) == 0xF0)
        {
            if (firstByte > 0xF4)
            {
                throw ThrowURIError("URI malformed", realm: realm);
            }
            return 4;
        }

        throw ThrowURIError("URI malformed", realm: realm);
    }

    private static void AppendDecodedUtf8Sequence(
        StringBuilder sb,
        int length,
        byte first,
        byte second,
        byte third,
        byte fourth,
        RealmState realm)
    {
        var codePoint = DecodeUtf8CodePoint(length, first, second, third, fourth, realm);
        AppendCodePoint(sb, codePoint);
    }

    private static uint DecodeUtf8CodePoint(int length, byte first, byte second, byte third, byte fourth, RealmState realm)
    {
        uint codePoint;
        switch (length)
        {
            case 2:
            {
                if (first < 0xC2)
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                codePoint = (uint)(((first & 0x1F) << 6) | (second & 0x3F));
                break;
            }
            case 3:
            {
                if (first == 0xE0 && second < 0xA0)
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                if (first == 0xED && second >= 0xA0)
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                codePoint = (uint)(((first & 0x0F) << 12) | ((second & 0x3F) << 6) | (third & 0x3F));
                break;
            }
            case 4:
            {
                if (first == 0xF0 && second < 0x90)
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                if (first == 0xF4 && second > 0x8F)
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                codePoint = (uint)(((first & 0x07) << 18) | ((second & 0x3F) << 12) |
                                   ((third & 0x3F) << 6) | (fourth & 0x3F));
                break;
            }
            default:
                throw ThrowURIError("URI malformed", realm: realm);
        }

        if (codePoint is >= 0xD800 and <= 0xDFFF)
        {
            throw ThrowURIError("URI malformed", realm: realm);
        }

        if (codePoint > 0x10FFFF)
        {
            throw ThrowURIError("URI malformed", realm: realm);
        }

        return codePoint;
    }

    private static void AppendCodePoint(StringBuilder sb, uint codePoint)
    {
        if (codePoint <= 0xFFFF)
        {
            sb.Append((char)codePoint);
            return;
        }

        codePoint -= 0x10000;
        sb.Append((char)((codePoint >> 10) + 0xD800));
        sb.Append((char)((codePoint & 0x3FF) + 0xDC00));
    }

    private static string CreateStringFromCodePoint(uint codePoint)
    {
        if (codePoint <= 0xFFFF)
        {
            return ((char)codePoint).ToString();
        }

        Span<char> chars = stackalloc char[2];
        codePoint -= 0x10000;
        chars[0] = (char)((codePoint >> 10) + 0xD800);
        chars[1] = (char)((codePoint & 0x3FF) + 0xDC00);
        return new string(chars);
    }

    private static bool IsDecodeUriReserved(char c)
    {
        return c is ';' or '/' or '?' or ':' or '@' or '&' or '=' or '+' or '$' or ',' or '#';
    }

    private static bool IsContinuationByte(byte value) => (value & 0xC0) == 0x80;

    private static bool TryParsePercentEncodedByte(char high, char low, out byte value)
    {
        var highNibble = HexValue(high);
        var lowNibble = HexValue(low);
        if (highNibble < 0 || lowNibble < 0)
        {
            value = 0;
            return false;
        }

        value = (byte)((highNibble << 4) | lowNibble);
        return true;
    }

    // Characters that are NOT escaped by the legacy escape() function
    // A-Z a-z 0-9 @ * _ + - . /
    private static readonly HashSet<char> EscapeUnescaped =
    [
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
        'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
        'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
        '@', '*', '_', '+', '-', '.', '/'
    ];

    [JsHostFunction("escape", Length = 1d, DeletePrototype = true)]
    private static JsValue Escape(IReadOnlyList<JsValue> args, EvaluationContext? context)
    {
        var str = args.Count > 0 ? JsOps.ToJsString(args[0], context) : "undefined";
        return new JsValue(EscapeString(str));
    }

    private static string EscapeString(string str)
    {
        var sb = new StringBuilder();

        foreach (var c in str)
        {
            if (EscapeUnescaped.Contains(c))
            {
                sb.Append(c);
            }
            else if (c < 256)
            {
                sb.Append('%');
                sb.Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append("%u");
                sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    [JsHostFunction("unescape", Length = 1d, DeletePrototype = true)]
    private static JsValue Unescape(IReadOnlyList<JsValue> args, EvaluationContext? context)
    {
        var str = args.Count > 0 ? JsOps.ToJsString(args[0], context) : "undefined";
        var sb = new StringBuilder();

        for (var i = 0; i < str.Length; i++)
        {
            var c = str[i];
            if (c == '%')
            {
                // Check for %uXXXX format
                if (i + 5 < str.Length && str[i + 1] == 'u')
                {
                    var hex = str.Substring(i + 2, 4);
                    if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var charCode))
                    {
                        sb.Append((char)charCode);
                        i += 5;
                        continue;
                    }
                }
                // Check for %XX format
                else if (i + 2 < str.Length)
                {
                    var hex = str.Substring(i + 1, 2);
                    if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var charCode))
                    {
                        sb.Append((char)charCode);
                        i += 2;
                        continue;
                    }
                }
            }

            // If we get here, it wasn't a valid escape sequence, so just append the character
            sb.Append(c);
        }

        return new JsValue(sb.ToString());
    }

    private static int HexValue(char c)
    {
        if (c is >= '0' and <= '9')
        {
            return c - '0';
        }

        if (c is >= 'A' and <= 'F')
        {
            return c - 'A' + 10;
        }

        if (c is >= 'a' and <= 'f')
        {
            return c - 'a' + 10;
        }

        return -1;
    }

}
