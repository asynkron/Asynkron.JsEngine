#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;

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

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool ProfileReadOperand(
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

            // Fast path: use flat slot for O(1) identifier read
            if (expr is IdentifierExpression { FlatSlotId: >= 0 } id && _flatSlots is not null)
            {
                value = _flatSlots[id.FlatSlotId].Read();
                return true;
            }

            // Fallback: slot-based read
            if (expr is IdentifierExpression { SlotIndex: >= 0, ScopeId: >= 0 } slotId)
            {
                return environment.TryReadIdentifierWithSlot(slotId, context, out value);
            }

            value = default;
            return false;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
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

        [MethodImpl(JsEngineConstants.Inlining)]
        private static int ProfileHandleJump(JumpInstruction jumpInstruction)
        {
            return jumpInstruction.TargetIndex;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static JsValue ProfileApplyBinaryOperator(
                    BinaryOperator op,
                    JsValue left,
                    JsValue right,
                    EvaluationContext context)
        {
            return ApplyBinaryOperator(op, left, right, context);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static JsValue ProfileGetIdentifier(
                    JsEnvironment environment,
                    Symbol symbol,
                    EvaluationContext context)
        {
            return context.AllowIdentifierCache
                ? environment.GetIdentifierJsValueDirect(symbol, context)
                : environment.GetIdentifierJsValueWithScope(symbol, context);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static void ProfileAssignJsValue(
                    JsEnvironment environment,
                    Symbol symbol,
                    JsValue value,
                    EvaluationContext context)
        {
            if (context.AllowIdentifierCache)
            {
                environment.AssignJsValue(symbol, value);
                return;
            }

            environment.SetIdentifierJsValue(symbol, value, context);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static ExecutionInstruction ProfileFetchInstruction(
                    ref ExecutionInstruction instructionsRef,
                    int programCounter)
        {
            return Unsafe.Add(ref instructionsRef, programCounter);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static int ProfileBranchDecision(bool isTruthy, int consequent, int alternate)
        {
            return isTruthy ? consequent : alternate;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static JsValue ProfileIncrementMath(JsValue currentValue, bool isIncrement)
        {
            // Fast path for numbers (most common case)
            if (currentValue.Kind == JsValueKind.Number)
            {
                var numValue = currentValue.NumberValue;
                return isIncrement ? numValue + 1.0 : numValue - 1.0;
            }
            // BigInt and other cases - return sentinel to indicate slow path needed
            return JsValue.Undefined;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static JsValue ProfileCompoundAdd(JsValue left, JsValue right)
        {
            // Fast path for number + number (most common in loops)
            if (left.Kind == JsValueKind.Number && right.Kind == JsValueKind.Number)
            {
                return left.NumberValue + right.NumberValue;
            }
            // Return sentinel to indicate slow path needed
            return JsValue.Undefined;
        }
    }
}
