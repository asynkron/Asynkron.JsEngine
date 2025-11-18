using System.Numerics;
using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

internal static class JsOps
{
    public static bool IsNullish(object? value)
    {
        return value is null || value is Symbol sym && ReferenceEquals(sym, Symbols.Undefined);
    }

    public static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            Symbol sym when ReferenceEquals(sym, Symbols.Undefined) => false,
            bool b => b,
            double d => !double.IsNaN(d) && Math.Abs(d) > double.Epsilon,
            float f => !float.IsNaN(f) && Math.Abs(f) > float.Epsilon,
            string s => s.Length > 0,
            _ => true
        };
    }

    public static bool StrictEquals(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return left is not double d || !double.IsNaN(d);
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is JsBigInt lbi && right is JsBigInt rbi)
        {
            return lbi == rbi;
        }

        if ((left is JsBigInt && IsNumeric(right)) || (IsNumeric(left) && right is JsBigInt))
        {
            return false;
        }

        if (!IsNumeric(left) || !IsNumeric(right))
        {
            return left.GetType() == right.GetType() && Equals(left, right);
        }

        var ln = left.ToNumber();
        var rn = right.ToNumber();
        if (double.IsNaN(ln) || double.IsNaN(rn))
        {
            return false;
        }

        return ln.Equals(rn);
    }

    public static bool LooseEquals(object? left, object? right)
    {
        while (true)
        {
            if (IsNullish(left) && IsNullish(right))
            {
                return true;
            }

            if (IsNullish(left) || IsNullish(right))
            {
                return false;
            }

            if (left?.GetType() == right?.GetType())
            {
                return StrictEquals(left, right);
            }

            if (left is JsBigInt lbi && IsNumeric(right))
            {
                var rn = right.ToNumber();
                if (double.IsNaN(rn) || double.IsInfinity(rn))
                {
                    return false;
                }

                if (rn == Math.Floor(rn))
                {
                    return lbi.Value == new BigInteger(rn);
                }

                return false;
            }

            if (IsNumeric(left) && right is JsBigInt rbi)
            {
                var ln = left.ToNumber();
                if (double.IsNaN(ln) || double.IsInfinity(ln))
                {
                    return false;
                }

                if (ln == Math.Floor(ln))
                {
                    return new BigInteger(ln) == rbi.Value;
                }

                return false;
            }

            switch (left)
            {
                case JsBigInt lbi2 when right is string rs:
                    try
                    {
                        var converted = new JsBigInt(rs.Trim());
                        return lbi2 == converted;
                    }
                    catch
                    {
                        return false;
                    }
                case string ls when right is JsBigInt rbi2:
                    try
                    {
                        var converted = new JsBigInt(ls.Trim());
                        return converted == rbi2;
                    }
                    catch
                    {
                        return false;
                    }
            }

            if (IsNumeric(left) && right is string)
            {
                return left.ToNumber().Equals(right.ToNumber());
            }

            switch (left)
            {
                case string when IsNumeric(right):
                    return left.ToNumber().Equals(right.ToNumber());
                case bool:
                    left = left.ToNumber();
                    continue;
            }

            if (right is bool)
            {
                right = right.ToNumber();
                continue;
            }

            if (left is JsObject or JsArray)
            {
                if (IsNumeric(right))
                {
                    return left.ToNumber().Equals(right.ToNumber());
                }

                if (right is string rs2)
                {
                    return string.Equals(left.ToJsString(), rs2, StringComparison.Ordinal);
                }
            }

            if (right is JsObject or JsArray)
            {
                if (IsNumeric(left))
                {
                    return left.ToNumber().Equals(right.ToNumber());
                }

                if (left is string ls2)
                {
                    return string.Equals(ls2, right.ToJsString(), StringComparison.Ordinal);
                }
            }

            return StrictEquals(left, right);
        }
    }

    public static string? ToPropertyName(object? value)
    {
        return value switch
        {
            null => "null",
            string s => s,
            Symbol symbol => symbol.Name,
            TypedAstSymbol jsSymbol => $"@@symbol:{jsSymbol.GetHashCode()}",
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d when !double.IsNaN(d) && !double.IsInfinity(d) => d.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    public static bool TryResolveArrayIndex(object? candidate, out int index)
    {
        switch (candidate)
        {
            case int i when i >= 0:
                index = i;
                return true;
            case long l when l >= 0 && l <= int.MaxValue:
                index = (int)l;
                return true;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                if (d < 0)
                {
                    break;
                }
                var truncated = Math.Truncate(d);
                if (Math.Abs(truncated - d) > double.Epsilon)
                {
                    break;
                }
                if (truncated > int.MaxValue)
                {
                    break;
                }
                index = (int)truncated;
                return true;
            case JsBigInt bigInt when bigInt.Value >= BigInteger.Zero && bigInt.Value <= int.MaxValue:
                index = (int)bigInt.Value;
                return true;
            case string s when int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0:
                index = parsed;
                return true;
        }

        index = 0;
        return false;
    }

    public static string GetTypeofString(object? value)
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

    private static bool IsNumeric(object? value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }
}