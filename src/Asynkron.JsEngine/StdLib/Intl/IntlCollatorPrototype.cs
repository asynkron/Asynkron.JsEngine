#region

using System.Globalization;
using System.Text;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.Collator", ToStringTag = "Intl.Collator")]
public sealed partial class IntlCollatorPrototype
{
    private const string CollatorBrand = "__collator__";
    private const string SlotsKey = "__collatorSlots__";

    internal static void InitializeInternalSlots(JsObject instance, IntlCollatorInternalSlots slots)
    {
        instance.SetProperty(CollatorBrand, true);
        instance.SetProperty(SlotsKey, JsValue.FromObjectUnsafe(slots));
    }

    [JsHostGetter("compare", DisplayName = "get compare")]
    private JsValue GetCompare(JsValue thisValue)
    {
        var collator = ValidateCollatorReceiver(thisValue);
        var slots = GetSlots(collator);
        return (JsValue)slots.GetOrCreateBoundCompare(() => CreateBoundCompareFunction(slots));
    }

    private HostFunction CreateBoundCompareFunction(IntlCollatorInternalSlots slots)
    {
        var function = new HostFunction((_, args) =>
        {
            var first = JsValueToString(args.GetArgument(0), Realm);
            var second = JsValueToString(args.GetArgument(1), Realm);
            return new JsValue(CompareStrings(slots, first, second));
        }, Realm, false);
        DefineCompareFunctionMetadata(function);
        return function;
    }

    private static void DefineCompareFunctionMetadata(HostFunction function)
    {
        function.DefineProperty("length",
            new PropertyDescriptor { Value = 2d, Writable = false, Enumerable = false, Configurable = true });
        function.DefineProperty("name",
            new PropertyDescriptor { Value = string.Empty, Writable = false, Enumerable = false, Configurable = true });
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var collator = ValidateCollatorReceiver(thisValue);
        var slots = GetSlots(collator);
        var options = new JsObject(Realm.ObjectPrototype);
        options.SetProperty("locale", slots.Locale);
        options.SetProperty("usage", slots.Usage);
        options.SetProperty("sensitivity", slots.Sensitivity);
        options.SetProperty("ignorePunctuation", slots.IgnorePunctuation);
        options.SetProperty("collation", slots.Collation);
        options.SetProperty("numeric", slots.Numeric);
        options.SetProperty("caseFirst", slots.CaseFirst);
        return new JsValue(options);
    }

    private JsObject ValidateCollatorReceiver(JsValue thisValue)
    {
        return thisValue.EnsureBrand(CollatorBrand, Realm,
            "Intl.Collator method called on incompatible receiver");
    }

    private IntlCollatorInternalSlots GetSlots(JsObject collator)
    {
        if (collator.TryGetProperty(SlotsKey, out var value) &&
            value.TryGetObject<IntlCollatorInternalSlots>(out var slots))
        {
            return slots;
        }

        throw ThrowTypeError("Intl.Collator instance is missing internal slots", realm: Realm);
    }

    private static double CompareStrings(IntlCollatorInternalSlots slots, string first, string second)
    {
        var compareInfo = slots.CompareInfo ?? CultureInfo.InvariantCulture.CompareInfo;
        var options = BuildCompareOptions(slots);
        var (firstValue, secondValue) = NormalizeCompareInputs(slots, first, second);

        var result = slots.Numeric
            ? CompareWithNumeric(firstValue, secondValue, compareInfo, options)
            : compareInfo.Compare(firstValue, secondValue, options);

        if (result == 0 && !string.Equals(slots.CaseFirst, "false", StringComparison.Ordinal))
        {
            result = ApplyCaseFirst(slots.CaseFirst, firstValue, secondValue);
        }

        if (result == 0 && !slots.IgnorePunctuation)
        {
            result = BreakPunctuationTie(firstValue, secondValue);
        }

        if (result == 0)
        {
            return 0d;
        }

        return result < 0 ? -1d : 1d;
    }

    private static int BreakPunctuationTie(string first, string second)
    {
        if (string.Equals(first, second, StringComparison.Ordinal))
        {
            return 0;
        }

        var filteredFirst = RemoveIgnoredPunctuation(first);
        var filteredSecond = RemoveIgnoredPunctuation(second);
        if (!string.Equals(filteredFirst, filteredSecond, StringComparison.Ordinal))
        {
            return 0;
        }

        var ordinal = string.CompareOrdinal(first, second);
        if (ordinal == 0)
        {
            return 0;
        }

        return ordinal < 0 ? -1 : 1;
    }

    private static CompareOptions BuildCompareOptions(IntlCollatorInternalSlots slots)
    {
        var options = CompareOptions.None;
        if (slots.IgnorePunctuation)
        {
            options |= CompareOptions.IgnoreSymbols;
        }

        switch (slots.Sensitivity)
        {
            case "base":
                options |= CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreCase |
                           CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;
                break;
            case "accent":
                options |= CompareOptions.IgnoreCase;
                break;
            case "case":
                options |= CompareOptions.IgnoreNonSpace;
                break;
        }

        if (string.Equals(slots.Usage, "search", StringComparison.Ordinal))
        {
            options |= CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth | CompareOptions.StringSort;
        }

        return options;
    }

    private static int CompareWithNumeric(string first, string second, CompareInfo compareInfo,
        CompareOptions options)
    {
        var indexA = 0;
        var indexB = 0;
        while (indexA < first.Length && indexB < second.Length)
        {
            var charA = first[indexA];
            var charB = second[indexB];
            var digitA = char.IsDigit(charA);
            var digitB = char.IsDigit(charB);

            if (digitA && digitB)
            {
                var startA = indexA;
                var startB = indexB;
                while (indexA < first.Length && char.IsDigit(first[indexA]))
                {
                    indexA++;
                }

                while (indexB < second.Length && char.IsDigit(second[indexB]))
                {
                    indexB++;
                }

                var numericResult = CompareNumericStrings(first.AsSpan(startA, indexA - startA),
                    second.AsSpan(startB, indexB - startB));
                if (numericResult != 0)
                {
                    return numericResult;
                }

                continue;
            }

            if (digitA || digitB)
            {
                var fallback = compareInfo.Compare(first, indexA, 1, second, indexB, 1, options);
                if (fallback != 0)
                {
                    return fallback;
                }

                indexA++;
                indexB++;
                continue;
            }

            var nextDigitA = indexA;
            var nextDigitB = indexB;
            while (nextDigitA < first.Length && !char.IsDigit(first[nextDigitA]))
            {
                nextDigitA++;
            }

            while (nextDigitB < second.Length && !char.IsDigit(second[nextDigitB]))
            {
                nextDigitB++;
            }

            var compare = compareInfo.Compare(first, indexA, nextDigitA - indexA, second, indexB,
                nextDigitB - indexB, options);
            if (compare != 0)
            {
                return compare;
            }

            indexA = nextDigitA;
            indexB = nextDigitB;
        }

        if (indexA < first.Length)
        {
            return 1;
        }

        if (indexB < second.Length)
        {
            return -1;
        }

        return 0;
    }

    private static int CompareNumericStrings(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        var trimmedLeft = TrimLeadingZeros(left);
        var trimmedRight = TrimLeadingZeros(right);

        if (trimmedLeft.Length != trimmedRight.Length)
        {
            return trimmedLeft.Length < trimmedRight.Length ? -1 : 1;
        }

        for (var i = 0; i < trimmedLeft.Length; i++)
        {
            var diff = trimmedLeft[i].CompareTo(trimmedRight[i]);
            if (diff != 0)
            {
                return diff;
            }
        }

        if (left.Length != right.Length)
        {
            return left.Length < right.Length ? -1 : 1;
        }

        return 0;
    }

    private static ReadOnlySpan<char> TrimLeadingZeros(ReadOnlySpan<char> span)
    {
        var index = 0;
        while (index < span.Length && span[index] == '0')
        {
            index++;
        }

        return index >= span.Length ? ReadOnlySpan<char>.Empty : span[index..];
    }

    private static int ApplyCaseFirst(string casePreference, string first, string second)
    {
        var limit = Math.Min(first.Length, second.Length);
        for (var i = 0; i < limit; i++)
        {
            var left = first[i];
            var right = second[i];
            if (left == right)
            {
                continue;
            }

            if (char.ToUpperInvariant(left) != char.ToUpperInvariant(right) ||
                char.ToLowerInvariant(left) != char.ToLowerInvariant(right))
            {
                continue;
            }

            if (char.IsUpper(left) != char.IsUpper(right))
            {
                return string.Equals(casePreference, "upper", StringComparison.Ordinal)
                    ? char.IsUpper(left) ? -1 : 1
                    : char.IsUpper(left)
                        ? 1
                        : -1;
            }

            if (char.IsLower(left) != char.IsLower(right))
            {
                return string.Equals(casePreference, "upper", StringComparison.Ordinal)
                    ? char.IsLower(left) ? 1 : -1
                    : char.IsLower(left)
                        ? -1
                        : 1;
            }
        }

        return 0;
    }

    private static (string First, string Second) NormalizeCompareInputs(IntlCollatorInternalSlots slots,
        string first, string second)
    {
        first = NormalizeToNfc(first);
        second = NormalizeToNfc(second);

        if (slots.IgnorePunctuation)
        {
            first = RemoveIgnoredPunctuation(first);
            second = RemoveIgnoredPunctuation(second);
        }

        (first, second) = NormalizeSearchInputs(slots, first, second);
        (first, second) = NormalizePhonebookInputs(slots, first, second);

        return (first, second);
    }

    private static string NormalizeToNfc(string value)
    {
        if (string.IsNullOrEmpty(value) || ContainsUnpairedSurrogate(value))
        {
            return value;
        }

        return value.Normalize(NormalizationForm.FormC);
    }

    private static bool ContainsUnpairedSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                {
                    return true;
                }

                i++;
                continue;
            }

            if (char.IsLowSurrogate(ch))
            {
                return true;
            }
        }

        return false;
    }

    private static string RemoveIgnoredPunctuation(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var needsFilter = false;
        foreach (var ch in value)
        {
            if (ShouldIgnorePunctuation(ch))
            {
                needsFilter = true;
                break;
            }
        }

        if (!needsFilter)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!ShouldIgnorePunctuation(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static bool ShouldIgnorePunctuation(char ch)
        => char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch);

    private static (string First, string Second) NormalizeSearchInputs(IntlCollatorInternalSlots slots,
        string first, string second)
    {
        if (!string.Equals(slots.Usage, "search", StringComparison.Ordinal))
        {
            return (first, second);
        }

        var baseLocale = IntlUtilities.RemoveUnicodeExtensions(slots.Locale);
        var dashIndex = baseLocale.IndexOf('-', StringComparison.Ordinal);
        var language = dashIndex >= 0 ? baseLocale[..dashIndex] : baseLocale;
        if (string.Equals(language, "de", StringComparison.OrdinalIgnoreCase))
        {
            return (NormalizeGermanSearchString(first), NormalizeGermanSearchString(second));
        }

        return (first, second);
    }

    private static (string First, string Second) NormalizePhonebookInputs(IntlCollatorInternalSlots slots,
        string first, string second)
    {
        if (!string.Equals(slots.Collation, "phonebk", StringComparison.Ordinal))
        {
            return (first, second);
        }

        var baseLocale = IntlUtilities.RemoveUnicodeExtensions(slots.Locale);
        var dashIndex = baseLocale.IndexOf('-', StringComparison.Ordinal);
        var language = dashIndex >= 0 ? baseLocale[..dashIndex] : baseLocale;
        if (!string.Equals(language, "de", StringComparison.OrdinalIgnoreCase))
        {
            return (first, second);
        }

        return (NormalizeGermanSpecialCharacters(first), NormalizeGermanSpecialCharacters(second));
    }

    private static string NormalizeGermanSearchString(string value)
    {
        return NormalizeGermanSpecialCharacters(value);
    }

    private static string NormalizeGermanSpecialCharacters(string value)
    {
        var needsNormalization = false;
        foreach (var ch in value)
        {
            if (ch is 'Ä' or 'ä' or 'Ö' or 'ö' or 'Ü' or 'ü' or 'ß')
            {
                needsNormalization = true;
                break;
            }
        }

        if (!needsNormalization)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 4);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case 'Ä':
                    builder.Append("AE");
                    break;
                case 'ä':
                    builder.Append("ae");
                    break;
                case 'Ö':
                    builder.Append("OE");
                    break;
                case 'ö':
                    builder.Append("oe");
                    break;
                case 'Ü':
                    builder.Append("UE");
                    break;
                case 'ü':
                    builder.Append("ue");
                    break;
                case 'ß':
                    builder.Append("ss");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }
}
