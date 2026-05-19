#region

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

internal static class IntlNumberFormatter
{
    private const double DecimalMaxMagnitude = 7.9228162514264338E28;

    public static IntlNumberFormatResult FormatBigInteger(BigInteger value, IntlNumberFormatInternalSlots slots)
    {
        var quantity = DecimalQuantity.FromBigInteger(value);
        var wasNegative = value.Sign < 0;
        return FormatQuantity(quantity, slots, wasNegative);
    }

    public static IntlNumberFormatResult FormatDouble(double value, IntlNumberFormatInternalSlots slots)
    {
        if (double.IsNaN(value))
        {
            var symbol = slots.Culture.NumberFormat.NaNSymbol;
            var result = IntlNumberFormatResult.FromParts(symbol,
                [new NumberFormatPart("nan", symbol)]);
            // NaN gets a "+" sign only for "always" signDisplay
            if (string.Equals(slots.SignDisplay, "always", StringComparison.Ordinal))
            {
                var plus = slots.Culture.NumberFormat.PositiveSign;
                result.Formatted = $"{plus}{result.Formatted}";
                result.Parts?.Insert(0, new NumberFormatPart("plusSign", plus));
            }

            return result;
        }

        if (double.IsInfinity(value))
        {
            var wasNeg = double.IsNegativeInfinity(value);
            var infinity = slots.Culture.NumberFormat.PositiveInfinitySymbol;
            var result = IntlNumberFormatResult.FromParts(infinity,
                [new NumberFormatPart("infinity", infinity)]);
            ApplySignDisplay(result, wasNeg, false, slots);
            return result;
        }

        var quantity = TryCreateDecimalQuantity(value) ?? DecimalQuantity.FromDouble(value);
        var wasNegative = IsNegative(value);
        return FormatQuantity(quantity, slots, wasNegative, value);
    }

    public static IntlNumberFormatResult? TryFormatDecimalString(string value, IntlNumberFormatInternalSlots slots)
    {
        if (!DecimalQuantity.TryFromString(value, out var quantity))
        {
            return null;
        }

        var numericValue = double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0d;

        return FormatQuantity(quantity, slots, quantity.IsNegative, numericValue);
    }

    private static IntlNumberFormatResult FormatQuantity(
        DecimalQuantity quantity,
        IntlNumberFormatInternalSlots slots,
        bool wasNegative,
        double numericValue = 0)
    {
        if (string.Equals(slots.Style, "percent", StringComparison.Ordinal))
        {
            MultiplyByPowerOfTen(quantity, 2);
        }

        // Note: do NOT clear IsNegative here for zero coefficients.
        // Negative zero (-0) must retain its sign per the spec.

        if (string.Equals(slots.RoundingType, "morePrecision", StringComparison.Ordinal) ||
            string.Equals(slots.RoundingType, "lessPrecision", StringComparison.Ordinal))
        {
            ApplyRoundingPriority(quantity, slots, wasNegative);
        }
        else if (slots.UseSignificantDigits)
        {
            ApplyMaximumSignificantDigits(quantity, slots.MaximumSignificantDigits!.Value,
                wasNegative, slots.RoundingMode);
            EnsureMinimumSignificantDigits(quantity, slots.MinimumSignificantDigits!.Value);
        }
        else if (slots.Notation is not "scientific" and not "engineering" and not "compact")
        {
            if (slots.RoundingIncrement > 1)
            {
                ApplyRoundingIncrement(quantity, slots.MaximumFractionDigits,
                    slots.RoundingIncrement, wasNegative, slots.RoundingMode);
            }
            else
            {
                // For scientific/engineering notation, fraction digits are applied to the mantissa
                // in FormatScientificNotation, not to the raw quantity
                // For compact notation, rounding is handled inside FormatCompactNotation
                ApplyMaximumFractionDigits(quantity, slots.MaximumFractionDigits,
                    wasNegative, slots.RoundingMode);
            }
        }

        var result = slots.Notation switch
        {
            "scientific" => FormatScientificNotation(quantity, slots, true),
            "engineering" => FormatScientificNotation(quantity, slots, false),
            "compact" => FormatCompactNotation(quantity, slots),
            _ => FormatDecimalWithParts(quantity, slots)
        };

        var isZero = quantity.Coefficient.IsZero;

        // Currency formatting handles sign+symbol together (accounting format needs this)
        if (string.Equals(slots.Style, "currency", StringComparison.Ordinal) &&
            slots.Currency is { Length: > 0 })
        {
            return FormatCurrencyComplete(result, slots, wasNegative, isZero);
        }

        ApplySignDisplay(result, wasNegative, isZero, slots);

        return slots.Style switch
        {
            "percent" => FormatPercentComplete(result, slots),
            "unit" when slots.Unit is { Length: > 0 } =>
                FormatUnitComplete(result, slots, numericValue),
            _ => result
        };
    }

    private static void ApplySignDisplay(
        IntlNumberFormatResult result,
        bool wasNegative,
        bool isZero,
        IntlNumberFormatInternalSlots slots)
    {
        var signDisplay = slots.SignDisplay;
        var nf = slots.Culture.NumberFormat;

        if (string.Equals(signDisplay, "never", StringComparison.Ordinal))
        {
            return;
        }

        if (wasNegative)
        {
            // Determine if we should show minus sign for this negative value
            var showMinus = signDisplay switch
            {
                "auto" or "always" => true, // auto/always: show minus for all negative including -0
                "exceptZero" => !isZero, // exceptZero: no sign for -0
                "negative" => !isZero, // negative: minus only for negative non-zero
                _ => true
            };

            if (showMinus)
            {
                var minus = nf.NegativeSign;
                result.Formatted = $"{minus}{result.Formatted}";
                result.Parts?.Insert(0, new NumberFormatPart("minusSign", minus));
            }

            return;
        }

        // Positive value: determine if we should show plus sign
        var showPlus = signDisplay switch
        {
            "always" => true,
            "exceptZero" => !isZero,
            _ => false // "auto", "negative"
        };

        if (showPlus)
        {
            var plus = nf.PositiveSign;
            result.Formatted = $"{plus}{result.Formatted}";
            result.Parts?.Insert(0, new NumberFormatPart("plusSign", plus));
        }
    }

    private static IntlNumberFormatResult FormatDecimalWithParts(
        DecimalQuantity quantity,
        IntlNumberFormatInternalSlots slots)
    {
        var decimal_ = FormatDecimal(quantity, slots, slots.UseGrouping, false);
        var parts = new List<NumberFormatPart>();

        if (slots.ShouldUseGrouping && decimal_.IntegerDigits.Contains(
                slots.Culture.NumberFormat.NumberGroupSeparator, StringComparison.Ordinal))
        {
            // Split grouped integer into group parts
            var groupSep = slots.Culture.NumberFormat.NumberGroupSeparator;
            var segments = decimal_.IntegerDigits.Split(groupSep);
            for (var i = 0; i < segments.Length; i++)
            {
                if (i > 0)
                {
                    parts.Add(new NumberFormatPart("group", groupSep));
                }

                parts.Add(new NumberFormatPart("integer", segments[i]));
            }
        }
        else
        {
            parts.Add(new NumberFormatPart("integer",
                decimal_.IntegerDigits.Length > 0 ? decimal_.IntegerDigits : "0"));
        }

        if (!string.IsNullOrEmpty(decimal_.FractionDigits))
        {
            parts.Add(new NumberFormatPart("decimal", decimal_.DecimalSeparator));
            parts.Add(new NumberFormatPart("fraction", decimal_.FractionDigits));
        }

        return IntlNumberFormatResult.FromParts(decimal_.Formatted, parts);
    }

    private static DecimalFormatResult FormatDecimal(
        DecimalQuantity quantity,
        IntlNumberFormatInternalSlots slots,
        string useGrouping,
        bool trimTrailingZeros,
        int? minimumIntegerOverride = null)
    {
        var digits = quantity.Coefficient.ToString(CultureInfo.InvariantCulture);
        var integerDigits = ExtractIntegerDigits(digits, quantity.Scale, out var fractionDigits);
        var minIntegerDigits = minimumIntegerOverride ?? slots.MinimumIntegerDigits;
        integerDigits = PadIntegerDigits(integerDigits, minIntegerDigits);

        if (fractionDigits.Length > 0)
        {
            if (!slots.UseSignificantDigits)
            {
                if (fractionDigits.Length < slots.MinimumFractionDigits)
                {
                    fractionDigits = fractionDigits.PadRight(slots.MinimumFractionDigits, '0');
                }
                else
                {
                    var minimumLength = trimTrailingZeros ? 0 : slots.MinimumFractionDigits;
                    if (fractionDigits.Length > minimumLength)
                    {
                        fractionDigits = TrimTrailingZeros(fractionDigits, minimumLength);
                    }
                }
            }
            else if (trimTrailingZeros)
            {
                fractionDigits = fractionDigits.TrimEnd('0');
            }
        }
        else if (slots is { UseSignificantDigits: false, MinimumFractionDigits: > 0 })
        {
            fractionDigits = new string('0', slots.MinimumFractionDigits);
        }

        var integerPortion = ShouldApplyGrouping(useGrouping, integerDigits, slots.Culture.NumberFormat)
            ? ApplyGrouping(integerDigits, slots.Culture.NumberFormat)
            : integerDigits;

        var separator = slots.Culture.NumberFormat.NumberDecimalSeparator;
        if (fractionDigits.Length == 0)
        {
            return new DecimalFormatResult
            {
                Formatted = integerPortion,
                IntegerDigits = integerPortion,
                FractionDigits = null,
                DecimalSeparator = separator
            };
        }

        return new DecimalFormatResult
        {
            Formatted = $"{integerPortion}{separator}{fractionDigits}",
            IntegerDigits = integerPortion,
            FractionDigits = fractionDigits,
            DecimalSeparator = separator
        };
    }

    private static string TrimTrailingZeros(string fractionDigits, int minimumLength)
    {
        var trimmed = fractionDigits.TrimEnd('0');
        if (trimmed.Length < minimumLength)
        {
            return fractionDigits[..minimumLength];
        }

        return trimmed;
    }

    private static IntlNumberFormatResult FormatScientificNotation(
        DecimalQuantity quantity,
        IntlNumberFormatInternalSlots slots,
        bool scientific)
    {
        if (quantity.Coefficient.IsZero)
        {
            var zeroParts = new List<NumberFormatPart>
            {
                new("integer", "0"), new("exponentSeparator", "E"), new("exponentInteger", "0")
            };
            return IntlNumberFormatResult.FromParts("0E0", zeroParts);
        }

        var digits = quantity.Coefficient.ToString(CultureInfo.InvariantCulture);
        var decimalPos = digits.Length - quantity.Scale;
        var baseExponent = decimalPos - 1;

        int exponent;
        if (scientific)
        {
            exponent = baseExponent;
        }
        else
        {
            var multiple = FloorDiv(baseExponent, 3);
            exponent = multiple * 3;
        }

        var intDigits = decimalPos - exponent;
        if (intDigits <= 0)
        {
            var adjustment = (int)Math.Ceiling(-intDigits / 3d);
            exponent -= adjustment * 3;
            intDigits += adjustment * 3;
        }

        intDigits = Math.Clamp(intDigits, 1, 3);

        var adjustedScale = quantity.Scale + exponent;
        var adjustedCoefficient = quantity.Coefficient;
        if (adjustedScale < 0)
        {
            adjustedCoefficient *= Pow10(-adjustedScale);
            adjustedScale = 0;
        }

        var mantissaQuantity = new DecimalQuantity
        {
            Coefficient = adjustedCoefficient,
            Scale = adjustedScale,
            IsNegative = false
        };

        ApplyMaximumFractionDigits(mantissaQuantity, slots.MaximumFractionDigits);

        var mantissa = FormatDecimal(mantissaQuantity, slots, "false", true,
            Math.Max(1, intDigits));

        var parts = new List<NumberFormatPart>();
        AppendMantissaParts(parts, mantissa);

        var builder = new StringBuilder();
        builder.Append(mantissa.Formatted);
        builder.Append('E');
        parts.Add(new NumberFormatPart("exponentSeparator", "E"));

        var exponentValue = exponent;
        if (exponentValue < 0)
        {
            var minus = slots.Culture.NumberFormat.NegativeSign;
            builder.Append(minus);
            parts.Add(new NumberFormatPart("exponentMinusSign", minus));
            exponentValue = -exponentValue;
        }

        var exponentDigits = exponentValue.ToString(CultureInfo.InvariantCulture);
        builder.Append(exponentDigits);
        parts.Add(new NumberFormatPart("exponentInteger", exponentDigits));

        return IntlNumberFormatResult.FromParts(builder.ToString(), parts);
    }

    private static void AppendMantissaParts(List<NumberFormatPart> parts, DecimalFormatResult mantissa)
    {
        var integer = mantissa.IntegerDigits.Length > 0 ? mantissa.IntegerDigits : "0";
        parts.Add(new NumberFormatPart("integer", integer));
        if (!string.IsNullOrEmpty(mantissa.FractionDigits))
        {
            parts.Add(new NumberFormatPart("decimal", mantissa.DecimalSeparator));
            parts.Add(new NumberFormatPart("fraction", mantissa.FractionDigits));
        }
    }

    private static IntlNumberFormatResult FormatCompactNotation(
        DecimalQuantity quantity,
        IntlNumberFormatInternalSlots slots)
    {
        if (quantity.Coefficient.IsZero)
        {
            var zeroParts = new List<NumberFormatPart> { new("integer", "0") };
            return IntlNumberFormatResult.FromParts("0", zeroParts);
        }

        // Compute the magnitude of the number
        var digits = quantity.Coefficient.ToString(CultureInfo.InvariantCulture);
        var integerDigitCount = digits.Length - quantity.Scale;

        // Find the appropriate compact pattern
        var isShort = !string.Equals(slots.CompactDisplay, "long", StringComparison.Ordinal);
        var (divisorPower, compactSuffix) = GetCompactPattern(integerDigitCount, slots.Culture, isShort);

        // Divide by the compact divisor (0 if no compact pattern applies)
        if (divisorPower > 0)
        {
            DivideByPowerOfTen(quantity, divisorPower);
        }

        // Recalculate mantissa integer digit count after division
        var mantissaDigits = quantity.Coefficient.ToString(CultureInfo.InvariantCulture);
        var mantissaIntDigits = mantissaDigits.Length - quantity.Scale;
        if (mantissaIntDigits < 0) mantissaIntDigits = 0;

        // Apply rounding based on type
        if (slots.RoundingType is "compactRounding")
        {
            // Compact rounding: use max(2, mantissaIntegerDigits) significant digits
            // This matches CLDR compact pattern behavior:
            // "000K" pattern → 3 sig digits, "00K" → 2, "0K" → 2 (min 2)
            var sigDigits = Math.Max(2, mantissaIntDigits);
            ApplyMaximumSignificantDigits(quantity, sigDigits);
            EnsureMinimumSignificantDigits(quantity, 1);
        }
        else if (slots.UseSignificantDigits)
        {
            ApplyMaximumSignificantDigits(quantity, slots.MaximumSignificantDigits!.Value);
            EnsureMinimumSignificantDigits(quantity, slots.MinimumSignificantDigits!.Value);
        }
        else
        {
            ApplyMaximumFractionDigits(quantity, slots.MaximumFractionDigits);
        }

        if (divisorPower == 0 || string.IsNullOrEmpty(compactSuffix))
        {
            // No compact suffix - format as regular decimal
            return FormatDecimalWithParts(quantity, slots);
        }

        var separator = GetCompactSeparator(slots.Culture.TwoLetterISOLanguageName, isShort);

        var decimal_ = FormatDecimal(quantity, slots, "false", true);
        var parts = new List<NumberFormatPart>();

        var integer = decimal_.IntegerDigits.Length > 0 ? decimal_.IntegerDigits : "0";
        parts.Add(new NumberFormatPart("integer", integer));
        if (!string.IsNullOrEmpty(decimal_.FractionDigits))
        {
            parts.Add(new NumberFormatPart("decimal", decimal_.DecimalSeparator));
            parts.Add(new NumberFormatPart("fraction", decimal_.FractionDigits));
        }

        if (separator.Length > 0)
        {
            parts.Add(new NumberFormatPart("literal", separator));
        }

        parts.Add(new NumberFormatPart("compact", compactSuffix));

        var formatted = $"{decimal_.Formatted}{separator}{compactSuffix}";
        return IntlNumberFormatResult.FromParts(formatted, parts);
    }

    /// <summary>
    /// Returns the separator string between a number and its compact suffix.
    /// </summary>
    private static string GetCompactSeparator(string lang, bool isShort)
    {
        // CJK: no separator for either short or long
        if (lang is "ja" or "ko" or "zh")
        {
            return string.Empty;
        }

        // German short: NBSP
        if (isShort && lang is "de")
        {
            return "\u00A0";
        }

        // Long display (non-CJK): regular space
        if (!isShort)
        {
            return " ";
        }

        // English/French/etc short: no separator
        return string.Empty;
    }

    private static (int divisorPower, string suffix) GetCompactPattern(
        int integerDigitCount, CultureInfo culture, bool isShort)
    {
        var lang = culture.TwoLetterISOLanguageName;

        // For Indian locale (en-IN), use lakh/crore system
        if (string.Equals(culture.Name, "en-IN", StringComparison.OrdinalIgnoreCase))
        {
            return integerDigitCount switch
            {
                >= 8 => (7, isShort ? "Cr" : "crore"),
                >= 6 => (5, isShort ? "L" : "lakh"),
                >= 4 => (3, isShort ? "K" : "thousand"),
                _ => (0, string.Empty)
            };
        }

        // Standard patterns (en-US, most locales)
        if (lang is "en" or "fr" or "es" or "pt" or "it")
        {
            return integerDigitCount switch
            {
                >= 13 => (12, isShort ? "T" : "trillion"),
                >= 10 => (9, isShort ? "B" : "billion"),
                >= 7 => (6, isShort ? "M" : "million"),
                >= 4 => (3, isShort ? "K" : "thousand"),
                _ => (0, string.Empty)
            };
        }

        // German: short uses Mio./Mrd./Bio. only at >= 7 digits
        // Long uses Tausend/Millionen/Milliarden/Billionen at >= 4 digits
        if (lang is "de")
        {
            if (isShort)
            {
                return integerDigitCount switch
                {
                    >= 13 => (12, "Bio."),
                    >= 10 => (9, "Mrd."),
                    >= 7 => (6, "Mio."),
                    _ => (0, string.Empty)
                };
            }

            return integerDigitCount switch
            {
                >= 13 => (12, "Billionen"),
                >= 10 => (9, "Milliarden"),
                >= 7 => (6, "Millionen"),
                >= 4 => (3, "Tausend"),
                _ => (0, string.Empty)
            };
        }

        // Japanese: 万 (10K), 億 (100M), 兆 (1T) — same for short and long
        if (lang is "ja")
        {
            return integerDigitCount switch
            {
                >= 13 => (12, "兆"),
                >= 9 => (8, "億"),
                >= 5 => (4, "万"),
                _ => (0, string.Empty)
            };
        }

        // Korean: 천 (1K), 만 (10K), 억 (100M), 조 (1T) — same for short and long
        if (lang is "ko")
        {
            return integerDigitCount switch
            {
                >= 13 => (12, "조"),
                >= 9 => (8, "억"),
                >= 5 => (4, "만"),
                >= 4 => (3, "천"),
                _ => (0, string.Empty)
            };
        }

        // Chinese: 萬 (10K), 億 (100M), 兆 (1T) — same for short and long
        if (lang is "zh")
        {
            return integerDigitCount switch
            {
                >= 13 => (12, "兆"),
                >= 9 => (8, "億"),
                >= 5 => (4, "萬"),
                _ => (0, string.Empty)
            };
        }

        // Default: use English-style K/M/B/T
        return integerDigitCount switch
        {
            >= 13 => (12, isShort ? "T" : "trillion"),
            >= 10 => (9, isShort ? "B" : "billion"),
            >= 7 => (6, isShort ? "M" : "million"),
            >= 4 => (3, isShort ? "K" : "thousand"),
            _ => (0, string.Empty)
        };
    }

    private static void DivideByPowerOfTen(DecimalQuantity quantity, int power)
    {
        if (power <= 0 || quantity.Coefficient.IsZero)
        {
            return;
        }

        quantity.Scale += power;
    }

    private static IntlNumberFormatResult FormatUnitComplete(
        IntlNumberFormatResult numberResult,
        IntlNumberFormatInternalSlots slots,
        double numericValue)
    {
        var lang = slots.Culture.TwoLetterISOLanguageName;
        var display = slots.UnitDisplay;
        var unit = slots.Unit!;
        var number = numberResult.Formatted;
        var numberParts = numberResult.Parts ?? [new NumberFormatPart("integer", number)];

        // For long compound units with CJK prefix-number-suffix patterns
        var perIndex = unit.IndexOf("-per-", StringComparison.Ordinal);
        if (perIndex >= 0 && string.Equals(display, "long", StringComparison.Ordinal))
        {
            var numerator = unit[..perIndex];
            var denominator = unit[(perIndex + 5)..];
            var result = FormatLongCompoundUnitParts(number, numberParts, numerator, denominator, lang);
            if (result != null)
            {
                return result;
            }
        }

        var unitName = GetUnitDisplayName(unit, display, lang, numericValue);
        var separator = GetUnitSeparator(lang, display);

        // Symbol-like units (%, °, °C, °F) never use a space
        if (unitName is "%" or "°" or "°C" or "°F")
        {
            separator = string.Empty;
        }

        var parts = new List<NumberFormatPart>(numberParts);
        if (separator.Length > 0)
        {
            parts.Add(new NumberFormatPart("literal", separator));
        }

        parts.Add(new NumberFormatPart("unit", unitName));
        return IntlNumberFormatResult.FromParts($"{number}{separator}{unitName}", parts);
    }

    /// <summary>
    /// Format long compound units for CJK locales that use prefix-number-suffix patterns.
    /// Returns null for locales that use the standard "{number} {unit per unit}" pattern.
    /// </summary>
    private static IntlNumberFormatResult? FormatLongCompoundUnitParts(
        string number, List<NumberFormatPart> numberParts,
        string numerator, string denominator, string lang)
    {
        var numName = GetLongUnitName(numerator, lang);

        // Japanese: "時速 {number} キロメートル"
        if (lang is "ja" && denominator is "hour")
        {
            var parts = new List<NumberFormatPart>();
            parts.Add(new NumberFormatPart("unit", "時速"));
            parts.Add(new NumberFormatPart("literal", " "));
            parts.AddRange(numberParts);
            parts.Add(new NumberFormatPart("literal", " "));
            parts.Add(new NumberFormatPart("unit", numName));
            return IntlNumberFormatResult.FromParts($"時速 {number} {numName}", parts);
        }

        // Korean: "시속 {number}킬로미터" (no space before unit name)
        if (lang is "ko" && denominator is "hour")
        {
            var parts = new List<NumberFormatPart>();
            parts.Add(new NumberFormatPart("unit", "시속"));
            parts.Add(new NumberFormatPart("literal", " "));
            parts.AddRange(numberParts);
            parts.Add(new NumberFormatPart("unit", numName));
            return IntlNumberFormatResult.FromParts($"시속 {number}{numName}", parts);
        }

        // Chinese: "每小時 {number} 公里"
        if (lang is "zh" && denominator is "hour")
        {
            var denName = GetLongUnitName(denominator, lang);
            var parts = new List<NumberFormatPart>();
            parts.Add(new NumberFormatPart("unit", $"每{denName}"));
            parts.Add(new NumberFormatPart("literal", " "));
            parts.AddRange(numberParts);
            parts.Add(new NumberFormatPart("literal", " "));
            parts.Add(new NumberFormatPart("unit", numName));
            return IntlNumberFormatResult.FromParts($"每{denName} {number} {numName}", parts);
        }

        return null;
    }

    private static string GetUnitSeparator(string lang, string display)
    {
        // German: always use space, even for narrow
        if (lang is "de")
        {
            return " ";
        }

        // Korean narrow/short: no space
        if (lang is "ko" && display is not "long")
        {
            return string.Empty;
        }

        // Narrow: no space (except German handled above)
        if (string.Equals(display, "narrow", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // Short and long: space
        return " ";
    }

    private static string GetUnitDisplayName(string unit, string display, string lang, double numericValue = 1)
    {
        // Handle compound units (e.g. "kilometer-per-hour")
        var perIndex = unit.IndexOf("-per-", StringComparison.Ordinal);
        if (perIndex >= 0)
        {
            var numerator = unit[..perIndex];
            var denominator = unit[(perIndex + 5)..];
            var numName = GetSimpleUnitName(numerator, display, lang, numericValue);
            var denName = GetSimpleUnitName(denominator, display, lang, 1);

            if (string.Equals(display, "long", StringComparison.Ordinal))
            {
                return lang switch
                {
                    "en" => $"{GetLongUnitName(numerator, lang, numericValue)} per {GetLongUnitName(denominator, lang, 1)}",
                    "de" => $"{GetLongUnitName(numerator, lang, numericValue)} pro {GetLongUnitName(denominator, lang, 1)}",
                    _ => $"{numName}/{denName}"
                };
            }

            return $"{numName}/{denName}";
        }

        if (string.Equals(display, "long", StringComparison.Ordinal))
        {
            return GetLongUnitName(unit, lang, numericValue);
        }

        return GetSimpleUnitName(unit, display, lang, numericValue);
    }

    private static string GetSimpleUnitName(string unit, string display, string lang, double numericValue = 2)
    {
        if (string.Equals(display, "long", StringComparison.Ordinal))
        {
            return GetLongUnitName(unit, lang, numericValue);
        }

        // Chinese short/narrow uses localized characters
        if (lang is "zh")
        {
            return GetChineseShortUnitName(unit);
        }

        // Short and narrow use abbreviations
        // Most abbreviations are invariant; some units need plural forms in "en"
        var isPlural = lang is "en" && !IsSingular(numericValue);
        return unit switch
        {
            "kilometer" => "km",
            "meter" => "m",
            "centimeter" => "cm",
            "millimeter" => "mm",
            "mile" => "mi",
            "foot" => "ft",
            "inch" => "in",
            "yard" => "yd",
            "hour" => "h",
            "minute" => "min",
            "second" => "s",
            "millisecond" => "ms",
            "microsecond" => "μs",
            "nanosecond" => "ns",
            "kilogram" => "kg",
            "gram" => "g",
            "pound" => "lb",
            "ounce" => "oz",
            "liter" => lang is "en" ? "L" : "l",
            "milliliter" => lang is "en" ? "mL" : "ml",
            "gallon" => "gal",
            "celsius" => "°C",
            "fahrenheit" => "°F",
            "percent" => "%",
            "degree" => "°",
            "acre" => "ac",
            "hectare" => "ha",
            "byte" => lang is "en" ? (isPlural ? "bytes" : "byte") : "B",
            "kilobyte" => "kB",
            "megabyte" => "MB",
            "gigabyte" => "GB",
            // Units that need plural forms in short display (CLDR en)
            "day" => isPlural ? "days" : "day",
            "week" => isPlural ? "wks" : "wk",
            "month" => isPlural ? "mos" : "mo",
            "year" => isPlural ? "yrs" : "yr",
            _ => unit
        };
    }

    /// <summary>
    /// English CLDR "one" plural category: absolute integer value is exactly 1
    /// with no visible fraction digits.
    /// </summary>
    private static bool IsSingular(double value)
    {
        return Math.Abs(value) == 1 && value == Math.Truncate(value);
    }

    private static string GetChineseShortUnitName(string unit)
    {
        return unit switch
        {
            "kilometer" => "公里",
            "meter" => "公尺",
            "centimeter" => "公分",
            "millimeter" => "公釐",
            "hour" => "小時",
            "minute" => "分鐘",
            "second" => "秒",
            "kilogram" => "公斤",
            "gram" => "克",
            "liter" => "公升",
            "milliliter" => "毫升",
            "celsius" => "°C",
            "fahrenheit" => "°F",
            "percent" => "%",
            "degree" => "°",
            _ => unit
        };
    }

    private static string GetLongUnitName(string unit, string lang, double numericValue = 2)
    {
        if (lang is "en")
        {
            var isPlural = !IsSingular(numericValue);
            return unit switch
            {
                "kilometer" => isPlural ? "kilometers" : "kilometer",
                "meter" => isPlural ? "meters" : "meter",
                "centimeter" => isPlural ? "centimeters" : "centimeter",
                "millimeter" => isPlural ? "millimeters" : "millimeter",
                "mile" => isPlural ? "miles" : "mile",
                "foot" => isPlural ? "feet" : "foot",
                "inch" => isPlural ? "inches" : "inch",
                "yard" => isPlural ? "yards" : "yard",
                "hour" => isPlural ? "hours" : "hour",
                "minute" => isPlural ? "minutes" : "minute",
                "second" => isPlural ? "seconds" : "second",
                "millisecond" => isPlural ? "milliseconds" : "millisecond",
                "microsecond" => isPlural ? "microseconds" : "microsecond",
                "nanosecond" => isPlural ? "nanoseconds" : "nanosecond",
                "day" => isPlural ? "days" : "day",
                "week" => isPlural ? "weeks" : "week",
                "month" => isPlural ? "months" : "month",
                "year" => isPlural ? "years" : "year",
                "kilogram" => isPlural ? "kilograms" : "kilogram",
                "gram" => isPlural ? "grams" : "gram",
                "pound" => isPlural ? "pounds" : "pound",
                "ounce" => isPlural ? "ounces" : "ounce",
                "liter" => isPlural ? "liters" : "liter",
                "milliliter" => isPlural ? "milliliters" : "milliliter",
                "gallon" => isPlural ? "gallons" : "gallon",
                "celsius" => isPlural ? "degrees Celsius" : "degree Celsius",
                "fahrenheit" => isPlural ? "degrees Fahrenheit" : "degree Fahrenheit",
                "percent" => "percent",
                "degree" => isPlural ? "degrees" : "degree",
                _ => unit
            };
        }

        if (lang is "de")
        {
            return unit switch
            {
                "kilometer" => "Kilometer",
                "meter" => "Meter",
                "hour" => "Stunde",
                "kilogram" => "Kilogramm",
                "liter" => "Liter",
                "celsius" => "Grad Celsius",
                _ => unit
            };
        }

        if (lang is "ja")
        {
            return unit switch
            {
                "kilometer" => "キロメートル",
                "hour" => "時間",
                "kilogram" => "キログラム",
                _ => unit
            };
        }

        if (lang is "ko")
        {
            return unit switch
            {
                "kilometer" => "킬로미터",
                "hour" => "시간",
                "kilogram" => "킬로그램",
                _ => unit
            };
        }

        if (lang is "zh")
        {
            return unit switch
            {
                "kilometer" => "公里",
                "hour" => "小時",
                "kilogram" => "公斤",
                _ => unit
            };
        }

        // Default: English-style
        return unit;
    }

    private static DecimalQuantity? TryCreateDecimalQuantity(double value)
    {
        if (value == 0d)
        {
            return DecimalQuantity.FromDecimal(0m);
        }

        if (value is <= DecimalMaxMagnitude and >= -DecimalMaxMagnitude)
        {
            try
            {
                var decimalValue = (decimal)value;
                if (decimalValue != 0m)
                {
                    return DecimalQuantity.FromDecimal(decimalValue);
                }
            }
            catch (OverflowException)
            {
                // fall back to double handling
            }
        }

        return null;
    }

    private static int FloorDiv(int dividend, int divisor)
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }

        var quotient = dividend / divisor;
        var remainder = dividend % divisor;
        if (remainder != 0 && (remainder < 0) ^ (divisor < 0))
        {
            quotient--;
        }

        return quotient;
    }

    private static void ApplyRoundingPriority(
        DecimalQuantity quantity,
        IntlNumberFormatInternalSlots slots,
        bool wasNegative)
    {
        // Apply both significant digits and fraction digits rounding independently
        var sdQuantity = new DecimalQuantity
        {
            Coefficient = quantity.Coefficient,
            Scale = quantity.Scale,
            IsNegative = quantity.IsNegative
        };
        ApplyMaximumSignificantDigits(sdQuantity, slots.MaximumSignificantDigits!.Value,
            wasNegative, slots.RoundingMode);
        EnsureMinimumSignificantDigits(sdQuantity, slots.MinimumSignificantDigits!.Value);

        var fdQuantity = new DecimalQuantity
        {
            Coefficient = quantity.Coefficient,
            Scale = quantity.Scale,
            IsNegative = quantity.IsNegative
        };
        ApplyMaximumFractionDigits(fdQuantity, slots.MaximumFractionDigits,
            wasNegative, slots.RoundingMode);

        // Compare using rounding magnitudes from the MAXIMUM constraints.
        // SD magnitude = floor(log10(|x|)) - maxSig + 1
        // FD magnitude = -maxFrac
        // morePrecision: pick the result with smaller magnitude (more precise)
        // lessPrecision: pick the result with larger magnitude (less precise)
        var maxSig = slots.MaximumSignificantDigits!.Value;
        var maxFrac = slots.MaximumFractionDigits;
        var fdMagnitude = -maxFrac;

        int sdMagnitude;
        if (quantity.Coefficient.IsZero)
        {
            sdMagnitude = 1 - maxSig;
        }
        else
        {
            var digits = quantity.Coefficient.ToString(CultureInfo.InvariantCulture);
            var exponent = digits.Length - quantity.Scale - 1; // floor(log10(|x|))
            sdMagnitude = exponent - maxSig + 1;
        }

        bool useSD;
        if (string.Equals(slots.RoundingType, "morePrecision", StringComparison.Ordinal))
        {
            useSD = sdMagnitude < fdMagnitude; // SD is more precise
        }
        else
        {
            useSD = sdMagnitude > fdMagnitude; // SD is less precise
        }

        var chosen = useSD ? sdQuantity : fdQuantity;
        quantity.Coefficient = chosen.Coefficient;
        quantity.Scale = chosen.Scale;

        // Ensure the minimum constraint of the chosen path is satisfied.
        // For FD: ensure MinimumFractionDigits; for SD: EnsureMinimumSignificantDigits already applied.
        if (!useSD && quantity.Scale < slots.MinimumFractionDigits)
        {
            var diff = slots.MinimumFractionDigits - quantity.Scale;
            quantity.Coefficient *= Pow10(diff);
            quantity.Scale = slots.MinimumFractionDigits;
        }
    }

    private static void ApplyRoundingIncrement(
        DecimalQuantity quantity,
        int maxFractionDigits,
        int roundingIncrement,
        bool isNegative,
        string roundingMode)
    {
        // Scale the coefficient to maxFractionDigits precision without rounding
        if (quantity.Scale < maxFractionDigits)
        {
            var diff = maxFractionDigits - quantity.Scale;
            quantity.Coefficient *= Pow10(diff);
            quantity.Scale = maxFractionDigits;
        }

        // For scales > maxFractionDigits, we need to combine the extra digits with
        // the increment rounding. Scale to maxFractionDigits + extra precision for accurate rounding.
        BigInteger scaledCoefficient;
        int extraScale;
        if (quantity.Scale > maxFractionDigits)
        {
            extraScale = quantity.Scale - maxFractionDigits;
            scaledCoefficient = quantity.Coefficient;
        }
        else
        {
            extraScale = 0;
            scaledCoefficient = quantity.Coefficient;
        }

        // The rounding increment applies at the maxFractionDigits level.
        // We need to round scaledCoefficient / 10^extraScale to nearest multiple of roundingIncrement.
        var increment = new BigInteger(roundingIncrement);
        var scaleFactor = Pow10(extraScale);
        var atTargetScale = BigInteger.DivRem(scaledCoefficient, scaleFactor, out var subRemainder);
        var remainder = atTargetScale % increment;

        // Combine the sub-scale remainder with the increment remainder for accurate rounding
        var totalRemainder = remainder * scaleFactor + subRemainder;
        var totalDivisor = increment * scaleFactor;

        var truncated = atTargetScale - remainder;
        if (ShouldRoundUp(totalRemainder, totalDivisor, truncated / increment, isNegative, roundingMode))
        {
            quantity.Coefficient = truncated + increment;
        }
        else
        {
            quantity.Coefficient = truncated;
        }

        quantity.Scale = maxFractionDigits;
    }

    private static void ApplyMaximumFractionDigits(
        DecimalQuantity quantity,
        int maxFractionDigits,
        bool isNegative = false,
        string roundingMode = "halfExpand")
    {
        if (quantity.Scale <= maxFractionDigits)
        {
            return;
        }

        var digitsToTrim = quantity.Scale - maxFractionDigits;
        TrimCoefficient(quantity, digitsToTrim, isNegative, roundingMode);
        quantity.Scale = maxFractionDigits;
    }

    private static void ApplyMaximumSignificantDigits(
        DecimalQuantity quantity,
        int maxDigits,
        bool isNegative = false,
        string roundingMode = "halfExpand")
    {
        if (quantity.Coefficient.IsZero)
        {
            return;
        }

        var digits = quantity.Coefficient.ToString(CultureInfo.InvariantCulture);
        var totalDigits = digits.Length;
        if (totalDigits <= maxDigits)
        {
            return;
        }

        var diff = totalDigits - maxDigits;
        var divisor = Pow10(diff);
        var rounded = BigInteger.DivRem(quantity.Coefficient, divisor, out var remainder);
        if (ShouldRoundUp(remainder, divisor, rounded, isNegative, roundingMode))
        {
            rounded += BigInteger.One;
        }

        if (quantity.Scale >= diff)
        {
            // All trimmed digits are in the fractional part - reduce scale
            quantity.Coefficient = rounded;
            quantity.Scale -= diff;
        }
        else
        {
            // Some trimmed digits are in the integer part - keep trailing zeros
            var integerZeros = diff - quantity.Scale;
            quantity.Coefficient = rounded * Pow10(integerZeros);
            quantity.Scale = 0;
        }

        // Normalize: remove trailing zeros from fractional part of coefficient.
        // These can be artifacts of floating-point imprecision (e.g. 1.23e-30 → G17 → "12300...").
        // EnsureMinimumSignificantDigits will add back any needed for minimum SD.
        NormalizeTrailingZeros(quantity);
    }

    private static void NormalizeTrailingZeros(DecimalQuantity quantity)
    {
        while (quantity.Scale > 0 && !quantity.Coefficient.IsZero && quantity.Coefficient % 10 == 0)
        {
            quantity.Coefficient /= 10;
            quantity.Scale--;
        }
    }

    private static void EnsureMinimumSignificantDigits(DecimalQuantity quantity, int minDigits)
    {
        if (quantity.Coefficient.IsZero)
        {
            if (minDigits > 1)
            {
                quantity.Scale = minDigits - 1;
            }

            return;
        }

        var digits = quantity.Coefficient.ToString(CultureInfo.InvariantCulture);
        if (digits.Length >= minDigits)
        {
            return;
        }

        var diff = minDigits - digits.Length;
        quantity.Coefficient *= Pow10(diff);
        quantity.Scale += diff;
    }

    private static void MultiplyByPowerOfTen(DecimalQuantity quantity, int exponent)
    {
        if (exponent == 0 || quantity.Coefficient.IsZero)
        {
            return;
        }

        if (quantity.Scale >= exponent)
        {
            quantity.Scale -= exponent;
            return;
        }

        var diff = exponent - quantity.Scale;
        quantity.Coefficient *= Pow10(diff);
        quantity.Scale = 0;
    }

    private static void TrimCoefficient(
        DecimalQuantity quantity,
        int digitsToTrim,
        bool isNegative = false,
        string roundingMode = "halfExpand")
    {
        if (digitsToTrim <= 0 || quantity.Coefficient.IsZero)
        {
            return;
        }

        var divisor = Pow10(digitsToTrim);
        var truncated = BigInteger.DivRem(quantity.Coefficient, divisor, out var remainder);
        if (ShouldRoundUp(remainder, divisor, truncated, isNegative, roundingMode))
        {
            truncated += BigInteger.One;
        }

        quantity.Coefficient = truncated;
    }

    private static string ExtractIntegerDigits(string digits, int scale, out string fractionDigits)
    {
        if (scale == 0)
        {
            fractionDigits = string.Empty;
            return digits;
        }

        if (digits.Length > scale)
        {
            var split = digits.Length - scale;
            fractionDigits = digits[split..];
            return digits[..split];
        }

        fractionDigits = new string('0', scale - digits.Length) + digits;
        return "0";
    }

    private static string PadIntegerDigits(string digits, int minimum)
    {
        if (digits.Length >= minimum)
        {
            return digits;
        }

        return new string('0', minimum - digits.Length) + digits;
    }

    private static bool ShouldApplyGrouping(string useGrouping, string integerDigits, NumberFormatInfo format)
    {
        if (string.Equals(useGrouping, "false", StringComparison.Ordinal) ||
            string.Equals(useGrouping, "never", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(useGrouping, "min2", StringComparison.Ordinal))
        {
            // "min2" means minimum 2 digits in the most significant group
            // For groupSize=3: 1,000 (1 digit before separator → no grouping)
            //                  10,000 (2 digits before separator → apply grouping)
            var groupSize = format.NumberGroupSizes.Length > 0 ? format.NumberGroupSizes[0] : 3;
            return groupSize > 0 && integerDigits.Length >= groupSize + 2;
        }

        // "auto", "always", or "true" - all apply grouping
        return true;
    }

    private static string ApplyGrouping(string digits, NumberFormatInfo format)
    {
        var groupSizes = format.NumberGroupSizes;
        if (groupSizes.Length == 0 || groupSizes[0] == 0 || digits.Length <= groupSizes[0])
        {
            return digits;
        }

        var separator = format.NumberGroupSeparator;
        var builder = new StringBuilder();
        var digitIndex = digits.Length - 1;
        var groupIndex = 0;
        var groupSize = groupSizes[groupIndex];
        var lastGroupSize = groupSizes[^1] == 0 && groupSizes.Length > 1
            ? groupSizes[groupSizes.Length - 2]
            : groupSizes[^1];
        var count = 0;

        while (digitIndex >= 0)
        {
            builder.Insert(0, digits[digitIndex]);
            digitIndex--;
            count++;

            if (digitIndex < 0)
            {
                break;
            }

            if (groupSize == 0)
            {
                continue;
            }

            if (count == groupSize)
            {
                builder.Insert(0, separator);
                count = 0;
                if (groupIndex < groupSizes.Length - 1)
                {
                    groupIndex++;
                    groupSize = groupSizes[groupIndex];
                }
                else
                {
                    groupSize = lastGroupSize;
                }
            }
        }

        var result = builder.ToString();
        if (result.StartsWith(separator, StringComparison.Ordinal))
        {
            result = result[separator.Length..];
        }

        return result;
    }

    private static IntlNumberFormatResult FormatPercentComplete(
        IntlNumberFormatResult numberResult, IntlNumberFormatInternalSlots slots)
    {
        var nfi = slots.Culture.NumberFormat;
        var symbol = nfi.PercentSymbol;
        var pattern = nfi.PercentPositivePattern;
        var numberParts = numberResult.Parts ?? [new NumberFormatPart("integer", numberResult.Formatted)];

        var parts = new List<NumberFormatPart>();
        string formatted;

        // Patterns: 0 = "n %" (space+after), 1 = "n%" (after), 2 = "%n" (before), 3 = "% n" (before+space)
        if (pattern is 2 or 3)
        {
            parts.Add(new NumberFormatPart("percentSign", symbol));
            if (pattern is 3)
            {
                parts.Add(new NumberFormatPart("literal", "\u00A0"));
            }

            parts.AddRange(numberParts);
            formatted = pattern is 3
                ? $"{symbol}\u00A0{numberResult.Formatted}"
                : $"{symbol}{numberResult.Formatted}";
        }
        else
        {
            parts.AddRange(numberParts);
            if (pattern is 0)
            {
                parts.Add(new NumberFormatPart("literal", "\u00A0"));
            }

            parts.Add(new NumberFormatPart("percentSign", symbol));
            formatted = pattern is 0
                ? $"{numberResult.Formatted}\u00A0{symbol}"
                : $"{numberResult.Formatted}{symbol}";
        }

        return IntlNumberFormatResult.FromParts(formatted, parts);
    }

    private static IntlNumberFormatResult FormatCurrencyComplete(
        IntlNumberFormatResult numberResult,
        IntlNumberFormatInternalSlots slots,
        bool wasNegative,
        bool isZero)
    {
        var symbol = GetCurrencySymbol(slots.Currency!, slots.CurrencyDisplay, slots.Culture);
        var nfi = slots.Culture.NumberFormat;
        var bare = numberResult.Formatted; // number without sign

        var signDisplay = slots.SignDisplay;
        var isAccounting = string.Equals(slots.CurrencySign, "accounting", StringComparison.Ordinal);

        // Determine sign behavior
        var showAccountingNegative = false;
        var showMinus = false;
        var showPlus = false;

        if (!string.Equals(signDisplay, "never", StringComparison.Ordinal))
        {
            if (wasNegative)
            {
                var showSign = signDisplay switch
                {
                    "auto" or "always" => true,
                    "exceptZero" or "negative" => !isZero,
                    _ => true
                };

                if (showSign)
                {
                    if (isAccounting && UseParenthesesForAccounting(slots.Culture))
                    {
                        showAccountingNegative = true;
                    }
                    else
                    {
                        showMinus = true;
                    }
                }
            }
            else
            {
                showPlus = signDisplay switch
                {
                    "always" => true,
                    "exceptZero" => !isZero,
                    _ => false
                };
            }
        }

        // Build parts list for the currency formatted result
        // CLDR uses NBSP (U+00A0) for the space between number and symbol in patterns 2,3
        const string nbsp = "\u00A0";
        var positivePattern = nfi.CurrencyPositivePattern;
        var numberParts = numberResult.Parts ?? [new NumberFormatPart("integer", bare)];

        var parts = new List<NumberFormatPart>();
        var sb = new StringBuilder();

        if (showAccountingNegative)
        {
            // Accounting negative: ( symbol number )
            parts.Add(new NumberFormatPart("literal", "("));
            sb.Append('(');

            if (positivePattern is 0 or 2)
            {
                // Symbol before number
                parts.Add(new NumberFormatPart("currency", symbol));
                sb.Append(symbol);
                if (positivePattern is 2)
                {
                    parts.Add(new NumberFormatPart("literal", nbsp));
                    sb.Append(nbsp);
                }

                parts.AddRange(numberParts);
                sb.Append(bare);
            }
            else
            {
                // Number before symbol (patterns 1, 3)
                parts.AddRange(numberParts);
                sb.Append(bare);
                if (positivePattern is 3)
                {
                    parts.Add(new NumberFormatPart("literal", nbsp));
                    sb.Append(nbsp);
                }

                parts.Add(new NumberFormatPart("currency", symbol));
                sb.Append(symbol);
            }

            parts.Add(new NumberFormatPart("literal", ")"));
            sb.Append(')');
        }
        else
        {
            // Non-accounting: optional sign + symbol + number
            if (showMinus)
            {
                parts.Add(new NumberFormatPart("minusSign", nfi.NegativeSign));
                sb.Append(nfi.NegativeSign);
            }
            else if (showPlus)
            {
                parts.Add(new NumberFormatPart("plusSign", nfi.PositiveSign));
                sb.Append(nfi.PositiveSign);
            }

            if (positivePattern is 0 or 2)
            {
                // Symbol before number
                parts.Add(new NumberFormatPart("currency", symbol));
                sb.Append(symbol);
                if (positivePattern is 2)
                {
                    parts.Add(new NumberFormatPart("literal", nbsp));
                    sb.Append(nbsp);
                }

                parts.AddRange(numberParts);
                sb.Append(bare);
            }
            else
            {
                // Number before symbol (patterns 1, 3)
                parts.AddRange(numberParts);
                sb.Append(bare);
                if (positivePattern is 3)
                {
                    parts.Add(new NumberFormatPart("literal", nbsp));
                    sb.Append(nbsp);
                }

                parts.Add(new NumberFormatPart("currency", symbol));
                sb.Append(symbol);
            }
        }

        return IntlNumberFormatResult.FromParts(sb.ToString(), parts);
    }

    private static string GetCurrencySymbol(string currencyCode, string display, CultureInfo culture)
    {
        if (string.Equals(display, "code", StringComparison.Ordinal))
        {
            return currencyCode;
        }

        if (string.Equals(display, "name", StringComparison.Ordinal))
        {
            // Simplified: return lowercase currency code as name placeholder
            return currencyCode.ToLowerInvariant();
        }

        // "symbol" or "narrowSymbol" - resolve the appropriate symbol
        // Check if this locale's region uses this currency natively
        try
        {
            var region = new RegionInfo(culture.Name);
            if (string.Equals(region.ISOCurrencySymbol, currencyCode, StringComparison.OrdinalIgnoreCase))
            {
                return region.CurrencySymbol;
            }
        }
        catch
        {
            // Region not available, fall through to lookup
        }

        // Foreign currency: use disambiguated symbol per CLDR conventions
        var lang = culture.TwoLetterISOLanguageName;

        return currencyCode switch
        {
            "USD" => UsesLongDollarSymbol(lang) ? "US$" : "$",
            "EUR" => "€",
            "GBP" => "£",
            "JPY" or "CNY" => "¥",
            "KRW" => "₩",
            "TWD" => UsesLongDollarSymbol(lang) ? "NT$" : "$",
            "CAD" => UsesLongDollarSymbol(lang) ? "CA$" : "$",
            "AUD" => UsesLongDollarSymbol(lang) ? "A$" : "$",
            _ => currencyCode
        };
    }

    private static bool UsesLongDollarSymbol(string lang)
    {
        // CLDR convention: ko and zh locales use "US$" for USD (and similar for other dollars)
        return lang is "ko" or "zh";
    }

    private static bool UseParenthesesForAccounting(CultureInfo culture)
    {
        var lang = culture.TwoLetterISOLanguageName;
        // Germanic languages (except English) typically don't use parentheses for accounting
        // This matches CLDR accounting currency patterns
        return lang is not ("de" or "nl" or "da" or "sv" or "nb" or "nn" or "fi" or "is");
    }

    private static bool ShouldRoundUp(
        BigInteger remainder,
        BigInteger divisor,
        BigInteger truncated,
        bool isNegative,
        string roundingMode)
    {
        if (remainder.IsZero)
        {
            return false;
        }

        return roundingMode switch
        {
            "ceil" => !isNegative,
            "floor" => isNegative,
            "expand" => true, // always away from zero
            "trunc" => false, // always toward zero
            "halfCeil" => HalfRound(remainder, divisor, !isNegative),
            "halfFloor" => HalfRound(remainder, divisor, isNegative),
            "halfExpand" => remainder * 2 >= divisor, // away from zero on tie
            "halfTrunc" => remainder * 2 > divisor, // toward zero on tie
            "halfEven" => HalfEven(remainder, divisor, truncated),
            _ => remainder * 2 >= divisor // default: halfExpand
        };
    }

    private static bool HalfRound(BigInteger remainder, BigInteger divisor, bool roundUpOnTie)
    {
        var doubled = remainder * 2;
        if (doubled > divisor)
        {
            return true;
        }

        if (doubled < divisor)
        {
            return false;
        }

        return roundUpOnTie;
    }

    private static bool HalfEven(BigInteger remainder, BigInteger divisor, BigInteger truncated)
    {
        var doubled = remainder * 2;
        if (doubled > divisor)
        {
            return true;
        }

        if (doubled < divisor)
        {
            return false;
        }

        // On tie, round to even
        return !truncated.IsEven;
    }

    private static BigInteger Pow10(int exponent)
    {
        if (exponent <= 0)
        {
            return BigInteger.One;
        }

        return BigInteger.Pow(10, exponent);
    }

    private static bool IsNegative(double value)
    {
        return BitConverter.DoubleToInt64Bits(value) < 0;
    }

    private sealed class DecimalQuantity
    {
        public required BigInteger Coefficient { get; set; }
        public required int Scale { get; set; }
        public required bool IsNegative { get; set; }

        public static DecimalQuantity FromBigInteger(BigInteger value)
        {
            return new DecimalQuantity { Coefficient = BigInteger.Abs(value), Scale = 0, IsNegative = value.Sign < 0 };
        }

        public static DecimalQuantity FromDouble(double value)
        {
            if (value == 0d)
            {
                return new DecimalQuantity { Coefficient = BigInteger.Zero, Scale = 0, IsNegative = IsNegative(value) };
            }

            var isNegative = IsNegative(value);
            var abs = Math.Abs(value);
            var raw = abs.ToString("G17", CultureInfo.InvariantCulture);
            var exponent = 0;
            var expIndex = raw.IndexOfAny(['e', 'E']);
            if (expIndex >= 0)
            {
                exponent = int.Parse(raw[(expIndex + 1)..], CultureInfo.InvariantCulture);
                raw = raw[..expIndex];
            }

            var decimalIndex = raw.IndexOf('.', StringComparison.Ordinal);
            var fractionLength = 0;
            if (decimalIndex >= 0)
            {
                fractionLength = raw.Length - decimalIndex - 1;
                raw = raw.Remove(decimalIndex, 1);
            }

            if (raw.Length == 0)
            {
                raw = "0";
            }

            var coefficient = BigInteger.Parse(raw, CultureInfo.InvariantCulture);
            if (coefficient.IsZero)
            {
                return new DecimalQuantity { Coefficient = BigInteger.Zero, Scale = 0, IsNegative = false };
            }

            var exponentAdjustment = exponent - fractionLength;
            if (exponentAdjustment >= 0)
            {
                coefficient *= Pow10(exponentAdjustment);
                return new DecimalQuantity { Coefficient = coefficient, Scale = 0, IsNegative = isNegative };
            }

            return new DecimalQuantity
            {
                Coefficient = coefficient,
                Scale = -exponentAdjustment,
                IsNegative = isNegative
            };
        }

        public static DecimalQuantity FromDecimal(decimal value)
        {
            if (value == 0m)
            {
                return new DecimalQuantity { Coefficient = BigInteger.Zero, Scale = 0, IsNegative = false };
            }

            var bits = decimal.GetBits(decimal.Abs(value));
            var scale = (bits[3] >> 16) & 0x7F;
            var isNegative = value < 0m;
            var high = (uint)bits[2];
            var mid = (uint)bits[1];
            var low = (uint)bits[0];
            var coefficient = ((BigInteger)high << 64) | ((BigInteger)mid << 32) | low;

            return new DecimalQuantity { Coefficient = coefficient, Scale = scale, IsNegative = isNegative };
        }

        public static bool TryFromString(
            string value,
            [NotNullWhen(true)] out DecimalQuantity? quantity)
        {
            quantity = null;
            var source = value.Trim();
            if (source.Length == 0)
            {
                return false;
            }

            var index = 0;
            var isNegative = false;
            if (source[index] is '+' or '-')
            {
                isNegative = source[index] == '-';
                index++;
                if (index == source.Length)
                {
                    return false;
                }
            }

            var coefficient = BigInteger.Zero;
            var fractionDigits = 0;
            var sawDigit = false;
            var sawDecimal = false;
            for (; index < source.Length; index++)
            {
                var ch = source[index];
                if (ch is '.')
                {
                    if (sawDecimal)
                    {
                        return false;
                    }

                    sawDecimal = true;
                    continue;
                }

                if (ch is 'e' or 'E')
                {
                    break;
                }

                if (ch < '0' || ch > '9')
                {
                    return false;
                }

                sawDigit = true;
                coefficient = (coefficient * 10) + (ch - '0');
                if (sawDecimal)
                {
                    fractionDigits++;
                }
            }

            if (!sawDigit)
            {
                return false;
            }

            var exponent = 0;
            if (index < source.Length)
            {
                if (!TryParseExponent(source, index + 1, out exponent))
                {
                    return false;
                }
            }

            var scale = fractionDigits - exponent;
            if (scale < 0)
            {
                coefficient *= Pow10(-scale);
                scale = 0;
            }

            quantity = new DecimalQuantity
            {
                Coefficient = coefficient,
                Scale = scale,
                IsNegative = isNegative
            };
            return true;
        }

        private static bool TryParseExponent(string source, int index, out int exponent)
        {
            exponent = 0;
            if (index >= source.Length)
            {
                return false;
            }

            var sign = 1;
            if (source[index] is '+' or '-')
            {
                sign = source[index] == '-' ? -1 : 1;
                index++;
                if (index >= source.Length)
                {
                    return false;
                }
            }

            for (; index < source.Length; index++)
            {
                var ch = source[index];
                if (ch < '0' || ch > '9')
                {
                    return false;
                }

                var digit = ch - '0';
                if (exponent > (int.MaxValue - digit) / 10)
                {
                    return false;
                }

                exponent = (exponent * 10) + digit;
            }

            exponent *= sign;
            return true;
        }
    }

    private sealed class DecimalFormatResult
    {
        public required string Formatted { get; init; }
        public required string IntegerDigits { get; init; }
        public string? FractionDigits { get; init; }
        public string DecimalSeparator { get; init; } = string.Empty;
    }
}
