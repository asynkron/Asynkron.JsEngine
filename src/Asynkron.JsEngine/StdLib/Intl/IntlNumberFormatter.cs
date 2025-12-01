using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Asynkron.JsEngine.StdLib.Intl;

internal static class IntlNumberFormatter
{
    public static string FormatBigInteger(BigInteger value, IntlNumberFormatInternalSlots slots)
    {
        var quantity = DecimalQuantity.FromBigInteger(value);
        return FormatQuantity(quantity, slots);
    }

    public static string FormatDouble(double value, IntlNumberFormatInternalSlots slots)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return slots.Culture.NumberFormat.PositiveInfinitySymbol;
        }

        if (double.IsNegativeInfinity(value))
        {
            return slots.Culture.NumberFormat.NegativeInfinitySymbol;
        }

        var quantity = DecimalQuantity.FromDouble(value);
        return FormatQuantity(quantity, slots);
    }

    private static string FormatQuantity(DecimalQuantity quantity, IntlNumberFormatInternalSlots slots)
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

        var digits = quantity.Coefficient.ToString(CultureInfo.InvariantCulture);
        var integerDigits = ExtractIntegerDigits(digits, quantity.Scale, out var fractionDigits);
        integerDigits = PadIntegerDigits(integerDigits, slots.MinimumIntegerDigits);

        if (!slots.UseSignificantDigits)
        {
            if (fractionDigits.Length < slots.MinimumFractionDigits)
            {
                fractionDigits = fractionDigits.PadRight(slots.MinimumFractionDigits, '0');
            }
        }

        var groupedInteger = slots.UseGrouping
            ? ApplyGrouping(integerDigits, slots.Culture.NumberFormat)
            : integerDigits;

        var formatted = fractionDigits.Length > 0
            ? $"{groupedInteger}{slots.Culture.NumberFormat.NumberDecimalSeparator}{fractionDigits}"
            : groupedInteger;

        if (quantity.IsNegative && !quantity.Coefficient.IsZero)
        {
            formatted = $"{slots.Culture.NumberFormat.NegativeSign}{formatted}";
        }

        return slots.Style switch
        {
            "percent" => ApplyPercentPattern(formatted, slots.Culture.NumberFormat),
            "currency" when slots.Currency is { Length: > 0 } => FormatCurrency(formatted, slots),
            "unit" when slots.Unit is { Length: > 0 } => $"{formatted} {slots.Unit}",
            _ => formatted
        };
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

    private sealed class DecimalQuantity
    {
        public required BigInteger Coefficient { get; set; }
        public required int Scale { get; set; }
        public required bool IsNegative { get; set; }

        public static DecimalQuantity FromBigInteger(BigInteger value)
        {
            return new DecimalQuantity
            {
                Coefficient = BigInteger.Abs(value),
                Scale = 0,
                IsNegative = value.Sign < 0
            };
        }

        public static DecimalQuantity FromDouble(double value)
        {
            if (value == 0d)
            {
                return new DecimalQuantity
                {
                    Coefficient = BigInteger.Zero,
                    Scale = 0,
                    IsNegative = false
                };
            }

            var isNegative = IsNegative(value);
            var abs = Math.Abs(value);
            var raw = abs.ToString("G17", CultureInfo.InvariantCulture);
            var exponent = 0;
            var expIndex = raw.IndexOfAny(new[] { 'e', 'E' });
            if (expIndex >= 0)
            {
                exponent = int.Parse(raw[(expIndex + 1)..], CultureInfo.InvariantCulture);
                raw = raw[..expIndex];
            }

            var decimalIndex = raw.IndexOf('.');
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
                return new DecimalQuantity
                {
                    Coefficient = BigInteger.Zero,
                    Scale = 0,
                    IsNegative = false
                };
            }

            var exponentAdjustment = exponent - fractionLength;
            if (exponentAdjustment >= 0)
            {
                coefficient *= Pow10(exponentAdjustment);
                return new DecimalQuantity
                {
                    Coefficient = coefficient,
                    Scale = 0,
                    IsNegative = isNegative
                };
            }

            return new DecimalQuantity
            {
                Coefficient = coefficient,
                Scale = -exponentAdjustment,
                IsNegative = isNegative
            };
        }
    }

    private static bool IsNegative(double value)
    {
        return BitConverter.DoubleToInt64Bits(value) < 0 && value != 0d;
    }
}
