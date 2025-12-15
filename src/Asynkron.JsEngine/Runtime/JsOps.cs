using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Runtime;

/// <summary>
/// Enum for ToPrimitive hint to avoid string comparisons in hot paths.
/// </summary>
internal enum ToPrimitiveHint
{
    Default,
    Number,
    String
}

internal static class JsOps
{
    internal const double NegativeZero = -0.0d;

    // Cached hint strings for ToPrimitive (used when calling [Symbol.toPrimitive])
    private static readonly string HintDefault = "default";
    private static readonly string HintNumber = "number";
    private static readonly string HintString = "string";

    // Static arrays for ToPrimitive to avoid allocation on every call
    private static readonly string[] StringHintMethods = ["toString", "valueOf"];
    private static readonly string[] DefaultHintMethods = ["valueOf", "toString"];

    internal static double MathPow(double baseValue, double exponent)
    {
        if (double.IsNaN(exponent))
        {
            return double.NaN;
        }

        if (exponent == 0)
        {
            // Covers both +0 and -0 exponents.
            return 1;
        }

        if (double.IsNaN(baseValue))
        {
            return double.NaN;
        }

        if (double.IsInfinity(exponent))
        {
            var abs = Math.Abs(baseValue);
            if (abs == 1)
            {
                return double.NaN;
            }

            if (abs > 1)
            {
                return exponent > 0 ? double.PositiveInfinity : 0.0;
            }

            return exponent > 0 ? 0.0 : double.PositiveInfinity;
        }

        if (double.IsInfinity(baseValue))
        {
            var sign = Math.Sign(baseValue);
            if (exponent > 0)
            {
                return sign < 0 && IsOddInteger(exponent)
                    ? double.NegativeInfinity
                    : double.PositiveInfinity;
            }

            return sign < 0 && IsOddInteger(exponent) ? NegativeZero : 0.0;
        }

        return Math.Pow(baseValue, exponent);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsOddInteger(double value)
    {
        return double.IsFinite(value) && value % 1 == 0 && Math.Abs(value % 2) == 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNullish(this object? value)
    {
        if (value is JsValue jsValue)
        {
            value = jsValue.ToObject();
        }
        return value is null ||
               (value is Symbol sym && ReferenceEquals(sym, Symbol.Undefined));
    }

    /// <summary>
    ///     ECMAScript-like ToBoolean semantics for engine values.
    ///     Kept in sync with <see cref="IsTruthy" /> which is the legacy name used throughout the codebase.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ToBoolean(object? value)
    {
        return value switch
        {
            null => false,
            JsValue jsValue => ToBoolean(jsValue.ToObject()),
            Symbol sym when ReferenceEquals(sym, Symbol.Undefined) => false,
            IIsHtmlDda => false,
            bool b => b,
            double d => !double.IsNaN(d) && d != 0,
            float f => !float.IsNaN(f) && f != 0,
            string s => s.Length > 0,
            JsBigInt bi => !bi.Value.IsZero,
            _ => true
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        return ToNumericResult(value, context).ToObject();
    }

    /// <summary>
    /// Converts a value to numeric without boxing. Use this for internal arithmetic operations.
    /// Only call ToObject() on the result when you need to return to JS.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static NumericResult ToNumericResult(JsValue value, EvaluationContext? context = null)
    {
        switch (value.Kind)
        {
            case JsValueKind.Undefined:
                return new NumericResult(double.NaN);
            case JsValueKind.Null:
                return new NumericResult(0d);
            case JsValueKind.Boolean:
            case JsValueKind.Number:
                return new NumericResult(value.NumberValue);
            case JsValueKind.BigInt when value.ObjectValue is JsBigInt jsBigInt:
                return new NumericResult(jsBigInt);
            case JsValueKind.String:
            case JsValueKind.Symbol:
            case JsValueKind.Object:
                return ToNumericResult(value.ObjectValue, context);
            default:
                return new NumericResult(double.NaN);
        }
    }

    internal static NumericResult ToNumericResult(object? value, EvaluationContext? context = null)
    {
        // Fast paths for already-numeric types (avoid full conversion)
        if (value is double d) return new NumericResult(d);
        if (value is JsBigInt bi) return new NumericResult(bi);
        if (value is int i) return new NumericResult(i);

        return ToNumericCore(value, context);
    }

    public static double ToNumberWithContext(object? value, EvaluationContext? context = null)
    {
        var result = ToNumericCore(value, context);
        return result.Kind switch
        {
            NumericKind.Number => result.NumberValue,
            NumericKind.BigInt => (double)result.BigIntValue!.Value,
            _ => double.NaN
        };
    }

    private static NumericResult ToNumericCore(
        object? value,
        EvaluationContext? context)
    {
        var iterations = 0;
        while (true)
        {
            if (++iterations > 20)
            {
                return new NumericResult(double.NaN);
            }

            switch (value)
            {
                case null:
                    return new NumericResult(0d);
                case JsValue jsValue:
                    switch (jsValue.Kind)
                    {
                        case JsValueKind.Undefined:
                            return new NumericResult(double.NaN);
                        case JsValueKind.Null:
                            return new NumericResult(0d);
                        case JsValueKind.Boolean:
                        case JsValueKind.Number:
                            return new NumericResult(jsValue.NumberValue);
                        case JsValueKind.BigInt:
                            if (jsValue.ObjectValue is JsBigInt jsBigInt)
                            {
                                return new NumericResult(jsBigInt);
                            }
                            return new NumericResult(double.NaN);
                        case JsValueKind.String:
                        case JsValueKind.Symbol:
                        case JsValueKind.Object:
                            value = jsValue.ObjectValue;
                            continue;
                        default:
                            value = jsValue.ToObject();
                            continue;
                    }
                case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
                case IIsHtmlDda:
                    return new NumericResult(double.NaN);
                case Symbol:
                case TypedAstSymbol:
                {
                    var error = CreateTypeError("Cannot convert a Symbol value to a number", context);
                    if (context is null)
                    {
                        throw new ThrowSignal(JsValue.FromObject(error));
                    }

                    context.SetThrow(JsValue.FromObject(error));
                    return NumericResult.Error;
                }
                case JsBigInt bigInt:
                    return new NumericResult(bigInt);
                case double d:
                    return new NumericResult(d);
                case float f:
                    return new NumericResult(f);
                case decimal m:
                    return new NumericResult((double)m);
                case int i:
                    return new NumericResult(i);
                case uint ui:
                    return new NumericResult(ui);
                case long l:
                    return new NumericResult(l);
                case ulong ul:
                    return new NumericResult(ul);
                case short s:
                    return new NumericResult(s);
                case ushort us:
                    return new NumericResult(us);
                case byte b:
                    return new NumericResult(b);
                case sbyte sb:
                    return new NumericResult(sb);
                case bool flag:
                    return new NumericResult(flag ? 1d : 0d);
                case string str:
                    return new NumericResult(NumericStringParser.ParseJsNumber(str));
            }

            switch (value)
            {
                case IJsPropertyAccessor accessor:
                {
                    if (accessor is JsObject jsObj)
                    {
                        if (jsObj.TryGetValue("__value__", out var inner))
                        {
                            value = inner;
                            continue;
                        }

                        // Symbol wrappers should behave like their unboxed symbol value for ToNumeric
                        // so mixed BigInt/Symbol cases throw correctly.
                        if (jsObj.TryGetProperty("SymbolData", out var symbolData) &&
                            symbolData.TryGetObject<TypedAstSymbol>(out var sym))
                        {
                            value = sym;
                            continue;
                        }
                    }

                    if (TryConvertToNumericPrimitive(accessor, out var primitive, context))
                    {
                        value = primitive;
                        continue;
                    }

                    if (context?.IsThrow == true)
                    {
                        return NumericResult.Error;
                    }

                    var error = CreateTypeError("Cannot convert object to primitive value", context);
                    if (context is null)
                    {
                        throw new ThrowSignal(JsValue.FromObject(error));
                    }

                    context.SetThrow(JsValue.FromObject(error));
                    return NumericResult.Error;
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
        var attempted = false;

        if (TryGetPropertyValue(accessor, SymbolKeys.ToPrimitive, out var toPrimitive, context))
        {
            if (context?.IsThrow == true)
            {
                return false;
            }

            if (toPrimitive is IJsCallable toPrimFn)
            {
                try
                {
                    var result = TypedAstEvaluator.InvokeCallable(
                        toPrimFn,
                        new JsValue[] { new JsValue("number") },
                        JsValue.FromObject(accessor),
                        context,
                        accessor is JsObject obj ? obj.RealmState?.Engine?.GlobalEnvironment : null);
                    if (context?.IsThrow == true)
                    {
                        return false;
                    }

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
        }

        // Check if valueOf exists and track whether we attempted to use it
        var valueOfExists = accessor.TryGetProperty("valueOf", out var valueOfMethod);
        if (valueOfExists)
        {
            attempted = true;
            if (TryInvokePropertyMethod(accessor, "valueOf", out var valueOfResult, context))
            {
                if ((valueOfResult is not IJsPropertyAccessor || valueOfResult is TypedAstSymbol or Symbol) &&
                    valueOfResult is not JsObject)
                {
                    primitive = valueOfResult;
                    return true;
                }
            }
        }

        if (context?.IsThrow == true)
        {
            return false;
        }

        // Check if toString exists and track whether we attempted to use it
        var toStringExists = accessor.TryGetProperty("toString", out var toStringMethod);
        var toStringAttempted = false;
        if (toStringExists)
        {
            attempted = true;
            toStringAttempted = TryInvokePropertyMethod(accessor, "toString", out var toStringResult, context);
            if (toStringAttempted)
            {
                if ((toStringResult is not IJsPropertyAccessor || toStringResult is TypedAstSymbol or Symbol) &&
                    toStringResult is not JsObject)
                {
                    primitive = toStringResult;
                    return true;
                }
            }
        }

        if (context?.IsThrow == true)
        {
            return false;
        }

        if (!attempted)
        {
            return false;
        }

        // OrdinaryToPrimitive failure path should be an abrupt completion.
        var error = CreateTypeError("Cannot convert object to primitive value", context);
        if (context is null)
        {
            throw new ThrowSignal(JsValue.FromObject(error));
        }

        context.SetThrow(JsValue.FromObject(error));

        return false;
    }

    /// <summary>
    /// Converts a value to a primitive using the specified hint string.
    /// Prefer using the ToPrimitiveHint enum overload in hot paths.
    /// </summary>
    public static object? ToPrimitive(object? value, string hint, EvaluationContext? context = null)
    {
        var hintEnum = hint switch
        {
            "number" => ToPrimitiveHint.Number,
            "string" => ToPrimitiveHint.String,
            _ => ToPrimitiveHint.Default
        };
        return ToPrimitive(value, hintEnum, context);
    }

    /// <summary>
    /// Converts a value to a primitive using the specified hint enum (faster than string version).
    /// </summary>
    public static object? ToPrimitive(object? value, ToPrimitiveHint hint, EvaluationContext? context = null)
    {
        if (value is TypedAstSymbol)
        {
            return value;
        }

        if (value is not IJsPropertyAccessor accessor)
        {
            return value;
        }

        // Date objects default to string hint
        if (hint == ToPrimitiveHint.Default && accessor is JsObject jsObj &&
            jsObj.TryGetProperty("_internalDate", out _))
        {
            hint = ToPrimitiveHint.String;
        }

        object? toPrimitive = null;

        if (TryGetPropertyValue(accessor, SymbolKeys.ToPrimitive, out var ownOrInheritedToPrimitive, context))
        {
            if (context?.IsThrow == true)
            {
                return value;
            }

            toPrimitive = ownOrInheritedToPrimitive;
        }

        if (toPrimitive is not null)
        {
            if (context?.IsThrow == true)
            {
                return value;
            }

            if (!IsNullish(toPrimitive) && toPrimitive is not IJsCallable)
            {
                throw StandardLibrary.ThrowTypeError("Cannot convert object to primitive value", context);
            }

            if (toPrimitive is IJsCallable toPrimFn)
            {
                try
                {
                    // Use cached hint strings to avoid string allocation
                    var hintString = hint switch
                    {
                        ToPrimitiveHint.Number => HintNumber,
                        ToPrimitiveHint.String => HintString,
                        _ => HintDefault
                    };
                    var result = TypedAstEvaluator.InvokeCallable(
                        toPrimFn,
                        new JsValue[] { new JsValue(hintString) },
                        JsValue.FromObject(accessor),
                        context,
                        accessor is JsObject obj ? obj.RealmState?.Engine?.GlobalEnvironment : null);
                    if (context?.IsThrow == true)
                    {
                        return value;
                    }

                    if (result is JsObject ||
                        result is IJsPropertyAccessor { } and not TypedAstSymbol)
                    {
                        var signal =
                            StandardLibrary.ThrowTypeError("Cannot convert object to primitive value", context);
                        if (context is null)
                        {
                            throw signal;
                        }

                        context.SetThrow(signal.ThrownValue);
                        return value;
                    }

                    return result;
                }
                catch (ThrowSignal signal) when (context is not null)
                {
                    context.SetThrow(signal.ThrownValue);
                    return value;
                }
            }
        }

        var methods = hint == ToPrimitiveHint.String
            ? StringHintMethods
            : DefaultHintMethods;

        foreach (var methodName in methods)
        {
            if (context?.IsThrow == true)
            {
                return value;
            }

            if (!TryInvokePropertyMethod(accessor, methodName, out var result, context))
            {
                continue;
            }

            if (context?.IsThrow == true)
            {
                return value;
            }

            if (result is not JsObject &&
                (result is not IJsPropertyAccessor || result is TypedAstSymbol))
            {
                return result;
            }
        }

        if (accessor is HostFunction)
        {
            return "function() { [native code] }";
        }

        if (context?.IsThrow == true)
        {
            return value;
        }

        var finalSignal = StandardLibrary.ThrowTypeError("Cannot convert object to primitive value", context);
        if (context is null)
        {
            throw finalSignal;
        }

        context.SetThrow(JsValue.FromObject(finalSignal.ThrownValue));
        return value;
    }

    public static string ToJsString(object? value, EvaluationContext? context = null)
    {
        return value.ToJsString(context, context?.RealmState);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            if (context?.IsThrow == true)
            {
                // Propagate the abrupt completion set by ToPrimitive/ToNumber/etc.
                throw new ThrowSignal(context.FlowValue);
            }

            var leftType = GetJsType(left);
            var rightType = GetJsType(right);

            if (leftType == rightType)
            {
                return StrictEquals(left, right);
            }

            if ((leftType == JsValueType.Null && rightType == JsValueType.Undefined) ||
                (leftType == JsValueType.Undefined && rightType == JsValueType.Null))
            {
                return true;
            }

            if (left is IIsHtmlDda || right is IIsHtmlDda)
            {
                return false;
            }

            if (leftType == JsValueType.Number && rightType == JsValueType.String)
            {
                right = ToNumber(right, context);
                continue;
            }

            if (leftType == JsValueType.String && rightType == JsValueType.Number)
            {
                left = ToNumber(left, context);
                continue;
            }

            if (leftType == JsValueType.BigInt && rightType == JsValueType.String)
            {
                return TryParseJsBigInt((string)right!, out var parsed) && StrictEquals(left, parsed);
            }

            if (leftType == JsValueType.String && rightType == JsValueType.BigInt)
            {
                return TryParseJsBigInt((string)left!, out var parsed) && StrictEquals(parsed, right);
            }

            if (leftType == JsValueType.Boolean)
            {
                left = ToNumber(left, context);
                continue;
            }

            if (rightType == JsValueType.Boolean)
            {
                right = ToNumber(right, context);
                continue;
            }

            if ((leftType == JsValueType.String || leftType == JsValueType.Number || leftType == JsValueType.BigInt ||
                 leftType == JsValueType.Symbol) && rightType == JsValueType.Object)
            {
                right = ToPrimitive((IJsPropertyAccessor)right!, ToPrimitiveHint.Default, context);
                continue;
            }

            if (leftType == JsValueType.Object &&
                (rightType == JsValueType.String || rightType == JsValueType.Number ||
                 rightType == JsValueType.BigInt || rightType == JsValueType.Symbol))
            {
                left = ToPrimitive((IJsPropertyAccessor)left!, ToPrimitiveHint.Default, context);
                continue;
            }

            if (leftType == JsValueType.Number && rightType == JsValueType.BigInt)
            {
                return NumberEqualsBigInt(left, (JsBigInt)right!);
            }

            if (leftType == JsValueType.BigInt && rightType == JsValueType.Number)
            {
                return NumberEqualsBigInt(right, (JsBigInt)left!);
            }

            return false;
        }
    }

    private enum ComparisonOperator
    {
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual
    }

    public static bool GreaterThan(object? left, object? right, EvaluationContext? context = null)
    {
        return PerformComparisonOperation(left, right, ComparisonOperator.GreaterThan, context);
    }

    public static bool GreaterThanOrEqual(object? left, object? right, EvaluationContext? context = null)
    {
        return PerformComparisonOperation(left, right, ComparisonOperator.GreaterThanOrEqual, context);
    }

    public static bool LessThan(object? left, object? right, EvaluationContext? context = null)
    {
        return PerformComparisonOperation(left, right, ComparisonOperator.LessThan, context);
    }

    public static bool LessThanOrEqual(object? left, object? right, EvaluationContext? context = null)
    {
        return PerformComparisonOperation(left, right, ComparisonOperator.LessThanOrEqual, context);
    }

    private static bool PerformComparisonOperation(
        object? left,
        object? right,
        ComparisonOperator op,
        EvaluationContext? context)
    {
        // ES Spec 7.2.15 Abstract Relational Comparison
        // Step 1-3: Call ToPrimitive with hint "number" on both operands
        var leftPrimitive = left;
        if (left is IJsPropertyAccessor leftAccessor && left is not TypedAstSymbol)
        {
            leftPrimitive = ToPrimitive(leftAccessor, ToPrimitiveHint.Number, context);
            if (context?.IsThrow == true)
            {
                return false;
            }
        }

        var rightPrimitive = right;
        if (right is IJsPropertyAccessor rightAccessor && right is not TypedAstSymbol)
        {
            rightPrimitive = ToPrimitive(rightAccessor, ToPrimitiveHint.Number, context);
            if (context?.IsThrow == true)
            {
                return false;
            }
        }

        // Step 4: If both are strings, do string comparison
        if (leftPrimitive is string leftStr && rightPrimitive is string rightStr)
        {
            // String comparison: check if leftStr < rightStr lexicographically
            var comparison = string.CompareOrdinal(leftStr, rightStr);
            return op switch
            {
                ComparisonOperator.LessThan => comparison < 0,
                ComparisonOperator.LessThanOrEqual => comparison <= 0,
                ComparisonOperator.GreaterThan => comparison > 0,
                ComparisonOperator.GreaterThanOrEqual => comparison >= 0,
                _ => false
            };
        }

        // Step 4b: If one is a string and the other is BigInt, try to convert string to BigInt
        // Per spec: when comparing String with BigInt, convert String to BigInt, not Number
        if (leftPrimitive is string leftString && rightPrimitive is JsBigInt rightBi)
        {
            if (!TryParseJsBigInt(leftString, out var leftAsBigInt))
            {
                // String cannot be parsed as BigInt, return undefined (false)
                return false;
            }

            return op switch
            {
                ComparisonOperator.LessThan => leftAsBigInt! < rightBi,
                ComparisonOperator.LessThanOrEqual => leftAsBigInt! <= rightBi,
                ComparisonOperator.GreaterThan => leftAsBigInt! > rightBi,
                ComparisonOperator.GreaterThanOrEqual => leftAsBigInt! >= rightBi,
                _ => false
            };
        }

        if (leftPrimitive is JsBigInt leftBi && rightPrimitive is string rightString)
        {
            if (!TryParseJsBigInt(rightString, out var rightAsBigInt))
            {
                // String cannot be parsed as BigInt, return undefined (false)
                return false;
            }

            return op switch
            {
                ComparisonOperator.LessThan => leftBi < rightAsBigInt!,
                ComparisonOperator.LessThanOrEqual => leftBi <= rightAsBigInt!,
                ComparisonOperator.GreaterThan => leftBi > rightAsBigInt!,
                ComparisonOperator.GreaterThanOrEqual => leftBi >= rightAsBigInt!,
                _ => false
            };
        }

        // Step 5: Otherwise, convert both to numeric values and compare
        var leftNumeric = ToNumeric(leftPrimitive, context);
        if (context?.IsThrow == true)
        {
            return false;
        }

        var rightNumeric = ToNumeric(rightPrimitive, context);
        if (context?.IsThrow == true)
        {
            return false;
        }

        // Step 5a: If both are BigInt, do BigInt comparison
        if (leftNumeric is JsBigInt leftBigInt && rightNumeric is JsBigInt rightBigInt)
        {
            return op switch
            {
                ComparisonOperator.LessThan => leftBigInt < rightBigInt,
                ComparisonOperator.LessThanOrEqual => leftBigInt <= rightBigInt,
                ComparisonOperator.GreaterThan => leftBigInt > rightBigInt,
                ComparisonOperator.GreaterThanOrEqual => leftBigInt >= rightBigInt,
                _ => false
            };
        }

        // Step 5b: If one is BigInt and the other is Number, do mixed-type comparison
        if ((leftNumeric is JsBigInt && rightNumeric is double) ||
            (leftNumeric is double && rightNumeric is JsBigInt))
        {
            JsBigInt bigIntValue;
            double numberValue;
            bool bigIntIsLeft;

            if (leftNumeric is JsBigInt)
            {
                bigIntValue = (JsBigInt)leftNumeric;
                numberValue = (double)rightNumeric;
                bigIntIsLeft = true;
            }
            else
            {
                bigIntValue = (JsBigInt)rightNumeric;
                numberValue = (double)leftNumeric;
                bigIntIsLeft = false;
            }

            // If the Number is NaN, return false
            if (double.IsNaN(numberValue))
            {
                return false;
            }

            // If the Number is ±Infinity, handle specially
            if (double.IsPositiveInfinity(numberValue))
            {
                // BigInt < +Infinity is always true, BigInt >= +Infinity is always false
                if (bigIntIsLeft)
                {
                    return op is ComparisonOperator.LessThan or ComparisonOperator.LessThanOrEqual;
                }
                else
                {
                    return op is ComparisonOperator.GreaterThan or ComparisonOperator.GreaterThanOrEqual;
                }
            }

            if (double.IsNegativeInfinity(numberValue))
            {
                // BigInt > -Infinity is always true, BigInt <= -Infinity is always false
                if (bigIntIsLeft)
                {
                    return op is ComparisonOperator.GreaterThan or ComparisonOperator.GreaterThanOrEqual;
                }
                else
                {
                    return op is ComparisonOperator.LessThan or ComparisonOperator.LessThanOrEqual;
                }
            }

            // Compare mathematical values without precision loss
            // Convert the Number to BigInteger if it's an integer value
            if (numberValue != Math.Floor(numberValue))
            {
                // Number is not an integer, use decimal comparison
                // For non-integer doubles, we need to compare against the BigInt's value
                var comparison = CompareBigIntToDouble(bigIntValue.Value, numberValue);
                if (bigIntIsLeft)
                {
                    return op switch
                    {
                        ComparisonOperator.LessThan => comparison < 0,
                        ComparisonOperator.LessThanOrEqual => comparison <= 0,
                        ComparisonOperator.GreaterThan => comparison > 0,
                        ComparisonOperator.GreaterThanOrEqual => comparison >= 0,
                        _ => false
                    };
                }
                else
                {
                    return op switch
                    {
                        ComparisonOperator.LessThan => comparison > 0,
                        ComparisonOperator.LessThanOrEqual => comparison >= 0,
                        ComparisonOperator.GreaterThan => comparison < 0,
                        ComparisonOperator.GreaterThanOrEqual => comparison <= 0,
                        _ => false
                    };
                }
            }
            else
            {
                // Number is an integer, convert to BigInteger for exact comparison
                var numberAsBigInt = new BigInteger(numberValue);
                var comparison = bigIntValue.Value.CompareTo(numberAsBigInt);
                if (bigIntIsLeft)
                {
                    return op switch
                    {
                        ComparisonOperator.LessThan => comparison < 0,
                        ComparisonOperator.LessThanOrEqual => comparison <= 0,
                        ComparisonOperator.GreaterThan => comparison > 0,
                        ComparisonOperator.GreaterThanOrEqual => comparison >= 0,
                        _ => false
                    };
                }
                else
                {
                    return op switch
                    {
                        ComparisonOperator.LessThan => comparison > 0,
                        ComparisonOperator.LessThanOrEqual => comparison >= 0,
                        ComparisonOperator.GreaterThan => comparison < 0,
                        ComparisonOperator.GreaterThanOrEqual => comparison <= 0,
                        _ => false
                    };
                }
            }
        }

        // Step 5c: Both are Numbers, do Number comparison
        var leftNum = ToNumber(leftNumeric, context);
        var rightNum = ToNumber(rightNumeric, context);
        if (double.IsNaN(leftNum) || double.IsNaN(rightNum))
        {
            return false;
        }

        return op switch
        {
            ComparisonOperator.LessThan => leftNum < rightNum,
            ComparisonOperator.LessThanOrEqual => leftNum <= rightNum,
            ComparisonOperator.GreaterThan => leftNum > rightNum,
            ComparisonOperator.GreaterThanOrEqual => leftNum >= rightNum,
            _ => false
        };
    }

    // Helper method to compare a BigInteger to a double without precision loss
    private static int CompareBigIntToDouble(BigInteger bigInt, double number)
    {
        // For non-integer doubles, we need to be careful about the comparison
        // We can't convert the BigInteger to double because that might lose precision
        // Instead, we compare the BigInteger to the floor and ceiling of the double

        var floor = Math.Floor(number);
        var ceiling = Math.Ceiling(number);

        var floorAsBigInt = new BigInteger(floor);
        var ceilingAsBigInt = new BigInteger(ceiling);

        // If bigInt < floor, then bigInt < number
        if (bigInt.CompareTo(floorAsBigInt) < 0)
        {
            return -1;
        }

        // If bigInt >= ceiling, then bigInt > number
        if (bigInt.CompareTo(ceilingAsBigInt) >= 0)
        {
            return 1;
        }

        // Otherwise, floor <= bigInt < ceiling, which means bigInt is between floor and ceiling
        // Since number is also between floor and ceiling, and bigInt is an integer,
        // bigInt must equal floor, and number is between floor and ceiling
        // Therefore bigInt < number
        return -1;
    }

    public static string? ToPropertyName(object? value, EvaluationContext? context = null)
    {
        while (true)
        {
            switch (value)
            {
                case null:
                    return "null";
                case JsValue jsValue:
                    // Unwrap JsValue and continue with the inner value
                    value = jsValue.ToObject();
                    continue;
                case string s:
                    return s;
                case JsBigInt bigInt:
                    return bigInt.Value.ToString(CultureInfo.InvariantCulture);
                case Symbol symbol:
                    return symbol.Name;
                case TypedAstSymbol jsSymbol:
                    return TypedAstSymbol.PropertyKey(jsSymbol);
                case bool b:
                    return b ? "true" : "false";
                case int i:
                    return JsValueCache.GetIndexString(i);
                case long l:
                    return JsValueCache.GetIndexString(l);
                case double d:
                    return ToCanonicalNumberString(d);
            }

            if (value is IJsPropertyAccessor accessor)
            {
                // ToPropertyKey delegates to ToPrimitive with string hint
                // and then converts the resulting primitive to a property name.
                value = ToPrimitive(accessor, ToPrimitiveHint.String, context);
                if (context?.IsThrow == true)
                {
                    return null;
                }

                continue;
            }

            if (context?.IsThrow == true)
            {
                return null;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    // Align numeric property keys with ECMAScript ToString(number) formatting (case-sensitive keys).
    internal static string ToCanonicalNumberString(double value)
    {
        // Fast path for common array indices (0-9999)
        var cachedIndexString = JsValueCache.TryGetIndexString(value);
        if (cachedIndexString is not null)
        {
            return cachedIndexString;
        }

        if (double.IsNaN(value))
        {
            return JsValueCache.NaNString;
        }

        if (double.IsPositiveInfinity(value))
        {
            return JsValueCache.InfinityString;
        }

        if (double.IsNegativeInfinity(value))
        {
            return JsValueCache.NegativeInfinityString;
        }

        if (value == 0)
        {
            return "0";
        }

        var sign = value < 0 ? "-" : string.Empty;
        var abs = Math.Abs(value);
        var exponent = (int)Math.Floor(Math.Log10(abs));
        var useExponential = exponent < -6 || exponent >= 21;

        if (!useExponential)
        {
            // Fixed-point form for the mid-range magnitude.
            var fixedText = abs.ToString("0.###################", CultureInfo.InvariantCulture);
            return sign + fixedText;
        }

        var expText = abs.ToString("0.###################e+0", CultureInfo.InvariantCulture);
        var parts = expText.Split('e');
        var mantissa = parts[0].TrimEnd('0').TrimEnd('.');
        var expVal = int.Parse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var expStr = expVal >= 0 ? $"+{expVal}" : expVal.ToString(CultureInfo.InvariantCulture);

        return $"{sign}{mantissa}e{expStr}";
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
            throw new ThrowSignal(JsValue.FromObject(error));
        }

        context.SetThrow(JsValue.FromObject(error));
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
        if (!accessor.TryGetProperty(methodName, out var method) || !method.TryGetObject<IJsCallable>(out var callable))
        {
            return false;
        }

        try
        {
            result = TypedAstEvaluator.InvokeCallable(callable, Array.Empty<JsValue>(), JsValue.FromObject(accessor), context,
                accessor is JsObject obj ? obj.RealmState?.Engine?.GlobalEnvironment : null);
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

    public static IJsPropertyAccessor? GetPrototypePointer(object? value)
    {
        if (value is IPrototypeAccessorProvider { PrototypeAccessor: { } protoAccessor })
        {
            return protoAccessor;
        }

        if (value is IJsObjectLike { Prototype: { } proto })
        {
            return proto;
        }

        if (value is JsObject { Prototype: { } jsProto })
        {
            return jsProto;
        }

        return null;
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

        if (value is Symbol sym && ReferenceEquals(sym, Symbol.Undefined))
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

        // Special handling for Proxy: typeof depends on whether the TARGET is callable,
        // not the proxy itself. Per ES spec, typeof on a revoked proxy doesn't throw -
        // it returns "function" if the original target was callable, "object" otherwise.
        if (value is JsProxy proxy)
        {
            return proxy.Target is IJsCallable ? "function" : "object";
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNumeric(object? value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }

    private static JsValueType GetJsType(object? value)
    {
        return value switch
        {
            null => JsValueType.Null,
            Symbol sym when ReferenceEquals(sym, Symbol.Undefined) => JsValueType.Undefined,
            TypedAstSymbol => JsValueType.Symbol,
            IIsHtmlDda => JsValueType.Undefined,
            bool => JsValueType.Boolean,
            JsBigInt => JsValueType.BigInt,
            double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte =>
                JsValueType.Number,
            string => JsValueType.String,
            _ => JsValueType.Object
        };
    }

    private static bool NumberEqualsBigInt(object? numberValue, JsBigInt bigInt)
    {
        var num = ToNumber(numberValue);
        if (double.IsNaN(num) || double.IsInfinity(num))
        {
            return false;
        }

        if (num != Math.Floor(num))
        {
            return false;
        }

        return new BigInteger(num) == bigInt.Value;
    }

    private static bool TryParseJsBigInt(string text, out JsBigInt? value)
    {
        value = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            value = JsBigInt.Zero;
            return true;
        }

        var span = trimmed.AsSpan();
        var isNegative = false;
        if (span.Length > 0 && (span[0] == '+' || span[0] == '-'))
        {
            isNegative = span[0] == '-';
            span = span[1..];
        }

        if (TryParsePrefixedBigInt(span, 16, NumberStyles.AllowHexSpecifier, out var parsed) ||
            TryParseBinaryBigInt(span, out parsed) ||
            TryParseOctalBigInt(span, out parsed))
        {
            value = new JsBigInt(isNegative ? BigInteger.Negate(parsed) : parsed);
            return true;
        }

        if (BigInteger.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
        {
            value = new JsBigInt(isNegative ? BigInteger.Negate(parsed) : parsed);
            return true;
        }

        return false;
    }

    private static bool TryParsePrefixedBigInt(ReadOnlySpan<char> span, int radix, NumberStyles styles,
        out BigInteger value)
    {
        value = BigInteger.Zero;
        if (span.Length <= 2 || span[0] != '0')
        {
            return false;
        }

        var prefixChar = span[1];
        var expected = radix switch
        {
            16 => ('x', 'X'),
            _ => ('?', '?')
        };

        if (prefixChar != expected.Item1 && prefixChar != expected.Item2)
        {
            return false;
        }

        var digits = span[2..];
        if (digits.Length == 0)
        {
            return false;
        }

        // BigInteger.Parse interprets hex input as two's complement; prefix
        // a zero nibble so 0xFF produces 255 instead of -1.
        if ((styles & NumberStyles.AllowHexSpecifier) != 0)
        {
            var padded = string.Concat("0", digits.ToString());
            return BigInteger.TryParse(padded, styles, CultureInfo.InvariantCulture, out value);
        }

        return BigInteger.TryParse(digits, styles, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseBinaryBigInt(ReadOnlySpan<char> span, out BigInteger value)
    {
        value = BigInteger.Zero;
        if (span.Length <= 2 || span[0] != '0' || span[1] is not ('b' or 'B'))
        {
            return false;
        }

        var digits = span[2..];
        if (digits.Length == 0)
        {
            return false;
        }

        foreach (var ch in digits)
        {
            value <<= 1;
            switch (ch)
            {
                case '0':
                    break;
                case '1':
                    value += BigInteger.One;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool TryParseOctalBigInt(ReadOnlySpan<char> span, out BigInteger value)
    {
        value = BigInteger.Zero;
        if (span.Length <= 2 || span[0] != '0' || span[1] is not ('o' or 'O'))
        {
            return false;
        }

        var digits = span[2..];
        if (digits.Length == 0)
        {
            return false;
        }

        foreach (var ch in digits)
        {
            if (ch is < '0' or > '7')
            {
                return false;
            }

            value = (value << 3) + (ch - '0');
        }

        return true;
    }

    private enum JsValueType
    {
        Undefined,
        Null,
        Boolean,
        Number,
        String,
        Symbol,
        BigInt,
        Object
    }

    /// <summary>
    /// Implements [[HasProperty]] internal method for the 'in' operator.
    /// Returns true if the property exists on the object or its prototype chain.
    /// Per ES spec, [[HasProperty]] uses [[GetOwnProperty]] to check for existence,
    /// it does NOT invoke getters like [[Get]] would.
    /// </summary>
    public static bool HasProperty(object? target, string propertyName, EvaluationContext? context = null)
    {
        // Walk the prototype chain checking for the property via [[GetOwnProperty]]
        // This does NOT invoke getters - it only checks if the property exists
        var current = target;
        while (current is not null)
        {
            if (current is IJsObjectLike objLike)
            {
                var descriptor = objLike.GetOwnPropertyDescriptor(propertyName);
                if (descriptor is not null)
                {
                    return true;
                }
                // Move to prototype
                current = objLike.Prototype;
            }
            else if (current is IJsPropertyAccessor accessor)
            {
                // For non-IJsObjectLike property accessors, fall back to TryGetProperty
                // but wrap in try-catch to handle poison pill getters
                try
                {
                    if (accessor.TryGetProperty(propertyName, out _))
                    {
                        return true;
                    }
                }
                catch (ThrowSignal)
                {
                    // Property exists but getter threw - per spec, HasProperty should return true
                    // because the property exists (it has a getter descriptor)
                    return true;
                }
                break;
            }
            else
            {
                break;
            }
        }
        return false;
    }

    public static bool TryGetPropertyValue(object? target, string propertyName, out object? value,
        EvaluationContext? context = null)
    {
        if (target is IJsPropertyAccessor propertyAccessor)
        {
            try
            {
                if (propertyAccessor is JsObject jsObject)
                {
                    return jsObject.TryGetProperty(propertyName, target, context, out value);
                }

                // For Symbol primitives, first try own properties, then fall back to Symbol.prototype
                if (propertyAccessor is TypedAstSymbol symbol)
                {
                    if (symbol.TryGetProperty(propertyName, out var jsValue))
                    {
                        value = jsValue.ToObject();
                        return true;
                    }

                    // Look up in Symbol.prototype chain
                    var symbolProto = context?.RealmState?.SymbolPrototype;
                    if (symbolProto is not null && symbolProto.TryGetProperty(propertyName, target, context, out value))
                    {
                        return true;
                    }

                    value = null;
                    return false;
                }

                if (propertyAccessor.TryGetProperty(propertyName, JsValue.FromObject(target), out var jsVal))
                {
                    value = jsVal.ToObject();
                    return true;
                }
                value = null;
                return false;
            }
            catch (ThrowSignal signal)
            {
                if (context is not null)
                {
                    context.SetThrow(signal.ThrownValue);
                    value = signal.ThrownValue;
                    return true;
                }

                throw;
            }
        }

        switch (target)
        {
            case bool b:
                if (context?.RealmState?.BooleanPrototype is { } booleanProto &&
                    booleanProto.TryGetProperty(propertyName, receiver: target, context, out value))
                {
                    return true;
                }

                // Fallback when no realm prototype is available.
                var booleanWrapper = StandardLibrary.CreateBooleanWrapper(b, context, context?.RealmState);
                if (booleanWrapper.TryGetProperty(propertyName, receiver: target, context, out value))
                {
                    return true;
                }

                break;
            case double num:
                if (context?.RealmState?.NumberPrototype is { } numberProto &&
                    numberProto.TryGetProperty(propertyName, receiver: target, context, out value))
                {
                    return true;
                }

                // Fallback when no realm prototype is available yet.
                var numberWrapper = StandardLibrary.CreateNumberWrapper(num, context, context?.RealmState);
                if (numberWrapper.TryGetProperty(propertyName, receiver: target, context, out value))
                {
                    return true;
                }

                break;
            case JsBigInt bigInt:
                if (context?.RealmState?.BigIntPrototype is { } bigIntProto &&
                    bigIntProto.TryGetProperty(propertyName, receiver: target, context, out value))
                {
                    return true;
                }

                // Fallback when no realm prototype is available.
                var bigIntWrapper = StandardLibrary.CreateBigIntWrapper(bigInt, context, context?.RealmState);
                if (bigIntWrapper.TryGetProperty(propertyName, receiver: target, context, out value))
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

                if (context?.RealmState?.StringPrototype is { } stringProto &&
                    stringProto.TryGetProperty(propertyName, receiver: target, context, out value))
                {
                    return true;
                }

                // Fallback when no realm prototype is available yet.
                var stringWrapper = StandardLibrary.CreateStringWrapper(str, context, context?.RealmState);
                if (stringWrapper.TryGetProperty(propertyName, receiver: target, context, out value))
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
            value = Symbol.Undefined;
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
            value = Symbol.Undefined;
            return false;
        }

        var propertyName = ToPropertyName(propertyKey, context);
        if (context?.IsThrow == true)
        {
            value = Symbol.Undefined;
            return false;
        }

        if (propertyName is null)
        {
            value = Symbol.Undefined;
            return true;
        }

        try
        {
            return TryGetPropertyValue(target, propertyName, out value, context);
        }
        catch (ThrowSignal signal)
        {
            if (context is not null)
            {
                context.SetThrow(signal.ThrownValue);
                value = signal.ThrownValue;
                return true;
            }

            throw;
        }
    }

    public static bool IsConstructor(object? value)
    {
        while (true)
        {
            switch (value)
            {
                case JsValue jsValue:
                    value = jsValue.ToObject();
                    continue;
                case JsProxy proxy:
                    value = proxy.Target;
                    continue;
                case HostFunction host:
                    return host is { IsConstructor: true, DisallowConstruct: false };
                case ICallableMetadata { IsArrowFunction: true }:
                case ICallableMetadata { DisallowConstruct: true }:
                    return false;
            }

            return value is IJsCallable;
        }
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
            if (typedIndex >= 0 && typedIndex < typedArray.Length)
            {
                value = typedArray.GetValueForIndex(typedIndex);
            }
            else
            {
                value = Symbol.Undefined;
            }

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
            accessor.SetProperty(propertyName, JsValue.FromObject(value), JsValue.FromObject(target));
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

            if (propertyName is null)
            {
                return true;
            }

            if (string.Equals(propertyName, "length", StringComparison.Ordinal))
            {
                jsArray.SetLength(value, context);
                return true;
            }

            var isArrayIndex = TryResolveArrayIndex(propertyKey, out var index, context);
            var ownDescriptor = jsArray.GetOwnPropertyDescriptor(propertyName);

            if (isArrayIndex)
            {
                if (ownDescriptor is not null)
                {
                    if (ownDescriptor.IsAccessorDescriptor)
                    {
                        if (ownDescriptor.Set is null)
                        {
                            if (context?.CurrentScope.IsStrict == true)
                            {
                                throw StandardLibrary.ThrowTypeError(
                                    $"Cannot set property '{propertyName}' that has only a getter.",
                                    context,
                                    context.RealmState);
                            }

                            return true;
                        }

                        TypedAstEvaluator.InvokeCallable(ownDescriptor.Set, new JsValue[] { JsValue.FromObject(value) }, JsValue.FromObject(jsArray), context);
                        return true;
                    }

                    if (!ownDescriptor.Writable)
                    {
                        if (context?.CurrentScope.IsStrict == true)
                        {
                            throw StandardLibrary.ThrowTypeError(
                                $"Cannot assign to read only property '{propertyName}'.",
                                context,
                                context.RealmState);
                        }

                        return true;
                    }

                    jsArray.SetElement(index, value);
                    return true;
                }

                var current = jsArray.PrototypeAccessor ?? jsArray.Prototype;
                while (current is not null)
                {
                    var inheritedDescriptor = current.GetOwnPropertyDescriptor(propertyName);
                    if (inheritedDescriptor is not null)
                    {
                        if (inheritedDescriptor.IsAccessorDescriptor)
                        {
                            if (inheritedDescriptor.Set is null)
                            {
                                if (context?.CurrentScope.IsStrict == true)
                                {
                                    throw StandardLibrary.ThrowTypeError(
                                        $"Cannot set property '{propertyName}' that has only a getter.",
                                        context,
                                        context.RealmState);
                                }

                                return true;
                            }

                            TypedAstEvaluator.InvokeCallable(inheritedDescriptor.Set, new JsValue[] { JsValue.FromObject(value) }, JsValue.FromObject(jsArray), context);
                            return true;
                        }

                        if (!inheritedDescriptor.Writable)
                        {
                            if (context?.CurrentScope.IsStrict == true)
                            {
                                throw StandardLibrary.ThrowTypeError(
                                    $"Cannot assign to read only property '{propertyName}'.",
                                    context,
                                    context.RealmState);
                            }

                            return true;
                        }

                        jsArray.DefineProperty(propertyName, new PropertyDescriptor
                        {
                            Value = value,
                            Writable = true,
                            Enumerable = inheritedDescriptor.Enumerable,
                            Configurable = inheritedDescriptor.Configurable,
                            HasValue = true,
                            HasWritable = true,
                            HasEnumerable = inheritedDescriptor.HasEnumerable,
                            HasConfigurable = inheritedDescriptor.HasConfigurable
                        });
                        return true;
                    }

                    current = current switch
                    {
                        IJsObjectLike objectLike => objectLike.Prototype,
                        IPrototypeAccessorProvider provider => provider.PrototypeAccessor,
                        _ => null
                    };
                }

                jsArray.SetElement(index, value);
                return true;
            }

            jsArray.SetProperty(propertyName, JsValue.FromObject(value), JsValue.FromObject(jsArray));
            return true;
        }

        if (target is TypedArrayBase typedArray && TryResolveArrayIndex(propertyKey, out var typedIndex, context))
        {
            typedArray.SetValue(typedIndex, JsValue.FromObject(value));
            return true;
        }

        return false;
    }

    public static bool DeletePropertyValue(object? target, object? propertyKey, EvaluationContext? context = null)
    {
        if (target is null || ReferenceEquals(target, Symbol.Undefined))
        {
            throw StandardLibrary.ThrowTypeError("Cannot delete property on null or undefined", context,
                context?.RealmState);
        }

        if (target is JsArray jsArray)
        {
            var propertyName = ToPropertyName(propertyKey, context);
            return propertyName is null || jsArray.Delete(propertyName);
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

    internal enum NumericKind
    {
        Number,
        BigInt,
        Error
    }

    /// <summary>
    /// Result struct for numeric operations that avoids boxing doubles.
    /// The double value is stored directly in the struct, not as object.
    /// Use this internally for arithmetic operations to avoid boxing/unboxing.
    /// </summary>
    internal readonly struct NumericResult
    {
        public readonly NumericKind Kind;
        public readonly double NumberValue;
        public readonly JsBigInt? BigIntValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NumericResult(double value)
        {
            Kind = NumericKind.Number;
            NumberValue = value;
            BigIntValue = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NumericResult(JsBigInt value)
        {
            Kind = NumericKind.BigInt;
            NumberValue = 0;
            BigIntValue = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private NumericResult(NumericKind kind)
        {
            Kind = kind;
            NumberValue = double.NaN;
            BigIntValue = null;
        }

        public static NumericResult Error => new(NumericKind.Error);

        public bool IsNumber => Kind == NumericKind.Number;
        public bool IsBigInt => Kind == NumericKind.BigInt;
        public bool IsError => Kind == NumericKind.Error;

        /// <summary>
        /// Boxes the result to object. Only call this when you need to return to JS.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object ToObject()
        {
            return Kind switch
            {
                NumericKind.BigInt => BigIntValue!,
                NumericKind.Number => JsValueCache.GetNumber(NumberValue),
                _ => JsValueCache.BoxedNaN
            };
        }
    }
}
