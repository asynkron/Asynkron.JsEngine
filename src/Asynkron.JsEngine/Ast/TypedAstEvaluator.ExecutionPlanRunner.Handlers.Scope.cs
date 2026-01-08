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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static InstructionResult HandleEnterWith(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<EnterWithInstruction>(instr);
            var objValueJs = instruction.ObjectExpression.EvaluateExpression(environment, context);

            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingWithResult, environment))
            {
                returnValue = pendingWithResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                return HandleEnterWithThrowSlow(runner, context, out returnValue);
            }

            if (TryConvertToWithBindingObject(objValueJs, context, out var withObject))
            {
                var withEnv = new JsEnvironment(environment, false, context.CurrentScope.IsStrict,
                    instruction.ObjectExpression.Source, "with", withObject);
                StoreSymbolValue(runner._executionEnvironment!, instruction.WithScopeSlot, withEnv);
                runner.WithStateRef.ActiveWithScopes.Push(instruction.WithScopeSlot);
                environment = withEnv;
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleEnterWithThrowSlow(
            ExecutionPlanRunner runner,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var thrownWith = context.FlowValue;
            context.Clear();
            if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrownWith))
            {
                returnValue = default;
                return InstructionResult.Continue;
            }

            runner.TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(thrownWith);
        }

        private static InstructionResult HandleLeaveWith(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext __,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<LeaveWithInstruction>(instr);
            if (runner.WithStateRef.ActiveWithScopes.Count > 0 &&
                ReferenceEquals(runner.WithStateRef.ActiveWithScopes.Peek(), instruction.WithScopeSlot))
            {
                runner.WithStateRef.ActiveWithScopes.Pop();
            }

            if (TryGetSymbolValueJsValue(runner._executionEnvironment!, instruction.WithScopeSlot, out var storedEnvValue) &&
                storedEnvValue.TryGetObject<JsEnvironment>(out var storedWithEnv))
            {
                environment = storedWithEnv.Enclosing ?? environment;
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandlePushEnvironment(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext __,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<PushEnvironmentInstruction>(instr);
            var hasIterationBindings = !instruction.PerIterationBindings.IsDefaultOrEmpty;
            var iteratorDriverState = hasIterationBindings ? runner.IteratorStateRef.CurrentDriverState : null;
            var isSubsequentIteration =
                hasIterationBindings &&
                ((instruction.ScopeId >= 0 && environment.ScopeId == instruction.ScopeId) ||
                 (instruction.ScopeId < 0 && environment.Description == "scope" && environment.Enclosing != null));

            if (isSubsequentIteration &&
                instruction.AllowPooling &&
                !instruction.PerIterationBindings.IsDefaultOrEmpty)
            {
                if (iteratorDriverState is not null)
                {
                    // Only update if this is the iterator's own loop, not a nested for-let loop
                    if (iteratorDriverState.LoopScopeEnvironment is null ||
                        ReferenceEquals(iteratorDriverState.LoopScopeEnvironment, environment.Enclosing))
                    {
                        iteratorDriverState.CurrentIterationEnvironment ??= environment;
                        iteratorDriverState.LoopScopeEnvironment ??= environment.Enclosing;
                        iteratorDriverState.AssertIterationEnvironmentState(nameof(HandlePushEnvironment));
                    }
                }

                runner._programCounter = instruction.Next;
                returnValue = default;
                return InstructionResult.Continue;
            }

            JsEnvironment loopScope;
            JsEnvironment? previousIterEnv = null;

            if (isSubsequentIteration)
            {
                previousIterEnv = environment;
                loopScope = environment.Enclosing!;
            }
            else
            {
                loopScope = environment;
            }

            var allowPooling = instruction.AllowPooling;
            var description = instruction.PerIterationBindings.IsDefaultOrEmpty ? "loop-scope" : "scope";
            var newIterationEnv = allowPooling
                ? JsEnvironmentPool.Rent(loopScope, false, false, null, description, logger: runner._realmState.Logger)
                : new JsEnvironment(loopScope, false, false, null, description);
            if (!allowPooling)
            {
                newIterationEnv.MarkLeasedDebug();
            }

            if (instruction.ScopeId >= 0)
            {
                var slotCount = instruction.SlotCount >= 0 ? instruction.SlotCount : 0;
                newIterationEnv.InitializeSlots(slotCount, instruction.ScopeId);

                // Fast path: use pre-computed SlotNames array if available (avoids ImmutableDictionary iteration)
                if (!instruction.SlotNames.IsDefaultOrEmpty)
                {
                    newIterationEnv.SetSlotNames(instruction.SlotNames);
                }
                else if (!instruction.SlotMap.IsEmpty)
                {
                    newIterationEnv.SetSlotMap(instruction.SlotMap);
                }

                // Mark lexical bindings as uninitialized (TDZ) using SlotMap for O(log n) lookup
                // instead of O(n) linear search via FindSlotIndex
                if (instruction.LexicalBindings is { Count: > 0 } && !instruction.SlotMap.IsEmpty)
                {
                    foreach (var binding in instruction.LexicalBindings)
                    {
                        if (instruction.SlotMap.TryGetValue(binding, out var slotIndex))
                        {
                            newIterationEnv.SetSlotLexicalUninitialized(slotIndex);
                        }
                    }
                }
            }

            if (previousIterEnv != null && !instruction.PerIterationBindings.IsDefaultOrEmpty)
            {
                var useSlotCopy = newIterationEnv.HasSlots &&
                                  previousIterEnv.HasSlots &&
                                  !instruction.SlotMap.IsEmpty;

                if (useSlotCopy)
                {
                    foreach (var binding in instruction.PerIterationBindings)
                    {
                        if (instruction.SlotMap.TryGetValue(binding, out var slotIndex))
                        {
                            var value = previousIterEnv.GetSlotRef(slotIndex);
                            newIterationEnv.SetSlotDirect(slotIndex, value);
                        }
                    }
                }
                else
                {
                    foreach (var binding in instruction.PerIterationBindings)
                    {
                        if (previousIterEnv.TryGetJsValueWithConst(binding, out var value, out var isConst))
                        {
                            newIterationEnv.DefineJsValue(binding, value, isConst, isLexicalBinding: true);
                        }
                    }
                }

                if (allowPooling && !ReferenceEquals(previousIterEnv, runner.IteratorStateRef.ResumedWithEnvironment))
                {
                    JsEnvironmentPool.Return(previousIterEnv, runner._realmState.Logger);
                }
            }
            else if (!instruction.PerIterationBindings.IsDefaultOrEmpty)
            {
                foreach (var binding in instruction.PerIterationBindings)
                {
                    if (loopScope.TryGetJsValueWithConst(binding, out var value, out var isConst))
                    {
                        newIterationEnv.DefineJsValue(binding, value, isConst, isLexicalBinding: true);
                    }
                }
            }

            runner.IteratorStateRef.ResumedWithEnvironment = null;

            if (iteratorDriverState is not null)
            {
                // Only update if this is the iterator's own loop, not a nested for-let loop.
                // A nested loop would have a different loopScope (the outer loop's iteration env).
                if (iteratorDriverState.LoopScopeEnvironment is null ||
                    ReferenceEquals(iteratorDriverState.LoopScopeEnvironment, loopScope))
                {
                    iteratorDriverState.CurrentIterationEnvironment = newIterationEnv;
                    iteratorDriverState.LoopScopeEnvironment ??= loopScope;
                    iteratorDriverState.AssertIterationEnvironmentState(nameof(HandlePushEnvironment));
                }
            }

            // Eagerly populate flat slots for this scope (no dictionary lookup - mappings are on instruction)
            if (runner._flatSlots is not null && !instruction.FlatSlotMappings.IsDefaultOrEmpty)
            {
                runner.AssertFlatSlotMappings(instruction.ScopeId, instruction.FlatSlotMappings, newIterationEnv);
                foreach (var (slotIndex, flatSlotId) in instruction.FlatSlotMappings)
                {
                    runner._flatSlots[flatSlotId] = new JsVariable(newIterationEnv, slotIndex);
                }
            }

            runner._realmState.Logger?.LogInformation(
                "PushEnv: old.ScopeId={OldScope} new.ScopeId={NewScope} loopScope.ScopeId={LoopScope} parent={Parent}",
                environment.ScopeId,
                newIterationEnv.ScopeId,
                loopScope.ScopeId,
                newIterationEnv.Enclosing?.ScopeId);

            environment = newIterationEnv;
            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandlePopEnvironment(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext __,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<PopEnvironmentInstruction>(instr);
            var shouldPop = instruction.ScopeId >= 0
                ? environment.ScopeId == instruction.ScopeId
                : environment.Description is "scope" or "loop-scope" && environment.Enclosing != null;

            if (shouldPop)
            {
                var envToPop = environment;
                environment = environment.Enclosing!;

                // Only return to pool if the environment was rented from pool
                if (instruction.AllowPooling)
                {
                    JsEnvironmentPool.Return(envToPop, runner._realmState.Logger);
                }
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

    }
}
