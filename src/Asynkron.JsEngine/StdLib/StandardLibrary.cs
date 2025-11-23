using System.Globalization;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

/// <summary>
///     Provides standard JavaScript library objects and functions (Math, JSON, etc.)
/// </summary>
public static partial class StandardLibrary
{
    internal static object CreateTypeError(string message, EvaluationContext? context = null, RealmState? realm = null)
    {
        realm ??= context?.RealmState;
        if (realm?.TypeErrorConstructor is IJsCallable callable)
        {
            return callable.Invoke([message], null) ?? new InvalidOperationException(message);
        }

        return new InvalidOperationException(message);
    }

    //TODO: why is this not used?
    // ECMAScript Type(x) == "bigint" when x is JsBigInt.
    private static string TypeOf(object? value)
    {
        return value switch
        {
            null => "object",
            Symbol sym when ReferenceEquals(sym, Symbols.Undefined) => "undefined",
            TypedAstSymbol => "symbol",
            JsBigInt => "bigint",
            _ => value switch
            {
                bool => "boolean",
                double or float or decimal or int or uint or long or ulong or short or ushort or byte
                    or sbyte => "number",
                string => "string",
                IJsCallable => "function",
                _ => "object"
            }
        };
    }

    internal static object CreateRangeError(string message, EvaluationContext? context = null, RealmState? realm = null)
    {
        realm ??= context?.RealmState;
        if (realm?.RangeErrorConstructor is IJsCallable callable)
        {
            return callable.Invoke([message], null) ?? new InvalidOperationException(message);
        }

        return new InvalidOperationException(message);
    }

    internal static ThrowSignal ThrowTypeError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        return new ThrowSignal(CreateTypeError(message, context, realm));
    }

    internal static ThrowSignal ThrowRangeError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        return new ThrowSignal(CreateRangeError(message, context, realm));
    }

    internal static ThrowSignal ThrowSyntaxError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        return new ThrowSignal(CreateSyntaxError(message, context, realm));
    }

    private static JsBigInt ThisBigIntValue(object? receiver)
    {
        return receiver switch
        {
            JsBigInt bi => bi,
            JsObject obj when obj.TryGetValue("__value__", out var inner) && inner is JsBigInt wrapped => wrapped,
            _ => throw ThrowTypeError("BigInt.prototype method called on incompatible receiver")
        };
    }

    internal static object CreateSyntaxError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        realm ??= context?.RealmState;
        if (realm?.SyntaxErrorConstructor is IJsCallable callable)
        {
            return callable.Invoke([message], null) ?? new InvalidOperationException(message);
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

        if (text.EndsWith('n'))
        {
            throw ThrowSyntaxError("Invalid BigInt literal");
        }

        var sign = 1;
        if (text.StartsWith('+') || text.StartsWith('-'))
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
            foreach (var t in text)
            {
                if (t is < '0' or > '9')
                {
                    throw ThrowSyntaxError("Invalid BigInt literal");
                }
            }
        }

        if (text.Length == 0 || !TryParseBigIntWithBase(text, numberBase, sign, out var parsed))
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
    ///     Converts a JavaScript value to its string representation, handling functions appropriately.
    /// </summary>
    private static string JsValueToString(object? value)
    {
        return value switch
        {
            null => "null",
            string s => s,
            bool b => b ? "true" : "false",
            double d => d.ToString(CultureInfo.InvariantCulture),
            IJsCallable => "function() { [native code] }",
            _ => value.ToString() ?? ""
        };
    }
}
