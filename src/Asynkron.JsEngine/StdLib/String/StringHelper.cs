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
