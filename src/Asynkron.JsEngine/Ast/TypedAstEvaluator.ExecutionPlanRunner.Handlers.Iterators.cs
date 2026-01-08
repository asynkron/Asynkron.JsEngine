#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        private static InstructionResult HandleIteratorClose(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext __,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<IteratorCloseInstruction>(instr);
            if (TryGetSymbolValueJsValue(environment, instruction.IteratorSlot, out var iterStateValue) &&
                iterStateValue.TryGetObject<IteratorDriverState>(out var iterState) &&
                iterState.IteratorObject is { } iteratorObj)
            {
                if (!iterState.HasEnteredLoop)
                {
                    iterState.MarkIteratorClosed();
                    runner._programCounter = instruction.Next;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                iterState.MarkIteratorClosed();

                var hasPendingThrow = false;
                if (runner.TryCatchStateRef.TryStack.Count > 0)
                {
                    var topFrame = runner.TryCatchStateRef.TryStack.Peek();
                    hasPendingThrow = topFrame.PendingCompletion.Kind == AbruptKind.Throw;
                }

                try
                {
                    iteratorObj.IteratorClose(runner.EnsureEvaluationContext(), hasPendingThrow);
                }
                catch (ThrowSignal closeThrown)
                {
                    if (hasPendingThrow)
                    {
                        runner._programCounter = instruction.Next;
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, closeThrown.ThrownValue))
                    {
                        runner._programCounter = instruction.Next;
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
                    throw;
                }
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
        private static InstructionResult HandleIteratorInit(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<IteratorInitInstruction>(instr);
            var iterableEnv = environment;
            if (!instruction.TdzBindings.IsDefaultOrEmpty)
            {
                iterableEnv = new JsEnvironment(environment, false, false,
                    instruction.IterableExpression.Source, "for-of-head-tdz");
                foreach (var tdzSymbol in instruction.TdzBindings)
                {
                    iterableEnv.DefineJsValue(tdzSymbol, JsValue.Uninitialized,
                        instruction.TdzIsConst, isLexicalBinding: true,
                        blocksFunctionScopeOverride: true);
                }
            }

            var iterableValue = instruction.IterableExpression.EvaluateExpression(iterableEnv, context);
            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingIteratorResult, environment))
            {
                returnValue = pendingIteratorResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                return HandleIteratorInitThrowSlow(runner, context, out returnValue);
            }

            var iteratorState = CreateIteratorDriverState(iterableValue, instruction.IteratorKind, context);

            var iteratorEnv = environment;
            var walkCount = 0;
            if (instruction.IteratorSlotIndex >= 0)
            {
                while (iteratorEnv is not null &&
                       (iteratorEnv.ScopeId != runner._plan!.RootScopeId ||
                        !iteratorEnv.HasSlots ||
                        iteratorEnv._slots!.Length <= instruction.IteratorSlotIndex))
                {
                    iteratorEnv = iteratorEnv.Enclosing;
                    walkCount++;
                    if (walkCount > 1000)
                    {
                        break;
                    }
                }

                iteratorEnv ??= environment;
            }

            if (instruction.IteratorSlotIndex >= 0 && iteratorEnv.HasSlots)
            {
                iteratorState.IteratorVariable = new JsVariable(iteratorEnv, instruction.IteratorSlotIndex);
            }

            iteratorState.LoopScopeEnvironment = environment;
            runner.IteratorStateRef.CurrentDriverState = iteratorState;

            StoreValueBySlot(iteratorEnv, instruction.IteratorSlot,
                instruction.IteratorSlotIndex,
                JsValue.FromObjectUnsafe(iteratorState));

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleIteratorInitThrowSlow(
            ExecutionPlanRunner runner,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var initThrown = context.FlowValue;
            context.Clear();
            if (runner.HandleAbruptCompletion(AbruptKind.Throw, initThrown))
            {
                returnValue = default;
                return InstructionResult.Continue;
            }

            runner.TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(initThrown);
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static InstructionResult HandleIteratorMoveNext(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<IteratorMoveNextInstruction>(instr);
            var iteratorIndex = runner._programCounter;

            // Use cached driver state for scope-correct access from child scopes
            // (The iterator slot is in the loop scope, but we may be in a per-iteration child scope)
            var driverState = runner.IteratorStateRef.CurrentDriverState;

            if (driverState is null)
            {
                // Fallback: try to get iterator state from the correct scope.
                // The iterator slot is stored in function/module scope (not per-iteration envs),
                // so we need to walk up the chain to find it, similar to IteratorInit.
                var slotEnv = environment;
                var slotIdx = instruction.IteratorSlotIndex;

                // Walk up to find the scope with the right slots
                // Skip per-iteration envs since iterator temps are stored
                // in the function's root scope (RootScopeId), not per-iteration envs
                if (slotIdx >= 0)
                {
                    var slotWalkCount = 0;
                    while (slotEnv != null &&
                           (slotEnv.ScopeId != runner._plan!.RootScopeId ||
                            !slotEnv.HasSlots ||
                            slotEnv._slots!.Length <= slotIdx))
                    {
                        slotEnv = slotEnv.Enclosing;
                        slotWalkCount++;
                        if (slotWalkCount > 100)
                        {
                            break;
                        }
                    }

                    slotEnv ??= environment;
                }

                if (slotEnv is null || !TryGetValueBySlot(slotEnv,
                        instruction.IteratorSlot,
                        slotIdx, out var iteratorStateValue))
                {
                    runner._programCounter = instruction.BreakIndex;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                if (!iteratorStateValue.TryGetObject(out driverState))
                {
                    runner._programCounter = instruction.BreakIndex;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                runner.IteratorStateRef.CurrentDriverState = driverState;
            }

            // Get JsVariables directly from driverState (O(1) access, no dictionary lookup)
            var iterVar = driverState.IteratorVariable;
            var valueVar = driverState.ValueVariable;

            // Capture value JsVariable on first execution (while still in loop scope)
            // IMPORTANT: Use the loop scope environment (from iterVar) rather than the current
            // environment, which may be a stale per-iteration environment from a previous outer
            // loop iteration. The value slot is allocated in the same scope as the iterator.
            if (!valueVar.IsValid && instruction.ValueSlotIndex >= 0)
            {
                // Use the iterator's environment since value slot is in the same scope
                var loopScopeEnv = iterVar.IsValid ? iterVar.Environment : environment;
                if (loopScopeEnv.HasSlots && loopScopeEnv._slots!.Length > instruction.ValueSlotIndex)
                {
                    valueVar = new JsVariable(loopScopeEnv, instruction.ValueSlotIndex);
                    driverState.ValueVariable = valueVar;
                }
            }

            if (!driverState.IsAsyncIterator)
            {
                return runner.HandleSyncIteratorMoveNext(instruction, ref environment, context, driverState, valueVar, out returnValue);
            }

            return runner.HandleAsyncIteratorMoveNext(instruction, ref environment, context, driverState, iterVar, valueVar, iteratorIndex, out returnValue);
        }
    }
}
