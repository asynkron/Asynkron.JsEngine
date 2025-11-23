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
        }

        AddNumberMethods(numberObj, num);
        return numberObj;
    }

    public static JsObject CreateBigIntWrapper(JsBigInt value, EvaluationContext? context = null, RealmState? realm = null)
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
    ///     Adds number methods to a number wrapper object.
    /// </summary>
    private static void AddNumberMethods(JsObject numberObj, double num)
    {
        // toString(radix?)
        numberObj.SetProperty("toString", new HostFunction(args =>
        {
            var radix = args.Count > 0 && args[0] is double d ? (int)d : 10;

            // Validate radix (must be between 2 and 36)
            if (radix is < 2 or > 36)
            {
                throw new ArgumentException("radix must be an integer at least 2 and no greater than 36");
            }

            // Handle special cases
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

            // For radix 10, use standard conversion
            if (radix == 10)
            {
                // Convert to string with proper handling of integers vs floats
                if (Math.Abs(num % 1) < double.Epsilon)
                {
                    return ((long)num).ToString(CultureInfo.InvariantCulture);
                }

                return num.ToString(CultureInfo.InvariantCulture);
            }

            // For other radices, only works on integers
            var intValue = (long)num;
            if (radix == 2)
            {
                return Convert.ToString(intValue, 2);
            }

            if (radix == 8)
            {
                return Convert.ToString(intValue, 8);
            }

            if (radix == 16)
            {
                return Convert.ToString(intValue, 16);
            }

            // For other radices, implement manual conversion
            if (intValue == 0)
            {
                return "0";
            }

            var isNegative = intValue < 0;
            intValue = Math.Abs(intValue);

            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            var result = "";
            while (intValue > 0)
            {
                result = digits[(int)(intValue % radix)] + result;
                intValue /= radix;
            }

            return isNegative ? "-" + result : result;
        }));

        // toFixed(fractionDigits?)
        numberObj.SetProperty("toFixed", new HostFunction(args =>
        {
            var fractionDigits = args.Count > 0 && args[0] is double d ? (int)d : 0;
            if (fractionDigits is < 0 or > 100)
            {
                throw new ArgumentException("toFixed() digits argument must be between 0 and 100");
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
        }));

        // toExponential(fractionDigits?)
        numberObj.SetProperty("toExponential", new HostFunction(args =>
        {
            if (double.IsNaN(num))
            {
                return "NaN";
            }

            if (double.IsInfinity(num))
            {
                return num > 0 ? "Infinity" : "-Infinity";
            }

            if (args.Count <= 0 || args[0] is not double d)
            {
                return num.ToString("e", CultureInfo.InvariantCulture);
            }

            var fractionDigits = (int)d;
            if (fractionDigits is < 0 or > 100)
            {
                throw new ArgumentException("toExponential() digits argument must be between 0 and 100");
            }

            return num.ToString("e" + fractionDigits, CultureInfo.InvariantCulture);

        }));

        // toPrecision(precision?)
        numberObj.SetProperty("toPrecision", new HostFunction(args =>
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
                throw new ArgumentException("toPrecision() precision argument must be between 1 and 100");
            }

            // Format with specified precision
            return num.ToString("G" + precision, CultureInfo.InvariantCulture);

        }));

        // valueOf()
        numberObj.SetProperty("valueOf", new HostFunction(_ => num));
    }

    public static HostFunction CreateBigIntFunction(RealmState realm)
    {
        HostFunction bigIntFunction = null!;
        bigIntFunction = new HostFunction((_, args) =>
        {
            if (args.Count == 0)
            {
                throw ThrowTypeError("Cannot convert undefined to a BigInt");
            }

            return ToBigInt(args[0]);
        }) { IsConstructor = true, DisallowConstruct = true, ConstructErrorMessage = "BigInt is not a constructor" };
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

            var toStringFn = new HostFunction((thisValue, args) =>
            {
                var value = ThisBigIntValue(thisValue);
                var radixArg = args.Count > 0 ? args[0] : Symbols.Undefined;
                var radixNumber = ReferenceEquals(radixArg, Symbols.Undefined)
                    ? 10d
                    : radixArg is JsBigInt biRadix
                        ? (double)biRadix.Value
                        : JsOps.ToNumber(radixArg);
                if (double.IsNaN(radixNumber) || Math.Abs(radixNumber % 1) > double.Epsilon)
                {
                    throw ThrowRangeError("Invalid radix");
                }

                var intRadix = (int)radixNumber;
                if (intRadix is < 2 or > 36)
                {
                    throw ThrowRangeError("toString() radix argument must be between 2 and 36");
                }

                return BigIntToString(value.Value, intRadix);
            }) { IsConstructor = false };
            toStringFn.DefineProperty("length",
                new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });
            toStringFn.DefineProperty("name",
                new PropertyDescriptor
                {
                    Value = "toString", Writable = false, Enumerable = false, Configurable = true
                });
            proto.DefineProperty("toString",
                new PropertyDescriptor
                {
                    Value = toStringFn, Writable = true, Enumerable = false, Configurable = true
                });

            var valueOfFn = new HostFunction((thisValue, _) => ThisBigIntValue(thisValue)) { IsConstructor = false };
            valueOfFn.DefineProperty("length",
                new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });
            valueOfFn.DefineProperty("name",
                new PropertyDescriptor
                {
                    Value = "valueOf", Writable = false, Enumerable = false, Configurable = true
                });
            proto.DefineProperty("valueOf",
                new PropertyDescriptor { Value = valueOfFn, Writable = true, Enumerable = false, Configurable = true });

            var toLocaleStringFn = new HostFunction((thisValue, _) =>
            {
                // Minimal locale-insensitive fallback: ignore locales/options and
                // use base-10 formatting per spec default.
                var value = ThisBigIntValue(thisValue);
                return BigIntToString(value.Value, 10);
            }) { IsConstructor = false };
            toLocaleStringFn.DefineProperty("length",
                new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });
            toLocaleStringFn.DefineProperty("name",
                new PropertyDescriptor
                {
                    Value = "toLocaleString", Writable = false, Enumerable = false, Configurable = true
                });
            proto.DefineProperty("toLocaleString",
                new PropertyDescriptor
                {
                    Value = toLocaleStringFn, Writable = true, Enumerable = false, Configurable = true
                });

            // Spec: built-in functions that are not constructors must not have
            // an own prototype property. Remove the default prototype from these
            // BigInt prototype methods.
            foreach (var fn in new[] { toStringFn, valueOfFn, toLocaleStringFn })
            {
                fn.PropertiesObject.DeleteOwnProperty("prototype");
            }
        }

        bigIntFunction.SetProperty("asIntN", new HostFunction(args =>
        {
            if (args.Count < 2)
            {
                throw ThrowTypeError("BigInt.asIntN requires bits and value");
            }

            var bits = ToIndex(args[0]);
            var value = ToBigInt(args[1]);
            return new JsBigInt(AsIntN(bits, value.Value));
        }));

        bigIntFunction.SetProperty("asUintN", new HostFunction(args =>
        {
            if (args.Count < 2)
            {
                throw ThrowTypeError("BigInt.asUintN requires bits and value");
            }

            var bits = ToIndex(args[0]);
            var value = ToBigInt(args[1]);
            return new JsBigInt(AsUintN(bits, value.Value));
        }));

        bigIntFunction.SetProperty("name", "BigInt");
        bigIntFunction.SetProperty("length", 1d);

        return bigIntFunction;
    }

    private static int ToIndex(object? value)
    {
        if (value is JsBigInt bigInt)
        {
            if (bigInt.Value < 0 || bigInt.Value > int.MaxValue)
            {
                throw ThrowRangeError("Index must be a non-negative integer");
            }

            return (int)bigInt.Value;
        }

        var number = JsOps.ToNumber(value);
        if (double.IsNaN(number) || double.IsInfinity(number) || number < 0 || Math.Abs(number % 1) > double.Epsilon)
        {
            throw ThrowRangeError("Index must be a non-negative integer");
        }

        if (number > int.MaxValue)
        {
            throw ThrowRangeError("Index is too large");
        }

        return (int)number;
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
        // Number constructor
        var numberConstructor = new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                if (thisValue is not JsObject objZero)
                {
                    return 0d;
                }

                objZero.SetProperty("__value__", 0d);
                return objZero;

            }

            var value = args[0];
            var result = JsOps.ToNumber(value);

            if (thisValue is not JsObject obj)
            {
                return result;
            }

            obj.SetProperty("__value__", result);
            return obj;

        });

        // Remember Number.prototype so that number wrapper objects can see
        // methods attached from user code (e.g. Number.prototype.toJSONString).
        if (numberConstructor.TryGetProperty("prototype", out var numberProto) &&
            numberProto is JsObject numberProtoObj)
        {
            realm.NumberPrototype ??= numberProtoObj;
            if (realm.ObjectPrototype is not null && numberProtoObj.Prototype is null)
            {
                numberProtoObj.SetPrototype(realm.ObjectPrototype);
            }

            numberProtoObj.SetProperty("toString", new HostFunction((thisValue, _) =>
            {
                var num = JsOps.ToNumber(thisValue);
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

                return num.ToString(CultureInfo.InvariantCulture);
            }));
        }

        // Number.isInteger(value)
        numberConstructor.SetProperty("isInteger", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return false;
            }

            if (args[0] is not double d)
            {
                return false;
            }

            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                return false;
            }

            return Math.Abs(d % 1) < double.Epsilon;
        }));

        // Number.isFinite(value)
        numberConstructor.SetProperty("isFinite", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return false;
            }

            if (args[0] is not double d)
            {
                return false;
            }

            return !double.IsNaN(d) && !double.IsInfinity(d);
        }));

        // Number.isNaN(value)
        numberConstructor.SetProperty("isNaN", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return false;
            }

            return args[0] is double.NaN;
        }));

        // Number.isSafeInteger(value)
        numberConstructor.SetProperty("isSafeInteger", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return false;
            }

            if (args[0] is not double d)
            {
                return false;
            }

            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                return false;
            }

            if (Math.Abs(d % 1) >= double.Epsilon)
            {
                return false; // Not an integer
            }

            return Math.Abs(d) <= 9007199254740991; // MAX_SAFE_INTEGER
        }));

        // Number.parseFloat(string)
        numberConstructor.SetProperty("parseFloat", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            var str = args[0]?.ToString() ?? "";
            str = str.Trim();
            if (str.Length == 0)
            {
                return double.NaN;
            }

            // Try to parse, taking as much as possible from the start
            var match = FloatRegex().Match(str);
            if (match.Success)
            {
                if (double.TryParse(match.Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var result))
                {
                    return result;
                }
            }

            if (str.StartsWith("Infinity"))
            {
                return double.PositiveInfinity;
            }

            if (str.StartsWith("+Infinity"))
            {
                return double.PositiveInfinity;
            }

            if (str.StartsWith("-Infinity"))
            {
                return double.NegativeInfinity;
            }

            return double.NaN;
        }));

        // Number.parseInt(string, radix)
        numberConstructor.SetProperty("parseInt", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            var str = args[0]?.ToString() ?? "";
            str = str.Trim();
            if (str.Length == 0)
            {
                return double.NaN;
            }

            var radix = args.Count > 1 && args[1] is double r ? (int)r : 10;
            if (radix is < 2 or > 36)
            {
                return double.NaN;
            }

            // Handle sign
            var sign = 1;
            if (str.StartsWith("-"))
            {
                sign = -1;
                str = str[1..].TrimStart();
            }
            else if (str.StartsWith("+"))
            {
                str = str[1..].TrimStart();
            }

            // Parse until we hit invalid character
            double result = 0;
            var hasDigits = false;
            foreach (var c in str)
            {
                int digit;
                if (char.IsDigit(c))
                {
                    digit = c - '0';
                }
                else if (char.IsLetter(c))
                {
                    var upper = char.ToUpperInvariant(c);
                    digit = upper - 'A' + 10;
                }
                else
                {
                    break; // Stop at first invalid character
                }

                if (digit >= radix)
                {
                    break;
                }

                result = result * radix + digit;
                hasDigits = true;
            }

            return hasDigits ? result * sign : double.NaN;
        }));

        // Number.EPSILON
        numberConstructor.SetProperty("EPSILON", double.Epsilon);

        // Number.MAX_SAFE_INTEGER
        numberConstructor.SetProperty("MAX_SAFE_INTEGER", 9007199254740991d);

        // Number.MIN_SAFE_INTEGER
        numberConstructor.SetProperty("MIN_SAFE_INTEGER", -9007199254740991d);

        // Number.MAX_VALUE
        numberConstructor.SetProperty("MAX_VALUE", double.MaxValue);

        // Number.MIN_VALUE
        numberConstructor.SetProperty("MIN_VALUE", double.MinValue);

        // Number.POSITIVE_INFINITY
        numberConstructor.SetProperty("POSITIVE_INFINITY", double.PositiveInfinity);

        // Number.NEGATIVE_INFINITY
        numberConstructor.SetProperty("NEGATIVE_INFINITY", double.NegativeInfinity);

        // Number.NaN
        numberConstructor.SetProperty("NaN", double.NaN);

        return numberConstructor;
    }

    [GeneratedRegex(@"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?")]
    private static partial Regex FloatRegex();
}
