#region

using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;
using static Asynkron.JsEngine.StdLib.BooleanHelper;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsOddInteger(double value)
    {
        return double.IsFinite(value) && value % 1 == 0 && Math.Abs(value % 2) == 1;
    }

    /// <summary>
    /// ECMAScript-compliant modulo operation that properly handles negative zero.
    /// Per ES spec 13.15.3: The result of a remainder operation preserves the sign of the dividend.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [Obsolete("Use JsValue overload for better performance and correctness.")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNullish(this object? value)
    {
        if (value is JsValue jsValue)
        {
            return jsValue.IsNullOrUndefined;
        }

        return value is null ||
               (value is Symbol sym && ReferenceEquals(sym, Symbol.Undefined));
    }

    /// <summary>
    ///     ECMAScript-like ToBoolean semantics for engine values.
    ///     Kept in sync with <see cref="IsTruthy" /> which is the legacy name used throughout the codebase.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    /// <summary>
    ///     ECMAScript-like ToBoolean semantics for engine values (object? overload for backward compatibility).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Obsolete("Use JsValue overload for better performance and correctness.")]
    public static bool ToBoolean(object? value)
    {
        return value switch
        {
            null => false,
            JsValue jsValue => ToBoolean(jsValue),
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
    public static bool IsTruthy(in JsValue value)
    {
        return ToBoolean(in value);
    }

    public static double ToNumber(in JsValue value, EvaluationContext? context = null)
    {
        return ToNumberWithContext(in value, context);
    }

    [Obsolete("Use JsValue overload for better performance and correctness.")]
    public static double ToNumber(object? value, EvaluationContext? context = null)
    {
        return ToNumberWithContext(value, context);
    }

    public static JsValue ToNumeric(in JsValue value, EvaluationContext? context = null)
    {
        return ToNumericAsJsValue(in value, context);
    }

    [Obsolete("Use JsValue overload for better performance and correctness.")]
    private static object ToNumeric(object? value, EvaluationContext? context = null)
    {
        var result = ToNumericAsJsValue(value, context);
        // Important: explicit casts to object to avoid implicit JsValue conversions
        if (result.IsNumber)
        {
            return result;
        }

        if (result.IsBigInt)
        {
            return result.AsBigInt();
        }

        return result;
    }

    /// <summary>
    /// Converts a JsValue to numeric as JsValue without boxing. Use this for internal arithmetic operations.
    /// Returns JsValue with Number or BigInt kind. On error, returns JsValue.Undefined (check context.IsThrow).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue ToNumericAsJsValue(in JsValue value, EvaluationContext? context = null)
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

    /// <summary>
    /// Converts a value to numeric as JsValue without boxing. Use this for internal arithmetic operations.
    /// Returns JsValue with Number or BigInt kind. On error, returns JsValue.Undefined (check context.IsThrow).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Obsolete("Use JsValue overload for better performance and correctness.")]
    private static JsValue ToNumericAsJsValue(object? value, EvaluationContext? context = null)
    {
        return value switch
        {
            // Fast paths for already-numeric types (avoid full conversion)
            double d => new JsValue(d),
            JsBigInt bi => new JsValue(bi),
            int i => new JsValue((double)i),
            JsValue jsv => ToNumericAsJsValue(jsv, context),
            _ => ToNumericCore(value, context)
        };
    }

    public static double ToNumberWithContext(in JsValue value, EvaluationContext? context = null)
    {
        var result = ToNumericAsJsValue(in value, context);
        if (result.IsNumber)
        {
            return result.NumberValue;
        }

        if (result.IsBigInt)
        {
            return (double)result.AsBigInt().Value;
        }

        return double.NaN;
    }

    [Obsolete("Use JsValue overload for better performance and correctness.")]
    public static double ToNumberWithContext(object? value, EvaluationContext? context = null)
    {
        var result = ToNumericAsJsValue(value, context);
        if (result.IsNumber)
        {
            return result.NumberValue;
        }

        if (result.IsBigInt)
        {
            return (double)result.AsBigInt().Value;
        }

        return double.NaN;
    }

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
                case TypedAstSymbol:
                {
                    var error = CreateTypeError("Cannot convert a Symbol value to a number", context);
                    if (context is null)
                    {
                        throw new ThrowSignal(JsValue.FromObjectUnsafe(error));
                    }

                    context.SetThrow(JsValue.FromObjectUnsafe(error));
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
                            symbolData.TryGetObject<TypedAstSymbol>(out var sym))
                        {
                            value = sym;
                            continue;
                        }
                    }

#pragma warning disable CS0618 // Transitional method uses object? API
                    if (TryConvertToNumericPrimitive(accessor, out var primitive, context))
#pragma warning restore CS0618
                    {
                        value = primitive;
                        continue;
                    }

                    if (context?.IsThrow == true)
                    {
                        return JsValue.Undefined; // Error state - caller should check context.IsThrow
                    }

                    var error = CreateTypeError("Cannot convert object to primitive value", context);
                    if (context is null)
                    {
                        throw new ThrowSignal(JsValue.FromObjectUnsafe(error));
                    }

                    context.SetThrow(JsValue.FromObjectUnsafe(error));
                    return JsValue.Undefined; // Error state - caller should check context.IsThrow
                }
                default:
                    throw new InvalidOperationException($"Cannot convert value '{value}' to a number.");
            }
        }
    }

    [Obsolete("Use JsValue overload for better performance and correctness.")]
    private static bool TryConvertToNumericPrimitive(IJsPropertyAccessor accessor, out object? primitive,
        EvaluationContext? context)
    {
        primitive = null;
        var attempted = false;

#pragma warning disable CS0618 // Transitional wrapper uses object? API
        if (TryGetPropertyValue(accessor, SymbolKeys.ToPrimitive, out var toPrimitive, context))
#pragma warning restore CS0618
        {
            if (context?.IsThrow == true)
            {
                return false;
            }

            if (toPrimitive is IJsCallable toPrimFn)
            {
                try
                {
                    var result = TypedAstEvaluator.InvokeCallableJsValue(
                        toPrimFn,
                        [new JsValue("number")],
                        JsValue.FromObjectUnsafe(accessor),
                        context,
                        accessor is JsObject obj ? obj.RealmState?.Engine?.GlobalEnvironment : null);
                    if (context?.IsThrow == true)
                    {
                        return false;
                    }

                    if (IsPrimitiveValue(result))
                    {
#pragma warning disable CS0618 // Transitional wrapper returns object?
                        primitive = result.IsObject ? result.ObjectValue : result.ToObject();
#pragma warning restore CS0618
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
#pragma warning disable CS0618 // Transitional wrapper returns object?
                    primitive = valueOfResult.IsObject ? valueOfResult.ObjectValue : valueOfResult.ToObject();
#pragma warning restore CS0618
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
        if (toStringExists)
        {
            attempted = true;
            var toStringAttempted = TryInvokePropertyMethodJsValue(accessor, "toString", out var toStringResult, context);
            if (toStringAttempted)
            {
                if (IsPrimitiveValue(toStringResult))
                {
#pragma warning disable CS0618 // Transitional wrapper returns object?
                    primitive = toStringResult.IsObject ? toStringResult.ObjectValue : toStringResult.ToObject();
#pragma warning restore CS0618
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
            throw new ThrowSignal(JsValue.FromObjectUnsafe(error));
        }

        context.SetThrow(JsValue.FromObjectUnsafe(error));

        return false;
    }

    /// <summary>
    /// Converts a value to a primitive using the specified hint enum (faster than string version).
    /// </summary>
    [Obsolete("Use JsValue overload for better performance and correctness.")]
    public static object? ToPrimitive(object? value, ToPrimitiveHint hint, EvaluationContext? context = null)
    {
        if (value is TypedAstSymbol || value is not IJsPropertyAccessor accessor)
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

            if (!toPrimitive.IsNullish() && toPrimitive is not IJsCallable)
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
                    var result = TypedAstEvaluator.InvokeCallableJsValue(
                        toPrimFn,
                        [new JsValue(hintString)],
                        JsValue.FromObjectUnsafe(accessor),
                        context,
                        accessor is JsObject obj ? obj.RealmState?.Engine?.GlobalEnvironment : null);
                    if (context?.IsThrow == true)
                    {
                        return value;
                    }

                    if (IsPrimitiveValue(result))
                    {
                        return result.IsObject ? result.ObjectValue : result.ToObject();
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
                continue;
            }

            if (context?.IsThrow == true)
            {
                return value;
            }

            if (IsPrimitiveValue(result))
            {
                return result.IsObject ? result.ObjectValue : result.ToObject();
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

        context.SetThrow(finalSignal.ThrownValue);
        return value;
    }

    /// <summary>
    /// JsValue overload for ToPrimitive. Returns object? since primitives can be various types.
    /// </summary>
    public static object? ToPrimitive(JsValue value, ToPrimitiveHint hint, EvaluationContext? context = null)
    {
        // Fast path: already a primitive
        return value.Kind switch
        {
            JsValueKind.Undefined => Symbol.Undefined,
            JsValueKind.Null => null,
            JsValueKind.Boolean => value.NumberValue != 0,
            JsValueKind.Number => value.NumberValue,
            JsValueKind.String => value.ObjectValue,
            JsValueKind.Symbol => value.ObjectValue,
            JsValueKind.BigInt => value.ObjectValue,
#pragma warning disable CS0618 // For objects, delegate to the object? version
            JsValueKind.Object => ToPrimitive(value.ObjectValue, hint, context),
#pragma warning restore CS0618
            _ => value.ObjectValue
        };
    }

    [Obsolete("Use JsValue overload for better performance and correctness.")]
    public static string ToJsString(object? value, EvaluationContext? context = null)
    {
        return value.ToJsString(context, context?.RealmState);
    }

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Obsolete("Use JsValue overload for better performance and correctness.")]
    private static bool StrictEquals(object? left, object? right)
    {
        // Fast path for JsValue
        if (left is JsValue ljv && right is JsValue rjv)
        {
            return StrictEquals(ljv, rjv);
        }

        if (ReferenceEquals(left, right))
        {
            return left is not (double and double.NaN);
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
        if ((left.Kind == JsValueKind.Null && right.Kind == JsValueKind.Undefined) ||
            (left.Kind == JsValueKind.Undefined && right.Kind == JsValueKind.Null))
        {
            return true;
        }

        // Delegate to object? version for type coercion
#pragma warning disable CS0618 // Delegate to object? version
        var left1 = ExtractValueForComparison(in left);
        var right1 = ExtractValueForComparison(in right);
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

            if (left1 is IIsHtmlDda || right1 is IIsHtmlDda)
            {
                return false;
            }

            switch (leftType)
            {
                case JsValueType.Number when rightType == JsValueType.String:
                    right1 = ToNumber(right1, context);
                    continue;
                case JsValueType.String when rightType == JsValueType.Number:
                    left1 = ToNumber(left1, context);
                    continue;
                case JsValueType.BigInt when rightType == JsValueType.String:
                {
                    return TryParseJsBigInt((string)right1!, out var parsed) && StrictEquals(left1, parsed);
                }
                case JsValueType.String when rightType == JsValueType.BigInt:
                {
                    return TryParseJsBigInt((string)left1!, out var parsed) && StrictEquals(parsed, right1);
                }
                case JsValueType.Boolean:
                    left1 = ToNumber(left1, context);
                    continue;
            }

            if (rightType == JsValueType.Boolean)
            {
                right1 = ToNumber(right1, context);
                continue;
            }

            switch (leftType)
            {
                case JsValueType.String or JsValueType.Number or JsValueType.BigInt or JsValueType.Symbol when rightType == JsValueType.Object:
                    right1 = ToPrimitive((IJsPropertyAccessor)right1!, ToPrimitiveHint.Default, context);
                    continue;
                case JsValueType.Object when
                    rightType is JsValueType.String or JsValueType.Number or JsValueType.BigInt or JsValueType.Symbol:
                    left1 = ToPrimitive((IJsPropertyAccessor)left1!, ToPrimitiveHint.Default, context);
                    continue;
                case JsValueType.Number when rightType == JsValueType.BigInt:
                    return NumberEqualsBigInt(left1, (JsBigInt)right1!);
                case JsValueType.BigInt when rightType == JsValueType.Number:
                    return NumberEqualsBigInt(right1, (JsBigInt)left1!);
                default:
                    return false;
            }
        }

#pragma warning restore CS0618
    }

    public static bool GreaterThan(in JsValue left, in JsValue right, EvaluationContext? context = null)
    {
#pragma warning disable CS0618 // Delegate to object? comparison for complex cases
        return left.Kind switch
        {
            // Fast path for comparing two numbers
            JsValueKind.Number when right.Kind == JsValueKind.Number => left.NumberValue > right.NumberValue,
            // Fast path for comparing two strings
            JsValueKind.String when right.Kind == JsValueKind.String => string.CompareOrdinal(
                left.ObjectValue as string, right.ObjectValue as string) > 0,
            _ => PerformComparisonOperation(ExtractValueForComparison(in left), ExtractValueForComparison(in right),
                ComparisonOperator.GreaterThan, context)
        };
#pragma warning restore CS0618
    }

    public static bool GreaterThanOrEqual(in JsValue left, in JsValue right, EvaluationContext? context = null)
    {
#pragma warning disable CS0618 // Delegate to object? comparison for complex cases
        return left.Kind switch
        {
            // Fast path for comparing two numbers
            JsValueKind.Number when right.Kind == JsValueKind.Number => left.NumberValue >= right.NumberValue,
            // Fast path for comparing two strings
            JsValueKind.String when right.Kind == JsValueKind.String => string.CompareOrdinal(
                left.ObjectValue as string, right.ObjectValue as string) >= 0,
            _ => PerformComparisonOperation(ExtractValueForComparison(in left), ExtractValueForComparison(in right),
                ComparisonOperator.GreaterThanOrEqual, context)
        };
#pragma warning restore CS0618
    }

    public static bool LessThan(in JsValue left, in JsValue right, EvaluationContext? context = null)
    {
#pragma warning disable CS0618 // Delegate to object? comparison for complex cases
        return left.Kind switch
        {
            // Fast path for comparing two numbers
            JsValueKind.Number when right.Kind == JsValueKind.Number => left.NumberValue < right.NumberValue,
            // Fast path for comparing two strings
            JsValueKind.String when right.Kind == JsValueKind.String => string.CompareOrdinal(
                left.ObjectValue as string, right.ObjectValue as string) < 0,
            _ => PerformComparisonOperation(ExtractValueForComparison(in left), ExtractValueForComparison(in right),
                ComparisonOperator.LessThan, context)
        };
#pragma warning restore CS0618
    }

    public static bool LessThanOrEqual(in JsValue left, in JsValue right, EvaluationContext? context = null)
    {
#pragma warning disable CS0618 // Delegate to object? comparison for complex cases
        return left.Kind switch
        {
            // Fast path for comparing two numbers
            JsValueKind.Number when right.Kind == JsValueKind.Number => left.NumberValue <= right.NumberValue,
            // Fast path for comparing two strings
            JsValueKind.String when right.Kind == JsValueKind.String => string.CompareOrdinal(
                left.ObjectValue as string, right.ObjectValue as string) <= 0,
            _ => PerformComparisonOperation(ExtractValueForComparison(in left), ExtractValueForComparison(in right),
                ComparisonOperator.LessThanOrEqual, context)
        };
#pragma warning restore CS0618
    }

    /// <summary>
    /// Extracts the underlying value from a JsValue for use in comparison operations.
    /// For numbers and booleans, returns a boxed value. For other types, returns ObjectValue.
    /// </summary>
    private static object? ExtractValueForComparison(in JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => Symbol.Undefined,
            JsValueKind.Null => null,
            JsValueKind.Boolean => value.NumberValue != 0,
            JsValueKind.Number => value.NumberValue,
            _ => value.ObjectValue
        };
    }

    [Obsolete("Use JsValue overload for better performance and correctness.")]
    private static bool PerformComparisonOperation(
        object? left,
        object? right,
        ComparisonOperator op,
        EvaluationContext? context)
    {
        // ES Spec 7.2.15 Abstract Relational Comparison
        // Step 1-3: Call ToPrimitive with hint "number" on both operands
        var leftPrimitive = left;
        if (left is IJsPropertyAccessor leftAccessor and not TypedAstSymbol)
        {
#pragma warning disable CS0618 // Transitional method uses object? API
            leftPrimitive = ToPrimitive(leftAccessor, ToPrimitiveHint.Number, context);
#pragma warning restore CS0618
            if (context?.IsThrow == true)
            {
                return false;
            }
        }

        var rightPrimitive = right;
        if (right is IJsPropertyAccessor rightAccessor and not TypedAstSymbol)
        {
#pragma warning disable CS0618 // Transitional method uses object? API
            rightPrimitive = ToPrimitive(rightAccessor, ToPrimitiveHint.Number, context);
#pragma warning restore CS0618
            if (context?.IsThrow == true)
            {
                return false;
            }
        }

        switch (leftPrimitive)
        {
            // Step 4: If both are strings, do string comparison
            case string leftStr when rightPrimitive is string rightStr:
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
            case string leftString when rightPrimitive is JsBigInt rightBi:
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
            case JsBigInt leftBi when rightPrimitive is string rightString:
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
        }

        // Step 5: Otherwise, convert both to numeric values and compare
#pragma warning disable CS0618 // Transitional method uses object? API
        var leftNumeric = ToNumeric(leftPrimitive, context);
#pragma warning restore CS0618
        if (context?.IsThrow == true)
        {
            return false;
        }

#pragma warning disable CS0618 // Transitional method uses object? API
        var rightNumeric = ToNumeric(rightPrimitive, context);
#pragma warning restore CS0618
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
        // Note: ToNumeric may return a boxed JsValue for numbers (not a raw double), so we need to
        // check both `double` and `JsValue` with IsNumber to correctly identify numeric values.
        var leftIsNumber = TryGetNumericDouble(leftNumeric, out var leftDouble);
        var rightIsNumber = TryGetNumericDouble(rightNumeric, out var rightDouble);

        if ((leftNumeric is JsBigInt && rightIsNumber) ||
            (leftIsNumber && rightNumeric is JsBigInt))
        {
            JsBigInt bigIntValue;
            double numberValue;
            bool bigIntIsLeft;

            if (leftNumeric is JsBigInt)
            {
                bigIntValue = (JsBigInt)leftNumeric;
                numberValue = rightDouble;
                bigIntIsLeft = true;
            }
            else
            {
                bigIntValue = (JsBigInt)rightNumeric;
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
#pragma warning disable CS0618 // Transitional method uses object? API
        var leftNum = ToNumber(leftNumeric, context);
        var rightNum = ToNumber(rightNumeric, context);
#pragma warning restore CS0618
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

    // Helper method to extract a double from a numeric value (either raw double or boxed JsValue)
    // Returns true if the value is a number, false otherwise.
    private static bool TryGetNumericDouble(object? value, out double result)
    {
        if (value is double d)
        {
            result = d;
            return true;
        }

        if (value is JsValue { IsNumber: true } jsv)
        {
            result = jsv.NumberValue;
            return true;
        }

        result = 0;
        return false;
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

    [Obsolete("Use JsValue overload for better performance and correctness.")]
    private static string? ToPropertyName(object? value, EvaluationContext? context = null)
    {
        while (true)
        {
            switch (value)
            {
                case null:
                    return "null";
                case JsValue jsValue:
                    // Handle JsValue based on kind to avoid boxing
                    switch (jsValue.Kind)
                    {
                        case JsValueKind.Null:
                            return "null";
                        case JsValueKind.Undefined:
                            return "undefined";
                        case JsValueKind.Boolean:
                            return jsValue.NumberValue != 0 ? "true" : "false";
                        case JsValueKind.Number:
                            return ToCanonicalNumberString(jsValue.NumberValue);
                        case JsValueKind.String:
                            return jsValue.ObjectValue as string ?? string.Empty;
                        case JsValueKind.BigInt:
                            return jsValue.ObjectValue is JsBigInt bi
                                ? bi.Value.ToString(CultureInfo.InvariantCulture)
                                : string.Empty;
                        case JsValueKind.Symbol:
                            if (jsValue.ObjectValue is TypedAstSymbol sym)
                            {
                                return TypedAstSymbol.PropertyKey(sym);
                            }

                            if (jsValue.ObjectValue is Symbol s)
                            {
                                return s.Name;
                            }

                            return null;
                        default:
                            value = jsValue.ObjectValue;
                            continue;
                    }
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

    public static string? ToPropertyName(JsValue value, EvaluationContext? context = null)
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
                    TypedAstSymbol astSym => TypedAstSymbol.PropertyKey(astSym),
                    _ => value.ObjectValue?.ToString() ?? string.Empty
                };
            case JsValueKind.Object:
            {
                if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
                {
#pragma warning disable CS0618 // Delegate to object? version for object conversion
                    var primitive = ToPrimitive(accessor, ToPrimitiveHint.String, context);
                    if (context?.IsThrow == true)
                    {
                        return null;
                    }

                    return ToPropertyName(primitive, context);
#pragma warning restore CS0618
                }

                if (context?.IsThrow == true)
                {
                    return null;
                }

                return Convert.ToString(value.ObjectValue, CultureInfo.InvariantCulture);
            }
            case JsValueKind.Unit:
            case JsValueKind.Uninitialized:
                return JsValueCache.UndefinedString;
            default:
                return JsValueCache.UndefinedString;
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
        var expStr = expVal >= 0 ? $"+{expVal}" : expVal.ToString(CultureInfo.InvariantCulture);

        return $"{sign}{mantissa}e{expStr}";
    }

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

    /// <summary>
    /// Checks if a JsValue represents a primitive value (not an object that needs ToPrimitive conversion).
    /// Primitives are: undefined, null, boolean, number, string, symbol, bigint.
    /// Objects wrapped as JsValue with ObjectValue being Symbol or TypedAstSymbol are also considered primitives.
    /// </summary>
    private static bool IsPrimitiveValue(JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined or JsValueKind.Null or JsValueKind.Boolean or
                JsValueKind.Number or JsValueKind.String or JsValueKind.Symbol or JsValueKind.BigInt => true,
            JsValueKind.Object => value.ObjectValue is TypedAstSymbol or Symbol,
            _ => false
        };
    }

    private static object CreateTypeError(string message, EvaluationContext? context)
    {
        var realm = context?.RealmState;
        return StandardLibrary.CreateTypeError(message, context, realm);
    }

    [Obsolete("Use JsValue overload for better performance and correctness.")]
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

    /// <summary>
    /// JsValue overload for GetPrototypePointer.
    /// </summary>
    public static IJsPropertyAccessor? GetPrototypePointer(JsValue value)
    {
        // Only objects have prototypes
        if (value.Kind != JsValueKind.Object)
        {
            return null;
        }

#pragma warning disable CS0618 // Type or member is obsolete - internal delegation
        return GetPrototypePointer(value.ObjectValue);
#pragma warning restore CS0618
    }

    [Obsolete("Use JsValue overload for better performance and correctness.")]
    public static string GetRequiredPropertyName(object? value, EvaluationContext? context = null)
    {
        var name = ToPropertyName(value, context);
        if (context?.IsThrow == true)
        {
            return string.Empty;
        }

        return name ?? throw new InvalidOperationException("Property name cannot be null.");
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

    [Obsolete("Use JsValue overload for better performance and correctness.")]
    private static bool TryResolveArrayIndex(object? candidate, out int index, EvaluationContext? context = null)
    {
        while (true)
        {
            switch (candidate)
            {
                case int i and >= 0:
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

    /// <summary>
    /// JsValue overload for GetTypeofString. Returns the typeof string for a JsValue.
    /// </summary>
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

    [Obsolete("Use JsValue overload for better performance and correctness.")]
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
#pragma warning disable CS0618 // Transitional method uses object? API
        var num = ToNumber(numberValue);
#pragma warning restore CS0618
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

    /// <summary>
    /// Implements [[HasProperty]] internal method for the 'in' operator.
    /// Returns true if the property exists on the object or its prototype chain.
    /// Per ES spec, [[HasProperty]] uses [[GetOwnProperty]] to check for existence,
    /// it does NOT invoke getters like [[Get]] would.
    /// </summary>
    public static bool HasProperty(JsValue target, string propertyName)
    {
        // Fast path: only objects can have properties via prototype chain
        if (target.Kind == JsValueKind.Object)
        {
#pragma warning disable CS0618 // Delegate to object? version for object
            return HasProperty(target.ObjectValue, propertyName);
#pragma warning restore CS0618
        }

        // Primitives boxed into objects would have gone through the object path
        // but a raw JsValue primitive doesn't have properties to check
        return false;
    }

    /// <summary>
    /// Implements [[HasProperty]] internal method for the 'in' operator.
    /// Returns true if the property exists on the object or its prototype chain.
    /// Per ES spec, [[HasProperty]] uses [[GetOwnProperty]] to check for existence,
    /// it does NOT invoke getters like [[Get]] would.
    /// </summary>
    [Obsolete("Use JsValue overload for better performance and correctness.")]
    public static bool HasProperty(object? target, string propertyName)
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

    public static bool TryGetPropertyValue(JsValue target, string propertyName, out JsValue value,
        EvaluationContext? context = null)
    {
        // Handle objects first - most common case
        if (target.Kind is JsValueKind.Object or JsValueKind.String or JsValueKind.Symbol or JsValueKind.BigInt)
        {
            var targetObj = target.ObjectValue;
#pragma warning disable CS0618 // Delegate to object? version for object
            if (targetObj != null && TryGetPropertyValue(targetObj, propertyName, out var objValue, context))
#pragma warning restore CS0618
            {
                value = objValue is JsValue jv ? jv : JsValue.FromObjectUnsafe(objValue);
                return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        // Handle primitives (Boolean, Number) - need prototype chain lookup
        if (target.Kind == JsValueKind.Boolean)
        {
            if (context?.RealmState?.BooleanPrototype is { } booleanProto &&
                booleanProto.TryGetProperty(propertyName, target.AsBoolean(), context, out var boolValue))
            {
                value = boolValue is JsValue jv ? jv : JsValue.FromObjectUnsafe(boolValue);
                return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        if (target.Kind == JsValueKind.Number)
        {
            if (context?.RealmState?.NumberPrototype is { } numberProto &&
                numberProto.TryGetProperty(propertyName, target.NumberValue, context, out var numValue))
            {
                value = numValue is JsValue jv ? jv : JsValue.FromObjectUnsafe(numValue);
                return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        value = JsValue.Undefined;
        return false;
    }

    /// <summary>
    /// JsValue overload for property access with JsValue property key.
    /// Avoids boxing when both target and key are JsValue.
    /// </summary>
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
        if (propertyName is not null && int.TryParse(propertyName, out index) && index >= 0)
        {
            return true;
        }

        index = -1;
        return false;
    }

    [Obsolete(
        "Use JsValue overload TryGetPropertyValue(JsValue, string, out JsValue, EvaluationContext?) for better performance.")]
    public static bool TryGetPropertyValue(object? target, string propertyName, out object? value,
        EvaluationContext? context = null)
    {
        // Unwrap JsValue early so property access works on wrapped callables/objects
        if (target is JsValue jsVal)
        {
            target = jsVal.Kind switch
            {
                JsValueKind.Object => jsVal.ObjectValue,
                JsValueKind.String => jsVal.ObjectValue,
                JsValueKind.Symbol => jsVal.ObjectValue,
                JsValueKind.BigInt => jsVal.ObjectValue,
                _ => null
            };
        }

        if (target is IJsPropertyAccessor propertyAccessor)
        {
            try
            {
                switch (propertyAccessor)
                {
                    case JsObject jsObject:
                        return jsObject.TryGetProperty(propertyName, target, context, out value);
                    // For Symbol primitives, first try own properties, then fall back to Symbol.prototype
                    case TypedAstSymbol symbol when symbol.TryGetProperty(propertyName, out var jsValue):
                        value = jsValue.ToObject();
                        return true;
                    // Look up in Symbol.prototype chain
                    case TypedAstSymbol symbol:
                    {
                        var symbolProto = context?.RealmState?.SymbolPrototype;
                        if (symbolProto is not null &&
                            symbolProto.TryGetProperty(propertyName, target, context, out value))
                        {
                            return true;
                        }

                        value = null;
                        return false;
                    }
                }

                if (propertyAccessor.TryGetProperty(propertyName, JsValue.FromObjectUnsafe(target), out var jsVal2))
                {
                    value = jsVal2.ToObject();
                    return true;
                }

                value = null;
                return false;
            }
            catch (ThrowSignal signal) when (context is not null)
            {
                context.SetThrow(signal.ThrownValue);
                value = signal.ThrownValue;
                return true;
            }
        }

        switch (target)
        {
            case bool b:
                if (context?.RealmState?.BooleanPrototype is { } booleanProto &&
                    booleanProto.TryGetProperty(propertyName, target, context, out value))
                {
                    return true;
                }

                // Fallback when no realm prototype is available.
                var booleanWrapper = CreateBooleanWrapper(b, context, context?.RealmState);
                if (booleanWrapper.TryGetProperty(propertyName, target, context, out value))
                {
                    return true;
                }

                break;
            case double num:
                if (context?.RealmState?.NumberPrototype is { } numberProto &&
                    numberProto.TryGetProperty(propertyName, target, context, out value))
                {
                    return true;
                }

                // Fallback when no realm prototype is available yet.
                var numberWrapper = NumberHelper.CreateNumberWrapper(num, context, context?.RealmState);
                if (numberWrapper.TryGetProperty(propertyName, target, context, out value))
                {
                    return true;
                }

                break;
            case JsBigInt bigInt:
                if (context?.RealmState?.BigIntPrototype is { } bigIntProto &&
                    bigIntProto.TryGetProperty(propertyName, target, context, out value))
                {
                    return true;
                }

                // Fallback when no realm prototype is available.
                var bigIntWrapper = BigIntHelper.CreateBigIntWrapper(bigInt, context, context?.RealmState);
                if (bigIntWrapper.TryGetProperty(propertyName, target, context, out value))
                {
                    return true;
                }

                break;
            case string str:
                if (string.Equals(propertyName, "length", StringComparison.Ordinal))
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
                    stringProto.TryGetProperty(propertyName, target, context, out value))
                {
                    return true;
                }

                // Fallback when no realm prototype is available yet.
                var stringWrapper = StringHelper.CreateStringWrapper(str, context, context?.RealmState);
                if (stringWrapper.TryGetProperty(propertyName, target, context, out value))
                {
                    return true;
                }

                break;
            case JsRopeString rope:
            {
                var ropeStr = rope.Flatten();
                if (string.Equals(propertyName, "length", StringComparison.Ordinal))
                {
                    value = (double)ropeStr.Length;
                    return true;
                }

                if (int.TryParse(propertyName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ropeIndex) &&
                    ropeIndex >= 0 && ropeIndex < ropeStr.Length)
                {
                    value = ropeStr[ropeIndex].ToString();
                    return true;
                }

                if (context?.RealmState?.StringPrototype is { } ropeStringProto &&
                    ropeStringProto.TryGetProperty(propertyName, ropeStr, context, out value))
                {
                    return true;
                }

                // Fallback when no realm prototype is available yet.
                var ropeWrapper = StringHelper.CreateStringWrapper(ropeStr, context, context?.RealmState);
                if (ropeWrapper.TryGetProperty(propertyName, ropeStr, context, out value))
                {
                    return true;
                }

                break;
            }
        }

        value = null;
        return false;
    }

    [Obsolete(
        "Use JsValue overload TryGetPropertyValue(JsValue, JsValue, out JsValue, EvaluationContext?) for better performance.")]
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
        if (TryGetArrayLikeValueJsValue(JsValue.FromObjectUnsafe(target), JsValue.FromObjectUnsafe(propertyKey), out var jsvalue, context))
        {
            value = jsvalue.ToObject();
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

    [Obsolete("Use JsValue overload for better performance and correctness.")]
    public static bool IsConstructor(object? value)
    {
        while (true)
        {
            switch (value)
            {
                case JsValue jsValue:
                    // Only objects can be constructors
                    if (jsValue.Kind != JsValueKind.Object)
                    {
                        return false;
                    }

                    value = jsValue.ObjectValue;
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


    [Obsolete(
        "Use JsValue overload AssignPropertyValueJsValue(JsValue, JsValue, JsValue, EvaluationContext?) for better performance.")]
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
        catch (ThrowSignal signal) when (context is not null)
        {
            context.SetThrow(signal.ThrownValue);
        }
    }

    [Obsolete(
        "Use JsValue overload AssignPropertyValueByNameJsValue(JsValue, string, JsValue) for better performance.")]
    private static void AssignPropertyValueByName(object? target, string propertyName, object? value)
    {
        if (target is IJsPropertyAccessor accessor)
        {
            accessor.SetProperty(propertyName, JsValue.FromObjectUnsafe(value), JsValue.FromObjectUnsafe(target));
            return;
        }

        throw new InvalidOperationException($"Cannot assign property '{propertyName}' on value '{target}'.");
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
                            [value], JsValue.FromObjectUnsafe(jsArray), context);
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

#pragma warning disable CS0618 // Need object? version for prototype chain traversal
                current = GetPrototypePointer(current);
#pragma warning restore CS0618
            }

            jsArray.SetElement(index, value);
            return true;

        }

        if (target.TryGetObject<TypedArrayBase>(out var typedArray) &&
            TryResolveArrayIndexJsValue(propertyKey, out var typedIndex, context))
        {
            if (typedIndex >= 0 && typedIndex < typedArray.Length)
            {
                typedArray.SetElement(typedIndex, ToNumber(value, context));
            }

            return true;
        }

        return false;
    }

    private static bool TryAssignArrayLikeValue(object? target, object? propertyKey, object? value,
        EvaluationContext? context)
    {
        if (target is JsArray jsArray)
        {
#pragma warning disable CS0618 // Transitional method uses object? API
            var propertyName = ToPropertyName(propertyKey, context);
#pragma warning restore CS0618
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

#pragma warning disable CS0618 // Transitional method uses object? API
            var isArrayIndex = TryResolveArrayIndex(propertyKey, out var index, context);
#pragma warning restore CS0618
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

                        TypedAstEvaluator.InvokeCallableJsValue(ownDescriptor.Set, [JsValue.FromObjectUnsafe(value)],
                            JsValue.FromObjectUnsafe(jsArray), context);
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

                            TypedAstEvaluator.InvokeCallableJsValue(inheritedDescriptor.Set,
                                [JsValue.FromObjectUnsafe(value)], JsValue.FromObjectUnsafe(jsArray), context);
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

                        jsArray.DefineProperty(propertyName,
                            new PropertyDescriptor
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

            jsArray.SetProperty(propertyName, JsValue.FromObjectUnsafe(value), JsValue.FromObjectUnsafe(jsArray));
            return true;
        }

#pragma warning disable CS0618 // Transitional method uses object? API
        if (target is TypedArrayBase typedArray && TryResolveArrayIndex(propertyKey, out var typedIndex, context))
#pragma warning restore CS0618
        {
            typedArray.SetValue(typedIndex, JsValue.FromObjectUnsafe(value));
            return true;
        }

        return false;
    }

    [Obsolete("Use JsValue overload for better performance and correctness.")]
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
}
