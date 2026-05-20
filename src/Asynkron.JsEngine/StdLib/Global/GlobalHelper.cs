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
                if (reservedSet is not null && reservedSet.Contains(decoded))
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

        if (codePoint <= 0xFFFF)
        {
            sb.Append((char)codePoint);
            return;
        }

        codePoint -= 0x10000;
        sb.Append((char)((codePoint >> 10) + 0xD800));
        sb.Append((char)((codePoint & 0x3FF) + 0xDC00));
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
