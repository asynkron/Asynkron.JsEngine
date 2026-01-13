#region

using System.Globalization;
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

        numberObj.SetHostedProperty("toFixed", args =>
        {
            var fractionDigits = args.Count > 0 && args[0].TryGetDouble(out var d) ? (int)d : 0;
            if (fractionDigits is < 0 or > 100)
            {
                throw ThrowRangeError("toFixed() digits argument must be between 0 and 100", realm: realm);
            }

            if (double.IsNaN(num))
            {
                return (JsValue)"NaN";
            }

            if (double.IsInfinity(num))
            {
                return (JsValue)(num > 0 ? "Infinity" : "-Infinity");
            }

            return (JsValue)num.ToString("F" + fractionDigits, CultureInfo.InvariantCulture);
        });

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
                return (JsValue)num.ToString(CultureInfo.InvariantCulture);
            }

            if (options.TryGetProperty("style", out var styleVal) && !styleVal.IsNullOrUndefined)
            {
                var style = JsOps.ToJsString(styleVal);
                if (string.Equals(style, "unit", StringComparison.OrdinalIgnoreCase) &&
                    options.TryGetProperty("unit", out var unitVal) &&
                    !unitVal.IsNullOrUndefined)
                {
                    return (JsValue)$"{num.ToString(CultureInfo.InvariantCulture)} {JsOps.ToJsString(unitVal)}";
                }
            }

            return (JsValue)num.ToString(CultureInfo.InvariantCulture);
        });
    }

    private static string FormatExponentialForJs(string netExponential)
    {
        var eIndex = netExponential.IndexOf('e', StringComparison.Ordinal);
        if (eIndex < 0)
        {
            return netExponential;
        }

        var mantissa = netExponential[..(eIndex + 1)];
        var exponent = netExponential[(eIndex + 1)..];
        var sign = "";
        if (exponent.Length > 0 && (exponent[0] == '+' || exponent[0] == '-'))
        {
            sign = exponent[..1];
            exponent = exponent[1..];
        }

        exponent = exponent.TrimStart('0');
        if (exponent.Length == 0)
        {
            exponent = "0";
        }

        return mantissa + sign + exponent;
    }

    /// <summary>
    /// Core toExponential formatting logic shared by NumberPrototype and fallback wrapper.
    /// </summary>
    internal static string FormatToExponentialCore(double num, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (double.IsNaN(num))
        {
            return "NaN";
        }

        if (double.IsInfinity(num))
        {
            return num > 0 ? "Infinity" : "-Infinity";
        }

        string result;
        if (args.Count == 0 || !args[0].TryGetDouble(out var d))
        {
            result = num.ToString("e", CultureInfo.InvariantCulture);
        }
        else
        {
            var fractionDigits = (int)d;
            if (fractionDigits is < 0 or > 100)
            {
                throw ThrowRangeError("toExponential() digits argument must be between 0 and 100", realm: realm);
            }

            result = num.ToString("e" + fractionDigits, CultureInfo.InvariantCulture);
        }

        return FormatExponentialForJs(result);
    }

    /// <summary>
    /// Core toPrecision formatting logic shared by NumberPrototype and fallback wrapper.
    /// </summary>
    internal static string FormatToPrecisionCore(double num, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (args.Count == 0)
        {
            return num.ToString(CultureInfo.InvariantCulture);
        }

        if (double.IsNaN(num))
        {
            return "NaN";
        }

        if (double.IsInfinity(num))
        {
            return num > 0 ? "Infinity" : "-Infinity";
        }

        if (!args[0].TryGetDouble(out var d))
        {
            return num.ToString(CultureInfo.InvariantCulture);
        }

        var precision = (int)d;
        if (precision is < 1 or > 100)
        {
            throw ThrowRangeError("toPrecision() precision argument must be between 1 and 100", realm: realm);
        }

        return num.ToString("G" + precision, CultureInfo.InvariantCulture);
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

            if (Math.Abs(num % 1) < double.Epsilon)
            {
                return ((long)num).ToString(CultureInfo.InvariantCulture);
            }

            return num.ToString(CultureInfo.InvariantCulture);
        }

        var intValue = (long)num;
        var isNegative = intValue < 0;
        if (isNegative)
        {
            intValue = -intValue;
        }

        if (intValue == 0)
        {
            return "0";
        }

        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var result = "";
        while (intValue > 0)
        {
            result = digits[(int)(intValue % radix)] + result;
            intValue /= radix;
        }

        return isNegative ? "-" + result : result;
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
