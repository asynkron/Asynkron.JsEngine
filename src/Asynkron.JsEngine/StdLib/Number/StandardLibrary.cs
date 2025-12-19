using System.Globalization;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
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
            var radixNumber = radixArg.IsUndefined ? 10d : JsOps.ToNumber(radixArg.ToObject());
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

        numberObj.SetHostedProperty("toExponential", args =>
        {
            if (double.IsNaN(num))
            {
                return (JsValue)"NaN";
            }

            if (double.IsInfinity(num))
            {
                return (JsValue)(num > 0 ? "Infinity" : "-Infinity");
            }

            string result;
            if (args.Count <= 0 || !args[0].TryGetDouble(out var d))
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

            return (JsValue)FormatExponentialForJs(result);
        });

        numberObj.SetHostedProperty("toPrecision", args =>
        {
            if (args.Count == 0)
            {
                return (JsValue)num.ToString(CultureInfo.InvariantCulture);
            }

            if (double.IsNaN(num))
            {
                return (JsValue)"NaN";
            }

            if (double.IsInfinity(num))
            {
                return (JsValue)(num > 0 ? "Infinity" : "-Infinity");
            }

            if (!args[0].TryGetDouble(out var d))
            {
                return (JsValue)num.ToString(CultureInfo.InvariantCulture);
            }

            var precision = (int)d;
            if (precision is < 1 or > 100)
            {
                throw ThrowRangeError("toPrecision() precision argument must be between 1 and 100", realm: realm);
            }

            return (JsValue)num.ToString("G" + precision, CultureInfo.InvariantCulture);
        });

        numberObj.SetHostedProperty("valueOf", _ => num);

        numberObj.SetHostedProperty("toLocaleString", args =>
        {
            var localesArg = args.GetArgument(0);
            var optionsArg = args.GetArgument(1);

            if (realm is not null &&
                TryFormatWithIntlNumberFormat(num, localesArg.ToObject(), optionsArg.ToObject(), realm, out var formatted))
            {
                return JsValue.FromObjectUnsafe(formatted);
            }

            if (!optionsArg.TryGetObject<JsObject>(out var options) || options is null)
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
        if (eIndex < 0) return netExponential;
        var mantissa = netExponential[..(eIndex + 1)];
        var exponent = netExponential[(eIndex + 1)..];
        var sign = "";
        if (exponent.Length > 0 && (exponent[0] == '+' || exponent[0] == '-'))
        {
            sign = exponent[..1];
            exponent = exponent[1..];
        }

        exponent = exponent.TrimStart('0');
        if (exponent.Length == 0) exponent = "0";
        return mantissa + sign + exponent;
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

    internal static int ToIndex(object? value, RealmState? realm = null)
    {
        const double MaxLength = 9007199254740991d; // 2^53 - 1
        var context = realm?.CreateContext();

        var numeric = JsOps.ToNumeric(value, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (numeric is JsBigInt or Symbol or TypedAstSymbol)
        {
            throw ThrowTypeError("Index must be a non-negative integer", context, realm);
        }

        var numberValue = numeric is double d ? d : JsOps.ToNumber(numeric, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        var integerIndex = double.IsNaN(numberValue) || Math.Abs(numberValue) < double.Epsilon
            ? 0d
            : double.IsInfinity(numberValue)
                ? numberValue > 0 ? double.PositiveInfinity : double.NegativeInfinity
                : Math.Truncate(numberValue);

        if (double.IsPositiveInfinity(integerIndex) || integerIndex < 0)
        {
            throw ThrowRangeError("Index must be a non-negative integer", context, realm);
        }

        var index = integerIndex > MaxLength ? MaxLength : integerIndex;
        var sameValueZero = integerIndex == index || (integerIndex == 0 && index == 0);
        if (!sameValueZero)
        {
            throw ThrowRangeError("Index must be a non-negative integer", context, realm);
        }

        if (index > int.MaxValue)
        {
            throw ThrowRangeError("Index is too large", context, realm);
        }

        return (int)index;
    }

    internal static long ToIndexAsLong(object? value, RealmState? realm = null)
    {
        const double MaxLength = 9007199254740991d; // 2^53 - 1
        var context = realm?.CreateContext();

        var numeric = JsOps.ToNumeric(value, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (numeric is JsBigInt or Symbol or TypedAstSymbol)
        {
            throw ThrowTypeError("Index must be a non-negative integer", context, realm);
        }

        var numberValue = numeric is double d ? d : JsOps.ToNumber(numeric, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        var integerIndex = double.IsNaN(numberValue) || Math.Abs(numberValue) < double.Epsilon
            ? 0d
            : double.IsInfinity(numberValue)
                ? numberValue > 0 ? double.PositiveInfinity : double.NegativeInfinity
                : Math.Truncate(numberValue);

        if (double.IsPositiveInfinity(integerIndex) || integerIndex < 0)
        {
            throw ThrowRangeError("Index must be a non-negative integer", context, realm);
        }

        var index = integerIndex > MaxLength ? MaxLength : integerIndex;
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
    public static HostFunction CreateNumberConstructor(RealmState realm)
    {
        return NumberConstructor.CreateConstructor(realm);
    }

    [GeneratedRegex(@"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?")]
    internal static partial Regex FloatRegex();
}
