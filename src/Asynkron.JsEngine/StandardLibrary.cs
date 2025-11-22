using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine;

/// <summary>
/// Provides standard JavaScript library objects and functions (Math, JSON, etc.)
/// </summary>
public static class StandardLibrary
{
    private static readonly AsyncLocal<RealmState?> CurrentRealmState = new();

    private static JsObject? _fallbackBooleanPrototype;
    private static JsObject? _fallbackNumberPrototype;
    private static JsObject? _fallbackStringPrototype;
    private static JsObject? _fallbackObjectPrototype;
    private static JsObject? _fallbackFunctionPrototype;
    private static JsObject? _fallbackArrayPrototype;
    private static JsObject? _fallbackBigIntPrototype;
    private static JsObject? _fallbackDatePrototype;
    private static JsObject? _fallbackErrorPrototype;
    private static JsObject? _fallbackTypeErrorPrototype;
    private static JsObject? _fallbackSyntaxErrorPrototype;
    private static HostFunction? _fallbackTypeErrorConstructor;
    private static HostFunction? _fallbackRangeErrorConstructor;
    private static HostFunction? _fallbackSyntaxErrorConstructor;
    private static HostFunction? _fallbackArrayConstructor;

    internal static RealmState? CurrentRealm
    {
        get => CurrentRealmState.Value;
        private set => CurrentRealmState.Value = value;
    }

    internal static void BindRealm(RealmState realm)
    {
        CurrentRealm = realm;
    }

    // Shared prototypes for primitive wrapper objects so that host-provided
    // globals like Boolean.prototype can be extended from JavaScript and still
    // be visible via auto-boxing of primitives.
    internal static JsObject? BooleanPrototype
    {
        get => CurrentRealm?.BooleanPrototype ?? _fallbackBooleanPrototype;
        set
        {
            _fallbackBooleanPrototype = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.BooleanPrototype = value;
            }
        }
    }

    internal static JsObject? NumberPrototype
    {
        get => CurrentRealm?.NumberPrototype ?? _fallbackNumberPrototype;
        set
        {
            _fallbackNumberPrototype = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.NumberPrototype = value;
            }
        }
    }

    internal static JsObject? StringPrototype
    {
        get => CurrentRealm?.StringPrototype ?? _fallbackStringPrototype;
        set
        {
            _fallbackStringPrototype = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.StringPrototype = value;
            }
        }
    }

    internal static JsObject? ObjectPrototype
    {
        get => CurrentRealm?.ObjectPrototype ?? _fallbackObjectPrototype;
        set
        {
            _fallbackObjectPrototype = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.ObjectPrototype = value;
            }
        }
    }

    internal static JsObject? FunctionPrototype
    {
        get => CurrentRealm?.FunctionPrototype ?? _fallbackFunctionPrototype;
        set
        {
            _fallbackFunctionPrototype = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.FunctionPrototype = value;
            }
        }
    }

    internal static JsObject? ArrayPrototype
    {
        get => CurrentRealm?.ArrayPrototype ?? _fallbackArrayPrototype;
        set
        {
            _fallbackArrayPrototype = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.ArrayPrototype = value;
            }
        }
    }

    internal static JsObject? BigIntPrototype
    {
        get => CurrentRealm?.BigIntPrototype ?? _fallbackBigIntPrototype;
        set
        {
            _fallbackBigIntPrototype = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.BigIntPrototype = value;
            }
        }
    }

    internal static JsObject? DatePrototype
    {
        get => CurrentRealm?.DatePrototype ?? _fallbackDatePrototype;
        set
        {
            _fallbackDatePrototype = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.DatePrototype = value;
            }
        }
    }

    internal static JsObject? ErrorPrototype
    {
        get => CurrentRealm?.ErrorPrototype ?? _fallbackErrorPrototype;
        set
        {
            _fallbackErrorPrototype = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.ErrorPrototype = value;
            }
        }
    }

    internal static JsObject? TypeErrorPrototype
    {
        get => CurrentRealm?.TypeErrorPrototype ?? _fallbackTypeErrorPrototype;
        set
        {
            _fallbackTypeErrorPrototype = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.TypeErrorPrototype = value;
            }
        }
    }

    internal static JsObject? SyntaxErrorPrototype
    {
        get => CurrentRealm?.SyntaxErrorPrototype ?? _fallbackSyntaxErrorPrototype;
        set
        {
            _fallbackSyntaxErrorPrototype = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.SyntaxErrorPrototype = value;
            }
        }
    }

    internal static HostFunction? TypeErrorConstructor
    {
        get => CurrentRealm?.TypeErrorConstructor ?? _fallbackTypeErrorConstructor;
        set
        {
            _fallbackTypeErrorConstructor = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.TypeErrorConstructor = value;
            }
        }
    }

    internal static HostFunction? RangeErrorConstructor
    {
        get => CurrentRealm?.RangeErrorConstructor ?? _fallbackRangeErrorConstructor;
        set
        {
            _fallbackRangeErrorConstructor = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.RangeErrorConstructor = value;
            }
        }
    }

    internal static HostFunction? SyntaxErrorConstructor
    {
        get => CurrentRealm?.SyntaxErrorConstructor ?? _fallbackSyntaxErrorConstructor;
        set
        {
            _fallbackSyntaxErrorConstructor = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.SyntaxErrorConstructor = value;
            }
        }
    }

    internal static HostFunction? ArrayConstructor
    {
        get => CurrentRealm?.ArrayConstructor ?? _fallbackArrayConstructor;
        set
        {
            _fallbackArrayConstructor = value;
            if (CurrentRealm is not null)
            {
                CurrentRealm.ArrayConstructor = value;
            }
        }
    }

    internal static object CreateTypeError(string message)
    {
        if (TypeErrorConstructor is IJsCallable ctor)
        {
            return ctor.Invoke([message], null);
        }

        return new InvalidOperationException(message);
    }

    // ECMAScript Type(x) == "bigint" when x is JsBigInt.
    private static string TypeOf(object? value)
    {
        if (value is null)
        {
            return "object";
        }

        if (value is Symbol sym && ReferenceEquals(sym, Symbols.Undefined))
        {
            return "undefined";
        }

        if (value is TypedAstSymbol)
        {
            return "symbol";
        }

        if (value is JsBigInt)
        {
            return "bigint";
        }

        return value switch
        {
            bool => "boolean",
            double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte => "number",
            string => "string",
            IJsCallable => "function",
            _ => "object"
        };
    }

    internal static object CreateRangeError(string message)
    {
        if (RangeErrorConstructor is IJsCallable ctor)
        {
            return ctor.Invoke([message], null);
        }

        return new InvalidOperationException(message);
    }

    internal static ThrowSignal ThrowTypeError(string message)
    {
        return new ThrowSignal(CreateTypeError(message));
    }

    internal static ThrowSignal ThrowRangeError(string message)
    {
        return new ThrowSignal(CreateRangeError(message));
    }

    internal static ThrowSignal ThrowSyntaxError(string message)
    {
        return new ThrowSignal(CreateSyntaxError(message));
    }

    private static JsBigInt ThisBigIntValue(object? receiver)
    {
        if (receiver is JsBigInt bi)
        {
            return bi;
        }

        if (receiver is JsObject obj &&
            obj.TryGetValue("__value__", out var inner) &&
            inner is JsBigInt wrapped)
        {
            return wrapped;
        }

        throw ThrowTypeError("BigInt.prototype method called on incompatible receiver");
    }

    internal static object CreateSyntaxError(string message)
    {
        if (SyntaxErrorConstructor is IJsCallable ctor)
        {
            return ctor.Invoke([message], null);
        }

        return new InvalidOperationException(message);
    }

    internal static JsBigInt ToBigInt(object? value)
    {
        while (true)
        {
            if (value is JsObject jsObj && jsObj.TryGetValue("__value__", out var inner))
            {
                if (ReferenceEquals(inner, value))
                {
                    throw ThrowTypeError("Cannot convert object to a BigInt");
                }

                value = inner;
                continue;
            }

            switch (value)
            {
                case JsBigInt bigInt:
                    return bigInt;
                case JsObject or IJsPropertyAccessor:
                    value = JsOps.ToPrimitive(value, "number");
                    continue;
                case null:
                case Symbol sym when ReferenceEquals(sym, Symbols.Undefined):
                    throw ThrowTypeError("Cannot convert undefined to a BigInt");
                case bool b:
                    return b ? JsBigInt.One : JsBigInt.Zero;
                case string s:
                    return new JsBigInt(ParseBigIntString(s));
                case double d when double.IsNaN(d) || double.IsInfinity(d) || d % 1 != 0:
                    throw ThrowRangeError("Cannot convert number to a BigInt");
                case double d:
                    return new JsBigInt(new BigInteger(d));
                case float f when float.IsNaN(f) || float.IsInfinity(f) || f % 1 != 0:
                    throw ThrowRangeError("Cannot convert number to a BigInt");
                case float f:
                    return new JsBigInt(new BigInteger(f));
                case decimal m when decimal.Truncate(m) != m:
                    throw ThrowRangeError("Cannot convert number to a BigInt");
                case decimal m:
                    return new JsBigInt(new BigInteger(m));
                case int i:
                    return new JsBigInt(i);
                case uint ui:
                    return new JsBigInt(new BigInteger(ui));
                case long l:
                    return new JsBigInt(new BigInteger(l));
                case ulong ul:
                    return new JsBigInt(new BigInteger(ul));
            }

            throw ThrowTypeError($"Cannot convert {value?.GetType().Name ?? "null"} to a BigInt");
        }
    }

    private static BigInteger ParseBigIntString(string value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return BigInteger.Zero;
        }

        if (text.EndsWith("n", StringComparison.Ordinal))
        {
            throw ThrowSyntaxError("Invalid BigInt literal");
        }

        var sign = 1;
        if (text.StartsWith("+", StringComparison.Ordinal) || text.StartsWith("-", StringComparison.Ordinal))
        {
            if (text[0] == '-')
            {
                sign = -1;
            }

            text = text[1..];
        }

        if (text.Length == 0)
        {
            return BigInteger.Zero;
        }

        var numberBase = 10;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            numberBase = 16;
            text = text[2..];
        }
        else if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            numberBase = 2;
            text = text[2..];
        }
        else if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
        {
            numberBase = 8;
            text = text[2..];
        }
        else if (text.StartsWith("0") && text.Length > 1 && char.IsDigit(text[1]))
        {
            throw ThrowSyntaxError("Invalid BigInt literal");
        }

        // A sign is only permitted with decimal strings.
        if (sign < 0 && numberBase != 10)
        {
            throw ThrowSyntaxError("Invalid BigInt literal");
        }

        // For decimal strings, reject any non-digit content.
        if (numberBase == 10)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] is < '0' or > '9')
                {
                    throw ThrowSyntaxError("Invalid BigInt literal");
                }
            }
        }

        if (text.Length == 0)
        {
            throw ThrowSyntaxError("Invalid BigInt literal");
        }

        if (!TryParseBigIntWithBase(text, numberBase, sign, out var parsed))
        {
            throw ThrowSyntaxError("Invalid BigInt literal");
        }

        return parsed;
    }

    private static bool TryParseBigIntWithBase(string digits, int numberBase, int sign, out BigInteger result)
    {
        result = BigInteger.Zero;
        foreach (var ch in digits)
        {
            var digit = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'a' and <= 'z' => ch - 'a' + 10,
                >= 'A' and <= 'Z' => ch - 'A' + 10,
                _ => -1
            };

            if (digit < 0 || digit >= numberBase)
            {
                return false;
            }

            result = result * numberBase + digit;
        }

        if (sign < 0)
        {
            result = BigInteger.Negate(result);
        }

        return true;
    }

    private static string BigIntToString(BigInteger value, int radix)
    {
        if (radix == 10)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var builder = new StringBuilder();
        var isNegative = value.Sign < 0;
        var remainder = BigInteger.Abs(value);
        if (remainder.IsZero)
        {
            return "0";
        }

        while (!remainder.IsZero)
        {
            remainder = BigInteger.DivRem(remainder, radix, out var rem);
            builder.Insert(0, digits[(int)rem]);
        }

        return isNegative ? "-" + builder : builder.ToString();
    }

    /// <summary>
    /// Converts a JavaScript value to its string representation, handling functions appropriately.
    /// </summary>
    private static string JsValueToString(object? value)
    {
        return value switch
        {
            null => "null",
            string s => s,
            bool b => b ? "true" : "false",
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IJsCallable => "function() { [native code] }",
            _ => value.ToString() ?? ""
        };
    }

    /// <summary>
    /// Creates a Math object with common mathematical functions and constants.
    /// </summary>
    public static JsObject CreateMathObject()
    {
        var math = new JsObject();

        // Constants
        math["E"] = Math.E;
        math["PI"] = Math.PI;
        math["LN2"] = Math.Log(2);
        math["LN10"] = Math.Log(10);
        math["LOG2E"] = Math.Log2(Math.E);
        math["LOG10E"] = Math.Log10(Math.E);
        math["SQRT1_2"] = Math.Sqrt(0.5);
        math["SQRT2"] = Math.Sqrt(2);

        // Methods
        math["abs"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] switch
            {
                double d => Math.Abs(d),
                int i => Math.Abs(i),
                _ => double.NaN
            };
        });

        math["ceil"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Ceiling(d) : double.NaN;
        });

        math["floor"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Floor(d) : double.NaN;
        });

        math["round"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            if (args[0] is not double d)
            {
                return double.NaN;
            }

            // JavaScript Math.round uses "round half away from zero"
            // while .NET Math.Round uses "round half to even" by default
            if (d >= 0)
            {
                return Math.Floor(d + 0.5);
            }

            return Math.Ceiling(d - 0.5);
        });

        math["sqrt"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Sqrt(d) : double.NaN;
        });

        math["pow"] = new HostFunction(args =>
        {
            if (args.Count < 2)
            {
                return double.NaN;
            }

            var baseValue = args[0] as double? ?? double.NaN;
            var exponent = args[1] as double? ?? double.NaN;
            return Math.Pow(baseValue, exponent);
        });

        math["max"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NegativeInfinity;
            }

            var max = double.NegativeInfinity;
            foreach (var arg in args)
            {
                if (arg is double d)
                {
                    if (double.IsNaN(d))
                    {
                        return double.NaN;
                    }

                    if (d > max)
                    {
                        max = d;
                    }
                }
            }

            return max;
        });

        math["min"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.PositiveInfinity;
            }

            var min = double.PositiveInfinity;
            foreach (var arg in args)
            {
                if (arg is double d)
                {
                    if (double.IsNaN(d))
                    {
                        return double.NaN;
                    }

                    if (d < min)
                    {
                        min = d;
                    }
                }
            }

            return min;
        });

        math["random"] = new HostFunction(args => { return Random.Shared.NextDouble(); });

        math["sin"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Sin(d) : double.NaN;
        });

        math["cos"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Cos(d) : double.NaN;
        });

        math["tan"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Tan(d) : double.NaN;
        });

        math["asin"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Asin(d) : double.NaN;
        });

        math["acos"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Acos(d) : double.NaN;
        });

        math["atan"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Atan(d) : double.NaN;
        });

        math["atan2"] = new HostFunction(args =>
        {
            if (args.Count < 2)
            {
                return double.NaN;
            }

            var y = args[0] as double? ?? double.NaN;
            var x = args[1] as double? ?? double.NaN;
            return Math.Atan2(y, x);
        });

        math["exp"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Exp(d) : double.NaN;
        });

        math["log"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Log(d) : double.NaN;
        });

        math["log10"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Log10(d) : double.NaN;
        });

        math["log2"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Log2(d) : double.NaN;
        });

        math["trunc"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Truncate(d) : double.NaN;
        });

        math["sign"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            if (args[0] is not double d)
            {
                return double.NaN;
            }

            if (double.IsNaN(d))
            {
                return double.NaN;
            }

            return Math.Sign(d);
        });

        // ES6+ Math methods
        math["cbrt"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Cbrt(d) : double.NaN;
        });

        math["clz32"] = new HostFunction(args =>
        {
            var number = args.Count > 0 ? JsOps.ToNumber(args[0]) : 0d;
            var value = JsNumericConversions.ToUInt32(number);
            if (value == 0)
            {
                return 32d;
            }

            return (double)System.Numerics.BitOperations.LeadingZeroCount(value);
        });

        math["imul"] = new HostFunction(args =>
        {
            var left = args.Count > 0 ? JsOps.ToNumber(args[0]) : 0d;
            var right = args.Count > 1 ? JsOps.ToNumber(args[1]) : 0d;
            var a = JsNumericConversions.ToInt32(left);
            var b = JsNumericConversions.ToInt32(right);
            return (double)(a * b);
        });

        math["fround"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            if (args[0] is not double d)
            {
                return double.NaN;
            }

            return (double)(float)d;
        });

        math["hypot"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return 0d;
            }

            double sumOfSquares = 0;
            foreach (var arg in args)
            {
                if (arg is double d)
                {
                    if (double.IsNaN(d))
                    {
                        return double.NaN;
                    }

                    if (double.IsInfinity(d))
                    {
                        return double.PositiveInfinity;
                    }

                    sumOfSquares += d * d;
                }
            }

            return Math.Sqrt(sumOfSquares);
        });

        math["acosh"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Acosh(d) : double.NaN;
        });

        math["asinh"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Asinh(d) : double.NaN;
        });

        math["atanh"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Atanh(d) : double.NaN;
        });

        math["cosh"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Cosh(d) : double.NaN;
        });

        math["sinh"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Sinh(d) : double.NaN;
        });

        math["tanh"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            return args[0] is double d ? Math.Tanh(d) : double.NaN;
        });

        math["expm1"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            if (args[0] is not double d)
            {
                return double.NaN;
            }

            // e^x - 1 with better precision for small x
            return Math.Exp(d) - 1;
        });

        math["log1p"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            if (args[0] is not double d)
            {
                return double.NaN;
            }

            // log(1 + x) with better precision for small x
            return Math.Log(1 + d);
        });

        return math;
    }

    /// <summary>
    /// Creates a minimal Function constructor with a callable `Function`
    /// value and a `Function.call` helper that can be used with patterns
    /// like <c>Function.call.bind(Object.prototype.hasOwnProperty)</c>.
    /// </summary>
    public static IJsCallable CreateFunctionConstructor(Runtime.RealmState realm)
    {
        // Minimal Function constructor: for now we ignore the body and
        // arguments and just return a no-op function value.
        HostFunction functionConstructor = null!;

        functionConstructor = new HostFunction((thisValue, args) =>
        {
            var realm = functionConstructor.Realm ?? thisValue as JsObject;
            return new HostFunction((innerThis, innerArgs) => Symbols.Undefined)
            {
                Realm = realm,
                RealmState = functionConstructor.RealmState ?? CurrentRealm
            };
        });
        functionConstructor.RealmState = realm;

        // Function.call: when used as `fn.call(thisArg, ...args)` the
        // target function is `fn` (the `this` value). We implement this
        // directly so that binding `Function.call` or
        // `Function.prototype.call` produces helpers that behave like
        // `Function.prototype.call`.
        var callHelper = new HostFunction((thisValue, args) =>
        {
            if (thisValue is not IJsCallable target)
            {
                return Symbols.Undefined;
            }

            object? thisArg = Symbols.Undefined;
            var callArgs = Array.Empty<object?>();

            if (args.Count > 0)
            {
                thisArg = args[0];
                if (args.Count > 1)
                {
                    callArgs = args.Skip(1).ToArray();
                }
            }

            return target.Invoke(callArgs, thisArg);
        });
        callHelper.Realm = functionConstructor.Realm;
        callHelper.RealmState = functionConstructor.RealmState ?? CurrentRealm;

        functionConstructor.SetProperty("call", callHelper);

        // Provide a minimal `Function.prototype` object that exposes the
        // same call helper so patterns like
        // `Function.prototype.call.bind(Object.prototype.hasOwnProperty)`
        // work as expected.
        var functionPrototype = new JsObject();
        functionPrototype.SetProperty("call", callHelper);
        if (realm.ObjectPrototype is not null)
        {
            functionPrototype.SetPrototype(realm.ObjectPrototype);
        }
        var hasInstanceKey = $"@@symbol:{TypedAstSymbol.For("Symbol.hasInstance").GetHashCode()}";
        functionPrototype.SetProperty(hasInstanceKey, new HostFunction((thisValue, args) =>
        {
            if (thisValue is not IJsCallable)
            {
                throw new InvalidOperationException("Function.prototype[@@hasInstance] called on non-callable value.");
            }

            var candidate = args.Count > 0 ? args[0] : Symbols.Undefined;
            if (candidate is not JsObject obj)
            {
                return false;
            }

            JsObject? targetPrototype = null;
            if (thisValue is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("prototype", out var protoVal) &&
                protoVal is JsObject protoObj)
            {
                targetPrototype = protoObj;
            }

            if (targetPrototype is null)
            {
                return false;
            }

            var cursor = obj;
            while (cursor is not null)
            {
                if (ReferenceEquals(cursor, targetPrototype))
                {
                    return true;
                }

                cursor = cursor.Prototype;
            }

            return false;
        }));
        realm.FunctionPrototype ??= functionPrototype;
        functionConstructor.SetProperty("prototype", functionPrototype);
        functionConstructor.Properties.SetPrototype(functionPrototype);

        return functionConstructor;
    }

    /// <summary>
    /// Creates a minimal window.localStorage-like object used by libraries
    /// such as debug/babel-standalone. This implementation keeps values in
    /// an in-memory dictionary and exposes the standard getItem/setItem API.
    /// </summary>
    public static JsObject CreateLocalStorageObject()
    {
        var storage = new JsObject();
        var backing = new Dictionary<string, string?>(StringComparer.Ordinal);

        storage.SetProperty("getItem", new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            var key = args[0]?.ToString() ?? string.Empty;
            return backing.TryGetValue(key, out var value) ? value : null;
        }));

        storage.SetProperty("setItem", new HostFunction((thisValue, args) =>
        {
            if (args.Count < 2)
            {
                return null;
            }

            var key = args[0]?.ToString() ?? string.Empty;
            var value = args[1]?.ToString() ?? string.Empty;
            backing[key] = value;
            return null;
        }));

        storage.SetProperty("removeItem", new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            var key = args[0]?.ToString() ?? string.Empty;
            backing.Remove(key);
            return null;
        }));

        storage.SetProperty("clear", new HostFunction((thisValue, args) =>
        {
            backing.Clear();
            return null;
        }));

        return storage;
    }

    /// <summary>
    /// Creates a Console object with common logging methods.
    /// </summary>
    public static JsObject CreateConsoleObject()
    {
        var console = new JsObject();

        // Helper function to format arguments for logging
        static string FormatArgs(IReadOnlyList<object?> args)
        {
            var parts = new List<string>();
            foreach (var arg in args)
            {
                if (arg == null)
                {
                    parts.Add("null");
                }
                else if (ReferenceEquals(arg, Symbols.Undefined))
                {
                    parts.Add("undefined");
                }
                else if (arg is string s)
                {
                    parts.Add(s);
                }
                else if (arg is JsObject obj)
                {
                    // Simple object representation
                    try
                    {
                        parts.Add(StringifyValue(obj, 0));
                    }
                    catch
                    {
                        parts.Add("[object Object]");
                    }
                }
                else if (arg is JsArray arr)
                {
                    try
                    {
                        parts.Add(StringifyValue(arr, 0));
                    }
                    catch
                    {
                        parts.Add("[Array]");
                    }
                }
                else if (arg is IJsCallable)
                {
                    parts.Add("[Function]");
                }
                else
                {
                    parts.Add(JsValueToString(arg));
                }
            }
            return string.Join(" ", parts);
        }

        // console.log(...args)
        console["log"] = new HostFunction(args =>
        {
            Console.WriteLine(FormatArgs(args));
            return Symbols.Undefined;
        });

        // console.error(...args)
        console["error"] = new HostFunction(args =>
        {
            Console.Error.WriteLine(FormatArgs(args));
            return Symbols.Undefined;
        });

        // console.warn(...args)
        console["warn"] = new HostFunction(args =>
        {
            Console.WriteLine($"Warning: {FormatArgs(args)}");
            return Symbols.Undefined;
        });

        // console.info(...args)
        console["info"] = new HostFunction(args =>
        {
            Console.WriteLine(FormatArgs(args));
            return Symbols.Undefined;
        });

        // console.debug(...args)
        console["debug"] = new HostFunction(args =>
        {
            Console.WriteLine($"Debug: {FormatArgs(args)}");
            return Symbols.Undefined;
        });

        return console;
    }

    /// <summary>
    /// Creates a Date object with JavaScript-like date handling.
    /// </summary>
    public static JsObject CreateDateObject()
    {
        var date = new JsObject();

        // Date.now() - returns milliseconds since epoch
        date["now"] = new HostFunction(args => { return (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); });

        // Date.UTC(...) - returns time value (ms since epoch) for the given UTC date/time components.
        date["UTC"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            double ToNumberOrNaN(object? v)
            {
                return v is double d ? d : double.NaN;
            }

            var y = ToNumberOrNaN(args[0]);
            var m = args.Count > 1 ? ToNumberOrNaN(args[1]) : 0;
            var dt = args.Count > 2 ? ToNumberOrNaN(args[2]) : 1;
            var h = args.Count > 3 ? ToNumberOrNaN(args[3]) : 0;
            var min = args.Count > 4 ? ToNumberOrNaN(args[4]) : 0;
            var s = args.Count > 5 ? ToNumberOrNaN(args[5]) : 0;
            var ms = args.Count > 6 ? ToNumberOrNaN(args[6]) : 0;

            if (double.IsNaN(y) || double.IsNaN(m) || double.IsNaN(dt) ||
                double.IsNaN(h) || double.IsNaN(min) || double.IsNaN(s) || double.IsNaN(ms))
            {
                return double.NaN;
            }

            // ECMAScript: years 0–99 are interpreted as 1900–1999.
            var year = (int)y;
            if (0 <= year && year <= 99)
            {
                year += 1900;
            }

            var month = (int)m + 1; // JS months are 0-based
            var day = (int)dt;
            var hour = (int)h;
            var minute = (int)min;
            var second = (int)s;
            var millisecond = (int)ms;

            try
            {
                var utcDate = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);
                var dto = new DateTimeOffset(utcDate);
                return (double)dto.ToUnixTimeMilliseconds();
            }
            catch
            {
                return double.NaN;
            }
        });

        // Date.parse() - parses a date string
        date["parse"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not string dateStr)
            {
                return double.NaN;
            }

            if (DateTimeOffset.TryParse(dateStr, out var parsed))
            {
                return (double)parsed.ToUnixTimeMilliseconds();
            }

            return double.NaN;
        });

        return date;
    }

    /// <summary>
    /// Creates a Date instance constructor.
    /// </summary>
    public static HostFunction CreateDateConstructor(Runtime.RealmState realm)
    {
        HostFunction? dateConstructor = null;
        JsObject? datePrototype = null;

        static DateTimeOffset ConvertMillisecondsToUtc(double milliseconds)
        {
            // JavaScript stores Date values as milliseconds since Unix epoch in UTC.
            // The input can be fractional, but DateTimeOffset only accepts long, so
            // truncate toward zero like ECMAScript's ToIntegerOrInfinity.
            var truncated = (long)Math.Truncate(milliseconds);
            return DateTimeOffset.FromUnixTimeMilliseconds(truncated);
        }

        static DateTimeOffset GetLocalTimeFromInternalDate(JsObject obj)
        {
            var utc = GetUtcTimeFromInternalDate(obj);
            return utc.ToLocalTime();
        }

        static DateTimeOffset GetUtcTimeFromInternalDate(JsObject obj)
        {
            if (obj.TryGetProperty("_internalDate", out var stored) && stored is double storedMs)
            {
                return ConvertMillisecondsToUtc(storedMs);
            }

            return ConvertMillisecondsToUtc(0);
        }

        static void StoreInternalDate(JsObject obj, DateTimeOffset dateTime)
        {
            obj.SetProperty("_internalDate", (double)dateTime.ToUnixTimeMilliseconds());
        }

        static string FormatDateToJsString(DateTimeOffset localTime)
        {
            // Match the typical "Wed Jan 02 2008 00:00:00 GMT+0100 (Central European Standard Time)" output.
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var weekday = localTime.ToString("ddd", culture);
            var month = localTime.ToString("MMM", culture);
            var day = localTime.ToString("dd", culture);
            var time = localTime.ToString("HH:mm:ss", culture);
            var year = localTime.ToString("yyyy", culture);

            // ECMAScript requires the GMT offset in the form GMT+HHMM.
            var offset = localTime.ToString("zzz", culture).Replace(":", string.Empty);

            var timeZone = TimeZoneInfo.Local.IsDaylightSavingTime(localTime.DateTime)
                ? TimeZoneInfo.Local.DaylightName
                : TimeZoneInfo.Local.StandardName;

            return $"{weekday} {month} {day} {year} {time} GMT{offset} ({timeZone})";
        }

        static string FormatUtcToJsUtcString(DateTimeOffset utcTime)
        {
            // Match Node/ECMAScript style: "Thu, 01 Jan 1970 00:00:00 GMT"
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return utcTime.UtcDateTime.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", culture);
        }

        dateConstructor = new HostFunction((thisValue, args) =>
        {
            // For `new Date(...)`, the typed evaluator creates the instance
            // object and passes it as `thisValue`. Reuse that object so it
            // keeps the correct prototype chain (Date.prototype).
            var dateInstance = thisValue as JsObject ?? new JsObject();

            if (dateInstance.Prototype is null && datePrototype is not null)
            {
                dateInstance.SetPrototype(datePrototype);
            }

            DateTimeOffset dateTime;

            if (args.Count == 0)
            {
                // No arguments: current date/time
                dateTime = DateTimeOffset.UtcNow;
            }
            else if (args.Count == 1)
            {
                // Single argument: milliseconds since epoch or date string
                if (args[0] is double ms)
                {
                    dateTime = ConvertMillisecondsToUtc(ms);
                }
                else if (args[0] is string dateStr && DateTimeOffset.TryParse(dateStr, out var parsed))
                {
                    dateTime = parsed;
                }
                else
                {
                    dateTime = DateTimeOffset.UtcNow;
                }
            }
            else
            {
                // Multiple arguments: year, month, day, hour, minute, second, millisecond
                var year = args[0] is double y ? (int)y : 1970;
                var month = args.Count > 1 && args[1] is double m ? (int)m + 1 : 1; // JS months are 0-indexed
                var day = args.Count > 2 && args[2] is double d ? (int)d : 1;
                var hour = args.Count > 3 && args[3] is double h ? (int)h : 0;
                var minute = args.Count > 4 && args[4] is double min ? (int)min : 0;
                var second = args.Count > 5 && args[5] is double s ? (int)s : 0;
                var millisecond = args.Count > 6 && args[6] is double ms ? (int)ms : 0;

                try
                {
                    var localDate = new DateTime(year, month, day, hour, minute, second, millisecond,
                        DateTimeKind.Utc);
                    dateTime = new DateTimeOffset(localDate);
                }
                catch
                {
                    dateTime = DateTimeOffset.UtcNow;
                }
            }

            // Store the internal date value
            StoreInternalDate(dateInstance, dateTime);

            // Add instance methods
            dateInstance["getTime"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) &&
                    val is double ms)
                {
                    return ms;
                }

                return double.NaN;
            });

            dateInstance["setTime"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && methodArgs.Count > 0 && methodArgs[0] is double ms)
                {
                    var utc = ConvertMillisecondsToUtc(ms);
                    StoreInternalDate(obj, utc);
                    return (double)utc.ToUnixTimeMilliseconds();
                }
                return double.NaN;
            });

            dateInstance["getFullYear"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.Year;
                }

                return double.NaN;
            });

            dateInstance["getYear"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                    var year = dt.Year;
                    return (double)(year >= 1900 ? year - 1900 : year);
                }

                return double.NaN;
            });

            dateInstance["getMonth"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)(local.Month - 1); // JS months are 0-indexed
                }

                return double.NaN;
            });

            dateInstance["getDate"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.Day;
                }

                return double.NaN;
            });

            dateInstance["getDay"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.DayOfWeek;
                }

                return double.NaN;
            });

            dateInstance["getHours"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.Hour;
                }

                return double.NaN;
            });

            dateInstance["getMinutes"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.Minute;
                }

                return double.NaN;
            });

            dateInstance["getSeconds"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.Second;
                }

                return double.NaN;
            });

            dateInstance["getMilliseconds"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)local.Millisecond;
                }

                return double.NaN;
            });

            dateInstance["getTimezoneOffset"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return (double)(-local.Offset.TotalMinutes);
                }

                return double.NaN;
            });

            dateInstance["toISOString"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = ConvertMillisecondsToUtc(ms);
                    return dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                }

                return "";
            });

            dateInstance["toString"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var local = GetLocalTimeFromInternalDate(obj);
                    return FormatDateToJsString(local);
                }

                return "Invalid Date";
            });

            // UTC-based accessors
            dateInstance["getUTCFullYear"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.Year;
                }

                return double.NaN;
            });

            dateInstance["getUTCMonth"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)(utc.Month - 1); // JS months are 0-indexed
                }

                return double.NaN;
            });

            dateInstance["getUTCDate"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.Day;
                }

                return double.NaN;
            });

            dateInstance["getUTCDay"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.DayOfWeek;
                }

                return double.NaN;
            });

            dateInstance["getUTCHours"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.Hour;
                }

                return double.NaN;
            });

            dateInstance["getUTCMinutes"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.Minute;
                }

                return double.NaN;
            });

            dateInstance["getUTCSeconds"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.Second;
                }

                return double.NaN;
            });

            dateInstance["getUTCMilliseconds"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj)
                {
                    var utc = GetUtcTimeFromInternalDate(obj);
                    return (double)utc.Millisecond;
                }

                return double.NaN;
            });

            // Formatting helpers
            dateInstance["toUTCString"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var utc = ConvertMillisecondsToUtc(ms);
                    return FormatUtcToJsUtcString(utc);
                }

                return "Invalid Date";
            });

            dateInstance["toJSON"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    var dt = ConvertMillisecondsToUtc(ms);
                    return dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                }

                return null;
            });

            // valueOf() – mirrors getTime()
            dateInstance["valueOf"] = new HostFunction((thisVal, methodArgs) =>
            {
                if (thisVal is JsObject obj && obj.TryGetProperty("_internalDate", out var val) && val is double ms)
                {
                    return ms;
                }

                return double.NaN;
            });

            return dateInstance;
        });

        dateConstructor.RealmState = realm;
        if (realm.FunctionPrototype is not null)
        {
            dateConstructor.Properties.SetPrototype(realm.FunctionPrototype);
        }

        datePrototype = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            datePrototype.SetPrototype(realm.ObjectPrototype);
        }
        dateConstructor.SetProperty("prototype", datePrototype);
        realm.DatePrototype ??= datePrototype;
        DatePrototype ??= datePrototype;

        dateConstructor.DefineProperty("name", new PropertyDescriptor
        {
            Value = "Date",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        dateConstructor.DefineProperty("length", new PropertyDescriptor
        {
            Value = 7d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        return dateConstructor;
    }

    /// <summary>
    /// Minimal Proxy constructor sufficient for Array.isArray proxy handling.
    /// General proxy semantics are not yet implemented.
    /// </summary>
    public static HostFunction CreateProxyConstructor(Runtime.RealmState realm)
    {
        JsObject? proxyPrototype = null;

        var proxyConstructor = new HostFunction((thisValue, args) =>
        {
            if (args.Count < 2)
            {
                throw new NotSupportedException("Proxy requires a target and handler.");
            }

            var target = args[0];
            var handler = args[1];

            if (target is not IJsObjectLike)
            {
                var error = TypeErrorConstructor is IJsCallable ctor
                    ? ctor.Invoke(["Proxy target must be an object"], null)
                    : new InvalidOperationException("Proxy target must be an object.");
                throw new ThrowSignal(error);
            }

            if (handler is not IJsObjectLike handlerObj)
            {
                var error = TypeErrorConstructor is IJsCallable ctor2
                    ? ctor2.Invoke(["Proxy handler must be an object"], null)
                    : new InvalidOperationException("Proxy handler must be an object.");
                throw new ThrowSignal(error);
            }

            var proxy = new JsProxy(target!, handlerObj);
            if (proxyPrototype is not null)
            {
                proxy.SetPrototype(proxyPrototype);
            }

            return proxy;
        });

        proxyConstructor.RealmState = realm;
        if (realm.FunctionPrototype is not null)
        {
            proxyConstructor.Properties.SetPrototype(realm.FunctionPrototype);
        }

        proxyPrototype = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            proxyPrototype.SetPrototype(realm.ObjectPrototype);
        }
        proxyConstructor.SetProperty("prototype", proxyPrototype);

        proxyConstructor.DefineProperty("name", new PropertyDescriptor
        {
            Value = "Proxy",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        proxyConstructor.DefineProperty("length", new PropertyDescriptor
        {
            Value = 2d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        var revocableFn = new HostFunction((thisValue, args) =>
        {
            if (args.Count < 2)
            {
                var error = TypeErrorConstructor is IJsCallable ctor
                    ? ctor.Invoke(["Proxy.revocable requires a target and handler"], null)
                    : new InvalidOperationException("Proxy.revocable requires a target and handler.");
                throw new ThrowSignal(error);
            }

            var target = args[0];
            var handler = args[1];

            if (target is not IJsObjectLike)
            {
                var error = TypeErrorConstructor is IJsCallable ctor2
                    ? ctor2.Invoke(["Proxy target must be an object"], null)
                    : new InvalidOperationException("Proxy target must be an object.");
                throw new ThrowSignal(error);
            }

            if (handler is not IJsObjectLike handlerObj)
            {
                var error = TypeErrorConstructor is IJsCallable ctor3
                    ? ctor3.Invoke(["Proxy handler must be an object"], null)
                    : new InvalidOperationException("Proxy handler must be an object.");
                throw new ThrowSignal(error);
            }

            var proxy = new JsProxy(target!, handlerObj);
            if (proxyPrototype is not null)
            {
                proxy.SetPrototype(proxyPrototype);
            }

            var container = new JsObject();
            container.SetProperty("proxy", proxy);
            container.SetProperty("revoke", new HostFunction((_, _) =>
            {
                proxy.Handler = null;
                return Symbols.Undefined;
            }));

            return container;
        });
        revocableFn.IsConstructor = false;
        proxyConstructor.DefineProperty("revocable", new PropertyDescriptor
        {
            Value = revocableFn,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });

        return proxyConstructor;
    }

    /// <summary>
    /// Creates a JSON object with parse and stringify methods.
    /// </summary>
    public static JsObject CreateJsonObject()
    {
        var json = new JsObject();

        // JSON.parse()
        json["parse"] = new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not string jsonStr)
            {
                return null;
            }

            try
            {
                return ParseJsonValue(System.Text.Json.JsonDocument.Parse(jsonStr).RootElement);
            }
            catch
            {
                // In real JavaScript, this would throw a SyntaxError
                return null;
            }
        });

        // JSON.stringify()
        json["stringify"] = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return "undefined";
            }

            var value = args[0];

            // Handle replacer function and space arguments if needed
            // For now, implement basic stringify
            return StringifyValue(value);
        });

        return json;
    }

    private static object? ParseJsonValue(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                var obj = new JsObject();
                foreach (var prop in element.EnumerateObject())
                {
                    obj[prop.Name] = ParseJsonValue(prop.Value);
                }

                return obj;

            case System.Text.Json.JsonValueKind.Array:
                var arr = new JsArray();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Push(ParseJsonValue(item));
                }

                AddArrayMethods(arr);
                return arr;

            case System.Text.Json.JsonValueKind.String:
                return element.GetString();

            case System.Text.Json.JsonValueKind.Number:
                return element.GetDouble();

            case System.Text.Json.JsonValueKind.True:
                return true;

            case System.Text.Json.JsonValueKind.False:
                return false;

            case System.Text.Json.JsonValueKind.Null:
            default:
                return null;
        }
    }

    private static string StringifyValue(object? value, int depth = 0)
    {
        if (depth > 100)
        {
            return "null"; // Prevent stack overflow
        }

        switch (value)
        {
            case null:
                return "null";

            case bool b:
                return b ? "true" : "false";

            case double d:
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    return "null";
                }

                return d.ToString(System.Globalization.CultureInfo.InvariantCulture);

            case string s:
                return System.Text.Json.JsonSerializer.Serialize(s);

            case JsArray arr:
                var arrItems = new List<string>();
                foreach (var item in arr.Items)
                {
                    arrItems.Add(StringifyValue(item, depth + 1));
                }

                return "[" + string.Join(",", arrItems) + "]";

            case JsObject obj:
                var objProps = new List<string>();
                foreach (var kvp in obj)
                {
                    // Skip functions and internal properties
                    if (kvp.Value is IJsCallable || kvp.Key.StartsWith("_"))
                    {
                        continue;
                    }

                    var key = System.Text.Json.JsonSerializer.Serialize(kvp.Key);
                    var val = StringifyValue(kvp.Value, depth + 1);
                    objProps.Add($"{key}:{val}");
                }

                return "{" + string.Join(",", objProps) + "}";

            case IJsCallable:
                return "undefined";

            default:
                return System.Text.Json.JsonSerializer.Serialize(value?.ToString() ?? "");
        }
    }

    /// <summary>
    /// Creates a RegExp constructor function.
    /// </summary>
    public static IJsCallable CreateRegExpConstructor()
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return CreateRegExpLiteral("(?:)", "");
            }

            if (args.Count == 1 && args[0] is JsObject { } existingObj &&
                existingObj.TryGetProperty("__regex__", out var internalRegex) &&
                internalRegex is JsRegExp existing)
            {
                return CreateRegExpLiteral(existing.Pattern, existing.Flags);
            }

            var pattern = args[0]?.ToString() ?? "";
            var flags = args.Count > 1 ? args[1]?.ToString() ?? "" : "";
            return CreateRegExpLiteral(pattern, flags);
        });
    }

    internal static JsObject CreateRegExpLiteral(string pattern, string flags)
    {
        var regex = new JsRegExp(pattern, flags);
        regex.JsObject["__regex__"] = regex;
        AddRegExpMethods(regex);
        return regex.JsObject;
    }

    /// <summary>
    /// Adds RegExp instance methods to a JsRegExp object.
    /// </summary>
    private static void AddRegExpMethods(JsRegExp regex)
    {
        // test(string) - returns boolean
        regex.SetProperty("test", new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                return false;
            }

            var input = args[0]?.ToString() ?? "";
            return regex.Test(input);
        }));

        // exec(string) - returns array with match details or null
        regex.SetProperty("exec", new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            var input = args[0]?.ToString() ?? "";
            return regex.Exec(input);
        }));
    }

    private static double ToLengthOrZero(object? value)
    {
        var number = JsOps.ToNumber(value);
        if (double.IsNaN(number) || number <= 0)
        {
            return 0;
        }

        if (double.IsPositiveInfinity(number))
        {
            return 9007199254740991d; // 2^53 - 1
        }

        var truncated = Math.Floor(number);
        return truncated > 9007199254740991d ? 9007199254740991d : truncated;
    }

    private static double ToIntegerOrInfinity(object? value)
    {
        var number = JsOps.ToNumberWithContext(value);
        if (double.IsNaN(number))
        {
            return 0;
        }

        if (double.IsInfinity(number) || number == 0)
        {
            return number;
        }

        return Math.Sign(number) * Math.Floor(Math.Abs(number));
    }

    private static bool SameValueZero(object? x, object? y)
    {
        if (x is double dx && double.IsNaN(dx) && y is double dy && double.IsNaN(dy))
        {
            return true;
        }

        return JsOps.StrictEquals(x, y);
    }

    private static IJsPropertyAccessor EnsureArrayLikeReceiver(object? receiver, string methodName)
    {
        if (receiver is null || ReferenceEquals(receiver, Symbols.Undefined))
        {
            throw ThrowTypeError($"{methodName} called on null or undefined");
        }

        if (receiver is IJsPropertyAccessor accessor)
        {
            if (accessor is JsObject jsObj && jsObj.TryGetProperty("__value__", out var inner) && inner is string sInner)
            {
                if (!jsObj.TryGetProperty("length", out _))
                {
                    jsObj.DefineProperty("length", new PropertyDescriptor
                    {
                        Value = (double)sInner.Length,
                        Writable = false,
                        Enumerable = false,
                        Configurable = false
                    });

                    for (var i = 0; i < sInner.Length; i++)
                    {
                        jsObj.SetProperty(i.ToString(CultureInfo.InvariantCulture), sInner[i].ToString());
                    }
                }

                return jsObj;
            }

            return accessor;
        }

        // Box primitives to objects per ToObject.
        if (receiver is string s)
        {
            var obj = new JsObject();
            obj.SetPrototype(StringPrototype);
            obj.SetProperty("__value__", s);
            obj.DefineProperty("length", new PropertyDescriptor
            {
                Value = (double)s.Length,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });

            for (var i = 0; i < s.Length; i++)
            {
                obj.SetProperty(i.ToString(CultureInfo.InvariantCulture), s[i].ToString());
            }

            return obj;
        }

        if (receiver is double or int or uint or long or ulong or short or ushort or byte or sbyte or decimal or float)
        {
            var obj = new JsObject();
            obj.SetPrototype(NumberPrototype);
            obj.SetProperty("__value__", receiver);
            return obj;
        }

        if (receiver is bool b)
        {
            var obj = new JsObject();
            obj.SetPrototype(BooleanPrototype);
            obj.SetProperty("__value__", b);
            return obj;
        }

        // Symbols and BigInts should throw TypeError for array methods
        if (receiver is TypedAstSymbol || receiver is JsBigInt)
        {
            throw ThrowTypeError($"{methodName} called on incompatible receiver");
        }

        throw ThrowTypeError($"{methodName} called on non-object");
    }

    /// <summary>
    /// Adds standard array methods to a JsArray instance.
    /// </summary>
    public static void AddArrayMethods(IJsPropertyAccessor array, JsObject? prototypeOverride = null)
    {
        // Once the shared Array prototype has been initialised, new arrays
        // should inherit from it instead of receiving per-instance copies of
        // every method. This keeps prototype mutations (e.g. in tests) visible
        // to existing arrays.
        var resolvedPrototype = prototypeOverride ?? ArrayPrototype;
        if (resolvedPrototype is not null && array is JsArray jsArray)
        {
            jsArray.SetPrototype(resolvedPrototype);
            return;
        }

        // push - already implemented natively
        array.SetProperty("push", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            foreach (var arg in args)
            {
                jsArray.Push(arg);
            }

            return jsArray.Items.Count;
        }));

        // pop
        array.SetProperty("pop", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            return jsArray.Pop();
        }));

        // map
        array.SetProperty("map", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
            }

            var result = new JsArray();
            for (var i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var mapped = callback.Invoke([element, (double)i, jsArray], null);
                result.Push(mapped);
            }

            AddArrayMethods(result);
            return result;
        }));

        // filter
        array.SetProperty("filter", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
            }

            var result = new JsArray();
            for (var i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var keep = callback.Invoke([element, (double)i, jsArray], null);
                if (IsTruthy(keep))
                {
                    result.Push(element);
                }
            }

            AddArrayMethods(result);
            return result;
        }));

        // reduce
        array.SetProperty("reduce", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
            }

            if (jsArray.Items.Count == 0)
            {
                return args.Count > 1 ? args[1] : null;
            }

            var startIndex = 0;
            object? accumulator;

            if (args.Count > 1)
            {
                accumulator = args[1];
            }
            else
            {
                accumulator = jsArray.Items[0];
                startIndex = 1;
            }

            for (var i = startIndex; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                accumulator = callback.Invoke([accumulator, element, (double)i, jsArray], null);
            }

            return accumulator;
        }));

        // forEach
        array.SetProperty("forEach", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
            }

            for (var i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                callback.Invoke([element, (double)i, jsArray], null);
            }

            return null;
        }));

        // find
        array.SetProperty("find", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
            }

            for (var i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var match = callback.Invoke([element, (double)i, jsArray], null);
                if (IsTruthy(match))
                {
                    return element;
                }
            }

            return null;
        }));

        // findIndex
        array.SetProperty("findIndex", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
            }

            for (var i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var match = callback.Invoke([element, (double)i, jsArray], null);
                if (IsTruthy(match))
                {
                    return (double)i;
                }
            }

            return -1d;
        }));

        // some
        array.SetProperty("some", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return false;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return false;
            }

            for (var i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var result = callback.Invoke([element, (double)i, jsArray], null);
                if (IsTruthy(result))
                {
                    return true;
                }
            }

            return false;
        }));

        // every
        array.SetProperty("every", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return true;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return true;
            }

            for (var i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var result = callback.Invoke([element, (double)i, jsArray], null);
                if (!IsTruthy(result))
                {
                    return false;
                }
            }

            return true;
        }));

        // join
        array.SetProperty("join", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return "";
            }

            var separator = args.Count > 0 && args[0] is string sep ? sep : ",";

            var length = jsArray.Length > int.MaxValue ? int.MaxValue : (int)jsArray.Length;
            var parts = new List<string>(length);
            for (var i = 0; i < length; i++)
            {
                var element = jsArray.GetElement(i);
                parts.Add(element.ToJsStringForArray());
            }

            return string.Join(separator, parts);
        }));

        // toString - delegates to join with the default separator
        array.SetProperty("toString", new HostFunction((thisValue, args) =>
        {
            if (thisValue is JsArray jsArray)
            {
                return array.TryGetProperty("join", out var join) && join is IJsCallable joinFn
                    ? joinFn.Invoke(Array.Empty<object?>(), jsArray)
                    : string.Empty;
            }

            if (thisValue is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("join", out var joinVal) &&
                joinVal is IJsCallable callableJoin)
            {
                return callableJoin.Invoke(Array.Empty<object?>(), thisValue);
            }

            return "[object Object]";
        }));

        // includes
        array.SetProperty("includes", new HostFunction((thisValue, args) =>
        {
            var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.includes");

            var searchElement = args.Count > 0 ? args[0] : Symbols.Undefined;
            var fromIndexArg = args.Count > 1 ? args[1] : 0d;
            var length = 0d;
            if (accessor.TryGetProperty("length", out var lenVal)) length = ToLengthOrZero(lenVal);

            var fromIndex = ToIntegerOrInfinity(fromIndexArg);
            if (double.IsPositiveInfinity(fromIndex))
            {
                return false;
            }

            if (fromIndex < 0)
            {
                fromIndex = length + Math.Ceiling(fromIndex);
                if (fromIndex < 0)
                {
                    fromIndex = 0;
                }
            }

            var start = (long)Math.Min(fromIndex, length);
            var lenLong = (long)Math.Min(length, 9007199254740991d);

            if (accessor is JsArray jsArr && lenLong > 100000)
            {
                var indices = jsArr.GetOwnIndices()
                    .Where(idx => idx >= start && idx < lenLong)
                    .OrderBy(idx => idx);
                foreach (var idx in indices)
                {
                    var val = jsArr.GetElement((int)idx);
                    if (SameValueZero(val, searchElement))
                    {
                        return true;
                    }
                }
            }
            else
            {
                for (var i = start; i < lenLong; i++)
                {
                    var key = i.ToString(CultureInfo.InvariantCulture);
                    var exists = accessor.TryGetProperty(key, out var value);
                    if (exists && SameValueZero(value, searchElement))
                    {
                        return true;
                    }
                }
            }

            return false;
        }));

        // indexOf
        array.SetProperty("indexOf", new HostFunction((thisValue, args) =>
        {
            var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.indexOf");

            if (args.Count == 0)
            {
                return -1d;
            }

            var searchElement = args[0];
            var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal) : 0d;
            var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1]) : 0d;

            if (double.IsPositiveInfinity(fromIndex))
            {
                return -1d;
            }

            if (fromIndex < 0)
            {
                fromIndex = Math.Max(length + Math.Ceiling(fromIndex), 0);
            }
            else
            {
                fromIndex = Math.Min(fromIndex, length);
            }

            var start = (long)Math.Min(fromIndex, length);
            var lenLong = (long)Math.Min(length, 9007199254740991d);

            if (accessor is JsArray jsArr && lenLong > 100000)
            {
                var indices = jsArr.GetOwnIndices()
                    .Where(idx => idx >= start && idx < lenLong)
                    .OrderBy(idx => idx);
                foreach (var idx in indices)
                {
                    if (AreStrictlyEqual(jsArr.GetElement((int)idx), searchElement))
                    {
                        return (double)idx;
                    }
                }
            }
            else
            {
                for (var i = start; i < lenLong; i++)
                {
                    var key = i.ToString(CultureInfo.InvariantCulture);
                    if (accessor.TryGetProperty(key, out var value) && AreStrictlyEqual(value, searchElement))
                    {
                        return (double)i;
                    }
                }
            }

            return -1d;
        }));

        // toLocaleString
        array.SetProperty("toLocaleString", new HostFunction((thisValue, args) =>
        {
            var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.toLocaleString");

            var locales = args.Count > 0 ? args[0] : Symbols.Undefined;
            var options = args.Count > 1 ? args[1] : Symbols.Undefined;
            var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal) : 0d;
            var parts = new List<string>((int)length);

            for (var i = 0; i < length; i++)
            {
                var key = i.ToString(CultureInfo.InvariantCulture);
                if (!accessor.TryGetProperty(key, out var element) ||
                    element is null ||
                    ReferenceEquals(element, Symbols.Undefined))
                {
                    parts.Add(string.Empty);
                    continue;
                }

                string part;
                if (element is IJsPropertyAccessor elementAccessor &&
                    elementAccessor.TryGetProperty("toLocaleString", out var method) &&
                    method is IJsCallable callable)
                {
                    var result = callable.Invoke(new object?[] { locales, options }, element);
                    part = JsOps.ToJsString(result);
                }
                else
                {
                    part = JsOps.ToJsString(element);
                }

                parts.Add(part);
            }

            return string.Join(",", parts);
        }));

        // slice
        array.SetProperty("slice", new HostFunction((thisValue, args) => ArraySlice(thisValue, args)));

        // shift
        array.SetProperty("shift", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            return jsArray.Shift();
        }));

        // unshift
        array.SetProperty("unshift", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return 0;
            }

            jsArray.Unshift(args.ToArray());
            return jsArray.Items.Count;
        }));

        // splice
        array.SetProperty("splice", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var start = args.Count > 0 && args[0] is double startD ? (int)startD : 0;
            var deleteCount = args.Count > 1 && args[1] is double deleteD ? (int)deleteD : jsArray.Items.Count - start;

            var itemsToInsert = args.Count > 2 ? args.Skip(2).ToArray() : [];

            var deleted = jsArray.Splice(start, deleteCount, itemsToInsert);
            AddArrayMethods(deleted);
            return deleted;
        }));

        // concat
        array.SetProperty("concat", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var result = new JsArray();
            // Add current array items
            foreach (var item in jsArray.Items)
            {
                result.Push(item);
            }

            // Add items from arguments
            foreach (var arg in args)
            {
                if (arg is JsArray argArray)
                {
                    foreach (var item in argArray.Items)
                    {
                        result.Push(item);
                    }
                }
                else
                {
                    result.Push(arg);
                }
            }

            AddArrayMethods(result);
            return result;
        }));

        // reverse
        array.SetProperty("reverse", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            jsArray.Reverse();
            return jsArray;
        }));

        // sort
        array.SetProperty("sort", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var items = jsArray.Items.ToList();

            if (args.Count > 0 && args[0] is IJsCallable compareFn)
                // Sort with custom compare function
            {
                items.Sort((a, b) =>
                {
                    var result = compareFn.Invoke([a, b], null);
                    if (result is double d)
                    {
                        return d > 0 ? 1 : d < 0 ? -1 : 0;
                    }

                    return 0;
                });
            }
            else
                // Default sort: convert to strings and sort lexicographically
            {
                items.Sort((a, b) =>
                {
                    var aStr = JsValueToString(a);
                    var bStr = JsValueToString(b);
                    return string.Compare(aStr, bStr, StringComparison.Ordinal);
                });
            }

            // Replace array items with sorted items
            for (var i = 0; i < items.Count; i++) jsArray.SetElement(i, items[i]);

            return jsArray;
        }));

        // at(index)
        array.SetProperty("at", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not double d)
            {
                return null;
            }

            var index = (int)d;
            // Handle negative indices
            if (index < 0)
            {
                index = jsArray.Items.Count + index;
            }

            if (index < 0 || index >= jsArray.Items.Count)
            {
                return null;
            }

            return jsArray.GetElement(index);
        }));

        // flat(depth = 1)
        array.SetProperty("flat", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var depth = args.Count > 0 && args[0] is double d ? (int)d : 1;

            var result = new JsArray();
            FlattenArray(jsArray, result, depth);
            AddArrayMethods(result);
            return result;
        }));

        // flatMap(callback)
        array.SetProperty("flatMap", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
            }

            var result = new JsArray();
            for (var i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var mapped = callback.Invoke([element, (double)i, jsArray], null);

                // Flatten one level
                if (mapped is JsArray mappedArray)
                {
                    for (var j = 0; j < mappedArray.Items.Count; j++)
                        result.Push(mappedArray.GetElement(j));
                }
                else
                {
                    result.Push(mapped);
                }
            }

            AddArrayMethods(result);
            return result;
        }));

        // findLast(callback)
        array.SetProperty("findLast", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
            }

            for (var i = jsArray.Items.Count - 1; i >= 0; i--)
            {
                var element = jsArray.Items[i];
                var matches = callback.Invoke([element, (double)i, jsArray], null);
                if (IsTruthy(matches))
                {
                    return element;
                }
            }

            return null;
        }));

        // findLastIndex(callback)
        array.SetProperty("findLastIndex", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return -1d;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return -1d;
            }

            for (var i = jsArray.Items.Count - 1; i >= 0; i--)
            {
                var element = jsArray.Items[i];
                var matches = callback.Invoke([element, (double)i, jsArray], null);
                if (IsTruthy(matches))
                {
                    return (double)i;
                }
            }

            return -1d;
        }));

        // fill(value, start = 0, end = length)
        array.SetProperty("fill", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0)
            {
                return jsArray;
            }

            var value = args[0];
            var start = args.Count > 1 && args[1] is double d1 ? (int)d1 : 0;
            var end = args.Count > 2 && args[2] is double d2 ? (int)d2 : jsArray.Items.Count;

            // Handle negative indices
            if (start < 0)
            {
                start = Math.Max(0, jsArray.Items.Count + start);
            }

            if (end < 0)
            {
                end = Math.Max(0, jsArray.Items.Count + end);
            }

            // Clamp to array bounds
            start = Math.Max(0, Math.Min(start, jsArray.Items.Count));
            end = Math.Max(start, Math.Min(end, jsArray.Items.Count));

            for (var i = start; i < end; i++) jsArray.SetElement(i, value);

            return jsArray;
        }));

        // copyWithin(target, start = 0, end = length)
        array.SetProperty("copyWithin", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0)
            {
                return jsArray;
            }

            var target = args[0] is double dt ? (int)dt : 0;
            var start = args.Count > 1 && args[1] is double ds ? (int)ds : 0;
            var end = args.Count > 2 && args[2] is double de ? (int)de : jsArray.Items.Count;

            var len = jsArray.Items.Count;

            // Handle negative indices
            if (target < 0)
            {
                target = Math.Max(0, len + target);
            }
            else
            {
                target = Math.Min(target, len);
            }

            if (start < 0)
            {
                start = Math.Max(0, len + start);
            }
            else
            {
                start = Math.Min(start, len);
            }

            if (end < 0)
            {
                end = Math.Max(0, len + end);
            }
            else
            {
                end = Math.Min(end, len);
            }

            var count = Math.Min(end - start, len - target);
            if (count <= 0)
            {
                return jsArray;
            }

            // Copy to temporary array to handle overlapping ranges
            var temp = new object?[count];
            for (var i = 0; i < count; i++) temp[i] = jsArray.GetElement(start + i);

            for (var i = 0; i < count; i++) jsArray.SetElement(target + i, temp[i]);

            return jsArray;
        }));

        // toSorted(compareFn) - non-mutating sort
        array.SetProperty("toSorted", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var result = new JsArray();
            for (var i = 0; i < jsArray.Items.Count; i++) result.Push(jsArray.GetElement(i));
            AddArrayMethods(result);

            var items = result.Items.ToList();

            if (args.Count > 0 && args[0] is IJsCallable compareFn)
                // Sort with custom compare function
            {
                items.Sort((a, b) =>
                {
                    var cmp = compareFn.Invoke([a, b], null);
                    if (cmp is double d)
                    {
                        return d > 0 ? 1 : d < 0 ? -1 : 0;
                    }

                    return 0;
                });
            }
            else
                // Default sort: convert to strings and sort lexicographically
            {
                items.Sort((a, b) =>
                {
                    var aStr = JsValueToString(a);
                    var bStr = JsValueToString(b);
                    return string.Compare(aStr, bStr, StringComparison.Ordinal);
                });
            }

            for (var i = 0; i < items.Count; i++) result.SetElement(i, items[i]);

            return result;
        }));

        // toReversed() - non-mutating reverse
        array.SetProperty("toReversed", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var result = new JsArray();
            for (var i = jsArray.Items.Count - 1; i >= 0; i--) result.Push(jsArray.GetElement(i));
            AddArrayMethods(result);
            return result;
        }));

        // toSpliced(start, deleteCount, ...items) - non-mutating splice
        array.SetProperty("toSpliced", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var result = new JsArray();
            var len = jsArray.Items.Count;

            if (args.Count == 0)
            {
                // No arguments, return copy
                for (var i = 0; i < len; i++) result.Push(jsArray.GetElement(i));
            }
            else
            {
                var start = args[0] is double ds ? (int)ds : 0;
                var deleteCount = args.Count > 1 && args[1] is double dc ? (int)dc : len - start;

                // Handle negative start
                if (start < 0)
                {
                    start = Math.Max(0, len + start);
                }
                else
                {
                    start = Math.Min(start, len);
                }

                // Clamp deleteCount
                deleteCount = Math.Max(0, Math.Min(deleteCount, len - start));

                // Copy elements before start
                for (var i = 0; i < start; i++) result.Push(jsArray.GetElement(i));

                // Insert new items
                for (var i = 2; i < args.Count; i++) result.Push(args[i]);

                // Copy elements after deleted section
                for (var i = start + deleteCount; i < len; i++) result.Push(jsArray.GetElement(i));
            }

            AddArrayMethods(result);
            return result;
        }));

        // with(index, value) - non-mutating element replacement
        array.SetProperty("with", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count < 2)
            {
                return null;
            }

            if (args[0] is not double d)
            {
                return null;
            }

            var index = (int)d;
            var value = args[1];

            // Handle negative indices
            if (index < 0)
            {
                index = jsArray.Items.Count + index;
            }

            // Index out of bounds throws RangeError in JavaScript
            if (index < 0 || index >= jsArray.Items.Count)
            {
                return null;
            }

            var result = new JsArray();
            for (var i = 0; i < jsArray.Items.Count; i++) result.Push(i == index ? value : jsArray.GetElement(i));
            AddArrayMethods(result);
            return result;
        }));

        static double ToLengthValue(object? candidate)
        {
            var num = JsOps.ToNumber(candidate);
            if (double.IsNaN(num) || double.IsInfinity(num) || num <= 0)
            {
                return 0;
            }

            var truncated = Math.Floor(num);
            return Math.Min(truncated, 9007199254740991d); // 2^53 - 1
        }

        static object CreateArrayIterator(object? thisValue, IJsPropertyAccessor accessor, Func<uint, object?> projector)
        {
            var iterator = new JsObject();
            var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
            var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

            uint index = 0;
            var exhausted = false;

            iterator.SetProperty("next", new HostFunction((_, __) =>
            {
                if (exhausted)
                {
                    var doneResult = new JsObject();
                    doneResult.SetProperty("value", Symbols.Undefined);
                    doneResult.SetProperty("done", true);
                    return doneResult;
                }

                uint length = 0;
                if (accessor.TryGetProperty("length", out var lengthValue))
                {
                    length = (uint)ToLengthValue(lengthValue);
                }

                var result = new JsObject();
                if (index < length)
                {
                    result.SetProperty("value", projector(index));
                    result.SetProperty("done", false);
                    index++;
                }
                else
                {
                    result.SetProperty("value", Symbols.Undefined);
                    result.SetProperty("done", true);
                    exhausted = true;
                }

                return result;
            }));

            iterator.SetProperty(iteratorKey, new HostFunction((_, __) => iterator));
            return iterator;
        }

        HostFunction DefineArrayIteratorFunction(string name, Func<IJsPropertyAccessor, object?, Func<uint, object?>> projectorFactory)
        {
            var fn = new HostFunction((thisValue, args) =>
            {
                if (thisValue is null || ReferenceEquals(thisValue, Symbols.Undefined))
                {
                    var error = TypeErrorConstructor is IJsCallable ctor
                        ? ctor.Invoke([$"{name} called on null or undefined"], null)
                        : new InvalidOperationException($"{name} called on null or undefined");
                    throw new ThrowSignal(error);
                }

                if (thisValue is not IJsPropertyAccessor accessor)
                {
                    var error = TypeErrorConstructor is IJsCallable ctor2
                        ? ctor2.Invoke([$"{name} called on non-object"], null)
                        : new InvalidOperationException($"{name} called on non-object");
                    throw new ThrowSignal(error);
                }

                var projector = projectorFactory(accessor, thisValue);
                return CreateArrayIterator(thisValue, accessor, projector);
            })
            {
                IsConstructor = false
            };

            fn.DefineProperty("name", new PropertyDescriptor
            {
                Value = name,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });

            fn.DefineProperty("length", new PropertyDescriptor
            {
                Value = 0d,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });

            var descriptor = new PropertyDescriptor
            {
                Value = fn,
                Writable = true,
                Enumerable = false,
                Configurable = true
            };

            if (array is IJsObjectLike objectLike)
            {
                objectLike.DefineProperty(name, descriptor);
            }
            else
            {
                array.SetProperty(name, fn);
            }

            return fn;
        }

        // entries() - returns an iterator of [index, value] pairs
        DefineArrayIteratorFunction("entries", (accessor, _) => idx =>
        {
            var pair = new JsArray();
            pair.Push((double)idx);
            if (accessor.TryGetProperty(idx.ToString(CultureInfo.InvariantCulture), out var value))
            {
                pair.Push(value);
            }
            else
            {
                pair.Push(Symbols.Undefined);
            }

            AddArrayMethods(pair);
            return pair;
        });

        // keys() - returns an iterator of indices
        DefineArrayIteratorFunction("keys", (_, __) => idx => (double)idx);

        // values() - returns an iterator of values
        var valuesFn = DefineArrayIteratorFunction("values", (accessor, thisValue) => idx =>
        {
            var key = idx.ToString(CultureInfo.InvariantCulture);
            return accessor.TryGetProperty(key, out var value) ? value : Symbols.Undefined;
        });
    }

    private static object? ArraySlice(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray)
        {
            return null;
        }

        var start = 0;
        var end = jsArray.Items.Count;

        if (args.Count > 0 && args[0] is double startD)
        {
            start = (int)startD;
            if (start < 0)
            {
                start = Math.Max(0, jsArray.Items.Count + start);
            }
        }

        if (args.Count > 1 && args[1] is double endD)
        {
            end = (int)endD;
            if (end < 0)
            {
                end = Math.Max(0, jsArray.Items.Count + end);
            }
        }

        var result = new JsArray();
        for (var i = start; i < Math.Min(end, jsArray.Items.Count); i++)
        {
            result.Push(jsArray.Items[i]);
        }

        AddArrayMethods(result);
        return result;
    }

    private static void FlattenArray(JsArray source, JsArray target, int depth)
    {
        foreach (var item in source.Items)
        {
            if (depth > 0 && item is JsArray nestedArray)
            {
                FlattenArray(nestedArray, target, depth - 1);
            }
            else
            {
                target.Push(item);
            }
        }
    }

    private static bool IsTruthy(object? value)
    {
        return JsOps.IsTruthy(value);
    }

    private static bool AreStrictlyEqual(object? left, object? right)
    {
        return JsOps.StrictEquals(left, right);
    }

    /// <summary>
    /// Creates the Boolean constructor function.
    /// </summary>
    public static HostFunction CreateBooleanConstructor(Runtime.RealmState realm)
    {
        // Boolean(value) -> boolean primitive using ToBoolean semantics.
        var booleanConstructor = new HostFunction((thisValue, args) =>
        {
            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            var coerced = JsOps.ToBoolean(value);

            // When called with `new`, thisValue is the newly-created object;
            // store the primitive value so property lookups (e.g. ToPropertyName)
            // can recover it.
            if (thisValue is JsObject obj)
            {
                obj.SetProperty("__value__", coerced);
                return obj;
            }

            return coerced;
        });

        // Expose Boolean.prototype so user code can attach methods (e.g.
        // Boolean.prototype.toJSONString in string-tagcloud.js).
        var prototype = new JsObject();
        realm.BooleanPrototype ??= prototype;
        BooleanPrototype ??= prototype;
        if (realm.ObjectPrototype is not null && prototype.Prototype is null)
        {
            prototype.SetPrototype(realm.ObjectPrototype);
        }
        booleanConstructor.SetProperty("prototype", prototype);

        return booleanConstructor;
    }

    /// <summary>
    /// Creates a wrapper object for a boolean primitive so that auto-boxed
    /// booleans can see methods added to Boolean.prototype.
    /// </summary>
    public static JsObject CreateBooleanWrapper(bool value)
    {
        var booleanObj = new JsObject
        {
            ["__value__"] = value
        };

        if (BooleanPrototype is not null)
        {
            booleanObj.SetPrototype(BooleanPrototype);
        }

        return booleanObj;
    }

    /// <summary>
    /// Creates a Promise constructor with static methods.
    /// </summary>
    public static IJsCallable CreatePromiseConstructor(JsEngine engine)
    {
        var promiseConstructor = new HostFunction((thisValue, args) =>
        {
            // Promise constructor takes an executor function: function(resolve, reject) { ... }
            if (args.Count == 0 || args[0] is not IJsCallable executor)
            {
                throw new InvalidOperationException("Promise constructor requires an executor function");
            }

            var promise = new JsPromise(engine);
            var promiseObj = promise.JsObject;

            // Create resolve and reject callbacks
            var resolve = new HostFunction(resolveArgs =>
            {
                promise.Resolve(resolveArgs.Count > 0 ? resolveArgs[0] : null);
                return null;
            });

            var reject = new HostFunction(rejectArgs =>
            {
                promise.Reject(rejectArgs.Count > 0 ? rejectArgs[0] : null);
                return null;
            });

            // Add then, catch, and finally methods
            promiseObj["then"] = new HostFunction((promiseThis, thenArgs) =>
            {
                var onFulfilled = thenArgs.Count > 0 ? thenArgs[0] as IJsCallable : null;
                var onRejected = thenArgs.Count > 1 ? thenArgs[1] as IJsCallable : null;
                var resultPromise = promise.Then(onFulfilled, onRejected);
                AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);
                return resultPromise.JsObject;
            });

            promiseObj["catch"] = new HostFunction((promiseThis, catchArgs) =>
            {
                var onRejected = catchArgs.Count > 0 ? catchArgs[0] as IJsCallable : null;
                var resultPromise = promise.Then(null, onRejected);
                AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);
                return resultPromise.JsObject;
            });

            promiseObj["finally"] = new HostFunction((promiseThis, finallyArgs) =>
            {
                var onFinally = finallyArgs.Count > 0 ? finallyArgs[0] as IJsCallable : null;
                if (onFinally == null)
                {
                    return promiseObj;
                }

                var finallyWrapper = new HostFunction(wrapperArgs =>
                {
                    onFinally.Invoke([], null);
                    return wrapperArgs.Count > 0 ? wrapperArgs[0] : null;
                });

                var resultPromise = promise.Then(finallyWrapper, finallyWrapper);
                AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);
                return resultPromise.JsObject;
            });

            // Execute the executor function immediately
            try
            {
                executor.Invoke([resolve, reject], null);
            }
            catch (Exception ex)
            {
                promise.Reject(ex.Message);
            }

            return promiseObj;
        });

        // Add static methods to Promise constructor
        if (promiseConstructor is HostFunction hf)
        {
            // Promise.resolve(value)
            hf.SetProperty("resolve", new HostFunction(args =>
            {
                var value = args.Count > 0 ? args[0] : null;
                var promise = new JsPromise(engine);

                // Add instance methods
                AddPromiseInstanceMethods(promise.JsObject, promise, engine);

                promise.Resolve(value);
                return promise.JsObject;
            }));

            // Promise.reject(reason)
            hf.SetProperty("reject", new HostFunction(args =>
            {
                var reason = args.Count > 0 ? args[0] : null;
                var promise = new JsPromise(engine);

                // Add instance methods
                AddPromiseInstanceMethods(promise.JsObject, promise, engine);

                promise.Reject(reason);
                return promise.JsObject;
            }));

            // Promise.all(iterable)
            hf.SetProperty("all", new HostFunction(args =>
            {
                if (args.Count == 0 || args[0] is not JsArray array)
                {
                    return null;
                }

                var resultPromise = new JsPromise(engine);
                AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);

                var results = new object?[array.Items.Count];
                var remaining = array.Items.Count;

                if (remaining == 0)
                {
                    var emptyArray = new JsArray();
                    AddArrayMethods(emptyArray);
                    resultPromise.Resolve(emptyArray);
                    return resultPromise.JsObject;
                }

                for (var i = 0; i < array.Items.Count; i++)
                {
                    var index = i;
                    var item = array.Items[i];

                    // Check if item is a promise (JsObject with "then" method)
                    if (item is JsObject itemObj && itemObj.TryGetProperty("then", out var thenMethod) &&
                        thenMethod is IJsCallable thenCallable)
                    {
                        thenCallable.Invoke([
                            new HostFunction(resolveArgs =>
                            {
                                results[index] = resolveArgs.Count > 0 ? resolveArgs[0] : null;
                                remaining--;

                                if (remaining == 0)
                                {
                                    var resultArray = new JsArray();
                                    foreach (var result in results)
                                    {
                                        resultArray.Push(result);
                                    }

                                    AddArrayMethods(resultArray);
                                    resultPromise.Resolve(resultArray);
                                }

                                return null;
                            }),
                            new HostFunction(rejectArgs =>
                            {
                                resultPromise.Reject(rejectArgs.Count > 0 ? rejectArgs[0] : null);
                                return null;
                            })
                        ], itemObj);
                    }
                    else
                    {
                        results[index] = item;
                        remaining--;

                        if (remaining == 0)
                        {
                            var resultArray = new JsArray();
                            foreach (var result in results)
                            {
                                resultArray.Push(result);
                            }

                            AddArrayMethods(resultArray);
                            resultPromise.Resolve(resultArray);
                        }
                    }
                }

                return resultPromise.JsObject;
            }));

            // Promise.race(iterable)
            hf.SetProperty("race", new HostFunction(args =>
            {
                if (args.Count == 0 || args[0] is not JsArray array)
                {
                    return null;
                }

                var resultPromise = new JsPromise(engine);
                AddPromiseInstanceMethods(resultPromise.JsObject, resultPromise, engine);

                var settled = false;

                foreach (var item in array.Items)
                    // Check if item is a promise (JsObject with "then" method)
                {
                    if (item is JsObject itemObj && itemObj.TryGetProperty("then", out var thenMethod) &&
                        thenMethod is IJsCallable thenCallable)
                    {
                        thenCallable.Invoke([
                            new HostFunction(resolveArgs =>
                            {
                                if (!settled)
                                {
                                    settled = true;
                                    resultPromise.Resolve(resolveArgs.Count > 0 ? resolveArgs[0] : null);
                                }

                                return null;
                            }),
                            new HostFunction(rejectArgs =>
                            {
                                if (!settled)
                                {
                                    settled = true;
                                    resultPromise.Reject(rejectArgs.Count > 0 ? rejectArgs[0] : null);
                                }

                                return null;
                            })
                        ], itemObj);
                    }
                    else if (!settled)
                    {
                        settled = true;
                        resultPromise.Resolve(item);
                    }
                }

                return resultPromise.JsObject;
            }));
        }

        return promiseConstructor;
    }

    /// <summary>
    /// Helper method to add instance methods to a promise.
    /// </summary>
    internal static void AddPromiseInstanceMethods(JsObject promiseObj, JsPromise promise, JsEngine engine)
    {
        promiseObj["then"] = new HostFunction((promiseThis, thenArgs) =>
        {
            var onFulfilled = thenArgs.Count > 0 ? thenArgs[0] as IJsCallable : null;
            var onRejected = thenArgs.Count > 1 ? thenArgs[1] as IJsCallable : null;
            var result = promise.Then(onFulfilled, onRejected);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        });

        promiseObj["catch"] = new HostFunction((promiseThis, catchArgs) =>
        {
            var onRejected = catchArgs.Count > 0 ? catchArgs[0] as IJsCallable : null;
            var result = promise.Then(null, onRejected);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        });

        promiseObj["finally"] = new HostFunction((promiseThis, finallyArgs) =>
        {
            var onFinally = finallyArgs.Count > 0 ? finallyArgs[0] as IJsCallable : null;
            if (onFinally == null)
            {
                return promiseObj;
            }

            var finallyWrapper = new HostFunction(wrapperArgs =>
            {
                onFinally.Invoke([], null);
                return wrapperArgs.Count > 0 ? wrapperArgs[0] : null;
            });

            var result = promise.Then(finallyWrapper, finallyWrapper);
            AddPromiseInstanceMethods(result.JsObject, result, engine);
            return result.JsObject;
        });
    }

    /// <summary>
    /// Creates a string wrapper object with string methods attached.
    /// This allows string primitives to have methods like toLowerCase(), substring(), etc.
    /// </summary>
    public static JsObject CreateStringWrapper(string str)
    {
        var stringObj = new JsObject();
        stringObj["__value__"] = str;
        stringObj["length"] = (double)str.Length;
        if (StringPrototype is not null)
        {
            stringObj.SetPrototype(StringPrototype);
        }
        AddStringMethods(stringObj, str);
        return stringObj;
    }

    /// <summary>
    /// Adds string methods to a string wrapper object.
    /// </summary>
    private static void AddStringMethods(JsObject stringObj, string str)
    {
        // charAt(index)
        stringObj.SetProperty("charAt", new HostFunction(args =>
        {
            var index = args.Count > 0 && args[0] is double d ? (int)d : 0;
            if (index < 0 || index >= str.Length)
            {
                return "";
            }

            return str[index].ToString();
        }));

        // charCodeAt(index)
        stringObj.SetProperty("charCodeAt", new HostFunction(args =>
        {
            var index = args.Count > 0 && args[0] is double d ? (int)d : 0;
            if (index < 0 || index >= str.Length)
            {
                return double.NaN;
            }

            return (double)str[index];
        }));

        // indexOf(searchString, position?)
        stringObj.SetProperty("indexOf", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return -1d;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? Math.Max(0, (int)d) : 0;
            var result = str.IndexOf(searchStr, position, StringComparison.Ordinal);
            return (double)result;
        }));

        // lastIndexOf(searchString, position?)
        stringObj.SetProperty("lastIndexOf", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return -1d;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? Math.Min((int)d, str.Length - 1) : str.Length - 1;
            var result = position >= 0 ? str.LastIndexOf(searchStr, position, StringComparison.Ordinal) : -1;
            return (double)result;
        }));

        // substring(start, end?)
        stringObj.SetProperty("substring", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return str;
            }

            var start = args[0] is double d1 ? Math.Max(0, Math.Min((int)d1, str.Length)) : 0;
            var end = args.Count > 1 && args[1] is double d2 ? Math.Max(0, Math.Min((int)d2, str.Length)) : str.Length;

            // JavaScript substring swaps if start > end
            if (start > end)
            {
                (start, end) = (end, start);
            }

            return str.Substring(start, end - start);
        }));

        // slice(start, end?)
        stringObj.SetProperty("slice", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return str;
            }

            var start = args[0] is double d1 ? (int)d1 : 0;
            var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : str.Length;

            // Handle negative indices
            if (start < 0)
            {
                start = Math.Max(0, str.Length + start);
            }
            else
            {
                start = Math.Min(start, str.Length);
            }

            if (end < 0)
            {
                end = Math.Max(0, str.Length + end);
            }
            else
            {
                end = Math.Min(end, str.Length);
            }

            if (start >= end)
            {
                return "";
            }

            return str.Substring(start, end - start);
        }));

        // substr(start, length?)
        stringObj.SetProperty("substr", new HostFunction(args =>
        {
            var length = str.Length;
            if (args.Count == 0)
            {
                return str;
            }

            var start = args[0] is double d1 ? (int)d1 : 0;
            if (start < 0)
            {
                start = Math.Max(0, length + start);
            }
            else if (start >= length)
            {
                return "";
            }

            int substrLength;
            if (args.Count > 1 && args[1] is double d2)
            {
                if (d2 <= 0)
                {
                    return "";
                }

                substrLength = (int)Math.Min(d2, length - start);
            }
            else
            {
                substrLength = length - start;
            }

            return str.Substring(start, substrLength);
        }));

        // concat(...strings)
        stringObj.SetProperty("concat", new HostFunction(args =>
        {
            var result = str;
            foreach (var arg in args)
            {
                result += JsValueToString(arg);
            }
            return result;
        }));

        // toLowerCase()
        stringObj.SetProperty("toLowerCase", new HostFunction(args => str.ToLowerInvariant()));

        // toUpperCase()
        stringObj.SetProperty("toUpperCase", new HostFunction(args => str.ToUpperInvariant()));

        // trim()
        stringObj.SetProperty("trim", new HostFunction(args => str.Trim()));

        // trimStart() / trimLeft()
        stringObj.SetProperty("trimStart", new HostFunction(args => str.TrimStart()));
        stringObj.SetProperty("trimLeft", new HostFunction(args => str.TrimStart()));

        // trimEnd() / trimRight()
        stringObj.SetProperty("trimEnd", new HostFunction(args => str.TrimEnd()));
        stringObj.SetProperty("trimRight", new HostFunction(args => str.TrimEnd()));

        // split(separator, limit?)
        stringObj.SetProperty("split", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return CreateArrayFromStrings([str]);
            }

            var separator = args[0]?.ToString();
            var limit = args.Count > 1 && args[1] is double d ? (int)d : int.MaxValue;

            if (separator is null or "")
            {
                // Split into individual characters
                var chars = str.Select(c => c.ToString()).Take(limit).ToArray();
                return CreateArrayFromStrings(chars);
            }

            var parts = str.Split([separator], StringSplitOptions.None);
            if (limit < parts.Length)
            {
                parts = parts.Take(limit).ToArray();
            }

            return CreateArrayFromStrings(parts);
        }));

        // replace(searchValue, replaceValue)
        stringObj.SetProperty("replace", new HostFunction(args =>
        {
            if (args.Count < 2)
            {
                return str;
            }

            var search = args[0];
            var replacement = args[1];

            // Function-replacer form: str.replace(pattern, (match) => ...)
            if (replacement is IJsCallable replacer)
            {
                // Regex search
                if (search is JsObject regexObj &&
                    regexObj.TryGetProperty("__regex__", out var regexValue) &&
                    regexValue is JsRegExp regex)
                {
                    var dotNetRegex = new System.Text.RegularExpressions.Regex(regex.Pattern);
                    var result = new System.Text.StringBuilder();
                    var lastIndex = 0;

                    if (regex.Global)
                    {
                        var matches = dotNetRegex.Matches(str);
                        if (matches.Count == 0)
                        {
                            return str;
                        }

                        foreach (System.Text.RegularExpressions.Match match in matches)
                        {
                            if (!match.Success)
                            {
                                continue;
                            }

                            if (match.Index > lastIndex)
                            {
                                result.Append(str.AsSpan(lastIndex, match.Index - lastIndex));
                            }

                            var replacementValue = replacer.Invoke([match.Value], str);
                            var replacementString = replacementValue.ToJsString();
                            result.Append(replacementString);

                            lastIndex = match.Index + match.Length;
                        }
                    }
                    else
                    {
                        var match = dotNetRegex.Match(str);
                        if (!match.Success)
                        {
                            return str;
                        }

                        if (match.Index > 0)
                        {
                            result.Append(str.AsSpan(0, match.Index));
                        }

                        var replacementValue = replacer.Invoke([match.Value], str);
                        var replacementString = replacementValue.ToJsString();
                        result.Append(replacementString);

                        lastIndex = match.Index + match.Length;
                    }

                    if (lastIndex < str.Length)
                    {
                        result.Append(str.AsSpan(lastIndex));
                    }

                    return result.ToString();
                }

                // String search with function replacer: only first occurrence
                var searchValueFunc = search?.ToString() ?? "";
                if (searchValueFunc.Length == 0)
                {
                    var replacementValue = replacer.Invoke([""], str);
                    var replacementString = replacementValue.ToJsString();
                    return replacementString + str;
                }

                var idx = str.IndexOf(searchValueFunc, StringComparison.Ordinal);
                if (idx < 0)
                {
                    return str;
                }

                var prefix = str.Substring(0, idx);
                var suffix = str.Substring(idx + searchValueFunc.Length);
                var replacedSegment = replacer.Invoke([searchValueFunc], str).ToJsString();
                return prefix + replacedSegment + suffix;
            }

            // Non-function replacer: existing behavior.

            // Check if first argument is a RegExp (JsObject with __regex__ property)
            if (search is JsObject regexObj2 && regexObj2.TryGetProperty("__regex__", out var regexValue2) &&
                regexValue2 is JsRegExp regex2)
            {
                var replaceValue = replacement?.ToString() ?? "";
                if (regex2.Global)
                {
                    return System.Text.RegularExpressions.Regex.Replace(str, regex2.Pattern, replaceValue);
                }

                var match = System.Text.RegularExpressions.Regex.Match(str, regex2.Pattern);
                if (match.Success)
                {
                    return string.Concat(str.AsSpan(0, match.Index), replaceValue, str.AsSpan(match.Index + match.Length));
                }

                return str;
            }

            // String replacement (only first occurrence)
            var searchValue = search?.ToString() ?? "";
            var replaceStr = replacement?.ToString() ?? "";
            var index = str.IndexOf(searchValue, StringComparison.Ordinal);
            if (index == -1)
            {
                return str;
            }

            return string.Concat(str.AsSpan(0, index), replaceStr, str.AsSpan(index + searchValue.Length));
        }));

        // match(regexp)
        stringObj.SetProperty("match", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            if (args[0] is JsObject regexObj && regexObj.TryGetProperty("__regex__", out var regexValue) &&
                regexValue is JsRegExp regex)
            {
                if (regex.Global)
                {
                    return regex.MatchAll(str);
                }

                return regex.Exec(str);
            }

            return null;
        }));

        // search(regexp)
        stringObj.SetProperty("search", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return -1d;
            }

            if (args[0] is JsObject regexObj && regexObj.TryGetProperty("__regex__", out var regexValue) &&
                regexValue is JsRegExp regex)
            {
                var result = regex.Exec(str);
                if (result is JsArray arr && arr.TryGetProperty("index", out var indexObj) &&
                    indexObj is double d)
                {
                    return d;
                }
            }

            return -1d;
        }));

        // startsWith(searchString, position?)
        stringObj.SetProperty("startsWith", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return true;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? (int)d : 0;
            if (position < 0 || position >= str.Length)
            {
                return false;
            }

            return str.Substring(position).StartsWith(searchStr, StringComparison.Ordinal);
        }));

        // endsWith(searchString, length?)
        stringObj.SetProperty("endsWith", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return true;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var length = args.Count > 1 && args[1] is double d ? (int)d : str.Length;
            if (length < 0)
            {
                return false;
            }

            length = Math.Min(length, str.Length);
            return str.Substring(0, length).EndsWith(searchStr, StringComparison.Ordinal);
        }));

        // includes(searchString, position?)
        stringObj.SetProperty("includes", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return true;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? Math.Max(0, (int)d) : 0;
            if (position >= str.Length)
            {
                return searchStr == "";
            }

            return str.IndexOf(searchStr, position, StringComparison.Ordinal) >= 0;
        }));

        // repeat(count)
        stringObj.SetProperty("repeat", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not double d)
            {
                return "";
            }

            var count = (int)d;
            if (count is < 0 or int.MaxValue)
            {
                return ""; // JavaScript throws RangeError, we return empty
            }

            if (count == 0)
            {
                return "";
            }

            return string.Concat(Enumerable.Repeat(str, count));
        }));

        // padStart(targetLength, padString?)
        stringObj.SetProperty("padStart", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return str;
            }

            var targetLength = args[0] is double d ? (int)d : 0;
            if (targetLength <= str.Length)
            {
                return str;
            }

            var padString = args.Count > 1 ? args[1]?.ToString() ?? " " : " ";
            if (padString == "")
            {
                return str;
            }

            var padLength = targetLength - str.Length;
            var padCount = (int)Math.Ceiling((double)padLength / padString.Length);
            var padding = string.Concat(Enumerable.Repeat(padString, padCount));
            return string.Concat(padding.AsSpan(0, padLength), str);
        }));

        // padEnd(targetLength, padString?)
        stringObj.SetProperty("padEnd", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return str;
            }

            var targetLength = args[0] is double d ? (int)d : 0;
            if (targetLength <= str.Length)
            {
                return str;
            }

            var padString = args.Count > 1 ? args[1]?.ToString() ?? " " : " ";
            if (padString == "")
            {
                return str;
            }

            var padLength = targetLength - str.Length;
            var padCount = (int)Math.Ceiling((double)padLength / padString.Length);
            var padding = string.Concat(Enumerable.Repeat(padString, padCount));
            return string.Concat(str, padding.AsSpan(0, padLength));
        }));

        // replaceAll(searchValue, replaceValue)
        stringObj.SetProperty("replaceAll", new HostFunction(args =>
        {
            if (args.Count < 2)
            {
                return str;
            }

            var searchValue = args[0]?.ToString() ?? "";
            var replaceValue = args[1]?.ToString() ?? "";
            return str.Replace(searchValue, replaceValue);
        }));

        // at(index)
        stringObj.SetProperty("at", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            if (args[0] is not double d)
            {
                return null;
            }

            var index = (int)d;
            // Handle negative indices
            if (index < 0)
            {
                index = str.Length + index;
            }

            if (index < 0 || index >= str.Length)
            {
                return null;
            }

            return str[index].ToString();
        }));

        // trimStart() / trimLeft()
        stringObj.SetProperty("trimStart", new HostFunction(args => str.TrimStart()));
        stringObj.SetProperty("trimLeft", new HostFunction(args => str.TrimStart()));

        // trimEnd() / trimRight()
        stringObj.SetProperty("trimEnd", new HostFunction(args => str.TrimEnd()));
        stringObj.SetProperty("trimRight", new HostFunction(args => str.TrimEnd()));

        // codePointAt(index)
        stringObj.SetProperty("codePointAt", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not double d)
            {
                return null;
            }

            var index = (int)d;
            if (index < 0 || index >= str.Length)
            {
                return null;
            }

            // Get the code point at the given position
            // Handle surrogate pairs for characters outside the BMP (Basic Multilingual Plane)
            var c = str[index];
            if (char.IsHighSurrogate(c) && index + 1 < str.Length)
            {
                var low = str[index + 1];
                if (char.IsLowSurrogate(low))
                {
                    // Calculate the code point from the surrogate pair
                    var high = (int)c;
                    var lowInt = (int)low;
                    var codePoint = ((high - 0xD800) << 10) + (lowInt - 0xDC00) + 0x10000;
                    return (double)codePoint;
                }
            }

            return (double)c;
        }));

        // localeCompare(compareString)
        stringObj.SetProperty("localeCompare", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return 0d;
            }

            var compareString = args[0]?.ToString() ?? "";
            var result = string.Compare(str, compareString, StringComparison.CurrentCulture);
            return (double)result;
        }));

        // normalize(form) - Unicode normalization
        stringObj.SetProperty("normalize", new HostFunction(args =>
        {
            var form = args.Count > 0 && args[0] != null ? args[0]!.ToString() : "NFC";

            try
            {
                return form switch
                {
                    "NFC" => str.Normalize(System.Text.NormalizationForm.FormC),
                    "NFD" => str.Normalize(System.Text.NormalizationForm.FormD),
                    "NFKC" => str.Normalize(System.Text.NormalizationForm.FormKC),
                    "NFKD" => str.Normalize(System.Text.NormalizationForm.FormKD),
                    _ => throw new Exception(
                        "RangeError: The normalization form should be one of NFC, NFD, NFKC, NFKD.")
                };
            }
            catch
            {
                return str;
            }
        }));

        // matchAll(regexp) - returns an array of all matches
        stringObj.SetProperty("matchAll", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            if (args[0] is JsObject regexObj && regexObj.TryGetProperty("__regex__", out var regexValue) &&
                regexValue is JsRegExp regex)
            {
                return regex.MatchAll(str);
            }

            // If not a RegExp, convert to one
            var pattern = args[0]?.ToString() ?? "";
            var tempRegex = new JsRegExp(pattern, "g");
            return tempRegex.MatchAll(str);
        }));

        // anchor(name) - deprecated HTML wrapper method
        stringObj.SetProperty("anchor", new HostFunction(args =>
        {
            var name = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            // Escape quotes in name
            name = name.Replace("\"", "&quot;");
            return $"<a name=\"{name}\">{str}</a>";
        }));

        // link(url) - deprecated HTML wrapper method
        stringObj.SetProperty("link", new HostFunction(args =>
        {
            var url = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            // Escape quotes in url
            url = url.Replace("\"", "&quot;");
            return $"<a href=\"{url}\">{str}</a>";
        }));

        // Set up Symbol.iterator for string
        var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

        // Create iterator function that returns an iterator object
        var iteratorFunction = new HostFunction((thisValue, args) =>
        {
            // Use array to hold index so it can be mutated in closure
            var indexHolder = new int[] { 0 };
            var iterator = new JsObject();

            // Add next() method to iterator
            iterator.SetProperty("next", new HostFunction((nextThisValue, nextArgs) =>
            {
                var result = new JsObject();
                if (indexHolder[0] < str.Length)
                {
                    result.SetProperty("value", str[indexHolder[0]].ToString());
                    result.SetProperty("done", false);
                    indexHolder[0]++;
                }
                else
                {
                    result.SetProperty("value", Symbols.Undefined);
                    result.SetProperty("done", true);
                }

                return result;
            }));

            return iterator;
        });

        stringObj.SetProperty(iteratorKey, iteratorFunction);
    }

    /// <summary>
    /// Creates a wrapper object for a number primitive, providing access to Number.prototype methods.
    /// </summary>
    public static JsObject CreateNumberWrapper(double num)
    {
        var numberObj = new JsObject();
        numberObj["__value__"] = num;
        if (NumberPrototype is not null)
        {
            numberObj.SetPrototype(NumberPrototype);
        }
        AddNumberMethods(numberObj, num);
        return numberObj;
    }

    public static JsObject CreateBigIntWrapper(JsBigInt value)
    {
        var wrapper = new JsObject
        {
            ["__value__"] = value
        };

        if (BigIntPrototype is not null)
        {
            wrapper.SetPrototype(BigIntPrototype);
        }

        return wrapper;
    }

    /// <summary>
    /// Adds number methods to a number wrapper object.
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
                    return ((long)num).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                return num.ToString(System.Globalization.CultureInfo.InvariantCulture);
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

            var digits = "0123456789abcdefghijklmnopqrstuvwxyz";
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

            return num.ToString("F" + fractionDigits, System.Globalization.CultureInfo.InvariantCulture);
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

            if (args.Count > 0 && args[0] is double d)
            {
                var fractionDigits = (int)d;
                if (fractionDigits is < 0 or > 100)
                {
                    throw new ArgumentException("toExponential() digits argument must be between 0 and 100");
                }

                return num.ToString("e" + fractionDigits, System.Globalization.CultureInfo.InvariantCulture);
            }

            return num.ToString("e", System.Globalization.CultureInfo.InvariantCulture);
        }));

        // toPrecision(precision?)
        numberObj.SetProperty("toPrecision", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return num.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (double.IsNaN(num))
            {
                return "NaN";
            }

            if (double.IsInfinity(num))
            {
                return num > 0 ? "Infinity" : "-Infinity";
            }

            if (args[0] is double d)
            {
                var precision = (int)d;
                if (precision is < 1 or > 100)
                {
                    throw new ArgumentException("toPrecision() precision argument must be between 1 and 100");
                }

                // Format with specified precision
                return num.ToString("G" + precision, System.Globalization.CultureInfo.InvariantCulture);
            }

            return num.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }));

        // valueOf()
        numberObj.SetProperty("valueOf", new HostFunction(args => num));
    }

    private static JsArray CreateArrayFromStrings(string[] strings)
    {
        var array = new JsArray();
        foreach (var s in strings)
        {
            array.Push(s);
        }

        AddArrayMethods(array);
        return array;
    }

    private static bool TryGetObject(object candidate, out IJsObjectLike accessor)
    {
        switch (candidate)
        {
            case null:
            case Symbol sym when ReferenceEquals(sym, Symbols.Undefined):
                accessor = null!;
                return false;
            case IJsObjectLike a:
                accessor = a;
                return true;
            case bool b:
                accessor = CreateBooleanWrapper(b);
                return true;
            case string s:
                accessor = CreateStringWrapper(s);
                return true;
            case JsBigInt bigInt:
                accessor = CreateBigIntWrapper(bigInt);
                return true;
            case double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte:
                accessor = CreateNumberWrapper(JsOps.ToNumber(candidate));
                return true;
            default:
                accessor = null!;
                return false;
        }
    }

    /// <summary>
    /// Creates the Object constructor with static methods.
    /// </summary>
    public static HostFunction CreateObjectConstructor(Runtime.RealmState realm)
    {
        // Object constructor function
        var objectConstructor = new HostFunction(args =>
        {
            JsObject CreateBlank()
            {
                var obj = new JsObject();
                var proto = realm.ObjectPrototype ?? ObjectPrototype;
                if (proto is not null)
                {
                    obj.SetPrototype(proto);
                }

                return obj;
            }

            // Object() or Object(value) - creates a new object or wraps the value
            if (args.Count == 0 || args[0] == null || args[0] == Symbols.Undefined)
            {
                return CreateBlank();
            }
            // If value is already an object, return it as-is
            if (args[0] is JsObject jsObj)
            {
                return jsObj;
            }

            var value = args[0];
            switch (value)
            {
                case JsBigInt bigInt:
                    return CreateBigIntWrapper(bigInt);
                case bool b:
                    return CreateBooleanWrapper(b);
                case string s:
                    return CreateStringWrapper(s);
                case double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte:
                    return CreateNumberWrapper(JsOps.ToNumber(value));
            }

            return CreateBlank();
        });

        // Capture Object.prototype so Object.prototype methods can be attached
        // and used with call/apply patterns.
        if (objectConstructor.TryGetProperty("prototype", out var objectProto) &&
            objectProto is JsObject objectProtoObj)
        {
            realm.ObjectPrototype ??= objectProtoObj;
            ObjectPrototype ??= objectProtoObj;

            if (realm.FunctionPrototype is not null)
            {
                realm.FunctionPrototype.SetPrototype(objectProtoObj);
            }

            if (realm.BooleanPrototype is not null)
            {
                realm.BooleanPrototype.SetPrototype(objectProtoObj);
            }

            if (realm.NumberPrototype is not null)
            {
                realm.NumberPrototype.SetPrototype(objectProtoObj);
            }

            if (realm.StringPrototype is not null)
            {
                realm.StringPrototype.SetPrototype(objectProtoObj);
            }

            objectProtoObj.DefineProperty("constructor", new PropertyDescriptor
            {
                Value = objectConstructor,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });

            if (realm.ErrorPrototype is not null && realm.ErrorPrototype.Prototype is null)
            {
                realm.ErrorPrototype.SetPrototype(objectProtoObj);
            }

            // Object.prototype.toString
            var objectToString = new HostFunction((thisValue, args) =>
            {
                var tag = thisValue switch
                {
                    null => "Null",
                    JsObject => "Object",
                    JsArray => "Array",
                    string => "String",
                    double => "Number",
                    bool => "Boolean",
                    IJsCallable => "Function",
                    _ when ReferenceEquals(thisValue, Symbols.Undefined) => "Undefined",
                    _ => "Object"
                };

                return $"[object {tag}]";
            });

            objectProtoObj.SetProperty("toString", objectToString);

            // Object.prototype.hasOwnProperty
            var hasOwn = new HostFunction((thisValue, args) =>
            {
                if (args.Count == 0)
                {
                    return false;
                }

                var propertyName = JsOps.ToPropertyName(args[0]);
                if (propertyName is null)
                {
                    return false;
                }

                switch (thisValue)
                {
                    case JsObject obj:
                        // Only own properties; JsObject.ContainsKey checks own keys.
                        return obj.ContainsKey(propertyName);
                    case JsArray array:
                        if (string.Equals(propertyName, "length", StringComparison.Ordinal))
                        {
                            return true;
                        }

                        if (JsOps.TryResolveArrayIndex(propertyName, out var index))
                        {
                            return array.HasOwnIndex(index);
                        }

                        return false;
                    case IJsObjectLike accessor:
                        return accessor.GetOwnPropertyDescriptor(propertyName) is not null;
                    default:
                        return false;
                }
            });

            objectProtoObj.SetProperty("hasOwnProperty", hasOwn);

            // Object.prototype.propertyIsEnumerable
            var propertyIsEnumerable = new HostFunction((thisValue, args) =>
            {
                if (args.Count == 0)
                {
                    return false;
                }

                var propertyName = JsOps.ToPropertyName(args[0]);
                if (propertyName is null)
                {
                    return false;
                }

                if (thisValue is not IJsObjectLike accessor)
                {
                    return false;
                }

                var desc = accessor.GetOwnPropertyDescriptor(propertyName);
                return desc is not null && desc.Enumerable;
            });

            objectProtoObj.SetProperty("propertyIsEnumerable", propertyIsEnumerable);

            // Object.prototype.isPrototypeOf
            var isPrototypeOf = new HostFunction((thisValue, args) =>
            {
                if (thisValue is null || ReferenceEquals(thisValue, Symbols.Undefined))
                {
                    var error = TypeErrorConstructor is IJsCallable ctor
                        ? ctor.Invoke(["Object.prototype.isPrototypeOf called on null or undefined"], null)
                        : new InvalidOperationException(
                            "Object.prototype.isPrototypeOf called on null or undefined");
                    throw new ThrowSignal(error);
                }

                if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbols.Undefined))
                {
                    return false;
                }

                if (args[0] is not IJsObjectLike objectLike)
                {
                    return false;
                }

                var cursor = objectLike;
                while (cursor.Prototype is JsObject proto)
                {
                    if (ReferenceEquals(proto, thisValue))
                    {
                        return true;
                    }

                    if (proto is not IJsObjectLike next)
                    {
                        break;
                    }

                    cursor = next;
                }

                return false;
            });

            objectProtoObj.SetProperty("isPrototypeOf", isPrototypeOf);

            // Also expose Object.hasOwnProperty so patterns like
            // Object.hasOwnProperty.call(obj, key) behave as expected.
            objectConstructor.SetProperty("hasOwnProperty", hasOwn);
        }

        objectConstructor.SetProperty("defineProperty", new HostFunction((thisValue, args) =>
        {
            if (args.Count < 3)
            {
                return args.Count > 0 ? args[0] : Symbols.Undefined;
            }

            var target = args[0];
            var propertyKey = args[1];
            var descriptorValue = args[2];

            if (target is not IJsPropertyAccessor accessor)
            {
                return args[0];
            }

            var name = JsOps.ToPropertyName(propertyKey) ?? string.Empty;

            if (descriptorValue is JsObject descObj)
            {
                // If an accessor is provided, eagerly evaluate the getter once
                // and store the resulting value. This approximates accessor
                // behaviour for the patterns used in chalk/debug without
                // requiring full descriptor support on all host objects.
                if (descObj.TryGetProperty("get", out var getterVal) && getterVal is IJsCallable getterFn)
                {
                    var builder = getterFn.Invoke(Array.Empty<object?>(), target);
                    accessor.SetProperty(name, builder);
                    return args[0];
                }

                if (descObj.TryGetProperty("value", out var value))
                {
                    accessor.SetProperty(name, value);
                    return args[0];
                }
            }

            accessor.SetProperty(name, Symbols.Undefined);
            return args[0];
        }));

        objectConstructor.SetProperty("defineProperties", new HostFunction((thisValue, args) =>
        {
            if (args.Count < 2)
            {
                return args.Count > 0 ? args[0] : Symbols.Undefined;
            }

            var target = args[0];
            var propsValue = args[1];

            if (target is not IJsPropertyAccessor accessor || propsValue is not JsObject props)
            {
                return args[0];
            }

            foreach (var key in props.GetOwnPropertyNames())
            {
                if (!props.TryGetProperty(key, out var descriptorValue) || descriptorValue is not JsObject descObj)
                {
                    continue;
                }

                if (descObj.TryGetProperty("get", out var getterVal) && getterVal is IJsCallable getterFn)
                {
                    var builder = getterFn.Invoke(Array.Empty<object?>(), target);
                    accessor.SetProperty(key, builder);
                    continue;
                }

                if (descObj.TryGetProperty("value", out var value))
                {
                    accessor.SetProperty(key, value);
                    continue;
                }

                accessor.SetProperty(key, Symbols.Undefined);
            }

            return args[0];
        }));

        objectConstructor.SetProperty("setPrototypeOf", new HostFunction((thisValue, args) =>
        {
            if (args.Count < 2)
            {
                return args.Count > 0 ? args[0] : Symbols.Undefined;
            }

            var target = args[0];
            var protoValue = args[1];
            var proto = protoValue as JsObject;

            switch (target)
            {
                case JsObject obj:
                    obj.SetPrototype(proto);
                    break;
                case JsArray array:
                    array.SetPrototype(proto);
                    break;
            }

            return target;
        }));

        objectConstructor.SetProperty("preventExtensions", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not IJsObjectLike target)
            {
                var error = TypeErrorConstructor is IJsCallable ctor
                    ? ctor.Invoke(["Object.preventExtensions requires an object"], null)
                    : new InvalidOperationException("Object.preventExtensions requires an object.");
                throw new ThrowSignal(error);
            }

            target.Seal();
            return target;
        }));

        objectConstructor.SetProperty("isExtensible", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not IJsObjectLike target)
            {
                return false;
            }

            return !target.IsSealed;
        }));

        objectConstructor.SetProperty("getOwnPropertySymbols", new HostFunction(args =>
        {
            // The engine currently uses internal string keys for symbol
            // properties on JsObject instances (\"@@symbol:...\"), and Babel
            // only uses getOwnPropertySymbols in cleanup paths (e.g. to
            // null-out metadata). Returning an empty array here avoids
            // observable behaviour differences while keeping the API
            // available for callers.
            return new JsArray();
        }));

        // Object.keys(obj)
        objectConstructor.SetProperty("keys", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return new JsArray();
            }

            var keys = new JsArray();
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                keys.Push(key);
            }

            AddArrayMethods(keys);
            return keys;
        }));

        // Object.values(obj)
        objectConstructor.SetProperty("values", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return new JsArray();
            }

            var values = new JsArray();
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                if (obj.TryGetValue(key, out var value))
                {
                    values.Push(value);
                }
            }

            AddArrayMethods(values);
            return values;
        }));

        // Object.entries(obj)
        objectConstructor.SetProperty("entries", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return new JsArray();
            }

            var entries = new JsArray();
            foreach (var key in obj.GetEnumerablePropertyNames())
            {
                if (obj.TryGetValue(key, out var value))
                {
                    var entry = new JsArray([key, value]);
                    AddArrayMethods(entry);
                    entries.Push(entry);
                }
            }

            AddArrayMethods(entries);
            return entries;
        }));

        // Object.assign(target, ...sources)
        objectConstructor.SetProperty("assign", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not IJsPropertyAccessor targetAccessor)
            {
                return args.Count > 0 ? args[0] : Symbols.Undefined;
            }

            for (var i = 1; i < args.Count; i++)
            {
                if (args[i] is not JsObject source)
                {
                    continue;
                }

                foreach (var key in source.GetOwnPropertyNames())
                {
                    if (source.TryGetProperty(key, out var value))
                    {
                        targetAccessor.SetProperty(key, value);
                    }
                }
            }

            return args[0];
        }));

        // Object.fromEntries(entries)
        objectConstructor.SetProperty("fromEntries", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsArray entries)
            {
                return new JsObject();
            }

            var result = new JsObject();
            foreach (var entry in entries.Items)
            {
                if (entry is JsArray { Items.Count: >= 2 } entryArray)
                {
                    var key = entryArray.GetElement(0)?.ToString() ?? "";
                    var value = entryArray.GetElement(1);
                    result[key] = value;
                }
            }

            return result;
        }));

        // Object.hasOwn(obj, prop)
        objectConstructor.SetProperty("hasOwn", new HostFunction(args =>
        {
            if (args.Count < 2 || args[0] is not JsObject obj)
            {
                return false;
            }

            var propName = args[1]?.ToString() ?? "";
            return obj.ContainsKey(propName);
        }));

        // Object.freeze(obj)
        objectConstructor.SetProperty("freeze", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return args.Count > 0 ? args[0] : null;
            }

            obj.Freeze();
            return obj;
        }));

        // Object.seal(obj)
        objectConstructor.SetProperty("seal", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return args.Count > 0 ? args[0] : null;
            }

            obj.Seal();
            return obj;
        }));

        // Object.isFrozen(obj)
        objectConstructor.SetProperty("isFrozen", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return true; // Non-objects are considered frozen
            }

            return obj.IsFrozen;
        }));

        // Object.isSealed(obj)
        objectConstructor.SetProperty("isSealed", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not JsObject obj)
            {
                return true; // Non-objects are considered sealed
            }

            return obj.IsSealed;
        }));

        // Object.create(proto, propertiesObject)
        objectConstructor.SetProperty("create", new HostFunction(args =>
        {
            var obj = new JsObject();
            if (args.Count > 0 && args[0] != null)
            {
                obj.SetPrototype(args[0]);
            }

            // Handle second parameter: property descriptors
            if (args.Count > 1 && args[1] is JsObject propsObj)
            {
                foreach (var propName in propsObj.GetOwnPropertyNames())
                {
                    if (propsObj.TryGetValue(propName, out var descriptorObj) && descriptorObj is JsObject descObj)
                    {
                        var descriptor = new PropertyDescriptor();

                        // Check if this is an accessor descriptor
                        var hasGet = descObj.TryGetValue("get", out var getVal);
                        var hasSet = descObj.TryGetValue("set", out var setVal);

                        if (hasGet || hasSet)
                        {
                            // Accessor descriptor
                            if (hasGet && getVal is IJsCallable getter)
                            {
                                descriptor.Get = getter;
                            }

                            if (hasSet && setVal is IJsCallable setter)
                            {
                                descriptor.Set = setter;
                            }
                        }
                        else
                        {
                            // Data descriptor
                            if (descObj.TryGetValue("value", out var value))
                            {
                                descriptor.Value = value;
                            }

                            if (descObj.TryGetValue("writable", out var writableVal))
                            {
                                descriptor.Writable = writableVal is bool b ? b : ToBoolean(writableVal);
                            }
                        }

                        // Common properties
                        if (descObj.TryGetValue("enumerable", out var enumerableVal))
                        {
                            descriptor.Enumerable = enumerableVal is bool b ? b : ToBoolean(enumerableVal);
                        }
                        else
                        {
                            descriptor.Enumerable = false; // Default is false for Object.create
                        }

                        if (descObj.TryGetValue("configurable", out var configurableVal))
                        {
                            descriptor.Configurable = configurableVal is bool b ? b : ToBoolean(configurableVal);
                        }
                        else
                        {
                            descriptor.Configurable = false; // Default is false for Object.create
                        }

                        obj.DefineProperty(propName, descriptor);
                    }
                }
            }

            return obj;
        }));

        // Object.getOwnPropertyNames(obj)
        objectConstructor.SetProperty("getOwnPropertyNames", new HostFunction(args =>
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, out var obj))
            {
                return new JsArray();
            }

            var names = new JsArray(obj.GetOwnPropertyNames());

            AddArrayMethods(names);
            return names;
        }));

        // Object.getOwnPropertyDescriptor(obj, prop)
        objectConstructor.SetProperty("getOwnPropertyDescriptor", new HostFunction(args =>
        {
            if (args.Count < 2 || !TryGetObject(args[0]!, out var obj))
            {
                return Symbols.Undefined;
            }

            var propName = args[1]?.ToString() ?? "";

            var desc = obj.GetOwnPropertyDescriptor(propName);
            if (desc == null)
            {
                return Symbols.Undefined;
            }

            var resultDesc = new JsObject();
            if (desc.IsAccessorDescriptor)
            {
                if (desc.Get != null)
                {
                    resultDesc["get"] = desc.Get;
                }

                if (desc.Set != null)
                {
                    resultDesc["set"] = desc.Set;
                }
            }
            else
            {
                resultDesc["value"] = desc.Value;
                resultDesc["writable"] = desc.Writable;
            }

            resultDesc["enumerable"] = desc.Enumerable;
            resultDesc["configurable"] = desc.Configurable;

            return resultDesc;
        }));

        // Object.getPrototypeOf(obj)
        objectConstructor.SetProperty("getPrototypeOf", new HostFunction(args =>
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, out var obj))
            {
                throw ThrowTypeError("Object.getPrototypeOf called on null or undefined");
            }

            var proto = obj.Prototype ?? (object?)Symbols.Undefined;
            if (proto is not JsObject &&
                obj is HostFunction hostFunction &&
                hostFunction.Realm is JsObject realm &&
                realm.TryGetProperty("Function", out var fnVal) &&
                fnVal is IJsPropertyAccessor fnAccessor &&
                fnAccessor.TryGetProperty("prototype", out var fnProtoObj) &&
                fnProtoObj is JsObject fnProto)
            {
                proto = fnProto;
            }

            return proto;
        }));

        // Object.defineProperty(obj, prop, descriptor)
        objectConstructor.SetProperty("defineProperty", new HostFunction(args =>
        {
            if (args.Count < 3 || !TryGetObject(args[0]!, out var obj))
            {
                return args.Count > 0 ? args[0] : null;
            }

            var propName = JsOps.ToPropertyName(args[1]) ?? string.Empty;

            if (args[2] is JsObject descriptorObj)
            {
                var descriptor = new PropertyDescriptor();

                // Check if this is an accessor descriptor
                var hasGet = descriptorObj.TryGetValue("get", out var getVal);
                var hasSet = descriptorObj.TryGetValue("set", out var setVal);

                if (hasGet || hasSet)
                {
                    // Accessor descriptor
                    if (hasGet && getVal is IJsCallable getter)
                    {
                        descriptor.Get = getter;
                    }

                    if (hasSet && setVal is IJsCallable setter)
                    {
                        descriptor.Set = setter;
                    }
                }
                else
                {
                    // Data descriptor
                    if (descriptorObj.TryGetValue("value", out var value))
                    {
                        descriptor.Value = value;
                    }

                    if (descriptorObj.TryGetValue("writable", out var writableVal))
                    {
                        descriptor.Writable = writableVal is bool b ? b : ToBoolean(writableVal);
                    }
                }

                // Common properties
                if (descriptorObj.TryGetValue("enumerable", out var enumerableVal))
                {
                    descriptor.Enumerable = enumerableVal is bool b ? b : ToBoolean(enumerableVal);
                }

                if (descriptorObj.TryGetValue("configurable", out var configurableVal))
                {
                    descriptor.Configurable = configurableVal is bool b ? b : ToBoolean(configurableVal);
                }

                if (obj is JsArray jsArray && string.Equals(propName, "length", StringComparison.Ordinal))
                {
                    jsArray.DefineLength(descriptor, null, throwOnWritableFailure: true);
                }
                else
                {
                    obj.DefineProperty(propName, descriptor);
                }
            }

            return args[0];
        }));

        return objectConstructor;
    }

    /// <summary>
    /// Creates the Array constructor with static methods.
    /// </summary>
    public static HostFunction CreateArrayConstructor(Runtime.RealmState realm)
    {
        JsObject? arrayPrototype = null;

        // Array constructor
        var arrayConstructor = new HostFunction((thisValue, args) =>
        {
            // Use provided receiver when available so Reflect.construct can
            // control allocation and prototype.
            var instance = thisValue as JsArray ?? new JsArray();

            // Honor an explicit prototype on the receiver; otherwise fall back
            // to the constructor's prototype if available.
            if (thisValue is JsObject thisObj && thisObj.Prototype is JsObject providedProto)
            {
                instance.SetPrototype(providedProto);
            }
            else if (instance.Prototype is null && arrayPrototype is not null)
            {
                instance.SetPrototype(arrayPrototype);
            }

            // Array(length) or Array(element0, element1, ...)
            if (args is [double length])
            {
                instance.SetProperty("length", length);
                AddArrayMethods(instance, instance.Prototype);
                return instance;
            }

            foreach (var value in args)
            {
                instance.Push(value);
            }

            AddArrayMethods(instance, instance.Prototype);
            return instance;
        });

        arrayConstructor.RealmState = realm;
        realm.ArrayConstructor ??= arrayConstructor;
        ArrayConstructor ??= arrayConstructor;

        // Ensure Array.[[Prototype]] is %FunctionPrototype% even if the shared
        // prototype was not available when the HostFunction was created.
        if (realm.FunctionPrototype is not null)
        {
            arrayConstructor.Properties.SetPrototype(realm.FunctionPrototype);
        }

        // Array.isArray(value)
        var isArrayFn = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return false;
            }

            var candidate = args[0];
            while (candidate is JsProxy proxy)
            {
                if (proxy.Handler is null)
                {
                    var error = TypeErrorConstructor is IJsCallable ctor
                        ? ctor.Invoke(["Cannot perform 'isArray' with a revoked Proxy"], null)
                        : new InvalidOperationException("Cannot perform 'isArray' with a revoked Proxy.");
                    throw new ThrowSignal(error);
                }

                candidate = proxy.Target;
            }

            if (candidate is JsArray jsArray)
            {
                if (jsArray.TryGetProperty("__arguments__", out var isArgs) && isArgs is true)
                {
                    return false;
                }

                return true;
            }

            if (candidate is JsObject obj && ArrayPrototype is not null &&
                ReferenceEquals(obj, ArrayPrototype))
            {
                return true;
            }

            return false;
        });

        isArrayFn.DefineProperty("name", new PropertyDescriptor
        {
            Value = "isArray",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        isArrayFn.DefineProperty("length", new PropertyDescriptor
        {
            Value = 1d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        isArrayFn.IsConstructor = false;

        arrayConstructor.DefineProperty("isArray", new PropertyDescriptor
        {
            Value = isArrayFn,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });

        // Array.from(arrayLike)
        HostFunction arrayFrom = null!;
        arrayFrom = new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbols.Undefined))
            {
                var error = TypeErrorConstructor is IJsCallable ctor
                    ? ctor.Invoke(["Array.from requires an array-like or iterable"], null)
                    : new InvalidOperationException("Array.from requires an array-like or iterable.");
                throw new ThrowSignal(error);
            }

            var source = args[0]!;
            var mapfn = args.Count > 1 ? args[1] : null;
            var thisArg = args.Count > 2 ? args[2] : Symbols.Undefined;
            var callingEnv = arrayFrom.CallingJsEnvironment;

            if (mapfn is not null && mapfn is not IJsCallable)
            {
                var error = TypeErrorConstructor is IJsCallable ctor2
                    ? ctor2.Invoke(["Array.from: when provided, the mapping callback must be callable"], null)
                    : new InvalidOperationException("Array.from: when provided, the mapping callback must be callable.");
                throw new ThrowSignal(error);
            }

            static double ToLength(object? value)
            {
                while (true)
                {
                    if (value is double d)
                    {
                        if (double.IsNaN(d) || d <= 0)
                        {
                            return 0;
                        }

                        if (double.IsPositiveInfinity(d))
                        {
                            return double.MaxValue;
                        }

                        return Math.Floor(d);
                    }

                    if (value is int i)
                    {
                        return i < 0 ? 0 : i;
                    }

                    if (value is string s && double.TryParse(s, out var parsed))
                    {
                        value = parsed;
                        continue;
                    }

                    return 0;
                }
            }

            static object? GetAt(object target, int index)
            {
                var key = index.ToString(CultureInfo.InvariantCulture);
                if (target is JsArray jsArr)
                {
                    return index < jsArr.Items.Count ? jsArr.GetElement(index) : Symbols.Undefined;
                }

                if (target is string str)
                {
                    return index < str.Length ? str[index].ToString() : Symbols.Undefined;
                }

                if (target is JsObject jsObj && jsObj.TryGetProperty(key, out var value))
                {
                    return value;
                }

                return Symbols.Undefined;
            }

            static bool TryGetIteratorMethod(object sourceObj, out IJsCallable? method)
            {
                var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
                var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";
                method = null;
                if (sourceObj is IJsPropertyAccessor accessor &&
                    accessor.TryGetProperty(iteratorKey, out var value) &&
                    !ReferenceEquals(value, Symbols.Undefined))
                {
                    method = value as IJsCallable;
                }

                return method is not null;
            }

            static void CreateDataPropertyOrThrow(IJsObjectLike target, string propertyKey, object? value,
                IJsCallable? typeErrorCtor)
            {
                var existing = target.GetOwnPropertyDescriptor(propertyKey);
                if (existing is null)
                {
                    if (target.IsSealed)
                    {
                        var error = typeErrorCtor is not null
                            ? typeErrorCtor.Invoke([$"Cannot define property {propertyKey} on a sealed object"], null)
                            : new InvalidOperationException(
                                $"Cannot define property {propertyKey} on a sealed object");
                        throw new ThrowSignal(error);
                    }
                }
                else if (!existing.Configurable)
                {
                    if (existing.IsAccessorDescriptor && existing.Set is null)
                    {
                        var error = typeErrorCtor is not null
                            ? typeErrorCtor.Invoke([$"Property {propertyKey} is non-writable"], null)
                            : new InvalidOperationException($"Property {propertyKey} is non-writable");
                        throw new ThrowSignal(error);
                    }

                    if (!existing.Writable)
                    {
                        var error = typeErrorCtor is not null
                            ? typeErrorCtor.Invoke([$"Property {propertyKey} is non-writable"], null)
                            : new InvalidOperationException($"Property {propertyKey} is non-writable");
                        throw new ThrowSignal(error);
                    }
                }

                var descriptor = new PropertyDescriptor
                {
                    Value = value,
                    Writable = true,
                    Enumerable = true,
                    Configurable = true
                };

                target.DefineProperty(propertyKey, descriptor);

                var defined = target.GetOwnPropertyDescriptor(propertyKey);
                if (defined is null || !defined.Writable || !defined.Enumerable || !defined.Configurable)
                {
                    var error = typeErrorCtor is not null
                        ? typeErrorCtor.Invoke([$"Failed to create data property {propertyKey}"], null)
                        : new InvalidOperationException($"Failed to create data property {propertyKey}");
                    throw new ThrowSignal(error);
                }
            }

            var constructor = thisValue as IJsCallable;
            var useConstructor = constructor is not null &&
                                 (constructor is not HostFunction hostFn || hostFn.IsConstructor);
            if (!useConstructor)
            {
                constructor = ArrayConstructor;
            }

            var lengthValue = source switch
            {
                string str => (double)str.Length,
                JsArray arr => (double)arr.Length,
                JsObject obj when obj.TryGetProperty("length", out var lenVal) => lenVal,
                _ => 0d
            };
            var len = ToLength(lengthValue);
            var lengthInt = len > int.MaxValue ? int.MaxValue : (int)len;

            IJsObjectLike result;
            if (constructor is HostFunction targetCtor && ReferenceEquals(targetCtor, ArrayConstructor))
            {
                var array = new JsArray();
                if (arrayPrototype is not null)
                {
                    array.SetPrototype(arrayPrototype);
                }

                array.SetProperty("length", (double)lengthInt);
                AddArrayMethods(array, arrayPrototype);
                result = array;
            }
            else
            {
                IJsObjectLike instance;
                var proto = constructor is not null ? ResolveConstructPrototype(constructor, constructor) : null;
                if (constructor is HostFunction hostFunction && ReferenceEquals(hostFunction, ArrayConstructor))
                {
                    instance = new JsArray();
                }
                else
                {
                    instance = new JsObject();
                }

                if (proto is not null)
                {
                    instance.SetPrototype(proto);
                }

                var constructed = constructor?.Invoke(new object?[] { (double)lengthInt }, instance);
                result = constructed is IJsObjectLike objectLike ? objectLike : instance;
            }

            if (TryGetIteratorMethod(source, out var iteratorMethod))
            {
                if (iteratorMethod is not IJsCallable callableIterator)
                {
                    var error = TypeErrorConstructor is IJsCallable ctor3
                        ? ctor3.Invoke(["Iterator method is not callable"], null)
                        : new InvalidOperationException("Iterator method is not callable.");
                    throw new ThrowSignal(error);
                }

                var iteratorObj = callableIterator.Invoke(Array.Empty<object?>(), source);
                if (iteratorObj is not JsObject iter)
                {
                    var error = TypeErrorConstructor is IJsCallable ctor4
                        ? ctor4.Invoke(["Iterator method did not return an object"], null)
                        : new InvalidOperationException("Iterator method did not return an object.");
                    throw new ThrowSignal(error);
                }

                var nextVal = iter.TryGetProperty("next", out var nextProp) ? nextProp : null;
                if (nextVal is not IJsCallable nextFn)
                {
                    var error = TypeErrorConstructor is IJsCallable ctor5
                        ? ctor5.Invoke(["Iterator.next is not callable"], null)
                        : new InvalidOperationException("Iterator.next is not callable.");
                    throw new ThrowSignal(error);
                }

                var k = 0;
                while (true)
                {
                    var step = nextFn.Invoke(Array.Empty<object?>(), iter);
                    if (step is not JsObject stepObj)
                    {
                        break;
                    }

                    var done = stepObj.TryGetProperty("done", out var doneVal) && ToBoolean(doneVal);
                    if (done)
                    {
                        break;
                    }

                    var value = stepObj.TryGetProperty("value", out var val) ? val : Symbols.Undefined;
                    if (mapfn is IJsCallable mapper)
                    {
                        if (mapper is IJsEnvironmentAwareCallable envAware && callingEnv is not null)
                        {
                            envAware.CallingJsEnvironment = callingEnv;
                        }

                        value = mapper.Invoke(new object?[] { value, (double)k }, thisArg);
                    }

                    CreateDataPropertyOrThrow(result, k.ToString(CultureInfo.InvariantCulture), value,
                        TypeErrorConstructor);
                    k++;
                }

                result.SetProperty("length", (double)k);
            }
            else
            {
                for (var k = 0; k < lengthInt; k++)
                {
                    var value = GetAt(source, k);
                    if (mapfn is IJsCallable mapper)
                    {
                        if (mapper is IJsEnvironmentAwareCallable envAware && callingEnv is not null)
                        {
                            envAware.CallingJsEnvironment = callingEnv;
                        }

                        value = mapper.Invoke(new object?[] { value, (double)k }, thisArg);
                    }

                    CreateDataPropertyOrThrow(result, k.ToString(CultureInfo.InvariantCulture), value,
                        TypeErrorConstructor);
                }

                result.SetProperty("length", (double)lengthInt);
            }
            return result;
        });
        arrayFrom.DefineProperty("name", new PropertyDescriptor
        {
            Value = "from",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        arrayFrom.DefineProperty("length", new PropertyDescriptor
        {
            Value = 1d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        arrayFrom.IsConstructor = false;
        arrayConstructor.DefineProperty("from", new PropertyDescriptor
        {
            Value = arrayFrom,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });

        // Array.of(...elements)
        arrayConstructor.SetProperty("of", new HostFunction(args =>
        {
            var arr = new JsArray(args);
            AddArrayMethods(arr);
            return arr;
        }));

        // Expose core Array prototype methods (such as slice) on
        // Array.prototype so patterns like `Array.prototype.slice.call`
        // work against array-like values (e.g. `arguments`).
        if (arrayConstructor.TryGetProperty("prototype", out var prototypeValue) &&
            prototypeValue is JsObject prototypeObject)
        {
            prototypeObject.SetProperty("slice", new HostFunction((thisValue, args) => ArraySlice(thisValue, args)));
        }

        if (arrayConstructor.TryGetProperty("prototype", out var protoValue) && protoValue is JsObject arrayProtoObj)
        {
            if (realm.ObjectPrototype is not null && arrayProtoObj.Prototype is null)
            {
                arrayProtoObj.SetPrototype(realm.ObjectPrototype);
            }
            arrayPrototype = arrayProtoObj;
            realm.ArrayPrototype ??= arrayProtoObj;
            ArrayPrototype ??= arrayProtoObj;
            AddArrayMethods(arrayProtoObj);
            arrayProtoObj.DefineProperty("length", new PropertyDescriptor
            {
                Value = 0d,
                Writable = true,
                Enumerable = false,
                Configurable = false
            });
            var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
            var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";
            if (arrayProtoObj.TryGetProperty("values", out var valuesFn))
            {
                arrayProtoObj.DefineProperty(iteratorKey, new PropertyDescriptor
                {
                    Value = valuesFn,
                    Writable = true,
                    Enumerable = false,
                    Configurable = true
                });
            }
        }

        arrayConstructor.DefineProperty("length", new PropertyDescriptor
        {
            Value = 1d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        arrayConstructor.DefineProperty("name", new PropertyDescriptor
        {
            Value = "Array",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        return arrayConstructor;
    }

    /// <summary>
    /// Creates the Symbol constructor function with static methods.
    /// </summary>
    public static HostFunction CreateSymbolConstructor()
    {
        // Symbol cannot be used with 'new' in JavaScript
        var symbolConstructor = new HostFunction(args =>
        {
            var description = args.Count > 0 && args[0] != null && !ReferenceEquals(args[0], Symbols.Undefined)
                ? args[0]!.ToString()
                : null;
            return TypedAstSymbol.Create(description);
        });

        // Symbol.for(key) - creates/retrieves a global symbol
        symbolConstructor.SetProperty("for", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return Symbols.Undefined;
            }

            var key = args[0]?.ToString() ?? "";
            return TypedAstSymbol.For(key);
        }));

        // Symbol.keyFor(symbol) - gets the key for a global symbol
        symbolConstructor.SetProperty("keyFor", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not TypedAstSymbol sym)
            {
                return Symbols.Undefined;
            }

            var key = TypedAstSymbol.KeyFor(sym);
            return key ?? (object)Symbols.Undefined;
        }));

        // Well-known symbols
        symbolConstructor.SetProperty("iterator", TypedAstSymbol.For("Symbol.iterator"));
        symbolConstructor.SetProperty("asyncIterator", TypedAstSymbol.For("Symbol.asyncIterator"));
        symbolConstructor.SetProperty("toPrimitive", TypedAstSymbol.For("Symbol.toPrimitive"));

        return symbolConstructor;
    }

    /// <summary>
    /// Creates the Map constructor function.
    /// </summary>
    public static IJsCallable CreateMapConstructor()
    {
        var mapConstructor = new HostFunction(args =>
        {
            var map = new JsMap();

            // If an iterable is provided, populate the map
            if (args.Count > 0 && args[0] is JsArray entries)
            {
                foreach (var entry in entries.Items)
                {
                    if (entry is JsArray { Items.Count: >= 2 } pair)
                    {
                        map.Set(pair.GetElement(0), pair.GetElement(1));
                    }
                }
            }

            AddMapMethods(map);
            return map;
        });

        return mapConstructor;
    }

    /// <summary>
    /// Adds instance methods to a Map object.
    /// </summary>
    private static void AddMapMethods(JsMap map)
    {
        // Note: size needs special handling as a getter - for now we'll just access it dynamically in the methods

        // set(key, value)
        map.SetProperty("set", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return Symbols.Undefined;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            var value = args.Count > 1 ? args[1] : Symbols.Undefined;
            return m.Set(key, value);
        }));

        // get(key)
        map.SetProperty("get", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return Symbols.Undefined;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            return m.Get(key);
        }));

        // has(key)
        map.SetProperty("has", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return false;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            return m.Has(key);
        }));

        // delete(key)
        map.SetProperty("delete", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return false;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            return m.Delete(key);
        }));

        // clear()
        map.SetProperty("clear", new HostFunction((thisValue, args) =>
        {
            if (thisValue is JsMap m)
            {
                m.Clear();
            }

            return Symbols.Undefined;
        }));

        // forEach(callback, thisArg)
        map.SetProperty("forEach", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return Symbols.Undefined;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return Symbols.Undefined;
            }

            var thisArg = args.Count > 1 ? args[1] : null;
            m.ForEach(callback, thisArg);
            return Symbols.Undefined;
        }));

        // entries()
        map.SetProperty("entries", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return Symbols.Undefined;
            }

            return m.Entries();
        }));

        // keys()
        map.SetProperty("keys", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return Symbols.Undefined;
            }

            return m.Keys();
        }));

        // values()
        map.SetProperty("values", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsMap m)
            {
                return Symbols.Undefined;
            }

            return m.Values();
        }));
    }

    /// <summary>
    /// Creates the Set constructor function.
    /// </summary>
    public static IJsCallable CreateSetConstructor()
    {
        var setConstructor = new HostFunction(args =>
        {
            var set = new JsSet();

            // If an iterable is provided, populate the set
            if (args.Count > 0 && args[0] is JsArray values)
            {
                foreach (var value in values.Items)
                {
                    set.Add(value);
                }
            }

            AddSetMethods(set);
            return set;
        });

        return setConstructor;
    }

    /// <summary>
    /// Adds instance methods to a Set object.
    /// </summary>
    private static void AddSetMethods(JsSet set)
    {
        // Note: size needs special handling as a getter - handled in Evaluator.TryGetPropertyValue

        // add(value)
        set.SetProperty("add", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return Symbols.Undefined;
            }

            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            return s.Add(value);
        }));

        // has(value)
        set.SetProperty("has", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return false;
            }

            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            return s.Has(value);
        }));

        // delete(value)
        set.SetProperty("delete", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return false;
            }

            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            return s.Delete(value);
        }));

        // clear()
        set.SetProperty("clear", new HostFunction((thisValue, args) =>
        {
            if (thisValue is JsSet s)
            {
                s.Clear();
            }

            return Symbols.Undefined;
        }));

        // forEach(callback, thisArg)
        set.SetProperty("forEach", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return Symbols.Undefined;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return Symbols.Undefined;
            }

            var thisArg = args.Count > 1 ? args[1] : null;
            s.ForEach(callback, thisArg);
            return Symbols.Undefined;
        }));

        // entries()
        set.SetProperty("entries", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return Symbols.Undefined;
            }

            return s.Entries();
        }));

        // keys()
        set.SetProperty("keys", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return Symbols.Undefined;
            }

            return s.Keys();
        }));

        // values()
        set.SetProperty("values", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsSet s)
            {
                return Symbols.Undefined;
            }

            return s.Values();
        }));
    }

    /// <summary>
    /// Creates the WeakMap constructor function.
    /// </summary>
    public static IJsCallable CreateWeakMapConstructor()
    {
        var weakMapConstructor = new HostFunction(args =>
        {
            var weakMap = new JsWeakMap();

            // Note: WeakMap constructor can accept an iterable, but we'll start with basic support
            // If an iterable is provided, populate the weak map
            if (args.Count > 0 && args[0] is JsArray entries)
            {
                foreach (var entry in entries.Items)
                {
                    if (entry is JsArray { Items.Count: >= 2 } pair)
                    {
                        try
                        {
                            weakMap.Set(pair.GetElement(0), pair.GetElement(1));
                        }
                        catch (Exception ex)
                        {
                            throw new Exception(ex.Message);
                        }
                    }
                }
            }

            AddWeakMapMethods(weakMap);
            return weakMap;
        });

        return weakMapConstructor;
    }

    /// <summary>
    /// Adds instance methods to a WeakMap object.
    /// </summary>
    private static void AddWeakMapMethods(JsWeakMap weakMap)
    {
        // set(key, value)
        weakMap.SetProperty("set", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakMap wm)
            {
                return Symbols.Undefined;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            var value = args.Count > 1 ? args[1] : Symbols.Undefined;
            try
            {
                return wm.Set(key, value);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }));

        // get(key)
        weakMap.SetProperty("get", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakMap wm)
            {
                return Symbols.Undefined;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            return wm.Get(key);
        }));

        // has(key)
        weakMap.SetProperty("has", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakMap wm)
            {
                return false;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            return wm.Has(key);
        }));

        // delete(key)
        weakMap.SetProperty("delete", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakMap wm)
            {
                return false;
            }

            var key = args.Count > 0 ? args[0] : Symbols.Undefined;
            return wm.Delete(key);
        }));
    }

    /// <summary>
    /// Creates the WeakSet constructor function.
    /// </summary>
    public static IJsCallable CreateWeakSetConstructor()
    {
        var weakSetConstructor = new HostFunction(args =>
        {
            var weakSet = new JsWeakSet();

            // If an iterable is provided, populate the weak set
            if (args.Count > 0 && args[0] is JsArray values)
            {
                foreach (var value in values.Items)
                {
                    try
                    {
                        weakSet.Add(value);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(ex.Message);
                    }
                }
            }

            AddWeakSetMethods(weakSet);
            return weakSet;
        });

        return weakSetConstructor;
    }

    /// <summary>
    /// Adds instance methods to a WeakSet object.
    /// </summary>
    private static void AddWeakSetMethods(JsWeakSet weakSet)
    {
        // add(value)
        weakSet.SetProperty("add", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakSet ws)
            {
                return Symbols.Undefined;
            }

            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            try
            {
                return ws.Add(value);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }));

        // has(value)
        weakSet.SetProperty("has", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakSet ws)
            {
                return false;
            }

            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            return ws.Has(value);
        }));

        // delete(value)
        weakSet.SetProperty("delete", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsWeakSet ws)
            {
                return false;
            }

            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            return ws.Delete(value);
        }));
    }

    /// <summary>
    /// Creates the BigInt function (not a constructor).
    /// </summary>
    public static HostFunction CreateBigIntFunction(Runtime.RealmState realm)
    {
        HostFunction bigIntFunction = null!;
        bigIntFunction = new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                throw ThrowTypeError("Cannot convert undefined to a BigInt");
            }

            return ToBigInt(args[0]);
        })
        {
            IsConstructor = true,
            DisallowConstruct = true,
            ConstructErrorMessage = "BigInt is not a constructor"
        };
        // length/name descriptors
        bigIntFunction.DefineProperty("length", new PropertyDescriptor
        {
            Value = 1d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        // name is already set on HostFunction; normalize attributes
        bigIntFunction.DefineProperty("name", new PropertyDescriptor
        {
            Value = "BigInt",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        if (bigIntFunction.TryGetProperty("prototype", out var protoValue) && protoValue is JsObject proto)
        {
            realm.BigIntPrototype ??= proto;
            BigIntPrototype ??= proto;
            if (realm.ObjectPrototype is not null && proto.Prototype is null)
            {
                proto.SetPrototype(realm.ObjectPrototype);
            }

            proto.DefineProperty("constructor", new PropertyDescriptor
            {
                Value = bigIntFunction,
                Writable = true,
                Enumerable = false,
                Configurable = true
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
            })
            {
                IsConstructor = false
            };
            toStringFn.DefineProperty("length", new PropertyDescriptor
            {
                Value = 0d,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
            toStringFn.DefineProperty("name", new PropertyDescriptor
            {
                Value = "toString",
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
            proto.DefineProperty("toString", new PropertyDescriptor
            {
                Value = toStringFn,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });

            var valueOfFn = new HostFunction((thisValue, args) => ThisBigIntValue(thisValue))
            {
                IsConstructor = false
            };
            valueOfFn.DefineProperty("length", new PropertyDescriptor
            {
                Value = 0d,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
            valueOfFn.DefineProperty("name", new PropertyDescriptor
            {
                Value = "valueOf",
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
            proto.DefineProperty("valueOf", new PropertyDescriptor
            {
                Value = valueOfFn,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });

            var toLocaleStringFn = new HostFunction((thisValue, args) =>
            {
                // Minimal locale-insensitive fallback: ignore locales/options and
                // use base-10 formatting per spec default.
                var value = ThisBigIntValue(thisValue);
                return BigIntToString(value.Value, 10);
            })
            {
                IsConstructor = false
            };
            toLocaleStringFn.DefineProperty("length", new PropertyDescriptor
            {
                Value = 0d,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
            toLocaleStringFn.DefineProperty("name", new PropertyDescriptor
            {
                Value = "toLocaleString",
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
            proto.DefineProperty("toLocaleString", new PropertyDescriptor
            {
                Value = toLocaleStringFn,
                Writable = true,
                Enumerable = false,
                Configurable = true
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
    /// Creates the Number constructor with static methods.
    /// </summary>
    public static HostFunction CreateNumberConstructor(Runtime.RealmState realm)
    {
        // Number constructor
        var numberConstructor = new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                if (thisValue is JsObject objZero)
                {
                    objZero.SetProperty("__value__", 0d);
                    return objZero;
                }

                return 0d;
            }

            var value = args[0];
            var result = JsOps.ToNumber(value);

            if (thisValue is JsObject obj)
            {
                obj.SetProperty("__value__", result);
                return obj;
            }

            return result;
        });

        // Remember Number.prototype so that number wrapper objects can see
        // methods attached from user code (e.g. Number.prototype.toJSONString).
        if (numberConstructor.TryGetProperty("prototype", out var numberProto) &&
            numberProto is JsObject numberProtoObj)
        {
            realm.NumberPrototype ??= numberProtoObj;
            NumberPrototype ??= numberProtoObj;
            if (realm.ObjectPrototype is not null && numberProtoObj.Prototype is null)
            {
                numberProtoObj.SetPrototype(realm.ObjectPrototype);
            }

            numberProtoObj.SetProperty("toString", new HostFunction((thisValue, args) =>
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

                return num.ToString(System.Globalization.CultureInfo.InvariantCulture);
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

            if (args[0] is not double d)
            {
                return false;
            }

            return double.IsNaN(d);
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
            if (str == "")
            {
                return double.NaN;
            }

            // Try to parse, taking as much as possible from the start
            var match = System.Text.RegularExpressions.Regex.Match(str, @"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?");
            if (match.Success)
            {
                if (double.TryParse(match.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var result))
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
            if (str == "")
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
                str = str.Substring(1).TrimStart();
            }
            else if (str.StartsWith("+"))
            {
                str = str.Substring(1).TrimStart();
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

    /// <summary>
    /// Creates the String constructor with static methods.
    /// </summary>
    public static HostFunction CreateStringConstructor(Runtime.RealmState realm)
    {
        // String constructor
        var stringConstructor = new HostFunction((thisValue, args) =>
        {
            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            var str = value switch
            {
                string s => s,
                double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                null => "null",
                Symbol sym when ReferenceEquals(sym, Symbols.Undefined) => "undefined",
                _ => value?.ToString() ?? string.Empty
            };

            if (thisValue is JsObject obj)
            {
                obj.SetProperty("__value__", str);
                obj.SetProperty("length", (double)str.Length);
                if (realm.StringPrototype is not null)
                {
                    obj.SetPrototype(realm.StringPrototype);
                }

                return obj;
            }

            return str;
        });

        // Remember String.prototype so that string wrapper objects can see
        // methods attached from user code (e.g. String.prototype.toJSONString),
        // and provide a minimal shared implementation of core helpers such as
        // String.prototype.slice for use with call/apply patterns.
        if (stringConstructor.TryGetProperty("prototype", out var stringProto) &&
            stringProto is JsObject stringProtoObj)
        {
            realm.StringPrototype ??= stringProtoObj;
            StringPrototype ??= stringProtoObj;
            if (realm.ObjectPrototype is not null && stringProtoObj.Prototype is null)
            {
                stringProtoObj.SetPrototype(realm.ObjectPrototype);
            }

            stringProtoObj.SetProperty("slice", new HostFunction((thisValue, args) =>
            {
                var str = JsValueToString(thisValue);
                if (str is null)
                {
                    return "";
                }

                if (args.Count == 0)
                {
                    return str;
                }

                var start = args[0] is double d1 ? (int)d1 : 0;
                var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : str.Length;

                if (start < 0)
                {
                    start = Math.Max(0, str.Length + start);
                }
                else
                {
                    start = Math.Min(start, str.Length);
                }

                if (end < 0)
                {
                    end = Math.Max(0, str.Length + end);
                }
                else
                {
                    end = Math.Min(end, str.Length);
                }

                if (start >= end)
                {
                    return "";
                }

                return str.Substring(start, end - start);
            }));
        }

        // String.fromCodePoint(...codePoints)
        stringConstructor.SetProperty("fromCodePoint", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return "";
            }

            var result = new System.Text.StringBuilder();
            foreach (var arg in args)
            {
                var num = JsOps.ToNumber(arg);
                if (double.IsNaN(num) || double.IsInfinity(num))
                {
                    continue;
                }

                var codePoint = (int)num;
                // Validate code point range
                if (codePoint is < 0 or > 0x10FFFF)
                {
                    throw new Exception("RangeError: Invalid code point " + codePoint);
                }

                // Handle surrogate pairs for code points > 0xFFFF
                if (codePoint <= 0xFFFF)
                {
                    result.Append((char)codePoint);
                }
                else
                {
                    codePoint -= 0x10000;
                    result.Append((char)(0xD800 + (codePoint >> 10)));
                    result.Append((char)(0xDC00 + (codePoint & 0x3FF)));
                }
            }

            return result.ToString();
        }));

        // String.fromCharCode(...charCodes) - for compatibility
        stringConstructor.SetProperty("fromCharCode", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return "";
            }

            var result = new System.Text.StringBuilder();
            foreach (var arg in args)
            {
                var num = JsOps.ToNumber(arg);
                if (double.IsNaN(num) || double.IsInfinity(num))
                {
                    continue;
                }

                var charCode = (int)num & 0xFFFF; // Limit to 16-bit range
                result.Append((char)charCode);
            }

            return result.ToString();
        }));

        // String.raw(template, ...substitutions)
        // This is a special method used with tagged templates
        stringConstructor.SetProperty("raw", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return "";
            }

            // First argument should be a template object with 'raw' property
            if (args[0] is not JsObject template)
            {
                return "";
            }

            // Get the raw strings array
            if (!template.TryGetProperty("raw", out var rawValue) || rawValue is not JsArray rawStrings)
            {
                return "";
            }

            var result = new System.Text.StringBuilder();
            var rawCount = rawStrings.Items.Count;

            for (var i = 0; i < rawCount; i++)
            {
                // Append the raw string part
                var rawPart = rawStrings.GetElement(i)?.ToString() ?? "";
                result.Append(rawPart);

                // Append the substitution if there is one
                if (i < args.Count - 1)
                {
                    var substitution = args[i + 1];
                    if (substitution != null)
                    {
                        result.Append(substitution.ToString());
                    }
                }
            }

            return result.ToString();
        }));

        // String.escape(string) - deprecated but used in some old code
        // Escapes special characters for use in URIs or HTML
        stringConstructor.SetProperty("escape", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return "";
            }

            var str = args[0]?.ToString() ?? "";

            var result = new System.Text.StringBuilder();
            foreach (var ch in str)
            {
                // Characters that don't need escaping
                if (ch is >= 'A' and <= 'Z' ||
                    ch is >= 'a' and <= 'z' ||
                    ch is >= '0' and <= '9' ||
                    ch == '@' || ch == '*' || ch == '_' ||
                    ch == '+' || ch == '-' || ch == '.' || ch == '/')
                {
                    result.Append(ch);
                }
                // Characters that need hex escaping
                else if (ch < 256)
                {
                    result.Append('%');
                    result.Append(((int)ch).ToString("X2", CultureInfo.InvariantCulture));
                }
                // Unicode characters use %uXXXX format
                else
                {
                    result.Append("%u");
                    result.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            return result.ToString();
        }));

        return stringConstructor;
    }

    /// <summary>
    /// Creates error constructor functions for standard JavaScript error types.
    /// </summary>
    public static HostFunction CreateErrorConstructor(Runtime.RealmState realm, string errorType = "Error")
    {
        JsObject? prototype = null;

        var errorConstructor = new HostFunction((thisValue, args) =>
        {
            var message = args.Count > 0 && args[0] != null ? args[0]!.ToString() : "";
            var errorObj = thisValue as JsObject ?? new JsObject();

            if (prototype is not null && errorObj.Prototype is null)
            {
                errorObj.SetPrototype(prototype);
            }

            errorObj["name"] = errorType;
            errorObj["message"] = message;

            return errorObj;
        });

        prototype = new JsObject();
        if (!string.Equals(errorType, "Error", StringComparison.Ordinal) && realm.ErrorPrototype is not null)
        {
            prototype.SetPrototype(realm.ErrorPrototype);
        }
        else if (realm.ObjectPrototype is not null)
        {
            prototype.SetPrototype(realm.ObjectPrototype);
        }

        prototype.SetProperty("toString", new HostFunction((errThis, toStringArgs) =>
        {
            if (errThis is JsObject err)
            {
                var name = err.TryGetValue("name", out var n) ? n?.ToString() : errorType;
                var msg = err.TryGetValue("message", out var m) ? m?.ToString() : "";
                return string.IsNullOrEmpty(msg) ? name : $"{name}: {msg}";
            }

            return errorType;
        }));

        prototype.DefineProperty("constructor", new PropertyDescriptor
        {
            Value = errorConstructor,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });

        errorConstructor.SetProperty("prototype", prototype);

        if (string.Equals(errorType, "Error", StringComparison.Ordinal))
        {
            realm.ErrorPrototype = prototype;
            ErrorPrototype = prototype;
        }

        if (string.Equals(errorType, "TypeError", StringComparison.Ordinal))
        {
            realm.TypeErrorPrototype = prototype;
            realm.TypeErrorConstructor = errorConstructor;
            TypeErrorPrototype = prototype;
            TypeErrorConstructor = errorConstructor;
        }

        if (string.Equals(errorType, "RangeError", StringComparison.Ordinal))
        {
            realm.RangeErrorConstructor = errorConstructor;
            RangeErrorConstructor = errorConstructor;
        }

        if (string.Equals(errorType, "SyntaxError", StringComparison.Ordinal))
        {
            realm.SyntaxErrorConstructor = errorConstructor;
            SyntaxErrorConstructor = errorConstructor;
            realm.SyntaxErrorPrototype = prototype;
            SyntaxErrorPrototype = prototype;
        }

        // Function.name
        errorConstructor.SetProperty("name", errorType);

        return errorConstructor;
    }

    /// <summary>
    /// Converts a value to a boolean following JavaScript truthiness rules.
    /// </summary>
    private static bool ToBoolean(object? value)
    {
        return value switch
        {
            null => false,
            Symbol sym when ReferenceEquals(sym, Symbols.Undefined) => false,
            bool b => b,
            double d => !double.IsNaN(d) && Math.Abs(d) > double.Epsilon,
            string s => s.Length > 0,
            _ => true
        };
    }

    /// <summary>
    /// Creates the ArrayBuffer constructor.
    /// </summary>
    public static HostFunction CreateArrayBufferConstructor()
    {
        var constructor = new HostFunction((thisValue, args) =>
        {
            var length = args.Count > 0 ? args[0] : 0d;
            var byteLength = length switch
            {
                double d => (int)d,
                int i => i,
                _ => 0
            };

            int? maxByteLength = null;
            if (args.Count > 1 && args[1] is JsObject opts)
            {
                if (opts.TryGetProperty("maxByteLength", out var maxVal) && maxVal is double maxD)
                {
                    maxByteLength = (int)maxD;
                }
            }

            return new JsArrayBuffer(byteLength, maxByteLength);
        });

        constructor.SetProperty("isView", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return false;
            }

            return args[0] is TypedArrayBase || args[0] is JsDataView;
        }));

        return constructor;
    }

    /// <summary>
    /// Creates the DataView constructor.
    /// </summary>
    public static HostFunction CreateDataViewConstructor()
    {
        return new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0 || args[0] is not JsArrayBuffer buffer)
            {
                throw new InvalidOperationException("DataView requires an ArrayBuffer");
            }

            var byteOffset = args.Count > 1 && args[1] is double d1 ? (int)d1 : 0;
            int? byteLength = args.Count > 2 && args[2] is double d2 ? (int)d2 : null;

            return new JsDataView(buffer, byteOffset, byteLength);
        });
    }

    /// <summary>
    /// Creates a typed array constructor.
    /// </summary>
    private static HostFunction CreateTypedArrayConstructor<T>(
        Func<int, T> fromLength,
        Func<JsArray, T> fromArray,
        Func<JsArrayBuffer, int, int, T> fromBuffer,
        int bytesPerElement) where T : TypedArrayBase
    {
        var prototype = new JsObject();
        var constructor = new HostFunction((thisValue, args) =>
        {
            if (!JsOps.IsConstructor(thisValue))
            {
                throw ThrowTypeError("%TypedArray%.of called on non-constructor");
            }

            if (args.Count == 0)
            {
                var ta = fromLength(0);
                ta.SetPrototype(prototype);
                return ta;
            }

            var firstArg = args[0];

            // TypedArray(length)
            if (firstArg is double d)
            {
                var ta = fromLength((int)d);
                ta.SetPrototype(prototype);
                return ta;
            }

            // TypedArray(array)
            if (firstArg is JsArray array)
            {
                var ta = fromArray(array);
                ta.SetPrototype(prototype);
                return ta;
            }

            // TypedArray(buffer, byteOffset, length)
            if (firstArg is JsArrayBuffer buffer)
            {
                var byteOffset = args.Count > 1 && args[1] is double d1 ? (int)d1 : 0;

                int length;
                if (args.Count > 2 && args[2] is double d2)
                {
                    length = (int)d2;
                }
                else
                {
                    // Calculate length from remaining buffer
                    var remainingBytes = buffer.ByteLength - byteOffset;
                    length = remainingBytes / bytesPerElement;
                }

                var ta = fromBuffer(buffer, byteOffset, length);
                ta.SetPrototype(prototype);
                return ta;
            }

            var fallback = fromLength(0);
            fallback.SetPrototype(prototype);
            return fallback;
        });

        constructor.SetProperty("BYTES_PER_ELEMENT", (double)bytesPerElement);
        prototype.SetPrototype(ObjectPrototype);
        prototype.SetProperty("constructor", constructor);
        constructor.DefineProperty("of", new PropertyDescriptor
        {
            Value = new HostFunction((thisValue, args) =>
            {
                if (!JsOps.IsConstructor(thisValue))
                {
                    throw ThrowTypeError("%TypedArray%.of called on non-constructor");
                }

                if (thisValue is not HostFunction ctor)
                {
                    throw ThrowTypeError("%TypedArray%.of called on incompatible receiver");
                }

                var length = args.Count;
                // Invoke the constructor with the desired length.
                var taObj = ctor.Invoke([(double)length], ctor);
                if (taObj is not TypedArrayBase typed)
                {
                    throw ThrowTypeError("%TypedArray%.of constructor did not return a typed array");
                }

                for (var i = 0; i < length; i++)
                {
                    typed.SetValue(i, args[i]);
                }

                return typed;
            })
            {
                IsConstructor = false
            },
            Writable = true,
            Enumerable = false,
            Configurable = true
        });
        prototype.SetProperty("indexOf", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not TypedArrayBase typed)
            {
                throw ThrowTypeError("TypedArray.prototype.indexOf called on incompatible receiver");
            }

            return TypedArrayBase.IndexOfInternal(typed, args);
        }));
        constructor.SetProperty("prototype", prototype);

        return constructor;
    }

    public static HostFunction CreateInt8ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsInt8Array.FromLength,
            JsInt8Array.FromArray,
            (buffer, offset, length) => new JsInt8Array(buffer, offset, length),
            JsInt8Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateUint8ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsUint8Array.FromLength,
            JsUint8Array.FromArray,
            (buffer, offset, length) => new JsUint8Array(buffer, offset, length),
            JsUint8Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateUint8ClampedArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsUint8ClampedArray.FromLength,
            JsUint8ClampedArray.FromArray,
            (buffer, offset, length) => new JsUint8ClampedArray(buffer, offset, length),
            JsUint8ClampedArray.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateInt16ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsInt16Array.FromLength,
            JsInt16Array.FromArray,
            (buffer, offset, length) => new JsInt16Array(buffer, offset, length),
            JsInt16Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateUint16ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsUint16Array.FromLength,
            JsUint16Array.FromArray,
            (buffer, offset, length) => new JsUint16Array(buffer, offset, length),
            JsUint16Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateInt32ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsInt32Array.FromLength,
            JsInt32Array.FromArray,
            (buffer, offset, length) => new JsInt32Array(buffer, offset, length),
            JsInt32Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateUint32ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsUint32Array.FromLength,
            JsUint32Array.FromArray,
            (buffer, offset, length) => new JsUint32Array(buffer, offset, length),
            JsUint32Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateFloat32ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsFloat32Array.FromLength,
            JsFloat32Array.FromArray,
            (buffer, offset, length) => new JsFloat32Array(buffer, offset, length),
            JsFloat32Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateFloat64ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsFloat64Array.FromLength,
            JsFloat64Array.FromArray,
            (buffer, offset, length) => new JsFloat64Array(buffer, offset, length),
            JsFloat64Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateBigInt64ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsBigInt64Array.FromLength,
            JsBigInt64Array.FromArray,
            (buffer, offset, length) => new JsBigInt64Array(buffer, offset, length),
            JsBigInt64Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateBigUint64ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsBigUint64Array.FromLength,
            JsBigUint64Array.FromArray,
            (buffer, offset, length) => new JsBigUint64Array(buffer, offset, length),
            JsBigUint64Array.BYTES_PER_ELEMENT);
    }

    /// <summary>
    /// Helper method for async iteration: gets an async iterator from an iterable.
    /// For for-await-of: tries Symbol.asyncIterator first, falls back to Symbol.iterator.
    /// </summary>
    public static HostFunction CreateGetAsyncIteratorHelper(JsEngine engine)
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                throw new InvalidOperationException("__getAsyncIterator requires an iterable");
            }

            var iterable = args[0];

            static bool HasCallableNext(object? candidate)
            {
                return candidate is JsObject obj &&
                       obj.TryGetProperty("next", out var nextProp) &&
                       nextProp is IJsCallable;
            }

            static bool TryInvokeSymbolIterator(JsObject target, string symbolName, out JsObject? iterator)
            {
                var symbol = TypedAstSymbol.For(symbolName);
                var propertyName = $"@@symbol:{symbol.GetHashCode()}";
                if (target.TryGetProperty(propertyName, out var method) && method is IJsCallable callable)
                {
                    if (callable.Invoke([], target) is JsObject iteratorObj)
                    {
                        iterator = iteratorObj;
                        return true;
                    }
                }

                iterator = null;
                return false;
            }

            static JsObject CreateStringIterator(string str)
            {
                var iteratorObj = new JsObject();
                var index = 0;
                iteratorObj.SetProperty("next", new HostFunction(_ =>
                {
                    var result = new JsObject();
                    if (index < str.Length)
                    {
                        result.SetProperty("value", str[index].ToString());
                        result.SetProperty("done", false);
                        index++;
                    }
                    else
                    {
                        result.SetProperty("done", true);
                    }

                    return result;
                }));
                return iteratorObj;
            }

            static JsObject CreateArrayIterator(JsArray array)
            {
                var iteratorObj = new JsObject();
                var index = 0;
                iteratorObj.SetProperty("next", new HostFunction(_ =>
                {
                    var result = new JsObject();
                    if (index < array.Length)
                    {
                        result.SetProperty("value", array.GetElement(index));
                        result.SetProperty("done", false);
                        index++;
                    }
                    else
                    {
                        result.SetProperty("done", true);
                    }

                    return result;
                }));

                return iteratorObj;
            }

            if (iterable is JsObject jsObject)
            {
                if (HasCallableNext(jsObject))
                {
                    engine.WriteAsyncIteratorTrace("getAsyncIterator: branch=next-property");
                    return jsObject;
                }

                if (TryInvokeSymbolIterator(jsObject, "Symbol.asyncIterator", out var asyncIterator))
                {
                    engine.WriteAsyncIteratorTrace(
                        $"getAsyncIterator: branch=symbol-asyncIterator hasCallableNext={HasCallableNext(asyncIterator)}");
                    return asyncIterator;
                }

                if (TryInvokeSymbolIterator(jsObject, "Symbol.iterator", out var iterator))
                {
                    engine.WriteAsyncIteratorTrace(
                        $"getAsyncIterator: branch=symbol-iterator hasCallableNext={HasCallableNext(iterator)}");
                    return iterator;
                }

                throw new InvalidOperationException(
                    "Object is not iterable (no Symbol.asyncIterator or Symbol.iterator method)");
            }

            if (iterable is JsArray jsArray)
            {
                var iteratorObj = CreateArrayIterator(jsArray);
                engine.WriteAsyncIteratorTrace(
                    $"getAsyncIterator: branch=array length={jsArray.Length}");
                return iteratorObj;
            }

            if (iterable is string str)
            {
                var iteratorObj = CreateStringIterator(str);
                engine.WriteAsyncIteratorTrace(
                    $"getAsyncIterator: branch=string hasCallableNext={HasCallableNext(iteratorObj)} length={str.Length}");
                return iteratorObj;
            }

            throw new InvalidOperationException($"Value is not iterable: {iterable?.GetType().Name}");
        });
    }

    /// <summary>
    /// Helper method for async iteration: gets next value from iterator and wraps in Promise if needed.
    /// This handles both sync and async iterators uniformly.
    /// </summary>
    public static HostFunction CreateIteratorNextHelper(JsEngine engine)
    {
        return new HostFunction(args =>
        {
            // args[0] should be the iterator object
            if (args.Count == 0 || args[0] is not JsObject iterator)
            {
                throw new InvalidOperationException("__iteratorNext requires an iterator object");
            }

            // Call iterator.next()
            if (!iterator.TryGetProperty("next", out var nextMethod) || nextMethod is not IJsCallable nextCallable)
            {
                throw new InvalidOperationException("Iterator must have a 'next' method");
            }

            engine.WriteAsyncIteratorTrace("iteratorNext: invoking next() on iterator");
            object? result;
            try
            {
                result = nextCallable.Invoke([], iterator);
                engine.WriteAsyncIteratorTrace("iteratorNext: next() invocation succeeded");
            }
            catch (Exception ex)
            {
                // Log the exception for debugging
                engine.LogException(ex, "Iterator.next() invocation");

                engine.WriteAsyncIteratorTrace($"iteratorNext: next() threw exception='{ex.Message}'");

                // If next() throws an error, wrap it in a rejected promise
                var rejectedPromise = new JsPromise(engine);
                AddPromiseInstanceMethods(rejectedPromise.JsObject, rejectedPromise, engine);
                rejectedPromise.Reject(ex.Message);
                engine.WriteAsyncIteratorTrace("iteratorNext: returning rejected promise due to exception");
                return rejectedPromise.JsObject;
            }

            // Check if result is already a promise (has a "then" method)
            if (result is JsObject resultObj && resultObj.TryGetProperty("then", out var thenMethod) &&
                thenMethod is IJsCallable)
            {
                engine.WriteAsyncIteratorTrace("iteratorNext: result already promise-like, returning as-is");
                // Already a promise, return as-is
                return result;
            }

            // Not a promise, wrap in Promise.resolve()
            var promise = new JsPromise(engine);
            AddPromiseInstanceMethods(promise.JsObject, promise, engine);
            promise.Resolve(result);
            engine.WriteAsyncIteratorTrace("iteratorNext: wrapped result in resolved promise");
            return promise.JsObject;
        });
    }

    /// <summary>
    /// Helper function for await expressions: wraps value in Promise if needed.
    /// Checks if the value is already a promise (has a "then" method) before wrapping.
    /// </summary>
    public static HostFunction CreateAwaitHelper(JsEngine engine)
    {
        return new HostFunction(args =>
        {
            // args[0] should be the value to await
            var value = args.Count > 0 ? args[0] : null;

            // Check if value is already a promise (has a "then" method)
            if (value is JsObject valueObj && valueObj.TryGetProperty("then", out var thenMethod) &&
                thenMethod is IJsCallable)
                // Already a promise, return as-is
            {
                return value;
            }

            // Not a promise, wrap in Promise.resolve()
            var promise = new JsPromise(engine);
            AddPromiseInstanceMethods(promise.JsObject, promise, engine);
            promise.Resolve(value);
            return promise.JsObject;
        });
    }

    /// <summary>
    /// Creates the global parseInt function.
    /// </summary>
    public static HostFunction CreateParseIntFunction()
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            var str = args[0]?.ToString() ?? "";
            str = str.Trim();
            if (str == "")
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
                str = str.Substring(1).TrimStart();
            }
            else if (str.StartsWith("+"))
            {
                str = str.Substring(1).TrimStart();
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
        });
    }

    /// <summary>
    /// Creates the global parseFloat function.
    /// </summary>
    public static HostFunction CreateParseFloatFunction()
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            var str = args[0]?.ToString() ?? "";
            str = str.Trim();
            if (str == "")
            {
                return double.NaN;
            }

            // Try parsing the string as a double
            if (double.TryParse(str, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            // JavaScript parseFloat allows partial parsing - parse as much as possible
            var i = 0;
            var hasSign = false;
            var hasDigits = false;
            var hasDecimal = false;

            // Handle sign
            if (i < str.Length && (str[i] == '+' || str[i] == '-'))
            {
                hasSign = true;
                i++;
            }

            // Parse digits before decimal point
            while (i < str.Length && char.IsDigit(str[i]))
            {
                hasDigits = true;
                i++;
            }

            // Parse decimal point and digits after
            if (i < str.Length && str[i] == '.')
            {
                hasDecimal = true;
                i++;
                while (i < str.Length && char.IsDigit(str[i]))
                {
                    hasDigits = true;
                    i++;
                }
            }

            // Parse exponent
            if (i < str.Length && (str[i] == 'e' || str[i] == 'E'))
            {
                var j = i + 1;
                if (j < str.Length && (str[j] == '+' || str[j] == '-'))
                {
                    j++;
                }

                var hasExpDigits = false;
                while (j < str.Length && char.IsDigit(str[j]))
                {
                    hasExpDigits = true;
                    j++;
                }

                if (hasExpDigits)
                {
                    i = j;
                }
            }

            if (!hasDigits)
            {
                return double.NaN;
            }

            var parsed = str.Substring(0, i);
            if (double.TryParse(parsed, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out result))
            {
                return result;
            }

            return double.NaN;
        });
    }

    /// <summary>
    /// Creates the global isNaN function.
    /// </summary>
    public static HostFunction CreateIsNaNFunction()
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return true;
            }

            var value = args[0];

            // Convert to number first (this is what JavaScript does)
            if (value is double d)
            {
                return double.IsNaN(d);
            }

            if (value is int or long or float or decimal)
            {
                return false;
            }

            if (value is string s)
            {
                if (double.TryParse(s, out var parsed))
                {
                    return double.IsNaN(parsed);
                }

                return true; // Can't parse, so NaN
            }

            return true; // Everything else becomes NaN
        });
    }

    /// <summary>
    /// Creates the global isFinite function.
    /// </summary>
    public static HostFunction CreateIsFiniteFunction()
    {
        return new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return false;
            }

            var value = args[0];

            // Convert to number first (this is what JavaScript does)
            if (value is double d)
            {
                return !double.IsNaN(d) && !double.IsInfinity(d);
            }

            if (value is int or long or float or decimal)
            {
                return true;
            }

            if (value is string s)
            {
                if (double.TryParse(s, out var parsed))
                {
                    return !double.IsNaN(parsed) && !double.IsInfinity(parsed);
                }

                return false; // Can't parse, so NaN, so not finite
            }

            return false; // Everything else becomes NaN, which is not finite
        });
    }

    public static JsObject CreateReflectObject()
    {
        var reflect = new JsObject();

        reflect.SetProperty("apply", new HostFunction(args =>
        {
            if (args.Count < 2 || args[0] is not IJsCallable callable)
            {
                throw new Exception("Reflect.apply: target must be callable.");
            }

            var thisArg = args[1];
            var argList = args.Count > 2 && args[2] is JsArray arr
                ? arr.Items.ToArray()
                : Array.Empty<object?>();

            return callable.Invoke(argList, thisArg);
        }));

        reflect.SetProperty("construct", new HostFunction(args =>
        {
            if (args.Count < 2 || args[0] is not IJsCallable target)
            {
                throw new Exception("Reflect.construct: target must be a constructor.");
            }

            var argList = args[1] is JsArray arr ? arr.Items.ToArray() : Array.Empty<object?>();
            var newTarget = args.Count > 2 && args[2] is IJsCallable ctor ? ctor : target;

            if (target is HostFunction hostTarget &&
                (!hostTarget.IsConstructor || hostTarget.DisallowConstruct))
            {
                var message = hostTarget.ConstructErrorMessage ?? "Target is not a constructor";
                var error = TypeErrorConstructor is IJsCallable typeErrorCtor
                    ? typeErrorCtor.Invoke([message], null)
                    : new InvalidOperationException(message);
                throw new ThrowSignal(error);
            }

            if (newTarget is HostFunction hostNewTarget &&
                !hostNewTarget.IsConstructor)
            {
                var message = hostNewTarget.ConstructErrorMessage ?? "newTarget is not a constructor";
                var error = TypeErrorConstructor is IJsCallable typeErrorCtor2
                    ? typeErrorCtor2.Invoke([message], null)
                    : new InvalidOperationException(message);
                throw new ThrowSignal(error);
            }

            var proto = ResolveConstructPrototype(newTarget, target);

            // If we are constructing Array (or a subclass), create a real JsArray
            // so length/index semantics behave correctly, then invoke the
            // constructor with that receiver.
            if (ReferenceEquals(target, ArrayConstructor) || ReferenceEquals(newTarget, ArrayConstructor))
            {
                var arrayInstance = new JsArray();
                if (proto is not null)
                {
                    arrayInstance.SetPrototype(proto);
                }

                var result = target.Invoke(argList, arrayInstance);
                return result is JsObject jsObj ? jsObj : arrayInstance;
            }

            var instance = new JsObject();
            if (proto is not null)
            {
                instance.SetPrototype(proto);
            }

            var constructed = target.Invoke(argList, instance);
            return constructed is JsObject obj ? obj : instance;
        }));

        reflect.SetProperty("defineProperty", new HostFunction(args =>
        {
            if (args.Count < 3 || !TryGetObject(args[0]!, out var target))
            {
                throw new Exception("Reflect.defineProperty: target must be an object.");
            }

            var propertyKey = args[1]?.ToString() ?? string.Empty;
            if (args[2] is not JsObject descriptorObj)
            {
                throw new Exception("Reflect.defineProperty: descriptor must be an object.");
            }

            var descriptor = new PropertyDescriptor();
            if (descriptorObj.TryGetProperty("value", out var value))
            {
                descriptor.Value = value;
            }

            if (descriptorObj.TryGetProperty("writable", out var writable))
            {
                descriptor.Writable = writable is bool b ? b : ToBoolean(writable);
            }

            if (descriptorObj.TryGetProperty("enumerable", out var enumerable))
            {
                descriptor.Enumerable = enumerable is bool b ? b : ToBoolean(enumerable);
            }

            if (descriptorObj.TryGetProperty("configurable", out var configurable))
            {
                descriptor.Configurable = configurable is bool b ? b : ToBoolean(configurable);
            }

            if (descriptorObj.TryGetProperty("get", out var getter) && getter is IJsCallable getterFn)
            {
                descriptor.Get = getterFn;
            }

            if (descriptorObj.TryGetProperty("set", out var setter) && setter is IJsCallable setterFn)
            {
                descriptor.Set = setterFn;
            }

            if (target is JsArray jsArray && string.Equals(propertyKey, "length", StringComparison.Ordinal))
            {
                return jsArray.DefineLength(descriptor, null, throwOnWritableFailure: false);
            }

            try
            {
                target.DefineProperty(propertyKey, descriptor);
                return true;
            }
            catch (ThrowSignal)
            {
                return false;
            }
        }));

        reflect.SetProperty("deleteProperty", new HostFunction(args =>
        {
            if (args.Count < 2 || !TryGetObject(args[0]!, out var target))
            {
                throw new Exception("Reflect.deleteProperty: target must be an object.");
            }

            var propertyKey = args[1]?.ToString() ?? string.Empty;
            return target is JsObject jsObj && jsObj.Remove(propertyKey);
        }));

        reflect.SetProperty("get", new HostFunction(args =>
        {
            if (args.Count < 2 || !TryGetObject(args[0]!, out var target))
            {
                throw new Exception("Reflect.get: target must be an object.");
            }

            var propertyKey = args[1]?.ToString() ?? string.Empty;
            return target.TryGetProperty(propertyKey, out var value) ? value : null;
        }));

        reflect.SetProperty("getOwnPropertyDescriptor", new HostFunction(args =>
        {
            if (args.Count < 2 || !TryGetObject(args[0]!, out var target))
            {
                throw new Exception("Reflect.getOwnPropertyDescriptor: target must be an object.");
            }

            var propertyKey = args[1]?.ToString() ?? string.Empty;
            var descriptor = target.GetOwnPropertyDescriptor(propertyKey);
            if (descriptor is null)
            {
                return null;
            }

            var descObj = new JsObject
            {
                ["value"] = descriptor.Value,
                ["writable"] = descriptor.Writable,
                ["enumerable"] = descriptor.Enumerable,
                ["configurable"] = descriptor.Configurable
            };

            if (descriptor.Get is not null)
            {
                descObj["get"] = descriptor.Get;
            }

            if (descriptor.Set is not null)
            {
                descObj["set"] = descriptor.Set;
            }

            return descObj;
        }));

        reflect.SetProperty("getPrototypeOf", new HostFunction(args =>
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, out var target))
            {
                throw new Exception("Reflect.getPrototypeOf: target must be an object.");
            }

            return target.Prototype;
        }));

        reflect.SetProperty("has", new HostFunction(args =>
        {
            if (args.Count < 2 || !TryGetObject(args[0]!, out var target))
            {
                throw new Exception("Reflect.has: target must be an object.");
            }

            var propertyKey = args[1]?.ToString() ?? string.Empty;
            return target.TryGetProperty(propertyKey, out _);
        }));

        reflect.SetProperty("isExtensible", new HostFunction(args =>
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, out var target))
            {
                throw new Exception("Reflect.isExtensible: target must be an object.");
            }

            return !target.IsSealed;
        }));

        reflect.SetProperty("ownKeys", new HostFunction(args =>
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, out var target))
            {
                throw new Exception("Reflect.ownKeys: target must be an object.");
            }

            var keys = target.Keys
                .Where(k => !k.StartsWith("__getter__", StringComparison.Ordinal) &&
                            !k.StartsWith("__setter__", StringComparison.Ordinal))
                .ToArray();
            return new JsArray(keys);
        }));

        reflect.SetProperty("preventExtensions", new HostFunction(args =>
        {
            if (args.Count == 0 || !TryGetObject(args[0]!, out var target))
            {
                throw new Exception("Reflect.preventExtensions: target must be an object.");
            }

            target.Seal();
            return true;
        }));

        reflect.SetProperty("set", new HostFunction(args =>
        {
            if (args.Count < 3 || !TryGetObject(args[0]!, out var target))
            {
                throw new Exception("Reflect.set: target must be an object.");
            }

            var propertyKey = args[1]?.ToString() ?? string.Empty;
            var value = args[2];
            if (target is JsArray jsArray && string.Equals(propertyKey, "length", StringComparison.Ordinal))
            {
                return jsArray.SetLength(value, null, throwOnWritableFailure: false);
            }

            try
            {
                target.SetProperty(propertyKey, value);
                return true;
            }
            catch (ThrowSignal)
            {
                return false;
            }
        }));

        reflect.SetProperty("setPrototypeOf", new HostFunction(args =>
        {
            if (args.Count < 2 || !TryGetObject(args[0]!, out var target))
            {
                throw new Exception("Reflect.setPrototypeOf: target must be an object.");
            }

            var proto = args[1];
            target.SetPrototype(proto);
            return true;
        }));

        return reflect;
    }

    private static JsObject? ResolveConstructPrototype(IJsCallable newTarget, IJsCallable target)
    {
        // Step 1: use newTarget.prototype if it is an object
        if (newTarget is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("prototype", out var protoVal) &&
            protoVal is JsObject protoObj)
        {
            return protoObj;
        }

        // Step 2: try realm default for Array (handles cross-realm Array subclassing)
        if (ReferenceEquals(target, ArrayConstructor) || ReferenceEquals(newTarget, ArrayConstructor))
        {
            if (newTarget is HostFunction hostFn && hostFn.RealmState?.ArrayPrototype is JsObject realmArrayProtoFromState)
            {
                return realmArrayProtoFromState;
            }

            if (newTarget is HostFunction hostFunction && hostFunction.Realm is JsObject realmObj &&
                realmObj.TryGetProperty("Array", out var realmArrayCtor) &&
                TryGetPrototype(realmArrayCtor!, out var realmArrayProto))
            {
                return realmArrayProto;
            }

            if (ArrayPrototype is not null)
            {
                return ArrayPrototype;
            }
            // Fall through to other realm lookups if needed.
        }

        // Step 3: for other constructors, look for the intrinsic in the
        // newTarget's realm using the target's name.
        if (TryResolveRealmDefaultPrototype(newTarget, target, out var realmProto))
        {
            return realmProto;
        }

        // Step 4: fall back to target.prototype if available
        if (TryGetPrototype(target, out var targetProto))
        {
            return targetProto;
        }

        return null;
    }

    private static bool TryResolveRealmDefaultPrototype(object newTarget, IJsCallable target, out JsObject? prototype)
    {
        prototype = null;
        if (newTarget is not HostFunction hostFunction)
        {
            return false;
        }

        if (target is not IJsPropertyAccessor accessor ||
            !accessor.TryGetProperty("name", out var nameValue) ||
            nameValue is not string ctorName)
        {
            return false;
        }

        if (hostFunction.RealmState is RealmState realmState &&
            TryGetPrototypeFromRealmState(ctorName, realmState, out prototype))
        {
            return true;
        }
        if (hostFunction.RealmState is RealmState realmDefaults &&
            realmDefaults.ObjectPrototype is not null)
        {
            prototype = realmDefaults.ObjectPrototype;
            return true;
        }

        if (hostFunction.Realm is JsObject realmObj &&
            realmObj.TryGetProperty(ctorName, out var realmCtor) &&
            TryGetPrototype(realmCtor, out var realmProto))
        {
            prototype = realmProto;
            return true;
        }
        if (hostFunction.Realm is JsObject fallbackRealm &&
            fallbackRealm.TryGetProperty("Object", out var objectCtor) &&
            TryGetPrototype(objectCtor, out var objectProto))
        {
            prototype = objectProto;
            return true;
        }

        return false;
    }

    private static bool TryGetPrototypeFromRealmState(string ctorName, RealmState realmState, out JsObject? prototype)
    {
        prototype = ctorName switch
        {
            "Array" => realmState.ArrayPrototype,
            "Date" => realmState.DatePrototype,
            _ => null
        };

        return prototype is not null;
    }

    private static bool TryGetPrototype(object candidate, out JsObject? prototype)
    {
        prototype = null;

        // Prefer an explicit "prototype" property when present (e.g. constructors
        // where [[Prototype]] is Function.prototype but the instance prototype
        // lives on the .prototype data property).
        if (candidate is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("prototype", out var protoProperty) &&
            protoProperty is JsObject protoObj)
        {
            prototype = protoObj;
            return true;
        }

        if (candidate is IJsObjectLike objectLike && objectLike.Prototype is not null)
        {
            prototype = objectLike.Prototype;
            return true;
        }

        if (candidate is JsObject jsObject && jsObject.Prototype is not null)
        {
            prototype = jsObject.Prototype;
            return true;
        }

        return false;
    }
}
