#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.DisplayNames", ToStringTag = "Intl.DisplayNames")]
public sealed partial class IntlDisplayNamesPrototype
{
    private const string BrandKey = "__displayNames__";
    private const string LocaleSlot = "__locale__";
    private const string StyleSlot = "__style__";
    private const string TypeSlot = "__type__";
    private const string FallbackSlot = "__fallback__";
    private const string LanguageDisplaySlot = "__languageDisplay__";

    private static readonly HashSet<string> DateTimeFieldValues = new(StringComparer.Ordinal)
    {
        "era",
        "year",
        "quarter",
        "month",
        "weekOfYear",
        "weekday",
        "day",
        "dayPeriod",
        "hour",
        "minute",
        "second",
        "relative",
        "timeZoneName"
    };

    internal static void InitializeInternalSlots(JsObject instance, string locale, string style, string type,
        string fallback, string languageDisplay)
    {
        instance.SetProperty(BrandKey, new JsValue(true));
        instance.SetProperty(LocaleSlot, new JsValue(locale));
        instance.SetProperty(StyleSlot, new JsValue(style));
        instance.SetProperty(TypeSlot, new JsValue(type));
        instance.SetProperty(FallbackSlot, new JsValue(fallback));
        instance.SetProperty(LanguageDisplaySlot, new JsValue(languageDisplay));
    }

    /* FLAKY */
    [JsHostMethod("of", Length = 1d)]
    private JsValue Of(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var instance = ValidateReceiver(thisValue);
        if (args.Count == 0)
        {
            throw ThrowTypeError("Intl.DisplayNames.of requires a code argument", realm: Realm);
        }

        var codeInput = JsValueToString(args[0], Realm);
        var type = instance.TryGetProperty(TypeSlot, out var typeValue) && typeValue.TryGetString(out var str)
            ? str
            : "language";
        var fallback = instance.TryGetProperty(FallbackSlot, out var fallbackValue) &&
                       fallbackValue.TryGetString(out var fb)
            ? fb
            : "code";

        var canonical = CanonicalizeCode(type, codeInput);
        if (canonical != null)
        {
            return new JsValue(canonical);
        }

        return fallback == "none" ? JsValue.Undefined : new JsValue(codeInput);
    }

    /* FLAKY */
    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var instance = ValidateReceiver(thisValue);
        var obj = new JsObject(Realm.ObjectPrototype);
        const string operation = "Intl.DisplayNames.prototype.resolvedOptions";
        var localeValue = instance.TryGetProperty(LocaleSlot, out var locale) && locale.TryGetString(out var localeStr)
            ? localeStr
            : "en";
        CreateDataPropertyOrThrow(obj, "locale", localeValue, Realm, operation);

        var styleValue = instance.TryGetProperty(StyleSlot, out var style) && style.TryGetString(out var styleStr)
            ? styleStr
            : "long";
        CreateDataPropertyOrThrow(obj, "style", styleValue, Realm, operation);

        var typeValue = instance.TryGetProperty(TypeSlot, out var type) && type.TryGetString(out var typeStr)
            ? typeStr
            : "language";
        CreateDataPropertyOrThrow(obj, "type", typeValue, Realm, operation);

        var fallbackValue =
            instance.TryGetProperty(FallbackSlot, out var fallback) && fallback.TryGetString(out var fallbackStr)
                ? fallbackStr
                : "code";
        CreateDataPropertyOrThrow(obj, "fallback", fallbackValue, Realm, operation);

        var languageDisplayValue = instance.TryGetProperty(LanguageDisplaySlot, out var languageDisplay) &&
                                   languageDisplay.TryGetString(out var languageDisplayStr)
            ? languageDisplayStr
            : "dialect";
        CreateDataPropertyOrThrow(obj, "languageDisplay", languageDisplayValue, Realm, operation);
        return new JsValue(obj);
    }

    private JsObject ValidateReceiver(JsValue candidate)
    {
        return candidate.EnsureBrand(BrandKey, Realm, "Intl.DisplayNames method called on incompatible receiver");
    }

    private string? CanonicalizeCode(string type, string code)
    {
        return type switch
        {
            "currency" => CanonicalizeCurrency(code),
            "region" => CanonicalizeRegion(code),
            "script" => CanonicalizeScript(code),
            "calendar" => CanonicalizeCalendar(code),
            "language" => CanonicalizeLanguage(code),
            "dateTimeField" => CanonicalizeDateTimeField(code),
            _ => throw ThrowRangeError($"Unsupported Intl.DisplayNames type '{type}'", realm: Realm)
        };
    }

    private string? CanonicalizeCurrency(string code)
    {
        if (!IntlUtilities.TryGetCanonicalCurrency(code, out var canonical))
        {
            throw ThrowRangeError($"Invalid currency code '{code}'", realm: Realm);
        }

        return IntlUtilities.IsSupportedCurrency(canonical) ? canonical : null;
    }

    private string? CanonicalizeRegion(string code)
    {
        if (code.Length == 2 &&
            char.IsLetter(code[0]) &&
            char.IsLetter(code[1]))
        {
            return code.ToUpperInvariant();
        }

        if (code.Length == 3 &&
            char.IsDigit(code[0]) &&
            char.IsDigit(code[1]) &&
            char.IsDigit(code[2]))
        {
            return code;
        }

        throw ThrowRangeError($"Invalid region code '{code}'", realm: Realm);
    }

    private string? CanonicalizeScript(string code)
    {
        if (code.Length != 4 || !IsAlphaString(code))
        {
            throw ThrowRangeError($"Invalid script code '{code}'", realm: Realm);
        }

        return char.ToUpperInvariant(code[0]) + code[1..].ToLowerInvariant();
    }

    private string? CanonicalizeCalendar(string code)
    {
        if (!IsUnicodeTypeIdentifier(code))
        {
            throw ThrowRangeError($"Invalid calendar code '{code}'", realm: Realm);
        }

        return IntlUtilities.TryNormalizeCalendar(code, out var canonical) ? canonical : null;
    }

    private string? CanonicalizeLanguage(string code)
    {
        if (code.Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            throw ThrowRangeError("Invalid language tag 'root'", realm: Realm);
        }

        var canonical = IntlUtilities.CanonicalizeLocale(code, Realm);
        var subtags = canonical.Split('-', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i < subtags.Length; i++)
        {
            if (subtags[i].Length == 1)
            {
                throw ThrowRangeError($"Invalid language tag '{code}'", realm: Realm);
            }
        }

        return canonical;
    }

    private string? CanonicalizeDateTimeField(string code)
    {
        if (!DateTimeFieldValues.Contains(code))
        {
            throw ThrowRangeError($"Unsupported dateTimeField '{code}'", realm: Realm);
        }

        return code;
    }

    private static bool IsAlphaString(string value)
    {
        foreach (var ch in value)
        {
            if (!char.IsLetter(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUnicodeTypeIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (!value.Equals(value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (value[0] == '-' || value[^1] == '-' ||
            value.Contains("--", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length is < 3 or > 8)
            {
                return false;
            }

            foreach (var ch in part)
            {
                if (!char.IsLetterOrDigit(ch))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
