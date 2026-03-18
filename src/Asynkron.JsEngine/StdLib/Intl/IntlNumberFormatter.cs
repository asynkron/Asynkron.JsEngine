#region

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
            // NaN never gets a sign per spec
            return IntlNumberFormatResult.FromParts(symbol,
                [new NumberFormatPart("nan", symbol)]);
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
        return FormatQuantity(quantity, slots, wasNegative);
    }

    private static IntlNumberFormatResult FormatQuantity(
        DecimalQuantity quantity,
        IntlNumberFormatInternalSlots slots,
        bool wasNegative)
    {
        if (string.Equals(slots.Style, "percent", StringComparison.Ordinal))
        {
            MultiplyByPowerOfTen(quantity, 2);
        }

        // Note: do NOT clear IsNegative here for zero coefficients.
        // Negative zero (-0) must retain its sign per the spec.

        if (slots.UseSignificantDigits)
        {
            ApplyMaximumSignificantDigits(quantity, slots.MaximumSignificantDigits!.Value,
                wasNegative, slots.RoundingMode);
            EnsureMinimumSignificantDigits(quantity, slots.MinimumSignificantDigits!.Value);
        }
        else if (slots.Notation is not "scientific" and not "engineering")
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
                ApplyMaximumFractionDigits(quantity, slots.MaximumFractionDigits,
                    wasNegative, slots.RoundingMode);
            }
        }

        var result = slots.Notation switch
        {
            "scientific" => FormatScientificNotation(quantity, slots, true),
            "engineering" => FormatScientificNotation(quantity, slots, false),
            _ => FormatDecimalWithParts(quantity, slots)
        };

        ApplySignDisplay(result, wasNegative, quantity.Coefficient.IsZero, slots);

        return slots.Style switch
        {
            "percent" => IntlNumberFormatResult.FromLiteral(
                ApplyPercentPattern(result.Formatted, slots.Culture.NumberFormat)),
            "currency" when slots.Currency is { Length: > 0 } =>
                IntlNumberFormatResult.FromLiteral(FormatCurrency(result.Formatted, slots)),
            "unit" when slots.Unit is { Length: > 0 } =>
                IntlNumberFormatResult.FromLiteral($"{result.Formatted} {slots.Unit}"),
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

    private static string ApplyPercentPattern(string formatted, NumberFormatInfo format)
    {
        var symbol = format.PercentSymbol;
        var pattern = format.PercentPositivePattern;
        var space = pattern is 0 or 3 ? "\u00A0" : string.Empty;
        return pattern switch
        {
            0 => $"{formatted}{space}{symbol}",
            1 => $"{formatted}{symbol}",
            2 => $"{symbol}{formatted}",
            3 => $"{symbol}{space}{formatted}",
            _ => $"{formatted}{symbol}"
        };
    }

    private static string FormatCurrency(string formatted, IntlNumberFormatInternalSlots slots)
    {
        var display = slots.CurrencyDisplay switch
        {
            "code" => slots.Currency ?? string.Empty,
            "name" => slots.Currency ?? string.Empty,
            _ => slots.Currency ?? string.Empty
        };

        if (string.IsNullOrEmpty(display))
        {
            return formatted;
        }

        return $"{display} {formatted}";
    }

    private static bool ShouldRoundUp(BigInteger remainder, BigInteger divisor)
    {
        if (remainder.IsZero)
        {
            return false;
        }

        return remainder * 2 >= divisor;
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
    }

    private sealed class DecimalFormatResult
    {
        public required string Formatted { get; init; }
        public required string IntegerDigits { get; init; }
        public string? FractionDigits { get; init; }
        public string DecimalSeparator { get; init; } = string.Empty;
    }
}
