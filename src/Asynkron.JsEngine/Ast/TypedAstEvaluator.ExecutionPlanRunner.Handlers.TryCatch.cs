#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

#pragma warning disable CS0618 // Compatibility overloads remain for dynamic/resume seams; not proof of direct runner AST fallback.

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleThrow(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ThrowInstruction>(instr);
            var throwProgram = instruction.ThrowProgram;
            var throwValue = instruction.AwaitedProgram is { } awaitedProgram
                ? runner.EvaluateAwaitInGenerator(instruction.AwaitStateKey!, awaitedProgram, environment, context)
                : runner.EvaluateExpressionProgram(
                    throwProgram ?? throw new InvalidOperationException("Throw instruction is missing its payload."),
                    environment,
                    context);

            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingThrowResult, environment))
            {
                returnValue = pendingThrowResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                return HandleThrowExistingSlow(runner, context, out returnValue);
            }

            return HandleThrowNewSlow(runner, throwValue, out returnValue);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleThrowExistingSlow(
            ExecutionPlanRunner runner,
            EvaluationContext context,
            out JsValue returnValue)
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleThrowNewSlow(
            ExecutionPlanRunner runner,
            JsValue throwValue,
            out JsValue returnValue)
        {
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
            EvaluationContext __,
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
            ref JsEnvironment _,
            EvaluationContext __,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<LeaveTryInstruction>(instr);
            if (runner.TryCatchStateRef.TryStack.Count == 0 ||
                runner.TryCatchStateRef.TryStack.Peek().LeaveTryIndex == runner._currentInstructionIndex)
            {
                runner.CompleteTryNormally(instruction.Next);
            }
            else
            {
                runner._programCounter = instruction.Next;
            }

            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleEnterCatch(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<EnterCatchInstruction>(instr);
            runner.ResetCompletionValue();

            var thrownValue = PrepareCatchEnvironment(runner, ref environment,
                instruction.SlotCount, instruction.ScopeId, out var catchEnv);

            if (instruction.CatchBindingProgram is IdentifierBindingTargetProgram identifier)
            {
                var param = identifier.Name;
                catchEnv.SetSimpleCatchParameters(
                    new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance) { param });
                catchEnv.DefineJsValue(param, thrownValue, false, isLexicalBinding: true);
            }
            else if (instruction.CatchBindingProgram is { } catchBindingProgram)
            {
                runner.ApplyBindingTargetProgram(
                    catchBindingProgram,
                    thrownValue,
                    catchEnv,
                    context,
                    BindingMode.DefineLet,
                    allowNameInference: false);

                if (context.IsThrow)
                {
                    return HandleEnterCatchThrowSlow(runner, instruction, context, out returnValue);
                }
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleEnterCatchThrowSlow(
            ExecutionPlanRunner runner,
            EnterCatchInstruction instruction,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var thrown = context.FlowValue;
            context.Clear();
            if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown))
            {
                if (runner._programCounter == runner._currentInstructionIndex)
                {
                    runner._programCounter = instruction.Next;
                }

                returnValue = default;
                return InstructionResult.Continue;
            }

            runner.TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(thrown);
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

            catchEnv = JsEnvironment.CreateInstance(environment,
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
            ref JsEnvironment _,
            EvaluationContext __,
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
                if (pending.OriginatedInFinally)
                {
                    runner.FinalizeCompletionValue();
                }
                else
                {
                    runner.RestoreCompletionValueFromFinally(completedFrame);
                }

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
