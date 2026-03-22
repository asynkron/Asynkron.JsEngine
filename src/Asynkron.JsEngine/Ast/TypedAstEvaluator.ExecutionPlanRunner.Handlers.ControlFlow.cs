#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        private static InstructionResult HandleBreakableEnter(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment _,
            EvaluationContext __,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<BreakableEnterInstruction>(instr);
            if (instruction.ConstructKind == BreakableKind.ResetsCompletionValue)
            {
                runner.ResetCompletionValue();
            }

            runner.BreakableStateRef.BreakableStack.Push(new BreakableFrame(
                instruction.Label,
                instruction.BreakTarget,
                instruction.ContinueTarget));

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleBreakableExit(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment _,
            EvaluationContext __,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<BreakableExitInstruction>(instr);
            if (runner.BreakableStateRef.BreakableStack.Count > 0)
            {
                runner.BreakableStateRef.BreakableStack.Pop();
            }

            runner.FinalizeCompletionValue();
            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleBreak(
                    ExecutionPlanRunner runner,
                    ExecutionInstruction instr,
                    ref JsEnvironment environment,
                    EvaluationContext __,
                    out JsValue returnValue)
        {
            var instruction = Unsafe.As<BreakInstruction>(instr);
            return HandleAbruptControlJump(
                runner,
                instruction.TargetIndex,
                instruction.TargetScopeId,
                AbruptKind.Break,
                ref environment,
                out returnValue);
        }

        private static InstructionResult HandleContinue(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext __,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ContinueInstruction>(instr);
            return HandleAbruptControlJump(
                runner,
                instruction.TargetIndex,
                instruction.TargetScopeId,
                AbruptKind.Continue,
                ref environment,
                out returnValue);
        }

        private static InstructionResult HandleAbruptControlJump(
            ExecutionPlanRunner runner,
            int targetIndex,
            int targetScopeId,
            AbruptKind abruptKind,
            ref JsEnvironment environment,
            out JsValue returnValue)
        {
            if (runner.HandleAbruptCompletion(abruptKind, targetIndex))
            {
                if (runner._programCounter == runner._currentInstructionIndex && runner.TryCatchStateRef.TryStack.Count > 0)
                {
                    var frame = runner.TryCatchStateRef.TryStack.Peek();
                    if (frame.EndFinallyIndex >= 0)
                    {
                        runner._programCounter = frame.EndFinallyIndex;
                    }
                }

                returnValue = default;
                return InstructionResult.Continue;
            }

            if (targetScopeId >= 0)
            {
                var walkEnv = environment;
                while (walkEnv.ScopeId != targetScopeId && walkEnv.Enclosing != null)
                {
                    walkEnv = walkEnv.Enclosing;
                }

                if (walkEnv.ScopeId == targetScopeId)
                {
                    environment = walkEnv;
                }
            }

            runner._programCounter = targetIndex;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleReturn(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ReturnInstruction>(instr);
            var returnVal = instruction.ReturnProgram is { } returnProgram
                ? runner.EvaluateExpressionProgram(returnProgram, environment, context)
                : JsValue.Undefined;

            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingReturnResult, environment))
            {
                returnValue = pendingReturnResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                return HandleReturnThrowSlow(runner, instruction, context, out returnValue);
            }

            if (context.IsReturn)
            {
                var pendingReturn = context.FlowValue;
                context.ClearReturn();
                returnVal = pendingReturn;
            }

            var wasInsideScheduledFinally = runner.IsInsideScheduledFinally();

            if (runner.HandleAbruptCompletionJsValue(AbruptKind.Return, returnVal))
            {
                if (wasInsideScheduledFinally)
                {
                    returnValue = runner.CompleteReturn(returnVal);
                    return InstructionResult.Return;
                }

                if (runner._programCounter == runner._currentInstructionIndex)
                {
                    runner._programCounter = instruction.Next;
                }
                returnValue = default;
                return InstructionResult.Continue;
            }

            returnValue = runner.CompleteReturn(returnVal);
            return InstructionResult.Return;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleReturnThrowSlow(
            ExecutionPlanRunner runner,
            ReturnInstruction instruction,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var pendingThrow = context.FlowValue;
            context.Clear();
            if (runner.HandleAbruptCompletion(AbruptKind.Throw, pendingThrow))
            {
                if (runner._programCounter == runner._currentInstructionIndex)
                {
                    runner._programCounter = instruction.Next;
                }
                returnValue = default;
                return InstructionResult.Continue;
            }

            runner.TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(pendingThrow);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleJump(
                    ExecutionPlanRunner runner,
                    ExecutionInstruction instr,
                    ref JsEnvironment _,
                    EvaluationContext __,
                    out JsValue returnValue)
        {
            runner._programCounter = Unsafe.As<JumpInstruction>(instr).TargetIndex;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleBranch(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<BranchInstruction>(instr);
            var testValue = runner.EvaluateExpressionProgram(instruction.ConditionProgram, environment, context);

            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingBranchResult, environment))
            {
                returnValue = pendingBranchResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                return HandleBranchThrowSlow(runner, context, out returnValue);
            }

            runner._programCounter = testValue.IsTruthy ? instruction.ConsequentIndex : instruction.AlternateIndex;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleBranchThrowSlow(
            ExecutionPlanRunner runner,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var thrownValue = context.FlowValue;
            context.Clear();
            if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrownValue))
            {
                returnValue = default;
                return InstructionResult.Continue;
            }

            runner.TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(thrownValue);
        }
    }
}
