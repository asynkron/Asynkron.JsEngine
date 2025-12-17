using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    // Debug counters - remove after testing
    public static long PreEvalPathCount;
    public static long NormalPathCount;

    extension(ReturnStatement statement)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateReturnJsValue(
            JsEnvironment environment,
            EvaluationContext context)
        {
            JsValue jsValue;

            // Use specialized pre-evaluation path for binary expressions with multiple calls
            if (statement.UseArgumentPreEvaluation &&
                statement.Expression is BinaryExpression binary &&
                binary.Left is CallExpression leftCall &&
                binary.Right is CallExpression rightCall)
            {
                Interlocked.Increment(ref PreEvalPathCount);
                jsValue = EvaluateBinaryWithPreEvaluation(binary, leftCall, rightCall, environment, context);
            }
            else
            {
                Interlocked.Increment(ref NormalPathCount);
                jsValue = statement.Expression is null
                    ? JsValue.Undefined
                    : EvaluateExpression(statement.Expression, environment, context);
            }

            if (context.ShouldStopEvaluation)
            {
                return jsValue;
            }

            context.SetReturn(jsValue);
            return jsValue;
        }
    }

    /// <summary>
    /// Specialized evaluator for binary expressions with pre-evaluated arguments.
    /// This enables environment reuse for ALL calls, not just the rightmost one.
    /// Pattern: return call1(args1) op call2(args2)
    /// </summary>
    /// <remarks>
    /// For expressions like `return fib(n-1) + fib(n-2)`:
    /// 1. Pre-evaluate ALL arguments BEFORE making any calls (n-1=4, n-2=3)
    /// 2. Now the environment can be safely reused because no more reads are needed
    /// 3. Make call1 with environment reuse, get result
    /// 4. Make call2 with environment reuse, get result
    /// 5. Apply the binary operator
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue EvaluateBinaryWithPreEvaluation(
        BinaryExpression binary,
        CallExpression leftCall,
        CallExpression rightCall,
        JsEnvironment environment,
        EvaluationContext context)
    {
        // Phase 1: Pre-evaluate all arguments for both calls
        // This reads from the environment BEFORE any calls modify it

        // Left call arguments
        var leftArg0 = JsValue.Undefined;
        if (leftCall.Arguments.Length >= 1 && !leftCall.Arguments[0].IsSpread)
        {
            leftArg0 = EvaluateExpression(leftCall.Arguments[0].Expression, environment, context);
            if (context.ShouldStopEvaluation) return JsValue.Undefined;
        }

        // Right call arguments
        var rightArg0 = JsValue.Undefined;
        if (rightCall.Arguments.Length >= 1 && !rightCall.Arguments[0].IsSpread)
        {
            rightArg0 = EvaluateExpression(rightCall.Arguments[0].Expression, environment, context);
            if (context.ShouldStopEvaluation) return JsValue.Undefined;
        }

        // Phase 2: Resolve callees
        var leftCalleeValue = EvaluateExpression(leftCall.Callee, environment, context);
        if (context.ShouldStopEvaluation) return JsValue.Undefined;

        var rightCalleeValue = EvaluateExpression(rightCall.Callee, environment, context);
        if (context.ShouldStopEvaluation) return JsValue.Undefined;

        // Check if both are TypedFunctions with 1 argument (optimal case)
        if (!leftCalleeValue.TryGetObject<TypedFunction>(out var leftFunc) ||
            !rightCalleeValue.TryGetObject<TypedFunction>(out var rightFunc) ||
            leftFunc.IsClassConstructor || rightFunc.IsClassConstructor ||
            leftCall.Arguments.Length != 1 || rightCall.Arguments.Length != 1 ||
            leftCall.Arguments[0].IsSpread || rightCall.Arguments[0].IsSpread)
        {
            // Fall back to standard evaluation (the arguments have already been evaluated,
            // but we can't use the optimized path)
            return EvaluateBinaryFallback(binary, environment, context);
        }

        // Phase 3: Make calls with environment reuse
        // Now all arguments are in C# locals, so the environment can be safely reused

        if (++context.CallDepth > context.MaxCallDepth)
        {
            throw new InvalidOperationException($"Exceeded maximum call depth of {context.MaxCallDepth}.");
        }

        var leftResult = leftFunc.InvokeWithContext1Reuse(leftArg0, JsValue.Undefined, context, environment);
        context.CallDepth--;
        if (context.ShouldStopEvaluation) return JsValue.Undefined;

        if (++context.CallDepth > context.MaxCallDepth)
        {
            throw new InvalidOperationException($"Exceeded maximum call depth of {context.MaxCallDepth}.");
        }

        var rightResult = rightFunc.InvokeWithContext1Reuse(rightArg0, JsValue.Undefined, context, environment);
        context.CallDepth--;
        if (context.ShouldStopEvaluation) return JsValue.Undefined;

        // Phase 4: Apply the binary operator
        return ApplyBinaryOperator(binary.Operator, leftResult, rightResult, context);
    }

    /// <summary>
    /// Fallback to standard binary expression evaluation when optimized path cannot be used.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue EvaluateBinaryFallback(
        BinaryExpression binary,
        JsEnvironment environment,
        EvaluationContext context)
    {
        return binary.EvaluateBinary(environment, context);
    }

    /// <summary>
    /// Applies a binary operator to two JsValue operands.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue ApplyBinaryOperator(BinaryOperator op, JsValue left, JsValue right, EvaluationContext context)
    {
        return op switch
        {
            BinaryOperator.Add => AddValue(left, right, context),
            BinaryOperator.Subtract => SubtractValue(left, right, context),
            BinaryOperator.Multiply => MultiplyValue(left, right, context),
            BinaryOperator.Divide => DivideValue(left, right, context),
            BinaryOperator.Modulo => ModuloValue(left, right, context),
            BinaryOperator.Power => PowerValue(left, right, context),
            BinaryOperator.LessThan => LessThanValue(left, right, context),
            BinaryOperator.LessThanOrEqual => LessThanOrEqualValue(left, right, context),
            BinaryOperator.GreaterThan => GreaterThanValue(left, right, context),
            BinaryOperator.GreaterThanOrEqual => GreaterThanOrEqualValue(left, right, context),
            BinaryOperator.Equal => LooseEqualsValue(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.NotEqual => LooseEqualsValue(left, right, context) ? JsValue.False : JsValue.True,
            BinaryOperator.StrictEqual => StrictEqualsValue(left, right) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictNotEqual => StrictEqualsValue(left, right) ? JsValue.False : JsValue.True,
            BinaryOperator.BitwiseAnd => BitwiseAndValue(left, right, context),
            BinaryOperator.BitwiseOr => BitwiseOrValue(left, right, context),
            BinaryOperator.BitwiseXor => BitwiseXorValue(left, right, context),
            BinaryOperator.LeftShift => LeftShiftValue(left, right, context),
            BinaryOperator.RightShift => RightShiftValue(left, right, context),
            BinaryOperator.UnsignedRightShift => UnsignedRightShiftValue(left, right, context),
            _ => throw new NotSupportedException($"Operator '{op}' is not supported in pre-evaluated path.")
        };
    }
}
