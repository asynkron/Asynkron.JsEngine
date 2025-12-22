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
            return IntlNumberFormatResult.FromParts(symbol,
                [new NumberFormatPart("nan", symbol)]);
        }

        if (double.IsPositiveInfinity(value))
        {
            var symbol = slots.Culture.NumberFormat.PositiveInfinitySymbol;
            return IntlNumberFormatResult.FromParts(symbol,
                [new NumberFormatPart("infinity", symbol)]);
        }

        if (double.IsNegativeInfinity(value))
        {
            var minus = slots.Culture.NumberFormat.NegativeSign;
            var infinity = slots.Culture.NumberFormat.PositiveInfinitySymbol;
            var formatted = slots.Culture.NumberFormat.NegativeInfinitySymbol;
            return IntlNumberFormatResult.FromParts(formatted,
            [
                new NumberFormatPart("minusSign", minus),
                new NumberFormatPart("infinity", infinity)
            ]);
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
        if (slots.Style == "percent")
        {
            MultiplyByPowerOfTen(quantity, 2);
        }

        if (quantity.Coefficient.IsZero)
        {
            quantity.IsNegative = false;
        }

        if (slots.UseSignificantDigits)
        {
            ApplyMaximumSignificantDigits(quantity, slots.MaximumSignificantDigits!.Value);
            EnsureMinimumSignificantDigits(quantity, slots.MinimumSignificantDigits!.Value);
        }
        else
        {
            ApplyMaximumFractionDigits(quantity, slots.MaximumFractionDigits);
        }

        var result = slots.Notation switch
        {
            "scientific" => FormatScientificNotation(quantity, slots, true),
            "engineering" => FormatScientificNotation(quantity, slots, false),
            _ => IntlNumberFormatResult.FromLiteral(
                FormatDecimal(quantity, slots, slots.UseGrouping, false).Formatted)
        };

        if (wasNegative)
        {
            var minus = slots.Culture.NumberFormat.NegativeSign;
            result.Formatted = $"{minus}{result.Formatted}";
            result.Parts?.Insert(0, new NumberFormatPart("minusSign", minus));
        }

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

    private static DecimalFormatResult FormatDecimal(
        DecimalQuantity quantity,
        IntlNumberFormatInternalSlots slots,
        bool useGrouping,
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

        var integerPortion = useGrouping
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
            Coefficient = adjustedCoefficient, Scale = adjustedScale, IsNegative = false
        };

        ApplyMaximumFractionDigits(mantissaQuantity, slots.MaximumFractionDigits);

        var mantissa = FormatDecimal(mantissaQuantity, slots, false, true,
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

    private static void ApplyMaximumFractionDigits(DecimalQuantity quantity, int maxFractionDigits)
    {
        if (quantity.Scale <= maxFractionDigits)
        {
            return;
        }

        var digitsToTrim = quantity.Scale - maxFractionDigits;
        TrimCoefficient(quantity, digitsToTrim);
        quantity.Scale = maxFractionDigits;
    }

    private static void ApplyMaximumSignificantDigits(DecimalQuantity quantity, int maxDigits)
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
        if (ShouldRoundUp(remainder, divisor))
        {
            rounded += BigInteger.One;
        }

        quantity.Coefficient = rounded * divisor;
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

    private static void TrimCoefficient(DecimalQuantity quantity, int digitsToTrim)
    {
        if (digitsToTrim <= 0 || quantity.Coefficient.IsZero)
        {
            return;
        }

        var divisor = Pow10(digitsToTrim);
        var truncated = BigInteger.DivRem(quantity.Coefficient, divisor, out var remainder);
        if (ShouldRoundUp(remainder, divisor))
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
                Coefficient = coefficient, Scale = -exponentAdjustment, IsNegative = isNegative
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
