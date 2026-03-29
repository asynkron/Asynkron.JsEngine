#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        // ============================================================================
        // Script Completion Value Helpers
        // See class-level documentation for the overall design.
        // ============================================================================

        /// <summary>
        /// Resets the completion value to Unit (sentinel) when entering a construct
        /// that has its own completion value (loops, try, catch).
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private void ResetCompletionValue()
        {
            if (_isScriptMode)
            {
                _scriptCompletionValue = JsValue.Unit;
            }
        }

        /// <summary>
        /// Finalizes the completion value when exiting a construct.
        /// If still Unit (no value produced), converts to undefined.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private void FinalizeCompletionValue()
        {
            if (_isScriptMode && _scriptCompletionValue.IsUnit)
            {
                _scriptCompletionValue = JsValue.Undefined;
            }
        }

        /// <summary>
        /// Saves the current completion value to a TryFrame before entering finally.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private void SaveCompletionValueForFinally(TryFrame frame)
        {
            if (_isScriptMode)
            {
                frame.SavedCompletionValue = _scriptCompletionValue;
            }
        }

        /// <summary>
        /// Restores the completion value from a TryFrame after finally completes normally.
        /// Applies Unit→undefined conversion.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private void RestoreCompletionValueFromFinally(TryFrame frame)
        {
            if (_isScriptMode)
            {
                _scriptCompletionValue = frame.SavedCompletionValue.IsUnit
                    ? JsValue.Undefined
                    : frame.SavedCompletionValue;
            }
        }

        // ============================================================================

        private void RecordYield(EvaluationContext context, JsEnvironment? currentEnvironment = null)
        {
            // Remember the active yield slot so the next resume value is applied to the
            // right YieldExpression (ECMA-262 GeneratorResume, step threading of sent values).
            YieldStateRef.LastYieldIndex = context.LastYieldIndex;

            // Also save source positions for yields from StatementInstruction (AST-evaluated yields).
            // These are used to set up resume state so the yield expression returns the resume value.
            YieldStateRef.LastYieldSourceStart = context.LastYieldSourceStart;
            YieldStateRef.LastYieldSourceEnd = context.LastYieldSourceEnd;

            _realmState.Logger?.LogInformation(
                "RecordYield: yieldIndex={YieldIndex} sourceStart={Start} sourceEnd={End}",
                YieldStateRef.LastYieldIndex, YieldStateRef.LastYieldSourceStart, YieldStateRef.LastYieldSourceEnd);

            // Save the current environment so that when the generator resumes, it uses
            // the correct per-iteration environment (for loops with let bindings).
            // This is critical for `continue` to work correctly in generators.
            if (currentEnvironment != null)
            {
                _executionEnvironment = currentEnvironment;
            }

            // Clear any pending environment restore from try-catch handling.
            // When we yield, we've saved the current environment (which may be inside a catch block).
            // On resume, we want to use that saved environment directly, not have it overwritten
            // by a stale RestoredEnvironmentFromTry that was set before the yield.
            _tryCatchState?.RestoredEnvironmentFromTry = null;

            // Clear the context's source positions for the next yield
            context.LastYieldSourceStart = -1;
            context.LastYieldSourceEnd = -1;
        }

        private void PreparePendingResumeValue(ResumeMode mode, JsValue resumeValue, bool wasStart)
        {
            if (wasStart)
            {
                // Per ES spec: The first next() argument is ignored when starting a generator.
                // This applies to both regular yield and yield* - the first call to inner iterator's
                // next() receives undefined, not the outer generator's first next() argument.
                AsyncStateRef.PendingResumeKind = ResumePayloadKind.None;
                AsyncStateRef.PendingResumeValue = JsValue.Undefined;
                return;
            }

            AsyncStateRef.PendingResumeKind = mode switch
            {
                ResumeMode.Throw => ResumePayloadKind.Throw,
                ResumeMode.Return => ResumePayloadKind.Return,
                _ => ResumePayloadKind.Value
            };

            AsyncStateRef.PendingResumeValue = resumeValue;

            if (_realmState.Logger?.IsEnabled(LogLevel.Information) == true)
            {
                var resumeType = resumeValue.ObjectValue?.GetType().Name ?? resumeValue.Kind.ToString();
                _realmState.Logger.LogInformation(
                    "PrepareResume yieldIndex={YieldIndex} kind={Kind} valueType={Type}",
                    YieldStateRef.LastYieldIndex,
                    AsyncStateRef.PendingResumeKind,
                    resumeType);
            }

            if (YieldStateRef.LastYieldIndex < 0)
            {
                return;
            }

            var resumeSlotIndex = YieldStateRef.LastYieldIndex;
            switch (AsyncStateRef.PendingResumeKind)
            {
                case ResumePayloadKind.Throw:
                    YieldStateRef.ResumeContext.SetException(resumeSlotIndex, resumeValue);
                    break;
                case ResumePayloadKind.Return:
                    YieldStateRef.ResumeContext.SetReturn(resumeSlotIndex, resumeValue);
                    break;
                default:
                    YieldStateRef.ResumeContext.SetValue(resumeSlotIndex, resumeValue);
                    break;
            }
        }

        private (ResumePayloadKind Kind, JsValue Value) ConsumeResumeValue()
        {
            var kind = AsyncStateRef.PendingResumeKind;
            var value = AsyncStateRef.PendingResumeValue;
            AsyncStateRef.PendingResumeKind = ResumePayloadKind.None;
            AsyncStateRef.PendingResumeValue = JsValue.Undefined;

            if (kind == ResumePayloadKind.None)
            {
                return (ResumePayloadKind.Value, JsValue.Undefined);
            }

            return (kind, value);
        }

        private void PushTryFrame(EnterTryInstruction instruction, JsEnvironment environment)
        {
            var frame = new TryFrame(
                instruction.HandlerIndex,
                instruction.CatchSlotSymbol,
                instruction.FinallyIndex,
                instruction.EndFinallyIndex,
                environment,
                instruction.LoopContinueTarget);
            if (instruction.CatchSlotSymbol is { } slot && !environment.HasBinding(slot))
            {
                environment.DefineJsValue(slot, JsValue.Undefined);
            }

            TryCatchStateRef.TryStack.Push(frame);
        }

        /// <summary>
        /// Finds the jump target for a break or continue signal.
        /// For unlabeled: returns the innermost breakable construct's target.
        /// For labeled: searches the stack for a matching label.
        /// </summary>
        private int FindBreakableTarget(Symbol? label, bool isBreak)
        {
            if (BreakableStateRef.BreakableStack.Count == 0)
            {
                return -1;
            }

            if (label is null)
            {
                // Unlabeled: use innermost loop/switch that supports this operation.
                // For break: any loop/switch works (BreakTarget is always valid).
                // For continue: skip switch statements (ContinueTarget = -1) and find enclosing loop.
                foreach (var frame in BreakableStateRef.BreakableStack)
                {
                    var target = isBreak ? frame.BreakTarget : frame.ContinueTarget;
                    if (target >= 0)
                    {
                        return target;
                    }
                }

                return -1;
            }

            // Labeled: search for matching label
            foreach (var frame in BreakableStateRef.BreakableStack)
            {
                if (ReferenceEquals(frame.Label, label))
                {
                    return isBreak ? frame.BreakTarget : frame.ContinueTarget;
                }
            }

            return -1;
        }

        private void CompleteTryNormally(int resumeTarget)
        {
            if (TryCatchStateRef.TryStack.Count == 0)
            {
                FinalizeCompletionValue();
                _programCounter = resumeTarget;
                return;
            }

            var frame = TryCatchStateRef.TryStack.Peek();
            if (frame is { FinallyIndex: >= 0, FinallyScheduled: false })
            {
                frame.FinallyScheduled = true;
                frame.PendingCompletion = PendingCompletion.FromNormal(resumeTarget);
                SaveCompletionValueForFinally(frame);
                TryCatchStateRef.RestoredEnvironmentFromTry = frame.EntryEnvironment;
                _programCounter = frame.FinallyIndex;
                return;
            }

            TryCatchStateRef.TryStack.Pop();
            FinalizeCompletionValue();
            _programCounter = resumeTarget;
        }

        private bool HandleAbruptCompletion(AbruptKind kind, object? /* intentional */ value)
        {
            // Clear any previous restored environment
            TryCatchStateRef.RestoredEnvironmentFromTry = null;

            while (TryCatchStateRef.TryStack.Count > 0)
            {
                var frame = TryCatchStateRef.TryStack.Peek();
                // Per ES spec: catch clause only handles exceptions from the try block, NOT from finally.
                // If FinallyScheduled is true, we're executing inside the finally block, so exceptions
                // should propagate up, not go to the catch handler.
                if (kind == AbruptKind.Throw && frame is
                    { HandlerIndex: >= 0, CatchUsed: false, FinallyScheduled: false })
                {
                    frame.CatchUsed = true;

                    // Store the thrown value in the frame for EnterCatchInstruction to read.
                    // Handle case where value is already a boxed JsValue.
                    var valueJs = value is JsValue js ? js : JsValue.FromObjectUnsafe(value);
                    frame.ThrownValue = valueJs;

                    // Restore to the environment that was active when entering the try block.
                    // This ensures that block-scoped bindings inside the try are no longer visible.
                    var targetEnv = frame.EntryEnvironment;
                    TryCatchStateRef.RestoredEnvironmentFromTry = targetEnv;

                    // Legacy: CatchSlotSymbol-based binding (kept for backward compatibility)
                    // New IR path uses EnterCatchInstruction which reads frame.ThrownValue directly.
                    if (frame.CatchSlotSymbol is { } slot)
                    {
                        // Use the entry environment for the catch slot, not the current (nested) environment
                        if (targetEnv.HasBinding(slot))
                        {
                            targetEnv.AssignJsValue(slot, valueJs);
                        }
                        else
                        {
                            targetEnv.DefineJsValue(slot, valueJs);
                        }
                    }

                    _programCounter = frame.HandlerIndex;
                    return true;
                }

                if (frame.FinallyIndex >= 0)
                {
                    // For for-of loops: if this is a continue that targets within the loop,
                    // skip the finally block (IteratorClose). We only run IteratorClose when
                    // exiting the loop, not when continuing to the next iteration.
                    if (kind == AbruptKind.Continue &&
                        frame.LoopContinueTarget >= 0 &&
                        value is int targetIndex &&
                        targetIndex == frame.LoopContinueTarget)
                    {
                        // Same-loop continue stays within the protected loop body.
                        // Skip IteratorClose/finally handling, but keep the try frame active
                        // for subsequent iterations and eventual loop exit.
                        return false;
                    }

                    if (!frame.FinallyScheduled)
                    {
                        frame.FinallyScheduled = true;
                        frame.PendingCompletion = PendingCompletion.FromAbrupt(kind, value);
                        SaveCompletionValueForFinally(frame);

                        // Restore to the environment that was active when entering the try block.
                        // This ensures that block-scoped bindings inside the try are no longer visible in finally.
                        TryCatchStateRef.RestoredEnvironmentFromTry = frame.EntryEnvironment;

                        _programCounter = frame.FinallyIndex;
                        return true;
                    }

                    // Already inside a finally block. Per ES spec: when an abrupt completion occurs
                    // inside a finally block, the new completion replaces the pending one.
                    // Jump to EndFinally to exit the finally block — continuing to execute
                    // remaining finally instructions would re-enter call loops and hang.
                    frame.PendingCompletion = PendingCompletion.FromAbrupt(kind, value);
                    if (frame.EndFinallyIndex >= 0)
                    {
                        _programCounter = frame.EndFinallyIndex;
                    }

                    return true;
                }

                TryCatchStateRef.TryStack.Pop();
            }

            return false;
        }

        /// <summary>
        /// JsValue overload - boxes the JsValue which is better than ToObject() as it preserves type info.
        /// </summary>
        private bool HandleAbruptCompletionJsValue(AbruptKind kind, JsValue value)
        {
            // Boxing JsValue is preferred over ToObject() because:
            // 1. It preserves the JsValue type information
            // 2. Downstream code can detect "is JsValue" and unbox efficiently
            return HandleAbruptCompletion(kind, value);
        }

        /// <summary>
        /// Checks if the current try frame is inside a finally block (FinallyScheduled is true).
        /// Used by Return/Throw instruction handlers to determine if they should terminate
        /// the finally block immediately rather than executing code after the statement.
        /// </summary>
        private bool IsInsideScheduledFinally()
        {
            return TryCatchStateRef.TryStack.Count > 0 &&
                   TryCatchStateRef.TryStack.Peek().FinallyScheduled;
        }

        /// <summary>
        /// Attempts to get the EndFinally jump target for the current try frame.
        /// Returns true if we're inside a scheduled finally block that has an EndFinally target,
        /// which means execution should skip remaining finally code and jump to EndFinally.
        /// Used by generator return/throw handling to prevent executing code after yield in finally.
        /// </summary>
        private bool TryGetEndFinallyJumpTarget(out int endFinallyTarget)
        {
            if (TryCatchStateRef.TryStack.Count > 0)
            {
                var frame = TryCatchStateRef.TryStack.Peek();
                if (frame.FinallyScheduled && frame.EndFinallyIndex >= 0)
                {
                    endFinallyTarget = frame.EndFinallyIndex;
                    return true;
                }
            }

            endFinallyTarget = -1;
            return false;
        }

        private JsValue CompleteReturn(JsValue value)
        {
            // Close any active iterators before completing (array patterns and for-of loops).
            // Per ES spec 27.5.3.4 step 8: if IteratorClose throws, that error replaces
            // the return completion. We must still mark the generator as completed.
            ThrowSignal? closeError = null;
            try
            {
                CloseActiveIterators();
            }
            catch (ThrowSignal signal)
            {
                closeError = signal;
            }

            _programCounter = -1;
            _state = GeneratorState.Completed;
            _done = true;
            TryCatchStateRef.TryStack.Clear();

            // If an iterator close threw, propagate that error instead of the return value.
            if (closeError is not null)
            {
                throw closeError;
            }

            return CreateIteratorResult(value, true);
        }

        /// <summary>
        /// Closes all active iterators stored in the environment chain.
        /// This handles both array pattern iterators (from destructuring) and
        /// for-of loop iterators via the unified IActiveIteratorState interface.
        /// </summary>
        private void CloseActiveIterators()
        {
            if (_executionEnvironment is null)
            {
                return;
            }

            // Create a fresh context to avoid state interference from the existing context
            var context = _realmState.CreateContext(ScopeKind.Function, DetermineGeneratorScopeMode());

            // Scan the environment for all IActiveIteratorState objects and close their iterators
            var statesToClose = new List<(IActiveIteratorState State, IJsObjectLike Iterator)>();
            ScanEnvironmentForActiveIterators(_executionEnvironment, statesToClose);
            var closedIterators = new HashSet<IJsObjectLike>(ReferenceEqualityComparer<IJsObjectLike>.Instance);

            // Mark ALL iterators as closed first to prevent re-entrancy.
            foreach (var (state, _) in statesToClose)
            {
                state.MarkIteratorClosed();
            }

            ThrowSignal? firstError = null;
            foreach (var (_, iterator) in statesToClose)
            {
                // Skip duplicate iterator objects to avoid double-closing the same iterator
                if (!closedIterators.Add(iterator))
                {
                    continue;
                }

                // Close the iterator. If it throws, remember the first error but continue
                // closing remaining iterators to avoid resource leaks.
                try
                {
                    iterator.IteratorClose(context);
                }
                catch (ThrowSignal signal)
                {
                    firstError ??= signal;
                }
            }

            // Propagate the first error after all iterators are closed.
            if (firstError is not null)
            {
                throw firstError;
            }
        }

        /// <summary>
        /// Scans the environment chain for all IActiveIteratorState objects and collects
        /// those with active iterators that need closing.
        /// IMPORTANT: Only scans within the current function scope, not parent scopes from the caller.
        /// This prevents closing iterators that are iterating OVER this generator when the generator completes.
        /// </summary>
        private static void ScanEnvironmentForActiveIterators(
            JsEnvironment env,
            List<(IActiveIteratorState, IJsObjectLike)> results)
        {
            var isFirstEnv = true;
            while (true)
            {
                // Scan symbol bindings in this environment (using safe method to skip uninitialized TDZ bindings)
                foreach (var symbol in env.GetBindingSymbols())
                {
                    if (env.TryGetJsValueLocalSafe(symbol, out var jsValue) &&
                        !jsValue.IsNullOrUndefined &&
                        jsValue.TryGetObject<IActiveIteratorState>(out var state) &&
                        state.TryGetActiveIterator(out var iterator))
                    {
                        results.Add((state, iterator));
                    }
                }

                // Also scan slots for IteratorDriverState (for-of loop state)
                if (env.HasSlots && env._slots is { } slots)
                {
                    for (var i = 0; i < env._slotCount; i++)
                    {
                        var slotValue = slots[i].Value;
                        if (!slotValue.IsNullOrUndefined &&
                            slotValue.TryGetObject<IActiveIteratorState>(out var state) &&
                            state.TryGetActiveIterator(out var iterator))
                        {
                            results.Add((state, iterator));
                        }
                    }
                }

                // Stop at the function scope boundary - don't scan caller's scopes.
                // If this is a function scope (and not the first one we entered), stop.
                // The first environment might be a function scope that we need to scan.
                if (!isFirstEnv && env.IsFunctionScope)
                {
                    break;
                }

                isFirstEnv = false;

                // Continue to parent environment within the function
                if (env.Enclosing is { } parent)
                {
                    env = parent;
                    continue;
                }

                break;
            }
        }
    }
}
