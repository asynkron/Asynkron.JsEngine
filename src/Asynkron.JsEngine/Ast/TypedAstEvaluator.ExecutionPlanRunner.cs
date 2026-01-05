#region

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Executes an IR execution plan (compiled from AST).
    /// </summary>
    /// <remarks>
    /// ## Script Completion Value (_scriptCompletionValue)
    ///
    /// In script/eval mode, we track the completion value per ES spec.
    /// The completion value is what eval() returns.
    ///
    /// ### Sentinel Pattern
    ///
    /// We use JsValue.Unit as a sentinel meaning "no value produced yet".
    ///
    /// - Script start: _scriptCompletionValue = Unit
    /// - Expression statement (e.g., 5+5;): _scriptCompletionValue = 10
    /// - At script end: if still Unit → return undefined, else return the value
    ///
    /// ### Loops, Try, Catch
    ///
    /// These constructs have their own internal completion value per ES spec.
    /// They all follow the same pattern:
    ///
    /// 1. On ENTER: _scriptCompletionValue = Unit (reset to sentinel)
    /// 2. Body executes: may or may not update _scriptCompletionValue
    /// 3. On EXIT: if (_scriptCompletionValue.IsUnit) → set to undefined
    ///
    /// This ensures:
    /// - eval('7; for (...) {}') returns undefined (not 7)
    /// - eval('7; for (...) { 9; }') returns 9
    /// - eval('for (...) { 9; break; }') returns 9 (break doesn't touch completion value)
    ///
    /// ### Finally (Special Case)
    ///
    /// Finally is different: its completion value is DISCARDED if it completes normally.
    /// The try/catch completion value is restored.
    ///
    /// - eval('try { 7; } finally { 8; }') returns 7 (not 8)
    ///
    /// Implementation:
    /// 1. When entering finally: frame.SavedCompletionValue = _scriptCompletionValue
    /// 2. Finally body executes (its value is irrelevant if normal completion)
    /// 3. On normal exit: _scriptCompletionValue = SavedCompletionValue.IsUnit ? undefined : SavedCompletionValue
    /// 4. On abrupt exit (return/throw): abrupt completion takes over, completion value doesn't matter
    /// </remarks>
    private sealed partial class ExecutionPlanRunner
    {
        // Core fields - always needed
        private readonly bool _allowIdentifierCache;
        private readonly IReadOnlyList<JsValue> _arguments;
        private readonly IJsCallable _callable;
        private readonly ImmutableArray<PrivateNameScope> _capturedPrivateNameScopes;
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly bool _hasFunctionNameEnvironment;
        private readonly IJsObjectLike? _homeObject;
        private readonly bool _isAsync;
        private readonly bool _isGenerator;
        private readonly bool _isScriptMode;
        private readonly bool _isStrict;
        private readonly JsEnvironment? _lexicalThisEnvironment;
        private readonly JsValue _newTarget;
        private readonly ExecutionPlan? _plan;
        private readonly PrivateNameScope? _privateNameScope;
        private readonly RealmState _realmState;
        private readonly JsValue _thisValue;
        private EvaluationContext? _context;
        private int _currentInstructionIndex;
        private bool _done;
        private JsEnvironment? _executionEnvironment;
        private bool _privateScopesApplied;
        private int _programCounter;
        private JsValue _scriptCompletionValue = JsValue.Unit;
        private GeneratorState _state = GeneratorState.Start;
        private bool _rootScopeLogged;

        // Lazy state objects - only allocated when needed
        // TryCatchState needs explicit backing field for hot-path null check without allocation
        private TryCatchState? _tryCatchState;

        // Flat slots array for O(1) variable access within this execution plan.
        // Indexed by FlatSlotId stamped on IdentifierExpression nodes.
        // Each JsVariable holds a reference to the environment and slot, providing direct read/write.
        private JsVariable[]? _flatSlots;

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
            var handlers = new InstructionHandler[33];
            handlers[(int)InstructionKind.Statement] = HandleStatement;
            handlers[(int)InstructionKind.Throw] = HandleThrow;
            handlers[(int)InstructionKind.EvaluateAndDiscard] = HandleEvaluateAndDiscard;
            handlers[(int)InstructionKind.BinaryOp] = HandleBinaryOp;
            handlers[(int)InstructionKind.IncrementSlot] = HandleIncrementSlot;
            handlers[(int)InstructionKind.CompoundAssignmentSlot] = HandleCompoundAssignmentSlot;
            handlers[(int)InstructionKind.FunctionDeclaration] = HandleFunctionDeclaration;
            handlers[(int)InstructionKind.ClassDeclaration] = HandleClassDeclaration;
            handlers[(int)InstructionKind.SimpleVariableDeclaration] = HandleSimpleVariableDeclaration;
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
            return handlers;
        }



        private static InstructionResult DispatchExpression(ExecutionPlanRunner runner, ExecutionInstruction instr, ref JsEnvironment env, EvaluationContext ctx, out JsValue ret)
            => throw new InvalidOperationException("Expression instruction should not be dispatched via handler table");

        // Lazy accessors
        private AsyncState AsyncStateRef => field ??= new AsyncState();
        private YieldState YieldStateRef => field ??= new YieldState();
        private IteratorState IteratorStateRef => field ??= new IteratorState();
        private TryCatchState TryCatchStateRef => _tryCatchState ??= new TryCatchState();
        private BreakableState BreakableStateRef => field ??= new BreakableState();
        private WithState WithStateRef => field ??= new WithState();

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

            // Cache try-catch state check - avoid repeated null checks in hot loop
            var hasTryCatchState = _tryCatchState is not null;

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
                        // Only check when TryCatchState has been allocated.
                        if (hasTryCatchState && _tryCatchState!.RestoredEnvironmentFromTry is { } restored)
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
                            if (result == InstructionResult.Return) return returnValue;
                            continue;
                        }

                        var loopResult = InstructionHandlers[(int)instructionKind](this, instruction, ref environment, context, out var loopReturnValue);
                        if (loopResult == InstructionResult.Return) return loopReturnValue;
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

                    if (HandleAbruptCompletion(AbruptKind.Throw, signal.ThrownValue, environment))
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

            _state = GeneratorState.Completed;
            _done = true;
            TryCatchStateRef.TryStack.Clear();
            return CreateIteratorResult(JsValue.Undefined, true);
        }


        internal JsValue EvaluateAwaitInGenerator(AwaitExpression expression, JsEnvironment environment,
            EvaluationContext context)
        {
            // When not executing under async-aware stepping, fall back to the
            // legacy blocking helper so synchronous generators remain usable.
            if (!AsyncStateRef.AsyncStepMode)
            {
                // Keep as JsValue to avoid boxing round trips
                var awaitedValueSync = expression.Expression.EvaluateExpression(environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return awaitedValueSync;
                }

                // awaitedValueSync is already JsValue
                if (!TryAwaitPromise(awaitedValueSync, context, out var resolvedSync))
                {
                    return resolvedSync;
                }

                return resolvedSync;
            }

            // Async-aware mode: use per-site await state so we don't re-run
            // side-effecting expressions after the promise has resolved.
            var awaitKey = expression.GetAwaitStateKey();
            if (awaitKey is not null &&
                environment.TryGetObject<AwaitState>(awaitKey, out var state) &&
                state.HasResult)
            {
                // Await has already completed; reuse the resolved value once
                // for this resume, then clear the flag so future iterations
                // (e.g. in loops) see a fresh await.
                var result = state.Result;
                var isThrow = state.IsThrow;
                environment.AssignJsValue(awaitKey, JsValue.FromObjectUnsafe(new AwaitState()));
                AsyncStateRef.PendingAwaitKey = null;

                // If the await was rejected, throw at this point so the
                // generator's try-catch can handle it.
                if (isThrow)
                {
                    throw new ThrowSignal(result);
                }

                return result;
            }

            // Keep as JsValue to avoid boxing round trips
            var awaitedValue = expression.Expression.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return awaitedValue;
            }

            if (awaitKey is not null)
            {
                var existingState = JsValue.FromObjectUnsafe(new AwaitState());

                if (environment.HasBinding(awaitKey))
                {
                    environment.AssignJsValue(awaitKey, existingState);
                }
                else
                {
                    environment.DefineJsValue(awaitKey, existingState);
                }
            }

            // Async-aware mode: surface promise-like values as pending steps
            // so AsyncGeneratorInvoker can resume via the event queue.
            // awaitedValue is already JsValue
            if (TryResolvePromiseOrYield(awaitedValue, context, out var resolved))
            {
                return resolved;
            }

            if (!AsyncStateRef.PendingPromise.TryGetPropertyAccessor(out _) || awaitKey is null)
            {
                return resolved;
            }

            // Remember which await site is pending so we can stash the
            // resolved value on resume.
            AsyncStateRef.PendingAwaitKey = awaitKey;
            _state = GeneratorState.Suspended;
            _programCounter = _currentInstructionIndex;
            context.SetPendingAwait();
            return JsValue.Undefined;

            // If TryResolvePromiseOrYield reported an error via the context,
            // let the caller observe the pending throw/return.
        }

        private bool TryResolvePromiseOrYield(JsValue candidate, EvaluationContext context, out JsValue resolvedValue)
        {
            var pendingPromise = AsyncStateRef.PendingPromise;
            var result = AwaitScheduler.TryResolvePromiseOrYield(candidate, AsyncStateRef.AsyncStepMode,
                ref pendingPromise,
                context, out var resolvedObj);
            AsyncStateRef.PendingPromise = pendingPromise;
            // resolvedObj is already JsValue from the scheduler
            resolvedValue = resolvedObj;
            return result;
        }

        private bool TryHandlePendingAwait(EvaluationContext context, out JsValue result,
            JsEnvironment? currentEnvironment = null)
        {
            if (!context.IsPendingAwait)
            {
                result = JsValue.Undefined;
                return false;
            }

            context.Clear();
            _state = GeneratorState.Suspended;

            // Save the current environment so that when the async function resumes after await,
            // it uses the correct per-iteration environment (for loops with let bindings).
            // This is critical for `continue` to work correctly in async loops.
            if (currentEnvironment != null)
            {
                _executionEnvironment = currentEnvironment;
            }

            // In async-step mode, surface the pending promise directly to the
            // caller without allocating an iterator result object.
            result = AsyncStateRef.AsyncStepMode
                ? JsValue.Undefined
                : CreateIteratorResult(JsValue.Undefined, false);
            return true;
        }

        /// <summary>
        /// Handles throw state from context by attempting abrupt completion handling.
        /// Returns true if the throw was handled and the caller should continue the loop.
        /// Throws ThrowSignal if the throw could not be handled.
        /// </summary>
        private bool TryHandleContextThrow(EvaluationContext context, JsEnvironment environment)
        {
            if (!context.IsThrow) return false;

            var thrownValue = context.FlowValue;
            context.Clear();
            if (HandleAbruptCompletion(AbruptKind.Throw, thrownValue, environment))
            {
                return true;
            }

            TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(thrownValue);
        }

        /// <summary>
        /// Result of HandleContextSignals indicating what action the caller should take.
        /// </summary>
        private enum SignalAction { None, Continue, Return }

        /// <summary>
        /// Handles async await, throw, return, and yield signals from context.
        /// Returns the action the caller should take and any result value.
        /// For Return action, the result should be returned from the caller.
        /// For Continue action, the caller should continue the loop.
        /// For None action, the caller should fall through to normal processing.
        /// May throw ThrowSignal if a throw cannot be handled.
        /// </summary>
        private (SignalAction action, JsValue result) HandleContextSignals(
            EvaluationContext context,
            JsEnvironment environment,
            int nextInstructionIndex)
        {
            if (_isAsync && TryHandlePendingAwait(context, out var pendingResult, environment))
            {
                return (SignalAction.Return, pendingResult);
            }

            if (context.IsThrow)
            {
                var thrown = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                {
                    if (_programCounter == _currentInstructionIndex)
                    {
                        _programCounter = nextInstructionIndex;
                    }

                    return (SignalAction.Continue, default);
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(thrown);
            }

            if (context.IsReturn)
            {
                var returnSignalValue = context.FlowValue;
                context.ClearReturn();
                if (!HandleAbruptCompletion(AbruptKind.Return, returnSignalValue, environment))
                {
                    return (SignalAction.Return, CompleteReturn(returnSignalValue));
                }

                if (_programCounter == _currentInstructionIndex)
                {
                    _programCounter = nextInstructionIndex;
                }

                return (SignalAction.Continue, default);
            }

            if (context.IsYield)
            {
                var yieldedSignalValue = context.FlowValue;
                var iteratorResultObject = (context.CurrentSignal as YieldCompletionSignal)
                    ?.IteratorResultObject;
                RecordYield(context, environment);
                context.Clear();
                _state = GeneratorState.Suspended;
                var result = iteratorResultObject is not null
                    ? new JsValue(JsValueKind.Object, 0.0, iteratorResultObject)
                    : CreateIteratorResult(yieldedSignalValue, false);
                return (SignalAction.Return, result);
            }

            return (SignalAction.None, default);
        }

        /// <summary>
        /// Handles the common logic when TryResolvePromiseOrYield returns false for iterator values.
        /// Manages async step mode suspension, throw handling, and environment restoration.
        /// Returns true if the caller should return the suspension result; false if caller should continue loop.
        /// </summary>
        private bool TryHandleAwaitSuspension(
            IteratorDriverState driverState,
            JsVariable iterVar,
            IteratorMoveNextInstruction instruction,
            ref JsEnvironment environment,
            EvaluationContext context,
            int iteratorIndex,
            out JsValue suspendResult)
        {
            if (AsyncStateRef.AsyncStepMode &&
                AsyncStateRef.PendingPromise.TryGetPropertyAccessor(out _))
            {
                driverState.AwaitingValue = true;
                var iterState = driverState.AsJsValue;
                if (iterVar.IsValid)
                {
                    iterVar.Write(iterState);
                }
                else
                {
                    StoreValueBySlot(environment, instruction.IteratorSlot,
                        instruction.IteratorSlotIndex, iterState);
                }

                _executionEnvironment = environment;
                _state = GeneratorState.Suspended;
                _programCounter = iteratorIndex;
                suspendResult = CreateIteratorResult(JsValue.Undefined, false);
                return true;
            }

            if (context.IsThrow)
            {
                var thrownValue = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, thrownValue, environment))
                {
                    suspendResult = JsValue.Undefined;
                    return false;
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(thrownValue);
            }

            // Restore environment to enclosing scope
            if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv)
            {
                environment = enclosingEnv;
            }

            IteratorStateRef.CurrentDriverState = null;
            _programCounter = instruction.BreakIndex;
            suspendResult = JsValue.Undefined;
            return false;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // PROFILING DIAGNOSTICS: NoInlining methods to isolate hot path costs
        // These show up separately in profiler output for analysis
        // Change to AggressiveInlining after profiling is complete
        // ═══════════════════════════════════════════════════════════════════════════

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private bool ProfileReadOperand(
            JsEnvironment environment,
            EvaluationContext context,
            ExpressionNode expr,
            out JsValue value)
        {
            if (expr is LiteralExpression lit)
            {
                value = lit.Value;
                return true;
            }

            // Fast path: use flat slot for O(1) identifier read
            if (expr is IdentifierExpression { FlatSlotId: >= 0 } id && _flatSlots is not null)
            {
                value = _flatSlots[id.FlatSlotId].Read();
                return true;
            }

            // Fallback: slot-based read
            if (expr is IdentifierExpression { SlotIndex: >= 0, ScopeId: >= 0 } slotId)
            {
                return environment.TryReadIdentifierWithSlot(slotId, context, out value);
            }

            value = default;
            return false;
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static JsValue ProfileBranchCompare(
            BinaryOperator op,
            JsValue leftVal,
            JsValue rightVal,
            EvaluationContext context)
        {
            return op switch
            {
                BinaryOperator.LessThan => LessThanValue(leftVal, rightVal, context),
                BinaryOperator.LessThanOrEqual => LessThanOrEqualValue(leftVal, rightVal, context),
                BinaryOperator.GreaterThan => GreaterThanValue(leftVal, rightVal, context),
                _ => GreaterThanOrEqualValue(leftVal, rightVal, context)
            };
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static int ProfileHandleJump(JumpInstruction jumpInstruction)
        {
            return jumpInstruction.TargetIndex;
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static JsValue ProfileEvaluateExpression(
            ExpressionNode expression,
            JsEnvironment environment,
            EvaluationContext context)
        {
            return expression.EvaluateExpression(environment, context);
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static JsValue ProfileEvaluateStatement(
            StatementNode statement,
            JsEnvironment environment,
            EvaluationContext context)
        {
            return statement.EvaluateStatementJsValue(environment, context);
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static JsValue ProfileApplyBinaryOperator(
            BinaryOperator op,
            JsValue left,
            JsValue right,
            EvaluationContext context)
        {
            return ApplyBinaryOperator(op, left, right, context);
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static JsValue ProfileGetIdentifier(
            JsEnvironment environment,
            Symbol symbol,
            EvaluationContext context)
        {
            return environment.GetIdentifierJsValueDirect(symbol, context);
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static void ProfileAssignJsValue(
            JsEnvironment environment,
            Symbol symbol,
            JsValue value)
        {
            environment.AssignJsValue(symbol, value);
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static ExecutionInstruction ProfileFetchInstruction(
            ref ExecutionInstruction instructionsRef,
            int programCounter)
        {
            return Unsafe.Add(ref instructionsRef, programCounter);
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static int ProfileBranchDecision(bool isTruthy, int consequent, int alternate)
        {
            return isTruthy ? consequent : alternate;
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static JsValue ProfileIncrementMath(JsValue currentValue, bool isIncrement)
        {
            // Fast path for numbers (most common case)
            if (currentValue.Kind == JsValueKind.Number)
            {
                var numValue = currentValue.NumberValue;
                return isIncrement ? numValue + 1.0 : numValue - 1.0;
            }
            // BigInt and other cases - return sentinel to indicate slow path needed
            return JsValue.Undefined;
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static JsValue ProfileCompoundAdd(JsValue left, JsValue right)
        {
            // Fast path for number + number (most common in loops)
            if (left.Kind == JsValueKind.Number && right.Kind == JsValueKind.Number)
            {
                return left.NumberValue + right.NumberValue;
            }
            // Return sentinel to indicate slow path needed
            return JsValue.Undefined;
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

        // ═══════════════════════════════════════════════════════════════════════════
        // INSTRUCTION HANDLERS: NoInlining methods for profiling visibility
        // Each handler processes one instruction kind and returns control flow action
        // Change to AggressiveInlining after profiling is complete
        // ═══════════════════════════════════════════════════════════════════════════

        private static InstructionResult HandleStatement(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<StatementInstruction>(instr);
            var stmtResult = ProfileEvaluateStatement(instruction.Statement, environment, context);

            if (runner._isScriptMode)
            {
                if (!stmtResult.IsUnit)
                {
                    runner._scriptCompletionValue = stmtResult;
                }
                else if (ShouldResetScriptCompletion(instruction.Statement))
                {
                    runner._scriptCompletionValue = JsValue.Undefined;
                }
            }

            var (signalAction, signalResult) = runner.HandleContextSignals(context, environment, instruction.Next);
            switch (signalAction)
            {
                case SignalAction.Return:
                    returnValue = signalResult;
                    return InstructionResult.Return;
                case SignalAction.Continue:
                    returnValue = default;
                    return InstructionResult.Continue;
            }

            if (context.IsBreak || context.IsContinue)
            {
                if (runner._isScriptMode)
                {
                    runner._scriptCompletionValue = JsValue.Undefined;
                }

                var isBreak = context.IsBreak;
                var label = (context.CurrentSignal as BreakCompletionSignal)?.Label
                            ?? (context.CurrentSignal as ContinueCompletionSignal)?.Label;
                context.Clear();

                var target = runner.FindBreakableTarget(label, isBreak);
                if (target >= 0)
                {
                    runner._programCounter = target;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                throw new InvalidOperationException(
                    $"No loop target found for {(isBreak ? "break" : "continue")}{(label is not null ? $" {label.Name}" : "")}");
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleEvaluateAndDiscard(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<EvaluateAndDiscardInstruction>(instr);
            var evaluatedValue = ProfileEvaluateExpression(instruction.Expression, environment, context);

            if (runner._isScriptMode && !instruction.SuppressCompletionValue)
            {
                runner._scriptCompletionValue = evaluatedValue;
            }

            var (evalSignalAction, evalSignalResult) = runner.HandleContextSignals(context, environment, instruction.Next);
            switch (evalSignalAction)
            {
                case SignalAction.Return:
                    returnValue = evalSignalResult;
                    return InstructionResult.Return;
                case SignalAction.Continue:
                    returnValue = default;
                    return InstructionResult.Continue;
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleBinaryOp(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<BinaryOpInstruction>(instr);
            var binLeft = instruction.Left.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingBinLeftResult, environment))
                {
                    returnValue = pendingBinLeftResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var binThrown = context.FlowValue;
                    context.Clear();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, binThrown, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(binThrown);
                }
            }

            var binRight = instruction.Right.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingBinRightResult, environment))
                {
                    returnValue = pendingBinRightResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var binThrown = context.FlowValue;
                    context.Clear();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, binThrown, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(binThrown);
                }
            }

            var binResult = ApplyBinaryOperator(instruction.Operator, binLeft, binRight, context);

            if (instruction.ResultSlot is not null)
            {
                environment.AssignJsValue(instruction.ResultSlot, binResult);
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static InstructionResult HandleIncrementSlot(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<IncrementSlotInstruction>(instr);
            var flatSlotId = instruction.FlatSlotId;

            // Super-fast path: flat slot with number value (covers most loop counters)
            if (flatSlotId >= 0)
            {
                ref var targetVar = ref runner._flatSlots![flatSlotId];

                // Check for const assignment - must throw TypeError
                if (targetVar.IsConst)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError(
                        $"Assignment to constant variable '{instruction.TargetSymbol.Name}'.",
                        realm: runner._realmState));
                }

                var currentValue = targetVar.Read();

                if (currentValue.Kind == JsValueKind.Number)
                {
                    var numValue = currentValue.NumberValue;
                    var newValue = instruction.IsIncrement ? numValue + 1.0 : numValue - 1.0;
                    targetVar.Write(newValue);
                    runner._programCounter = instruction.Next;
                    returnValue = default;
                    return InstructionResult.Continue;
                }
            }

            // Delegate to slow path for non-number cases
            return HandleIncrementSlotSlow(runner, instruction, flatSlotId, ref environment, context, out returnValue);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleIncrementSlotSlow(
            ExecutionPlanRunner runner,
            IncrementSlotInstruction instruction,
            int flatSlotId,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            // Regular path: use ref ternary for variable access
            JsValue incCurrentValue;
            ref var variable = ref (flatSlotId >= 0 && runner._flatSlots is not null
                ? ref runner._flatSlots[flatSlotId]
                : ref Unsafe.NullRef<JsVariable>());
            var useFlatSlot = !Unsafe.IsNullRef(ref variable) && variable.IsValid;

            // Check for const assignment - must throw TypeError
            if (useFlatSlot && variable.IsConst)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    $"Assignment to constant variable '{instruction.TargetSymbol.Name}'.",
                    realm: runner._realmState));
            }

            if (useFlatSlot)
            {
                incCurrentValue = variable.Read();
            }
            else
            {
                incCurrentValue = ProfileGetIdentifier(environment, instruction.TargetSymbol, context);
            }

            if (context.IsThrow)
            {
                var incThrown = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, incThrown, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                runner.TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(incThrown);
            }

            JsValue incNewJsValue;
            JsValue incOldNumericValue;

            var fastResult = ProfileIncrementMath(incCurrentValue, instruction.IsIncrement);
            if (!fastResult.IsUndefined)
            {
                incNewJsValue = fastResult;
                incOldNumericValue = incCurrentValue;
            }
            else if (incCurrentValue.IsBigInt)
            {
                var bigInt = (JsBigInt)incCurrentValue.ObjectValue!;
                incOldNumericValue = incCurrentValue;
                var incNewBigInt = instruction.IsIncrement
                    ? bigInt.Value + 1
                    : bigInt.Value - 1;
                incNewJsValue = new JsBigInt(incNewBigInt);
            }
            else
            {
                var numericJsValue = ToNumericValue(incCurrentValue, context);
                if (context.ShouldStopEvaluation)
                {
                    var incFlowThrown = context.FlowValue;
                    context.Clear();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, incFlowThrown, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(incFlowThrown);
                }

                if (numericJsValue.IsBigInt)
                {
                    var bigInt = (JsBigInt)numericJsValue.ObjectValue!;
                    incOldNumericValue = numericJsValue;
                    var incNewBigInt = instruction.IsIncrement
                        ? bigInt.Value + 1
                        : bigInt.Value - 1;
                    incNewJsValue = new JsBigInt(incNewBigInt);
                }
                else
                {
                    var incNumValue = numericJsValue.NumberValue;
                    incOldNumericValue = JsValueCache.GetNumberJsValue(incNumValue);
                    var incNewValue = instruction.IsIncrement
                        ? incNumValue + 1.0
                        : incNumValue - 1.0;
                    incNewJsValue = JsValueCache.GetNumberJsValue(incNewValue);
                }
            }

            // Fast path: use flat slot for O(1) write when available
            if (useFlatSlot)
            {
                variable.Write(incNewJsValue);
            }
            else
            {
                ProfileAssignJsValue(environment, instruction.TargetSymbol, incNewJsValue);
            }

            if (runner._isScriptMode && !instruction.SuppressCompletionValue)
            {
                runner._scriptCompletionValue = instruction.IsPrefix ? incNewJsValue : incOldNumericValue;
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static InstructionResult HandleCompoundAssignmentSlot(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<CompoundAssignmentSlotInstruction>(instr);
            var flatSlotId = instruction.FlatSlotId;
            var rhsFlatSlotId = instruction.RhsFlatSlotId;

            // Super-fast path: both operands use flat slots, operator is Add, both are numbers
            // This covers the common loop case like: sum = sum + prev
            if (flatSlotId >= 0 &&
                rhsFlatSlotId >= 0 &&
                instruction.Operator == BinaryOperator.Add)
            {
                ref var targetVar = ref runner._flatSlots![flatSlotId];

                // Check for const assignment - must throw TypeError
                if (targetVar.IsConst)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError(
                        $"Assignment to constant variable '{instruction.TargetSymbol.Name}'.",
                        realm: runner._realmState));
                }

                var leftValue = targetVar.Read();
                var rightValue = runner._flatSlots[rhsFlatSlotId].Read();

                if (leftValue.Kind == JsValueKind.Number && rightValue.Kind == JsValueKind.Number)
                {
                    var result = leftValue.NumberValue + rightValue.NumberValue;
                    targetVar.Write(result);
                    runner._programCounter = instruction.Next;
                    returnValue = default;
                    return InstructionResult.Continue;
                }
            }

            // Delegate to slow path for non-fast cases
            return HandleCompoundAssignmentSlotSlow(runner, instruction, flatSlotId, ref environment, context, out returnValue);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleCompoundAssignmentSlotSlow(
            ExecutionPlanRunner runner,
            CompoundAssignmentSlotInstruction instruction,
            int flatSlotId,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            // Regular path: use ref ternary for variable access
            JsValue compCurrentValue;
            ref var variable = ref (flatSlotId >= 0 && runner._flatSlots is not null
                ? ref runner._flatSlots[flatSlotId]
                : ref Unsafe.NullRef<JsVariable>());
            var useFlatSlot = !Unsafe.IsNullRef(ref variable) && variable.IsValid;

            // Check for const assignment - must throw TypeError
            if (useFlatSlot && variable.IsConst)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    $"Assignment to constant variable '{instruction.TargetSymbol.Name}'.",
                    realm: runner._realmState));
            }

            if (useFlatSlot)
            {
                compCurrentValue = variable.Read();
            }
            else
            {
                compCurrentValue = ProfileGetIdentifier(environment, instruction.TargetSymbol, context);
            }

            if (context.IsThrow)
            {
                var compThrown = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, compThrown, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                runner.TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(compThrown);
            }

            JsValue compRhsValue;
            switch (instruction.RhsExpression)
            {
                case LiteralExpression { Value: var literalValue }:
                    compRhsValue = literalValue;
                    break;
                case IdentifierExpression { FlatSlotId: >= 0 } rhsIdent when runner._flatSlots is not null:
                    // Fast path: use flat slot for O(1) RHS read
                    compRhsValue = runner._flatSlots[rhsIdent.FlatSlotId].Read();
                    break;
                case IdentifierExpression { SlotIndex: >= 0, ScopeId: >= 0 } rhsIdent:
                    if (environment.TryReadIdentifierWithSlot(rhsIdent, context, out compRhsValue))
                    {
                    }
                    else
                    {
                        compRhsValue = rhsIdent.EvaluateExpression(environment, context);
                    }
                    break;
                default:
                    compRhsValue = instruction.RhsExpression.EvaluateExpression(environment, context);
                    break;
            }

            if (context.ShouldStopEvaluation)
            {
                if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingCompResult, environment))
                {
                    returnValue = pendingCompResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var compRhsThrown = context.FlowValue;
                    context.Clear();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, compRhsThrown, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(compRhsThrown);
                }
            }

            JsValue compResult;
            if (instruction.Operator == BinaryOperator.Add)
            {
                var fastAdd = ProfileCompoundAdd(compCurrentValue, compRhsValue);
                compResult = !fastAdd.IsUndefined
                    ? fastAdd
                    : ProfileApplyBinaryOperator(instruction.Operator, compCurrentValue, compRhsValue, context);
            }
            else
            {
                compResult = ProfileApplyBinaryOperator(instruction.Operator, compCurrentValue, compRhsValue, context);
            }

            // Fast path: use flat slot for O(1) write when available
            if (useFlatSlot)
            {
                variable.Write(compResult);
            }
            else
            {
                ProfileAssignJsValue(environment, instruction.TargetSymbol, compResult);
            }

            if (runner._isScriptMode && !instruction.SuppressCompletionValue)
            {
                runner._scriptCompletionValue = compResult;
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleBreakableEnter(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext ctx,
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
            ref JsEnvironment environment,
            EvaluationContext ctx,
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

        private static InstructionResult HandleSetCompletionValue(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext ctx,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<SetCompletionValueInstruction>(instr);
            if (runner._isScriptMode)
            {
                runner._scriptCompletionValue = JsValue.Undefined;
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
        private static InstructionResult HandleBreak(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext ctx,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<BreakInstruction>(instr);
            if (runner.HandleAbruptCompletion(AbruptKind.Break, instruction.TargetIndex, environment))
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

            if (instruction.TargetScopeId >= 0)
            {
                var targetScopeId = instruction.TargetScopeId;
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

            runner._programCounter = instruction.TargetIndex;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleContinue(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext ctx,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ContinueInstruction>(instr);
            if (runner.HandleAbruptCompletion(AbruptKind.Continue, instruction.TargetIndex, environment))
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

            if (instruction.TargetScopeId >= 0)
            {
                var targetScopeId = instruction.TargetScopeId;
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

            runner._programCounter = instruction.TargetIndex;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleReturn(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ReturnInstruction>(instr);
            var returnVal = instruction.ReturnExpression?.EvaluateExpression(environment, context) ?? JsValue.Undefined;

            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingReturnResult, environment))
            {
                returnValue = pendingReturnResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                var pendingThrow = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, pendingThrow, environment))
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

            if (context.IsReturn)
            {
                var pendingReturn = context.FlowValue;
                context.ClearReturn();
                returnVal = pendingReturn;
            }

            var wasInsideScheduledFinally = runner.IsInsideScheduledFinally();

            if (runner.HandleAbruptCompletionJsValue(AbruptKind.Return, returnVal, environment))
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

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static InstructionResult HandleJump(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext ctx,
            out JsValue returnValue)
        {
            runner._programCounter = Unsafe.As<JumpInstruction>(instr).TargetIndex;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleBranch(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<BranchInstruction>(instr);
            var testValue = instruction.Condition.EvaluateExpression(environment, context);

            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingBranchResult, environment))
            {
                returnValue = pendingBranchResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                var thrownValue = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrownValue, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                runner.TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(thrownValue);
            }

            runner._programCounter = testValue.IsTruthy ? instruction.ConsequentIndex : instruction.AlternateIndex;
            returnValue = default;
            return InstructionResult.Continue;
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private InstructionResult HandleBranchFastPath(
            BranchInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            // Fast path for simple binary comparisons (e.g., i < 1000000)
            JsValue testValue;
            var usedFastPath = false;

            if (instruction.Condition is BinaryExpression
                {
                    Operator: BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual or
                    BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual
                } binCond)
            {
                // Profiling wrappers - NoInlining so they show up in profiler
                if (ProfileReadOperand(environment, context, binCond.Left, out var leftVal) &&
                    ProfileReadOperand(environment, context, binCond.Right, out var rightVal))
                {
                    // Comparison via profiling wrapper
                    testValue = ProfileBranchCompare(binCond.Operator, leftVal, rightVal, context);
                    usedFastPath = true;
                }
                else
                {
                    testValue = default;
                }
            }
            else
            {
                testValue = default;
            }

            if (!usedFastPath)
            {
                testValue = ProfileEvaluateExpression(instruction.Condition, environment, context);
            }

            // Check for pending await (async code) - skip entirely for sync functions
            if (_isAsync && TryHandlePendingAwait(context, out var pendingBranchResult, environment))
            {
                returnValue = pendingBranchResult;
                return InstructionResult.Return;
            }

            // Check for throw
            if (TryHandleContextThrow(context, environment))
            {
                returnValue = default;
                return InstructionResult.Continue;
            }

            // Normal path: branch based on condition (with profiling)
            _programCounter = ProfileBranchDecision(
                testValue.IsTruthy,
                instruction.ConsequentIndex,
                instruction.AlternateIndex);
            returnValue = default;
            return InstructionResult.Continue;
        }



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
                    isFirstYieldStarEntry = false;
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

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private InstructionResult HandleSyncIteratorMoveNext(
            IteratorMoveNextInstruction instruction,
            ref JsEnvironment environment,
            EvaluationContext context,
            IteratorDriverState driverState,
            JsVariable valueVar,
            out JsValue returnValue)
        {
            // If we're resuming this iterator site with an abrupt completion (return/throw),
            // propagate it immediately instead of calling iterator.next() again.
            var pendingResumeKind = AsyncStateRef.PendingResumeKind;
            if (pendingResumeKind is ResumePayloadKind.Throw or ResumePayloadKind.Return)
            {
                var (kind, payload) = ConsumeResumeValue();
                var abruptKind = kind == ResumePayloadKind.Return
                    ? AbruptKind.Return
                    : AbruptKind.Throw;

                if (HandleAbruptCompletion(abruptKind, payload, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                if (abruptKind == AbruptKind.Throw)
                {
                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(payload);
                }

                returnValue = CompleteReturn(payload);
                return InstructionResult.Return;
            }

            JsValue currentValue;
            if (driverState.IteratorObject is { } iteratorObj)
            {
                driverState.NextMethod ??= iteratorObj.GetIteratorNextCallable(context);
                var nextResult = iteratorObj.InvokeIteratorNext(
                    driverState.NextMethod,
                    context: context,
                    callingEnvironment: environment);
                // Handle case where nextResult is already a boxed JsValue
                if (!nextResult.TryGetObject<IJsPropertyAccessor>(out var resultObj))
                {
                    // Per ES spec 7.4.2: if result is not an object, throw TypeError
                    var typeError = StandardLibrary.CreateTypeError(
                        "Iterator result is not an object",
                        context, context.RealmState);
                    if (HandleAbruptCompletion(AbruptKind.Throw, typeError, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(typeError);
                }

                var done = resultObj.TryGetProperty("done", out var doneValue) &&
                           JsOps.ToBoolean(doneValue);
                if (done)
                {
                    // Return pooled iterator result object
                    if (resultObj is IteratorResultObject poolableResult)
                    {
                        IteratorResultObjectPool.Return(poolableResult);
                    }

                    // When breaking out of iterator, restore environment to enclosing scope.
                    // This is critical for nested loops: after async resume, environment was
                    // reset to function scope, and we need to restore it to the loop scope
                    // so that variable lookups (like loop counter increments) work correctly.
                    if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv)
                    {
                        environment = enclosingEnv;
                    }

                    // Clear driver state to prevent outer loop's CreateIterationEnv from
                    // incorrectly updating this driver's CurrentIterationEnvironment.
                    IteratorStateRef.CurrentDriverState = null;
                    _programCounter = instruction.BreakIndex;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                // yielded is already a JsValue from TryGetProperty
                currentValue = resultObj.TryGetProperty("value", out var yielded)
                    ? yielded
                    : JsValue.Undefined;

                // Return pooled iterator result object - we've extracted value/done, it's safe to recycle
                if (resultObj is IteratorResultObject poolableResult2)
                {
                    IteratorResultObjectPool.Return(poolableResult2);
                }

                // Mark that we've successfully entered the loop (next() succeeded).
                // Per ES spec 13.6.4.13 step 5.d, IteratorClose should only be called
                // if we've entered the loop body, not if next() itself throws.
                driverState.HasEnteredLoop = true;
            }
            else if (driverState.Enumerator is { } enumerator)
            {
                if (!enumerator.MoveNext())
                {
                    // Restore environment to enclosing scope when iterator exhausted
                    if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv2)
                    {
                        environment = enclosingEnv2;
                    }

                    IteratorStateRef.CurrentDriverState = null;
                    _programCounter = instruction.BreakIndex;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                currentValue = enumerator.Current;

                // Mark that we've successfully entered the loop (enumerator succeeded).
                driverState.HasEnteredLoop = true;
            }
            else
            {
                // Restore environment to enclosing scope when no iterator
                if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv3)
                {
                    environment = enclosingEnv3;
                }

                IteratorStateRef.CurrentDriverState = null;
                _programCounter = instruction.BreakIndex;
                returnValue = default;
                return InstructionResult.Continue;
            }

            // Use JsVariable for scope-correct access (value slot is in loop scope)
            _realmState.Logger?.LogInformation(
                "SyncIterator StoreValue: valueVar.IsValid={Valid} currentEnv.ScopeId={CurScope} slot={Slot} value={Value}",
                valueVar.IsValid,
                environment.ScopeId,
                instruction.ValueSlot.Name,
                currentValue.Kind);
            if (valueVar.IsValid)
            {
                valueVar.Write(currentValue);
                // Also create binding for symbol-based identifier lookup in loop body
                valueVar.Environment.DefineOrAssignJsValue(
                    instruction.ValueSlot, currentValue);
                _realmState.Logger?.LogInformation(
                    "SyncIterator StoreValue: wrote to valueVar.Environment.ScopeId={Scope}",
                    valueVar.Environment.ScopeId);
            }
            else
            {
                StoreValueBySlot(environment, instruction.ValueSlot,
                    instruction.ValueSlotIndex, currentValue);
                _realmState.Logger?.LogInformation(
                    "SyncIterator StoreValue: wrote via StoreValueBySlot to env.ScopeId={Scope}",
                    environment.ScopeId);
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

#if NO_INLINING
        [MethodImpl(MethodImplOptions.NoInlining)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private InstructionResult HandleAsyncIteratorMoveNext(
            IteratorMoveNextInstruction instruction,
            ref JsEnvironment environment,
            EvaluationContext context,
            IteratorDriverState driverState,
            JsVariable iterVar,
            JsVariable valueVar,
            int iteratorIndex,
            out JsValue returnValue)
        {
            var awaitedValue = JsValue.Undefined;
            var awaitedNextResult = JsValue.Undefined;
            var hasAwaitedNextResult = false;
            var skipToStoreValue = false;

            // If we're resuming after a pending await from this
            // iterator site, consume the resume payload and treat
            // it as the awaited result instead of calling into the
            // iterator again.
            if (driverState.AwaitingNextResult || driverState.AwaitingValue)
            {
                var awaitingValue = driverState.AwaitingValue;
                driverState.AwaitingNextResult = false;
                driverState.AwaitingValue = false;
                var (forAwaitResumeKind, forAwaitResumePayload) = ConsumeResumeValue();
                // Use JsVariable for scope-correct access (iterator slot is in loop scope)
                var iterStateValue = driverState.AsJsValue;
                if (iterVar.IsValid)
                {
                    iterVar.Write(iterStateValue);
                }
                else
                {
                    StoreValueBySlot(environment, instruction.IteratorSlot,
                        instruction.IteratorSlotIndex, iterStateValue);
                }

                if (forAwaitResumeKind == ResumePayloadKind.Throw)
                {
                    // forAwaitResumePayload is already JsValue, no need to box with .ToObject()
                    if (HandleAbruptCompletion(AbruptKind.Throw, forAwaitResumePayload,
                            environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(forAwaitResumePayload);
                }

                if (forAwaitResumeKind == ResumePayloadKind.Return)
                {
                    // forAwaitResumePayload is already JsValue, no need to box with .ToObject()
                    if (HandleAbruptCompletion(AbruptKind.Return, forAwaitResumePayload,
                            environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    returnValue = CompleteReturn(forAwaitResumePayload);
                    return InstructionResult.Return;
                }

                if (awaitingValue)
                {
                    awaitedValue = forAwaitResumePayload;
                    skipToStoreValue = true;
                }
                else
                {
                    awaitedNextResult = forAwaitResumePayload;
                    hasAwaitedNextResult = true;
                }
            }

            if (!skipToStoreValue)
            {
                if (driverState.IteratorObject is { } awaitIteratorObj)
                {
                    if (!hasAwaitedNextResult)
                    {
                        driverState.NextMethod ??= awaitIteratorObj.GetIteratorNextCallable(context);
                        var nextResult = awaitIteratorObj.InvokeIteratorNext(
                            driverState.NextMethod,
                            context: context,
                            callingEnvironment: environment);
                        if (!TryResolvePromiseOrYield(nextResult, context, out var awaitedNext))
                        {
                            if (AsyncStateRef.AsyncStepMode &&
                                AsyncStateRef.PendingPromise.TryGetPropertyAccessor(out _))
                            {
                                driverState.AwaitingNextResult = true;
                                // Use JsVariable for scope-correct access
                                var iterState = driverState.AsJsValue;
                                if (iterVar.IsValid)
                                {
                                    iterVar.Write(iterState);
                                }
                                else
                                {
                                    StoreValueBySlot(environment,
                                        instruction.IteratorSlot,
                                        instruction.IteratorSlotIndex, iterState);
                                }

                                // Save environment before suspending so we restore it on resume
                                _executionEnvironment = environment;
                                _state = GeneratorState.Suspended;
                                _programCounter = iteratorIndex;
                                returnValue = CreateIteratorResult(JsValue.Undefined, false);
                                return InstructionResult.Return;
                            }

                            if (context.IsThrow)
                            {
                                var thrownAwait = context.FlowValue;
                                context.Clear();
                                if (HandleAbruptCompletion(AbruptKind.Throw, thrownAwait, environment))
                                {
                                    returnValue = default;
                                    return InstructionResult.Continue;
                                }

                                TryCatchStateRef.TryStack.Clear();
                                throw new ThrowSignal(thrownAwait);
                            }

                            // Restore environment to enclosing scope when breaking
                            if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv4)
                            {
                                environment = enclosingEnv4;
                            }

                            IteratorStateRef.CurrentDriverState = null;
                            _programCounter = instruction.BreakIndex;
                            returnValue = default;
                            return InstructionResult.Continue;
                        }

                        awaitedNextResult = awaitedNext;
                    }

                    if (!awaitedNextResult.TryGetObject<IJsPropertyAccessor>(out var awaitResultObj))
                    {
                        // Per ES spec 7.4.2: if result is not an object, throw TypeError
                        var typeError = StandardLibrary.CreateTypeError(
                            "Iterator result is not an object", context,
                            context.RealmState);
                        if (HandleAbruptCompletion(AbruptKind.Throw, typeError, environment))
                        {
                            returnValue = default;
                            return InstructionResult.Continue;
                        }

                        TryCatchStateRef.TryStack.Clear();
                        throw new ThrowSignal(typeError);
                    }

                    var doneAwait = awaitResultObj.TryGetProperty("done", out var awaitDoneValue) &&
                                    JsOps.ToBoolean(awaitDoneValue);
                    if (doneAwait)
                    {
                        // Return pooled iterator result object
                        if (awaitResultObj is IteratorResultObject asyncPoolableResult)
                        {
                            IteratorResultObjectPool.Return(asyncPoolableResult);
                        }

                        // Restore environment to enclosing scope when async iterator exhausted
                        if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv5)
                        {
                            environment = enclosingEnv5;
                        }

                        IteratorStateRef.CurrentDriverState = null;
                        _programCounter = instruction.BreakIndex;
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    var rawValue = awaitResultObj.TryGetProperty("value", out var yieldedAwait)
                        ? yieldedAwait
                        : JsValue.Undefined;

                    // Return pooled iterator result object - we've extracted value/done
                    if (awaitResultObj is IteratorResultObject asyncPoolableResult2)
                    {
                        IteratorResultObjectPool.Return(asyncPoolableResult2);
                    }

                    if (!TryResolvePromiseOrYield(rawValue, context, out var fullyAwaitedValue))
                    {
                        if (TryHandleAwaitSuspension(driverState, iterVar,
                                instruction, ref environment, context,
                                iteratorIndex, out var suspendResult))
                        {
                            returnValue = suspendResult;
                            return InstructionResult.Return;
                        }

                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    awaitedValue = fullyAwaitedValue;
                }
                else if (driverState.Enumerator is { } awaitEnumerator)
                {
                    if (!awaitEnumerator.MoveNext())
                    {
                        // Restore environment to enclosing scope when enumerator exhausted
                        if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv7)
                        {
                            environment = enclosingEnv7;
                        }

                        // Clear the driver state since this iterator loop is done.
                        // This prevents outer loop's CreateIterationEnv from incorrectly
                        // updating this driver's CurrentIterationEnvironment.
                        IteratorStateRef.CurrentDriverState = null;
                        _programCounter = instruction.BreakIndex;
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    // enumerated is already JsValue from IEnumerator<JsValue>.Current
                    var enumerated = awaitEnumerator.Current;
                    if (!TryResolvePromiseOrYield(enumerated, context, out var awaitedEnumerated))
                    {
                        if (TryHandleAwaitSuspension(driverState, iterVar,
                                instruction, ref environment, context,
                                iteratorIndex, out var suspendResult))
                        {
                            returnValue = suspendResult;
                            return InstructionResult.Return;
                        }

                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    awaitedValue = awaitedEnumerated;
                }
                else
                {
                    // Restore environment to enclosing scope
                    if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv9)
                    {
                        environment = enclosingEnv9;
                    }

                    IteratorStateRef.CurrentDriverState = null;
                    _programCounter = instruction.BreakIndex;
                    returnValue = default;
                    return InstructionResult.Continue;
                }
            }

            // StoreIteratorValue:
            // Mark that we've successfully entered the loop (next() succeeded for async iterator).
            // Per ES spec 13.6.4.13 step 5.d, IteratorClose should only be called
            // if we've entered the loop body, not if next() itself throws.
            driverState.HasEnteredLoop = true;

            // Use JsVariable for scope-correct access (value slot is in loop scope)
            _realmState.Logger?.LogInformation(
                "StoreIteratorValue: valueVar.IsValid={Valid} slot={Slot} value={Value} envHash={Env}",
                valueVar.IsValid,
                instruction.ValueSlot.Name,
                awaitedValue.Kind,
                environment.GetHashCode());
            if (valueVar.IsValid)
            {
                valueVar.Write(awaitedValue);
                // Also create binding for symbol-based identifier lookup in loop body
                valueVar.Environment.DefineOrAssignJsValue(
                    instruction.ValueSlot, awaitedValue);
                _realmState.Logger?.LogInformation(
                    "StoreIteratorValue: wrote to valueVar.Environment={Env}",
                    valueVar.Environment.GetHashCode());
            }
            else
            {
                StoreValueBySlot(environment, instruction.ValueSlot,
                    instruction.ValueSlotIndex, awaitedValue);
                _realmState.Logger?.LogInformation(
                    "StoreIteratorValue: wrote via StoreValueBySlot to env={Env}",
                    environment.GetHashCode());
            }

            // For async iterators, clear any pending completion flags that would
            // prevent subsequent iterations after continue.
            if (_isAsync)
            {
                TryCatchStateRef.TryStack.Clear();
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

    }
}
