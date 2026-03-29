#region

using System.Globalization;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.IntlHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.DateTimeFormat", PrototypeType = typeof(IntlDateTimeFormatPrototype), Length = 0d,
    DisplayName = "DateTimeFormat")]
public sealed partial class IntlDateTimeFormatConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    // Spec Table 7 component names in order (including dayPeriod and fractionalSecondDigits)
    private static readonly string[] ComponentNamesInOrder =
    [
        "weekday", "era", "year", "month", "day", "dayPeriod", "hour", "minute", "second",
        "fractionalSecondDigits", "timeZoneName"
    ];

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);

        var slots = CreateInternalSlots(localesArg, optionsArg);
        var instance = PrepareThisObject(thisValue);
        IntlDateTimeFormatPrototype.InitializeInternalSlots(instance, slots);
        return new JsValue(instance);
    }

    private DateTimeFormatInternalSlots CreateInternalSlots(JsValue localesArg, JsValue optionsArg)
    {
        var (_, resolvedLocale) = ResolveIntlLocales(localesArg, Realm);

        // Per spec: Let options be ? ToObject(options) — use GetOptionsObject for proper ToObject
        var options = IntlOptionHelpers.GetOptionsObject(optionsArg, Realm, "DateTimeFormat");

        // Step 4: localeMatcher
        var localeMatcher = IntlOptionHelpers.GetStringOption(options, "localeMatcher", Realm,
            "DateTimeFormat", ["lookup", "best fit"], "best fit");

        // Parse unicode extension keywords from locale for calendar, numberingSystem, hourCycle
        var unicodeKeywords = IntlUtilities.ParseUnicodeExtensionKeywords(resolvedLocale);
        var baseLocale = IntlUtilities.RemoveUnicodeExtensions(resolvedLocale);

        // Calendar: option overrides unicode extension, option overrides locale
        var calendarOption = ReadCalendarOption(options);
        string calendar;
        if (calendarOption is not null)
        {
            calendar = calendarOption;
        }
        else if (unicodeKeywords.TryGetValue("ca", out var caValues) && caValues.Count > 0)
        {
            var extCalendar = string.Join("-", caValues);
            calendar = IntlUtilities.TryNormalizeCalendar(extCalendar, out var canonical) ? canonical : "gregory";
        }
        else
        {
            calendar = "gregory";
        }

        // NumberingSystem: option overrides unicode extension, option overrides locale
        var numberingSystemOption = ReadNumberingSystemOption(options);
        string numberingSystem;
        if (numberingSystemOption is not null)
        {
            numberingSystem = numberingSystemOption;
        }
        else if (unicodeKeywords.TryGetValue("nu", out var nuValues) && nuValues.Count > 0)
        {
            var extNu = string.Join("-", nuValues);
            numberingSystem = IntlUtilities.TryNormalizeSupportedNumberingSystem(extNu, out var canonical) &&
                              !IsNumberingSystemAlias(canonical)
                ? canonical
                : "latn";
        }
        else
        {
            numberingSystem = "latn";
        }

        // Step 12: hour12
        var hour12 = IntlOptionHelpers.GetBooleanOption(options, "hour12");

        // Step 13: hourCycle
        string? hourCycleOption = null;
        if (options is not null && options.TryGetProperty("hourCycle", out var hcValue) && !hcValue.IsUndefined)
        {
            var hcString = JsValueToString(hcValue, Realm);
            if (hcString is "h11" or "h12" or "h23" or "h24")
            {
                hourCycleOption = hcString;
            }
        }

        // Resolve hourCycle: option overrides locale extension
        string hourCycle;
        if (hourCycleOption is not null)
        {
            hourCycle = hourCycleOption;
        }
        else if (unicodeKeywords.TryGetValue("hc", out var hcValues) && hcValues.Count > 0)
        {
            var extHc = hcValues[0];
            hourCycle = extHc is "h11" or "h12" or "h23" or "h24" ? extHc : GetDefaultHourCycle(baseLocale);
        }
        else
        {
            hourCycle = GetDefaultHourCycle(baseLocale);
        }

        // hour12 overrides hourCycle per spec
        if (hour12 == true)
        {
            if (hourCycle is "h23" or "h24")
            {
                // Switch to locale-preferred 12-hour cycle
                hourCycle = GetPreferred12HourCycle(baseLocale);
            }
        }
        else if (hour12 == false)
        {
            if (hourCycle is "h11" or "h12")
            {
                hourCycle = "h23";
            }
        }

        // Step 29: timeZone
        var timeZoneValue = options is null ? JsValue.Undefined : GetOption(options, "timeZone");
        var timeZone = IntlUtilities.NormalizeTimeZone(timeZoneValue, Realm);
        string? displayTimeZone = null;
        if (options is not null && options.TryGetProperty("__temporalDisplayTimeZone", out var displayTimeZoneValue) &&
            displayTimeZoneValue.TryGetString(out var displayTimeZoneString) &&
            !string.IsNullOrWhiteSpace(displayTimeZoneString))
        {
            displayTimeZone = displayTimeZoneString;
        }

        // Step 36: Read components in table order
        var components = new Dictionary<string, string>(StringComparer.Ordinal);
        var hasExplicitFormatComponents = false;

        foreach (var component in ComponentNamesInOrder)
        {
            if (string.Equals(component, "fractionalSecondDigits", StringComparison.Ordinal))
            {
                var fsd = ReadFractionalSecondDigits(options);
                if (fsd is not null)
                {
                    components["fractionalSecondDigits"] = fsd.Value.ToString(CultureInfo.InvariantCulture);
                    hasExplicitFormatComponents = true;
                }
            }
            else if (string.Equals(component, "dayPeriod", StringComparison.Ordinal))
            {
                var dp = ReadDayPeriodOption(options);
                if (dp is not null)
                {
                    components["dayPeriod"] = dp;
                    hasExplicitFormatComponents = true;
                }
            }
            else
            {
                var value = ReadComponentOption(options, component);
                if (value is not null)
                {
                    components[component] = value;
                    hasExplicitFormatComponents = true;
                }
            }
        }

        // Step 37: formatMatcher
        var formatMatcher = IntlOptionHelpers.GetStringOption(options, "formatMatcher", Realm,
            "DateTimeFormat", ["basic", "best fit"], "best fit");

        // Step 38-39: dateStyle
        var dateStyle = ReadStyleOption(options, "dateStyle");

        // Step 40-41: timeStyle
        var timeStyle = ReadStyleOption(options, "timeStyle");

        // Step 43: If dateStyle or timeStyle is not undefined and hasExplicitFormatComponents, throw TypeError
        if ((dateStyle is not null || timeStyle is not null) && hasExplicitFormatComponents)
        {
            throw ThrowTypeError(
                "Intl.DateTimeFormat dateStyle/timeStyle options cannot be used with explicit format components",
                realm: Realm);
        }

        // Per spec: If dateStyle and timeStyle are both undefined and no components are set,
        // apply defaults. For "any"/"date" defaults, add year/month/day.
        if (dateStyle is null && timeStyle is null && !hasExplicitFormatComponents)
        {
            components["year"] = "numeric";
            components["month"] = "numeric";
            components["day"] = "numeric";
        }

        // Build the resolved locale tag - strip all unicode extensions that aren't relevant
        // DateTimeFormat only uses ca, hc, nu; all others must be stripped
        // Per spec: hour12 option also suppresses hc extension in the resolved locale
        var finalLocale = BuildResolvedLocale(baseLocale, unicodeKeywords, calendar, hourCycle,
            numberingSystem, calendarOption, hourCycleOption, numberingSystemOption, hour12);

        // Determine hour12 for resolved options
        bool? resolvedHour12 = null;
        if (components.ContainsKey("hour") || timeStyle is not null)
        {
            resolvedHour12 = hourCycle is "h11" or "h12";
        }

        return new DateTimeFormatInternalSlots
        {
            Locale = finalLocale,
            TimeZone = timeZone,
            HourCycle = hourCycle,
            Calendar = calendar,
            NumberingSystem = numberingSystem,
            DateStyle = dateStyle,
            TimeStyle = timeStyle,
            Hour12 = resolvedHour12,
            DisplayTimeZone = displayTimeZone,
            Components = components
        };
    }

    private static JsValue GetOption(IJsPropertyAccessor options, string propertyName)
    {
        return options.TryGetProperty(propertyName, out var value) ? value : JsValue.Undefined;
    }

    private string? ReadCalendarOption(IJsPropertyAccessor? options)
    {
        if (options is null || !options.TryGetProperty("calendar", out var value) || value.IsUndefined)
        {
            return null;
        }

        var calendar = JsValueToString(value, Realm);

        // Per spec: only throw RangeError if the value doesn't match Unicode type nonterminal
        if (!IsValidUnicodeTypeNonterminal(calendar))
        {
            throw ThrowRangeError($"Invalid calendar value '{calendar}' for Intl.DateTimeFormat", realm: Realm);
        }

        // If it's structurally valid but not supported, return null (fall through to unicode ext/default)
        return IntlUtilities.TryNormalizeCalendar(calendar, out var canonical) ? canonical : null;
    }

    private string? ReadNumberingSystemOption(IJsPropertyAccessor? options)
    {
        if (options is null || !options.TryGetProperty("numberingSystem", out var value) || value.IsUndefined)
        {
            return null;
        }

        var system = JsValueToString(value, Realm);

        // Per spec: If numberingSystem does not match the Unicode Locale Identifier type nonterminal, throw RangeError
        if (!IsValidUnicodeTypeNonterminal(system))
        {
            throw ThrowRangeError(
                $"Invalid numberingSystem '{system}' for Intl.DateTimeFormat", realm: Realm);
        }

        // If structurally valid but not a recognized numbering system, return null (fall through)
        return IntlUtilities.TryNormalizeSupportedNumberingSystem(system, out var canonical) ? canonical : null;
    }

    private string? ReadStyleOption(IJsPropertyAccessor? options, string propertyName)
    {
        if (options is null || !options.TryGetProperty(propertyName, out var value) || value.IsUndefined)
        {
            return null;
        }

        var stringValue = JsValueToString(value, Realm);

        if (stringValue is not ("full" or "long" or "medium" or "short"))
        {
            throw ThrowRangeError(
                $"Invalid value '{stringValue}' for option '{propertyName}' on Intl.DateTimeFormat", realm: Realm);
        }

        return stringValue;
    }

    private string? ReadDayPeriodOption(IJsPropertyAccessor? options)
    {
        if (options is null || !options.TryGetProperty("dayPeriod", out var value) || value.IsUndefined)
        {
            return null;
        }

        var stringValue = JsValueToString(value, Realm);

        if (stringValue is not ("narrow" or "short" or "long"))
        {
            throw ThrowRangeError(
                $"Invalid value '{stringValue}' for option 'dayPeriod' on Intl.DateTimeFormat", realm: Realm);
        }

        return stringValue;
    }

    private int? ReadFractionalSecondDigits(IJsPropertyAccessor? options)
    {
        if (options is null || !options.TryGetProperty("fractionalSecondDigits", out var value) ||
            value.IsUndefined)
        {
            return null;
        }

        var number = JsOps.ToNumber(value);
        if (double.IsNaN(number) || number < 1 || number > 3)
        {
            throw ThrowRangeError(
                "Intl.DateTimeFormat fractionalSecondDigits must be 1, 2, or 3", realm: Realm);
        }

        var intValue = (int)Math.Floor(number);
        if (intValue < 1 || intValue > 3)
        {
            throw ThrowRangeError(
                "Intl.DateTimeFormat fractionalSecondDigits must be 1, 2, or 3", realm: Realm);
        }

        return intValue;
    }

    private string? ReadComponentOption(IJsPropertyAccessor? options, string propertyName)
    {
        if (options is null || !options.TryGetProperty(propertyName, out var value) || value.IsUndefined)
        {
            return null;
        }

        var component = JsValueToString(value, Realm);

        var isAllowed = propertyName switch
        {
            "month" => component is "2-digit" or "numeric" or "narrow" or "short" or "long",
            "weekday" => component is "narrow" or "short" or "long",
            "era" => component is "narrow" or "short" or "long",
            "timeZoneName" => component is "short" or "long" or "shortOffset" or "longOffset" or "shortGeneric" or
                "longGeneric",
            _ => component is "2-digit" or "numeric"
        };

        if (!isAllowed)
        {
            throw ThrowRangeError(
                $"Intl.DateTimeFormat {propertyName} option '{component}' is not supported", realm: Realm);
        }

        return component;
    }

    /// <summary>
    /// Returns the preferred 12-hour cycle for a locale.
    /// Most locales use h12 (1-12), but some (like Japanese) use h11 (0-11).
    /// </summary>
    private static string GetPreferred12HourCycle(string locale)
    {
        // ICU data: Japanese locale uses h11 for 12-hour clock (K pattern letter, 0-11)
        var lang = locale.Split('-')[0].ToLowerInvariant();
        return lang is "ja" or "ko" ? "h11" : "h12";
    }

    private static string GetDefaultHourCycle(string locale)
    {
        // Determine default hour cycle from locale
        try
        {
            var culture = IntlUtilities.ResolveCulture(locale);
            var pattern = culture.DateTimeFormat.ShortTimePattern;
            if (pattern.Contains('h', StringComparison.Ordinal) ||
                pattern.Contains("tt", StringComparison.Ordinal))
            {
                return "h12";
            }

            return "h23";
        }
        catch
        {
            return "h23";
        }
    }

    /// <summary>
    /// BCP 47 numbering system alias keywords that resolve to locale-specific numbering systems.
    /// These are not valid concrete numbering system identifiers for DateTimeFormat.
    /// </summary>
    private static bool IsNumberingSystemAlias(string value)
    {
        return value is "native" or "traditio" or "finance";
    }

    /// <summary>
    /// Validates that a string matches the Unicode Locale Identifier type nonterminal:
    /// type = alphanum{3,8} ("-" alphanum{3,8})*
    /// </summary>
    private static bool IsValidUnicodeTypeNonterminal(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var parts = value.Split('-');
        foreach (var part in parts)
        {
            if (part.Length < 3 || part.Length > 8)
            {
                return false;
            }

            foreach (var ch in part)
            {
                if (!((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Builds the resolved locale string, keeping only relevant unicode extension keys (ca, hc, nu)
    /// and only when they came from the locale (not overridden by options).
    /// </summary>
    private static string BuildResolvedLocale(
        string baseLocale,
        IReadOnlyDictionary<string, List<string>> unicodeKeywords,
        string calendar, string hourCycle, string numberingSystem,
        string? calendarOption, string? hourCycleOption, string? numberingSystemOption,
        bool? hour12)
    {
        // Start with base locale (no unicode extensions)
        var extensions = new List<string>();

        // Keep the ca extension only if:
        // 1. The locale had a ca extension
        // 2. The resolved calendar matches the normalized extension value
        // 3. The calendar is not the default ("gregory")
        // When an option explicitly overrides to a different value, the extension is dropped
        if (unicodeKeywords.TryGetValue("ca", out var caExtValues) && caExtValues.Count > 0)
        {
            var extCalendar = string.Join("-", caExtValues);
            if (IntlUtilities.TryNormalizeCalendar(extCalendar, out var normalizedExtCa) &&
                string.Equals(normalizedExtCa, calendar, StringComparison.Ordinal))
            {
                extensions.Add("ca-" + calendar);
            }
        }

        // Keep the hc extension only if:
        // 1. The locale had an hc extension
        // 2. hour12 option was NOT set (hour12 always suppresses hc extension)
        // 3. If hourCycle option was set, it must match the extension value
        if (hour12 is null && unicodeKeywords.TryGetValue("hc", out var hcExtValues) && hcExtValues.Count > 0)
        {
            var extHc = hcExtValues[0];
            if (hourCycleOption is null ||
                string.Equals(hourCycleOption, extHc, StringComparison.Ordinal))
            {
                extensions.Add("hc-" + hourCycle);
            }
        }

        // Keep the nu extension only if:
        // 1. The locale had a nu extension
        // 2. The resolved numberingSystem matches the normalized extension value
        // 3. The numberingSystem is not the default ("latn")
        if (unicodeKeywords.TryGetValue("nu", out var nuExtValues) && nuExtValues.Count > 0)
        {
            var extNu = string.Join("-", nuExtValues);
            if (IntlUtilities.TryNormalizeSupportedNumberingSystem(extNu, out var normalizedExtNu) &&
                !IsNumberingSystemAlias(normalizedExtNu) &&
                string.Equals(normalizedExtNu, numberingSystem, StringComparison.Ordinal))
            {
                extensions.Add("nu-" + numberingSystem);
            }
        }

        if (extensions.Count == 0)
        {
            return baseLocale;
        }

        return baseLocale + "-u-" + string.Join("-", extensions);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        IntlHelper.ConfigureSupportedLocalesOf(constructor, Realm);
    }
}
