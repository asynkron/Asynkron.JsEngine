#region

using System.Runtime.CompilerServices;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    // Debug counters - remove after testing
    public static long PreEvalPathCount;
    public static long NormalPathCount;

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
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
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
        if (leftCall.Arguments is [{ IsSpread: false }, ..])
        {
            leftArg0 = leftCall.Arguments[0].Expression.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }
        }

        // Right call arguments
        var rightArg0 = JsValue.Undefined;
        if (rightCall.Arguments is [{ IsSpread: false }, ..])
        {
            rightArg0 = rightCall.Arguments[0].Expression.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }
        }

        // Phase 2: Resolve callees
        var leftCalleeValue = leftCall.Callee.EvaluateExpression(environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        var rightCalleeValue = rightCall.Callee.EvaluateExpression(environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        // Check if both are SyncFunctionInvokers with 1 argument (optimal case)
        if (!leftCalleeValue.TryGetObject<SyncFunctionInvoker>(out var leftFunc) ||
            !rightCalleeValue.TryGetObject<SyncFunctionInvoker>(out var rightFunc) ||
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
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        if (++context.CallDepth > context.MaxCallDepth)
        {
            throw new InvalidOperationException($"Exceeded maximum call depth of {context.MaxCallDepth}.");
        }

        var rightResult = rightFunc.InvokeWithContext1Reuse(rightArg0, JsValue.Undefined, context, environment);
        context.CallDepth--;
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        // Phase 4: Apply the binary operator
        return ApplyBinaryOperator(binary.Operator, leftResult, rightResult, context);
    }

    /// <summary>
    /// Specialized evaluator for call * identifier pattern.
    /// Pre-evaluates the identifier before the call to preserve its value.
    /// Pattern: return call(args) op identifier
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateBinaryCallIdentifier(
        BinaryExpression binary,
        CallExpression call,
        IdentifierExpression identifier,
        JsEnvironment environment,
        EvaluationContext context)
    {
        // Phase 1: Pre-evaluate the call argument and the identifier
        // This reads from the environment BEFORE the call modifies it

        var callArg0 = JsValue.Undefined;
        if (call.Arguments is [{ IsSpread: false }, ..])
        {
            callArg0 = call.Arguments[0].Expression.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }
        }

        // Pre-evaluate the identifier (the right side of the binary expression)
        var identifierValue = identifier.EvaluateIdentifier(environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        // Phase 2: Resolve callee
        var calleeValue = call.Callee.EvaluateExpression(environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        // Check if it's a SyncFunctionInvoker with 1 argument (optimal case)
        if (!calleeValue.TryGetObject<SyncFunctionInvoker>(out var func) ||
            func.IsClassConstructor ||
            call.Arguments.Length != 1 ||
            call.Arguments[0].IsSpread)
        {
            // Fall back to standard evaluation
            return EvaluateBinaryFallback(binary, environment, context);
        }

        // Phase 3: Make call with environment reuse
        if (++context.CallDepth > context.MaxCallDepth)
        {
            throw new InvalidOperationException($"Exceeded maximum call depth of {context.MaxCallDepth}.");
        }

        var callResult = func.InvokeWithContext1Reuse(callArg0, JsValue.Undefined, context, environment);
        context.CallDepth--;
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        // Phase 4: Apply the binary operator using pre-evaluated identifier value
        return ApplyBinaryOperator(binary.Operator, callResult, identifierValue, context);
    }

    /// <summary>
    /// Fallback to standard binary expression evaluation when optimized path cannot be used.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateBinaryFallback(
        BinaryExpression binary,
        JsEnvironment environment,
        EvaluationContext context)
    {
        return binary.EvaluateBinary(environment, context);
    }

    /// <summary>
    /// Applies a binary operator to two JsValue operands.
    /// For logical operators (&&, ||, ??), short-circuit evaluation must be done by the caller;
    /// this method just returns the right operand for those cases.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue ApplyBinaryOperator(BinaryOperator op, JsValue left, JsValue right,
        EvaluationContext context)
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
            BinaryOperator.In => InOperatorJsValue(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.InstanceOf => InstanceofOperatorJsValue(left, right, context) ? JsValue.True : JsValue.False,
            // Logical operators: short-circuit evaluation already done by caller, just return right
            BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr or BinaryOperator.NullishCoalescing => right,
            _ => throw new NotSupportedException($"Operator '{op}' is not supported.")
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateReturnJsValue(this ReturnStatement statement, JsEnvironment environment,
        EvaluationContext context)
    {
        JsValue jsValue;

        // Use specialized pre-evaluation path for binary expressions with multiple calls
        if (statement is
            {
                UseArgumentPreEvaluation: true,
                Expression: BinaryExpression
                {
                    Left: CallExpression leftCall, Right: CallExpression rightCall
                } binary
            })
        {
            Interlocked.Increment(ref PreEvalPathCount);
            jsValue = EvaluateBinaryWithPreEvaluation(binary, leftCall, rightCall, environment, context);
        }
        // Use specialized pre-evaluation path for call * identifier pattern
        else if (statement is
        {
            UseArgumentPreEvaluation: true,
            Expression: BinaryExpression
            {
                Left: CallExpression callLeft, Right: IdentifierExpression idRight
            } binaryCallId
        })
        {
            Interlocked.Increment(ref PreEvalPathCount);
            jsValue = EvaluateBinaryCallIdentifier(binaryCallId, callLeft, idRight, environment, context);
        }
        else
        {
            Interlocked.Increment(ref NormalPathCount);
            jsValue = statement.Expression is null
                ? JsValue.Undefined
                : statement.Expression.EvaluateExpression(environment, context);
        }

        if (context.ShouldStopEvaluation)
        {
            return jsValue;
        }

        context.SetReturn(jsValue);
        return jsValue;
    }
}
