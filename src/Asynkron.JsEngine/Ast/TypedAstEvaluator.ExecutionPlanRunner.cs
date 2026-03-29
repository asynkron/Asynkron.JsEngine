#region

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Executes an IR execution plan (compiled from AST).
    /// </summary>
    /// <remarks>
    /// <para>## Script Completion Value (_scriptCompletionValue)</para>
    /// <para>
    /// In script/eval mode, we track the completion value per ES spec.
    /// The completion value is what eval() returns.
    /// </para>
    /// <para>### Sentinel Pattern</para>
    /// <para>We use JsValue.Unit as a sentinel meaning "no value produced yet".</para>
    /// <para>
    /// - Script start: _scriptCompletionValue = Unit
    /// - Expression statement (e.g., 5+5;): _scriptCompletionValue = 10
    /// - At script end: if still Unit → return undefined, else return the value
    /// </para>
    /// <para>### Loops, Try, Catch</para>
    /// <para>
    /// These constructs have their own internal completion value per ES spec.
    /// They all follow the same pattern:
    /// </para>
    /// <para>
    /// 1. On ENTER: _scriptCompletionValue = Unit (reset to sentinel)
    /// 2. Body executes: may or may not update _scriptCompletionValue
    /// 3. On EXIT: if (_scriptCompletionValue.IsUnit) → set to undefined
    /// </para>
    /// <para>
    /// This ensures:
    /// - eval('7; for (...) {}') returns undefined (not 7)
    /// - eval('7; for (...) { 9; }') returns 9
    /// - eval('for (...) { 9; break; }') returns 9 (break doesn't touch completion value)
    /// </para>
    /// <para>### Finally (Special Case)</para>
    /// <para>
    /// Finally is different: its completion value is DISCARDED if it completes normally.
    /// The try/catch completion value is restored.
    /// </para>
    /// <para>- eval('try { 7; } finally { 8; }') returns 7 (not 8)</para>
    /// <para>
    /// Implementation:
    /// 1. When entering finally: frame.SavedCompletionValue = _scriptCompletionValue
    /// 2. Finally body executes (its value is irrelevant if normal completion)
    /// 3. On normal exit: _scriptCompletionValue = SavedCompletionValue.IsUnit ? undefined : SavedCompletionValue
    /// 4. On abrupt exit (return/throw): abrupt completion takes over, completion value doesn't matter
    /// </para>
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
        private readonly RealmState _derivedClassErrorRealm;
        private readonly IJsEnvironmentAwareCallable? _superConstructor;
        private readonly IJsPropertyAccessor? _superPrototype;
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

        /// <summary>
        /// Offset applied to slot indices when running in script mode on GlobalEnvironment.
        /// IR instructions use 0-based slot indices, but GlobalEnvironment may already have
        /// slots (like Symbol.This at slot 0). This offset ensures synthetic slots don't
        /// overwrite existing GlobalEnvironment slots.
        /// </summary>
        private readonly int _slotOffset;

        // Lazy state objects - only allocated when needed
        // TryCatchState needs explicit backing field for hot-path null check without allocation
        private TryCatchState? _tryCatchState;

        // Flat slots array for O(1) variable access within this execution plan.
        // Indexed by FlatSlotId stamped on IdentifierExpression nodes.
        // Each JsVariable holds a reference to the environment and slot, providing direct read/write.
        private JsVariable[]? _flatSlots;

        // Lazy accessors
        private AsyncState AsyncStateRef => field ??= new AsyncState();
        private YieldState YieldStateRef => field ??= new YieldState();
        private IteratorState IteratorStateRef => field ??= new IteratorState();
        private TryCatchState TryCatchStateRef => _tryCatchState ??= new TryCatchState();
        private BreakableState BreakableStateRef => field ??= new BreakableState();
        private WithState WithStateRef => field ??= new WithState();
        private ForInState ForInStateRef => field ??= new ForInState();

        internal JsValue EvaluateAwaitInGenerator(AwaitExpression expression, JsEnvironment environment,
            EvaluationContext context)
        {
            return EvaluateAwaitInGenerator(
                expression.GetAwaitStateKey(),
                expression.Expression,
                null,
                environment,
                context);
        }

        internal JsValue EvaluateAwaitInGenerator(
            Symbol awaitKey,
            ExpressionProgram awaitedProgram,
            JsEnvironment environment,
            EvaluationContext context)
        {
            return EvaluateAwaitInGenerator(awaitKey, null, awaitedProgram, environment, context);
        }

        internal JsValue EvaluateAwaitInGenerator(
            Symbol awaitKey,
            ExpressionNode? awaitedExpression,
            ExpressionProgram? awaitedProgram,
            JsEnvironment environment,
            EvaluationContext context)
        {
            // When not executing under async-aware stepping, fall back to the
            // legacy blocking helper so synchronous generators remain usable.
            if (!AsyncStateRef.AsyncStepMode)
            {
                // Keep as JsValue to avoid boxing round trips
                var awaitedValueSync = awaitedProgram is { } syncProgram
                    ? EvaluateExpressionProgram(syncProgram, environment, context)
                    : awaitedExpression!.EvaluateExpression(environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return awaitedValueSync;
                }

                // awaitedValueSync is already JsValue
                TryAwaitPromise(awaitedValueSync, context, out var resolvedSync);

                return resolvedSync;

            }

            // Async-aware mode: use per-site await state so we don't re-run
            // side-effecting expressions after the promise has resolved.
            if (environment.TryGetObject<AwaitState>(awaitKey, out var state) &&
                state.HasResult)
            {
                var result = state.Result;
                var isThrow = state.IsThrow;
                RecordAwaitKeyForReset(awaitKey);

                // If the await was rejected, throw at this point so the
                // generator's try-catch can handle it.
                if (isThrow)
                {
                    throw new ThrowSignal(result);
                }

                return result;
            }

            // Keep as JsValue to avoid boxing round trips
            var awaitedValue = awaitedProgram is { } program
                ? EvaluateExpressionProgram(program, environment, context)
                : awaitedExpression!.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return awaitedValue;
            }

            var existingState = JsValue.FromObjectUnsafe(new AwaitState());

            if (environment.HasBinding(awaitKey))
            {
                environment.AssignJsValue(awaitKey, existingState);
            }
            else
            {
                environment.DefineJsValue(awaitKey, existingState);
            }

            // Async-aware mode: surface promise-like values as pending steps
            // so AsyncGeneratorInvoker can resume via the event queue.
            // awaitedValue is already JsValue
            if (TryResolvePromiseOrYield(awaitedValue, context, out var resolved))
            {
                return resolved;
            }

            if (!HasPendingPromise())
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

        private void RecordAwaitKeyForReset(Symbol awaitKey)
        {
            var asyncState = AsyncStateRef;
            var awaitKeysToReset = asyncState.AwaitKeysToReset ??= [];
            if (!ReferenceEquals(asyncState.LastAwaitKeyToReset, awaitKey))
            {
                awaitKeysToReset.Add(awaitKey);
                asyncState.LastAwaitKeyToReset = awaitKey;
            }
        }

        private void ResetAwaitKeysAfterInstruction(JsEnvironment environment)
        {
            if (!_isAsync)
            {
                return;
            }

            var asyncState = AsyncStateRef;
            var awaitKeysToReset = asyncState.AwaitKeysToReset;
            if (awaitKeysToReset is null || awaitKeysToReset.Count == 0)
            {
                return;
            }

            for (var i = 0; i < awaitKeysToReset.Count; i++)
            {
                var awaitKey = awaitKeysToReset[i];
                if (!environment.TryGetObject<AwaitState>(awaitKey, out var state))
                {
                    continue;
                }

                state.HasResult = false;
                state.IsThrow = false;
                state.Result = JsValue.Undefined;
            }

            awaitKeysToReset.Clear();
            asyncState.LastAwaitKeyToReset = null;
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

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool HasPendingPromise()
        {
            return JsPromise.TryGetInternalPromise(AsyncStateRef.PendingPromise, out _) ||
                   AsyncStateRef.PendingPromise.TryGetPropertyAccessor(out _);
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
        private bool TryHandleContextThrow(EvaluationContext context)
        {
            if (!context.IsThrow)
            {
                return false;
            }

            var thrownValue = context.FlowValue;
            context.Clear();
            if (HandleAbruptCompletion(AbruptKind.Throw, thrownValue))
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
            ref JsEnvironment environment,
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
                if (HandleAbruptCompletion(AbruptKind.Throw, thrown))
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
                if (!HandleAbruptCompletion(AbruptKind.Return, returnSignalValue))
                {
                    return (SignalAction.Return, CompleteReturn(returnSignalValue));
                }

                if (_programCounter == _currentInstructionIndex)
                {
                    _programCounter = nextInstructionIndex;
                }

                return (SignalAction.Continue, default);
            }

            if (context.IsBreak)
            {
                var label = ((BreakCompletionSignal)context.CurrentSignal!).Label;
                context.TryClearBreak(label);

                var breakTarget = FindBreakableTarget(label, isBreak: true);
                if (breakTarget < 0)
                {
                    throw new InvalidOperationException("Unable to resolve break target.");
                }

                if (HandleAbruptCompletion(AbruptKind.Break, breakTarget))
                {
                    if (_programCounter == _currentInstructionIndex)
                    {
                        _programCounter = nextInstructionIndex;
                    }

                    return (SignalAction.Continue, default);
                }

                MoveEnvironmentToControlTarget(ref environment, breakTarget);
                _programCounter = breakTarget;
                return (SignalAction.Continue, default);
            }

            if (context.IsContinue)
            {
                var label = ((ContinueCompletionSignal)context.CurrentSignal!).Label;
                context.TryClearContinue(label);

                var continueTarget = FindBreakableTarget(label, isBreak: false);
                if (continueTarget < 0)
                {
                    throw new InvalidOperationException("Unable to resolve continue target.");
                }

                if (HandleAbruptCompletion(AbruptKind.Continue, continueTarget))
                {
                    if (_programCounter == _currentInstructionIndex)
                    {
                        _programCounter = nextInstructionIndex;
                    }

                    return (SignalAction.Continue, default);
                }

                MoveEnvironmentToControlTarget(ref environment, continueTarget);
                _programCounter = continueTarget;
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
                HasPendingPromise())
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
                if (HandleAbruptCompletion(AbruptKind.Throw, thrownValue))
                {
                    suspendResult = JsValue.Undefined;
                    return false;
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(thrownValue);
            }

            RestoreIteratorLoopScopeEnvironment(driverState, ref environment);

            IteratorStateRef.CurrentDriverState = null;
            _programCounter = instruction.BreakIndex;
            suspendResult = JsValue.Undefined;
            return false;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static void RestoreIteratorLoopScopeEnvironment(
            IteratorDriverState driverState,
            ref JsEnvironment environment)
        {
            // Prefer the captured loop-scope environment rather than relying on
            // CurrentIterationEnvironment.Enclosing, which may reference a pooled
            // iteration environment that has been reused for a different scope.
            if (driverState.LoopScopeEnvironment is { } loopScopeEnvironment)
            {
                environment = loopScopeEnvironment;
                return;
            }

            if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnvironment)
            {
                environment = enclosingEnvironment;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // INSTRUCTION HANDLERS: NoInlining methods for profiling visibility
        // Each handler processes one instruction kind and returns control flow action
        // Change to AggressiveInlining after profiling is complete
        // ═══════════════════════════════════════════════════════════════════════════

        [MethodImpl(JsEngineConstants.Inlining)]
        private InstructionResult HandleBranchFastPath(
                    BranchInstruction instruction,
                    JsEnvironment environment,
                    EvaluationContext context,
                    out JsValue returnValue)
        {
            var testValue = EvaluateExpressionProgram(instruction.ConditionProgram, environment, context);

            // Check for pending await (async code) - skip entirely for sync functions
            if (_isAsync && TryHandlePendingAwait(context, out var pendingBranchResult, environment))
            {
                returnValue = pendingBranchResult;
                return InstructionResult.Return;
            }

            // Check for throw
            if (TryHandleContextThrow(context))
            {
                returnValue = default;
                return InstructionResult.Continue;
            }

            // Normal path: branch based on condition (with profiling)
            _programCounter = ProfileBranchDecision(
                testValue.IsTruthy,
                instruction.ConsequentIndex,
                instruction.AlternateIndex);

            // Check cancellation on backward jumps (loop iterations).
            // This enforces ExecutionTimeout for IR execution — without this,
            // do-while and while loops that use BranchInstruction for their
            // back-edge ignore the timeout and can hang the host.
            if (_programCounter <= _currentInstructionIndex)
            {
                context.ThrowIfCancellationRequested();
            }

            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
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

                if (HandleAbruptCompletion(abruptKind, payload))
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
                    if (HandleAbruptCompletion(AbruptKind.Throw, typeError))
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
                    RestoreIteratorLoopScopeEnvironment(driverState, ref environment);

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
                    RestoreIteratorLoopScopeEnvironment(driverState, ref environment);

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
                RestoreIteratorLoopScopeEnvironment(driverState, ref environment);

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

        [MethodImpl(JsEngineConstants.Inlining)]
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
                    if (HandleAbruptCompletion(AbruptKind.Throw, forAwaitResumePayload))
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
                    if (HandleAbruptCompletion(AbruptKind.Return, forAwaitResumePayload))
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
                                HasPendingPromise())
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
                                if (HandleAbruptCompletion(AbruptKind.Throw, thrownAwait))
                                {
                                    returnValue = default;
                                    return InstructionResult.Continue;
                                }

                                TryCatchStateRef.TryStack.Clear();
                                throw new ThrowSignal(thrownAwait);
                            }

                            // Restore environment to enclosing scope when breaking
                            RestoreIteratorLoopScopeEnvironment(driverState, ref environment);

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
                        if (HandleAbruptCompletion(AbruptKind.Throw, typeError))
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
                        RestoreIteratorLoopScopeEnvironment(driverState, ref environment);

                        IteratorStateRef.CurrentDriverState = null;
                        _programCounter = instruction.BreakIndex;
                        _realmState.Logger?.LogInformation(
                            "[ASYNC-ITER-DEBUG] AsyncIterator done=true, jumping to BreakIndex={BreakIndex}, instructionsLength={Length}",
                            instruction.BreakIndex, _plan!.Instructions.Length);
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
                        RestoreIteratorLoopScopeEnvironment(driverState, ref environment);

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
                    RestoreIteratorLoopScopeEnvironment(driverState, ref environment);

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

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

    }
}
