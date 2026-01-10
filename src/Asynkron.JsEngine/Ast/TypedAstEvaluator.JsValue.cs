#region

using System.Numerics;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// JsValue-based arithmetic operations for hot paths.
/// These avoid boxing for primitive operations.
/// </summary>
public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Adds two JsValue operands without boxing intermediate results.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue AddValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both operands are already numbers (very common in loops)
        if (left.IsNumber && right.IsNumber)
        {
            return JsValue.FromDouble(left.NumberValue + right.NumberValue);
        }

        // Need ToPrimitive for objects FIRST per ES spec 13.15.3
        // (ToPrimitive must be called before checking for strings)
        if (left.IsObject || right.IsObject)
        {
            return AddWithToPrimitive(left, right, context);
        }

        // Fast path: string concatenation (both are primitives here)
        if (left.IsString || right.IsString)
        {
            return AddStringValue(left, right, context);
        }

        // Handle BigInt
        if (left.IsBigInt && right.IsBigInt)
        {
            return new JsValue(left.AsBigInt() + right.AsBigInt());
        }

        if (left.IsBigInt || right.IsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        // Convert to numeric and add
        var leftNum = ToNumberValue(left, context);
        var rightNum = ToNumberValue(right, context);
        return JsValue.FromDouble(leftNum + rightNum);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue AddStringValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        if (left.IsSymbol || right.IsSymbol)
        {
            throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a string", context);
        }

        // Get underlying string objects (could be string or JsRopeString)
        // Avoid flattening ropes - use rope concatenation for O(1) concat
        var leftObj = left.IsString ? left.ObjectValue! : ToStringValue(left, context);
        var rightObj = right.IsString ? right.ObjectValue! : ToStringValue(right, context);

        // JsRopeString.Concat handles both string and JsRopeString inputs
        // Returns string for small concatenations, JsRopeString for larger ones
        var result = JsRopeString.Concat(leftObj, rightObj);
        return new JsValue(JsValueKind.String, 0.0, result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue AddWithToPrimitive(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fall back to object? path for ToPrimitive
        var leftPrim = JsOps.ToPrimitive(left, ToPrimitiveHint.Default, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        var rightPrim = JsOps.ToPrimitive(right, ToPrimitiveHint.Default, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        // Convert results back to JsValue
        var leftVal = leftPrim;
        var rightVal = rightPrim;

        // Per spec 12.8.3:
        // 7. If Type(lprim) is String or Type(rprim) is String, then
        //    a. Let lstr be ? ToString(lprim).
        //    b. Let rstr be ? ToString(rprim).
        //    c. Return the concatenation of lstr and rstr.
        if (leftVal.IsString || rightVal.IsString)
        {
            // Use rope concatenation for efficiency
            var leftObj = leftVal.IsString ? leftVal.ObjectValue! : ToStringValue(leftVal, context);
            var rightObj = rightVal.IsString ? rightVal.ObjectValue! : ToStringValue(rightVal, context);
            var result = JsRopeString.Concat(leftObj, rightObj);
            return new JsValue(JsValueKind.String, 0.0, result);
        }

        // 8. Let lnum be ? ToNumeric(lprim).
        // 9. Let rnum be ? ToNumeric(rprim).
        // 10. If Type(lnum) is different from Type(rnum), throw a TypeError exception.
        // 11. Let T be Type(lnum).
        // 12. Return T::add(lnum, rnum).

        // Handle BigInt
        if (leftVal.IsBigInt && rightVal.IsBigInt)
        {
            return new JsValue(leftVal.AsBigInt() + rightVal.AsBigInt());
        }

        if (leftVal.IsBigInt || rightVal.IsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        // Convert both to numbers - this will throw for Symbols
        var leftNum = ToNumberValue(leftVal, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        var rightNum = ToNumberValue(rightVal, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return JsValue.FromDouble(leftNum + rightNum);
    }

    /// <summary>
    /// Subtracts two JsValue operands.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue SubtractValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers
        if (left.IsNumber && right.IsNumber)
        {
            return JsValue.FromDouble(left.NumberValue - right.NumberValue);
        }

        return NumericBinaryOp(left, right, (l, r) => l - r, (l, r) => l - r, context);
    }

    /// <summary>
    /// Multiplies two JsValue operands.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue MultiplyValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers
        if (left.IsNumber && right.IsNumber)
        {
            return JsValue.FromDouble(left.NumberValue * right.NumberValue);
        }

        return NumericBinaryOp(left, right, (l, r) => l * r, (l, r) => l * r, context);
    }

    /// <summary>
    /// Divides two JsValue operands.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue DivideValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers
        if (left.IsNumber && right.IsNumber)
        {
            return JsValue.FromDouble(left.NumberValue / right.NumberValue);
        }

        return NumericBinaryOp(left, right,
            (l, r) => l / r,
            (l, r) =>
            {
                if (r.Value.IsZero)
                {
                    throw StandardLibrary.ThrowRangeError("Division by zero", context);
                }

                return l / r;
            },
            context);
    }

    /// <summary>
    /// Computes modulo of two JsValue operands.
    /// Uses JsOps.MathMod for proper negative zero handling.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue ModuloValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers - use MathMod for proper ES spec compliance
        if (left.IsNumber && right.IsNumber)
        {
            return JsValue.FromDouble(JsOps.MathMod(left.NumberValue, right.NumberValue));
        }

        return NumericBinaryOp(left, right,
            JsOps.MathMod,
            (l, r) =>
            {
                if (r.Value.IsZero)
                {
                    throw StandardLibrary.ThrowRangeError("Division by zero", context);
                }

                return l % r;
            },
            context);
    }

    /// <summary>
    /// Generic numeric binary operation for JsValue.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue NumericBinaryOp(
        in JsValue left,
        in JsValue right,
        Func<double, double, double> numericOp,
        Func<JsBigInt, JsBigInt, JsBigInt> bigIntOp,
        EvaluationContext context)
    {
        // Convert to numeric
        var leftNumeric = ToNumericValue(left, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        var rightNumeric = ToNumericValue(right, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        // Both numbers
        if (leftNumeric.IsNumber && rightNumeric.IsNumber)
        {
            return JsValue.FromDouble(numericOp(leftNumeric.NumberValue, rightNumeric.NumberValue));
        }

        // Both BigInt
        if (leftNumeric.IsBigInt && rightNumeric.IsBigInt)
        {
            return new JsValue(bigIntOp(leftNumeric.AsBigInt(), rightNumeric.AsBigInt()));
        }

        // Mixed types
        if (leftNumeric.IsBigInt || rightNumeric.IsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        return JsValue.FromDouble(numericOp(leftNumeric.NumberValue, rightNumeric.NumberValue));
    }

    /// <summary>
    /// Converts a JsValue to a numeric JsValue (Number or BigInt).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue ToNumericValue(in JsValue value, EvaluationContext context)
    {
        // Fast path for already-numeric values
        if (value.IsNumber || value.IsBigInt)
        {
            return value;
        }

        return value.Kind switch
        {
            JsValueKind.Undefined => JsValue.NaN,
            JsValueKind.Null => JsValue.Zero,
            JsValueKind.Boolean => value.AsBoolean() ? JsValue.One : JsValue.Zero,
            JsValueKind.String => new JsValue(JsOps.ToNumber(value.AsString(), context)),
            JsValueKind.Symbol => throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a number",
                context),
            JsValueKind.Object => ToNumericValueFromObject(value, context),
            _ => JsValue.NaN
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue ToNumericValueFromObject(JsValue value, EvaluationContext context)
    {
        var primitive = JsOps.ToPrimitive(value, ToPrimitiveHint.Number, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        // Guard against infinite recursion if ToPrimitive returns an object
        // (broken valueOf/toString that returns 'this')
        if (primitive.Kind == JsValueKind.Object)
        {
            throw StandardLibrary.ThrowTypeError(
                "Cannot convert object to primitive value (valueOf/toString returned non-primitive)",
                context);
        }

        return ToNumericValue(primitive, context);
    }

    /// <summary>
    /// Converts a JsValue to a double (ToNumber).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static double ToNumberValue(in JsValue value, EvaluationContext context)
    {
        if (value.IsNumber)
        {
            return value.NumberValue;
        }

        return value.Kind switch
        {
            JsValueKind.Undefined => double.NaN,
            JsValueKind.Null => 0.0,
            JsValueKind.Boolean => value.AsBoolean() ? 1.0 : 0.0,
            JsValueKind.String => JsOps.ToNumber(value.AsString(), context),
            JsValueKind.BigInt => throw StandardLibrary.ThrowTypeError("Cannot convert a BigInt value to a number",
                context),
            JsValueKind.Symbol => throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a number",
                context),
            JsValueKind.Object => JsOps.ToNumber(value, context),
            _ => double.NaN
        };
    }

    /// <summary>
    /// Converts a JsValue to a string (ToString).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static string ToStringValue(in JsValue value, EvaluationContext context)
    {
        if (value.IsString)
        {
            return value.AsString();
        }

        return value.Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "null",
            JsValueKind.Boolean => value.AsBoolean() ? "true" : "false",
            JsValueKind.Number => JsOps.ToJsString(value.NumberValue, context),
            JsValueKind.BigInt => value.AsBigInt().ToString(),
            JsValueKind.Symbol => throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a string",
                context),
            JsValueKind.Object => JsOps.ToJsString(value, context),
            _ => "undefined"
        };
    }

    /// <summary>
    /// Increments a JsValue by 1.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue IncrementValue(in JsValue value, EvaluationContext context)
    {
        if (value.IsNumber)
        {
            return JsValue.FromDouble(value.NumberValue + 1.0);
        }

        if (value.IsBigInt)
        {
            return new JsValue(new JsBigInt(value.AsBigInt().Value + BigInteger.One));
        }

        // Convert to numeric first
        var numeric = ToNumericValue(value, context);
        if (numeric.IsNumber)
        {
            return JsValue.FromDouble(numeric.NumberValue + 1.0);
        }

        if (numeric.IsBigInt)
        {
            return new JsValue(new JsBigInt(numeric.AsBigInt().Value + BigInteger.One));
        }

        return JsValue.NaN;
    }

    /// <summary>
    /// Decrements a JsValue by 1.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue DecrementValue(in JsValue value, EvaluationContext context)
    {
        if (value.IsNumber)
        {
            return JsValue.FromDouble(value.NumberValue - 1.0);
        }

        if (value.IsBigInt)
        {
            return new JsValue(new JsBigInt(value.AsBigInt().Value - BigInteger.One));
        }

        // Convert to numeric first
        var numeric = ToNumericValue(value, context);
        if (numeric.IsNumber)
        {
            return JsValue.FromDouble(numeric.NumberValue - 1.0);
        }

        if (numeric.IsBigInt)
        {
            return new JsValue(new JsBigInt(numeric.AsBigInt().Value - BigInteger.One));
        }

        return JsValue.NaN;
    }

    /// <summary>
    /// Negates a JsValue (unary -).
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue NegateValue(in JsValue value, EvaluationContext context)
    {
        if (value.IsNumber)
        {
            return JsValue.FromDouble(-value.NumberValue);
        }

        if (value.IsBigInt)
        {
            return new JsValue(-value.AsBigInt());
        }

        var numeric = ToNumericValue(value, context);
        if (numeric.IsNumber)
        {
            return JsValue.FromDouble(-numeric.NumberValue);
        }

        if (numeric.IsBigInt)
        {
            return new JsValue(-numeric.AsBigInt());
        }

        return JsValue.NaN;
    }

    /// <summary>
    /// Compares two JsValue operands for less-than.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue LessThanValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers
        if (left.IsNumber && right.IsNumber)
        {
            return left.NumberValue < right.NumberValue ? JsValue.True : JsValue.False;
        }

        // Use existing comparison logic
        return JsOps.LessThan(left, right, context) ? JsValue.True : JsValue.False;
    }

    /// <summary>
    /// Compares two JsValue operands for less-than-or-equal.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue LessThanOrEqualValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers
        if (left.IsNumber && right.IsNumber)
        {
            return left.NumberValue <= right.NumberValue ? JsValue.True : JsValue.False;
        }

        // Use existing comparison logic
        return JsOps.LessThanOrEqual(left, right, context) ? JsValue.True : JsValue.False;
    }

    /// <summary>
    /// Compares two JsValue operands for greater-than.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue GreaterThanValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers
        if (left.IsNumber && right.IsNumber)
        {
            return left.NumberValue > right.NumberValue ? JsValue.True : JsValue.False;
        }

        // Use direct comparison (not inversion) to handle NaN correctly
        return JsOps.GreaterThan(left, right, context) ? JsValue.True : JsValue.False;
    }

    /// <summary>
    /// Compares two JsValue operands for greater-than-or-equal.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue GreaterThanOrEqualValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers
        if (left.IsNumber && right.IsNumber)
        {
            return left.NumberValue >= right.NumberValue ? JsValue.True : JsValue.False;
        }

        // Use direct comparison (not inversion) to handle NaN correctly
        return JsOps.GreaterThanOrEqual(left, right, context) ? JsValue.True : JsValue.False;
    }

    /// <summary>
    /// Computes power of two JsValue operands.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue PowerValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers - use JsOps.MathPow for ES spec compliance
        // (handles special cases like abs(base)==1 with ±Infinity exponent)
        if (left.IsNumber && right.IsNumber)
        {
            return JsValue.FromDouble(JsOps.MathPow(left.NumberValue, right.NumberValue));
        }

        // Use JsValue overload directly - avoids boxing
        return PerformBigIntOrNumericOperationJsValue(left, right,
            (l, r, ctx) =>
            {
                try
                {
                    return JsBigInt.Pow(l, r);
                }
                catch (InvalidOperationException ex)
                {
                    throw StandardLibrary.ThrowRangeError(ex.Message, ctx);
                }
            },
            JsOps.MathPow,
            context);
    }

    /// <summary>
    /// Loose equality comparison for JsValue.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static bool LooseEqualsValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: same types
        if (left.Kind == right.Kind)
        {
            return StrictEqualsValue(left, right);
        }

        // Null == Undefined
        if ((left.IsNull && right.IsUndefined) || (left.IsUndefined && right.IsNull))
        {
            return true;
        }

        // Use JsOps.LooseEquals JsValue overload directly - avoids boxing
        return JsOps.LooseEquals(left, right, context);
    }

    /// <summary>
    /// Strict equality comparison for JsValue.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool StrictEqualsValue(in JsValue left, in JsValue right)
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
            JsValueKind.Boolean => left.AsBoolean() == right.AsBoolean(),
            JsValueKind.Number => left.NumberValue == right.NumberValue,
            JsValueKind.String => string.Equals(left.AsString(), right.AsString(), StringComparison.Ordinal),
            JsValueKind.BigInt => left.AsBigInt().Value == right.AsBigInt().Value,
            JsValueKind.Symbol => ReferenceEquals(left.ObjectValue, right.ObjectValue),
            JsValueKind.Object => ReferenceEquals(left.ObjectValue, right.ObjectValue),
            _ => false
        };
    }

    /// <summary>
    /// Bitwise AND of two JsValue operands.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue BitwiseAndValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers
        if (left.IsNumber && right.IsNumber)
        {
            return JsValue.FromDouble(JsNumericConversions.ToInt32(left.NumberValue) &
                                      JsNumericConversions.ToInt32(right.NumberValue));
        }

        // Use JsValue overload directly - avoids boxing
        return PerformBigIntOrInt32OperationJsValue(left, right,
            (l, r) => l & r,
            (l, r) => l & r,
            context);
    }

    /// <summary>
    /// Bitwise OR of two JsValue operands.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue BitwiseOrValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers
        if (left.IsNumber && right.IsNumber)
        {
            return JsValue.FromDouble(JsNumericConversions.ToInt32(left.NumberValue) |
                                      JsNumericConversions.ToInt32(right.NumberValue));
        }

        // Use JsValue overload directly - avoids boxing
        return PerformBigIntOrInt32OperationJsValue(left, right,
            (l, r) => l | r,
            (l, r) => l | r,
            context);
    }

    /// <summary>
    /// Bitwise XOR of two JsValue operands.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue BitwiseXorValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers
        if (left.IsNumber && right.IsNumber)
        {
            return JsValue.FromDouble(JsNumericConversions.ToInt32(left.NumberValue) ^
                                      JsNumericConversions.ToInt32(right.NumberValue));
        }

        // Use JsValue overload directly - avoids boxing
        return PerformBigIntOrInt32OperationJsValue(left, right,
            (l, r) => l ^ r,
            (l, r) => l ^ r,
            context);
    }

    /// <summary>
    /// Left shift of two JsValue operands.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue LeftShiftValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers
        if (left.IsNumber && right.IsNumber)
        {
            return JsValue.FromDouble(JsNumericConversions.ToInt32(left.NumberValue) <<
                                      (JsNumericConversions.ToInt32(right.NumberValue) & 0x1F));
        }

        // Use JsValue overload directly - avoids boxing
        return LeftShiftJsValue(left, right, context);
    }

    /// <summary>
    /// Right shift of two JsValue operands.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue RightShiftValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers
        if (left.IsNumber && right.IsNumber)
        {
            return JsValue.FromDouble(JsNumericConversions.ToInt32(left.NumberValue) >>
                                      (JsNumericConversions.ToInt32(right.NumberValue) & 0x1F));
        }

        // Use JsValue overload directly - avoids boxing
        return RightShiftJsValue(left, right, context);
    }

    /// <summary>
    /// Unsigned right shift of two JsValue operands.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue UnsignedRightShiftValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path: both numbers
        if (left.IsNumber && right.IsNumber)
        {
            return JsValue.FromDouble(JsNumericConversions.ToUInt32(left.NumberValue) >>
                                      (JsNumericConversions.ToInt32(right.NumberValue) & 0x1F));
        }

        // Use JsValue overload directly - avoids boxing
        return UnsignedRightShiftJsValue(left, right, context);
    }

    #region Public API for JsOps

    /// <summary>
    /// Public wrapper for Add operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue Add(in JsValue a, in JsValue b, EvaluationContext ctx)
    {
        return AddValue(a, b, ctx);
    }

    /// <summary>
    /// Public wrapper for Subtract operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue Subtract(in JsValue a, in JsValue b, EvaluationContext ctx)
    {
        return SubtractValue(a, b, ctx);
    }

    /// <summary>
    /// Public wrapper for Multiply operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue Multiply(in JsValue a, in JsValue b, EvaluationContext ctx)
    {
        return MultiplyValue(a, b, ctx);
    }

    /// <summary>
    /// Public wrapper for Divide operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue Divide(in JsValue a, in JsValue b, EvaluationContext ctx)
    {
        return DivideValue(a, b, ctx);
    }

    /// <summary>
    /// Public wrapper for Modulo operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue Modulo(in JsValue a, in JsValue b, EvaluationContext ctx)
    {
        return ModuloValue(a, b, ctx);
    }

    /// <summary>
    /// Public wrapper for Power operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue Power(in JsValue a, in JsValue b, EvaluationContext ctx)
    {
        return PowerValue(a, b, ctx);
    }

    /// <summary>
    /// Public wrapper for Negate operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue Negate(in JsValue a, EvaluationContext ctx)
    {
        return NegateValue(a, ctx);
    }

    /// <summary>
    /// Public wrapper for BitwiseAnd operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue BitwiseAnd(in JsValue a, in JsValue b, EvaluationContext ctx)
    {
        return BitwiseAndValue(a, b, ctx);
    }

    /// <summary>
    /// Public wrapper for BitwiseOr operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue BitwiseOr(in JsValue a, in JsValue b, EvaluationContext ctx)
    {
        return BitwiseOrValue(a, b, ctx);
    }

    /// <summary>
    /// Public wrapper for BitwiseXor operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue BitwiseXor(in JsValue a, in JsValue b, EvaluationContext ctx)
    {
        return BitwiseXorValue(a, b, ctx);
    }

    /// <summary>
    /// Public wrapper for BitwiseNot operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue BitwiseNot(in JsValue a, EvaluationContext ctx)
    {
        return BitwiseNotJsValue(a, ctx);
    }

    /// <summary>
    /// Public wrapper for LeftShift operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue LeftShift(in JsValue a, in JsValue b, EvaluationContext ctx)
    {
        return LeftShiftValue(a, b, ctx);
    }

    /// <summary>
    /// Public wrapper for RightShift operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue RightShift(in JsValue a, in JsValue b, EvaluationContext ctx)
    {
        return RightShiftValue(a, b, ctx);
    }

    /// <summary>
    /// Public wrapper for UnsignedRightShift operation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue UnsignedRightShift(in JsValue a, in JsValue b, EvaluationContext ctx)
    {
        return UnsignedRightShiftValue(a, b, ctx);
    }

    #endregion
}
