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

    public static bool TryGetPropertyValue(object? target, string propertyName, out object? value)
    {
        if (target is IJsPropertyAccessor propertyAccessor)
        {
            return propertyAccessor.TryGetProperty(propertyName, out value);
        }

        switch (target)
        {
            case double num:
                var numberWrapper = StandardLibrary.CreateNumberWrapper(num);
                if (numberWrapper.TryGetProperty(propertyName, out value))
                {
                    return true;
                }

                break;
            case string str:
                if (propertyName == "length")
                {
                    value = (double)str.Length;
                    return true;
                }

                if (int.TryParse(propertyName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                    index >= 0 && index < str.Length)
                {
                    value = str[index].ToString();
                    return true;
                }

                var stringWrapper = StandardLibrary.CreateStringWrapper(str);
                if (stringWrapper.TryGetProperty(propertyName, out value))
                {
                    return true;
                }

                break;
        }

        value = null;
        return false;
    }

    public static bool TryGetPropertyValue(object? target, object? propertyKey, out object? value)
    {
        if (TryGetArrayLikeValue(target, propertyKey, out value))
        {
            return true;
        }

        var propertyName = ToPropertyName(propertyKey);
        if (propertyName is null)
        {
            value = Symbols.Undefined;
            return true;
        }

        return TryGetPropertyValue(target, propertyName, out value);
    }

    private static bool TryGetArrayLikeValue(object? target, object? propertyKey, out object? value)
    {
        if (target is JsArray jsArray && TryResolveArrayIndex(propertyKey, out var arrayIndex))
        {
            value = jsArray.GetElement(arrayIndex);
            return true;
        }

        if (target is TypedArrayBase typedArray && TryResolveArrayIndex(propertyKey, out var typedIndex))
        {
            value = typedIndex >= 0 && typedIndex < typedArray.Length
                ? typedArray.GetElement(typedIndex)
                : Symbols.Undefined;
            return true;
        }

        value = null;
        return false;
    }

    public static void AssignPropertyValue(object? target, object? propertyKey, object? value)
    {
        if (TryAssignArrayLikeValue(target, propertyKey, value))
        {
            return;
        }

        var propertyName = ToPropertyName(propertyKey)
                           ?? throw new InvalidOperationException("Property name cannot be null.");

        AssignPropertyValueByName(target, propertyName, value);
    }

    public static void AssignPropertyValueByName(object? target, string propertyName, object? value)
    {
        if (target is IJsPropertyAccessor accessor)
        {
            accessor.SetProperty(propertyName, value);
            return;
        }

        throw new InvalidOperationException($"Cannot assign property '{propertyName}' on value '{target}'.");
    }

    private static bool TryAssignArrayLikeValue(object? target, object? propertyKey, object? value)
    {
        if (target is JsArray jsArray && TryResolveArrayIndex(propertyKey, out var index))
        {
            jsArray.SetElement(index, value);
            return true;
        }

        if (target is TypedArrayBase typedArray && TryResolveArrayIndex(propertyKey, out var typedIndex))
        {
            if (typedIndex < 0 || typedIndex >= typedArray.Length)
            {
                return true;
            }

            var numericValue = value switch
            {
                double d => d,
                int i => i,
                long l => l,
                float f => f,
                bool b => b ? 1.0 : 0.0,
                null => 0.0,
                _ => 0.0
            };

            typedArray.SetElement(typedIndex, numericValue);
            return true;
        }

        return false;
    }

    public static bool DeletePropertyValue(object? target, object? propertyKey)
    {
        if (target is JsArray jsArray)
        {
            if (TryResolveArrayIndex(propertyKey, out var arrayIndex))
            {
                return jsArray.DeleteElement(arrayIndex);
            }

            var propertyName = ToPropertyName(propertyKey);
            return propertyName is null || jsArray.DeleteProperty(propertyName);
        }

        if (target is TypedArrayBase typedArray)
        {
            if (TryResolveArrayIndex(propertyKey, out _))
            {
                return false;
            }

            var propertyName = ToPropertyName(propertyKey);
            return propertyName is null || typedArray.DeleteProperty(propertyName);
        }

        var resolvedName = ToPropertyName(propertyKey);
        if (resolvedName is null)
        {
            return true;
        }

        if (target is JsObject jsObject)
        {
            if (!jsObject.ContainsKey(resolvedName))
            {
                return true;
            }

            return jsObject.Remove(resolvedName);
        }

        // Deleting primitives or other non-object values is a no-op that succeeds
        return true;
    }
}
