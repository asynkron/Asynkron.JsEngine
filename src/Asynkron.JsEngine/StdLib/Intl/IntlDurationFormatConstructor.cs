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
    // Table 1: units with their allowed styles and digital defaults
    private static readonly (string Unit, string[] StylesList, string DigitalBase)[] UnitTable =
    [
        ("years", ["long", "short", "narrow"], "short"),
        ("months", ["long", "short", "narrow"], "short"),
        ("weeks", ["long", "short", "narrow"], "short"),
        ("days", ["long", "short", "narrow"], "short"),
        ("hours", ["long", "short", "narrow", "numeric", "2-digit"], "numeric"),
        ("minutes", ["long", "short", "narrow", "numeric", "2-digit"], "numeric"),
        ("seconds", ["long", "short", "narrow", "numeric", "2-digit"], "numeric"),
        ("milliseconds", ["long", "short", "narrow", "numeric"], "numeric"),
        ("microseconds", ["long", "short", "narrow", "numeric"], "numeric"),
        ("nanoseconds", ["long", "short", "narrow", "numeric"], "numeric"),
    ];

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ConstructCore(thisValue, args);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        constructor.SetInvokeWithContext((args, thisValue, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw ThrowTypeError("Intl.DurationFormat constructor requires 'new'", realm: Realm);
            }

            return ConstructCore(thisValue, args);
        });

        ConfigureSupportedLocalesOf(constructor, Realm);
    }

    private JsValue ConstructCore(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);

        var (_, resolvedLocale) = ResolveIntlLocales(localesArg, Realm);
        var options = IntlOptionHelpers.GetOptionsObject(optionsArg, Realm, "DurationFormat");

        // Read options in spec order: localeMatcher, numberingSystem, style
        _ = IntlOptionHelpers.GetStringOption(options, "localeMatcher", Realm, "DurationFormat",
            ["lookup", "best fit"], "best fit");

        var numberingSystem = ResolveNumberingSystem(options);

        var baseStyle = IntlOptionHelpers.GetStringOption(options, "style", Realm, "DurationFormat",
            ["long", "short", "narrow", "digital"], "short");

        var (resolvedNumberingSystem, finalLocale) =
            ResolveNumberingSystemAndLocale(numberingSystem, resolvedLocale);

        var instance = PrepareThisObject(thisValue);

        // Process per-unit options
        string? prevStyle = null;
        var unitStyles = new Dictionary<string, string>(10, StringComparer.Ordinal);
        var unitDisplays = new Dictionary<string, string>(10, StringComparer.Ordinal);

        foreach (var (unit, stylesList, digitalBase) in UnitTable)
        {
            var (style, display) = GetDurationUnitOptions(unit, options, baseStyle, stylesList, digitalBase,
                prevStyle);
            unitStyles[unit] = style;
            unitDisplays[unit] = display;
            prevStyle = style;
        }

        // fractionalDigits: 0-9 or undefined
        var fractionalDigits = IntlOptionHelpers.GetNumberOption(options, "fractionalDigits", 0, 9, null, Realm,
            "DurationFormat");

        IntlDurationFormatPrototype.InitializeInternalSlots(instance, finalLocale, resolvedNumberingSystem,
            baseStyle, unitStyles, unitDisplays, fractionalDigits);

        return new JsValue(instance);
    }

    private (string Style, string Display) GetDurationUnitOptions(
        string unit,
        IJsPropertyAccessor? options,
        string baseStyle,
        string[] stylesList,
        string digitalBase,
        string? prevStyle)
    {
        // Read the style option for this unit
        string? style = null;
        if (options is not null && options.TryGetProperty(unit, out var rawStyle) && !rawStyle.IsUndefined)
        {
            style = JsValueToString(rawStyle, Realm);
            if (!stylesList.Contains(style, StringComparer.Ordinal))
            {
                throw ThrowRangeError(
                    $"Invalid value '{style}' for option '{unit}' on Intl.DurationFormat", realm: Realm);
            }
        }

        // Read the display option for this unit
        var displayProp = unit + "Display";
        var display = IntlOptionHelpers.GetStringOption(options, displayProp, Realm, "DurationFormat",
            ["auto", "always"], "auto");

        // If style is undefined, resolve default based on prevStyle and baseStyle
        if (style is null)
        {
            // prevStyle cascade takes priority
            if (prevStyle is "numeric" or "2-digit")
            {
                style = Array.IndexOf(stylesList, "numeric") >= 0 ? "numeric" : baseStyle;
            }
            else if (baseStyle == "digital")
            {
                style = digitalBase;
            }
            else
            {
                style = baseStyle;
            }
        }

        // Validate: if prevStyle is "numeric" or "2-digit", style must be "numeric" or "2-digit"
        if (prevStyle is "numeric" or "2-digit")
        {
            if (style is not "numeric" and not "2-digit")
            {
                throw ThrowRangeError(
                    $"Invalid style '{style}' for '{unit}': must be 'numeric' or '2-digit' after a numeric unit",
                    realm: Realm);
            }

            // Force minutes/seconds to "2-digit" when following numeric/2-digit
            if (unit is "minutes" or "seconds")
            {
                style = "2-digit";
            }
        }

        return (style, display);
    }

    private string? ResolveNumberingSystem(IJsPropertyAccessor? options)
    {
        if (options is null || !options.TryGetProperty("numberingSystem", out var rawValue) ||
            rawValue.IsUndefined)
        {
            return null;
        }

        var numberingSystem = JsValueToString(rawValue, Realm);

        if (!IntlUtilities.IsValidUnicodeTypeNonterminal(numberingSystem))
        {
            throw ThrowRangeError($"Invalid numbering system '{numberingSystem}'", realm: Realm);
        }

        return IntlUtilities.TryNormalizeNumberingSystem(numberingSystem, out var canonical)
            ? canonical
            : numberingSystem;
    }

    private static (string NumberingSystem, string Locale) ResolveNumberingSystemAndLocale(
        string? optionNu,
        string resolvedLocale)
    {
        var unicodeKeywords = IntlUtilities.ParseUnicodeExtensionKeywords(resolvedLocale);
        string? extensionNu = null;
        if (unicodeKeywords.TryGetValue("nu", out var nuValues) && nuValues.Count > 0)
        {
            extensionNu = nuValues[0];
        }

        var baseLocale = IntlUtilities.RemoveUnicodeExtensions(resolvedLocale);

        if (optionNu is not null && IntlUtilities.TryNormalizeNumberingSystem(optionNu, out var canonicalOption))
        {
            if (extensionNu is not null &&
                string.Equals(canonicalOption, extensionNu, StringComparison.Ordinal))
            {
                return (canonicalOption, resolvedLocale);
            }

            return (canonicalOption, baseLocale);
        }

        if (extensionNu is not null &&
            IntlUtilities.TryNormalizeNumberingSystem(extensionNu, out var validExtNu))
        {
            return (validExtNu, resolvedLocale);
        }

        return ("latn", baseLocale);
    }
}
