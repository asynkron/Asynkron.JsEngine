#region

using System.Globalization;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.RelativeTimeFormat", ToStringTag = "Intl.RelativeTimeFormat")]
public sealed partial class IntlRelativeTimeFormatPrototype
{
    private const string BrandKey = "__relativeTimeFormat__";

    internal static void InitializeInternalSlots(JsObject instance, string locale, string numberingSystem,
        string numeric, string style)
    {
        instance.SetProperty(BrandKey, true);
        instance.SetProperty("__locale__", locale);
        instance.SetProperty("__numberingSystem__", numberingSystem);
        instance.SetProperty("__numeric__", numeric);
        instance.SetProperty("__style__", style);
    }

    [JsHostMethod("format", Length = 2d)]
    private JsValue Format(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var instance = ValidateReceiver(thisValue);
        var (value, unit, locale, style, numeric, numberingSystem) = ExtractFormatArgs(instance, args);
        return new JsValue(FormatRelativeTime(value, unit, locale, style, numeric, numberingSystem));
    }

    [JsHostMethod("formatToParts", Length = 2d)]
    private JsValue FormatToParts(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var instance = ValidateReceiver(thisValue);
        var (value, unit, locale, style, numeric, numberingSystem) = ExtractFormatArgs(instance, args);
        return FormatRelativeTimeToParts(value, unit, locale, style, numeric, numberingSystem);
    }

    private (double Value, string Unit, string Locale, string Style, string Numeric, string NumberingSystem)
        ExtractFormatArgs(JsObject instance, IReadOnlyList<JsValue> args)
    {
        var context = EvaluationContext.Current;
        var value = args.Count > 0 ? JsOps.ToNumber(args[0], context) : double.NaN;

        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (!double.IsFinite(value))
        {
            throw ThrowRangeError("Value must be finite for RelativeTimeFormat", realm: Realm);
        }

        var unitArg = args.Count > 1
            ? JsValueToString(args[1], Realm)
            : throw ThrowTypeError("RelativeTimeFormat requires a unit argument", realm: Realm);

        var unit = SingularRelativeTimeUnit(unitArg);
        var locale = GetStringSlot(instance, "__locale__", "en");
        var style = GetStringSlot(instance, "__style__", "long");
        var numeric = GetStringSlot(instance, "__numeric__", "always");
        var numberingSystem = GetStringSlot(instance, "__numberingSystem__", "latn");

        return (value, unit, locale, style, numeric, numberingSystem);
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var instance = ValidateReceiver(thisValue);
        var obj = new JsObject(Realm.ObjectPrototype);
        const string operation = "Intl.RelativeTimeFormat.prototype.resolvedOptions";

        CreateDataPropertyOrThrow(obj, "locale", GetStringSlot(instance, "__locale__", "en"), Realm, operation);
        CreateDataPropertyOrThrow(obj, "style", GetStringSlot(instance, "__style__", "long"), Realm, operation);
        CreateDataPropertyOrThrow(obj, "numeric", GetStringSlot(instance, "__numeric__", "always"), Realm,
            operation);
        CreateDataPropertyOrThrow(obj, "numberingSystem", GetStringSlot(instance, "__numberingSystem__", "latn"),
            Realm, operation);

        return new JsValue(obj);
    }

    private JsObject ValidateReceiver(JsValue thisValue)
    {
        return thisValue.EnsureBrand(BrandKey, Realm,
            "Intl.RelativeTimeFormat method called on incompatible receiver");
    }

    // ------- SingularRelativeTimeUnit -------

    private string SingularRelativeTimeUnit(string unit)
    {
        return unit switch
        {
            "second" or "seconds" => "second",
            "minute" or "minutes" => "minute",
            "hour" or "hours" => "hour",
            "day" or "days" => "day",
            "week" or "weeks" => "week",
            "month" or "months" => "month",
            "quarter" or "quarters" => "quarter",
            "year" or "years" => "year",
            _ => throw ThrowRangeError($"Invalid unit argument '{unit}' for RelativeTimeFormat", realm: Realm)
        };
    }

    // ------- Format -------

    private static string FormatRelativeTime(double value, string unit, string locale, string style,
        string numeric, string numberingSystem)
    {
        if (string.Equals(numeric, "auto", StringComparison.Ordinal))
        {
            var special = TryGetAutoStringForFormat(value, unit, locale);
            if (special is not null)
            {
                return special;
            }
        }

        var absValue = Math.Abs(value);
        var formattedNumber = FormatNumberForDisplay(absValue, locale, numberingSystem);
        var unitDisplay = GetUnitDisplay(absValue, unit, locale, style);

        if (IsNegativeOrNegativeZero(value))
        {
            return ApplyPattern(GetPastPattern(locale), formattedNumber, unitDisplay);
        }

        return ApplyPattern(GetFuturePattern(locale), formattedNumber, unitDisplay);
    }

    // ------- FormatToParts -------

    private JsValue FormatRelativeTimeToParts(double value, string unit, string locale, string style,
        string numeric, string numberingSystem)
    {
        if (string.Equals(numeric, "auto", StringComparison.Ordinal))
        {
            var special = TryGetAutoStringForParts(value, unit, locale);
            if (special is not null)
            {
                var literalParts = new JsArray(Realm);
                literalParts.Push(CreateLiteralPart(special));
                return JsValue.FromJsArray(literalParts);
            }
        }

        var absValue = Math.Abs(value);
        var formattedNumber = FormatNumberForDisplay(absValue, locale, numberingSystem);

        // For formatToParts, we need the "other" plural category for non-integer values
        // (e.g., Polish 123456.78 → "other" not "many")
        var pluralValue = absValue;
        var unitDisplay = GetUnitDisplay(pluralValue, unit, locale, style);

        var culture = IntlUtilities.ResolveCulture(locale);
        var decimalSep = culture.NumberFormat.NumberDecimalSeparator;
        var groupSep = culture.NumberFormat.NumberGroupSeparator;

        var parts = new JsArray(Realm);

        if (IsNegativeOrNegativeZero(value))
        {
            AddNumberParts(parts, formattedNumber, unit, decimalSep, groupSep);
            parts.Push(CreateLiteralPart($" {unitDisplay}" + GetPastSuffix(locale)));
        }
        else
        {
            parts.Push(CreateLiteralPart(GetFuturePrefix(locale)));
            AddNumberParts(parts, formattedNumber, unit, decimalSep, groupSep);
            parts.Push(CreateLiteralPart($" {unitDisplay}"));
        }

        return JsValue.FromJsArray(parts);
    }

    private JsObject CreateLiteralPart(string value)
    {
        var part = new JsObject(Realm.ObjectPrototype);
        CreateDataProperty(part, "type", "literal");
        CreateDataProperty(part, "value", value);
        return part;
    }

    private void AddNumberParts(JsArray parts, string formattedNumber, string unit, string decimalSep,
        string groupSep)
    {
        var afterDecimal = false;
        var i = 0;
        while (i < formattedNumber.Length)
        {
            if (char.IsDigit(formattedNumber[i]))
            {
                var start = i;
                while (i < formattedNumber.Length && char.IsDigit(formattedNumber[i]))
                {
                    i++;
                }

                var part = new JsObject(Realm.ObjectPrototype);
                CreateDataProperty(part, "type", afterDecimal ? "fraction" : "integer");
                CreateDataProperty(part, "value", formattedNumber[start..i]);
                CreateDataProperty(part, "unit", unit);
                parts.Push(part);
            }
            else if (formattedNumber.AsSpan(i).StartsWith(decimalSep.AsSpan(), StringComparison.Ordinal))
            {
                var part = new JsObject(Realm.ObjectPrototype);
                CreateDataProperty(part, "type", "decimal");
                CreateDataProperty(part, "value", decimalSep);
                CreateDataProperty(part, "unit", unit);
                parts.Push(part);
                afterDecimal = true;
                i += decimalSep.Length;
            }
            else if (groupSep.Length > 0 &&
                     formattedNumber.AsSpan(i).StartsWith(groupSep.AsSpan(), StringComparison.Ordinal))
            {
                var part = new JsObject(Realm.ObjectPrototype);
                CreateDataProperty(part, "type", "group");
                CreateDataProperty(part, "value", groupSep);
                CreateDataProperty(part, "unit", unit);
                parts.Push(part);
                i += groupSep.Length;
            }
            else
            {
                // Unknown separator character
                var part = new JsObject(Realm.ObjectPrototype);
                CreateDataProperty(part, "type", "group");
                CreateDataProperty(part, "value", formattedNumber[i].ToString());
                CreateDataProperty(part, "unit", unit);
                parts.Push(part);
                i++;
            }
        }
    }

    private static void CreateDataProperty(JsObject obj, string key, string value)
    {
        obj.DefineProperty(key,
            new PropertyDescriptor
            {
                Value = value, Writable = true, Enumerable = true, Configurable = true
            });
    }

    // ------- Number formatting -------

    private static string FormatNumberForDisplay(double value, string locale, string numberingSystem)
    {
        var culture = IntlUtilities.ResolveCulture(locale);

        // Apply numbering system if not latin
        if (!string.Equals(numberingSystem, "latn", StringComparison.Ordinal))
        {
            var nuLocale = IntlUtilities.RemoveUnicodeExtensions(locale) + "-u-nu-" + numberingSystem;
            culture = IntlUtilities.ResolveCulture(nuLocale);
        }

        // CLDR minimumGroupingDigits: some locales (pl, fr, etc.) require 2+ digits
        // before the first group separator. Use locale number formatting with grouping.
        var minGroupingDigits = GetMinimumGroupingDigits(locale);

        if (value == Math.Truncate(value))
        {
            var longVal = (long)Math.Abs(value);
            var formatted = longVal.ToString("N0", culture);

            // Strip group separators if the number doesn't meet minimum grouping threshold
            if (minGroupingDigits > 1 && !MeetsMinimumGrouping(longVal, minGroupingDigits))
            {
                formatted = StripGroupSeparators(formatted, culture);
            }

            return formatted;
        }

        // Non-integer: preserve fractional digits with locale formatting
        var fracStr = value.ToString("R", CultureInfo.InvariantCulture);
        var dotIdx = fracStr.IndexOf('.', StringComparison.Ordinal);
        var fracDigits = dotIdx >= 0 ? fracStr.Length - dotIdx - 1 : 0;

        return value.ToString($"N{fracDigits}", culture);
    }

    private static int GetMinimumGroupingDigits(string locale)
    {
        var lang = ExtractLanguage(locale);
        // CLDR minimumGroupingDigits: most locales use 1, but some use 2
        return lang switch
        {
            "pl" or "fr" or "pt" or "bg" or "cs" or "sk" or "sl" or "hr" or "sr"
                or "uk" or "be" or "lt" or "lv" or "et" or "fi" or "sv" or "nb"
                or "nn" or "da" or "is" => 2,
            _ => 1
        };
    }

    private static bool MeetsMinimumGrouping(long value, int minGroupingDigits)
    {
        // For minGroupingDigits=2, the number must have 2+ digits before the first group
        // e.g., for groups of 3: need 5+ digits (10000+) for first separator
        var digitCount = value == 0 ? 1 : (int)Math.Floor(Math.Log10(value)) + 1;
        // First group is 3 digits from right. Digits before first group = total - 3.
        // Need at least minGroupingDigits digits before the first group.
        return digitCount - 3 >= minGroupingDigits;
    }

    private static string StripGroupSeparators(string formatted, CultureInfo culture)
    {
        return formatted.Replace(culture.NumberFormat.NumberGroupSeparator, "", StringComparison.Ordinal);
    }

    // ------- Locale patterns -------

    private static bool IsNegativeOrNegativeZero(double value)
    {
        return value < 0 || (value == 0 && double.IsNegative(value));
    }

    private static string ApplyPattern(string pattern, string number, string unitDisplay)
    {
        return pattern.Replace("{0}", number).Replace("{1}", unitDisplay);
    }

    private static string GetFuturePattern(string locale)
    {
        var lang = ExtractLanguage(locale);
        return lang switch
        {
            "pl" => "za {0} {1}",
            _ => "in {0} {1}"
        };
    }

    private static string GetPastPattern(string locale)
    {
        var lang = ExtractLanguage(locale);
        return lang switch
        {
            "pl" => "{0} {1} temu",
            _ => "{0} {1} ago"
        };
    }

    private static string GetFuturePrefix(string locale)
    {
        var lang = ExtractLanguage(locale);
        return lang switch
        {
            "pl" => "za ",
            _ => "in "
        };
    }

    private static string GetPastSuffix(string locale)
    {
        var lang = ExtractLanguage(locale);
        return lang switch
        {
            "pl" => " temu",
            _ => " ago"
        };
    }

    // ------- Unit display names -------

    private static string GetUnitDisplay(double absValue, string unit, string locale, string style)
    {
        var lang = ExtractLanguage(locale);
        var plural = IntlPluralRulesPrototype.ResolvePlural(absValue, "cardinal", locale);

        return style switch
        {
            "short" => GetShortUnitDisplay(unit, plural, lang),
            "narrow" => GetNarrowUnitDisplay(unit, plural, lang),
            _ => GetLongUnitDisplay(unit, plural, lang)
        };
    }

    private static string GetLongUnitDisplay(string unit, string plural, string lang)
    {
        if (string.Equals(lang, "pl", StringComparison.Ordinal))
        {
            return GetPolishLongUnitDisplay(unit, plural);
        }

        // English
        if (string.Equals(plural, "one", StringComparison.Ordinal))
        {
            return unit; // "second", "minute", etc.
        }

        return unit + "s"; // "seconds", "minutes", etc.
    }

    private static string GetPolishLongUnitDisplay(string unit, string plural)
    {
        return (unit, plural) switch
        {
            ("second", "one") => "sekundę",
            ("second", "few") => "sekundy",
            ("second", "other") => "sekundy",
            ("second", _) => "sekund",
            ("minute", "one") => "minutę",
            ("minute", "few") => "minuty",
            ("minute", "other") => "minuty",
            ("minute", _) => "minut",
            ("hour", "one") => "godzinę",
            ("hour", "few") => "godziny",
            ("hour", "other") => "godziny",
            ("hour", _) => "godzin",
            ("day", "one") => "dzień",
            ("day", "other") => "dnia",
            ("day", _) => "dni",
            ("week", "one") => "tydzień",
            ("week", "few") => "tygodnie",
            ("week", "other") => "tygodnia",
            ("week", _) => "tygodni",
            ("month", "one") => "miesiąc",
            ("month", "few") => "miesiące",
            ("month", "other") => "miesiąca",
            ("month", _) => "miesięcy",
            ("quarter", "one") => "kwartał",
            ("quarter", "few") => "kwartały",
            ("quarter", "other") => "kwartału",
            ("quarter", _) => "kwartałów",
            ("year", "one") => "rok",
            ("year", "few") => "lata",
            ("year", "other") => "roku",
            ("year", _) => "lat",
            _ => unit
        };
    }

    private static string GetShortUnitDisplay(string unit, string plural, string lang)
    {
        if (string.Equals(lang, "pl", StringComparison.Ordinal))
        {
            return GetPolishShortUnitDisplay(unit, plural);
        }

        return (unit, plural) switch
        {
            ("second", _) => "sec.",
            ("minute", _) => "min.",
            ("hour", _) => "hr.",
            ("day", "one") => "day",
            ("day", _) => "days",
            ("week", _) => "wk.",
            ("month", _) => "mo.",
            ("quarter", "one") => "qtr.",
            ("quarter", _) => "qtrs.",
            ("year", _) => "yr.",
            _ => unit
        };
    }

    private static string GetPolishShortUnitDisplay(string unit, string plural)
    {
        return (unit, plural) switch
        {
            ("second", _) => "sek.",
            ("minute", _) => "min",
            ("hour", _) => "godz.",
            ("day", "one") => "dzień",
            ("day", "other") => "dnia",
            ("day", _) => "dni",
            ("week", "one") => "tydz.",
            ("week", _) => "tyg.",
            ("month", _) => "mies.",
            ("quarter", _) => "kw.",
            ("year", "one") => "rok",
            ("year", "few") => "lata",
            ("year", "other") => "roku",
            ("year", _) => "lat",
            _ => unit
        };
    }

    private static string GetNarrowUnitDisplay(string unit, string plural, string lang)
    {
        if (string.Equals(lang, "pl", StringComparison.Ordinal))
        {
            return GetPolishNarrowUnitDisplay(unit, plural);
        }

        return (unit, plural) switch
        {
            ("second", _) => "s",
            ("minute", _) => "m",
            ("hour", _) => "h",
            ("day", _) => "d",
            ("week", _) => "w",
            ("month", _) => "mo.",
            ("quarter", "one") => "qtr.",
            ("quarter", _) => "qtrs.",
            ("year", _) => "yr.",
            _ => unit
        };
    }

    private static string GetPolishNarrowUnitDisplay(string unit, string plural)
    {
        return (unit, plural) switch
        {
            ("second", _) => "s",
            ("minute", _) => "min",
            ("hour", _) => "g.",
            ("day", "one") => "dzień",
            ("day", "other") => "dnia",
            ("day", _) => "dni",
            ("week", "one") => "tydz.",
            ("week", _) => "tyg.",
            ("month", _) => "mies.",
            ("quarter", _) => "kw.",
            ("year", "one") => "rok",
            ("year", "few") => "lata",
            ("year", "other") => "roku",
            ("year", _) => "lat",
            _ => unit
        };
    }

    // ------- Auto numeric: special strings -------
    // format() uses all auto strings (including ones that look numeric like "in 1 second")
    // formatToParts() only uses truly non-numeric auto strings (like "yesterday", "now")

    private static string? TryGetAutoStringForFormat(double value, string unit, string locale)
    {
        // -0 should be treated as 0 for auto lookup
        var lookupValue = (value == 0 && double.IsNegative(value)) ? 0.0 : value;

        if (lookupValue != -1 && lookupValue != 0 && lookupValue != 1)
        {
            return null;
        }

        var lang = ExtractLanguage(locale);
        var intValue = (int)lookupValue;

        return lang switch
        {
            "en" => GetEnglishAutoFormat(intValue, unit),
            "pl" => GetPolishAutoFormat(intValue, unit),
            _ => GetEnglishAutoFormat(intValue, unit)
        };
    }

    private static string? TryGetAutoStringForParts(double value, string unit, string locale)
    {
        // -0 should be treated as 0 for auto lookup
        var lookupValue = (value == 0 && double.IsNegative(value)) ? 0.0 : value;

        // formatToParts only returns literal auto strings for truly non-numeric forms
        if (lookupValue != -1 && lookupValue != 0 && lookupValue != 1)
        {
            return null;
        }

        var lang = ExtractLanguage(locale);
        var intValue = (int)lookupValue;

        return lang switch
        {
            "en" => GetEnglishAutoParts(intValue, unit),
            "pl" => GetPolishAutoParts(intValue, unit),
            _ => GetEnglishAutoParts(intValue, unit)
        };
    }

    // For format(): all CLDR relative fields including numeric-looking ones
    private static string? GetEnglishAutoFormat(int value, string unit)
    {
        return (unit, value) switch
        {
            ("year", -1) => "last year",
            ("year", 0) => "this year",
            ("year", 1) => "next year",
            ("quarter", -1) => "last quarter",
            ("quarter", 0) => "this quarter",
            ("quarter", 1) => "next quarter",
            ("month", -1) => "last month",
            ("month", 0) => "this month",
            ("month", 1) => "next month",
            ("week", -1) => "last week",
            ("week", 0) => "this week",
            ("week", 1) => "next week",
            ("day", -1) => "yesterday",
            ("day", 0) => "today",
            ("day", 1) => "tomorrow",
            ("hour", -1) => "1 hour ago",
            ("hour", 0) => "this hour",
            ("hour", 1) => "in 1 hour",
            ("minute", -1) => "1 minute ago",
            ("minute", 0) => "this minute",
            ("minute", 1) => "in 1 minute",
            ("second", -1) => "1 second ago",
            ("second", 0) => "now",
            ("second", 1) => "in 1 second",
            _ => null
        };
    }

    // For formatToParts(): only truly non-numeric auto strings
    private static string? GetEnglishAutoParts(int value, string unit)
    {
        return (unit, value) switch
        {
            ("year", -1) => "last year",
            ("year", 0) => "this year",
            ("year", 1) => "next year",
            ("quarter", -1) => "last quarter",
            ("quarter", 0) => "this quarter",
            ("quarter", 1) => "next quarter",
            ("month", -1) => "last month",
            ("month", 0) => "this month",
            ("month", 1) => "next month",
            ("week", -1) => "last week",
            ("week", 0) => "this week",
            ("week", 1) => "next week",
            ("day", -1) => "yesterday",
            ("day", 0) => "today",
            ("day", 1) => "tomorrow",
            ("hour", 0) => "this hour",
            ("minute", 0) => "this minute",
            ("second", 0) => "now",
            _ => null
        };
    }

    private static string? GetPolishAutoFormat(int value, string unit)
    {
        return (unit, value) switch
        {
            ("year", -1) => "w zeszłym roku",
            ("year", 0) => "w tym roku",
            ("year", 1) => "w przyszłym roku",
            ("quarter", -1) => "w zeszłym kwartale",
            ("quarter", 0) => "w tym kwartale",
            ("quarter", 1) => "w przyszłym kwartale",
            ("month", -1) => "w zeszłym miesiącu",
            ("month", 0) => "w tym miesiącu",
            ("month", 1) => "w przyszłym miesiącu",
            ("week", -1) => "w zeszłym tygodniu",
            ("week", 0) => "w tym tygodniu",
            ("week", 1) => "w przyszłym tygodniu",
            ("day", -1) => "wczoraj",
            ("day", 0) => "dzisiaj",
            ("day", 1) => "jutro",
            ("second", 0) => "teraz",
            _ => null
        };
    }

    private static string? GetPolishAutoParts(int value, string unit)
    {
        return GetPolishAutoFormat(value, unit);
    }

    private static string ExtractLanguage(string locale)
    {
        var dashIndex = locale.IndexOf('-', StringComparison.Ordinal);
        return dashIndex >= 0 ? locale[..dashIndex] : locale;
    }

    private static string GetStringSlot(JsObject instance, string key, string defaultValue)
    {
        return instance.TryGetProperty(key, out var value) && value.TryGetString(out var str)
            ? str
            : defaultValue;
    }
}
