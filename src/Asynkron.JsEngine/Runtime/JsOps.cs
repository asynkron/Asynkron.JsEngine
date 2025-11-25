using System.Globalization;
using System.Numerics;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Runtime;

internal static class JsOps
{
    private enum NumericKind
    {
        Number,
        BigInt,
        Error
    }

    public static bool IsNullish(this object? value)
    {
        return value is null || (value is Symbol sym && ReferenceEquals(sym, Symbols.Undefined));
    }

    /// <summary>
    ///     ECMAScript-like ToBoolean semantics for engine values.
    ///     Kept in sync with <see cref="IsTruthy" /> which is the legacy name used throughout the codebase.
    /// </summary>
    public static bool ToBoolean(object? value)
    {
        return value switch
        {
            null => false,
            Symbol sym when ReferenceEquals(sym, Symbols.Undefined) => false,
            IIsHtmlDda => false,
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

    public static double ToNumber(object? value, EvaluationContext? context = null)
    {
        return ToNumberWithContext(value, context);
    }

    public static object ToNumeric(object? value, EvaluationContext? context = null)
    {
        var result = ToNumericCore(value, context);
        return result.Kind switch
        {
            NumericKind.BigInt => result.Value!,
            NumericKind.Number => result.Value!,
            _ => double.NaN
        };
    }

    public static double ToNumberWithContext(object? value, EvaluationContext? context = null)
    {
        var result = ToNumericCore(value, context);
        return result.Kind switch
        {
            NumericKind.Number => (double)result.Value!,
            NumericKind.BigInt => (double)((JsBigInt)result.Value!).Value,
            _ => double.NaN
        };
    }

    private static (NumericKind Kind, object? Value) ToNumericCore(
        object? value,
        EvaluationContext? context)
    {
        while (true)
        {
            switch (value)
            {
                case null:
                    return (NumericKind.Number, 0d);
                case Symbol sym when ReferenceEquals(sym, Symbols.Undefined):
                case IIsHtmlDda:
                    return (NumericKind.Number, double.NaN);
                case Symbol:
                case TypedAstSymbol:
                    {
                        var error = CreateTypeError("Cannot convert a Symbol value to a number", context);
                        if (context is null)
                        {
                            throw new ThrowSignal(error);
                        }

                        context.SetThrow(error);
                        return (NumericKind.Error, null);
                    }
                case JsBigInt bigInt:
                    return (NumericKind.BigInt, bigInt);
                case double d:
                    return (NumericKind.Number, d);
                case float f:
                    return (NumericKind.Number, (double)f);
                case decimal m:
                    return (NumericKind.Number, (double)m);
                case int i:
                    return (NumericKind.Number, (double)i);
                case uint ui:
                    return (NumericKind.Number, (double)ui);
                case long l:
                    return (NumericKind.Number, (double)l);
                case ulong ul:
                    return (NumericKind.Number, (double)ul);
                case short s:
                    return (NumericKind.Number, (double)s);
                case ushort us:
                    return (NumericKind.Number, (double)us);
                case byte b:
                    return (NumericKind.Number, (double)b);
                case sbyte sb:
                    return (NumericKind.Number, (double)sb);
                case bool flag:
                    return (NumericKind.Number, flag ? 1d : 0d);
                case string str:
                    return (NumericKind.Number, NumericStringParser.ParseJsNumber(str));
            }

            switch (value)
            {
                case JsObject jsObj when jsObj.TryGetValue("__value__", out var inner):
                    value = inner;
                    continue;
                case IJsPropertyAccessor accessor
                    when TryConvertToNumericPrimitive(accessor, out var primitive, context):
                    value = primitive;
                    continue;
                case IJsPropertyAccessor accessor when (context?.IsThrow == true):
                    return (NumericKind.Error, null);
                case IJsPropertyAccessor accessor:
                {
                    var typeError = CreateTypeError("Cannot convert object to number", context);
                    if (context is null)
                    {
                        throw new ThrowSignal(typeError);
                    }

                    context.SetThrow(typeError);
                    return (NumericKind.Error, null);
                }
                default:
                    throw new InvalidOperationException($"Cannot convert value '{value}' to a number.");
            }
        }
    }

    private static bool TryConvertToNumericPrimitive(IJsPropertyAccessor accessor, out object? primitive,
        EvaluationContext? context)
    {
        primitive = null;

        var toPrimitiveKey = TypedAstSymbol.For("Symbol.toPrimitive");
        var symbolPropertyName = $"@@symbol:{toPrimitiveKey.GetHashCode()}";
        if (accessor.TryGetProperty(symbolPropertyName, out var toPrimitive) && toPrimitive is IJsCallable toPrimFn)
        {
            try
            {
                var result = toPrimFn.Invoke(["number"], accessor);
                if ((result is not IJsPropertyAccessor || result is TypedAstSymbol or Symbol) &&
                    result is not JsObject)
                {
                    primitive = result;
                    return true;
                }
            }
            catch (ThrowSignal signal) when (context is not null)
            {
                context.SetThrow(signal.ThrownValue);
                return false;
            }
        }

        if (TryInvokePropertyMethod(accessor, "valueOf", out var valueOfResult, context) &&
            (valueOfResult is not IJsPropertyAccessor || valueOfResult is TypedAstSymbol or Symbol) &&
            valueOfResult is not JsObject)
        {
            primitive = valueOfResult;
            return true;
        }

        if (context?.IsThrow == true)
        {
            return false;
        }

        if (!TryInvokePropertyMethod(accessor, "toString", out var toStringResult, context) ||
            (toStringResult is IJsPropertyAccessor && toStringResult is not TypedAstSymbol and not Symbol) ||
            toStringResult is JsObject)
        {
            return false;
        }

        primitive = toStringResult;
        return true;
    }

    public static object? ToPrimitive(object? value, string hint, EvaluationContext? context = null)
    {
        if (value is not IJsPropertyAccessor accessor)
        {
            return value;
        }

        var toPrimitiveKey = TypedAstSymbol.For("Symbol.toPrimitive");
        var symbolPropertyName = $"@@symbol:{toPrimitiveKey.GetHashCode()}";
        if (accessor.TryGetProperty(symbolPropertyName, out var toPrimitive) && toPrimitive is IJsCallable toPrimFn)
        {
            try
            {
                var result = toPrimFn.Invoke([hint], accessor);
                if (result is IJsPropertyAccessor or JsObject)
                {
                    throw StandardLibrary.ThrowTypeError("Cannot convert object to primitive value", context);
                }

                return result;
            }
            catch (ThrowSignal signal) when (context is not null)
            {
                context.SetThrow(signal.ThrownValue);
                return value;
            }
        }

        var methods = hint == "string"
            ? new[] { "toString", "valueOf" }
            : new[] { "valueOf", "toString" };

        foreach (var methodName in methods)
        {
            if (!TryInvokePropertyMethod(accessor, methodName, out var result, context))
            {
                continue;
            }

            if (context?.IsThrow == true)
            {
                return value;
            }

            if (result is not IJsPropertyAccessor && result is not JsObject)
            {
                return result;
            }
        }

        if (accessor is HostFunction)
        {
            return "function() { [native code] }";
        }

        throw StandardLibrary.ThrowTypeError("Cannot convert object to primitive value", context);
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

    public static bool LooseEquals(object? left, object? right, EvaluationContext? context = null)
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
                var rn = ToNumber(right, context);
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
                var ln = ToNumber(left, context);
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
                return ToNumber(left, context).Equals(ToNumber(right, context));
            }

            switch (left)
            {
                case string when IsNumeric(right):
                    return ToNumber(left, context).Equals(ToNumber(right, context));
                case bool:
                    left = ToNumber(left, context);
                    continue;
            }

            if (right is bool)
            {
                right = ToNumber(right, context);
                continue;
            }

            if (left is JsObject or JsArray)
            {
                if (IsNumeric(right))
                {
                    return ToNumber(left, context).Equals(ToNumber(right, context));
                }

                if (right is string rs2)
                {
                    return string.Equals(ToJsString(left), rs2, StringComparison.Ordinal);
                }
            }

            if (right is not (JsObject or JsArray))
            {
                return StrictEquals(left, right);
            }

            if (IsNumeric(left))
            {
                return ToNumber(left, context).Equals(ToNumber(right, context));
            }

            if (left is string ls2)
            {
                return string.Equals(ls2, ToJsString(right), StringComparison.Ordinal);
            }

            return StrictEquals(left, right);
        }
    }

    public static bool GreaterThan(object? left, object? right, EvaluationContext? context = null)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l > r,
            (l, r) => l > r,
            (l, r) => l > r,
            context);
    }

    public static bool GreaterThanOrEqual(object? left, object? right, EvaluationContext? context = null)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l >= r,
            (l, r) => l >= r,
            (l, r) => l >= r,
            context);
    }

    public static bool LessThan(object? left, object? right, EvaluationContext? context = null)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l < r,
            (l, r) => l < r,
            (l, r) => l < r,
            context);
    }

    public static bool LessThanOrEqual(object? left, object? right, EvaluationContext? context = null)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l <= r,
            (l, r) => l <= r,
            (l, r) => l <= r,
            context);
    }

    private static bool PerformComparisonOperation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, bool> bigIntOp,
        Func<BigInteger, BigInteger, bool> mixedOp,
        Func<double, double, bool> numericOp,
        EvaluationContext? context)
    {
        switch (left)
        {
            case JsBigInt leftBigInt when right is JsBigInt rightBigInt:
                return bigIntOp(leftBigInt, rightBigInt);
            case JsBigInt lbi:
            {
                var rightNum = ToNumber(right, context);
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
                var leftNum = ToNumber(left, context);
                if (double.IsNaN(leftNum))
                {
                    return false;
                }

                return mixedOp(new BigInteger(leftNum), rbi.Value);
            }
            default:
                return numericOp(ToNumber(left, context), ToNumber(right, context));
        }
    }

    public static string? ToPropertyName(object? value, EvaluationContext? context = null)
    {
        while (true)
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
                case int i:
                    return i.ToString(CultureInfo.InvariantCulture);
                case long l:
                    return l.ToString(CultureInfo.InvariantCulture);
                case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                    return d.ToString(CultureInfo.InvariantCulture);
            }

            if (value is IJsPropertyAccessor accessor)
            {
                if (TryConvertObjectToPropertyKey(accessor, out var primitive, context))
                {
                    value = primitive;
                    continue;
                }

                if (context?.IsThrow == true)
                {
                    return null;
                }

                // Fallback: use general ToPrimitive(string) semantics before converting to string
                var primitiveValue = ToPrimitive(accessor, "string", context);
                if (context?.IsThrow == true)
                {
                    return null;
                }

                value = primitiveValue;
                continue;
            }

            if (context?.IsThrow == true)
            {
                return null;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
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

        if (context?.IsThrow == true)
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

        if (context?.IsThrow == true)
        {
            key = null;
            return false;
        }

        if (!attempted)
        {
            return false;
        }

        key = null;

        var error = CreateTypeError("Cannot convert object to property key", context);
        if (context is null)
        {
            throw new ThrowSignal(error);
        }

        context.SetThrow(error);
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
            result = callable.Invoke([], accessor);
            return context?.IsThrow != true;
        }
        catch (ThrowSignal signal)
        {
            if (context is not null)
            {
                context.SetThrow(signal.ThrownValue);
                return false;
            }

            throw;
        }
    }

    private static object CreateTypeError(string message, EvaluationContext? context)
    {
        var realm = context?.RealmState;
        return StandardLibrary.CreateTypeError(message, context, realm);
    }

    public static string GetRequiredPropertyName(object? value, EvaluationContext? context = null)
    {
        var name = ToPropertyName(value, context);
        if (context?.IsThrow == true)
        {
            return string.Empty;
        }

        return name ?? throw new InvalidOperationException("Property name cannot be null.");
    }

    public static bool TryResolveArrayIndex(object? candidate, out int index, EvaluationContext? context = null)
    {
        while (true)
        {
            switch (candidate)
            {
                case int i when i >= 0:
                    index = i;
                    return true;
                case long l and >= 0 and <= int.MaxValue:
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
                case string s when int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
                                   parsed >= 0:
                    index = parsed;
                    return true;
            }

            if (candidate is JsObject jsObj && jsObj.TryGetValue("__value__", out var innerValue))
            {
                candidate = innerValue;
                continue;
            }

            var coerced = ToPropertyName(candidate, context);
            if (coerced is not null && !ReferenceEquals(coerced, candidate))
            {
                candidate = coerced;
                continue;
            }

            index = 0;
            return false;
        }
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

        if (value is IIsHtmlDda)
        {
            return "undefined";
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

    public static bool TryGetPropertyValue(object? target, string propertyName, out object? value,
        EvaluationContext? context = null)
    {
        if (target is IJsPropertyAccessor propertyAccessor)
        {
            return propertyAccessor.TryGetProperty(propertyName, out value);
        }

        switch (target)
        {
            case bool b:
                var booleanWrapper = StandardLibrary.CreateBooleanWrapper(b, context);
                if (booleanWrapper.TryGetProperty(propertyName, out value))
                {
                    return true;
                }

                break;
            case double num:
                var numberWrapper = StandardLibrary.CreateNumberWrapper(num, context);
                if (numberWrapper.TryGetProperty(propertyName, out value))
                {
                    return true;
                }

                break;
            case JsBigInt bigInt:
                var bigIntWrapper = StandardLibrary.CreateBigIntWrapper(bigInt, context);
                if (bigIntWrapper.TryGetProperty(propertyName, out value))
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

                var stringWrapper = StandardLibrary.CreateStringWrapper(str, context);
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
        if (context?.IsThrow == true)
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

        if (context?.IsThrow == true)
        {
            value = Symbols.Undefined;
            return false;
        }

        var propertyName = ToPropertyName(propertyKey, context);
        if (context?.IsThrow == true)
        {
            value = Symbols.Undefined;
            return false;
        }

        if (propertyName is null)
        {
            value = Symbols.Undefined;
            return true;
        }

        return TryGetPropertyValue(target, propertyName, out value, context);
    }

    public static bool IsConstructor(object? value)
    {
        if (value is HostFunction host)
        {
            return host is { IsConstructor: true, DisallowConstruct: false };
        }

        if (value is ICallableMetadata { IsArrowFunction: true })
        {
            return false;
        }

        return value is IJsCallable;
    }

    private static bool TryGetArrayLikeValue(object? target, object? propertyKey, out object? value,
        EvaluationContext? context)
    {
        if (target is JsArray jsArray && TryResolveArrayIndex(propertyKey, out var arrayIndex, context))
        {
            if (arrayIndex >= 0 && jsArray.HasOwnIndex((uint)arrayIndex))
            {
                value = jsArray.GetElement(arrayIndex);
                return true;
            }

            value = null;
            return false;
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
        try
        {
            if (TryAssignArrayLikeValue(target, propertyKey, value, context))
            {
                return;
            }

            var propertyName = GetRequiredPropertyName(propertyKey, context);

            AssignPropertyValueByName(target, propertyName, value);
        }
        catch (ThrowSignal signal)
        {
            if (context is not null)
            {
                context.SetThrow(signal.ThrownValue);
                return;
            }

            throw;
        }
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
        if (target is JsArray jsArray)
        {
            var propertyName = ToPropertyName(propertyKey, context);
            if (context?.IsThrow == true)
            {
                return true;
            }

            if (string.Equals(propertyName, "length", StringComparison.Ordinal))
            {
                jsArray.SetLength(value, context);
                return true;
            }

            if (TryResolveArrayIndex(propertyKey, out var index, context))
            {
                jsArray.SetElement(index, value);
                return true;
            }
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

        if (target is HostFunction hostFunc)
        {
            var propertyName = ToPropertyName(propertyKey, context);
            return propertyName is null || hostFunc.DeleteProperty(propertyName);
        }

        if (target is ModuleNamespace moduleNamespace)
        {
            var propertyName = ToPropertyName(propertyKey, context);
            return propertyName is null || moduleNamespace.Delete(propertyName);
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

        if (target is JsArgumentsObject argumentsObject)
        {
            var propertyName = ToPropertyName(propertyKey, context);
            return propertyName is null || argumentsObject.Delete(propertyName);
        }

        var resolvedName = ToPropertyName(propertyKey, context);
        if (resolvedName is null)
        {
            return true;
        }

        if (target is IJsObjectLike objectLike)
        {
            return objectLike.Delete(resolvedName);
        }

        // Deleting primitives or other non-object values is a no-op that succeeds
        return true;
    }
}
