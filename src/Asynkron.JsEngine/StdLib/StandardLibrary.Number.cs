using System.Globalization;
using System.Numerics;
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

    public static JsObject CreateBigIntWrapper(JsBigInt value, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        var wrapper = new JsObject { ["__value__"] = value };

        var prototype = context?.RealmState?.BigIntPrototype ?? realm?.BigIntPrototype;
        if (prototype is not null)
        {
            wrapper.SetPrototype(prototype);
        }

        return wrapper;
    }

    /// <summary>
    ///     Fallback attachment of number instance methods when no prototype is available.
    /// </summary>
    private static void AddNumberMethods(JsObject numberObj, double num, RealmState? realm = null)
    {
        numberObj.SetHostedProperty("toString", args =>
        {
            var radixArg = args.GetArgument(0);
            var radixNumber = ReferenceEquals(radixArg, Symbol.Undefined) ? 10d : JsOps.ToNumber(radixArg);
            if (double.IsNaN(radixNumber) || Math.Abs(radixNumber % 1) > double.Epsilon)
            {
                throw ThrowRangeError("radix must be an integer at least 2 and no greater than 36", realm: realm);
            }

            var radix = (int)radixNumber;
            if (radix is < 2 or > 36)
            {
                throw ThrowRangeError("radix must be an integer at least 2 and no greater than 36", realm: realm);
            }

            return NumberToString(num, radix);
        });

        numberObj.SetHostedProperty("toFixed", args =>
        {
            var fractionDigits = args.Count > 0 && args[0] is double d ? (int)d : 0;
            if (fractionDigits is < 0 or > 100)
            {
                throw ThrowRangeError("toFixed() digits argument must be between 0 and 100", realm: realm);
            }

            if (double.IsNaN(num))
            {
                return "NaN";
            }

            if (double.IsInfinity(num))
            {
                return num > 0 ? "Infinity" : "-Infinity";
            }

            return num.ToString("F" + fractionDigits, CultureInfo.InvariantCulture);
        });

        numberObj.SetHostedProperty("toExponential", args =>
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
            if (args.Count <= 0 || args[0] is not double d)
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
        });

        numberObj.SetHostedProperty("toPrecision", args =>
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

            if (args[0] is not double d)
            {
                return num.ToString(CultureInfo.InvariantCulture);
            }

            var precision = (int)d;
            if (precision is < 1 or > 100)
            {
                throw ThrowRangeError("toPrecision() precision argument must be between 1 and 100", realm: realm);
            }

            return num.ToString("G" + precision, CultureInfo.InvariantCulture);
        });

        numberObj.SetHostedProperty("valueOf", _ => num);

        numberObj.SetHostedProperty("toLocaleString", args =>
        {
            var localesArg = args.GetArgument(0);
            var optionsArg = args.GetArgument(1);

            if (realm is not null &&
                TryFormatWithIntlNumberFormat(num, localesArg, optionsArg, realm, out var formatted))
            {
                return formatted;
            }

            if (optionsArg is JsObject options)
            {
                var style = options.TryGetProperty("style", out var styleVal) ? styleVal?.ToString() : null;
                if (string.Equals(style, "unit", StringComparison.OrdinalIgnoreCase) &&
                    options.TryGetProperty("unit", out var unitVal) &&
                    unitVal is not null &&
                    !ReferenceEquals(unitVal, Symbol.Undefined))
                {
                    return $"{num.ToString(CultureInfo.InvariantCulture)} {unitVal}";
                }
            }

            return num.ToString(CultureInfo.InvariantCulture);
        });
    }

    private static string FormatExponentialForJs(string netExponential)
    {
        var eIndex = netExponential.IndexOf('e');
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

    public static HostFunction CreateBigIntFunction(RealmState realm)
    {
        HostFunction bigIntFunction = null!;
        bigIntFunction = new HostFunction(BigIntCtor)
        {
            IsConstructor = true, DisallowConstruct = true, ConstructErrorMessage = "BigInt is not a constructor"
        };
        // length/name descriptors
        bigIntFunction.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        // name is already set on HostFunction; normalize attributes
        bigIntFunction.DefineProperty("name",
            new PropertyDescriptor { Value = "BigInt", Writable = false, Enumerable = false, Configurable = true });

        if (bigIntFunction.TryGetProperty("prototype", out var protoValue) && protoValue is JsObject proto)
        {
            realm.BigIntPrototype ??= proto;
            if (realm.ObjectPrototype is not null && proto.Prototype is null)
            {
                proto.SetPrototype(realm.ObjectPrototype);
            }

            proto.DefineProperty("constructor",
                new PropertyDescriptor
                {
                    Value = bigIntFunction, Writable = true, Enumerable = false, Configurable = true
                });

            DefineBuiltinFunction(proto, "toString", new HostFunction(BigIntPrototypeToString, realm), 0);
            DefineBuiltinFunction(proto, "valueOf", new HostFunction(BigIntPrototypeValueOf, realm), 0);
            var toLocaleFunction = new HostFunction(BigIntPrototypeToLocaleString, realm);
            DefineBuiltinFunction(proto, "toLocaleString", toLocaleFunction, 0);
        }

        DefineBuiltinFunction(
            bigIntFunction.PropertiesObject,
            "asIntN",
            new HostFunction(BigIntAsIntN, realm),
            2);

        DefineBuiltinFunction(
            bigIntFunction.PropertiesObject,
            "asUintN",
            new HostFunction(BigIntAsUintN, realm),
            2);

        bigIntFunction.SetProperty("name", "BigInt");
        bigIntFunction.SetProperty("length", 1d);

        return bigIntFunction;

        object? BigIntCtor(object? _, IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                throw ThrowTypeError("Cannot convert undefined to a BigInt", realm: realm);
            }

            return ToBigInt(args[0], realmState: realm);
        }

        object? BigIntPrototypeToString(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ThisBigIntValue(thisValue, realm);
            var radixArg = args.GetArgument(0);
            var radixNumber = ReferenceEquals(radixArg, Symbol.Undefined)
                ? 10d
                : radixArg is JsBigInt biRadix
                    ? (double)biRadix.Value
                    : JsOps.ToNumber(radixArg);
            if (double.IsNaN(radixNumber) || Math.Abs(radixNumber % 1) > double.Epsilon)
            {
                throw ThrowRangeError("Invalid radix", realm: realm);
            }

            var intRadix = (int)radixNumber;
            if (intRadix is < 2 or > 36)
            {
                throw ThrowRangeError("toString() radix argument must be between 2 and 36", realm: realm);
            }

            return BigIntToString(value.Value, intRadix);
        }

        object? BigIntPrototypeValueOf(object? thisValue, IReadOnlyList<object?> _)
        {
            return ThisBigIntValue(thisValue, realm);
        }

        object? BigIntPrototypeToLocaleString(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ThisBigIntValue(thisValue, realm);
            var localesArg = args.GetArgument(0);
            var optionsArg = args.GetArgument(1);
            if (TryFormatWithIntlNumberFormat(value, localesArg, optionsArg, realm, out var formatted))
            {
                return formatted;
            }

            return BigIntToString(value.Value, 10);
        }

        object? BigIntAsIntN(IReadOnlyList<object?> args)
        {
            if (args.Count < 2)
            {
                throw ThrowTypeError("BigInt.asIntN requires bits and value", realm: realm);
            }

            var bits = ToIndex(args[0], realm);
            var value = args[1];
            if (ReferenceEquals(value, Symbol.Undefined))
            {
                throw ThrowTypeError("Cannot convert undefined to a BigInt", realm: realm);
            }

            var bigIntValue = ToBigInt(value, realmState: realm);
            return new JsBigInt(AsIntN(bits, bigIntValue.Value));
        }

        object? BigIntAsUintN(IReadOnlyList<object?> args)
        {
            if (args.Count < 2)
            {
                throw ThrowTypeError("BigInt.asUintN requires bits and value", realm: realm);
            }

            var bits = ToIndex(args[0], realm);
            var value = args[1];
            if (ReferenceEquals(value, Symbol.Undefined))
            {
                throw ThrowTypeError("Cannot convert undefined to a BigInt", realm: realm);
            }

            var bigIntValue = ToBigInt(value, realmState: realm);
            return new JsBigInt(AsUintN(bits, bigIntValue.Value));
        }
    }

    private static int ToIndex(object? value, RealmState? realm = null)
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

    private static BigInteger AsIntN(int bits, BigInteger value)
    {
        if (bits == 0)
        {
            return BigInteger.Zero;
        }

        var modulus = BigInteger.One << bits;
        var unsigned = value % modulus;
        if (unsigned.Sign < 0)
        {
            unsigned += modulus;
        }

        var threshold = modulus >> 1;
        return unsigned >= threshold ? unsigned - modulus : unsigned;
    }

    private static BigInteger AsUintN(int bits, BigInteger value)
    {
        if (bits == 0)
        {
            return BigInteger.Zero;
        }

        var modulus = BigInteger.One << bits;
        var result = value % modulus;
        if (result.Sign < 0)
        {
            result += modulus;
        }

        return result;
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
