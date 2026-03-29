#region

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
    private static readonly int[] ValidRoundingIncrements =
        [1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 2500, 5000];

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
        IntlHelper.ConfigureSupportedLocalesOf(constructor, Realm);
    }

    private IntlNumberFormatInternalSlots CreateInternalSlots(string locale, IJsPropertyAccessor? options)
    {
        // Step 1-2: localeMatcher, numberingSystem
        _ = IntlOptionHelpers.GetStringOption(options, "localeMatcher", Realm, "NumberFormat",
            ["lookup", "best fit"], "best fit");
        var numberingSystem = ResolveNumberingSystem(options);
        var (resolvedNumberingSystem, finalLocale) = ResolveNumberingSystemAndLocale(numberingSystem, locale);
        var culture = IntlUtilities.ResolveCulture(finalLocale);

        // Step 3-8: SetNumberFormatUnitOptions (always reads all, regardless of style)
        var style = ResolveStyle(options);

        // Always read currency/currencyDisplay/currencySign for observable side-effects
        string? currency = null;
        var currencyDisplay = "symbol";
        var currencySign = "standard";

        if (options is not null && options.TryGetProperty("currency", out var currencyValue) &&
            !currencyValue.IsUndefined)
        {
            var currencyStr = JsValueToString(currencyValue, Realm);
            if (!IntlUtilities.TryGetCanonicalCurrency(currencyStr, out var canonical))
            {
                throw ThrowRangeError($"Invalid currency code '{currencyStr}'", realm: Realm);
            }

            currency = canonical;
        }
        else if (string.Equals(style, "currency", StringComparison.Ordinal))
        {
            throw ThrowTypeError(
                "Intl.NumberFormat currency option is required when style is 'currency'", realm: Realm);
        }

        currencyDisplay = IntlOptionHelpers.GetStringOption(options, "currencyDisplay", Realm, "NumberFormat",
            ["code", "symbol", "narrowSymbol", "name"], "symbol");
        currencySign = IntlOptionHelpers.GetStringOption(options, "currencySign", Realm, "NumberFormat",
            ["standard", "accounting"], "standard");

        // Always read unit/unitDisplay for observable side-effects
        string? unit = null;
        var unitDisplay = "short";

        if (options is not null && options.TryGetProperty("unit", out var unitValue) &&
            !unitValue.IsUndefined)
        {
            var unitStr = JsValueToString(unitValue, Realm);
            if (!IntlUtilities.TryGetCanonicalUnit(unitStr, out var canonical))
            {
                throw ThrowRangeError($"Invalid unit '{unitStr}'", realm: Realm);
            }

            unit = canonical;
        }
        else if (string.Equals(style, "unit", StringComparison.Ordinal))
        {
            throw ThrowTypeError(
                "Intl.NumberFormat unit option is required when style is 'unit'", realm: Realm);
        }

        unitDisplay = IntlOptionHelpers.GetStringOption(options, "unitDisplay", Realm, "NumberFormat",
            ["short", "narrow", "long"], "short");

        // Step 9: notation
        var notation = IntlOptionHelpers.GetStringOption(options, "notation", Realm, "NumberFormat",
            ["standard", "scientific", "engineering", "compact"], "standard");

        // Step 10: SetNumberFormatDigitOptions
        var digits = ResolveDigitOptions(options, style, currency, notation);

        // Step 11: compactDisplay (after digit options, per spec read order)
        string? compactDisplay = null;
        if (string.Equals(notation, "compact", StringComparison.Ordinal))
        {
            compactDisplay = IntlOptionHelpers.GetStringOption(options, "compactDisplay", Realm, "NumberFormat",
                ["short", "long"], "short");
        }
        else
        {
            // Still read the option for observable side-effects
            _ = IntlOptionHelpers.GetStringOption(options, "compactDisplay", Realm, "NumberFormat",
                ["short", "long"], "short");
        }

        // Step 12: useGrouping
        var useGrouping = ResolveUseGrouping(options, notation);

        // Step 13: signDisplay
        var signDisplay = IntlOptionHelpers.GetStringOption(options, "signDisplay", Realm, "NumberFormat",
            ["auto", "never", "always", "exceptZero", "negative"], "auto");

        return new IntlNumberFormatInternalSlots
        {
            Locale = finalLocale,
            NumberingSystem = resolvedNumberingSystem,
            Style = style,
            Currency = string.Equals(style, "currency", StringComparison.Ordinal) ? currency : null,
            CurrencyDisplay = currencyDisplay,
            CurrencySign = currencySign,
            Unit = string.Equals(style, "unit", StringComparison.Ordinal) ? unit : null,
            UnitDisplay = unitDisplay,
            MinimumIntegerDigits = digits.MinimumIntegerDigits,
            MinimumFractionDigits = digits.MinimumFractionDigits,
            MaximumFractionDigits = digits.MaximumFractionDigits,
            MinimumSignificantDigits = digits.MinimumSignificantDigits,
            MaximumSignificantDigits = digits.MaximumSignificantDigits,
            UseGrouping = useGrouping,
            Notation = notation,
            CompactDisplay = compactDisplay,
            SignDisplay = signDisplay,
            RoundingIncrement = digits.RoundingIncrement,
            RoundingMode = digits.RoundingMode,
            RoundingPriority = digits.RoundingPriority,
            TrailingZeroDisplay = digits.TrailingZeroDisplay,
            RoundingType = digits.RoundingType,
            Culture = culture
        };
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

        if (optionNu is not null && IntlUtilities.TryNormalizeSupportedNumberingSystem(optionNu, out var canonicalOption))
        {
            if (extensionNu is not null &&
                string.Equals(canonicalOption, extensionNu, StringComparison.Ordinal))
            {
                return (canonicalOption, resolvedLocale);
            }

            return (canonicalOption, baseLocale);
        }

        if (extensionNu is not null &&
            IntlUtilities.TryNormalizeSupportedNumberingSystem(extensionNu, out var validExtNu))
        {
            return (validExtNu, resolvedLocale);
        }

        return ("latn", baseLocale);
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

        var numberingSystem = JsValueToString(rawValue, Realm);

        // Validate against Unicode Locale Identifier type nonterminal: 3*8alphanum *("-" 3*8alphanum)
        if (!IntlUtilities.IsValidUnicodeTypeNonterminal(numberingSystem))
        {
            throw ThrowRangeError(
                $"Invalid numbering system '{numberingSystem}'", realm: Realm);
        }

        return IntlUtilities.TryNormalizeNumberingSystem(numberingSystem, out var canonical)
            ? canonical
            : throw ThrowRangeError($"Invalid numbering system '{numberingSystem}'", realm: Realm);
    }

    private string ResolveUseGrouping(IJsPropertyAccessor? options, string notation)
    {
        if (options is null ||
            !options.TryGetProperty("useGrouping", out var value) ||
            value.IsUndefined)
        {
            // Default depends on notation
            return string.Equals(notation, "compact", StringComparison.Ordinal) ? "min2" : "auto";
        }

        // If value is boolean true, return "always"
        if (value.TryGetBoolean(out var boolValue))
        {
            return boolValue ? "always" : "false";
        }

        // If ToBoolean(value) is false, return false (stored as "false")
        if (!JsOps.ToBoolean(value))
        {
            return "false";
        }

        // Convert to string and validate
        var stringValue = JsValueToString(value, Realm);
        return stringValue switch
        {
            "auto" or "always" or "min2" => stringValue,
            "true" or "false" => "auto", // per spec: "true"/"false" strings map to fallback
            _ => throw ThrowRangeError(
                $"Invalid value '{stringValue}' for Intl.NumberFormat useGrouping option", realm: Realm)
        };
    }

    private DigitOptions ResolveDigitOptions(
        IJsPropertyAccessor? options,
        string style,
        string? currency,
        string notation)
    {
        var minimumIntegerDigits = GetDigitOption(options, "minimumIntegerDigits", 1, 21, 1);

        // Currency digit defaults only apply for "standard" notation
        var useCurrencyDefaults = string.Equals(style, "currency", StringComparison.Ordinal)
                                  && string.Equals(notation, "standard", StringComparison.Ordinal);
        var currencyDigits = useCurrencyDefaults ? GetCurrencyDigits(currency) : 0;
        var mnfdDefault = useCurrencyDefaults ? currencyDigits : 0;
        var mxfdDefault = style switch
        {
            _ when useCurrencyDefaults => currencyDigits,
            "percent" => 0,
            _ => string.Equals(notation, "compact", StringComparison.Ordinal) ? 0 : 3
        };

        var hasMinFrac = TryGetDigitOption(options, "minimumFractionDigits", 0, 100, out var rawMinFrac);
        var hasMaxFrac = TryGetDigitOption(options, "maximumFractionDigits", 0, 100, out var rawMaxFrac);
        var hasMinSig = TryGetDigitOption(options, "minimumSignificantDigits", 1, 21, out var minimumSignificantDigits);
        var hasMaxSig = TryGetDigitOption(options, "maximumSignificantDigits", 1, 21, out var maximumSignificantDigits);

        // v3 options
        var roundingIncrement = GetRoundingIncrement(options);
        var roundingMode = IntlOptionHelpers.GetStringOption(options, "roundingMode", Realm, "NumberFormat",
            ["ceil", "floor", "expand", "trunc", "halfCeil", "halfFloor", "halfExpand", "halfTrunc", "halfEven"],
            "halfExpand");
        var roundingPriority = IntlOptionHelpers.GetStringOption(options, "roundingPriority", Realm, "NumberFormat",
            ["auto", "morePrecision", "lessPrecision"], "auto");
        var trailingZeroDisplay = IntlOptionHelpers.GetStringOption(options, "trailingZeroDisplay", Realm, "NumberFormat",
            ["auto", "stripIfInteger"], "auto");

        var hasFD = hasMinFrac || hasMaxFrac;
        var hasSD = hasMinSig || hasMaxSig;

        // Determine needFD, needSD per spec
        bool needFD, needSD;
        if (!string.Equals(roundingPriority, "auto", StringComparison.Ordinal))
        {
            needSD = true;
            needFD = true;
        }
        else if (hasSD)
        {
            needSD = true;
            needFD = false;
        }
        else
        {
            needSD = false;
            needFD = true;
        }

        // Resolve significant digits if needed
        if (needSD)
        {
            minimumSignificantDigits ??= 1;
            maximumSignificantDigits ??= 21;
            if (minimumSignificantDigits > maximumSignificantDigits)
            {
                throw ThrowRangeError(
                    "minimumSignificantDigits must be less than or equal to maximumSignificantDigits", realm: Realm);
            }
        }

        // Resolve fraction digits
        int minimumFractionDigits;
        int maximumFractionDigits;
        string roundingType;

        if (needFD)
        {
            if (hasFD)
            {
                // Keep provided values, may be null
            }
            else
            {
                rawMinFrac = mnfdDefault;
                rawMaxFrac = mxfdDefault;
            }

            // Resolve undefined values per spec
            if (!hasMinFrac && hasFD)
            {
                // mnfd is undefined but mxfd was provided
                minimumFractionDigits = Math.Min(mnfdDefault, rawMaxFrac ?? mxfdDefault);
            }
            else
            {
                minimumFractionDigits = rawMinFrac ?? mnfdDefault;
            }

            if (!hasMaxFrac && hasFD)
            {
                // mxfd is undefined but mnfd was provided
                maximumFractionDigits = Math.Max(mxfdDefault, minimumFractionDigits);
            }
            else
            {
                maximumFractionDigits = rawMaxFrac ?? Math.Max(minimumFractionDigits, mxfdDefault);
            }

            if (minimumFractionDigits > maximumFractionDigits)
            {
                throw ThrowRangeError(
                    "minimumFractionDigits must be less than or equal to maximumFractionDigits", realm: Realm);
            }
        }
        else
        {
            minimumFractionDigits = mnfdDefault;
            maximumFractionDigits = Math.Max(mnfdDefault, mxfdDefault);
        }

        // Determine rounding type
        if (needSD && needFD)
        {
            roundingType = roundingPriority switch
            {
                "morePrecision" => "morePrecision",
                "lessPrecision" => "lessPrecision",
                _ => "morePrecision"
            };
        }
        else if (needSD)
        {
            roundingType = "significantDigits";
        }
        else if (!hasFD && !hasSD && string.Equals(notation, "compact", StringComparison.Ordinal))
        {
            roundingType = "compactRounding";
        }
        else
        {
            roundingType = "fractionDigits";
        }

        // Validate roundingIncrement constraints
        if (roundingIncrement != 1)
        {
            if (!string.Equals(roundingType, "fractionDigits", StringComparison.Ordinal))
            {
                throw ThrowTypeError(
                    "roundingIncrement requires rounding type to be fractionDigits", realm: Realm);
            }

            if (maximumFractionDigits != minimumFractionDigits)
            {
                throw ThrowRangeError(
                    "When roundingIncrement is set, maximumFractionDigits must equal minimumFractionDigits",
                    realm: Realm);
            }
        }

        return new DigitOptions
        {
            MinimumIntegerDigits = minimumIntegerDigits,
            MinimumFractionDigits = minimumFractionDigits,
            MaximumFractionDigits = maximumFractionDigits,
            MinimumSignificantDigits = minimumSignificantDigits,
            MaximumSignificantDigits = maximumSignificantDigits,
            RoundingIncrement = roundingIncrement,
            RoundingMode = roundingMode,
            RoundingPriority = roundingPriority,
            TrailingZeroDisplay = trailingZeroDisplay,
            RoundingType = roundingType
        };
    }

    private int GetRoundingIncrement(IJsPropertyAccessor? options)
    {
        if (options is null ||
            !options.TryGetProperty("roundingIncrement", out var value) ||
            value.IsUndefined)
        {
            return 1;
        }

        var number = JsOps.ToNumber(value);
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            throw ThrowRangeError(
                "Intl.NumberFormat roundingIncrement option must be a finite number", realm: Realm);
        }

        var intValue = (int)Math.Floor(number);
        if (!Array.Exists(ValidRoundingIncrements, v => v == intValue))
        {
            throw ThrowRangeError(
                $"Invalid roundingIncrement value '{intValue}'", realm: Realm);
        }

        return intValue;
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

        // ISO 4217 currency digit overrides
        return currency.ToUpperInvariant() switch
        {
            "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
            "CLF" or "UYW" => 4,
            "BIF" or "CLP" or "DJF" or "GNF" or "ISK" or "JPY" or "KMF" or "KRW"
                or "PYG" or "RWF" or "UGX" or "UYI" or "VND" or "VUV" or "XAF"
                or "XOF" or "XPF" => 0,
            _ => 2
        };
    }

    private sealed class DigitOptions
    {
        public int MinimumIntegerDigits { get; init; }
        public int MinimumFractionDigits { get; init; }
        public int MaximumFractionDigits { get; init; }
        public int? MinimumSignificantDigits { get; init; }
        public int? MaximumSignificantDigits { get; init; }
        public int RoundingIncrement { get; init; } = 1;
        public string RoundingMode { get; init; } = "halfExpand";
        public string RoundingPriority { get; init; } = "auto";
        public string TrailingZeroDisplay { get; init; } = "auto";
        public string RoundingType { get; init; } = "fractionDigits";
    }
}
