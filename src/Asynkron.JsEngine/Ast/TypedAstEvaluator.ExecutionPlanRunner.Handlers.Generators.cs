#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleYield(
                    ExecutionPlanRunner runner,
                    ExecutionInstruction instr,
                    ref JsEnvironment environment,
                    EvaluationContext context,
                    out JsValue returnValue)
        {
            var instruction = Unsafe.As<YieldInstruction>(instr);
            var yieldedValue = JsValue.Undefined;
            if (instruction.YieldProgram is not null || instruction.YieldExpression is not null)
            {
                yieldedValue = instruction.YieldProgram is { } yieldProgram
                    ? runner.EvaluateExpressionProgram(yieldProgram, environment, context)
                    : instruction.YieldExpression!.EvaluateExpression(environment, context);

                if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingYieldResult, environment))
                {
                    returnValue = pendingYieldResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    return HandleYieldThrowSlow(runner, context, out returnValue);
                }

                if (context.IsYield)
                {
                    return HandleYieldNestedSlow(runner, ref environment, context, out returnValue);
                }
            }

            runner._programCounter = instruction.Next;
            runner.RecordYield(context, environment);
            runner._state = GeneratorState.Suspended;
            returnValue = CreateIteratorResult(yieldedValue, false);
            return InstructionResult.Return;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleYieldThrowSlow(
            ExecutionPlanRunner runner,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var thrown = context.FlowValue;
            context.Clear();
            if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown))
            {
                returnValue = default;
                return InstructionResult.Continue;
            }

            runner.TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(thrown);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleYieldNestedSlow(
            ExecutionPlanRunner runner,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var yieldedValue = context.FlowValue;
            var nestedIteratorResult = (context.CurrentSignal as YieldCompletionSignal)?.IteratorResultObject;
            context.Clear();
            runner._programCounter = runner._currentInstructionIndex;
            runner.RecordYield(context, environment);
            runner._state = GeneratorState.Suspended;
            returnValue = nestedIteratorResult is not null
                ? JsValue.FromObjectUnsafe(nestedIteratorResult)
                : CreateIteratorResult(yieldedValue, false);
            return InstructionResult.Return;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleStoreResumeValue(
                    ExecutionPlanRunner runner,
                    ExecutionInstruction instr,
                    ref JsEnvironment environment,
                    EvaluationContext context,
                    out JsValue returnValue)
        {
            var instruction = Unsafe.As<StoreResumeValueInstruction>(instr);
            var (resumeKind, resumePayload) = runner.ConsumeResumeValue();
            if (resumeKind == ResumePayloadKind.Throw)
            {
                context.SetThrow(resumePayload);
            }
            else if (resumeKind == ResumePayloadKind.Return)
            {
                context.SetReturn(resumePayload);
            }
            else if (instruction.TargetSymbol is { } resumeSymbol)
            {
                StoreSymbolValueJsValue(environment, resumeSymbol, resumePayload);
            }

            if (context.IsThrow)
            {
                var thrownPayload = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrownPayload))
                {
                    if (runner._programCounter == runner._currentInstructionIndex)
                    {
                        // If we're inside a scheduled finally block and the throw updated the
                        // pending completion, skip remaining finally code by jumping to EndFinally.
                        // This prevents execution of code after the yield in the finally block.
                        if (runner.TryGetEndFinallyJumpTarget(out var endFinallyTarget))
                        {
                            runner._programCounter = endFinallyTarget;
                        }
                        else
                        {
                            runner._programCounter = instruction.Next;
                        }
                    }
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                runner.TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(thrownPayload);
            }

            if (context.IsReturn)
            {
                var resumeReturnValue = context.FlowValue;
                context.ClearReturn();
                if (runner.HandleAbruptCompletion(AbruptKind.Return, resumeReturnValue))
                {
                    if (runner._programCounter == runner._currentInstructionIndex)
                    {
                        // If we're inside a scheduled finally block and the return updated the
                        // pending completion, skip remaining finally code by jumping to EndFinally.
                        // This prevents execution of code after the yield in the finally block.
                        if (runner.TryGetEndFinallyJumpTarget(out var endFinallyTarget))
                        {
                            runner._programCounter = endFinallyTarget;
                        }
                        else
                        {
                            runner._programCounter = instruction.Next;
                        }
                    }
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                returnValue = runner.CompleteReturn(resumeReturnValue);
                return InstructionResult.Return;
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleYieldStar(
                    ExecutionPlanRunner runner,
                    ExecutionInstruction instr,
                    ref JsEnvironment environment,
                    EvaluationContext context,
                    out JsValue returnValue)
        {
            var instruction = Unsafe.As<YieldStarInstruction>(instr);
            var currentIndex = runner._programCounter;
            if (!TryGetSymbolValueJsValue(environment, instruction.StateSlotSymbol,
                    out var stateValue) ||
                !stateValue.TryGetObject<YieldStarState>(out var yieldStarState))
            {
                yieldStarState = new YieldStarState();
                StoreSymbolValue(environment, instruction.StateSlotSymbol, yieldStarState);
            }

            if (yieldStarState.PendingAbrupt != AbruptKind.None &&
                runner.AsyncStateRef.PendingResumeKind is not ResumePayloadKind.Throw
                    and not ResumePayloadKind.Return)
            {
                var pendingKind = yieldStarState.PendingAbrupt;
                var pendingValue = yieldStarState.PendingValue;
                yieldStarState.PendingAbrupt = AbruptKind.None;
                yieldStarState.PendingValue = JsValue.Undefined;
                yieldStarState.State = null;
                yieldStarState.AwaitingResume = false;
                environment.AssignJsValue(instruction.StateSlotSymbol, JsValue.Null);

                switch (pendingKind)
                {
                    case AbruptKind.Throw
                        when runner.HandleAbruptCompletion(AbruptKind.Throw, pendingValue):
                        returnValue = default;
                        return InstructionResult.Continue;
                    case AbruptKind.Throw:
                        runner.TryCatchStateRef.TryStack.Clear();
                        throw new ThrowSignal(pendingValue);
                    case AbruptKind.Return when runner.HandleAbruptCompletion(AbruptKind.Return,
                        pendingValue):
                        returnValue = default;
                        return InstructionResult.Continue;
                    case AbruptKind.Return:
                        returnValue = runner.CompleteReturn(pendingValue);
                        return InstructionResult.Return;
                }
            }

            var isFirstYieldStarEntry = yieldStarState.State is null;

            if (yieldStarState.State is null)
            {
                runner._realmState.Logger?.LogInformation("YieldStar: Creating new DelegatedState");
                var yieldStarIterableValue = instruction.IterableProgram is { } iterableProgram
                    ? runner.EvaluateExpressionProgram(iterableProgram, environment, context)
                    : instruction.IterableExpression!.EvaluateExpression(environment, context);
                if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingYieldStarResult, environment))
                {
                    returnValue = pendingYieldStarResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(thrown);
                }

                yieldStarState.State = CreateDelegatedState(yieldStarIterableValue, context);

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(thrown);
                }

                yieldStarState.AwaitingResume = false;
            }
            else
            {
                runner._realmState.Logger?.LogInformation(
                    "YieldStar: Reusing existing DelegatedState, AwaitingResume={Awaiting}",
                    yieldStarState.AwaitingResume);
            }

            while (true)
            {
                var sendValue = JsValue.Undefined;
                var propagateThrow = false;
                var propagateReturn = false;

                if (isFirstYieldStarEntry)
                {
                    sendValue = JsValue.Undefined;
                }
                else if (yieldStarState.AwaitingResume)
                {
                    var (delegatedResumeKind, delegatedResumePayload) = runner.ConsumeResumeValue();
                    switch (delegatedResumeKind)
                    {
                        case ResumePayloadKind.Throw:
                            propagateThrow = true;
                            break;
                        case ResumePayloadKind.Return:
                            propagateReturn = true;
                            break;
                    }

                    sendValue = delegatedResumePayload;
                }

                var iteratorResult = yieldStarState.State!.MoveNext(
                    sendValue,
                    propagateThrow,
                    propagateReturn,
                    context,
                    out _);

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    yieldStarState.State = null;
                    yieldStarState.AwaitingResume = false;
                    environment.AssignJsValue(instruction.StateSlotSymbol, JsValue.Null);
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown))
                    {
                        break;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(thrown);
                }

                if (iteratorResult.IsDelegatedCompletion)
                {
                    var isThrowCompletion = propagateThrow || iteratorResult.PropagateThrow;
                    var pendingKind = isThrowCompletion ? AbruptKind.Throw : AbruptKind.Return;
                    var abruptValue = iteratorResult.Value;

                    if (!iteratorResult.Done)
                    {
                        yieldStarState.PendingAbrupt = pendingKind;
                        yieldStarState.PendingValue = sendValue;
                        yieldStarState.AwaitingResume = true;
                        runner._programCounter = currentIndex;
                        runner.RecordYield(context, environment);
                        runner._state = GeneratorState.Suspended;
                        returnValue = iteratorResult.IteratorResultObject is not null
                            ? JsValue.FromObjectUnsafe(iteratorResult.IteratorResultObject)
                            : CreateIteratorResult(iteratorResult.Value, false);
                        return InstructionResult.Return;
                    }

                    yieldStarState.State = null;
                    yieldStarState.AwaitingResume = false;
                    environment.AssignJsValue(instruction.StateSlotSymbol, JsValue.Null);

                    if (pendingKind == AbruptKind.Throw)
                    {
                        if (runner.HandleAbruptCompletion(AbruptKind.Throw, abruptValue))
                        {
                            break;
                        }

                        runner.TryCatchStateRef.TryStack.Clear();
                        throw new ThrowSignal(abruptValue);
                    }

                    if (runner.HandleAbruptCompletion(AbruptKind.Return, abruptValue))
                    {
                        break;
                    }

                    returnValue = runner.CompleteReturn(abruptValue);
                    return InstructionResult.Return;
                }

                if (propagateThrow && iteratorResult.Done)
                {
                    yieldStarState.State = null;
                    yieldStarState.AwaitingResume = false;
                    environment.AssignJsValue(instruction.StateSlotSymbol, JsValue.Null);
                    if (instruction.ResultSlotSymbol is { } throwResultSlot)
                    {
                        StoreSymbolValue(environment, throwResultSlot, iteratorResult.Value);
                    }

                    runner._programCounter = instruction.Next;
                    break;
                }

                if (iteratorResult.Done && !propagateThrow && !propagateReturn)
                {
                    yieldStarState.State = null;
                    yieldStarState.AwaitingResume = false;
                    environment.AssignJsValue(instruction.StateSlotSymbol, JsValue.Null);
                    if (instruction.ResultSlotSymbol is { } resultSlot)
                    {
                        StoreSymbolValue(environment, resultSlot, iteratorResult.Value);
                    }

                    runner._programCounter = instruction.Next;
                    break;
                }

                yieldStarState.AwaitingResume = true;
                runner._programCounter = currentIndex;
                runner.RecordYield(context, environment);
                runner._state = GeneratorState.Suspended;
                if (iteratorResult.IteratorResultObject is { } originalResult)
                {
                    returnValue = JsValue.FromObjectUnsafe(originalResult);
                    return InstructionResult.Return;
                }

                var resultDone = propagateReturn && iteratorResult.Done;
                returnValue = CreateIteratorResult(iteratorResult.Value, resultDone);
                return InstructionResult.Return;
            }

            returnValue = default;
            return InstructionResult.Continue;
        }
    }
}
