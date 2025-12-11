using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.Collator", PrototypeType = typeof(IntlCollatorPrototype), Length = 0d, DisplayName = "Collator")]
public sealed partial class IntlCollatorConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var slots = CreateInternalSlots(args.GetArgument(0), args.GetArgument(1));
        var instance = PrepareThisObject(thisValue);
        IntlCollatorPrototype.InitializeInternalSlots(instance, slots);
        return instance;
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        var supportedLocalesOf = new HostFunction(SupportedLocalesOf, isConstructor: false);
        supportedLocalesOf.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        supportedLocalesOf.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "supportedLocalesOf", Writable = false, Enumerable = false, Configurable = true
            });

        constructor.DefineProperty("supportedLocalesOf",
            new PropertyDescriptor
            {
                Value = supportedLocalesOf, Writable = true, Enumerable = false, Configurable = true
            });

        supportedLocalesOf.SetPrototype(constructor.Prototype);
        supportedLocalesOf.Delete("prototype");
    }

    private JsArray SupportedLocalesOf(IReadOnlyList<object?> args)
    {
        return StandardLibrary.ResolveSupportedLocales(args.GetArgument(0), args.GetArgument(1), Realm);
    }

    private IntlCollatorInternalSlots CreateInternalSlots(object? localesArg, object? optionsArg)
    {
        var (_, resolvedLocale) = StandardLibrary.ResolveIntlLocales(localesArg, Realm);
        var baseLocale = IntlUtilities.RemoveUnicodeExtensions(resolvedLocale);
        var extensionKeywords = IntlUtilities.ParseUnicodeExtensionKeywords(resolvedLocale);

        var collation = ResolveCollationFromExtension(extensionKeywords);
        var numeric = ResolveNumericFromExtension(extensionKeywords);
        var caseFirst = ResolveCaseFirstFromExtension(extensionKeywords);

        var options = IntlOptionHelpers.GetOptionsObject(optionsArg, Realm, "Collator");
        var usage = IntlOptionHelpers.GetStringOption(options, "usage", Realm, "Collator",
            ["sort", "search"], "sort");
        var sensitivityDefault = string.Equals(usage, "search", StringComparison.Ordinal)
            ? "base"
            : "variant";
        var sensitivity = IntlOptionHelpers.GetStringOption(options, "sensitivity", Realm, "Collator",
            ["base", "accent", "case", "variant"], sensitivityDefault);
        var ignorePunctuation = IntlOptionHelpers.GetBooleanOption(options, "ignorePunctuation", Realm,
            "Collator", false) ?? false;
        var localeMatcher = IntlOptionHelpers.GetStringOption(options, "localeMatcher", Realm, "Collator",
            ["lookup", "best fit"], "best fit");

        numeric = IntlOptionHelpers.GetBooleanOption(options, "numeric", Realm, "Collator", numeric) ?? numeric;
        caseFirst = IntlOptionHelpers.GetStringOption(options, "caseFirst", Realm, "Collator",
            ["upper", "lower", "false"], caseFirst);
        var collationOption = ResolveCollationOption(options);
        if (!string.IsNullOrEmpty(collationOption))
        {
            collation = collationOption;
        }

        var finalLocale = ComposeResolvedLocale(baseLocale, collation, numeric, caseFirst);
        var compareInfo = IntlUtilities.ResolveCulture(finalLocale).CompareInfo;

        return new IntlCollatorInternalSlots
        {
            Locale = finalLocale,
            Usage = usage,
            Sensitivity = sensitivity,
            IgnorePunctuation = ignorePunctuation,
            Numeric = numeric,
            CaseFirst = caseFirst,
            Collation = collation,
            LocaleMatcher = localeMatcher,
            CompareInfo = compareInfo
        };
    }

    private string ResolveCollationOption(IJsPropertyAccessor? options)
    {
        if (options is null || !options.TryGetProperty("collation", out var value) ||
            ReferenceEquals(value, Symbol.Undefined))
        {
            return string.Empty;
        }

        var text = StandardLibrary.JsValueToString(value, Realm);
        if (string.IsNullOrEmpty(text))
        {
            throw StandardLibrary.ThrowRangeError("Intl.Collator collation option cannot be empty", realm: Realm);
        }

        var normalized = NormalizeCollationValue(text);
        if (string.IsNullOrEmpty(normalized))
        {
            throw StandardLibrary.ThrowRangeError(
                $"Unsupported collation '{text}' for Intl.Collator", realm: Realm);
        }

        return normalized;
    }

    private static string ComposeResolvedLocale(string baseLocale, string collation, bool numeric, string caseFirst)
    {
        var entries = new List<(string Key, List<string> Values)>();
        if (!string.Equals(collation, "default", StringComparison.Ordinal))
        {
            entries.Add(("co", new List<string> { collation }));
        }

        if (numeric)
        {
            entries.Add(("kn", new List<string>()));
        }

        if (!string.Equals(caseFirst, "false", StringComparison.Ordinal))
        {
            entries.Add(("kf", new List<string> { caseFirst }));
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

        return $"{baseLocale}-{string.Join("-", parts)}";
    }

    private static string ResolveCaseFirstFromExtension(IReadOnlyDictionary<string, List<string>> keywords)
    {
        if (keywords.TryGetValue("kf", out var values) && values.Count > 0)
        {
            var value = values[0].ToLowerInvariant();
            if (IsValidCaseFirst(value))
            {
                return value;
            }
        }

        return "false";
    }

    private static bool ResolveNumericFromExtension(IReadOnlyDictionary<string, List<string>> keywords)
    {
        if (keywords.TryGetValue("kn", out var values))
        {
            if (values.Count == 0)
            {
                return true;
            }

            var value = values[0].ToLowerInvariant();
            if (value == "true")
            {
                return true;
            }

            if (value == "false")
            {
                return false;
            }
        }

        return false;
    }

    private static string ResolveCollationFromExtension(IReadOnlyDictionary<string, List<string>> keywords)
    {
        if (keywords.TryGetValue("co", out var values) && values.Count > 0)
        {
            var normalized = NormalizeCollationValue(values[0]);
            if (!string.IsNullOrEmpty(normalized))
            {
                return normalized;
            }
        }

        return "default";
    }

    private static bool IsValidCaseFirst(string value)
    {
        return value is "upper" or "lower" or "false";
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

    private static readonly HashSet<string> SupportedCollations = new(StringComparer.Ordinal)
    {
        "default",
        "phonebk",
        "stroke",
        "compat",
        "dict",
        "ducet",
        "gb2312",
        "pinyin",
        "reformed",
        "traditional",
        "unihan",
        "zhuyin",
        "emoji"
    };
}
