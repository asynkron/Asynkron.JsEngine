#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.IntlHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.DurationFormat", PrototypeType = typeof(IntlDurationFormatPrototype), Length = 0d,
    DisplayName = "DurationFormat")]
public sealed partial class IntlDurationFormatConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    // Unit definitions per spec Table 1.
    // Each entry: (unit name, allowed styles, digital base style).
    // Date-like units (years/months/weeks/days) have no digitalBase (null).
    // Time-like units have digitalBase = "numeric".
    private static readonly UnitDefinition[] UnitTable =
    [
        new("years", ["long", "short", "narrow"], null),
        new("months", ["long", "short", "narrow"], null),
        new("weeks", ["long", "short", "narrow"], null),
        new("days", ["long", "short", "narrow"], null),
        new("hours", ["long", "short", "narrow", "numeric", "2-digit"], "numeric"),
        new("minutes", ["long", "short", "narrow", "numeric", "2-digit"], "numeric"),
        new("seconds", ["long", "short", "narrow", "numeric", "2-digit"], "numeric"),
        new("milliseconds", ["long", "short", "narrow", "numeric"], "numeric"),
        new("microseconds", ["long", "short", "narrow", "numeric"], "numeric"),
        new("nanoseconds", ["long", "short", "narrow", "numeric"], "numeric"),
    ];

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Step 1: If NewTarget is undefined, throw a TypeError exception.
        if (!thisValue.IsObject || thisValue.AsObject() is not { IsConstructing: true })
        {
            throw ThrowTypeError(
                "Constructor Intl.DurationFormat requires 'new'", realm: Realm);
        }

        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);
        var (_, resolvedLocale) = ResolveIntlLocales(localesArg, Realm);

        // Step 4: Let options be ? GetOptionsObject(options).
        var options = IntlOptionHelpers.GetOptionsObject(optionsArg, Realm, "DurationFormat");

        // Step 5: Let matcher be ? GetOption(options, "localeMatcher", "string",
        //   << "lookup", "best fit" >>, "best fit").
        _ = IntlOptionHelpers.GetStringOption(options, "localeMatcher", Realm, "DurationFormat",
            ["lookup", "best fit"], "best fit");

        // Step 6-7: numberingSystem
        var (numberingSystem, finalLocale) = ResolveNumberingSystem(options, resolvedLocale);

        // Step 13: Let style be ? GetOption(options, "style", "string",
        //   << "long", "short", "narrow", "digital" >>, "short").
        var style = IntlOptionHelpers.GetStringOption(options, "style", Realm, "DurationFormat",
            ["long", "short", "narrow", "digital"], "short");

        // Steps 14-17: Process each unit from Table 1 via GetDurationUnitOptions.
        var unitStyles = new string[UnitTable.Length];
        var unitDisplays = new string[UnitTable.Length];
        string? prevStyle = null;

        for (var i = 0; i < UnitTable.Length; i++)
        {
            var def = UnitTable[i];
            var (unitStyle, unitDisplay) = GetDurationUnitOptions(
                def.Unit, options, style, def.AllowedStyles, def.DigitalBase, prevStyle);
            unitStyles[i] = unitStyle;
            unitDisplays[i] = unitDisplay;
            prevStyle = unitStyle;
        }

        // Step 18: Set durationFormat.[[FractionalDigits]] to
        //   ? GetNumberOption(options, "fractionalDigits", 0, 9, undefined).
        var fractionalDigits = GetFractionalDigitsOption(options);

        var slots = new IntlDurationFormatInternalSlots
        {
            Locale = finalLocale,
            NumberingSystem = numberingSystem,
            Style = style,
            YearsStyle = unitStyles[0],
            YearsDisplay = unitDisplays[0],
            MonthsStyle = unitStyles[1],
            MonthsDisplay = unitDisplays[1],
            WeeksStyle = unitStyles[2],
            WeeksDisplay = unitDisplays[2],
            DaysStyle = unitStyles[3],
            DaysDisplay = unitDisplays[3],
            HoursStyle = unitStyles[4],
            HoursDisplay = unitDisplays[4],
            MinutesStyle = unitStyles[5],
            MinutesDisplay = unitDisplays[5],
            SecondsStyle = unitStyles[6],
            SecondsDisplay = unitDisplays[6],
            MillisecondsStyle = unitStyles[7],
            MillisecondsDisplay = unitDisplays[7],
            MicrosecondsStyle = unitStyles[8],
            MicrosecondsDisplay = unitDisplays[8],
            NanosecondsStyle = unitStyles[9],
            NanosecondsDisplay = unitDisplays[9],
            FractionalDigits = fractionalDigits,
        };

        var instance = PrepareThisObject(thisValue);
        IntlDurationFormatPrototype.InitializeInternalSlots(instance, slots);
        return new JsValue(instance);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        ConfigureSupportedLocalesOf(constructor, Realm);
    }

    /// <summary>
    /// Implements GetDurationUnitOptions per spec.
    /// </summary>
    private (string Style, string Display) GetDurationUnitOptions(
        string unit,
        IJsPropertyAccessor? options,
        string baseStyle,
        string[] stylesList,
        string? digitalBase,
        string? prevStyle)
    {
        // Step 1: Let style be ? GetOption(options, unit, "string", stylesList, undefined).
        var style = GetStringOptionOrUndefined(options, unit, stylesList);

        // Step 2: Let displayDefault be "auto".
        var displayDefault = "auto";

        // Step 3: If style is undefined, then
        if (style is null)
        {
            if (string.Equals(baseStyle, "digital", StringComparison.Ordinal))
            {
                // Step 3b: If digitalBase is not empty, then set style to digitalBase.
                if (digitalBase is not null)
                {
                    style = digitalBase;
                    // Step 3d: Set displayDefault to "always".
                    displayDefault = "always";
                }
                else
                {
                    // Date-like units in digital style: per spec 3e,
                    // set style to baseStyle but since there is no digital for date-like,
                    // this effectively defaults to "short".
                    style = "short";
                }
            }
            else
            {
                // Step 3g: Set style to baseStyle.
                style = baseStyle;
            }

            // Step 3i: If prevStyle is "fractional", "numeric" or "2-digit", then
            if (IsNumericLike(prevStyle))
            {
                // Step 3i.1-2: If style is not numeric-like, set style to "numeric".
                if (!IsNumericLike(style))
                {
                    style = "numeric";
                }
            }
        }

        // Step 4-5: Read display option.
        var displayProperty = unit + "Display";
        var display = IntlOptionHelpers.GetStringOption(options, displayProperty, Realm, "DurationFormat",
            ["auto", "always"], displayDefault);

        // Step 9: If prevStyle is "numeric" or "2-digit", then
        if (string.Equals(prevStyle, "numeric", StringComparison.Ordinal) ||
            string.Equals(prevStyle, "2-digit", StringComparison.Ordinal))
        {
            // Step 9a: If style is not "numeric" or "2-digit", then
            if (!string.Equals(style, "numeric", StringComparison.Ordinal) &&
                !string.Equals(style, "2-digit", StringComparison.Ordinal) &&
                !string.Equals(style, "fractional", StringComparison.Ordinal))
            {
                throw ThrowRangeError(
                    $"Invalid style '{style}' for unit '{unit}' when preceding unit uses numeric or 2-digit style",
                    realm: Realm);
            }

            // Step 9b: If unit is "minutes" or "seconds", then set style to "2-digit".
            if (string.Equals(unit, "minutes", StringComparison.Ordinal) ||
                string.Equals(unit, "seconds", StringComparison.Ordinal))
            {
                style = "2-digit";
            }
        }

        return (style, display);
    }

    private static bool IsNumericLike(string? style)
    {
        return string.Equals(style, "fractional", StringComparison.Ordinal) ||
               string.Equals(style, "numeric", StringComparison.Ordinal) ||
               string.Equals(style, "2-digit", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads a string option, returning null if the property is absent or undefined.
    /// This is needed for unit style options where undefined means "not specified by user".
    /// </summary>
    private string? GetStringOptionOrUndefined(
        IJsPropertyAccessor? options,
        string property,
        string[] allowedValues)
    {
        if (options is null || !options.TryGetProperty(property, out var value) ||
            value.IsUndefined)
        {
            return null;
        }

        var stringValue = JsValueToString(value, Realm);
        foreach (var allowed in allowedValues)
        {
            if (string.Equals(stringValue, allowed, StringComparison.Ordinal))
            {
                return stringValue;
            }
        }

        throw ThrowRangeError(
            $"Invalid value '{stringValue}' for option '{property}' on Intl.DurationFormat", realm: Realm);
    }

    /// <summary>
    /// GetNumberOption(options, "fractionalDigits", 0, 9, undefined).
    /// Returns null when the option is absent or undefined.
    /// </summary>
    private int? GetFractionalDigitsOption(IJsPropertyAccessor? options)
    {
        if (options is null || !options.TryGetProperty("fractionalDigits", out var value) ||
            value.IsUndefined)
        {
            return null;
        }

        var number = JsOps.ToNumber(value);
        if (double.IsNaN(number) || number < 0 || number > 9)
        {
            throw ThrowRangeError(
                "Intl.DurationFormat fractionalDigits option must be between 0 and 9", realm: Realm);
        }

        return (int)Math.Floor(number);
    }

    private (string NumberingSystem, string Locale) ResolveNumberingSystem(
        IJsPropertyAccessor? options,
        string resolvedLocale)
    {
        var unicodeKeywords = IntlUtilities.ParseUnicodeExtensionKeywords(resolvedLocale);
        string? extensionNu = null;
        if (unicodeKeywords.TryGetValue("nu", out var nuValues) && nuValues.Count > 0)
        {
            extensionNu = nuValues[0];
        }

        var baseLocale = IntlUtilities.RemoveUnicodeExtensions(resolvedLocale);

        // Read numberingSystem option with ToString conversion (per spec: GetOption with type "string").
        string? optionNu = null;
        if (options is not null && options.TryGetProperty("numberingSystem", out var rawValue) &&
            !rawValue.IsUndefined)
        {
            var numberingSystem = JsValueToString(rawValue, Realm);

            // Per spec: If numberingSystem does not match the Unicode Locale Identifier type nonterminal,
            // throw a RangeError exception.
            if (!IntlUtilities.IsValidUnicodeTypeNonterminal(numberingSystem))
            {
                throw ThrowRangeError(
                    $"Invalid numbering system '{numberingSystem}'", realm: Realm);
            }

            // Only use option if it is a recognized/supported numbering system.
            if (IntlUtilities.TryNormalizeNumberingSystem(numberingSystem, out var canonical))
            {
                optionNu = canonical;
            }
        }

        // Resolution: option > unicode extension > default.
        if (optionNu is not null)
        {
            if (extensionNu is not null &&
                string.Equals(optionNu, extensionNu, StringComparison.Ordinal))
            {
                return (optionNu, resolvedLocale);
            }

            return (optionNu, baseLocale);
        }

        if (extensionNu is not null &&
            IntlUtilities.TryNormalizeNumberingSystem(extensionNu, out var validExtNu))
        {
            return (validExtNu, resolvedLocale);
        }

        return ("latn", baseLocale);
    }

    private readonly record struct UnitDefinition(string Unit, string[] AllowedStyles, string? DigitalBase);
}
