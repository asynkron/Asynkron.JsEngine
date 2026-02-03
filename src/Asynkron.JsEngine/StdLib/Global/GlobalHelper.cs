#region

using System.Globalization;
using System.Text;
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
            radix = double.IsNaN(radixNum) ? 0 : (int)radixNum;
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
            if (char.IsDigit(c))
            {
                digit = c - '0';
            }
            else if (char.IsLetter(c))
            {
                var upper = char.ToUpperInvariant(c);
                digit = upper - 'A' + 10;
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
        str = str.Trim();
        if (str.Length == 0)
        {
            return JsValue.NaN;
        }

        // Try parsing the string as a double
        if (double.TryParse(str, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var result))
        {
            return new JsValue(result);
        }

        // JavaScript parseFloat allows partial parsing - parse as much as possible
        var i = 0;
        var hasDigits = false;

        // Handle sign
        if (i < str.Length && (str[i] == '+' || str[i] == '-'))
        {
            i++;
        }

        // Parse digits before decimal point
        while (i < str.Length && char.IsDigit(str[i]))
        {
            hasDigits = true;
            i++;
        }

        // Parse decimal point and digits after
        if (i < str.Length && str[i] == '.')
        {
            i++;
            while (i < str.Length && char.IsDigit(str[i]))
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
            while (j < str.Length && char.IsDigit(str[j]))
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
                CultureInfo.InvariantCulture, out result))
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

        // Convert to number first (this is what JavaScript does)
        if (value.TryGetDouble(out var d))
        {
            return new JsValue(!double.IsNaN(d) && !double.IsInfinity(d));
        }

        if (value.TryGetString(out var s))
        {
            if (double.TryParse(s, CultureInfo.InvariantCulture, out var parsed))
            {
                return new JsValue(!double.IsNaN(parsed) && !double.IsInfinity(parsed));
            }

            return JsValue.False; // Can't parse, so NaN, so not finite
        }

        // For other types, convert to number using ToNumber
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

    // Characters that are NOT decoded by decodeURI (uriReserved + '#')
    // uriReserved: ; / ? : @ & = + $ ,
    // Plus: #
    private static readonly HashSet<char> DecodeUriReserved =
    [
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
        return DecodeUri(str, DecodeUriReserved, realm);
    }

    [JsHostFunction("decodeURIComponent", Length = 1d, DeletePrototype = true)]
    private static JsValue DecodeURIComponent(IReadOnlyList<JsValue> args, RealmState realm)
    {
        var str = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? "" : "undefined";
        return DecodeUri(str, null, realm);
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

    private static JsValue DecodeUri(string str, HashSet<char>? reservedSet, RealmState realm)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < str.Length; i++)
        {
            var c = str[i];
            if (c != '%')
            {
                sb.Append(c);
                continue;
            }

            var start = i;
            if (i + 2 >= str.Length)
            {
                throw ThrowURIError("URI malformed", realm: realm);
            }

            var firstByte = ParsePercentEncodedByte(str, i, realm);
            var expectedBytes = GetUtf8SequenceLength(firstByte, realm);
            var bytes = new byte[expectedBytes];
            bytes[0] = firstByte;

            var nextIndex = i + 3;
            for (var byteIndex = 1; byteIndex < expectedBytes; byteIndex++)
            {
                if (nextIndex + 2 >= str.Length || str[nextIndex] != '%')
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                var continuationByte = ParsePercentEncodedByte(str, nextIndex, realm);
                if ((continuationByte & 0xC0) != 0x80)
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                bytes[byteIndex] = continuationByte;
                nextIndex += 3;
            }

            var raw = str.Substring(start, nextIndex - start);
            i = nextIndex - 1;

            var decoded = DecodeUtf8Sequence(bytes, realm);

            if (expectedBytes == 1 && reservedSet is not null && decoded.Length == 1 &&
                reservedSet.Contains(decoded[0]))
            {
                sb.Append(raw);
            }
            else
            {
                sb.Append(decoded);
            }
        }

        return sb.ToString();
    }

    private static byte ParsePercentEncodedByte(string str, int index, RealmState realm)
    {
        if (str[index] != '%' || index + 2 >= str.Length)
        {
            throw ThrowURIError("URI malformed", realm: realm);
        }

        var high = str[index + 1];
        var low = str[index + 2];
        if (!IsHexDigit(high) || !IsHexDigit(low) ||
            !byte.TryParse(str.Substring(index + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                out var value))
        {
            throw ThrowURIError("URI malformed", realm: realm);
        }

        return value;
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

    private static string DecodeUtf8Sequence(byte[] bytes, RealmState realm)
    {
        uint codePoint;
        switch (bytes.Length)
        {
            case 1:
                if (bytes[0] > 0x7F)
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                codePoint = bytes[0];
                break;
            case 2:
            {
                var first = bytes[0];
                if (first < 0xC2)
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                codePoint = (uint)(((first & 0x1F) << 6) | (bytes[1] & 0x3F));
                break;
            }
            case 3:
            {
                var first = bytes[0];
                var second = bytes[1];
                if (first == 0xE0 && second < 0xA0)
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                if (first == 0xED && second >= 0xA0)
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                codePoint = (uint)(((first & 0x0F) << 12) | ((second & 0x3F) << 6) | (bytes[2] & 0x3F));
                break;
            }
            case 4:
            {
                var first = bytes[0];
                var second = bytes[1];
                if (first == 0xF0 && second < 0x90)
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                if (first == 0xF4 && second > 0x8F)
                {
                    throw ThrowURIError("URI malformed", realm: realm);
                }

                codePoint = (uint)(((first & 0x07) << 18) | ((second & 0x3F) << 12) |
                                   ((bytes[2] & 0x3F) << 6) | (bytes[3] & 0x3F));
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

        return codePoint <= 0xFFFF
            ? ((char)codePoint).ToString()
            : char.ConvertFromUtf32((int)codePoint);
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

    /// <summary>
    /// Checks if a character is a valid hexadecimal digit (0-9, A-F, a-f).
    /// Used for strict URI decoding validation per ECMAScript spec.
    /// </summary>
    private static bool IsHexDigit(char c)
    {
        return c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';
    }

}
