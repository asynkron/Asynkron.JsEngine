#region

using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        // Delegate type for instruction handlers - enables O(1) dispatch via array lookup
        private delegate InstructionResult InstructionHandler(
            ExecutionPlanRunner runner,
            ExecutionInstruction instruction,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue);

        // Static handler array indexed by InstructionKind for O(1) dispatch
        private static readonly InstructionHandler[] InstructionHandlers = InitializeHandlers();

        private static InstructionHandler[] InitializeHandlers()
        {
            var handlers = new InstructionHandler[36];
            handlers[(int)InstructionKind.Statement] = HandleStatement;
            handlers[(int)InstructionKind.Throw] = HandleThrow;
            handlers[(int)InstructionKind.EvaluateAndDiscard] = HandleEvaluateAndDiscard;
            handlers[(int)InstructionKind.BinaryOp] = HandleBinaryOp;
            handlers[(int)InstructionKind.IncrementSlot] = HandleIncrementSlot;
            handlers[(int)InstructionKind.CompoundAssignmentSlot] = HandleCompoundAssignmentSlot;
            handlers[(int)InstructionKind.FunctionDeclaration] = HandleFunctionDeclaration;
            handlers[(int)InstructionKind.ClassDeclaration] = HandleClassDeclaration;
            handlers[(int)InstructionKind.SimpleVariableDeclaration] = HandleSimpleVariableDeclaration;
            handlers[(int)InstructionKind.ComplexVariableDeclaration] = HandleComplexVariableDeclaration;
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
            handlers[(int)InstructionKind.Jump] = HandleJump;
            handlers[(int)InstructionKind.Branch] = HandleBranch;
            handlers[(int)InstructionKind.Break] = HandleBreak;
            handlers[(int)InstructionKind.Continue] = HandleContinue;
            handlers[(int)InstructionKind.Return] = HandleReturn;
            handlers[(int)InstructionKind.EnterWith] = HandleEnterWith;
            handlers[(int)InstructionKind.LeaveWith] = HandleLeaveWith;
            handlers[(int)InstructionKind.IteratorClose] = HandleIteratorClose;
            handlers[(int)InstructionKind.SetCompletionValue] = HandleSetCompletionValue;
            handlers[(int)InstructionKind.Expression] = DispatchExpression;
            handlers[(int)InstructionKind.ForInInit] = HandleForInInit;
            handlers[(int)InstructionKind.ForInMoveNext] = HandleForInMoveNext;
            return handlers;
        }

        private static InstructionResult DispatchExpression(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment env,
            EvaluationContext ctx,
            out JsValue ret)
            => throw new InvalidOperationException("Expression instruction should not be dispatched via handler table");

        /// <summary>
        /// Result of an instruction handler for control flow.
        /// </summary>
        private enum InstructionResult
        {
            /// <summary>Continue to next instruction (normal flow).</summary>
            Continue,
            /// <summary>Return from ExecutePlan with a value.</summary>
            Return,
            /// <summary>An exception was thrown (already handled).</summary>
            //Throw
        }
    }
}
