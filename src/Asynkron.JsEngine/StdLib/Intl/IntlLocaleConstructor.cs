using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.Locale", PrototypeType = typeof(IntlLocalePrototype), Length = 1d, DisplayName = "Locale")]
public sealed partial class IntlLocaleConstructor : JsConstructor
{
    private static readonly HashSet<string> HourCycleValues = new(StringComparer.Ordinal)
    {
        "h11", "h12", "h23", "h24"
    };

    private static readonly HashSet<string> CaseFirstValues = new(StringComparer.Ordinal)
    {
        "upper", "lower", "false"
    };

    public IntlLocaleConstructor(IJsObjectLike prototype, RealmState realm) : base(prototype, realm)
    {
    }

    internal static JsObject CreateLocaleFromCanonical(string canonicalTag, RealmState realm, JsObject? prototype = null)
    {
        var instance = new JsObject(prototype ?? realm.ObjectPrototype);
        InitializeLocaleSlots(instance, canonicalTag, realm);
        return instance;
    }

    internal static void InitializeLocaleSlots(JsObject target, string canonicalTag, RealmState realm)
    {
        var baseName = ExtractBaseName(canonicalTag);
        var (language, script, region, variants) = ParseBaseName(baseName);
        var keywords = ParseUnicodeKeywords(canonicalTag);

        target.SetProperty(IntlLocalePrototype.TagSlot, canonicalTag);
        target.SetProperty(IntlLocalePrototype.BrandKey, true);
        DefineInternalSlot(target, IntlLocalePrototype.LanguageSlot, language);
        DefineInternalSlot(target, IntlLocalePrototype.ScriptSlot, script ?? string.Empty);
        DefineInternalSlot(target, IntlLocalePrototype.RegionSlot, region ?? string.Empty);
        DefineInternalSlot(target, IntlLocalePrototype.VariantsSlot, variants);
        DefineInternalSlot(target, IntlLocalePrototype.KeywordsSlot, keywords);
        DefineInternalSlot(target, IntlLocalePrototype.WeekendSlot, new[] { 6, 7 });
        DefineInternalSlot(target, IntlLocalePrototype.TextDirectionSlot, "ltr");
    }

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var instance = PrepareThisObject(thisValue);
        var tagValue = args.GetArgument(0);
        var tag = StandardLibrary.JsValueToString(tagValue, Realm);
        var canonicalTag = IntlUtilities.CanonicalizeLocale(tag, Realm);

        InitializeLocaleSlots(instance, canonicalTag, Realm);

        var optionsArg = args.GetArgument(1);
        if (StandardLibrary.TryGetObject(optionsArg, Realm, out var optionsAccessor) &&
            optionsAccessor is IJsPropertyAccessor options)
        {
            ApplyOptions(instance, options);
        }

        return new JsValue(instance);
    }

    private void ApplyOptions(JsObject locale, IJsPropertyAccessor options)
    {
        var keywords = IntlLocalePrototype.GetLocaleKeywords(locale);
        var variants = IntlLocalePrototype.GetLocaleVariants(locale);

        if (TryGetStringOption(options, "baseName", out var baseNameOption))
        {
            var canonicalBase = IntlUtilities.CanonicalizeLocale(baseNameOption, Realm);
            var baseOnly = ExtractBaseName(canonicalBase);
            var (language, script, region, parsedVariants) = ParseBaseName(baseOnly);
            locale.SetProperty(IntlLocalePrototype.LanguageSlot, language);
            locale.SetProperty(IntlLocalePrototype.ScriptSlot, script ?? string.Empty);
            locale.SetProperty(IntlLocalePrototype.RegionSlot, region ?? string.Empty);
            variants.Clear();
            variants.AddRange(parsedVariants);
        }

        if (TryGetStringOption(options, "language", out var languageOption))
        {
            if (!IsLanguageSubtag(languageOption))
            {
                throw StandardLibrary.ThrowRangeError("Invalid Intl.Locale language option", realm: Realm);
            }

            locale.SetProperty(IntlLocalePrototype.LanguageSlot, languageOption.ToLowerInvariant());
        }

        if (TryGetStringOption(options, "script", out var scriptOption))
        {
            if (!IsScriptSubtag(scriptOption))
            {
                throw StandardLibrary.ThrowRangeError("Invalid Intl.Locale script option", realm: Realm);
            }

            locale.SetProperty(IntlLocalePrototype.ScriptSlot,
                char.ToUpperInvariant(scriptOption[0]) + scriptOption[1..].ToLowerInvariant());
        }

        if (TryGetStringOption(options, "region", out var regionOption))
        {
            if (!IsRegionSubtag(regionOption))
            {
                throw StandardLibrary.ThrowRangeError("Invalid Intl.Locale region option", realm: Realm);
            }

            locale.SetProperty(IntlLocalePrototype.RegionSlot,
                regionOption.Length == 2 ? regionOption.ToUpperInvariant() : regionOption);
        }

        if (TryGetStringOption(options, "calendar", out var calendarOption))
        {
            if (!IntlUtilities.TryNormalizeCalendar(calendarOption, out var canonicalCalendar))
            {
                throw StandardLibrary.ThrowRangeError("Invalid Intl.Locale calendar option", realm: Realm);
            }

            SetKeyword(keywords, "ca", canonicalCalendar);
        }

        if (TryGetStringOption(options, "numberingSystem", out var numberingSystemOption))
        {
            if (!IntlUtilities.TryNormalizeNumberingSystem(numberingSystemOption, out var canonicalNumbering))
            {
                throw StandardLibrary.ThrowRangeError("Invalid Intl.Locale numberingSystem option", realm: Realm);
            }

            SetKeyword(keywords, "nu", canonicalNumbering);
        }

        if (TryGetStringOption(options, "collation", out var collationOption))
        {
            var normalized = collationOption.ToLowerInvariant();
            if (!IsValidCollation(normalized))
            {
                throw StandardLibrary.ThrowRangeError("Invalid Intl.Locale collation option", realm: Realm);
            }

            SetKeyword(keywords, "co", normalized);
        }

        if (TryGetStringOption(options, "hourCycle", out var hourCycleOption))
        {
            var normalized = hourCycleOption.ToLowerInvariant();
            if (!HourCycleValues.Contains(normalized))
            {
                throw StandardLibrary.ThrowRangeError("Invalid Intl.Locale hourCycle option", realm: Realm);
            }

            SetKeyword(keywords, "hc", normalized);
        }

        if (TryGetStringOption(options, "caseFirst", out var caseFirstOption))
        {
            var normalized = caseFirstOption.ToLowerInvariant();
            if (!CaseFirstValues.Contains(normalized))
            {
                throw StandardLibrary.ThrowRangeError("Invalid Intl.Locale caseFirst option", realm: Realm);
            }

            SetKeyword(keywords, "kf", normalized);
        }

        if (TryGetBooleanOption(options, "numeric", out var numericOption))
        {
            SetKeyword(keywords, "kn", numericOption ? "true" : "false");
        }

        if (TryGetFirstDayOfWeekOption(options, out var firstDayOfWeek))
        {
            SetKeyword(keywords, "fw", firstDayOfWeek);
        }

        var canonicalTag = IntlUtilities.ApplyUnicodeLocaleOverrides(
            IntlLocalePrototype.BuildBaseName(locale),
            keywords);
        locale.SetProperty(IntlLocalePrototype.TagSlot, canonicalTag);
    }

    internal static void DefineInternalSlot(JsObject target, string slot, object? value)
    {
        target.DefineProperty(slot,
            new PropertyDescriptor { Value = value, Writable = true, Enumerable = false, Configurable = true });
    }

    internal static string BuildVariantSuffix(List<string> variants)
    {
        return variants.Count == 0 ? string.Empty : "-" + string.Join("-", variants);
    }

    internal static string BuildBaseTag(string language, string? script, string? region)
    {
        var resolvedLanguage = string.IsNullOrEmpty(language) ? "und" : language;
        var result = resolvedLanguage;
        if (!string.IsNullOrEmpty(script))
        {
            result += "-" + script;
        }

        if (!string.IsNullOrEmpty(region))
        {
            result += "-" + region;
        }

        return result;
    }

    private bool TryGetStringOption(IJsPropertyAccessor options, string property, out string value)
    {
        if (!options.TryGetProperty(property, out var rawValue))
        {
            value = string.Empty;
            return false;
        }

        var raw = rawValue;
        if (raw.IsUndefined)
        {
            value = string.Empty;
            return false;
        }

        value = StandardLibrary.JsValueToString(raw, Realm);
        return true;
    }

    private static bool TryGetBooleanOption(IJsPropertyAccessor options, string property, out bool value)
    {
        if (!options.TryGetProperty(property, out var rawValue))
        {
            value = false;
            return false;
        }

        var raw = rawValue;
        if (raw.IsUndefined)
        {
            value = false;
            return false;
        }

        value = JsOps.ToBoolean(raw);
        return true;
    }

    private static void SetKeyword(Dictionary<string, string> keywords, string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            keywords.Remove(key);
        }
        else
        {
            keywords[key] = value;
        }
    }

    private bool TryGetFirstDayOfWeekOption(IJsPropertyAccessor options, out string value)
    {
        value = string.Empty;
        if (!options.TryGetProperty("firstDayOfWeek", out var rawValue))
        {
            return false;
        }

        var raw = rawValue;
        if (raw.IsUndefined)
        {
            return false;
        }

        if (raw.TryGetDouble(out var dbl))
        {
            return TryNormalizeWeekdayFromNumber((int)dbl, out value);
        }

        var str = StandardLibrary.JsValueToString(raw);
        if (TryNormalizeWeekdayFromString(str, out value))
        {
            return true;
        }

        if (int.TryParse(str, out var parsed))
        {
            return TryNormalizeWeekdayFromNumber(parsed, out value);
        }

        throw StandardLibrary.ThrowRangeError("Invalid Intl.Locale firstDayOfWeek option", realm: Realm);
    }

    private static bool TryNormalizeWeekdayFromString(string input, out string normalized)
    {
        var lower = input.ToLowerInvariant();
        switch (lower)
        {
            case "sun":
            case "mon":
            case "tue":
            case "wed":
            case "thu":
            case "fri":
            case "sat":
                normalized = lower;
                return true;
            default:
                normalized = string.Empty;
                return false;
        }
    }

    private static bool TryNormalizeWeekdayFromNumber(int input, out string normalized)
    {
        var adjusted = input % 7;
        if (adjusted <= 0)
        {
            adjusted += 7;
        }

        normalized = adjusted switch
        {
            1 => "mon",
            2 => "tue",
            3 => "wed",
            4 => "thu",
            5 => "fri",
            6 => "sat",
            7 => "sun",
            _ => string.Empty
        };

        return normalized.Length > 0;
    }

    private static bool IsLanguageSubtag(string candidate)
    {
        if (candidate.Length is >= 2 and <= 3)
        {
            foreach (var ch in candidate)
            {
                if (!char.IsLetter(ch))
                {
                    return false;
                }
            }

            return true;
        }

        if (candidate.Length is >= 5 and <= 8)
        {
            foreach (var ch in candidate)
            {
                if (!char.IsLetter(ch))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    private static bool IsScriptSubtag(string candidate)
    {
        if (candidate.Length != 4)
        {
            return false;
        }

        foreach (var ch in candidate)
        {
            if (!char.IsLetter(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRegionSubtag(string candidate)
    {
        if (candidate.Length == 2)
        {
            return char.IsLetter(candidate[0]) && char.IsLetter(candidate[1]);
        }

        if (candidate.Length == 3)
        {
            return char.IsDigit(candidate[0]) && char.IsDigit(candidate[1]) && char.IsDigit(candidate[2]);
        }

        return false;
    }

    private static bool IsValidCollation(string value)
    {
        if (value.Length is < 3 or > 8)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    internal static (string Language, string? Script, string? Region, List<string> Variants) ParseBaseName(
        string baseName)
    {
        var subtags = baseName.Length == 0
            ? Array.Empty<string>()
            : baseName.Split('-', StringSplitOptions.RemoveEmptyEntries);

        if (subtags.Length == 0)
        {
            return ("und", null, null, new List<string>());
        }

        var index = 1;
        var language = subtags[0].ToLowerInvariant();
        string? script = null;
        string? region = null;

        if (index < subtags.Length && subtags[index].Length == 4)
        {
            var tag = subtags[index];
            script = char.ToUpperInvariant(tag[0]) + tag[1..].ToLowerInvariant();
            index++;
        }

        if (index < subtags.Length)
        {
            var candidate = subtags[index];
            if (candidate.Length == 2 && char.IsLetter(candidate[0]) && char.IsLetter(candidate[1]))
            {
                region = candidate.ToUpperInvariant();
                index++;
            }
            else if (candidate.Length == 3 && char.IsDigit(candidate[0]) && char.IsDigit(candidate[1]) &&
                     char.IsDigit(candidate[2]))
            {
                region = candidate;
                index++;
            }
        }

        var variants = new List<string>();
        for (; index < subtags.Length; index++)
        {
            variants.Add(subtags[index]);
        }

        return (language, script, region, variants);
    }

    internal static string ExtractBaseName(string canonical)
    {
        var end = canonical.Length;
        var unicodeIndex = canonical.IndexOf("-u-", StringComparison.Ordinal);
        if (unicodeIndex >= 0)
        {
            end = Math.Min(end, unicodeIndex);
        }

        var transformIndex = canonical.IndexOf("-t-", StringComparison.Ordinal);
        if (transformIndex >= 0)
        {
            end = Math.Min(end, transformIndex);
        }

        var privateIndex = canonical.IndexOf("-x-", StringComparison.Ordinal);
        if (privateIndex >= 0)
        {
            end = Math.Min(end, privateIndex);
        }

        return end == canonical.Length ? canonical : canonical[..end];
    }

    internal static Dictionary<string, string> ParseUnicodeKeywords(string canonicalTag)
    {
        var keywords = new Dictionary<string, string>(StringComparer.Ordinal);
        var subtags = canonicalTag.Split('-', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < subtags.Length; i++)
        {
            if (!string.Equals(subtags[i], "u", StringComparison.Ordinal))
            {
                continue;
            }

            i++;
            while (i < subtags.Length && subtags[i].Length >= 3)
            {
                i++;
            }

            while (i < subtags.Length && subtags[i].Length == 2)
            {
                var key = subtags[i];
                i++;

                var typeParts = new List<string>();
                while (i < subtags.Length && subtags[i].Length > 2)
                {
                    typeParts.Add(subtags[i]);
                    i++;
                }

                var value = typeParts.Count == 0 ? "true" : string.Join("-", typeParts);
                keywords[key] = value;
            }

            break;
        }

        return keywords;
    }
}
