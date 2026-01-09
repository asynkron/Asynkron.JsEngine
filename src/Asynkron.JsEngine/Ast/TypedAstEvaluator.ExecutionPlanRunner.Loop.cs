#region

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private JsValue ExecutePlan(ResumeMode mode, JsValue resumeValue)
        {
            if (_plan is null)
            {
                throw new InvalidOperationException("No generator plan available.");
            }

            JsEnvironment environment;
            EvaluationContext context;

            // Fast path for non-generator, non-async functions - skip all generator/async machinery
            if (!_isGenerator && !_isAsync)
            {
                environment = EnsureExecutionEnvironment();
                context = EnsureEvaluationContext();
            }
            else
            {
                // Full generator/async path with state machine support
                if (_state == GeneratorState.Executing)
                {
                    _state = GeneratorState.Completed;
                    _done = true;
                    _programCounter = -1;
                    TryCatchStateRef.TryStack.Clear();
                    YieldStateRef.ResumeContext.Clear();
                    var throwContext = _context ??= _realmState.CreateContext(
                        ScopeKind.Function,
                        DetermineGeneratorScopeMode());
                    throw StandardLibrary.ThrowTypeError("Generator is already executing", throwContext, _realmState);
                }

                var wasStart = _state == GeneratorState.Start;
                if (_done || _state == GeneratorState.Completed)
                {
                    _done = true;
                    return FinishExternalCompletion(mode, resumeValue);
                }

                if (mode is ResumeMode.Throw or ResumeMode.Return && wasStart)
                {
                    _state = GeneratorState.Completed;
                    _done = true;
                    return FinishExternalCompletion(mode, resumeValue);
                }

                _state = GeneratorState.Executing;
                PreparePendingResumeValue(mode, resumeValue, wasStart);

                environment = EnsureExecutionEnvironment();

                // Track the environment we resumed with (if resuming from suspend).
                // This prevents returning it to the pool while we're still using it.
                IteratorStateRef.ResumedWithEnvironment = wasStart ? null : environment;
                context = EnsureEvaluationContext();

                // If we're resuming from a yield that happened during AST evaluation
                // (via StatementInstruction), handle based on the resume mode.
                _realmState.Logger?.LogInformation(
                    "ExecutePlan resume check: wasStart={WasStart} mode={Mode} YieldStateRef.LastYieldSourceStart={Start}",
                    wasStart, mode, YieldStateRef.LastYieldSourceStart);

                if (!wasStart && YieldStateRef.LastYieldSourceStart >= 0)
                {
                    switch (mode)
                    {
                        case ResumeMode.Next:
                            // For next(), set up resume state so the yield expression returns the resume value
                            SetYieldResumeValue(environment, resumeValue, YieldStateRef.LastYieldSourceStart,
                                YieldStateRef.LastYieldSourceEnd);
                            break;
                        case ResumeMode.Return:
                            // For return(), close any active iterators and complete the generator.
                            // Don't re-evaluate the statement - just close and return.
                            _realmState.Logger?.LogInformation("ExecutePlan: early CompleteReturn for Return mode");
                            YieldStateRef.LastYieldSourceStart = -1;
                            YieldStateRef.LastYieldSourceEnd = -1;
                            return CompleteReturn(resumeValue);
                    }
                    // For Throw mode, we'll let the normal flow handle it via AsyncStateRef.PendingResumeKind

                    YieldStateRef.LastYieldSourceStart = -1;
                    YieldStateRef.LastYieldSourceEnd = -1;
                }

                // Restore active with-scopes when resuming
                // The _activeWithScopes stack contains the slots in reverse order (bottom to top)
                // We need to restore environments from bottom to top
                if (WithStateRef.ActiveWithScopes.Count > 0)
                {
                    var scopesToRestore = WithStateRef.ActiveWithScopes.ToArray();
                    // The array is in stack order (top first), so reverse to get bottom-to-top order
                    for (var i = scopesToRestore.Length - 1; i >= 0; i--)
                    {
                        var slot = scopesToRestore[i];
                        if (TryGetSymbolValueJsValue(environment, slot, out var storedEnvValue) &&
                            storedEnvValue.TryGetObject<JsEnvironment>(out var storedWithEnv))
                        {
                            environment = storedWithEnv;
                        }
                    }
                }

                // If we are resuming after a pending await, thread the resolved
                // value into the per-site await state so subsequent evaluations
                // of the AwaitExpression see the fulfilled value instead of the
                // original promise object.
                if (_isAsync && AsyncStateRef.PendingAwaitKey is { } awaitKey)
                {
                    var (kind, value) = ConsumeResumeValue();
                    var isThrow = kind == ResumePayloadKind.Throw;

                    // Store the resolved value (or thrown error) in AwaitState so
                    // EvaluateAwaitInGenerator can retrieve it when re-evaluated.
                    if (kind == ResumePayloadKind.Value || isThrow)
                    {
                        if (environment.TryGetObject<AwaitState>(awaitKey, out var state))
                        {
                            state.HasResult = true;
                            state.IsThrow = isThrow;
                            state.Result = value;
                            environment.AssignJsValue(awaitKey, JsValue.FromObjectUnsafe(state));
                        }
                        else
                        {
                            var newState = new AwaitState { HasResult = true, IsThrow = isThrow, Result = value };
                            if (environment.HasBinding(awaitKey))
                            {
                                environment.AssignJsValue(awaitKey, JsValue.FromObjectUnsafe(newState));
                            }
                            else
                            {
                                environment.DefineJsValue(awaitKey, JsValue.FromObjectUnsafe(newState));
                            }
                        }
                    }

                    AsyncStateRef.PendingAwaitKey = null;
                }
            }

            return ExecuteInstructionLoop(ref environment, context);
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private JsValue ExecuteInstructionLoop(ref JsEnvironment environment, EvaluationContext context)
        {
            // Cache debug mode check outside the hot loop - avoid virtual property access per iteration
            var debugMode = _realmState.Options.DebugMode;
            var instructions = _plan!.Instructions;
            var instructionsLength = instructions.Length;

            // Allocate flat slots array for O(1) variable access if this plan uses flat slots.
            // Each JsVariable will be populated when its scope is entered via PushEnvironment.
            var flatSlotCount = _plan.FlatSlotCount;
            if (flatSlotCount > 0 && _flatSlots is null)
            {
                _flatSlots = new JsVariable[flatSlotCount];
            }

            // Get underlying array from ImmutableArray and reference to start - enables bounds-check-free access
            var instructionsArray = ImmutableCollectionsMarshal.AsArray(instructions)!;
            ref var instructionsRef = ref MemoryMarshal.GetArrayDataReference(instructionsArray);

            bool continueAfterCatch;
            do
            {
                continueAfterCatch = false;
                try
                {
                    while ((uint)_programCounter < (uint)instructionsLength)
                    {
                        // Check if HandleAbruptCompletion restored the environment (e.g., jumping to catch handler)
                        // This ensures block-scoped bindings from inside the try are no longer visible.
                        // NOTE: Must check _tryCatchState directly (not cached hasTryCatchState) because
                        // TryCatchState is lazily allocated when EnterTry executes inside the loop.
                        if (_tryCatchState?.RestoredEnvironmentFromTry is { } restored)
                        {
                            environment = restored;
                            _tryCatchState.RestoredEnvironmentFromTry = null;
                        }

                        _currentInstructionIndex = _programCounter;
                        // Use profiling wrapper to measure instruction fetch cost
                        var instruction = ProfileFetchInstruction(ref instructionsRef, _programCounter);
                        var instructionKind = instruction.Kind;

                        // Trace instruction execution when debug logging is enabled
                        if (debugMode)
                        {
                            _realmState.Logger?.LogTrace(
                                "[IR:{PC,3}] {Instruction}",
                                _programCounter,
                                ExecutionPlanPrinter.FormatInstruction(instruction));
                        }

                        // Detailed IR execution trace with environment depth
#pragma warning disable CS0162 // Unreachable code detected (TraceIrExecution is compile-time constant)
                        if (JsEngineConstants.TraceIrExecution && _realmState.Logger is not null)
                        {
                            ExecutionPlanPrinter.TraceInstruction(
                                _realmState.Logger,
                                _programCounter,
                                instruction,
                                environment.Depth,
                                environment.ScopeId,
                                environment.GetHashCode()
                            );
                        }
#pragma warning restore CS0162

                        // ═══════════════════════════════════════════════════════════════════════════
                        // FAST PATH: Handle the hottest instructions before switch dispatch
                        // For a 1M iteration loop, this saves millions of switch table lookups
                        // ═══════════════════════════════════════════════════════════════════════════

                        // Jump is the simplest - just update program counter
                        if (instructionKind == InstructionKind.Jump)
                        {
                            _programCounter = ProfileHandleJump(Unsafe.As<JumpInstruction>(instruction));
                            continue;
                        }

                        // Branch is hot - handle before switch dispatch
                        if (instructionKind == InstructionKind.Branch)
                        {
                            var result = HandleBranchFastPath(Unsafe.As<BranchInstruction>(instruction), environment, context, out var returnValue);
                            if (result == InstructionResult.Return)
                            {
                                return returnValue;
                            }

                            continue;
                        }

                        var loopResult = InstructionHandlers[(int)instructionKind](this, instruction, ref environment, context, out var loopReturnValue);
                        if (loopResult == InstructionResult.Return)
                        {
                            return loopReturnValue;
                        }
                    }
                }
                catch (ThrowSignal signal)
                {
                    // A ThrowSignal was thrown from code evaluation (e.g., from EvaluateAwaitInGenerator
                    // when resuming after a rejected promise). Route it through HandleAbruptCompletion
                    // to check if there's a JS catch block that can handle it.

                    // Clear any stale throw state from context before handling - this ensures
                    // finally blocks don't see the stale throw state
                    if (context.IsThrow)
                    {
                        context.Clear();
                    }

                    if (HandleAbruptCompletion(AbruptKind.Throw, signal.ThrownValue))
                    {
                        // A catch block will handle this - continue execution from the catch handler
                        if (_programCounter == _currentInstructionIndex)
                        {
                            // When already inside a finally block, ensure forward progress
                            // instead of re-executing the same instruction repeatedly.
                            _programCounter = _currentInstructionIndex + 1;
                        }

                        continueAfterCatch = true;
                        continue;
                    }

                    // No catch block - mark as completed and re-throw
                    _state = GeneratorState.Completed;
                    _done = true;
                    _programCounter = -1;
                    TryCatchStateRef.TryStack.Clear();
                    YieldStateRef.ResumeContext.Clear();
                    throw;
                }
                catch
                {
                    _state = GeneratorState.Completed;
                    _done = true;
                    _programCounter = -1;
                    TryCatchStateRef.TryStack.Clear();
                    YieldStateRef.ResumeContext.Clear();
                    throw;
                }
            } while (continueAfterCatch);

            _realmState.Logger?.LogInformation(
                "[ASYNC-ITER-DEBUG] ExecuteInstructionLoop exiting normally: PC={PC}, instructionsLength={Len}",
                _programCounter, instructionsLength);
            _state = GeneratorState.Completed;
            _done = true;
            TryCatchStateRef.TryStack.Clear();
            return CreateIteratorResult(JsValue.Undefined, true);
        }
    }
}
