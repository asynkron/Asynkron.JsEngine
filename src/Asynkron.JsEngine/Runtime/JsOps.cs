using System.Numerics;
using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

internal static class JsOps
{
    public static bool IsNullish(this object? value)
    {
        return value is null || value is Symbol sym && ReferenceEquals(sym, Symbols.Undefined);
    }

    /// <summary>
    /// ECMAScript-like ToBoolean semantics for engine values.
    /// Kept in sync with <see cref="IsTruthy"/> which is the legacy name used throughout the codebase.
    /// </summary>
    public static bool ToBoolean(object? value)
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

    public static bool IsTruthy(object? value)
    {
        return ToBoolean(value);
    }

    public static double ToNumber(object? value)
    {
        return value.ToNumber();
    }

    public static string ToJsString(object? value)
    {
        return value.ToJsString();
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

        var ln = ToNumber(left);
        var rn = ToNumber(right);
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
                var rn = ToNumber(right);
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
                var ln = ToNumber(left);
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
                return ToNumber(left).Equals(ToNumber(right));
            }

            switch (left)
            {
                case string when IsNumeric(right):
                    return ToNumber(left).Equals(ToNumber(right));
                case bool:
                    left = ToNumber(left);
                    continue;
            }

            if (right is bool)
            {
                right = ToNumber(right);
                continue;
            }

            if (left is JsObject or JsArray)
            {
                if (IsNumeric(right))
                {
                    return ToNumber(left).Equals(ToNumber(right));
                }

                if (right is string rs2)
                {
                    return string.Equals(ToJsString(left), rs2, StringComparison.Ordinal);
                }
            }

            if (right is JsObject or JsArray)
            {
                if (IsNumeric(left))
                {
                    return ToNumber(left).Equals(ToNumber(right));
                }

                if (left is string ls2)
                {
                    return string.Equals(ls2, ToJsString(right), StringComparison.Ordinal);
                }
            }

            return StrictEquals(left, right);
        }
    }

    public static bool GreaterThan(object? left, object? right)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l > r,
            (l, r) => l > r,
            (l, r) => l > r);
    }

    public static bool GreaterThanOrEqual(object? left, object? right)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l >= r,
            (l, r) => l >= r,
            (l, r) => l >= r);
    }

    public static bool LessThan(object? left, object? right)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l < r,
            (l, r) => l < r,
            (l, r) => l < r);
    }

    public static bool LessThanOrEqual(object? left, object? right)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l <= r,
            (l, r) => l <= r,
            (l, r) => l <= r);
    }

    private static bool PerformComparisonOperation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, bool> bigIntOp,
        Func<BigInteger, BigInteger, bool> mixedOp,
        Func<double, double, bool> numericOp)
    {
        switch (left)
        {
            case JsBigInt leftBigInt when right is JsBigInt rightBigInt:
                return bigIntOp(leftBigInt, rightBigInt);
            case JsBigInt lbi:
            {
                var rightNum = ToNumber(right);
                if (double.IsNaN(rightNum))
                {
                    return false;
                }

                return mixedOp(lbi.Value, new BigInteger(rightNum));
            }
        }

        switch (right)
        {
            case JsBigInt rbi:
            {
                var leftNum = ToNumber(left);
                if (double.IsNaN(leftNum))
                {
                    return false;
                }

                return mixedOp(new BigInteger(leftNum), rbi.Value);
            }
            default:
                return numericOp(ToNumber(left), ToNumber(right));
        }
    }

    public static string? ToPropertyName(object? value, EvaluationContext? context = null)
    {
        switch (value)
        {
            case null:
                return "null";
            case string s:
                return s;
            case Symbol symbol:
                return symbol.Name;
            case TypedAstSymbol jsSymbol:
                return $"@@symbol:{jsSymbol.GetHashCode()}";
            case bool b:
                return b ? "true" : "false";
            case JsObject jsObj when jsObj.TryGetValue("__value__", out var inner):
                return ToPropertyName(inner, context);
            case int i:
                return i.ToString(CultureInfo.InvariantCulture);
            case long l:
                return l.ToString(CultureInfo.InvariantCulture);
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                return d.ToString(CultureInfo.InvariantCulture);
        }

        if (value is IJsPropertyAccessor accessor &&
            TryConvertObjectToPropertyKey(accessor, out var primitive, context))
        {
            return ToPropertyName(primitive, context);
        }

        if (context is not null && context.IsThrow)
        {
            return null;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static bool TryConvertObjectToPropertyKey(IJsPropertyAccessor accessor, out object? key,
        EvaluationContext? context)
    {
        key = null;

        var attempted = false;

        if (TryInvokePropertyMethod(accessor, "toString", out var toStringResult, context))
        {
            attempted = true;
            if (IsPropertyKeyPrimitive(toStringResult))
            {
                key = toStringResult;
                return true;
            }
        }

        if (context is not null && context.IsThrow)
        {
            key = null;
            return false;
        }

        if (TryInvokePropertyMethod(accessor, "valueOf", out var valueOfResult, context))
        {
            attempted = true;
            if (IsPropertyKeyPrimitive(valueOfResult))
            {
                key = valueOfResult;
                return true;
            }
        }

        if (context is not null && context.IsThrow)
        {
            key = null;
            return false;
        }

        if (attempted)
        {
            key = null;

            var error = CreateTypeError("Cannot convert object to property key");
            if (context is not null)
            {
                context.SetThrow(error);
                return false;
            }

            throw new ThrowSignal(error);
        }

        return false;
    }

    private static bool IsPropertyKeyPrimitive(object? candidate)
    {
        if (candidate is JsObject jsObj && jsObj.TryGetValue("__value__", out var inner))
        {
            candidate = inner;
        }

        return candidate is null or string or bool or double or float or decimal or int or uint
            or long or ulong or short or ushort or byte or sbyte or Symbol or TypedAstSymbol
            or JsBigInt;
    }

    private static bool TryInvokePropertyMethod(IJsPropertyAccessor accessor, string methodName, out object? result,
        EvaluationContext? context)
    {
        result = null;
        if (!accessor.TryGetProperty(methodName, out var method) || method is not IJsCallable callable)
        {
            return false;
        }

        try
        {
            result = callable.Invoke(Array.Empty<object?>(), accessor);
            return true;
        }
        catch (ThrowSignal signal)
        {
            if (context is not null)
            {
                context.SetThrow(signal.ThrownValue);
                return true;
            }

            throw;
        }
    }

    private static object CreateTypeError(string message)
    {
        if (StandardLibrary.TypeErrorConstructor is not null)
        {
            JsObject? prototype = null;
            if (StandardLibrary.TypeErrorConstructor.TryGetProperty("prototype", out var protoVal) &&
                protoVal is JsObject protoObj)
            {
                prototype = protoObj;
            }

            var created = StandardLibrary.TypeErrorConstructor.Invoke([message], new JsObject());
            if (created is JsObject jsObj)
            {
                if (prototype is not null && jsObj.Prototype is null)
                {
                    jsObj.SetPrototype(prototype);
                }

                return jsObj;
            }
        }

        var fallback = new JsObject();
        if (StandardLibrary.TypeErrorPrototype is not null)
        {
            fallback.SetPrototype(StandardLibrary.TypeErrorPrototype);
        }
        else if (StandardLibrary.ErrorPrototype is not null)
        {
            fallback.SetPrototype(StandardLibrary.ErrorPrototype);
        }
        else if (StandardLibrary.ObjectPrototype is not null)
        {
            fallback.SetPrototype(StandardLibrary.ObjectPrototype);
        }

        fallback.SetProperty("name", "TypeError");
        fallback.SetProperty("message", message);
        return fallback;
    }

    public static string GetRequiredPropertyName(object? value, EvaluationContext? context = null)
    {
        var name = ToPropertyName(value, context);
        if (context is not null && context.IsThrow)
        {
            return string.Empty;
        }

        return name ?? throw new InvalidOperationException("Property name cannot be null.");
    }

    public static bool TryResolveArrayIndex(object? candidate, out int index, EvaluationContext? context = null)
    {
        switch (candidate)
        {
            case int i when i >= 0:
                index = i;
                return true;
            case long l and >= 0 when l <= int.MaxValue:
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

        if (candidate is JsObject jsObj && jsObj.TryGetValue("__value__", out var innerValue))
        {
            return TryResolveArrayIndex(innerValue, out index, context);
        }

        var coerced = ToPropertyName(candidate, context);
        if (coerced is not null && !ReferenceEquals(coerced, candidate))
        {
            return TryResolveArrayIndex(coerced, out index, context);
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
            case bool b:
                var booleanWrapper = StandardLibrary.CreateBooleanWrapper(b);
                if (booleanWrapper.TryGetProperty(propertyName, out value))
                {
                    return true;
                }

                break;
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

    public static bool TryGetPropertyValue(object? target, object? propertyKey, out object? value,
        EvaluationContext? context = null)
    {
        if (context is not null && context.IsThrow)
        {
            value = Symbols.Undefined;
            return false;
        }

        // Special-case TypedAstSymbol keys used for @@iterator / @@asyncIterator
        // so that non-callable values are treated as missing, allowing helpers
        // like Babel's _createForOfIteratorHelperLoose to fall back to their
        // Array/@@iterator code paths instead of attempting to call a symbol.
        if (TryGetArrayLikeValue(target, propertyKey, out value, context))
        {
            return true;
        }

        if (context is not null && context.IsThrow)
        {
            value = Symbols.Undefined;
            return false;
        }

        var propertyName = ToPropertyName(propertyKey, context);
        if (context is not null && context.IsThrow)
        {
            value = Symbols.Undefined;
            return false;
        }
        if (propertyName is null)
        {
            value = Symbols.Undefined;
            return true;
        }

        return TryGetPropertyValue(target, propertyName, out value);
    }

    private static bool TryGetArrayLikeValue(object? target, object? propertyKey, out object? value,
        EvaluationContext? context)
    {
        if (target is JsArray jsArray && TryResolveArrayIndex(propertyKey, out var arrayIndex, context))
        {
            value = jsArray.GetElement(arrayIndex);
            return true;
        }

        if (target is TypedArrayBase typedArray && TryResolveArrayIndex(propertyKey, out var typedIndex, context))
        {
            value = typedIndex >= 0 && typedIndex < typedArray.Length
                ? typedArray.GetElement(typedIndex)
                : Symbols.Undefined;
            return true;
        }

        value = null;
        return false;
    }

    public static void AssignPropertyValue(object? target, object? propertyKey, object? value,
        EvaluationContext? context = null)
    {
        if (TryAssignArrayLikeValue(target, propertyKey, value, context))
        {
            return;
        }

        var propertyName = GetRequiredPropertyName(propertyKey, context);

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

    private static bool TryAssignArrayLikeValue(object? target, object? propertyKey, object? value,
        EvaluationContext? context)
    {
        if (target is JsArray jsArray && TryResolveArrayIndex(propertyKey, out var index, context))
        {
            jsArray.SetElement(index, value);
            return true;
        }

        if (target is TypedArrayBase typedArray && TryResolveArrayIndex(propertyKey, out var typedIndex, context))
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

    public static bool DeletePropertyValue(object? target, object? propertyKey, EvaluationContext? context = null)
    {
        if (target is JsArray jsArray)
        {
            if (TryResolveArrayIndex(propertyKey, out var arrayIndex, context))
            {
                return jsArray.DeleteElement(arrayIndex);
            }

            var propertyName = ToPropertyName(propertyKey, context);
            return propertyName is null || jsArray.DeleteProperty(propertyName);
        }

        if (target is TypedArrayBase typedArray)
        {
            if (TryResolveArrayIndex(propertyKey, out _, context))
            {
                return false;
            }

            var propertyName = ToPropertyName(propertyKey, context);
            return propertyName is null || typedArray.DeleteProperty(propertyName);
        }

        var resolvedName = ToPropertyName(propertyKey, context);
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
