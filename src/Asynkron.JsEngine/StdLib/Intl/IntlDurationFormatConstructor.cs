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
    // Table 1: Duration format units
    // Each entry: (unit, styleProperty, displayProperty, allowedStyles, digitalBase)
    private static readonly (string Unit, string[] AllowedStyles, string DigitalBase)[] UnitTable =
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
        // Intl.DurationFormat step 1: If NewTarget is undefined, throw a TypeError exception.
        constructor.SetInvokeWithContext((args, thisValue, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw ThrowTypeError("Intl.DurationFormat constructor requires 'new'",
                    realm: Realm);
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

        // Per spec option read order: localeMatcher, numberingSystem, style, then per-unit options
        _ = IntlOptionHelpers.GetStringOption(options, "localeMatcher", Realm, "DurationFormat",
            ["lookup", "best fit"], "best fit");

        var numberingSystem = ResolveNumberingSystem(options);

        var style = IntlOptionHelpers.GetStringOption(options, "style", Realm, "DurationFormat",
            ["long", "short", "narrow", "digital"], "short");

        // Process per-unit options
        var unitStyles = new string[10];
        var unitDisplays = new string[10];
        var prevStyle = (string?)null;

        for (var i = 0; i < UnitTable.Length; i++)
        {
            var (unit, allowedStyles, digitalBase) = UnitTable[i];
            var (unitStyle, unitDisplay) = GetDurationUnitOptions(
                unit, options, style, allowedStyles, digitalBase, prevStyle);
            unitStyles[i] = unitStyle;
            unitDisplays[i] = unitDisplay;
            prevStyle = unitStyle;
        }

        // fractionalDigits: GetNumberOption(options, "fractionalDigits", 0, 9, undefined)
        var fractionalDigits = IntlOptionHelpers.GetNumberOption(
            options, "fractionalDigits", 0, 9, null, Realm, "DurationFormat");

        // Resolve numbering system from option, unicode extension, or default
        var (resolvedNumberingSystem, finalLocale) =
            ResolveNumberingSystemAndLocale(numberingSystem, resolvedLocale);

        var instance = PrepareThisObject(thisValue);
        IntlDurationFormatPrototype.InitializeInternalSlots(
            instance, finalLocale, resolvedNumberingSystem, style,
            unitStyles, unitDisplays, fractionalDigits);
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
        // Step 1: Read the unit style option
        var styleValue = IntlOptionHelpers.GetStringOption(options, unit, Realm, "DurationFormat",
            stylesList, null!);
        var style = string.IsNullOrEmpty(styleValue) ? null : styleValue;

        // Step 2-3: Determine defaults
        string displayDefault;

        if (style is null)
        {
            if (baseStyle == "digital")
            {
                // For digital: non-time units get "short", time units get their digitalBase
                style = unit is "hours" or "minutes" or "seconds"
                    ? digitalBase
                    : "short";
            }
            else
            {
                // For non-digital: cascade from previous numeric style
                if (prevStyle is "fractional" or "numeric" or "2-digit")
                {
                    style = Array.IndexOf(stylesList, "numeric") >= 0 ? "numeric" : "fractional";
                }
                else
                {
                    style = baseStyle;
                }
            }
        }

        // Determine display default based on prevStyle
        if (prevStyle is "numeric" or "2-digit" or "fractional")
        {
            displayDefault = "always";
        }
        else
        {
            displayDefault = "auto";
        }

        // Step 4: Read display option
        var display = IntlOptionHelpers.GetStringOption(options, unit + "Display", Realm, "DurationFormat",
            ["auto", "always"], displayDefault);

        // Step 5: Enforce style constraints when previous was numeric/2-digit
        if (prevStyle is "numeric" or "2-digit")
        {
            if (style is not "numeric" and not "2-digit")
            {
                throw ThrowRangeError(
                    $"Intl.DurationFormat: '{style}' style for '{unit}' is not allowed after a numeric-styled unit",
                    realm: Realm);
            }

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

        // Validate against Unicode Locale Identifier type nonterminal: 3*8alphanum *("-" 3*8alphanum)
        if (!IntlUtilities.IsValidUnicodeTypeNonterminal(numberingSystem))
        {
            throw ThrowRangeError($"Invalid numbering system '{numberingSystem}'", realm: Realm);
        }

        // Normalize if possible, otherwise keep the well-formed value as-is
        return IntlUtilities.TryNormalizeNumberingSystem(numberingSystem, out var canonical)
            ? canonical
            : numberingSystem;
    }

    private static (string NumberingSystem, string Locale) ResolveNumberingSystemAndLocale(
        string? optionNu,
        string resolvedLocale)
    {
        // Extract unicode extension numbering system from locale (e.g., "en-u-nu-arab" → "arab")
        var unicodeKeywords = IntlUtilities.ParseUnicodeExtensionKeywords(resolvedLocale);
        string? extensionNu = null;
        if (unicodeKeywords.TryGetValue("nu", out var nuValues) && nuValues.Count > 0)
        {
            extensionNu = nuValues[0];
        }

        var baseLocale = IntlUtilities.RemoveUnicodeExtensions(resolvedLocale);

        // Resolution: option > unicode extension > default
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
