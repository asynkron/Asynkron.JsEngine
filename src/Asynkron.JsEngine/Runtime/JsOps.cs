#region

using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Runtime;

internal static class JsOps
{
    private const double NegativeZero = -0.0d;

    // Cached hint strings for ToPrimitive (used when calling [Symbol.toPrimitive])
    private const string HintDefault = "default";
    private const string HintNumber = "number";
    private const string HintString = "string";

    // Static arrays for ToPrimitive to avoid allocation on every call
    private static readonly string[] StringHintMethods = ["toString", "valueOf"];
    private static readonly string[] DefaultHintMethods = ["valueOf", "toString"];

    [MethodImpl(JsEngineConstants.Inlining)]
    internal static double MathPow(double baseValue, double exponent)
    {
        switch (exponent)
        {
            case double.NaN:
                return double.NaN;
            case 0:
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
            return abs switch
            {
                1 => double.NaN,
                > 1 => exponent > 0 ? double.PositiveInfinity : 0.0,
                _ => exponent > 0 ? 0.0 : double.PositiveInfinity
            };
        }

        if (!double.IsInfinity(baseValue))
        {
            return Math.Pow(baseValue, exponent);
        }

        var sign = Math.Sign(baseValue);
        if (exponent > 0)
        {
            return sign < 0 && IsOddInteger(exponent)
                ? double.NegativeInfinity
                : double.PositiveInfinity;
        }

        return sign < 0 && IsOddInteger(exponent) ? NegativeZero : 0.0;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool IsOddInteger(double value)
    {
        return double.IsFinite(value) && value % 1 == 0 && Math.Abs(value % 2) == 1;
    }

    /// <summary>
    /// ECMAScript-compliant modulo operation that properly handles negative zero.
    /// Per ES spec 13.15.3: The result of a remainder operation preserves the sign of the dividend.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static double MathMod(double dividend, double divisor)
    {
        // If either operand is NaN, return NaN
        if (double.IsNaN(dividend) || double.IsNaN(divisor))
        {
            return double.NaN;
        }

        // If the dividend is infinity, return NaN
        if (double.IsInfinity(dividend))
        {
            return double.NaN;
        }

        // If divisor is zero, return NaN
        if (divisor == 0)
        {
            return double.NaN;
        }

        // If divisor is infinity and dividend is finite, result equals dividend (preserves sign)
        if (double.IsInfinity(divisor))
        {
            return dividend;
        }

        // If dividend is zero, result equals dividend (preserves sign of zero)
        // This is important: -0 % n = -0
        if (dividend == 0)
        {
            return dividend;
        }

        // Standard remainder operation
        return dividend % divisor;
    }

    /// <summary>
    ///     ECMAScript-like ToBoolean semantics for engine values.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static bool ToBoolean(in JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => false,
            JsValueKind.Null => false,
            JsValueKind.Boolean => value.NumberValue != 0,
            JsValueKind.Number => !double.IsNaN(value.NumberValue) && value.NumberValue != 0,
            JsValueKind.BigInt => value.ObjectValue is JsBigInt { Value.IsZero: false },
            JsValueKind.String => value.ObjectValue is string { Length: > 0 },
            JsValueKind.Symbol => true,
            JsValueKind.Object => value.ObjectValue is not IIsHtmlDda,
            _ => true
        };
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public static double ToNumber(in JsValue value, EvaluationContext? context = null)
    {
        var result = ToNumericAsJsValue(in value, context);
        if (result.IsNumber)
        {
            return result.NumberValue;
        }

        if (!result.IsBigInt)
        {
            return double.NaN;
        }

        // Per ECMAScript spec, ToNumber on BigInt throws TypeError
        var error = StandardLibrary.CreateTypeError("Cannot convert a BigInt value to a number", context, context?.RealmState);
        if (context is null)
        {
            throw new ThrowSignal(error);
        }
        context.SetThrow(error);
        return double.NaN;

    }

    /// <summary>
    /// Converts a JsValue to numeric as JsValue without boxing. Use this for internal arithmetic operations.
    /// Returns JsValue with Number or BigInt kind. On error, returns JsValue.Undefined (check context.IsThrow).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue ToNumericAsJsValue(in JsValue value, EvaluationContext? context = null)
    {
        if (value.IsNumber || value.IsBigInt)
        {
            return value;
        }

        return value.Kind switch
        {
            // Handle JsValue kinds that have null ObjectValue specially
            // to avoid null being treated as 0 in ToNumericCore
            JsValueKind.Undefined => JsValue.NaN,
            JsValueKind.Null => JsValue.Zero,
            JsValueKind.Boolean => new JsValue(value.NumberValue),
            _ => ToNumericCore(value.ObjectValue, context)
        };
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue ToNumericCore(
        object? value,
        EvaluationContext? context)
    {
        var iterations = 0;
        while (true)
        {
            if (++iterations > 20)
            {
                return JsValue.NaN;
            }

            switch (value)
            {
                case null:
                    return JsValue.Zero;
                case JsValue jsValue:
                    switch (jsValue.Kind)
                    {
                        case JsValueKind.Undefined:
                            return JsValue.NaN;
                        case JsValueKind.Null:
                            return JsValue.Zero;
                        case JsValueKind.Boolean:
                        case JsValueKind.Number:
                            return new JsValue(jsValue.NumberValue);
                        case JsValueKind.BigInt:
                            if (jsValue.ObjectValue is JsBigInt jsBigInt)
                            {
                                return new JsValue(jsBigInt);
                            }

                            return JsValue.NaN;
                        case JsValueKind.String:
                        case JsValueKind.Symbol:
                        case JsValueKind.Object:
                        default:
                            value = jsValue.ObjectValue;
                            continue;
                    }
                case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
                case IIsHtmlDda:
                    return JsValue.NaN;
                case Symbol:
                case JsSymbol:
                    {
                        var error = StandardLibrary.CreateTypeError("Cannot convert a Symbol value to a number", context, context?.RealmState);
                        if (context is null)
                        {
                            throw new ThrowSignal(error);
                        }

                        context.SetThrow(error);
                        return JsValue.Undefined; // Error state - caller should check context.IsThrow
                    }
                case JsBigInt bigInt:
                    return new JsValue(bigInt);
                case double d:
                    return new JsValue(d);
                case float f:
                    return new JsValue(f);
                case decimal m:
                    return new JsValue((double)m);
                case int i:
                    return new JsValue((double)i);
                case uint ui:
                    return new JsValue(ui);
                case long l:
                    return new JsValue(l);
                case ulong ul:
                    return new JsValue(ul);
                case short s:
                    return new JsValue(s);
                case ushort us:
                    return new JsValue(us);
                case byte b:
                    return new JsValue(b);
                case sbyte sb:
                    return new JsValue(sb);
                case bool flag:
                    return flag ? JsValue.One : JsValue.Zero;
                case string str:
                    return new JsValue(NumericStringParser.ParseJsNumber(str));
                case JsRopeString rope:
                    return new JsValue(NumericStringParser.ParseJsNumber(rope.Flatten()));
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
                                symbolData.TryGetObject<JsSymbol>(out var sym))
                            {
                                value = sym;
                                continue;
                            }
                        }

                        if (TryConvertToNumericPrimitiveJsValue(accessor, out var primitive, context))
                        {
                            value = primitive;
                            continue;
                        }

                        if (context?.IsThrow == true)
                        {
                            return JsValue.Undefined; // Error state - caller should check context.IsThrow
                        }

                        var error = StandardLibrary.CreateTypeError("Cannot convert object to primitive value", context, context?.RealmState);
                        if (context is null)
                        {
                            throw new ThrowSignal(error);
                        }

                        context.SetThrow(error);
                        return JsValue.Undefined; // Error state - caller should check context.IsThrow
                    }
                default:
                    throw new InvalidOperationException($"Cannot convert value '{value}' to a number.");
            }
        }
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool TryConvertToNumericPrimitiveJsValue(IJsPropertyAccessor accessor, out JsValue primitive,
        EvaluationContext? context)
    {
        primitive = JsValue.Undefined;
        var attempted = false;

        // Cache accessor JsValue to avoid repeated FromObjectUnsafe calls
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);

        if (TryGetPropertyValue(accessorJsValue, SymbolKeys.ToPrimitive, out var toPrimitive, context))
        {
            if (context?.IsThrow == true)
            {
                return false;
            }

            if (toPrimitive.TryGetObject<IJsCallable>(out var toPrimFn))
            {
                try
                {
                    var result = TypedAstEvaluator.InvokeCallableJsValue(
                        toPrimFn,
                        [new JsValue("number")],
                        accessorJsValue,
                        context,
                        accessor is JsObject obj ? obj.RealmState?.Engine?.GlobalEnvironment : null);
                    if (context?.IsThrow == true)
                    {
                        return false;
                    }

                    if (IsPrimitiveValue(result))
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
        var valueOfExists = accessor.TryGetProperty("valueOf", out _);
        if (valueOfExists)
        {
            attempted = true;
            if (TryInvokePropertyMethodJsValue(accessor, "valueOf", out var valueOfResult, context))
            {
                if (IsPrimitiveValue(valueOfResult))
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
        var toStringExists = accessor.TryGetProperty("toString", out _);
        if (toStringExists)
        {
            attempted = true;
            var toStringAttempted = TryInvokePropertyMethodJsValue(accessor, "toString", out var toStringResult, context);
            if (toStringAttempted)
            {
                if (IsPrimitiveValue(toStringResult))
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
        var error = StandardLibrary.CreateTypeError("Cannot convert object to primitive value", context, context?.RealmState);
        if (context is null)
        {
            throw new ThrowSignal(error);
        }

        context.SetThrow(error);

        return false;
    }

    /// <summary>
    /// JsValue overload for ToPrimitive. Returns a JsValue representing the primitive result.
    /// </summary>
    public static JsValue ToPrimitive(JsValue value, ToPrimitiveHint hint, EvaluationContext? context = null)
    {
        // Fast path: already a primitive
        switch (value.Kind)
        {
            case JsValueKind.Undefined:
            case JsValueKind.Null:
            case JsValueKind.Boolean:
            case JsValueKind.Number:
            case JsValueKind.String:
            case JsValueKind.Symbol:
            case JsValueKind.BigInt:
                return value;
        }

        // Handle objects - need to convert to primitive
        if (value.ObjectValue is JsSymbol)
        {
            return JsValue.FromObjectUnsafe(value.ObjectValue);
        }

        if (value.ObjectValue is not IJsPropertyAccessor accessor)
        {
            return JsValue.FromObjectUnsafe(value.ObjectValue);
        }

        // Date objects default to string hint
        if (hint == ToPrimitiveHint.Default && accessor is JsObject jsObj &&
            jsObj.TryGetProperty("_internalDate", out _))
        {
            hint = ToPrimitiveHint.String;
        }

        // Cache accessor JsValue to avoid repeated FromObjectUnsafe calls
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);

        if (TryGetPropertyValue(accessorJsValue, SymbolKeys.ToPrimitive, out var ownOrInheritedToPrimitive, context))
        {
            if (context?.IsThrow == true)
            {
                return value;
            }

            if (!ownOrInheritedToPrimitive.IsNullOrUndefined &&
                !ownOrInheritedToPrimitive.TryGetObject<IJsCallable>(out _))
            {
                throw StandardLibrary.ThrowTypeError("Cannot convert object to primitive value", context);
            }

            if (ownOrInheritedToPrimitive.TryGetObject<IJsCallable>(out var toPrimFn))
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
                    var result = TypedAstEvaluator.InvokeCallableJsValue(
                        toPrimFn,
                        [new JsValue(hintString)],
                        accessorJsValue,
                        context,
                        accessor is JsObject obj ? obj.RealmState?.Engine?.GlobalEnvironment : null);
                    if (context?.IsThrow == true)
                    {
                        return value;
                    }

                    if (IsPrimitiveValue(result))
                    {
                        return result;
                    }

                    var signal =
                        StandardLibrary.ThrowTypeError("Cannot convert object to primitive value", context);
                    if (context is null)
                    {
                        throw signal;
                    }

                    context.SetThrow(signal.ThrownValue);
                    return value;
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

            if (!TryInvokePropertyMethodJsValue(accessor, methodName, out var result, context))
            {
                // Check if the method threw an exception - if so, propagate it instead of trying the next method
                if (context?.IsThrow == true)
                {
                    return value;
                }
                // Method doesn't exist or is not callable - try the next method
                continue;
            }

            if (context?.IsThrow == true)
            {
                return value;
            }

            if (IsPrimitiveValue(result))
            {
                return result;
            }
        }

        if (accessor is HostFunction)
        {
            return new JsValue("function() { [native code] }");
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

        context.SetThrow(finalSignal.ThrownValue);
        return value;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public static string ToJsString(in JsValue value, EvaluationContext? context = null)
    {
        var realm = context?.RealmState;
        return value.Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "null",
            JsValueKind.Boolean => value.NumberValue != 0 ? "true" : "false",
            JsValueKind.Number => ToCanonicalNumberString(value.NumberValue),
            JsValueKind.String => value.ObjectValue as string ?? string.Empty,
            JsValueKind.Symbol => throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a string",
                context, realm),
            JsValueKind.BigInt => value.ObjectValue is JsBigInt bi ? bi.ToString() : string.Empty,
            JsValueKind.Object => value.ObjectValue.ToJsString(context, realm),
            _ => value.ObjectValue?.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// ECMAScript SameValue comparison for JsValue types.
    /// Unlike StrictEquals: NaN === NaN is true, -0 !== +0 is true.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static bool SameValue(in JsValue left, in JsValue right)
    {
        // Different types are never equal
        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Kind switch
        {
            JsValueKind.Undefined => true,
            JsValueKind.Null => true,
            JsValueKind.Boolean => left.NumberValue == right.NumberValue,
            JsValueKind.Number => SameValueNumber(left.NumberValue, right.NumberValue),
            JsValueKind.String => ReferenceEquals(left.ObjectValue, right.ObjectValue) ||
                                  string.Equals(left.ObjectValue as string, right.ObjectValue as string,
                                      StringComparison.Ordinal),
            JsValueKind.Symbol => ReferenceEquals(left.ObjectValue, right.ObjectValue),
            JsValueKind.BigInt => left.ObjectValue is JsBigInt lbi && right.ObjectValue is JsBigInt rbi && lbi == rbi,
            JsValueKind.Object => ReferenceEquals(left.ObjectValue, right.ObjectValue),
            _ => false
        };
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool SameValueNumber(double left, double right)
    {
        return left switch
        {
            // NaN equals NaN in SameValue
            double.NaN when double.IsNaN(right) => true,
            // -0 and +0 are different in SameValue
            0.0 when right == 0.0 => BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right),
            _ => left == right
        };
    }

    /// <summary>
    /// ECMAScript strict equality comparison for JsValue types.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static bool StrictEquals(in JsValue left, in JsValue right)
    {
        // Different types are never strictly equal
        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Kind switch
        {
            JsValueKind.Undefined => true,
            JsValueKind.Null => true,
            JsValueKind.Boolean => left.NumberValue == right.NumberValue,
            JsValueKind.Number => !double.IsNaN(left.NumberValue) && !double.IsNaN(right.NumberValue) &&
                                  left.NumberValue == right.NumberValue,
            JsValueKind.String => ReferenceEquals(left.ObjectValue, right.ObjectValue) ||
                                  string.Equals(left.ObjectValue as string, right.ObjectValue as string,
                                      StringComparison.Ordinal),
            JsValueKind.Symbol => ReferenceEquals(left.ObjectValue, right.ObjectValue),
            JsValueKind.BigInt => left.ObjectValue is JsBigInt lbi && right.ObjectValue is JsBigInt rbi && lbi == rbi,
            JsValueKind.Object => ReferenceEquals(left.ObjectValue, right.ObjectValue),
            _ => false
        };
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public static bool LooseEquals(in JsValue left, in JsValue right, EvaluationContext? context = null)
    {
        // Fast path for same-type comparisons
        if (left.Kind == right.Kind)
        {
            return left.Kind switch
            {
                JsValueKind.Undefined => true,
                JsValueKind.Null => true,
                JsValueKind.Boolean => left.NumberValue == right.NumberValue,
                JsValueKind.Number => left.NumberValue == right.NumberValue, // NaN != NaN per IEEE 754
                JsValueKind.String => string.Equals(left.ObjectValue as string, right.ObjectValue as string,
                    StringComparison.Ordinal),
                JsValueKind.Symbol => ReferenceEquals(left.ObjectValue, right.ObjectValue),
                JsValueKind.BigInt => left.ObjectValue is JsBigInt lbi && right.ObjectValue is JsBigInt rbi &&
                                      lbi == rbi,
                JsValueKind.Object => ReferenceEquals(left.ObjectValue, right.ObjectValue),
                _ => false
            };
        }

        // Fast path for null/undefined comparison
        if (
            (left.Kind == JsValueKind.Null && right.Kind == JsValueKind.Undefined) ||
             (left.Kind == JsValueKind.Undefined && right.Kind == JsValueKind.Null))
        {
            return true;
        }

        // Type coercion loop using JsValue
        var left1 = left;
        var right1 = right;
        while (true)
        {
            if (context?.IsThrow == true)
            {
                // Propagate the abrupt completion set by ToPrimitive/ToNumber/etc.
                throw new ThrowSignal(context.FlowValue);
            }

            var leftType = GetJsType(left1);
            var rightType = GetJsType(right1);

            if (leftType == rightType)
            {
                return StrictEquals(left1, right1);
            }

            if ((leftType == JsValueType.Null && rightType == JsValueType.Undefined) ||
                (leftType == JsValueType.Undefined && rightType == JsValueType.Null))
            {
                return true;
            }

            if ((left1.Kind == JsValueKind.Object && left1.ObjectValue is IIsHtmlDda) ||
                (right1.Kind == JsValueKind.Object && right1.ObjectValue is IIsHtmlDda))
            {
                return false;
            }

            switch (leftType)
            {
                case JsValueType.Number when rightType == JsValueType.String:
                    right1 = new JsValue(ToNumber(right1, context));
                    continue;
                case JsValueType.String when rightType == JsValueType.Number:
                    left1 = new JsValue(ToNumber(left1, context));
                    continue;
                case JsValueType.BigInt when rightType == JsValueType.String:
                    {
                        var rightStr = right1.ObjectValue as string ?? "";
                        return TryParseJsBigInt(rightStr, out var parsed) && StrictEquals(left1, new JsValue(parsed!));
                    }
                case JsValueType.String when rightType == JsValueType.BigInt:
                    {
                        var leftStr = left1.ObjectValue as string ?? "";
                        return TryParseJsBigInt(leftStr, out var parsed) && StrictEquals(new JsValue(parsed!), right1);
                    }
                case JsValueType.Boolean:
                    left1 = new JsValue(ToNumber(left1, context));
                    continue;
            }

            if (rightType == JsValueType.Boolean)
            {
                right1 = new JsValue(ToNumber(right1, context));
                continue;
            }

            switch (leftType)
            {
                case JsValueType.String or JsValueType.Number or JsValueType.BigInt or JsValueType.Symbol when rightType == JsValueType.Object:
                    right1 = ToPrimitive(right1, ToPrimitiveHint.Default, context);
                    continue;
                case JsValueType.Object when
                    rightType is JsValueType.String or JsValueType.Number or JsValueType.BigInt or JsValueType.Symbol:
                    left1 = ToPrimitive(left1, ToPrimitiveHint.Default, context);
                    continue;
                case JsValueType.Number when rightType == JsValueType.BigInt:
                    return NumberEqualsBigInt(left1, right1.AsBigInt());
                case JsValueType.BigInt when rightType == JsValueType.Number:
                    return NumberEqualsBigInt(right1, left1.AsBigInt());
                default:
                    return false;
            }
        }
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public static bool GreaterThan(in JsValue left, in JsValue right, EvaluationContext? context = null)
    {
        // Fast path for comparing two numbers
        if (left.Kind == JsValueKind.Number && right.Kind == JsValueKind.Number)
        {
            return left.NumberValue > right.NumberValue;
        }
        // Fast path for comparing two strings
        if (left.Kind == JsValueKind.String && right.Kind == JsValueKind.String)
        {
            return string.CompareOrdinal(left.ObjectValue as string, right.ObjectValue as string) > 0;
        }

        return PerformComparisonOperation(left, right, ComparisonOperator.GreaterThan, context);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public static bool GreaterThanOrEqual(in JsValue left, in JsValue right, EvaluationContext? context = null)
    {
        // Fast path for comparing two numbers
        if (left.Kind == JsValueKind.Number && right.Kind == JsValueKind.Number)
        {
            return left.NumberValue >= right.NumberValue;
        }
        // Fast path for comparing two strings
        if (left.Kind == JsValueKind.String && right.Kind == JsValueKind.String)
        {
            return string.CompareOrdinal(left.ObjectValue as string, right.ObjectValue as string) >= 0;
        }

        return PerformComparisonOperation(left, right, ComparisonOperator.GreaterThanOrEqual, context);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public static bool LessThan(in JsValue left, in JsValue right, EvaluationContext? context = null)
    {

        // Fast path for comparing two numbers
        if (left.Kind == JsValueKind.Number && right.Kind == JsValueKind.Number)
        {
            return left.NumberValue < right.NumberValue;
        }
        // Fast path for comparing two strings
        if (left.Kind == JsValueKind.String && right.Kind == JsValueKind.String)
        {
            return string.CompareOrdinal(left.ObjectValue as string, right.ObjectValue as string) < 0;
        }

        return PerformComparisonOperation(left, right, ComparisonOperator.LessThan, context);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public static bool LessThanOrEqual(in JsValue left, in JsValue right, EvaluationContext? context = null)
    {

        // Fast path for comparing two numbers
        if (left.Kind == JsValueKind.Number && right.Kind == JsValueKind.Number)
        {
            return left.NumberValue <= right.NumberValue;
        }
        // Fast path for comparing two strings
        if (left.Kind == JsValueKind.String && right.Kind == JsValueKind.String)
        {
            return string.CompareOrdinal(left.ObjectValue as string, right.ObjectValue as string) <= 0;
        }

        return PerformComparisonOperation(left, right, ComparisonOperator.LessThanOrEqual, context);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool PerformComparisonOperation(
        JsValue left,
        JsValue right,
        ComparisonOperator op,
        EvaluationContext? context)
    {
        // ES Spec 7.2.15 Abstract Relational Comparison
        // Step 1-3: Call ToPrimitive with hint "number" on both operands
        var leftPrimitive = left;
        if (left is { Kind: JsValueKind.Object, ObjectValue: IJsPropertyAccessor and not JsSymbol })
        {
            leftPrimitive = ToPrimitive(left, ToPrimitiveHint.Number, context);
            if (context?.IsThrow == true)
            {
                return false;
            }
        }

        var rightPrimitive = right;
        if (right is { Kind: JsValueKind.Object, ObjectValue: IJsPropertyAccessor and not JsSymbol })
        {
            rightPrimitive = ToPrimitive(right, ToPrimitiveHint.Number, context);
            if (context?.IsThrow == true)
            {
                return false;
            }
        }

        // Step 4: If both are strings, do string comparison
        if (leftPrimitive.Kind == JsValueKind.String && rightPrimitive.Kind == JsValueKind.String)
        {
            // String comparison: check if leftStr < rightStr lexicographically
            var comparison = string.CompareOrdinal(leftPrimitive.ObjectValue as string, rightPrimitive.ObjectValue as string);
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
        if (leftPrimitive.Kind == JsValueKind.String && rightPrimitive.IsBigInt)
        {
            if (!TryParseJsBigInt(leftPrimitive.ObjectValue as string ?? "", out var leftAsBigInt))
            {
                // String cannot be parsed as BigInt, return undefined (false)
                return false;
            }

            var rightBi = rightPrimitive.AsBigInt();
            return op switch
            {
                ComparisonOperator.LessThan => leftAsBigInt! < rightBi,
                ComparisonOperator.LessThanOrEqual => leftAsBigInt! <= rightBi,
                ComparisonOperator.GreaterThan => leftAsBigInt! > rightBi,
                ComparisonOperator.GreaterThanOrEqual => leftAsBigInt! >= rightBi,
                _ => false
            };
        }

        if (leftPrimitive.IsBigInt && rightPrimitive.Kind == JsValueKind.String)
        {
            if (!TryParseJsBigInt(rightPrimitive.ObjectValue as string ?? "", out var rightAsBigInt))
            {
                // String cannot be parsed as BigInt, return undefined (false)
                return false;
            }

            var leftBi = leftPrimitive.AsBigInt();
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
        var leftNumericJsValue = ToNumericAsJsValue(in leftPrimitive, context);
        if (context?.IsThrow == true)
        {
            return false;
        }

        var rightNumericJsValue = ToNumericAsJsValue(in rightPrimitive, context);
        if (context?.IsThrow == true)
        {
            return false;
        }

        // Step 5a: If both are BigInt, do BigInt comparison
        if (leftNumericJsValue.IsBigInt && rightNumericJsValue.IsBigInt)
        {
            var leftBigInt = leftNumericJsValue.AsBigInt();
            var rightBigInt = rightNumericJsValue.AsBigInt();
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
        var leftIsNumber = leftNumericJsValue.IsNumber;
        var rightIsNumber = rightNumericJsValue.IsNumber;
        var leftDouble = leftNumericJsValue.IsNumber ? leftNumericJsValue.NumberValue : 0;
        var rightDouble = rightNumericJsValue.IsNumber ? rightNumericJsValue.NumberValue : 0;

        if ((leftNumericJsValue.IsBigInt && rightIsNumber) ||
            (leftIsNumber && rightNumericJsValue.IsBigInt))
        {
            JsBigInt bigIntValue;
            double numberValue;
            bool bigIntIsLeft;

            if (leftNumericJsValue.IsBigInt)
            {
                bigIntValue = leftNumericJsValue.AsBigInt();
                numberValue = rightDouble;
                bigIntIsLeft = true;
            }
            else
            {
                bigIntValue = rightNumericJsValue.AsBigInt();
                numberValue = leftDouble;
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

                return op is ComparisonOperator.GreaterThan or ComparisonOperator.GreaterThanOrEqual;
            }

            if (double.IsNegativeInfinity(numberValue))
            {
                // BigInt > -Infinity is always true, BigInt <= -Infinity is always false
                if (bigIntIsLeft)
                {
                    return op is ComparisonOperator.GreaterThan or ComparisonOperator.GreaterThanOrEqual;
                }

                return op is ComparisonOperator.LessThan or ComparisonOperator.LessThanOrEqual;
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

                return op switch
                {
                    ComparisonOperator.LessThan => comparison > 0,
                    ComparisonOperator.LessThanOrEqual => comparison >= 0,
                    ComparisonOperator.GreaterThan => comparison < 0,
                    ComparisonOperator.GreaterThanOrEqual => comparison <= 0,
                    _ => false
                };
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

        // Step 5c: Both are Numbers, do Number comparison
        var leftNum = leftNumericJsValue.IsNumber ? leftNumericJsValue.NumberValue : ToNumber(leftNumericJsValue, context);
        var rightNum = rightNumericJsValue.IsNumber ? rightNumericJsValue.NumberValue : ToNumber(rightNumericJsValue, context);
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

    [MethodImpl(JsEngineConstants.Inlining)]
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

    [MethodImpl(JsEngineConstants.Inlining)]
    public static string? ToPropertyName(JsValue value, EvaluationContext? context = null)
    {
        while (true)
        {
            switch (value.Kind)
            {
                case JsValueKind.Undefined:
                    return JsValueCache.UndefinedString;
                case JsValueKind.Null:
                    return JsValueCache.NullString;
                case JsValueKind.Boolean:
                    return value.AsBoolean() ? JsValueCache.TrueString : JsValueCache.FalseString;
                case JsValueKind.Number:
                    return ToCanonicalNumberString(value.NumberValue);
                case JsValueKind.BigInt:
                    return value.AsBigInt().Value.ToString(CultureInfo.InvariantCulture);
                case JsValueKind.String:
                    return value.AsString();
                case JsValueKind.Symbol:
                    return value.ObjectValue switch
                    {
                        Symbol sym => sym.Name,
                        JsSymbol astSym => JsSymbol.PropertyKey(astSym),
                        _ => value.ObjectValue?.ToString() ?? string.Empty
                    };
                case JsValueKind.Object:
                    {
                        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
                        {
                            var primitive = ToPrimitive(JsValue.FromObjectUnsafe(accessor), ToPrimitiveHint.String, context);
                            if (context?.IsThrow == true)
                            {
                                return null;
                            }

                            value = primitive;
                            continue;
                        }

                        if (context?.IsThrow == true)
                        {
                            return null;
                        }

                        return Convert.ToString(value.ObjectValue, CultureInfo.InvariantCulture);
                    }
                case JsValueKind.Unit:
                case JsValueKind.Uninitialized:
                default:
                    return JsValueCache.UndefinedString;
            }
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

        // Fast path: if it's a whole number that fits in a long, use integer conversion.
        // This avoids precision loss from floating-point format strings for large integers.
        // The format "0.###################" rounds incorrectly for values near and above 2^53.
        // Note: Any integer that fits in a double without fraction is exactly representable
        // up to the limits of double precision, and can be safely converted to long.
        if (abs == Math.Truncate(abs) && abs <= long.MaxValue)
        {
            var intVal = (long)abs;
            return sign + intVal.ToString(CultureInfo.InvariantCulture);
        }

        var exponent = (int)Math.Floor(Math.Log10(abs));
        var useExponential = exponent is < -6 or >= 21;

        if (!useExponential)
        {
            // Fixed-point form for the mid-range magnitude.
            // Use "0.###################" format for non-integers - it works correctly for these.
            // Only large integers near MAX_SAFE_INTEGER need the special handling above.
            var fixedText = abs.ToString("0.###################", CultureInfo.InvariantCulture);
            return sign + fixedText;
        }

        var expText = abs.ToString("0.###################e+0", CultureInfo.InvariantCulture);
        var parts = expText.Split('e');
        var mantissa = parts[0].TrimEnd('0').TrimEnd('.');
        var expVal = int.Parse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var expStr = expVal >= 0 ? $"+{expVal.ToString(CultureInfo.InvariantCulture)}" : expVal.ToString(CultureInfo.InvariantCulture);

        return $"{sign}{mantissa}e{expStr}";
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool TryInvokePropertyMethodJsValue(IJsPropertyAccessor accessor, string methodName,
        out JsValue result,
        EvaluationContext? context)
    {
        result = JsValue.Undefined;
        if (!accessor.TryGetProperty(methodName, out var method) || !method.TryGetObject<IJsCallable>(out var callable))
        {
            return false;
        }

        try
        {
            result = TypedAstEvaluator.InvokeCallableJsValue(callable, [], JsValue.FromObjectUnsafe(accessor), context,
                accessor is JsObject obj ? obj.RealmState?.Engine?.GlobalEnvironment : null);
            return context?.IsThrow != true;
        }
        catch (ThrowSignal signal) when (context is not null)
        {

            context.SetThrow(signal.ThrownValue);
            return false;

        }
    }

    /// <summary>
    /// Checks if a JsValue represents a primitive value (not an object that needs ToPrimitive conversion).
    /// Primitives are: undefined, null, boolean, number, string, symbol, bigint.
    /// Objects wrapped as JsValue with ObjectValue being Symbol or TypedAstSymbol are also considered primitives.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool IsPrimitiveValue(JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined or JsValueKind.Null or JsValueKind.Boolean or
                JsValueKind.Number or JsValueKind.String or JsValueKind.Symbol or JsValueKind.BigInt => true,
            JsValueKind.Object => value.ObjectValue is JsSymbol or Symbol,
            _ => false
        };
    }

    /// <summary>
    /// JsValue overload for GetPrototypePointer.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static IJsPropertyAccessor? GetPrototypePointer(JsValue value)
    {
        // Only objects have prototypes
        if (value.Kind != JsValueKind.Object)
        {
            return null;
        }

        var obj = value.ObjectValue;
        return obj switch
        {
            IPrototypeAccessorProvider { PrototypeAccessor: { } protoAccessor } => protoAccessor,
            IJsObjectLike { Prototype: { } proto } => proto,
            JsObject { Prototype: { } jsProto } => jsProto,
            _ => null
        };
    }

    public static string GetRequiredPropertyName(JsValue value, EvaluationContext? context = null)
    {
        var name = ToPropertyName(value, context);
        if (context?.IsThrow == true)
        {
            return string.Empty;
        }

        return name ?? throw new InvalidOperationException("Property name cannot be null.");
    }

    /// <summary>
    /// JsValue overload for TryResolveArrayIndex. Avoids boxing when the candidate is already a JsValue.
    /// </summary>
    public static bool TryResolveArrayIndex(JsValue candidate, out int index, EvaluationContext? context = null)
    {
        while (true)
        {
            // Fast path for common numeric types
            switch (candidate.Kind)
            {
                case JsValueKind.Number:
                    var d = candidate.AsDouble();
                    if (!double.IsNaN(d) && !double.IsInfinity(d) && d >= 0)
                    {
                        var truncated = Math.Truncate(d);
                        if (Math.Abs(truncated - d) <= double.Epsilon && truncated <= int.MaxValue)
                        {
                            index = (int)truncated;
                            return true;
                        }
                    }

                    break;
                case JsValueKind.String:
                    var s = candidate.AsString();
                    if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
                    {
                        index = parsed;
                        return true;
                    }

                    break;
                case JsValueKind.BigInt:
                    var bigInt = candidate.AsBigInt();
                    if (bigInt.Value >= BigInteger.Zero && bigInt.Value <= int.MaxValue)
                    {
                        index = (int)bigInt.Value;
                        return true;
                    }

                    break;
            }

            // Fall through to object-based resolution for wrapped objects
            if (candidate is { Kind: JsValueKind.Object, ObjectValue: JsObject jsObj } &&
                jsObj.TryGetValue("__value__", out var innerValue))
            {
                candidate = JsValue.FromObjectUnsafe(innerValue);
                continue;
            }

            var coerced = ToPropertyName(candidate, context);
            if (coerced is not null)
            {
                if (int.TryParse(coerced, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCoerced) &&
                    parsedCoerced >= 0)
                {
                    index = parsedCoerced;
                    return true;
                }
            }

            index = 0;
            return false;
        }
    }

    /// <summary>
    /// JsValue overload for GetTypeofString. Returns the typeof string for a JsValue.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static string GetTypeofString(JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "object",
            JsValueKind.Boolean => "boolean",
            JsValueKind.Number => "number",
            JsValueKind.String => "string",
            JsValueKind.Symbol => "symbol",
            JsValueKind.BigInt => "bigint",
            JsValueKind.Object => value.ObjectValue switch
            {
                IIsHtmlDda => "undefined",
                JsProxy proxy => proxy.Target is IJsCallable ? "function" : "object",
                IJsCallable => "function",
                _ => "object"
            },
            _ => "undefined"
        };
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValueType GetJsType(JsValue value)
    {
        // Special case: IIsHtmlDda objects report as undefined for typeof
        if (value is { Kind: JsValueKind.Object, ObjectValue: IIsHtmlDda })
        {
            return JsValueType.Undefined;
        }

        return value.Kind switch
        {
            JsValueKind.Undefined => JsValueType.Undefined,
            JsValueKind.Null => JsValueType.Null,
            JsValueKind.Boolean => JsValueType.Boolean,
            JsValueKind.Number => JsValueType.Number,
            JsValueKind.BigInt => JsValueType.BigInt,
            JsValueKind.String => JsValueType.String,
            JsValueKind.Symbol => JsValueType.Symbol,
            JsValueKind.Object => JsValueType.Object,
            _ => JsValueType.Object
        };
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool NumberEqualsBigInt(JsValue numberValue, JsBigInt bigInt)
    {
        var num = ToNumber(numberValue, null);
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

    [MethodImpl(JsEngineConstants.Inlining)]
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

        if (!TryParsePrefixedBigInt(span, 16, NumberStyles.AllowHexSpecifier, out var parsed) &&
            !NumericStringParser.TryParseBinary(span, out parsed) &&
            !NumericStringParser.TryParseOctal(span, out parsed) &&
            !BigInteger.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
        {
            return false;
        }

        value = new JsBigInt(isNegative ? BigInteger.Negate(parsed) : parsed);
        return true;

    }

    [MethodImpl(JsEngineConstants.Inlining)]
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
        if ((styles & NumberStyles.AllowHexSpecifier) == NumberStyles.None)
        {
            return BigInteger.TryParse(digits, styles, CultureInfo.InvariantCulture, out value);
        }

        var padded = $"0{digits.ToString()}";
        return BigInteger.TryParse(padded, styles, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Implements [[HasProperty]] internal method for the 'in' operator.
    /// Returns true if the property exists on the object or its prototype chain.
    /// Per ES spec, [[HasProperty]] uses [[GetOwnProperty]] to check for existence,
    /// it does NOT invoke getters like [[Get]] would.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static bool HasProperty(JsValue target, string propertyName)
    {
        // Fast path: only objects can have properties via prototype chain
        if (target.Kind != JsValueKind.Object)
        {
            return false;
        }

        // Walk the prototype chain checking for the property via [[GetOwnProperty]]
        // This does NOT invoke getters - it only checks if the property exists
        var current = target.ObjectValue;
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
            else
            {
                if (current is IJsPropertyAccessor accessor)
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
                }

                break;
            }
        }

        return false;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public static bool TryGetPropertyValue(JsValue target, string propertyName, out JsValue value,
        EvaluationContext? context = null)
    {
        switch (target.Kind)
        {
            // Handle objects first - most common case
            case JsValueKind.Object or JsValueKind.String or JsValueKind.Symbol or JsValueKind.BigInt:
                {
                    var targetObj = target.ObjectValue;
                    if (targetObj != null && TryGetPropertyValueObject(targetObj, propertyName, out var objValue, context))
                    {
                        value = objValue;
                        return true;
                    }

                    value = JsValue.Undefined;
                    return false;
                }
            // Handle primitives (Boolean, Number) - need prototype chain lookup
            case JsValueKind.Boolean when (context?.RealmState?.BooleanPrototype is { } booleanProto &&
                                           booleanProto.TryGetProperty(propertyName, target.AsBoolean(), context, out var boolValue)):
                {
                    value = boolValue is JsValue jv ? jv : JsValue.FromObjectUnsafe(boolValue);
                    return true;
                }
            case JsValueKind.Boolean:
                value = JsValue.Undefined;
                return false;
            case JsValueKind.Number when (context?.RealmState?.NumberPrototype is { } numberProto &&
                                          numberProto.TryGetProperty(propertyName, target.NumberValue, context, out var numValue)):
                {
                    value = numValue is JsValue jv ? jv : JsValue.FromObjectUnsafe(numValue);
                    return true;
                }
            default:
                value = JsValue.Undefined;
                return false;
        }
    }

    /// <summary>
    /// JsValue overload for property access with JsValue property key.
    /// Avoids boxing when both target and key are JsValue.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static bool TryGetPropertyValueJsValue(JsValue target, JsValue propertyKey, out JsValue value,
        EvaluationContext? context = null)
    {
        if (context?.IsThrow == true)
        {
            value = JsValue.Undefined;
            return false;
        }

        // Fast path for array-like access
        if (TryGetArrayLikeValueJsValue(target, propertyKey, out value, context))
        {
            return true;
        }

        if (context?.IsThrow == true)
        {
            value = JsValue.Undefined;
            return false;
        }

        var propertyName = ToPropertyName(propertyKey, context);
        if (context?.IsThrow == true)
        {
            value = JsValue.Undefined;
            return false;
        }

        if (propertyName is null)
        {
            value = JsValue.Undefined;
            return true;
        }

        try
        {
            return TryGetPropertyValue(target, propertyName, out value, context);
        }
        catch (ThrowSignal signal) when (context is not null)
        {
            context.SetThrow(signal.ThrownValue);
            value = signal.ThrownValue;
            return true;

        }
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool TryGetArrayLikeValueJsValue(JsValue target, JsValue propertyKey, out JsValue value,
        EvaluationContext? context)
    {
        if (target.TryGetObject<JsArray>(out var jsArray) &&
            TryResolveArrayIndexJsValue(propertyKey, out var arrayIndex, context))
        {
            if (arrayIndex >= 0 && jsArray.HasOwnIndex((uint)arrayIndex))
            {
                value = jsArray.GetElement(arrayIndex);
                return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        if (target.TryGetObject<TypedArrayBase>(out var typedArray) &&
            TryResolveArrayIndexJsValue(propertyKey, out var typedIndex, context))
        {
            if (typedIndex >= 0 && typedIndex < typedArray.Length)
            {
                value = JsValue.FromDouble(typedArray.GetElement(typedIndex));
            }
            else
            {
                value = JsValue.Undefined;
            }

            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool TryGetPropertyValueObject(object? target, string propertyName, out JsValue value,
        EvaluationContext? context)
    {
        switch (target)
        {
            case JsValue jsVal:
                return TryGetPropertyValue(jsVal, propertyName, out value, context);
            case IJsPropertyAccessor propertyAccessor:
                try
                {
                    switch (propertyAccessor)
                    {
                        case JsObject jsObject:
                            if (jsObject.TryGetProperty(propertyName, target, context, out var jsObjVal))
                            {
                                value = jsObjVal is JsValue jsv ? jsv : JsValue.FromObjectUnsafe(jsObjVal);
                                return true;
                            }

                            value = JsValue.Undefined;
                            return false;
                        // For Symbol primitives, first try own properties, then fall back to Symbol.prototype
                        case JsSymbol symbol when symbol.TryGetProperty(propertyName, out var symbolJsValue):
                            value = symbolJsValue;
                            return true;
                        // Look up in Symbol.prototype chain
                        case JsSymbol:
                            {
                                var symbolProto = context?.RealmState?.SymbolPrototype;
                                if (symbolProto is not null &&
                                    symbolProto.TryGetProperty(propertyName, target, context, out var protoVal))
                                {
                                    value = protoVal is JsValue protoJs ? protoJs : JsValue.FromObjectUnsafe(protoVal);
                                    return true;
                                }

                                value = JsValue.Undefined;
                                return false;
                            }
                    }

                    if (propertyAccessor.TryGetProperty(propertyName, JsValue.FromObjectUnsafe(target), out var jsVal2))
                    {
                        value = jsVal2 is JsValue jv ? jv : jsVal2;
                        return true;
                    }

                    value = JsValue.Undefined;
                    return false;
                }
                catch (ThrowSignal signal) when (context is not null)
                {
                    context.SetThrow(signal.ThrownValue);
                    value = signal.ThrownValue;
                    return true;
                }
            case bool:
                if (context?.RealmState?.BooleanPrototype is { } booleanProto &&
                    booleanProto.TryGetProperty(propertyName, target, context, out var boolVal))
                {
                    value = boolVal is JsValue jv ? jv : JsValue.FromObjectUnsafe(boolVal);
                    return true;
                }

                value = JsValue.Undefined;
                return false;
            case double:
                if (context?.RealmState?.NumberPrototype is { } numberProto &&
                    numberProto.TryGetProperty(propertyName, target, context, out var numVal))
                {
                    value = numVal is JsValue jv ? jv : JsValue.FromObjectUnsafe(numVal);
                    return true;
                }

                value = JsValue.Undefined;
                return false;
            case JsBigInt:
                if (context?.RealmState?.BigIntPrototype is { } bigIntProto &&
                    bigIntProto.TryGetProperty(propertyName, target, context, out var bigIntVal))
                {
                    value = bigIntVal is JsValue jv ? jv : JsValue.FromObjectUnsafe(bigIntVal);
                    return true;
                }

                value = JsValue.Undefined;
                return false;
            case string str:
                if (string.Equals(propertyName, "length", StringComparison.Ordinal))
                {
                    value = new JsValue((double)str.Length);
                    return true;
                }

                if (int.TryParse(propertyName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                    index >= 0 && index < str.Length)
                {
                    value = new JsValue(str[index].ToString());
                    return true;
                }

                if (context?.RealmState?.StringPrototype is { } stringProto &&
                    stringProto.TryGetProperty(propertyName, target, context, out var stringVal))
                {
                    value = stringVal is JsValue jv ? jv : JsValue.FromObjectUnsafe(stringVal);
                    return true;
                }

                value = JsValue.Undefined;
                return false;
            case JsRopeString rope:
                {
                    var ropeStr = rope.Flatten();
                    if (string.Equals(propertyName, "length", StringComparison.Ordinal))
                    {
                        value = new JsValue((double)ropeStr.Length);
                        return true;
                    }

                    if (int.TryParse(propertyName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ropeIndex) &&
                        ropeIndex >= 0 && ropeIndex < ropeStr.Length)
                    {
                        value = new JsValue(ropeStr[ropeIndex].ToString());
                        return true;
                    }

                    if (context?.RealmState?.StringPrototype is { } ropeStringProto &&
                        ropeStringProto.TryGetProperty(propertyName, ropeStr, context, out var ropeVal))
                    {
                        value = ropeVal is JsValue jv ? jv : JsValue.FromObjectUnsafe(ropeVal);
                        return true;
                    }

                    value = JsValue.Undefined;
                    return false;
                }
            default:
                value = JsValue.Undefined;
                return false;
        }
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool TryResolveArrayIndexJsValue(JsValue propertyKey, out int index, EvaluationContext? context)
    {
        // Fast path for numbers
        if (propertyKey.Kind == JsValueKind.Number)
        {
            var num = propertyKey.NumberValue;
            if (num is >= 0 and <= int.MaxValue && num == Math.Floor(num))
            {
                index = (int)num;
                return true;
            }
        }

        // Fall back to string conversion
        var propertyName = ToPropertyName(propertyKey, context);
        if (propertyName is not null && int.TryParse(propertyName, CultureInfo.InvariantCulture, out index) && index >= 0)
        {
            return true;
        }

        index = -1;
        return false;
    }

    /// <summary>
    /// JsValue overload for IsConstructor. Returns true if the value is a constructor.
    /// </summary>
    public static bool IsConstructor(JsValue value)
    {
        // Only objects can be constructors
        if (value.Kind != JsValueKind.Object)
        {
            return false;
        }

        var obj = value.ObjectValue;
        while (obj is not null)
        {
            switch (obj)
            {
                case JsProxy proxy:
                    obj = proxy.Target;
                    continue;
                case HostFunction host:
                    return host is { IsConstructor: true, DisallowConstruct: false };
                case ICallableMetadata { IsArrowFunction: true }:
                case ICallableMetadata { DisallowConstruct: true }:
                    return false;
            }

            return obj is IJsCallable;
        }

        return false;
    }

    /// <summary>
    /// JsValue overload for property assignment. Avoids boxing when all parameters are JsValue.
    /// </summary>
    public static void AssignPropertyValueJsValue(JsValue target, JsValue propertyKey, JsValue value,
        EvaluationContext? context = null)
    {
        try
        {
            if (TryAssignArrayLikeValueJsValue(target, propertyKey, value, context))
            {
                return;
            }

            var propertyName = GetRequiredPropertyName(propertyKey, context);

            AssignPropertyValueByNameJsValue(target, propertyName, value);
        }
        catch (ThrowSignal signal) when (context is not null)
        {
            context.SetThrow(signal.ThrownValue);
        }
    }

    /// <summary>
    /// JsValue overload for property assignment by name. Avoids boxing.
    /// </summary>
    public static void AssignPropertyValueByNameJsValue(JsValue target, string propertyName, JsValue value)
    {
        if (target.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            accessor.SetProperty(propertyName, value, target);
            return;
        }

        throw new InvalidOperationException($"Cannot assign property '{propertyName}' on value '{target}'.");
    }

    private static bool TryAssignArrayLikeValueJsValue(JsValue target, JsValue propertyKey, JsValue value,
        EvaluationContext? context)
    {
        if (target.TryGetObject<JsArray>(out var jsArray))
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

            var isArrayIndex = TryResolveArrayIndexJsValue(propertyKey, out var index, context);
            var ownDescriptor = jsArray.GetOwnPropertyDescriptor(propertyName);

            if (!isArrayIndex)
            {
                return false;
            }

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

                    TypedAstEvaluator.InvokeCallableJsValue(ownDescriptor.Set, [value], target, context);
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
            }

            // Check prototype chain for inherited setters
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

                        TypedAstEvaluator.InvokeCallableJsValue(inheritedDescriptor.Set,
                            [value], JsValue.FromJsArray(jsArray), context);
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

                    // If data descriptor, shadow it on own array
                    jsArray.DefineProperty(propertyName,
                        new PropertyDescriptor
                        {
                            Value = value,
                            Writable = true,
                            Enumerable = true,
                            Configurable = true
                        });
                    return true;
                }

                current = GetPrototypePointer(JsValue.FromObjectUnsafe(current));
            }

            jsArray.SetElement(index, value);
            return true;

        }

        if (!target.TryGetObject<TypedArrayBase>(out var typedArray) ||
            !TryResolveArrayIndexJsValue(propertyKey, out var typedIndex, context))
        {
            return false;
        }

        if (typedIndex >= 0 && typedIndex < typedArray.Length)
        {
            typedArray.SetElement(typedIndex, ToNumber(value, context));
        }

        return true;

    }

    /// <summary>
    /// JsValue overload for property deletion. Avoids boxing when both parameters are JsValue.
    /// </summary>
    public static bool DeletePropertyValueJsValue(JsValue target, JsValue propertyKey,
        EvaluationContext? context = null)
    {
        if (target.IsNullish)
        {
            throw StandardLibrary.ThrowTypeError("Cannot delete property on null or undefined", context,
                context?.RealmState);
        }

        if (target.TryGetObject<JsArray>(out var jsArray))
        {
            var propertyName = ToPropertyName(propertyKey, context);
            return propertyName is null || jsArray.Delete(propertyName);
        }

        if (target.TryGetObject<HostFunction>(out var hostFunc))
        {
            var propertyName = ToPropertyName(propertyKey, context);
            return propertyName is null || hostFunc.DeleteProperty(propertyName);
        }

        if (target.TryGetObject<ModuleNamespace>(out var moduleNamespace))
        {
            var propertyName = ToPropertyName(propertyKey, context);
            return propertyName is null || moduleNamespace.Delete(propertyName);
        }

        if (target.TryGetObject<TypedArrayBase>(out var typedArray))
        {
            if (TryResolveArrayIndexJsValue(propertyKey, out _, context))
            {
                return false;
            }

            var propertyName = ToPropertyName(propertyKey, context);
            return propertyName is null || typedArray.DeleteProperty(propertyName);
        }

        if (target.TryGetObject<JsArgumentsObject>(out var argumentsObject))
        {
            var propertyName = ToPropertyName(propertyKey, context);
            return propertyName is null || argumentsObject.Delete(propertyName);
        }

        var resolvedName = ToPropertyName(propertyKey, context);
        if (resolvedName is null)
        {
            return true;
        }

        if (target.TryGetObject<IJsObjectLike>(out var objectLike))
        {
            return objectLike.Delete(resolvedName);
        }

        // Deleting primitives or other non-object values is a no-op that succeeds
        return true;
    }

    private enum ComparisonOperator
    {
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual
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

    #region Arithmetic Operations

    /// <summary>
    /// Addition operation (a + b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue Add(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return TypedAstEvaluator.Add(a, b, ctx);
    }

    /// <summary>
    /// Subtraction operation (a - b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue Sub(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return TypedAstEvaluator.Subtract(a, b, ctx);
    }

    /// <summary>
    /// Multiplication operation (a * b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue Mul(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return TypedAstEvaluator.Multiply(a, b, ctx);
    }

    /// <summary>
    /// Division operation (a / b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue Div(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return TypedAstEvaluator.Divide(a, b, ctx);
    }

    /// <summary>
    /// Modulo operation (a % b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue Mod(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return TypedAstEvaluator.Modulo(a, b, ctx);
    }

    /// <summary>
    /// Exponentiation operation (a ** b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue Exp(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return TypedAstEvaluator.Power(a, b, ctx);
    }

    #endregion

    #region Comparison Operations

    /// <summary>
    /// Loose equality comparison (a == b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue Eq(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return LooseEquals(a, b, ctx) ? JsValue.True : JsValue.False;
    }

    /// <summary>
    /// Strict equality comparison (a === b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue StrictEq(JsValue a, JsValue b)
    {
        return StrictEquals(a, b) ? JsValue.True : JsValue.False;
    }

    /// <summary>
    /// Less than comparison (a &lt; b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue Lt(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return LessThan(a, b, ctx) ? JsValue.True : JsValue.False;
    }

    /// <summary>
    /// Less than or equal comparison (a &lt;= b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue Lte(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return LessThanOrEqual(a, b, ctx) ? JsValue.True : JsValue.False;
    }

    /// <summary>
    /// Greater than comparison (a &gt; b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue Gt(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return GreaterThan(a, b, ctx) ? JsValue.True : JsValue.False;
    }

    /// <summary>
    /// Greater than or equal comparison (a &gt;= b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue Gte(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return GreaterThanOrEqual(a, b, ctx) ? JsValue.True : JsValue.False;
    }

    #endregion

    #region Unary Operations

    /// <summary>
    /// Unary negation (-a).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue Neg(JsValue a, EvaluationContext ctx)
    {
        return TypedAstEvaluator.Negate(a, ctx);
    }

    /// <summary>
    /// Logical NOT (!a).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue Not(JsValue a)
    {
        return ToBoolean(a) ? JsValue.False : JsValue.True;
    }

    /// <summary>
    /// Typeof operator (typeof a).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue TypeOf(JsValue a)
    {
        return new JsValue(GetTypeofString(a));
    }

    #endregion

    #region Bitwise Operations

    /// <summary>
    /// Bitwise AND (a &amp; b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue BitAnd(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return TypedAstEvaluator.BitwiseAnd(a, b, ctx);
    }

    /// <summary>
    /// Bitwise OR (a | b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue BitOr(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return TypedAstEvaluator.BitwiseOr(a, b, ctx);
    }

    /// <summary>
    /// Bitwise XOR (a ^ b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue BitXor(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return TypedAstEvaluator.BitwiseXor(a, b, ctx);
    }

    /// <summary>
    /// Bitwise NOT (~a).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue BitNot(JsValue a, EvaluationContext ctx)
    {
        return TypedAstEvaluator.BitwiseNot(a, ctx);
    }

    /// <summary>
    /// Left shift (a &lt;&lt; b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue LeftShift(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return TypedAstEvaluator.LeftShift(a, b, ctx);
    }

    /// <summary>
    /// Right shift (a &gt;&gt; b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue RightShift(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return TypedAstEvaluator.RightShift(a, b, ctx);
    }

    /// <summary>
    /// Unsigned right shift (a &gt;&gt;&gt; b).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    public static JsValue UnsignedRightShift(JsValue a, JsValue b, EvaluationContext ctx)
    {
        return TypedAstEvaluator.UnsignedRightShift(a, b, ctx);
    }

    #endregion
}
