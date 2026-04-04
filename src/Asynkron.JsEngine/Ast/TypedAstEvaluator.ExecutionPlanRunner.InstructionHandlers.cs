#region

using Asynkron.JsEngine.Execution.Instructions;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

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
            var handlers = new InstructionHandler[Enum.GetValues<InstructionKind>().Length];
            handlers[(int)InstructionKind.Throw] = HandleThrow;
            handlers[(int)InstructionKind.EvaluateAndDiscard] = HandleEvaluateAndDiscard;
            handlers[(int)InstructionKind.AwaitAndDiscard] = HandleAwaitAndDiscard;
            handlers[(int)InstructionKind.IncrementSlot] = HandleIncrementSlot;
            handlers[(int)InstructionKind.AssignmentSlot] = HandleAssignmentSlot;
            handlers[(int)InstructionKind.LogicalCompoundAssignmentSlot] = HandleLogicalCompoundAssignmentSlot;
            handlers[(int)InstructionKind.CompoundAssignmentSlot] = HandleCompoundAssignmentSlot;
            handlers[(int)InstructionKind.FunctionDeclaration] = HandleFunctionDeclaration;
            handlers[(int)InstructionKind.ClassDeclaration] = HandleClassDeclaration;
            handlers[(int)InstructionKind.SimpleVariableDeclaration] = HandleSimpleVariableDeclaration;
            handlers[(int)InstructionKind.SuspendingSimpleVariableDeclaration] = HandleSuspendingSimpleVariableDeclaration;
            handlers[(int)InstructionKind.BindingVariableDeclaration] = HandleBindingVariableDeclaration;
            handlers[(int)InstructionKind.SuspendingBindingVariableDeclaration] = HandleSuspendingBindingVariableDeclaration;
            handlers[(int)InstructionKind.PushEnvironment] = HandlePushEnvironment;
            handlers[(int)InstructionKind.PopEnvironment] = HandlePopEnvironment;
            handlers[(int)InstructionKind.Yield] = HandleYield;
            handlers[(int)InstructionKind.YieldStar] = HandleYieldStar;
            handlers[(int)InstructionKind.StoreResumeValue] = HandleStoreResumeValue;
            handlers[(int)InstructionKind.EnterTry] = HandleEnterTry;
            handlers[(int)InstructionKind.EnterCatch] = HandleEnterCatch;
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
            handlers[(int)InstructionKind.ForInInit] = HandleForInInit;
            handlers[(int)InstructionKind.ForInMoveNext] = HandleForInMoveNext;
            // Array destructuring
            handlers[(int)InstructionKind.ArrayDestructuringInit] = HandleArrayDestructuringInit;
            handlers[(int)InstructionKind.ArrayDestructuringElement] = HandleArrayDestructuringElement;
            handlers[(int)InstructionKind.ArrayDestructuringRest] = HandleArrayDestructuringRest;
            handlers[(int)InstructionKind.ArrayDestructuringClose] = HandleArrayDestructuringClose;
            return handlers;
        }

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
