#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        private static InstructionResult HandleThrow(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ThrowInstruction>(instr);
            var throwValue = instruction.Expression.EvaluateExpression(environment, context);

            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingThrowResult, environment))
            {
                returnValue = pendingThrowResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                var existingThrown = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, existingThrown))
                {
                    if (runner._programCounter != runner._currentInstructionIndex)
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    if (runner.TryCatchStateRef.TryStack.Count > 0)
                    {
                        runner.TryCatchStateRef.TryStack.Pop();
                        if (runner.HandleAbruptCompletion(AbruptKind.Throw, existingThrown))
                        {
                            returnValue = default;
                            return InstructionResult.Continue;
                        }
                    }
                }

                runner.TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(existingThrown);
            }

            if (runner.HandleAbruptCompletion(AbruptKind.Throw, throwValue))
            {
                if (runner._programCounter != runner._currentInstructionIndex)
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                if (runner.TryCatchStateRef.TryStack.Count > 0)
                {
                    runner.TryCatchStateRef.TryStack.Pop();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, throwValue))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }
                }
            }

            runner.TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(throwValue);
        }

        private static InstructionResult HandleEnterTry(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext ctx,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<EnterTryInstruction>(instr);
            runner.ResetCompletionValue();
            runner.PushTryFrame(instruction, environment);
            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleLeaveTry(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext ctx,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<LeaveTryInstruction>(instr);
            runner.CompleteTryNormally(instruction.Next);
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleEnterCatch(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext ctx,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<EnterCatchInstruction>(instr);
            runner.ResetCompletionValue();

            var thrownValue = PrepareCatchEnvironment(runner, ref environment,
                instruction.SlotCount, instruction.ScopeId, out var catchEnv);

            if (instruction.CatchParameterSymbol is { } param)
            {
                catchEnv.DefineJsValue(param, thrownValue, false, isLexicalBinding: true);
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleEnterCatchWithDestructuring(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<EnterCatchWithDestructuringInstruction>(instr);
            runner.ResetCompletionValue();

            var thrownValue = PrepareCatchEnvironment(runner, ref environment,
                instruction.SlotCount, instruction.ScopeId, out var catchEnv);

            instruction.BindingPattern.DefineBindingTarget(thrownValue, catchEnv, context, false);

            if (context.ShouldStopEvaluation)
            {
                if (context.IsThrow)
                {
                    var exception = context.FlowValue;
                    context.Clear();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, exception))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(exception);
                }
            }

            environment = catchEnv;
            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static JsValue PrepareCatchEnvironment(
            ExecutionPlanRunner runner,
            ref JsEnvironment environment,
            int slotCount,
            int scopeId,
            out JsEnvironment catchEnv)
        {
            var thrownValue = JsValue.Undefined;
            if (runner.TryCatchStateRef.TryStack.Count > 0)
            {
                thrownValue = runner.TryCatchStateRef.TryStack.Peek().ThrownValue;
            }

            catchEnv = new JsEnvironment(
                environment,
                false,
                environment.IsStrict,
                null,
                "catch");

            if (slotCount > 0)
            {
                catchEnv.InitializeSlots(slotCount, scopeId);
            }

            environment = catchEnv;
            return thrownValue;
        }

        private static InstructionResult HandleEndFinally(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext ctx,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<EndFinallyInstruction>(instr);
            if (runner.TryCatchStateRef.TryStack.Count == 0)
            {
                runner._programCounter = instruction.Next;
                returnValue = default;
                return InstructionResult.Continue;
            }

            var completedFrame = runner.TryCatchStateRef.TryStack.Pop();
            var pending = completedFrame.PendingCompletion;

            if (pending.Kind == AbruptKind.None)
            {
                runner.RestoreCompletionValueFromFinally(completedFrame);
                var target = pending.ResumeTarget >= 0 ? pending.ResumeTarget : instruction.Next;
                runner._programCounter = target;
                returnValue = default;
                return InstructionResult.Continue;
            }

            if (pending.Kind == AbruptKind.Return)
            {
                if (runner.HandleAbruptCompletion(AbruptKind.Return, pending.Value))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                var pendingJs = pending.Value is JsValue pjs ? pjs : JsValue.FromObjectUnsafe(pending.Value);
                returnValue = runner.CompleteReturn(pendingJs);
                return InstructionResult.Return;
            }

            if (pending.Kind == AbruptKind.Break || pending.Kind == AbruptKind.Continue)
            {
                runner.RestoreCompletionValueFromFinally(completedFrame);
                if (runner.HandleAbruptCompletion(pending.Kind, pending.Value))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                runner._programCounter = pending.Value is int idx ? idx : instruction.Next;
                returnValue = default;
                return InstructionResult.Continue;
            }

            if (runner.HandleAbruptCompletion(AbruptKind.Throw, pending.Value))
            {
                returnValue = default;
                return InstructionResult.Continue;
            }

            runner.TryCatchStateRef.TryStack.Clear();
            var throwJs = pending.Value is JsValue tjs ? tjs : JsValue.FromObjectUnsafe(pending.Value);
            throw new ThrowSignal(throwJs);
        }
    }
}
