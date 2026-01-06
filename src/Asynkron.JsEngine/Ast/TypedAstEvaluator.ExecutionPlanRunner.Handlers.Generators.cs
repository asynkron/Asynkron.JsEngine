#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static InstructionResult HandleYield(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<YieldInstruction>(instr);
            var yieldedValue = JsValue.Undefined;
            if (instruction.YieldExpression is not null)
            {
                yieldedValue = instruction.YieldExpression.EvaluateExpression(environment, context);

                if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingYieldResult, environment))
                {
                    returnValue = pendingYieldResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(thrown);
                }

                if (context.IsYield)
                {
                    yieldedValue = context.FlowValue;
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
            }

            runner._programCounter = instruction.Next;
            runner.RecordYield(context, environment);
            runner._state = GeneratorState.Suspended;
            returnValue = CreateIteratorResult(yieldedValue, false);
            return InstructionResult.Return;
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
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
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrownPayload, environment))
                {
                    if (runner._programCounter == runner._currentInstructionIndex)
                    {
                        runner._programCounter = instruction.Next;
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
                if (runner.HandleAbruptCompletion(AbruptKind.Return, resumeReturnValue, environment))
                {
                    if (runner._programCounter == runner._currentInstructionIndex)
                    {
                        runner._programCounter = instruction.Next;
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

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
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
                        when runner.HandleAbruptCompletion(AbruptKind.Throw, pendingValue, environment):
                        returnValue = default;
                        return InstructionResult.Continue;
                    case AbruptKind.Throw:
                        runner.TryCatchStateRef.TryStack.Clear();
                        throw new ThrowSignal(pendingValue);
                    case AbruptKind.Return when runner.HandleAbruptCompletion(AbruptKind.Return,
                        pendingValue, environment):
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
                var yieldStarIterableValue =
                    instruction.IterableExpression.EvaluateExpression(environment, context);
                if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingYieldStarResult, environment))
                {
                    returnValue = pendingYieldStarResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
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
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
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
                            sendValue = delegatedResumePayload;
                            break;
                        case ResumePayloadKind.Return:
                            propagateReturn = true;
                            sendValue = delegatedResumePayload;
                            break;
                        default:
                            sendValue = delegatedResumePayload;
                            break;
                    }
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
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
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
                        if (runner.HandleAbruptCompletion(AbruptKind.Throw, abruptValue, environment))
                        {
                            break;
                        }

                        runner.TryCatchStateRef.TryStack.Clear();
                        throw new ThrowSignal(abruptValue);
                    }

                    if (runner.HandleAbruptCompletion(AbruptKind.Return, abruptValue, environment))
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
