#region

using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
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
            // In script mode, track the entry completion value to detect empty try/catch completions
            // (for UpdateEmpty semantics per ES spec)
            var entryCompletionValue = _isScriptMode ? _scriptCompletionValue : JsValue.Unit;
            var frame = new TryFrame(instruction.HandlerIndex, instruction.CatchSlotSymbol, instruction.FinallyIndex,
                environment, entryCompletionValue);
            if (instruction.CatchSlotSymbol is { } slot && !environment.HasBinding(slot))
            {
                environment.DefineJsValue(slot, JsValue.Undefined);
            }

            TryCatchStateRef.TryStack.Push(frame);
        }

        /// <summary>
        /// Finds the jump target for a break or continue signal.
        /// For unlabeled: returns the innermost loop's target.
        /// For labeled: searches the stack for a matching label.
        /// </summary>
        private int FindLoopTarget(Symbol? label, bool isBreak)
        {
            if (LoopStateRef.LoopStack.Count == 0)
            {
                return -1;
            }

            if (label is null)
            {
                // Unlabeled: use innermost loop
                var frame = LoopStateRef.LoopStack.Peek();
                return isBreak ? frame.BreakTarget : frame.ContinueTarget;
            }

            // Labeled: search for matching label
            foreach (var frame in LoopStateRef.LoopStack)
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
                _programCounter = resumeTarget;
                return;
            }

            var frame = TryCatchStateRef.TryStack.Peek();
            if (frame is { FinallyIndex: >= 0, FinallyScheduled: false })
            {
                frame.FinallyScheduled = true;
                frame.PendingCompletion = PendingCompletion.FromNormal(resumeTarget);
                // Per ES spec 13.15.8: Store try/catch completion value before entering finally.
                // If finally completes normally, this value will be restored (not finally's value).
                if (_isScriptMode)
                {
                    frame.TryCatchCompletionValue = _scriptCompletionValue;
                }
                _programCounter = frame.FinallyIndex;
                return;
            }

            // Apply UpdateEmpty semantics per ES spec:
            // If the try/catch body didn't produce a new completion value, result becomes undefined.
            // This implements ES spec 13.15.8: "Return Completion(UpdateEmpty(C, undefined))"
            if (_isScriptMode && _scriptCompletionValue.Equals(frame.EntryCompletionValue))
            {
                _scriptCompletionValue = JsValue.Undefined;
            }

            TryCatchStateRef.TryStack.Pop();
            _programCounter = resumeTarget;
        }

        private bool HandleAbruptCompletion(AbruptKind kind, object? /* intentional */ value, JsEnvironment environment)
        {
            // Clear any previous restored environment
            TryCatchStateRef.RestoredEnvironmentFromTry = null;

            while (TryCatchStateRef.TryStack.Count > 0)
            {
                var frame = TryCatchStateRef.TryStack.Peek();
                if (kind == AbruptKind.Throw && frame is { HandlerIndex: >= 0, CatchUsed: false })
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
                    if (!frame.FinallyScheduled)
                    {
                        frame.FinallyScheduled = true;
                        frame.PendingCompletion = PendingCompletion.FromAbrupt(kind, value);

                        // Restore to the environment that was active when entering the try block.
                        // This ensures that block-scoped bindings inside the try are no longer visible in finally.
                        TryCatchStateRef.RestoredEnvironmentFromTry = frame.EntryEnvironment;

                        _programCounter = frame.FinallyIndex;
                        return true;
                    }

                    // Already inside a finally block. Per ES spec: when an abrupt completion occurs
                    // inside a finally block, the new completion replaces the pending one.
                    // The caller decides how to proceed based on the instruction type:
                    // - StoreResumeValue (generator resumption): continues executing finally
                    // - Return/Throw statement: should terminate finally immediately
                    frame.PendingCompletion = PendingCompletion.FromAbrupt(kind, value);
                    return true;
                }

                TryCatchStateRef.TryStack.Pop();
            }

            return false;
        }

        /// <summary>
        /// JsValue overload - boxes the JsValue which is better than ToObject() as it preserves type info.
        /// </summary>
        private bool HandleAbruptCompletionJsValue(AbruptKind kind, JsValue value, JsEnvironment environment)
        {
            // Boxing JsValue is preferred over ToObject() because:
            // 1. It preserves the JsValue type information
            // 2. Downstream code can detect "is JsValue" and unbox efficiently
            return HandleAbruptCompletion(kind, value, environment);
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

        private JsValue CompleteReturn(JsValue value)
        {
            // Close any active iterators before completing (array patterns and for-of loops)
            CloseActiveIterators();

            _programCounter = -1;
            _state = GeneratorState.Completed;
            _done = true;
            TryCatchStateRef.TryStack.Clear();
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

            foreach (var (state, iterator) in statesToClose)
            {
                // Mark as closed first (before potential exception)
                state.MarkIteratorClosed();

                // Close the iterator - if it throws, that error replaces the return completion
                // Let the exception propagate directly without catching
                iterator.IteratorClose(context);
            }
        }

        /// <summary>
        /// Scans the environment chain for all IActiveIteratorState objects and collects
        /// those with active iterators that need closing.
        /// </summary>
        private static void ScanEnvironmentForActiveIterators(
            JsEnvironment env,
            List<(IActiveIteratorState, IJsObjectLike)> results)
        {
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
                    foreach (var slot in slots)
                    {
                        if (!slot.IsNullOrUndefined &&
                            slot.TryGetObject<IActiveIteratorState>(out var state) &&
                            state.TryGetActiveIterator(out var iterator))
                        {
                            results.Add((state, iterator));
                        }
                    }
                }

                // Also scan parent environments
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
