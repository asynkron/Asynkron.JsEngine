#region

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static partial class NumberHelper
{
    public static JsObject CreateNumberWrapper(double num, EvaluationContext? context = null, RealmState? realm = null)
    {
        var numberObj = new JsObject();
        numberObj["__value__"] = num;
        var prototype = context?.RealmState?.NumberPrototype ?? realm?.NumberPrototype;
        if (prototype is not null)
        {
            numberObj.SetPrototype(prototype);
            return numberObj;
        }

        AddNumberMethods(numberObj, num, context?.RealmState ?? realm);
        return numberObj;
    }

    /// <summary>
    ///     Fallback attachment of number instance methods when no prototype is available.
    /// </summary>
    private static void AddNumberMethods(JsObject numberObj, double num, RealmState? realm = null)
    {
        numberObj.SetHostedProperty("toString", args =>
        {
            var radixArg = args.GetArgument(0);
            var radixNumber = radixArg.IsUndefined ? 10d : JsOps.ToNumber(radixArg);
            if (double.IsNaN(radixNumber) || Math.Abs(radixNumber % 1) > double.Epsilon)
            {
                throw ThrowRangeError("radix must be an integer at least 2 and no greater than 36", realm: realm);
            }

            var radix = (int)radixNumber;
            if (radix is < 2 or > 36)
            {
                throw ThrowRangeError("radix must be an integer at least 2 and no greater than 36", realm: realm);
            }

            return (JsValue)NumberToString(num, radix);
        });

        numberObj.SetHostedProperty("toFixed", args => (JsValue)FormatToFixedCore(num, args, realm));

        numberObj.SetHostedProperty("toExponential", args => (JsValue)FormatToExponentialCore(num, args, realm));

        numberObj.SetHostedProperty("toPrecision", args => (JsValue)FormatToPrecisionCore(num, args, realm));

        numberObj.SetHostedProperty("valueOf", _ => num);

        numberObj.SetHostedProperty("toLocaleString", args =>
        {
            var localesArg = args.GetArgument(0);
            var optionsArg = args.GetArgument(1);

            if (realm is not null &&
                TryFormatWithIntlNumberFormatJsValue(num, localesArg, optionsArg, realm, out var formatted))
            {
                return formatted;
            }

            if (!optionsArg.TryGetObject<JsObject>(out var options))
            {
                return (JsValue)NumberToString(num, 10);
            }

            if (options.TryGetProperty("style", out var styleVal) && !styleVal.IsNullOrUndefined)
            {
                var style = JsOps.ToJsString(styleVal);
                if (string.Equals(style, "unit", StringComparison.OrdinalIgnoreCase) &&
                    options.TryGetProperty("unit", out var unitVal) &&
                    !unitVal.IsNullOrUndefined)
                {
                    return (JsValue)$"{NumberToString(num, 10)} {JsOps.ToJsString(unitVal)}";
                }
            }

            return (JsValue)NumberToString(num, 10);
        });
    }

    /// <summary>
    ///     Performs ToIntegerOrInfinity on a JsValue argument, as required by
    ///     toFixed/toExponential/toPrecision per the ECMAScript spec.
    ///     This calls ToNumber (which triggers valueOf/toString) and then truncates.
    /// </summary>
    private static double ToIntegerOrInfinity(JsValue arg)
    {
        var n = JsOps.ToNumber(arg);
        if (double.IsNaN(n) || n == 0.0)
        {
            return 0.0;
        }

        if (double.IsInfinity(n))
        {
            return n;
        }

        return Math.Truncate(n);
    }

    /// <summary>
    ///     Returns true if the value is negative zero.
    /// </summary>
    private static bool IsNegZero(double value)
    {
        return value == 0.0 && double.IsNegative(value);
    }

    /// <summary>
    ///     Core toFixed formatting logic shared by NumberPrototype and fallback wrapper.
    ///     Implements ES2024 Number.prototype.toFixed(fractionDigits).
    /// </summary>
    internal static string FormatToFixedCore(double x, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        // Step 2: Let f be ? ToIntegerOrInfinity(fractionDigits)
        var fractionDigitsArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var f = fractionDigitsArg.IsUndefined ? 0.0 : ToIntegerOrInfinity(fractionDigitsArg);

        // Step 3: If f is not finite, throw RangeError
        if (double.IsInfinity(f))
        {
            throw ThrowRangeError("toFixed() digits argument must be between 0 and 100", realm: realm);
        }

        // Step 4: If f < 0 or f > 100, throw RangeError
        var fractionDigits = (int)f;
        if (fractionDigits is < 0 or > 100)
        {
            throw ThrowRangeError("toFixed() digits argument must be between 0 and 100", realm: realm);
        }

        // Step 5-6: Handle NaN
        if (double.IsNaN(x))
        {
            return "NaN";
        }

        // Handle Infinity
        if (double.IsInfinity(x))
        {
            return x > 0 ? "Infinity" : "-Infinity";
        }

        // Step 8: If |x| >= 10^21, return ToString(x)
        if (Math.Abs(x) >= 1e21)
        {
            return NumberToString(x, 10);
        }

        // Format using the exact IEEE 754 value per spec exactness requirement.
        return FormatFixedFromExact(x, fractionDigits);
    }

    /// <summary>
    ///     Format a number with fixed-point notation using the exact IEEE 754 value.
    ///     This handles the spec's "exactness" requirement: (1000000000000000128).toFixed(0)
    ///     must return "1000000000000000128", not "1000000000000000100".
    /// </summary>
    private static string FormatFixedFromExact(double x, int fractionDigits)
    {
        if (x == 0.0)
        {
            return fractionDigits == 0 ? "0" : "0." + new string('0', fractionDigits);
        }

        var negative = x < 0;
        var absX = Math.Abs(x);

        // Get the exact decimal representation with full IEEE 754 precision
        GetExactDigits(absX, 50, out var digits, out var dotPosition);

        // totalDigitsNeeded is the number of significant digits before + after decimal = dotPosition + fractionDigits
        var totalDigitsNeeded = dotPosition + fractionDigits;

        if (totalDigitsNeeded <= 0)
        {
            // The number rounds to 0 at this precision, but check rounding
            if (totalDigitsNeeded == 0 && digits.Length > 0 && digits[0] >= '5')
            {
                var result = negative ? "-1" : "1";
                if (fractionDigits > 0)
                {
                    result += "." + new string('0', fractionDigits);
                }

                return result;
            }

            return fractionDigits == 0
                ? (negative ? "-0" : "0")
                : (negative ? "-0." : "0.") + new string('0', fractionDigits);
        }

        // Round the digit string to totalDigitsNeeded digits
        var rounded = RoundDigitString(digits, totalDigitsNeeded);

        // If rounding caused carry, dotPosition increases by 1
        if (rounded.Length > Math.Max(digits.Length, totalDigitsNeeded))
        {
            dotPosition++;
            totalDigitsNeeded = dotPosition + fractionDigits;
            if (rounded.Length > totalDigitsNeeded)
            {
                rounded = rounded[..totalDigitsNeeded];
            }
        }

        // Pad with zeros if needed
        while (rounded.Length < totalDigitsNeeded)
        {
            rounded += '0';
        }

        // Truncate excess
        if (rounded.Length > totalDigitsNeeded && totalDigitsNeeded > 0)
        {
            rounded = rounded[..totalDigitsNeeded];
        }

        // Build integer part
        var intPart = dotPosition > 0 ? rounded[..Math.Min(dotPosition, rounded.Length)] : "0";
        while (intPart.Length < dotPosition)
        {
            intPart += '0';
        }

        var sb = new StringBuilder();
        if (negative)
        {
            sb.Append('-');
        }

        sb.Append(intPart);

        if (fractionDigits > 0)
        {
            sb.Append('.');
            var fracStart = dotPosition >= 0 ? dotPosition : 0;
            if (dotPosition < 0)
            {
                sb.Append('0', -dotPosition);
                fracStart = 0;
            }

            if (fracStart < rounded.Length)
            {
                sb.Append(rounded[fracStart..]);
            }

            // Ensure exactly fractionDigits after the dot
            var dotIdx = sb.ToString().IndexOf('.');
            var currentFracLen = sb.Length - dotIdx - 1;
            if (currentFracLen > fractionDigits)
            {
                sb.Length = dotIdx + 1 + fractionDigits;
            }
            else
            {
                while (currentFracLen < fractionDigits)
                {
                    sb.Append('0');
                    currentFracLen++;
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Get the exact decimal digits and dot position of a positive double value,
    ///     requesting at least minSignificantDigits significant digits.
    ///     Uses "G" format with high enough precision to reveal the exact IEEE 754 value.
    /// </summary>
    private static void GetExactDigits(double absX, int minSignificantDigits, out string digits, out int dotPosition)
    {
        // Use "G" format with enough precision. IEEE 754 doubles have at most ~49 significant
        // decimal digits in their exact representation, so G50 gives the full exact value.
        // For fewer digits needed, "R" (which gives 15-17 digits) is sufficient and faster.
        var precision = Math.Max(minSignificantDigits + 2, 21); // +2 for rounding safety
        if (precision > 50)
        {
            precision = 50; // G50 gives the full exact representation
        }

        var repr = absX.ToString("G" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        ParseDecimalRepresentation(repr, out digits, out dotPosition);
    }

    /// <summary>
    ///     Parse a decimal string representation (possibly with 'E' notation from "R" format)
    ///     into a digit string and a dot position (number of digits before decimal point).
    /// </summary>
    private static void ParseDecimalRepresentation(string repr, out string digits, out int dotPosition)
    {
        var eIndex = repr.IndexOf('E', StringComparison.Ordinal);
        if (eIndex < 0)
        {
            eIndex = repr.IndexOf('e', StringComparison.Ordinal);
        }

        if (eIndex >= 0)
        {
            var mantissa = repr[..eIndex];
            var exponent = int.Parse(repr[(eIndex + 1)..], CultureInfo.InvariantCulture);

            var dotIdx = mantissa.IndexOf('.');
            string mantDigits;
            int mantDotPos;
            if (dotIdx >= 0)
            {
                mantDigits = mantissa.Replace(".", "");
                mantDotPos = dotIdx;
            }
            else
            {
                mantDigits = mantissa;
                mantDotPos = mantissa.Length;
            }

            dotPosition = mantDotPos + exponent;
            digits = mantDigits;
        }
        else
        {
            var dotIdx = repr.IndexOf('.');
            if (dotIdx >= 0)
            {
                digits = repr.Replace(".", "");
                dotPosition = dotIdx;
            }
            else
            {
                digits = repr;
                dotPosition = repr.Length;
            }
        }
    }

    /// <summary>
    ///     Round a digit string to the specified length, using "round half away from zero"
    ///     (i.e., pick the larger n) as required by the ES spec.
    /// </summary>
    private static string RoundDigitString(string digits, int length)
    {
        if (length >= digits.Length)
        {
            return digits;
        }

        if (length <= 0)
        {
            if (digits.Length > 0 && digits[0] >= '5')
            {
                return "1";
            }

            return "";
        }

        var result = digits[..length].ToCharArray();

        var roundDigit = digits[length] - '0';
        if (roundDigit >= 5)
        {
            // Round up (away from zero, as spec requires)
            var carry = true;
            for (var i = result.Length - 1; i >= 0 && carry; i--)
            {
                var d = result[i] - '0' + 1;
                if (d >= 10)
                {
                    result[i] = '0';
                }
                else
                {
                    result[i] = (char)('0' + d);
                    carry = false;
                }
            }

            if (carry)
            {
                return "1" + new string(result);
            }
        }

        return new string(result);
    }

    /// <summary>
    ///     Core toExponential formatting logic shared by NumberPrototype and fallback wrapper.
    ///     Implements ES2024 Number.prototype.toExponential(fractionDigits).
    /// </summary>
    internal static string FormatToExponentialCore(double x, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        // Step 2: Let f be ? ToIntegerOrInfinity(fractionDigits)
        // Must call ToNumber on the argument even for NaN/Infinity x (spec step ordering).
        var fractionDigitsArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var fractionDigitsIsUndefined = fractionDigitsArg.IsUndefined;
        var f = fractionDigitsIsUndefined ? 0.0 : ToIntegerOrInfinity(fractionDigitsArg);

        // Step 3: NaN check (after ToInteger)
        if (double.IsNaN(x))
        {
            return "NaN";
        }

        // Step 4: Infinity check
        if (double.IsInfinity(x))
        {
            return x > 0 ? "Infinity" : "-Infinity";
        }

        // Step 5-7: Range check (only if fractionDigits is not undefined)
        if (!fractionDigitsIsUndefined)
        {
            if (double.IsInfinity(f) || f < 0 || f > 100)
            {
                throw ThrowRangeError("toExponential() digits argument must be between 0 and 100", realm: realm);
            }
        }

        // Step 8: Determine sign
        var s = "";
        if (x < 0)
        {
            s = "-";
            x = -x;
        }
        else if (IsNegZero(x))
        {
            // -0 produces "0e+0" (no negative sign)
            x = 0.0;
        }

        // Step 9: If x = 0
        if (x == 0.0)
        {
            var fractionDigitsInt = (int)f;
            string m;
            if (fractionDigitsIsUndefined || fractionDigitsInt == 0)
            {
                m = "0";
            }
            else
            {
                m = "0." + new string('0', fractionDigitsInt);
            }

            return s + m + "e+0";
        }

        // Step 10: x != 0
        string mantissa;
        int exponent;

        if (fractionDigitsIsUndefined)
        {
            FormatExponentialShortest(x, out mantissa, out exponent);
        }
        else
        {
            FormatExponentialWithFractionDigits(x, (int)f, out mantissa, out exponent);
        }

        var expSign = exponent >= 0 ? "+" : "-";
        var expStr = Math.Abs(exponent).ToString(CultureInfo.InvariantCulture);

        return s + mantissa + "e" + expSign + expStr;
    }

    /// <summary>
    ///     Format a positive nonzero double in exponential form with the shortest mantissa.
    ///     Corresponds to the "fractionDigits is undefined" branch of the spec.
    /// </summary>
    private static void FormatExponentialShortest(double x, out string mantissa, out int exponent)
    {
        // Use "R" format for shortest representation (this is correct for undefined fractionDigits)
        var repr = x.ToString("R", CultureInfo.InvariantCulture);
        ParseDecimalRepresentation(repr, out var digits, out var dotPosition);

        var trimmed = digits.TrimEnd('0');
        if (trimmed.Length == 0)
        {
            trimmed = "0";
        }

        // Skip leading zeros and adjust exponent accordingly
        // e.g., "00001" with dotPosition=1 means 0.0001 = 1e-4
        var leadingZeros = 0;
        while (leadingZeros < trimmed.Length - 1 && trimmed[leadingZeros] == '0')
        {
            leadingZeros++;
        }

        if (leadingZeros > 0)
        {
            trimmed = trimmed[leadingZeros..];
        }

        exponent = dotPosition - 1 - leadingZeros;

        mantissa = trimmed.Length == 1
            ? trimmed
            : trimmed[..1] + "." + trimmed[1..];
    }

    /// <summary>
    ///     Format a positive nonzero double in exponential form with exactly fractionDigits
    ///     digits after the decimal point.
    ///     Uses "round half away from zero" as required by the spec.
    /// </summary>
    private static void FormatExponentialWithFractionDigits(double x, int fractionDigits, out string mantissa,
        out int exponent)
    {
        var totalDigits = fractionDigits + 1;

        // Get enough digits for the requested precision
        GetExactDigits(x, totalDigits + 1, out var digits, out var dotPosition);

        // Skip leading zeros and adjust dotPosition
        // e.g., for 0.0001: digits="0000100...", dotPosition=1 → skip 4 zeros, dotPosition=-3
        var leadingZeros = 0;
        while (leadingZeros < digits.Length - 1 && digits[leadingZeros] == '0')
        {
            leadingZeros++;
        }

        if (leadingZeros > 0)
        {
            digits = digits[leadingZeros..];
            dotPosition -= leadingZeros;
        }

        exponent = dotPosition - 1;

        var rounded = RoundDigitString(digits, totalDigits);

        // Check if rounding caused carry (e.g., 9.99 rounds to 10.0 -> 1.00e+1)
        if (rounded.Length > totalDigits)
        {
            exponent++;
            rounded = rounded[..totalDigits];
        }

        while (rounded.Length < totalDigits)
        {
            rounded += '0';
        }

        mantissa = fractionDigits == 0
            ? rounded[..1]
            : rounded[..1] + "." + rounded[1..];
    }

    /// <summary>
    ///     Core toPrecision formatting logic shared by NumberPrototype and fallback wrapper.
    ///     Implements ES2024 Number.prototype.toPrecision(precision).
    /// </summary>
    internal static string FormatToPrecisionCore(double x, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        // Step 2: If precision is undefined, return ToString(x)
        var precisionArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (precisionArg.IsUndefined)
        {
            return NumberToString(x, 10);
        }

        // Step 3: Let p be ? ToIntegerOrInfinity(precision)
        var p = ToIntegerOrInfinity(precisionArg);

        // Step 4: If x is NaN, return "NaN"
        if (double.IsNaN(x))
        {
            return "NaN";
        }

        // Step 5-6: Infinity
        if (double.IsInfinity(x))
        {
            return x > 0 ? "Infinity" : "-Infinity";
        }

        // Step 8: Range check
        if (double.IsInfinity(p) || p < 1 || p > 100)
        {
            throw ThrowRangeError("toPrecision() precision argument must be between 1 and 100", realm: realm);
        }

        var precision = (int)p;

        // Determine sign
        var s = "";
        if (x < 0)
        {
            s = "-";
            x = -x;
        }
        else if (IsNegZero(x))
        {
            x = 0.0;
        }

        // Step 9: If x = 0
        if (x == 0.0)
        {
            var m = new string('0', precision);
            // e = 0, so e = p-1 only when precision = 1
            if (precision == 1)
            {
                return s + m;
            }

            return s + m[..1] + "." + m[1..];
        }

        // Step 10: x != 0
        GetExactDigits(x, precision + 1, out var digits, out var dotPosition);

        var rounded = RoundDigitString(digits, precision);

        if (rounded.Length > precision)
        {
            dotPosition++;
            rounded = rounded[..precision];
        }

        while (rounded.Length < precision)
        {
            rounded += '0';
        }

        var exp = dotPosition - 1;

        // If e = p-1, no decimal point needed
        if (exp == precision - 1)
        {
            return s + rounded;
        }

        // If e >= 0 and e < p-1, insert decimal point
        if (exp >= 0 && exp < precision - 1)
        {
            var intDigits = exp + 1;
            return s + rounded[..intDigits] + "." + rounded[intDigits..];
        }

        // e < -6 or e >= p: exponential notation
        if (exp < -6 || exp >= precision)
        {
            string m;
            if (precision == 1)
            {
                m = rounded[..1];
            }
            else
            {
                m = rounded[..1] + "." + rounded[1..];
            }

            var expSign = exp >= 0 ? "+" : "-";
            var expStr = Math.Abs(exp).ToString(CultureInfo.InvariantCulture);
            return s + m + "e" + expSign + expStr;
        }

        // e is between -6 and -1: use "0." + zeros + digits format
        var leadingZeros = -(exp + 1);
        return s + "0." + new string('0', leadingZeros) + rounded;
    }

    internal static string NumberToString(double num, int radix)
    {
        if (double.IsNaN(num))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(num))
        {
            return "Infinity";
        }

        if (double.IsNegativeInfinity(num))
        {
            return "-Infinity";
        }

        if (radix == 10)
        {
            if (num == 0)
            {
                return "0";
            }

            // Use "R" (round-trip) format to get exact representation,
            // then convert E-notation to the form JavaScript expects.
            var repr = num.ToString("R", CultureInfo.InvariantCulture);

            // If "R" format produces E-notation, convert to JS form
            var eIdx = repr.IndexOf('E', StringComparison.Ordinal);
            if (eIdx >= 0)
            {
                return FormatRoundTripAsJsString(repr);
            }

            return repr;
        }

        // Non-base-10: truncate to integer, then convert
        var isNegative = num < 0;
        var absNum = Math.Abs(Math.Truncate(num));

        if (absNum == 0)
        {
            return "0";
        }

        // Check if the number fits in a long safely
        if (absNum <= 9007199254740991d) // MAX_SAFE_INTEGER
        {
            var intValue = (long)absNum;
            const string digitChars = "0123456789abcdefghijklmnopqrstuvwxyz";
            var result = "";
            while (intValue > 0)
            {
                result = digitChars[(int)(intValue % radix)] + result;
                intValue /= radix;
            }

            return isNegative ? "-" + result : result;
        }

        // For very large numbers, convert via string-based division
        var intStr = FormatRoundTripAsJsString(absNum.ToString("R", CultureInfo.InvariantCulture));
        var dotIdx2 = intStr.IndexOf('.');
        if (dotIdx2 >= 0)
        {
            intStr = intStr[..dotIdx2];
        }

        var resultChars = ConvertBase10StringToRadix(intStr, radix);
        return isNegative ? "-" + resultChars : resultChars;
    }

    /// <summary>
    ///     Convert a round-trip "R" format string (which may use 'E' notation)
    ///     to the JavaScript-style string representation.
    /// </summary>
    internal static string FormatRoundTripAsJsString(string repr)
    {
        var eIdx = repr.IndexOf('E', StringComparison.Ordinal);
        if (eIdx < 0)
        {
            return repr;
        }

        var mantissa = repr[..eIdx];
        var exponent = int.Parse(repr[(eIdx + 1)..], CultureInfo.InvariantCulture);

        var isNeg = mantissa[0] == '-';
        if (isNeg)
        {
            mantissa = mantissa[1..];
        }

        var dotIdx = mantissa.IndexOf('.');
        string digits;
        int mantDotPos;
        if (dotIdx >= 0)
        {
            digits = mantissa.Replace(".", "");
            mantDotPos = dotIdx;
        }
        else
        {
            digits = mantissa;
            mantDotPos = mantissa.Length;
        }

        // n = decimalPos is the exponent in the spec sense: s × 10^(n-k) = x
        var n = mantDotPos + exponent;
        var k = digits.Length;

        // Trim trailing zeros from digits to get the minimal k
        var trimmedDigits = digits.TrimEnd('0');
        if (trimmedDigits.Length == 0)
        {
            trimmedDigits = "0";
        }

        k = trimmedDigits.Length;

        // ES2024 7.1.12.1 Number::toString steps 5-9:
        string result;
        if (k <= n && n <= 21)
        {
            // Step 5: k ≤ n ≤ 21 → s₁...sₖ followed by (n-k) zeros
            result = trimmedDigits + new string('0', n - k);
        }
        else if (0 < n && n <= 21)
        {
            // Step 6: 0 < n ≤ 21 (and n < k) → s₁...sₙ.sₙ₊₁...sₖ
            result = trimmedDigits[..n] + "." + trimmedDigits[n..];
        }
        else if (-5 < n && n <= 0)
        {
            // Step 7: -5 < n ≤ 0 → "0." + zeros + digits
            result = "0." + new string('0', -n) + trimmedDigits;
        }
        else if (k == 1)
        {
            // Step 8: single digit with exponent
            var exp = n - 1;
            result = trimmedDigits + "e" + (exp >= 0 ? "+" : "") + exp.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            // Step 9: multiple digits with exponent
            var exp = n - 1;
            result = trimmedDigits[..1] + "." + trimmedDigits[1..] + "e" + (exp >= 0 ? "+" : "") +
                     exp.ToString(CultureInfo.InvariantCulture);
        }

        return isNeg ? "-" + result : result;
    }

    /// <summary>
    ///     Convert a base-10 integer string to a different radix.
    /// </summary>
    private static string ConvertBase10StringToRadix(string base10, int radix)
    {
        const string digitChars = "0123456789abcdefghijklmnopqrstuvwxyz";
        var result = new StringBuilder();

        var numDigits = new int[base10.Length];
        for (var i = 0; i < base10.Length; i++)
        {
            numDigits[i] = base10[i] - '0';
        }

        while (true)
        {
            var allZero = true;
            for (var i = 0; i < numDigits.Length; i++)
            {
                if (numDigits[i] != 0)
                {
                    allZero = false;
                    break;
                }
            }

            if (allZero)
            {
                break;
            }

            var remainder = 0;
            for (var i = 0; i < numDigits.Length; i++)
            {
                var current = remainder * 10 + numDigits[i];
                numDigits[i] = current / radix;
                remainder = current % radix;
            }

            result.Insert(0, digitChars[remainder]);
        }

        return result.Length > 0 ? result.ToString() : "0";
    }

    /// <summary>
    ///     Converts a JsValue to a valid array index (int).
    ///     Throws RangeError if the index exceeds int.MaxValue.
    /// </summary>
    internal static int ToIndex(JsValue value, RealmState? realm = null)
    {
        var (index, context) = ToIndexCore(value, realm);

        if (index > int.MaxValue)
        {
            throw ThrowRangeError("Index is too large", context, realm);
        }

        return (int)index;
    }

    /// <summary>
    ///     Converts a JsValue to a valid array index (long).
    ///     Allows indices up to 2^53 - 1 (JavaScript's MAX_SAFE_INTEGER).
    /// </summary>
    internal static long ToIndexAsLong(JsValue value, RealmState? realm = null)
    {
        var (index, _) = ToIndexCore(value, realm);
        return index;
    }

    /// <summary>
    ///     Core implementation for ToIndex conversion. Returns the validated index as long
    ///     along with the evaluation context for error reporting.
    /// </summary>
    private static (long index, EvaluationContext? context) ToIndexCore(JsValue value, RealmState? realm)
    {
        const double MaxLength = 9007199254740991d; // 2^53 - 1
        var context = realm?.CreateContext();

        // Fast path for numbers
        if (value.Kind == JsValueKind.Number)
        {
            return (ToIndexFromNumberCore(value.NumberValue, MaxLength, context, realm), context);
        }

        // Handle other types via ToNumeric
        var numeric = JsOps.ToNumericAsJsValue(in value, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // BigInt and Symbol are not valid indices
        if (numeric.Kind == JsValueKind.BigInt ||
            numeric.TryGetObject<Symbol>(out _) ||
            numeric.TryGetObject<JsSymbol>(out _))
        {
            throw ThrowTypeError("Index must be a non-negative integer", context, realm);
        }

        // Fast path: avoid ToNumber call if already a number
        var numberValue = numeric.Kind == JsValueKind.Number ? numeric.NumberValue : JsOps.ToNumber(numeric);
        return (ToIndexFromNumberCore(numberValue, MaxLength, context, realm), context);
    }

    /// <summary>
    ///     Validates and converts a number to a valid index per ECMAScript ToIndex specification.
    /// </summary>
    private static long ToIndexFromNumberCore(double numberValue, double maxLength, EvaluationContext? context,
        RealmState? realm)
    {
        var integerIndex = double.IsNaN(numberValue) || Math.Abs(numberValue) < double.Epsilon
            ? 0d
            : double.IsInfinity(numberValue)
                ? numberValue > 0 ? double.PositiveInfinity : double.NegativeInfinity
                : Math.Truncate(numberValue);

        if (double.IsPositiveInfinity(integerIndex) || integerIndex < 0)
        {
            throw ThrowRangeError("Index must be a non-negative integer", context, realm);
        }

        var index = integerIndex > maxLength ? maxLength : integerIndex;
        var sameValueZero = integerIndex == index || (integerIndex == 0 && index == 0);
        if (!sameValueZero)
        {
            throw ThrowRangeError("Index must be a non-negative integer", context, realm);
        }

        return (long)index;
    }

    /// <summary>
    ///     Creates the Number constructor with static methods.
    /// </summary>
    [GeneratedRegex(@"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?", RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        5000)]
    internal static partial Regex FloatRegex();
}
