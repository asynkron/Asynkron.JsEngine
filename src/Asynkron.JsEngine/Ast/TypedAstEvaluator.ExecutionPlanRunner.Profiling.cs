#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        // ═══════════════════════════════════════════════════════════════════════════
        // PROFILING DIAGNOSTICS: NoInlining methods to isolate hot path costs
        // These show up separately in profiler output for analysis
        // Change to AggressiveInlining after profiling is complete
        // ═══════════════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ProfileReadOperand(
            JsEnvironment environment,
            EvaluationContext context,
            ExpressionNode expr,
            out JsValue value)
        {
            if (expr is LiteralExpression lit)
            {
                value = lit.Value;
                return true;
            }

            if (expr is IdentifierExpression id && id.SlotIndex >= 0 && id.ScopeId >= 0)
            {
                return environment.TryReadIdentifierWithSlot(id, context, out value);
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static JsValue ProfileBranchCompare(
            BinaryOperator op,
            JsValue leftVal,
            JsValue rightVal,
            EvaluationContext context)
        {
            return op switch
            {
                BinaryOperator.LessThan => LessThanValue(leftVal, rightVal, context),
                BinaryOperator.LessThanOrEqual => LessThanOrEqualValue(leftVal, rightVal, context),
                BinaryOperator.GreaterThan => GreaterThanValue(leftVal, rightVal, context),
                _ => GreaterThanOrEqualValue(leftVal, rightVal, context)
            };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ProfileHandleJump(JumpInstruction jumpInstruction)
        {
            return jumpInstruction.TargetIndex;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static JsValue ProfileEvaluateExpression(
            ExpressionNode expression,
            JsEnvironment environment,
            EvaluationContext context)
        {
            return expression.EvaluateExpression(environment, context);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static JsValue ProfileEvaluateStatement(
            StatementNode statement,
            JsEnvironment environment,
            EvaluationContext context)
        {
            return statement.EvaluateStatementJsValue(environment, context);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static JsValue ProfileApplyBinaryOperator(
            BinaryOperator op,
            JsValue left,
            JsValue right,
            EvaluationContext context)
        {
            return ApplyBinaryOperator(op, left, right, context);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static JsValue ProfileGetIdentifier(
            JsEnvironment environment,
            Symbol symbol,
            EvaluationContext context)
        {
            return environment.GetIdentifierJsValueDirect(symbol, context);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ProfileAssignJsValue(
            JsEnvironment environment,
            Symbol symbol,
            JsValue value)
        {
            environment.AssignJsValue(symbol, value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ExecutionInstruction ProfileFetchInstruction(
            ref ExecutionInstruction instructionsRef,
            int programCounter)
        {
            return Unsafe.Add(ref instructionsRef, programCounter);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ProfileBranchDecision(bool isTruthy, int consequent, int alternate)
        {
            return isTruthy ? consequent : alternate;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static JsValue ProfileIncrementMath(JsValue currentValue, bool isIncrement)
        {
            // Fast path for numbers (most common case)
            if (currentValue.Kind == JsValueKind.Number)
            {
                var numValue = currentValue.NumberValue;
                var newValue = isIncrement ? numValue + 1.0 : numValue - 1.0;
                return JsValueCache.GetNumberJsValue(newValue);
            }
            // BigInt and other cases - return sentinel to indicate slow path needed
            return JsValue.Undefined;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static JsValue ProfileCompoundAdd(JsValue left, JsValue right)
        {
            // Fast path for number + number (most common in loops)
            if (left.Kind == JsValueKind.Number && right.Kind == JsValueKind.Number)
            {
                return JsValueCache.GetNumberJsValue(left.NumberValue + right.NumberValue);
            }
            // Return sentinel to indicate slow path needed
            return JsValue.Undefined;
        }
    }
}
