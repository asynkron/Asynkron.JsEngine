using System.Globalization;
using System.Numerics;

namespace Asynkron.JsEngine.Runtime;

/// <summary>
/// Minimal ECMAScript-style parser for converting strings to numbers without
/// accepting culture-specific formats such as thousand separators.
/// </summary>
internal static class NumericStringParser
{
    private const NumberStyles DecimalStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    public static double ParseJsNumber(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0d;
        }

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return 0d;
        }

        if (trimmed == "NaN")
        {
            return double.NaN;
        }

        if (trimmed == "Infinity" || trimmed == "+Infinity")
        {
            return double.PositiveInfinity;
        }

        if (trimmed == "-Infinity")
        {
            return double.NegativeInfinity;
        }

        var span = trimmed.AsSpan();
        var isNegative = false;

        if (span.Length > 0 && (span[0] == '+' || span[0] == '-'))
        {
            isNegative = span[0] == '-';
            span = span[1..];
        }

        if (TryParsePrefixedInteger(span, 16, NumberStyles.AllowHexSpecifier, out var prefixed) ||
            TryParseBinary(span, out prefixed) ||
            TryParseOctal(span, out prefixed))
        {
            return (double)(isNegative ? -prefixed : prefixed);
        }

        return double.TryParse(trimmed, DecimalStyles, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : double.NaN;
    }

    private static bool TryParsePrefixedInteger(ReadOnlySpan<char> span, int radix, NumberStyles styles,
        out BigInteger value)
    {
        value = BigInteger.Zero;
        if (span.Length <= 2 || span[0] != '0')
        {
            return false;
        }

        var prefixChar = span[1];
        var expected = radix switch
        {
            16 => ('x', 'X'),
            _ => ('?', '?')
        };

        if (prefixChar != expected.Item1 && prefixChar != expected.Item2)
        {
            return false;
        }

        var digits = span[2..];
        return digits.Length > 0 &&
               BigInteger.TryParse(digits, styles, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseBinary(ReadOnlySpan<char> span, out BigInteger value)
    {
        value = BigInteger.Zero;
        if (span.Length <= 2 || span[0] != '0' || span[1] is not ('b' or 'B'))
        {
            return false;
        }

        var digits = span[2..];
        if (digits.Length == 0)
        {
            return false;
        }

        foreach (var ch in digits)
        {
            value <<= 1;
            switch (ch)
            {
                case '0':
                    break;
                case '1':
                    value += BigInteger.One;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool TryParseOctal(ReadOnlySpan<char> span, out BigInteger value)
    {
        value = BigInteger.Zero;
        if (span.Length <= 2 || span[0] != '0' || span[1] is not ('o' or 'O'))
        {
            return false;
        }

        var digits = span[2..];
        if (digits.Length == 0)
        {
            return false;
        }

        foreach (var ch in digits)
        {
            if (ch is < '0' or > '7')
            {
                return false;
            }

            value = (value << 3) + (ch - '0');
        }

        return true;
    }
}
