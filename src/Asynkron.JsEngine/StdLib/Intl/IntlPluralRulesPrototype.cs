#region

using System.Globalization;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.PluralRules", ToStringTag = "Intl.PluralRules")]
public sealed partial class IntlPluralRulesPrototype
{
    private const string BrandKey = "__pluralRules__";

    internal static void InitializeInternalSlots(JsObject instance, string locale, string type, string notation,
        int minimumIntegerDigits, int minimumFractionDigits, int maximumFractionDigits,
        int? minimumSignificantDigits, int? maximumSignificantDigits,
        string roundingMode, string roundingType, int roundingIncrement, string trailingZeroDisplay)
    {
        instance.SetProperty(BrandKey, true);
        instance.SetProperty("__locale__", locale);
        instance.SetProperty("__type__", type);
        instance.SetProperty("__notation__", notation);
        instance.SetProperty("__minimumIntegerDigits__", (double)minimumIntegerDigits);
        instance.SetProperty("__minimumFractionDigits__", (double)minimumFractionDigits);
        instance.SetProperty("__maximumFractionDigits__", (double)maximumFractionDigits);
        if (minimumSignificantDigits.HasValue)
        {
            instance.SetProperty("__minimumSignificantDigits__", (double)minimumSignificantDigits.Value);
        }

        if (maximumSignificantDigits.HasValue)
        {
            instance.SetProperty("__maximumSignificantDigits__", (double)maximumSignificantDigits.Value);
        }

        instance.SetProperty("__roundingMode__", roundingMode);
        instance.SetProperty("__roundingType__", roundingType);
        instance.SetProperty("__roundingIncrement__", (double)roundingIncrement);
        instance.SetProperty("__trailingZeroDisplay__", trailingZeroDisplay);
    }

    [JsHostMethod("select", Length = 1d)]
    private JsValue Select(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var instance = ValidateReceiver(thisValue);
        var n = args.Count > 0 ? JsOps.ToNumber(args[0]) : double.NaN;

        if (double.IsNaN(n) || double.IsInfinity(n))
        {
            return new JsValue("other");
        }

        var type = GetStringSlot(instance, "__type__", "cardinal");
        var locale = GetStringSlot(instance, "__locale__", "en");
        var notation = GetStringSlot(instance, "__notation__", "standard");
        return new JsValue(ResolvePlural(n, type, locale, notation));
    }

    [JsHostMethod("selectRange", Length = 2d)]
    private JsValue SelectRange(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        ValidateReceiver(thisValue);
        var start = args.GetArgument(0);
        var end = args.GetArgument(1);

        if (start.IsUndefined)
        {
            throw ThrowTypeError("start must not be undefined", realm: Realm);
        }

        if (end.IsUndefined)
        {
            throw ThrowTypeError("end must not be undefined", realm: Realm);
        }

        var x = JsOps.ToNumber(start);
        var y = JsOps.ToNumber(end);

        if (double.IsNaN(x))
        {
            throw ThrowRangeError("start must not be NaN", realm: Realm);
        }

        if (double.IsNaN(y))
        {
            throw ThrowRangeError("end must not be NaN", realm: Realm);
        }

        // For most locales, selectRange returns "other" for English
        return new JsValue("other");
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var instance = ValidateReceiver(thisValue);
        var obj = new JsObject(Realm.ObjectPrototype);
        const string operation = "Intl.PluralRules.prototype.resolvedOptions";

        var locale = GetStringSlot(instance, "__locale__", "en");
        var type = GetStringSlot(instance, "__type__", "cardinal");
        var notation = GetStringSlot(instance, "__notation__", "standard");
        var minimumIntegerDigits = GetNumberSlot(instance, "__minimumIntegerDigits__", 1);
        var roundingMode = GetStringSlot(instance, "__roundingMode__", "halfExpand");
        var roundingType = GetStringSlot(instance, "__roundingType__", "significantDigits");
        var roundingIncrement = GetNumberSlot(instance, "__roundingIncrement__", 1);
        var trailingZeroDisplay = GetStringSlot(instance, "__trailingZeroDisplay__", "auto");

        double minSig = 0, maxSig = 0;
        var hasMinSig = instance.TryGetProperty("__minimumSignificantDigits__", out var minSigVal) &&
                        minSigVal.TryGetDouble(out minSig);
        var hasMaxSig = instance.TryGetProperty("__maximumSignificantDigits__", out var maxSigVal) &&
                        maxSigVal.TryGetDouble(out maxSig);

        // Property order per spec: locale, type, notation, minimumIntegerDigits,
        // then digit options (sig or frac), pluralCategories, rounding options
        CreateDataPropertyOrThrow(obj, "locale", locale, Realm, operation);
        CreateDataPropertyOrThrow(obj, "type", type, Realm, operation);
        CreateDataPropertyOrThrow(obj, "notation", notation, Realm, operation);
        CreateDataPropertyOrThrowJsValue(obj, "minimumIntegerDigits", (double)minimumIntegerDigits, Realm, operation);

        if (hasMinSig)
        {
            CreateDataPropertyOrThrowJsValue(obj, "minimumSignificantDigits", minSig, Realm, operation);
        }
        else
        {
            var minimumFractionDigits = GetNumberSlot(instance, "__minimumFractionDigits__", 0);
            CreateDataPropertyOrThrowJsValue(obj, "minimumFractionDigits", (double)minimumFractionDigits, Realm,
                operation);
        }

        if (hasMaxSig)
        {
            CreateDataPropertyOrThrowJsValue(obj, "maximumSignificantDigits", maxSig, Realm, operation);
        }
        else
        {
            var maximumFractionDigits = GetNumberSlot(instance, "__maximumFractionDigits__", 0);
            CreateDataPropertyOrThrowJsValue(obj, "maximumFractionDigits", (double)maximumFractionDigits, Realm,
                operation);
        }

        // Plural categories for the resolved locale (ordered: zero, one, two, few, many, other)
        var pluralCategories = GetPluralCategories(locale, type);
        var categoriesArray = new JsArray(Realm);
        foreach (var category in pluralCategories)
        {
            categoriesArray.Push(category);
        }

        CreateDataPropertyOrThrowJsValue(obj, "pluralCategories", JsValue.FromJsArray(categoriesArray), Realm,
            operation);
        CreateDataPropertyOrThrowJsValue(obj, "roundingIncrement", (double)roundingIncrement, Realm, operation);
        CreateDataPropertyOrThrow(obj, "roundingMode", roundingMode, Realm, operation);
        CreateDataPropertyOrThrow(obj, "roundingPriority",
            roundingType is "significantDigits" or "fractionDigits" ? "auto" : roundingType, Realm, operation);
        CreateDataPropertyOrThrow(obj, "trailingZeroDisplay", trailingZeroDisplay, Realm, operation);

        return new JsValue(obj);
    }

    private JsObject ValidateReceiver(JsValue thisValue)
    {
        return thisValue.EnsureBrand(BrandKey, Realm,
            "Intl.PluralRules method called on incompatible receiver");
    }

    private static string GetStringSlot(JsObject instance, string key, string defaultValue)
    {
        return instance.TryGetProperty(key, out var value) && value.TryGetString(out var str)
            ? str
            : defaultValue;
    }

    private static int GetNumberSlot(JsObject instance, string key, int defaultValue)
    {
        return instance.TryGetProperty(key, out var value) && value.TryGetDouble(out var num)
            ? (int)num
            : defaultValue;
    }

    /// <summary>
    /// Resolves the plural category for a given number using CLDR rules.
    /// </summary>
    internal static string ResolvePlural(double n, string type, string locale, string notation = "standard")
    {
        var language = ExtractLanguage(locale);

        if (string.Equals(type, "ordinal", StringComparison.Ordinal))
        {
            return ResolveOrdinal(n, language);
        }

        return ResolveCardinal(n, language, notation);
    }

    private static string ExtractLanguage(string locale)
    {
        var dashIndex = locale.IndexOf('-', StringComparison.Ordinal);
        return dashIndex >= 0 ? locale[..dashIndex] : locale;
    }

    /// <summary>
    /// Cardinal plural rules based on CLDR for common locales.
    /// </summary>
    private static string ResolveCardinal(double n, string language, string notation = "standard")
    {
        // Absolute integer value
        var i = (long)Math.Abs(Math.Truncate(n));
        // Number of visible fraction digits
        var formatted = n.ToString(CultureInfo.InvariantCulture);
        var dotIndex = formatted.IndexOf('.', StringComparison.Ordinal);
        var v = dotIndex >= 0 ? formatted.Length - dotIndex - 1 : 0;

        // Compute compact decimal exponent (e) based on notation
        var e = ComputeCompactExponent(n, notation);

        return language switch
        {
            // English, German, Dutch, Italian, etc.: "one" for i=1, v=0
            "en" or "de" or "nl" or "it" or "sv" or "da" or "nb" or "nn" or "no"
                or "pt" or "es" or "ca" or "et" or "fi" or "el" or "hu" or "bg"
                or "tr" or "hi" or "he" or "ur" =>
                i == 1 && v == 0 ? "one" : "other",

            // French: CLDR one/many/other with compact exponent
            "fr" => ResolveFrenchCardinal(n, i, v, e),

            // Arabic: complex rules
            "ar" => ResolveArabicCardinal(n, i),

            // Russian, Ukrainian: East Slavic rules
            "ru" or "uk" => ResolveRussianCardinal(i, v),

            // Polish
            "pl" => ResolvePolishCardinal(i, v),

            // Czech, Slovak
            "cs" or "sk" => ResolveCzechCardinal(i, v),

            // Japanese, Chinese, Korean, Vietnamese, Thai, etc.: always "other"
            "ja" or "zh" or "ko" or "vi" or "th" or "id" or "ms" or "lo" or "km" => "other",

            // Default to English-like rules
            _ => i == 1 && v == 0 ? "one" : "other"
        };
    }

    /// <summary>
    /// Computes the compact decimal exponent for a number based on notation.
    /// Standard notation: e=0; scientific/engineering/compact: based on magnitude.
    /// </summary>
    private static int ComputeCompactExponent(double n, string notation)
    {
        if (string.Equals(notation, "standard", StringComparison.Ordinal))
        {
            return 0;
        }

        var absN = Math.Abs(n);
        if (absN == 0)
        {
            return 0;
        }

        var magnitude = (int)Math.Floor(Math.Log10(absN));

        return notation switch
        {
            "scientific" => magnitude,
            "engineering" => magnitude - (magnitude % 3),
            "compact" => magnitude < 4 ? 0 : magnitude - (magnitude % 3),
            _ => 0
        };
    }

    private static string ResolveFrenchCardinal(double n, long i, int v, int e)
    {
        // CLDR French: one → i = 0,1 and e = 0
        if (i is 0 or 1 && e == 0)
        {
            return "one";
        }
        // CLDR French: many → e not in 0..5, OR (e == 0 and n != 0 and n % 1000000 == 0 and v == 0)
        if (e is < 0 or > 5)
        {
            return "many";
        }

        if (e == 0 && v == 0 && n != 0 && n % 1000000 == 0)
        {
            return "many";
        }

        return "other";
    }

    private static string ResolveArabicCardinal(double n, long i)
    {
        if (n == 0)
        {
            return "zero";
        }

        if (n == 1)
        {
            return "one";
        }

        if (n == 2)
        {
            return "two";
        }

        var mod100 = i % 100;
        if (mod100 >= 3 && mod100 <= 10)
        {
            return "few";
        }

        if (mod100 >= 11 && mod100 <= 99)
        {
            return "many";
        }

        return "other";
    }

    private static string ResolveRussianCardinal(long i, int v)
    {
        if (v != 0)
        {
            return "other";
        }

        var mod10 = i % 10;
        var mod100 = i % 100;
        if (mod10 == 1 && mod100 != 11)
        {
            return "one";
        }

        if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14))
        {
            return "few";
        }

        if (mod10 == 0 || (mod10 >= 5 && mod10 <= 9) || (mod100 >= 11 && mod100 <= 14))
        {
            return "many";
        }

        return "other";
    }

    private static string ResolvePolishCardinal(long i, int v)
    {
        if (i == 1 && v == 0)
        {
            return "one";
        }

        if (v != 0)
        {
            return "other";
        }

        var mod10 = i % 10;
        var mod100 = i % 100;
        if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14))
        {
            return "few";
        }

        if ((mod10 is 0 or 1) || (mod10 >= 5 && mod10 <= 9) || (mod100 >= 12 && mod100 <= 14))
        {
            return "many";
        }

        return "other";
    }

    private static string ResolveCzechCardinal(long i, int v)
    {
        if (i == 1 && v == 0)
        {
            return "one";
        }

        if (i >= 2 && i <= 4 && v == 0)
        {
            return "few";
        }

        if (v != 0)
        {
            return "many";
        }

        return "other";
    }

    /// <summary>
    /// Ordinal plural rules.
    /// </summary>
    private static string ResolveOrdinal(double n, string language)
    {
        var i = (long)Math.Abs(Math.Truncate(n));
        var mod10 = i % 10;
        var mod100 = i % 100;

        return language switch
        {
            "en" => mod10 == 1 && mod100 != 11 ? "one" :
                    mod10 == 2 && mod100 != 12 ? "two" :
                    mod10 == 3 && mod100 != 13 ? "few" : "other",

            // Most languages just use "other" for ordinals
            _ => "other"
        };
    }

    /// <summary>
    /// Returns the plural categories available for a locale/type combination.
    /// Categories are ordered per spec: zero, one, two, few, many, other.
    /// </summary>
    internal static string[] GetPluralCategories(string locale, string type)
    {
        var language = ExtractLanguage(locale);

        if (string.Equals(type, "ordinal", StringComparison.Ordinal))
        {
            return language switch
            {
                "en" => ["one", "two", "few", "other"],
                _ => ["other"]
            };
        }

        // Cardinal categories ordered per spec: zero, one, two, few, many, other
        return language switch
        {
            "ar" => ["zero", "one", "two", "few", "many", "other"],
            "ru" or "uk" => ["one", "few", "many", "other"],
            "pl" => ["one", "few", "many", "other"],
            "cs" or "sk" => ["one", "few", "many", "other"],
            "fr" => ["one", "many", "other"],
            "gv" => ["one", "two", "few", "many", "other"],
            "sl" => ["one", "two", "few", "other"],
            "ja" or "zh" or "ko" or "vi" or "th" or "id" or "ms" or "lo" or "km" => ["other"],
            // Default: English-like (one, other)
            _ => ["one", "other"]
        };
    }
}
