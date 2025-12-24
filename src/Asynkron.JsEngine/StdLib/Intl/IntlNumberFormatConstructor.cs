#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.IntlHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.NumberFormat", PrototypeType = typeof(IntlNumberFormatPrototype), Length = 0d,
    DisplayName = "NumberFormat")]
public sealed partial class IntlNumberFormatConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (_, resolvedLocale) = ResolveIntlLocales(args.GetArgument(0), Realm);
        var options = IntlOptionHelpers.GetOptionsObject(args.GetArgument(1), Realm, "NumberFormat");
        var slots = CreateInternalSlots(resolvedLocale, options);
        var instance = PrepareThisObject(thisValue);
        IntlNumberFormatPrototype.InitializeInternalSlots(instance, slots);
        return new JsValue(instance);
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

    private JsValue SupportedLocalesOf(IReadOnlyList<JsValue> args)
    {
        var result = ResolveSupportedLocales(args.GetArgument(0), args.GetArgument(1), Realm);
        return JsValue.FromJsArray(result);
    }

    private IntlNumberFormatInternalSlots CreateInternalSlots(string locale, IJsPropertyAccessor? options)
    {
        _ = IntlOptionHelpers.GetStringOption(options, "localeMatcher", Realm, "NumberFormat",
            ["lookup", "best fit"], "best fit");
        var style = ResolveStyle(options);
        var numberingSystem = ResolveNumberingSystem(options);
        var culture = IntlUtilities.ResolveCulture(locale);
        string? currency = null;
        var currencyDisplay = "symbol";
        var currencySign = "standard";
        if (style == "currency")
        {
            currency = ResolveCurrency(options);
            currencyDisplay = ResolveCurrencyDisplay(options);
            currencySign = ResolveCurrencySign(options);
        }

        string? unit = null;
        var unitDisplay = "short";
        if (style == "unit")
        {
            unit = ResolveUnit(options);
            unitDisplay = ResolveUnitDisplay(options);
        }

        var notation = IntlOptionHelpers.GetStringOption(options, "notation", Realm, "NumberFormat",
            ["standard", "scientific", "engineering", "compact"], "standard");
        var digits = ResolveDigitOptions(options, style, currency, notation);
        var useGrouping = ResolveUseGrouping(options);
        var signDisplay = IntlOptionHelpers.GetStringOption(options, "signDisplay", Realm, "NumberFormat",
            ["auto", "never", "always", "exceptZero"], "auto");

        return new IntlNumberFormatInternalSlots
        {
            Locale = locale,
            NumberingSystem = numberingSystem,
            Style = style,
            Currency = currency,
            CurrencyDisplay = currencyDisplay,
            CurrencySign = currencySign,
            Unit = unit,
            UnitDisplay = unitDisplay,
            MinimumIntegerDigits = digits.MinimumIntegerDigits,
            MinimumFractionDigits = digits.MinimumFractionDigits,
            MaximumFractionDigits = digits.MaximumFractionDigits,
            MinimumSignificantDigits = digits.MinimumSignificantDigits,
            MaximumSignificantDigits = digits.MaximumSignificantDigits,
            UseGrouping = useGrouping,
            Notation = notation,
            SignDisplay = signDisplay,
            Culture = culture
        };
    }

    private string ResolveStyle(IJsPropertyAccessor? options)
    {
        return IntlOptionHelpers.GetStringOption(options, "style", Realm, "NumberFormat",
            ["decimal", "currency", "percent", "unit"], "decimal");
    }

    private string ResolveNumberingSystem(IJsPropertyAccessor? options)
    {
        if (options is null ||
            !options.TryGetProperty("numberingSystem", out var rawValue) ||
            rawValue.IsUndefined)
        {
            return "latn";
        }

        if (!rawValue.TryGetString(out var numberingSystem))
        {
            throw ThrowTypeError(
                "Intl.NumberFormat numberingSystem option must be a string", realm: Realm);
        }

        return IntlUtilities.TryNormalizeNumberingSystem(numberingSystem, out var canonical)
            ? canonical
            : "latn";
    }

    private string ResolveCurrency(IJsPropertyAccessor? options)
    {
        if (options is null || !options.TryGetProperty("currency", out var value) ||
            value.IsUndefined)
        {
            throw ThrowTypeError(
                "Intl.NumberFormat currency option is required when style is 'currency'", realm: Realm);
        }

        if (!value.TryGetString(out var currency))
        {
            throw ThrowTypeError(
                "Intl.NumberFormat currency option must be a string", realm: Realm);
        }

        if (!IntlUtilities.TryGetCanonicalCurrency(currency, out var canonical))
        {
            throw ThrowRangeError($"Invalid currency code '{currency}'", realm: Realm);
        }

        return canonical;
    }

    private string ResolveCurrencyDisplay(IJsPropertyAccessor? options)
    {
        return IntlOptionHelpers.GetStringOption(options, "currencyDisplay", Realm, "NumberFormat",
            ["code", "symbol", "narrowSymbol", "name"], "symbol");
    }

    private string ResolveCurrencySign(IJsPropertyAccessor? options)
    {
        return IntlOptionHelpers.GetStringOption(options, "currencySign", Realm, "NumberFormat",
            ["standard", "accounting"], "standard");
    }

    private string ResolveUnit(IJsPropertyAccessor? options)
    {
        if (options is null || !options.TryGetProperty("unit", out var value) ||
            value.IsUndefined)
        {
            throw ThrowTypeError(
                "Intl.NumberFormat unit option is required when style is 'unit'", realm: Realm);
        }

        if (!value.TryGetString(out var unit))
        {
            throw ThrowTypeError(
                "Intl.NumberFormat unit option must be a string", realm: Realm);
        }

        if (!IntlUtilities.TryGetCanonicalUnit(unit, out var canonical))
        {
            throw ThrowRangeError($"Invalid unit '{unit}'", realm: Realm);
        }

        return canonical;
    }

    private string ResolveUnitDisplay(IJsPropertyAccessor? options)
    {
        return IntlOptionHelpers.GetStringOption(options, "unitDisplay", Realm, "NumberFormat",
            ["short", "narrow", "long"], "short");
    }

    private bool ResolveUseGrouping(IJsPropertyAccessor? options)
    {
        if (options is null ||
            !options.TryGetProperty("useGrouping", out var value) ||
            value.IsUndefined)
        {
            return true;
        }

        if (value.TryGetBoolean(out var boolValue))
        {
            return boolValue;
        }

        if (value.TryGetString(out var stringValue))
        {
            return stringValue switch
            {
                "always" => true,
                "auto" => true,
                "min2" => true,
                "false" => false,
                "never" => false,
                "true" => true,
                _ => throw ThrowRangeError(
                    $"Invalid value '{stringValue}' for Intl.NumberFormat useGrouping option", realm: Realm)
            };
        }

        throw ThrowTypeError(
            "Intl.NumberFormat useGrouping option must be a boolean or string", realm: Realm);
    }

    private DigitOptions ResolveDigitOptions(
        IJsPropertyAccessor? options,
        string style,
        string? currency,
        string notation)
    {
        var minimumIntegerDigits = GetDigitOption(options, "minimumIntegerDigits", 1, 21, 1);
        var hasMinSig = TryGetDigitOption(options, "minimumSignificantDigits", 1, 21, out var minimumSignificantDigits);
        var hasMaxSig = TryGetDigitOption(options, "maximumSignificantDigits", 1, 21, out var maximumSignificantDigits);

        var currencyDigits = style == "currency" ? GetCurrencyDigits(currency) : 0;
        var minimumFractionDefault = style == "currency" ? currencyDigits : 0;
        var maximumFractionDefault = style switch
        {
            "currency" => currencyDigits,
            "percent" => 0,
            _ => 3
        };

        var minimumFractionDigits = GetDigitOption(options, "minimumFractionDigits", 0, 20, minimumFractionDefault);
        var maximumFractionDigits = GetDigitOption(options, "maximumFractionDigits", minimumFractionDigits, 20,
            Math.Max(minimumFractionDigits, maximumFractionDefault));

        if (hasMinSig || hasMaxSig || notation is "scientific" or "engineering")
        {
            minimumSignificantDigits ??= 1;
            maximumSignificantDigits ??= 21;
            if (minimumSignificantDigits > maximumSignificantDigits)
            {
                throw ThrowRangeError(
                    "minimumSignificantDigits must be less than or equal to maximumSignificantDigits", realm: Realm);
            }

            return new DigitOptions
            {
                MinimumIntegerDigits = minimumIntegerDigits,
                MinimumFractionDigits = minimumFractionDigits,
                MaximumFractionDigits = maximumFractionDigits,
                MinimumSignificantDigits = minimumSignificantDigits,
                MaximumSignificantDigits = maximumSignificantDigits
            };
        }

        if (minimumFractionDigits > maximumFractionDigits)
        {
            throw ThrowRangeError(
                "minimumFractionDigits must be less than or equal to maximumFractionDigits", realm: Realm);
        }

        return new DigitOptions
        {
            MinimumIntegerDigits = minimumIntegerDigits,
            MinimumFractionDigits = minimumFractionDigits,
            MaximumFractionDigits = maximumFractionDigits
        };
    }

    private int GetDigitOption(IJsPropertyAccessor? options, string property, int minimum, int maximum, int fallback)
    {
        if (options is null ||
            !options.TryGetProperty(property, out var value) ||
            value.IsUndefined)
        {
            return fallback;
        }

        var number = JsOps.ToNumber(value);
        if (double.IsNaN(number) || number < minimum || number > maximum)
        {
            throw ThrowRangeError(
                $"Intl.NumberFormat {property} option must be between {minimum} and {maximum}", realm: Realm);
        }

        return (int)Math.Floor(number);
    }

    private bool TryGetDigitOption(
        IJsPropertyAccessor? options,
        string property,
        int minimum,
        int maximum,
        out int? result)
    {
        result = null;
        if (options is null ||
            !options.TryGetProperty(property, out var value) ||
            value.IsUndefined)
        {
            return false;
        }

        var number = JsOps.ToNumber(value);
        if (double.IsNaN(number) || number < minimum || number > maximum)
        {
            throw ThrowRangeError(
                $"Intl.NumberFormat {property} option must be between {minimum} and {maximum}", realm: Realm);
        }

        result = (int)Math.Floor(number);
        return true;
    }

    private static int GetCurrencyDigits(string? currency)
    {
        if (string.IsNullOrEmpty(currency))
        {
            return 2;
        }

        return 2;
    }

    private sealed class DigitOptions
    {
        public int MinimumIntegerDigits { get; init; }
        public int MinimumFractionDigits { get; init; }
        public int MaximumFractionDigits { get; init; }
        public int? MinimumSignificantDigits { get; init; }
        public int? MaximumSignificantDigits { get; init; }
    }
}
