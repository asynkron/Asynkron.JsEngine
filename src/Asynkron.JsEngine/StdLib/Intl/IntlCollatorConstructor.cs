#region

using System.Linq;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.IntlHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.Collator", PrototypeType = typeof(IntlCollatorPrototype), Length = 0d, DisplayName = "Collator")]
public sealed partial class IntlCollatorConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private static readonly HashSet<string> SupportedCollations = new(StringComparer.Ordinal)
    {
        "default",
        "phonebk",
        "stroke",
        "compat",
        "dict",
        "ducet",
        "eor",
        "gb2312",
        "pinyin",
        "reformed",
        "traditional",
        "unihan",
        "zhuyin",
        "emoji"
    };
    private static readonly IReadOnlyList<string> SupportedValues =
        SupportedCollations
            .Where(static x => !string.Equals(x, "default", StringComparison.Ordinal))
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();

    internal static IReadOnlyList<string> GetSupportedValues() => SupportedValues;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var slots = CreateInternalSlots(args.GetArgument(0), args.GetArgument(1));
        var instance = PrepareThisObject(thisValue);
        IntlCollatorPrototype.InitializeInternalSlots(instance, slots);
        return new JsValue(instance);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        IntlHelper.ConfigureSupportedLocalesOf(constructor, Realm);
    }

    private IntlCollatorInternalSlots CreateInternalSlots(JsValue localesArg, JsValue optionsArg)
    {
        var (_, resolvedLocale) = ResolveIntlLocales(localesArg, Realm);

        var options = IntlOptionHelpers.GetOptionsObject(optionsArg, Realm, "Collator");
        var usage = IntlOptionHelpers.GetStringOption(options, "usage", Realm, "Collator",
            ["sort", "search"], "sort");
        var sensitivityDefault = string.Equals(usage, "search", StringComparison.Ordinal)
            ? "base"
            : "variant";
        var sensitivity = IntlOptionHelpers.GetStringOption(options, "sensitivity", Realm, "Collator",
            ["base", "accent", "case", "variant"], sensitivityDefault);
        var localeMatcher = IntlOptionHelpers.GetStringOption(options, "localeMatcher", Realm, "Collator",
            ["lookup", "best fit"], "best fit");

        resolvedLocale = CanonicalizeOrFallbackLocale(resolvedLocale);

        var baseLocale = IntlUtilities.RemoveUnicodeExtensions(resolvedLocale);
        var extensionKeywords = IntlUtilities.ParseUnicodeExtensionKeywords(resolvedLocale);

        var ignorePunctuationOption = IntlOptionHelpers.GetBooleanOption(options, "ignorePunctuation");
        var ignorePunctuation = ignorePunctuationOption ?? ResolveIgnorePunctuationDefault(baseLocale);

        var numericOption = IntlOptionHelpers.GetBooleanOption(options, "numeric");
        var caseFirstOption = ResolveCaseFirstOption(options);
        var collationOption = ResolveCollationOption(options);

        var resolvedCollation = ResolveCollation(
            baseLocale,
            extensionKeywords,
            string.IsNullOrEmpty(collationOption) ? null : collationOption,
            out var useCollationExtension);
        var resolvedNumeric = ResolveNumeric(
            extensionKeywords,
            numericOption,
            out var useNumericExtension);
        var resolvedCaseFirst = ResolveCaseFirst(
            extensionKeywords,
            caseFirstOption,
            out var useCaseFirstExtension);

        var finalLocale = ComposeResolvedLocale(
            baseLocale,
            resolvedCollation,
            useCollationExtension,
            resolvedNumeric,
            useNumericExtension,
            resolvedCaseFirst,
            useCaseFirstExtension);
        var compareInfo = IntlUtilities.ResolveCulture(finalLocale).CompareInfo;

        return new IntlCollatorInternalSlots
        {
            Locale = finalLocale,
            Usage = usage,
            Sensitivity = sensitivity,
            IgnorePunctuation = ignorePunctuation,
            Numeric = resolvedNumeric,
            CaseFirst = resolvedCaseFirst,
            Collation = resolvedCollation,
            LocaleMatcher = localeMatcher,
            CompareInfo = compareInfo
        };
    }

    private string CanonicalizeOrFallbackLocale(string locale)
    {
        try
        {
            return IntlUtilities.CanonicalizeLocale(locale, Realm);
        }
        catch (ThrowSignal)
        {
            return "en";
        }
    }

    private static bool ResolveIgnorePunctuationDefault(string baseLocale)
    {
        var (language, _, _, _) = IntlLocaleConstructor.ParseBaseName(baseLocale);
        return string.Equals(language, "th", StringComparison.Ordinal);
    }

    private string? ResolveCaseFirstOption(IJsPropertyAccessor? options)
    {
        if (options is null || !options.TryGetProperty("caseFirst", out var rawValue) || rawValue.IsUndefined)
        {
            return null;
        }

        var value = JsValueToString(rawValue, Realm);
        if (string.IsNullOrEmpty(value))
        {
            throw ThrowRangeError("Intl.Collator caseFirst option cannot be empty", realm: Realm);
        }

        if (!IsValidCaseFirst(value))
        {
            throw ThrowRangeError($"Invalid Intl.Collator caseFirst option '{value}'", realm: Realm);
        }

        return value;
    }

    private static string ResolveCollation(
        string baseLocale,
        IReadOnlyDictionary<string, List<string>> extensionKeywords,
        string? collationOption,
        out bool useCollationExtension)
    {
        var (language, _, _, _) = IntlLocaleConstructor.ParseBaseName(baseLocale);
        var extensionCollation = TryGetCollationFromExtension(extensionKeywords);
        var supportedExtensionCollation = extensionCollation is not null &&
                                          IsCollationSupported(language, extensionCollation)
            ? extensionCollation
            : null;

        var supportedOptionCollation = collationOption is not null && IsCollationSupported(language, collationOption)
            ? collationOption
            : null;

        if (collationOption is not null)
        {
            if (supportedOptionCollation is not null)
            {
                useCollationExtension = supportedExtensionCollation is not null &&
                                        string.Equals(supportedOptionCollation, supportedExtensionCollation,
                                            StringComparison.Ordinal);
                return supportedOptionCollation;
            }

            if (supportedExtensionCollation is not null)
            {
                useCollationExtension = true;
                return supportedExtensionCollation;
            }

            useCollationExtension = false;
            return "default";
        }

        if (supportedExtensionCollation is not null)
        {
            useCollationExtension = true;
            return supportedExtensionCollation;
        }

        useCollationExtension = false;
        return "default";
    }

    private static bool ResolveNumeric(
        IReadOnlyDictionary<string, List<string>> extensionKeywords,
        bool? numericOption,
        out bool useNumericExtension)
    {
        var extensionNumeric = TryGetNumericFromExtension(extensionKeywords, out var hasNumericExtension);

        if (numericOption.HasValue)
        {
            useNumericExtension = hasNumericExtension && extensionNumeric.HasValue &&
                                  numericOption.Value == extensionNumeric.Value;
            return numericOption.Value;
        }

        if (hasNumericExtension && extensionNumeric.HasValue)
        {
            useNumericExtension = true;
            return extensionNumeric.Value;
        }

        useNumericExtension = false;
        return false;
    }

    private static string ResolveCaseFirst(
        IReadOnlyDictionary<string, List<string>> extensionKeywords,
        string? caseFirstOption,
        out bool useCaseFirstExtension)
    {
        var extensionCaseFirst = TryGetCaseFirstFromExtension(extensionKeywords);

        if (caseFirstOption is not null)
        {
            useCaseFirstExtension = extensionCaseFirst is not null &&
                                    string.Equals(caseFirstOption, extensionCaseFirst, StringComparison.Ordinal);
            return caseFirstOption;
        }

        if (extensionCaseFirst is not null)
        {
            useCaseFirstExtension = true;
            return extensionCaseFirst;
        }

        useCaseFirstExtension = false;
        return "false";
    }

    private string ResolveCollationOption(IJsPropertyAccessor? options)
    {
        if (options is null || !options.TryGetProperty("collation", out var rawValue))
        {
            return string.Empty;
        }

        if (rawValue.IsUndefined)
        {
            return string.Empty;
        }

        var text = JsValueToString(rawValue, Realm);
        if (string.IsNullOrEmpty(text))
        {
            throw ThrowRangeError("Intl.Collator collation option cannot be empty", realm: Realm);
        }

        var normalized = NormalizeCollationValue(text);
        if (string.IsNullOrEmpty(normalized))
        {
            throw ThrowRangeError(
                $"Unsupported collation '{text}' for Intl.Collator", realm: Realm);
        }

        return normalized;
    }

    private static string ComposeResolvedLocale(
        string baseLocale,
        string collation,
        bool useCollationExtension,
        bool numeric,
        bool useNumericExtension,
        string caseFirst,
        bool useCaseFirstExtension)
    {
        var entries = new List<(string Key, List<string> Values)>();

        if (useCollationExtension && !string.Equals(collation, "default", StringComparison.Ordinal))
        {
            entries.Add(("co", [collation]));
        }

        if (useNumericExtension)
        {
            entries.Add(("kn", numeric ? [] : ["false"]));
        }

        if (useCaseFirstExtension)
        {
            entries.Add(("kf", [caseFirst]));
        }

        if (entries.Count == 0)
        {
            return baseLocale;
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));

        var parts = new List<string> { "u" };
        foreach (var entry in entries)
        {
            parts.Add(entry.Key);
            parts.AddRange(entry.Values);
        }

        return $"{baseLocale}-{string.Join('-', parts)}";
    }

    private static string? TryGetCaseFirstFromExtension(IReadOnlyDictionary<string, List<string>> keywords)
    {
        if (keywords.TryGetValue("kf", out var values) && values.Count > 0)
        {
            var value = values[0].ToLowerInvariant();
            if (IsValidCaseFirst(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool? TryGetNumericFromExtension(IReadOnlyDictionary<string, List<string>> keywords, out bool hasKey)
    {
        if (!keywords.TryGetValue("kn", out var values))
        {
            hasKey = false;
            return null;
        }

        hasKey = true;
        if (values.Count == 0)
        {
            return true;
        }

        var value = values[0].ToLowerInvariant();
        if (string.Equals(value, "true", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(value, "false", StringComparison.Ordinal))
        {
            return false;
        }

        return null;
    }

    private static string? TryGetCollationFromExtension(IReadOnlyDictionary<string, List<string>> keywords)
    {
        if (keywords.TryGetValue("co", out var values) && values.Count > 0)
        {
            var normalized = NormalizeCollationValue(values[0]);
            if (!string.IsNullOrEmpty(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static bool IsValidCaseFirst(string value)
    {
        return value is "upper" or "lower" or "false";
    }

    private static bool IsCollationSupported(string language, string collation)
    {
        if (string.Equals(collation, "default", StringComparison.Ordinal))
        {
            return true;
        }

        return collation switch
        {
            "compat" => string.Equals(language, "ar", StringComparison.Ordinal),
            "dict" => string.Equals(language, "si", StringComparison.Ordinal),
            "gb2312" => string.Equals(language, "zh", StringComparison.Ordinal),
            "phonebk" => string.Equals(language, "de", StringComparison.Ordinal),
            "pinyin" => string.Equals(language, "zh", StringComparison.Ordinal),
            "reformed" => string.Equals(language, "sv", StringComparison.Ordinal),
            "stroke" => string.Equals(language, "zh", StringComparison.Ordinal),
            "traditional" => string.Equals(language, "zh", StringComparison.Ordinal),
            "unihan" => string.Equals(language, "zh", StringComparison.Ordinal),
            "zhuyin" => string.Equals(language, "zh", StringComparison.Ordinal),
            // Collations commonly available across multiple locales.
            "ducet" or "emoji" or "eor" => true,
            _ => false
        };
    }

    private static string NormalizeCollationValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized = value.ToLowerInvariant();
        if (normalized is "standard" or "search")
        {
            return "default";
        }

        return SupportedCollations.Contains(normalized) ? normalized : string.Empty;
    }
}
