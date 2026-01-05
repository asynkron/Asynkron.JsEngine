#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        /// <summary>
        /// Delegate type for instruction handlers.
        /// Returns true to continue the loop, false to return _handlerReturnValue from ExecutePlan.
        /// </summary>
        private delegate bool InstructionHandler(
            ExecutionPlanRunner runner,
            ExecutionInstruction instruction,
            ref JsEnvironment environment,
            EvaluationContext context);

        /// <summary>
        /// Return value set by handlers when they return false.
        /// </summary>
        private JsValue _handlerReturnValue;

        /// <summary>
        /// Handler lookup table indexed by InstructionKind.
        /// Eliminates switch dispatch overhead by using direct array indexing.
        /// </summary>
        private static readonly InstructionHandler[] s_handlers = BuildHandlerTable();

        private static InstructionHandler[] BuildHandlerTable()
        {
            // InstructionKind is a byte enum with max value ~32
            var handlers = new InstructionHandler[64]; // Extra space for safety

            handlers[(int)InstructionKind.Jump] = HandleJump;
            handlers[(int)InstructionKind.Branch] = HandleBranch;
            handlers[(int)InstructionKind.Statement] = HandleStatement;
            handlers[(int)InstructionKind.Throw] = HandleThrow;
            handlers[(int)InstructionKind.EvaluateAndDiscard] = HandleEvaluateAndDiscard;
            handlers[(int)InstructionKind.BinaryOp] = HandleBinaryOp;
            handlers[(int)InstructionKind.IncrementSlot] = HandleIncrementSlot;
            handlers[(int)InstructionKind.FunctionDeclaration] = HandleFunctionDeclaration;
            handlers[(int)InstructionKind.ClassDeclaration] = HandleClassDeclaration;
            handlers[(int)InstructionKind.SimpleVariableDeclaration] = HandleSimpleVariableDeclaration;
            handlers[(int)InstructionKind.PushEnvironment] = HandlePushEnvironment;
            handlers[(int)InstructionKind.PopEnvironment] = HandlePopEnvironment;
            handlers[(int)InstructionKind.Yield] = HandleYield;
            handlers[(int)InstructionKind.YieldStar] = HandleYieldStar;
            handlers[(int)InstructionKind.StoreResumeValue] = HandleStoreResumeValue;
            handlers[(int)InstructionKind.EnterTry] = HandleEnterTry;
            handlers[(int)InstructionKind.EnterCatch] = HandleEnterCatch;
            handlers[(int)InstructionKind.EnterCatchWithDestructuring] = HandleEnterCatchWithDestructuring;
            handlers[(int)InstructionKind.LeaveTry] = HandleLeaveTry;
            handlers[(int)InstructionKind.BreakableEnter] = HandleBreakableEnter;
            handlers[(int)InstructionKind.BreakableExit] = HandleBreakableExit;
            handlers[(int)InstructionKind.EndFinally] = HandleEndFinally;
            handlers[(int)InstructionKind.IteratorInit] = HandleIteratorInit;
            handlers[(int)InstructionKind.IteratorMoveNext] = HandleIteratorMoveNext;
            handlers[(int)InstructionKind.IteratorClose] = HandleIteratorClose;
            handlers[(int)InstructionKind.Break] = HandleBreak;
            handlers[(int)InstructionKind.Continue] = HandleContinue;
            handlers[(int)InstructionKind.Return] = HandleReturn;
            handlers[(int)InstructionKind.EnterWith] = HandleEnterWith;
            handlers[(int)InstructionKind.LeaveWith] = HandleLeaveWith;
            handlers[(int)InstructionKind.Expression] = HandleExpression;
            handlers[(int)InstructionKind.SetCompletionValue] = HandleSetCompletionValue;
            handlers[(int)InstructionKind.CompoundAssignmentSlot] = HandleCompoundAssignmentSlot;

            return handlers;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Individual instruction handlers
        // Each returns true to continue the loop, false to return _handlerReturnValue
        // ═══════════════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HandleJump(
            ExecutionPlanRunner runner,
            ExecutionInstruction instruction,
            ref JsEnvironment environment,
            EvaluationContext context)
        {
            runner._programCounter = Unsafe.As<JumpInstruction>(instruction).TargetIndex;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HandleBranch(
            ExecutionPlanRunner runner,
            ExecutionInstruction instruction,
            ref JsEnvironment environment,
            EvaluationContext context)
        {
            var branchInstruction = Unsafe.As<BranchInstruction>(instruction);

            // Fast path for simple binary comparisons
            JsValue testValue;
            if (branchInstruction.Condition is BinaryExpression
                {
                    Operator: BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual or
                    BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual
                } binCond)
            {
                // Try to get left operand without AST evaluation
                JsValue leftVal;
                switch (binCond.Left)
                {
                    case LiteralExpression { Value: var lit }:
                        leftVal = lit;
                        break;
                    case IdentifierExpression { SlotIndex: >= 0, ScopeId: >= 0 } leftId:
                        if (!environment.TryReadIdentifierWithSlot(leftId, context, out leftVal))
                            goto slowPath;
                        break;
                    default:
                        goto slowPath;
                }

                // Try to get right operand without AST evaluation
                JsValue rightVal;
                switch (binCond.Right)
                {
                    case LiteralExpression { Value: var lit }:
                        rightVal = lit;
                        break;
                    case IdentifierExpression { SlotIndex: >= 0, ScopeId: >= 0 } rightId:
                        if (!environment.TryReadIdentifierWithSlot(rightId, context, out rightVal))
                            goto slowPath;
                        break;
                    default:
                        goto slowPath;
                }

                // Apply comparison directly
                testValue = binCond.Operator switch
                {
                    BinaryOperator.LessThan => LessThanValue(leftVal, rightVal, context),
                    BinaryOperator.LessThanOrEqual => LessThanOrEqualValue(leftVal, rightVal, context),
                    BinaryOperator.GreaterThan => GreaterThanValue(leftVal, rightVal, context),
                    BinaryOperator.GreaterThanOrEqual => GreaterThanOrEqualValue(leftVal, rightVal, context),
                    _ => throw new InvalidOperationException()
                };
                goto afterConditionEval;
            }

            slowPath:
            testValue = branchInstruction.Condition.EvaluateExpression(environment, context);
            afterConditionEval:

            // Check for pending await (async code)
            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingBranchResult, environment))
            {
                runner._handlerReturnValue = pendingBranchResult;
                return false;
            }

            // Check for throw
            if (runner.TryHandleContextThrow(context, environment))
                return true;

            // Normal path: branch based on condition
            runner._programCounter = testValue.IsTruthy
                ? branchInstruction.ConsequentIndex
                : branchInstruction.AlternateIndex;
            return true;
        }

        // Placeholder handlers - will be filled in from existing switch cases
        private static bool HandleStatement(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleThrow(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleEvaluateAndDiscard(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleBinaryOp(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleIncrementSlot(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleFunctionDeclaration(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleClassDeclaration(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleSimpleVariableDeclaration(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandlePushEnvironment(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandlePopEnvironment(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleYield(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleYieldStar(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleStoreResumeValue(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleEnterTry(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleEnterCatch(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleEnterCatchWithDestructuring(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleLeaveTry(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleBreakableEnter(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleBreakableExit(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleEndFinally(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleIteratorInit(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleIteratorMoveNext(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleIteratorClose(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleBreak(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleContinue(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleReturn(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleEnterWith(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleLeaveWith(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleExpression(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleSetCompletionValue(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
        private static bool HandleCompoundAssignmentSlot(ExecutionPlanRunner r, ExecutionInstruction i, ref JsEnvironment e, EvaluationContext c)
            => throw new NotImplementedException("Handler not yet extracted");
    }
}
