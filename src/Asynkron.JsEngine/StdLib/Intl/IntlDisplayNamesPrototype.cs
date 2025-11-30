using System.Collections.Generic;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

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
        "era", "year", "quarter", "month", "weekOfYear", "weekday", "day",
        "dayPeriod", "hour", "minute", "second", "relative"
    };

    private static readonly Regex LanguageTagRegex =
        new(@"^(?<language>[A-Za-z]{2,3}|[A-Za-z]{5,8})(-(?<script>[A-Za-z]{4}))?(-(?
<region>[A-Za-z]{2}|[0-9]{3}))?(?<variants>(-(?:[0-9][A-Za-z0-9]{3}|[A-Za-z0-9]{5,8}))*)$",
            RegexOptions.Compiled);

    internal static void InitializeInternalSlots(JsObject instance, string locale, string style, string type,
        string fallback, string languageDisplay)
    {
        instance.SetProperty(BrandKey, true);
        instance.SetProperty(LocaleSlot, locale);
        instance.SetProperty(StyleSlot, style);
        instance.SetProperty(TypeSlot, type);
        instance.SetProperty(FallbackSlot, fallback);
        instance.SetProperty(LanguageDisplaySlot, languageDisplay);
    }

    [JsHostMethod("of", Length = 1d)]
    private object? Of(object? thisValue, IReadOnlyList<object?> args)
    {
        var instance = ValidateReceiver(thisValue);
        if (args.Count == 0)
        {
            throw ThrowTypeError("Intl.DisplayNames.of requires a code argument", realm: Realm);
        }

        var codeInput = JsValueToString(args[0], Realm);
        var type = instance.TryGetProperty(TypeSlot, out var typeValue) && typeValue is string str ? str : "language";
        var fallback = instance.TryGetProperty(FallbackSlot, out var fallbackValue) && fallbackValue is string fb
            ? fb
            : "code";

        var canonical = CanonicalizeCode(type, codeInput);
        if (canonical is null)
        {
            return fallback == "none" ? Symbol.Undefined : codeInput;
        }

        return canonical;
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsObject ResolvedOptions(object? thisValue, IReadOnlyList<object?> _)
    {
        var instance = ValidateReceiver(thisValue);
        var obj = new JsObject(Realm.ObjectPrototype);
        obj.SetProperty("locale",
            instance.TryGetProperty(LocaleSlot, out var locale) ? locale ?? "en" : "en");
        obj.SetProperty("style",
            instance.TryGetProperty(StyleSlot, out var style) ? style ?? "long" : "long");
        obj.SetProperty("type",
            instance.TryGetProperty(TypeSlot, out var type) ? type ?? "language" : "language");
        obj.SetProperty("fallback",
            instance.TryGetProperty(FallbackSlot, out var fallback) ? fallback ?? "code" : "code");
        obj.SetProperty("languageDisplay",
            instance.TryGetProperty(LanguageDisplaySlot, out var languageDisplay)
                ? languageDisplay ?? "dialect"
                : "dialect");
        return obj;
    }

    private JsObject ValidateReceiver(object? candidate)
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

        var match = LanguageTagRegex.Match(code);
        if (!match.Success)
        {
            throw ThrowRangeError($"Invalid language tag '{code}'", realm: Realm);
        }

        var language = match.Groups["language"].Value.ToLowerInvariant();
        var script = match.Groups["script"].Success
            ? match.Groups["script"].Value
            : null;
        var region = match.Groups["region"].Success
            ? match.Groups["region"].Value
            : null;
        var variantsGroup = match.Groups["variants"];

        var builder = new List<string> { language };
        if (script is not null)
        {
            builder.Add(char.ToUpperInvariant(script[0]) + script[1..].ToLowerInvariant());
        }

        if (region is not null)
        {
            builder.Add(region.Length == 2 ? region.ToUpperInvariant() : region);
        }

        if (variantsGroup.Success && variantsGroup.Value.Length > 0)
        {
            var variants = variantsGroup.Value.Split('-', StringSplitOptions.RemoveEmptyEntries);
            builder.AddRange(variants.Select(v => v.ToLowerInvariant()));
        }

        return string.Join('-', builder);
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
