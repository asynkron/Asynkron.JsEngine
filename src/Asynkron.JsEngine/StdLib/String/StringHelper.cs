#region

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class StringHelper
{
    /// <summary>
    /// Reads a Unicode code point from the string at the given index, handling surrogate pairs.
    /// Advances the index past the code point (by 1 for BMP, by 2 for surrogate pairs).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static string ReadCodePoint(string str, ref int index)
    {
        var ch = str[index];
        if (char.IsHighSurrogate(ch) && index + 1 < str.Length && char.IsLowSurrogate(str[index + 1]))
        {
            var result = str.Substring(index, 2);
            index += 2;
            return result;
        }

        index++;
        return ch.ToString();
    }

    internal static JsObject InitializeStringWrapper(string str, JsObject wrapper, RealmState? realm = null)
    {
        wrapper.SetProperty("__value__", str);

        wrapper.DefineProperty("length",
            new PropertyDescriptor
            {
                Value = (double)str.Length,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });
        wrapper.SetVirtualPropertyProvider(new StringVirtualPropertyProvider(str));
        wrapper.RealmState ??= realm;
        return wrapper;
    }

    internal static string RequireStringReceiver(JsValue receiver, RealmState? realm = null)
    {
        // Fast path for string kind
        if (receiver.Kind == JsValueKind.String)
        {
            return receiver.ObjectValue as string ?? string.Empty;
        }

        // For objects, check for __value__ property
        if (receiver is { Kind: JsValueKind.Object, ObjectValue: IJsPropertyAccessor accessor })
        {
            if (accessor.TryGetProperty("__value__", out var inner) && inner.TryGetString(out var s))
            {
                return s;
            }
        }

        throw ThrowTypeError("String.prototype valueOf called on non-string object", realm: realm);
    }

    internal static string ToEcmaUpperCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        StringBuilder? builder = null;
        foreach (var rune in value.EnumerateRunes())
        {
            var mapped = StringUnicodeCaseMappings.TryGetSpecialUppercase(rune, out var specialUppercase)
                ? specialUppercase
                : rune.ToString().ToUpperInvariant();
            builder ??= new StringBuilder(value.Length);
            builder.Append(mapped);
        }

        return builder?.ToString() ?? value;
    }

    internal static string ToEcmaLowerCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var runes = value.EnumerateRunes().ToArray();
        StringBuilder? builder = null;
        for (var index = 0; index < runes.Length; index++)
        {
            var rune = runes[index];
            string mapped;
            if (rune.Value == 0x03A3 && IsFinalSigmaContext(runes, index))
            {
                mapped = "\u03C2";
            }
            else if (StringUnicodeCaseMappings.TryGetSpecialLowercase(rune, out var specialLowercase))
            {
                mapped = specialLowercase;
            }
            else
            {
                mapped = rune.ToString().ToLowerInvariant();
            }

            builder ??= new StringBuilder(value.Length);
            builder.Append(mapped);
        }

        return builder?.ToString() ?? value;
    }

    internal static string ToEcmaLocaleLowerCase(string value, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var lang = culture.TwoLetterISOLanguageName;

        if (string.Equals(lang, "tr", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(lang, "az", StringComparison.OrdinalIgnoreCase))
        {
            return ToLowerTurkishAzeri(value, culture);
        }

        if (string.Equals(lang, "lt", StringComparison.OrdinalIgnoreCase))
        {
            return ToLowerLithuanian(value, culture);
        }

        return value.ToLower(culture);
    }

    /// <summary>
    /// Turkish/Azerbaijani special lowercasing per Unicode SpecialCasing.txt:
    /// İ (U+0130) → i; I + [below combiners] + U+0307 → i + [below combiners] (dot removed); I → ı (U+0131)
    /// </summary>
    private static string ToLowerTurkishAzeri(string value, CultureInfo culture)
    {
        var runes = value.EnumerateRunes().ToArray();
        var sb = new StringBuilder(value.Length);
        var i = 0;
        while (i < runes.Length)
        {
            var rune = runes[i];

            // İ (U+0130) → i
            if (rune.Value == 0x0130)
            {
                sb.Append('i');
                i++;
                continue;
            }

            // I → check if Before_Dot (followed by U+0307 after optional below-class combiners)
            if (rune.Value == 'I')
            {
                var dotIndex = FindDotAboveAfter(runes, i + 1);
                if (dotIndex >= 0)
                {
                    // I → i, skip the \u0307 at dotIndex, preserve intermediate combiners
                    sb.Append('i');
                    for (var k = i + 1; k < dotIndex; k++)
                    {
                        sb.Append(runes[k]);
                    }

                    i = dotIndex + 1; // skip past the dot above
                }
                else
                {
                    // I → ı (Not_Before_Dot)
                    sb.Append('\u0131');
                    i++;
                }

                continue;
            }

            // All other characters: normal culture-aware lowercasing
            sb.Append(rune.ToString().ToLower(culture));
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Scan forward from startIndex looking for U+0307 (COMBINING DOT ABOVE),
    /// skipping combining marks with CCC != 0 and CCC != 230 (i.e., "below" class like CCC 220).
    /// Returns the index of U+0307 if found, or -1.
    /// </summary>
    private static int FindDotAboveAfter(Rune[] runes, int startIndex)
    {
        for (var j = startIndex; j < runes.Length; j++)
        {
            if (runes[j].Value == 0x0307)
            {
                return j;
            }

            // CCC 0 (base char) or CCC 230 (above) → stop searching
            if (!IsCombiningMark(runes[j]) || IsAboveCombiningMark(runes[j]))
            {
                return -1;
            }

            // CCC != 0 and != 230 (e.g., 220 below) → skip
        }

        return -1;
    }

    /// <summary>
    /// Lithuanian special lowercasing per Unicode SpecialCasing.txt:
    /// I/J/Į + More_Above → lowercase + U+0307; precomposed Ì→i̇̀, Í→i̇́, Ĩ→i̇̃
    /// </summary>
    private static string ToLowerLithuanian(string value, CultureInfo culture)
    {
        var runes = value.EnumerateRunes().ToArray();
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < runes.Length; i++)
        {
            var rune = runes[i];

            // Precomposed characters (unconditional Lithuanian mappings)
            switch (rune.Value)
            {
                case 0x00CC: // Ì → i + U+0307 + U+0300
                    sb.Append("i\u0307\u0300");
                    continue;
                case 0x00CD: // Í → i + U+0307 + U+0301
                    sb.Append("i\u0307\u0301");
                    continue;
                case 0x0128: // Ĩ → i + U+0307 + U+0303
                    sb.Append("i\u0307\u0303");
                    continue;
            }

            // I, J, Į with More_Above condition
            if (rune.Value is 'I' or 'J' or 0x012E)
            {
                var moreAbove = HasMoreAbove(runes, i + 1);
                var lowerChar = rune.Value switch
                {
                    'I' => "i",
                    'J' => "j",
                    0x012E => "\u012F", // į
                    _ => rune.ToString().ToLower(culture)
                };
                sb.Append(lowerChar);
                if (moreAbove)
                {
                    sb.Append('\u0307');
                }

                continue;
            }

            sb.Append(rune.ToString().ToLower(culture));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Check More_Above condition: there is a combining mark with CCC 230 (Above)
    /// following, possibly after combining marks with 0 &lt; CCC &lt; 230.
    /// </summary>
    private static bool HasMoreAbove(Rune[] runes, int startIndex)
    {
        for (var j = startIndex; j < runes.Length; j++)
        {
            if (!IsCombiningMark(runes[j]))
            {
                return false; // CCC 0 → stop
            }

            if (IsAboveCombiningMark(runes[j]))
            {
                return true; // CCC 230 → More_Above
            }

            // Other combining classes (e.g., 220) → skip
        }

        return false;
    }

    /// <summary>
    /// Returns true if the rune is a combining mark with Canonical Combining Class 230 (Above).
    /// </summary>
    private static bool IsAboveCombiningMark(Rune rune)
    {
        if (!IsCombiningMark(rune))
        {
            return false;
        }

        var cp = rune.Value;

        // Combining Diacritical Marks (0x0300-0x036F)
        if (cp is >= 0x0300 and <= 0x036F)
        {
            return cp is (>= 0x0300 and <= 0x0314)
                or (>= 0x033D and <= 0x0344)
                or 0x0346
                or (>= 0x034A and <= 0x034C)
                or (>= 0x0350 and <= 0x0352)
                or 0x0357
                or (>= 0x0363 and <= 0x036F);
        }

        // Combining Diacritical Marks Extended (0x1AB0-0x1AFF) - most are above
        if (cp is >= 0x1AB0 and <= 0x1ABE)
        {
            return true;
        }

        // Combining Diacritical Marks Supplement (0x1DC0-0x1DFF)
        if (cp is >= 0x1DC0 and <= 0x1DFF)
        {
            // Most are 230; a few exceptions exist but default to above
            return cp is not (0x1DCA or 0x1DCB or 0x1DCC);
        }

        // Musical Symbols combining marks (CCC 230)
        if (cp is >= 0x1D185 and <= 0x1D189)
        {
            return true;
        }

        // Default for unknown combining marks: not above (conservative)
        return false;
    }

    internal static string ToEcmaLocaleUpperCase(string value, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (!string.Equals(culture.TwoLetterISOLanguageName, "lt", StringComparison.OrdinalIgnoreCase))
        {
            return value.ToUpper(culture);
        }

        var runes = value.EnumerateRunes().ToArray();
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < runes.Length; index++)
        {
            var rune = runes[index];
            if (rune.Value == 0x0307 && HasSoftDottedBaseBefore(runes, index))
            {
                continue;
            }

            builder.Append(rune.ToString().ToUpper(culture));
        }

        return builder.ToString();
    }

    private static bool IsFinalSigmaContext(Rune[] runes, int sigmaIndex)
    {
        for (var index = sigmaIndex - 1; index >= 0; index--)
        {
            if (IsCased(runes[index]))
            {
                for (var lookahead = sigmaIndex + 1; lookahead < runes.Length; lookahead++)
                {
                    if (IsCased(runes[lookahead]))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        return false;
    }

    private static bool IsCased(Rune rune)
    {
        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.UppercaseLetter => true,
            UnicodeCategory.LowercaseLetter => true,
            UnicodeCategory.TitlecaseLetter => true,
            _ => false
        };
    }

    private static bool HasSoftDottedBaseBefore(Rune[] runes, int index)
    {
        for (var current = index - 1; current >= 0; current--)
        {
            var rune = runes[current];
            if (IsCombiningMark(rune))
            {
                continue;
            }

            return StringUnicodeCaseMappings.IsSoftDotted(rune);
        }

        return false;
    }

    private static bool IsCombiningMark(Rune rune)
    {
        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.NonSpacingMark => true,
            UnicodeCategory.SpacingCombiningMark => true,
            UnicodeCategory.EnclosingMark => true,
            _ => false
        };
    }

    /// <summary>
    ///     Creates a string wrapper object with string methods attached.
    ///     This allows string primitives to have methods like toLowerCase(), substring(), etc.
    /// </summary>
    public static JsObject CreateStringWrapper(string str, EvaluationContext? context = null, RealmState? realm = null)
    {
        var stringObj = InitializeStringWrapper(str, new JsObject(), realm);

        var realmState = realm ?? context?.RealmState;
        var prototype = realmState?.StringPrototype;
        if (prototype is not null)
        {
            stringObj.SetPrototype(prototype);
        }

        return stringObj;
    }

    /// <summary>
    ///     Creates the String constructor with static methods.
    /// </summary>
    private sealed class StringVirtualPropertyProvider(string value) : IVirtualPropertyProvider
    {
        public bool TryGetOwnProperty(string name, out object? valueOut, out PropertyDescriptor? descriptor)
        {
            valueOut = null;
            descriptor = null;

            if (!IsArrayIndex(name, out var index) || index < 0 || index >= value.Length)
            {
                return false;
            }

            var ch = value[index].ToString();
            valueOut = ch;
            descriptor = new PropertyDescriptor
            {
                Value = ch,
                Writable = false,
                Enumerable = true,
                Configurable = false
            };
            return true;
        }

        public IEnumerable<string> GetEnumerableKeys()
        {
            for (var i = 0; i < value.Length; i++)
            {
                yield return i.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static bool IsArrayIndex(string key, out int index)
        {
            return int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0;
        }
    }
}
