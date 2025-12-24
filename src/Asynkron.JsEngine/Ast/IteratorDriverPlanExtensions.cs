#region

using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IteratorDriverPlan plan)
    {
        private JsValue ExecuteIteratorDriverJsValue(IJsObjectLike? iterator,
            IEnumerator<JsValue>? enumerator,
            JsEnvironment loopEnvironment,
            JsEnvironment outerEnvironment,
            EvaluationContext context,
            Symbol? loopLabel,
            Func<JsEnvironment>? rentIterationEnvironment = null)
        {
            var lastValueJs = JsValue.Undefined;
            var iteratorDone = false;

            var state = new IteratorDriverState
            {
                IteratorObject = iterator,
                Enumerator = enumerator,
                IsAsyncIterator = plan.Kind == IteratorDriverKind.Await,
                NextMethod = iterator?.GetIteratorNextCallable(context)
            };

            // OPTIMIZATION: Check if we can use the fast slot path for simple identifier bindings
            // This avoids dictionary-based AssignLoopBinding and SyncIterationSlots per iteration
            var canUseSlotFastPath = plan.Target is IdentifierBinding &&
                                     !plan.PerIterationSlotIndices.IsDefaultOrEmpty &&
                                     plan.PerIterationSlotIndices.Length == 1 &&
                                     plan.PerIterationSlotIndices[0] >= 0 &&
                                     rentIterationEnvironment is not null;
            var fastPathSlotIndex = canUseSlotFastPath ? plan.PerIterationSlotIndices[0] : -1;

            // OPTIMIZATION: For 'var' bindings, pre-resolve JsVariable once and reuse for all iterations
            // This gives O(1) direct slot access via JsVariable.Write() instead of SetSlot() per iteration
            var canCacheVarVariable = canUseSlotFastPath && plan.DeclarationKind == VariableKind.Var;
            var cachedVarVariable = canCacheVarVariable
                ? new JsVariable(loopEnvironment, fastPathSlotIndex)
                : default;

            while (!context.ShouldStopEvaluation)
            {
                context.ThrowIfCancellationRequested();

                object? nextResult = null;
                if (state.IteratorObject is not null)
                {
                    var throwBeforeNext = context.IsThrow;
                    nextResult = state.IteratorObject.InvokeIteratorNext(
                        state.NextMethod!,
                        context: context,
                        callingEnvironment: loopEnvironment);
                    if (!throwBeforeNext && context.IsThrow)
                    {
                        // Per ES spec 13.6.4.13 step 5.d: if IteratorStep (calling next())
                        // returns an abrupt completion, we return that completion directly
                        // WITHOUT calling IteratorClose.
                        var thrown = context.FlowValue;
                        context.Clear();
                        throw new ThrowSignal(thrown);
                    }
                }
                else if (state.Enumerator is not null)
                {
                    if (!state.Enumerator.MoveNext())
                    {
                        break;
                    }

                    // FAST PATH: Enumerator yields JsValue directly - skip iterator result unwrapping
                    // and go directly to body execution. This avoids creating {done, value} objects.
                    var enumeratorValue = state.Enumerator.Current;

                    var iterationEnvironment = plan.DeclarationKind is VariableKind.Let or VariableKind.Const
                        or VariableKind.Using or VariableKind.AwaitUsing
                        ? rentIterationEnvironment?.Invoke() ?? new JsEnvironment(loopEnvironment,
                            creatingSource: plan.Body.Source, description: "for-each-iteration")
                        : loopEnvironment;

                    // OPTIMIZATION: For simple identifier bindings, write directly to slot
                    // This avoids dictionary-based AssignLoopBinding and SyncIterationSlots
                    if (cachedVarVariable.IsValid)
                    {
                        // Fastest path: pre-resolved JsVariable for 'var' bindings
                        cachedVarVariable.Write(enumeratorValue);
                    }
                    else if (canUseSlotFastPath && iterationEnvironment.HasSlots)
                    {
                        // Fast path: direct slot write for let/const bindings
                        iterationEnvironment.GetSlotRef(fastPathSlotIndex) = enumeratorValue;
                    }
                    else
                    {
                        plan.Target.AssignLoopBinding(enumeratorValue, iterationEnvironment, outerEnvironment, context,
                            plan.DeclarationKind);
                        if (context.IsThrow)
                        {
                            throw new ThrowSignal(context.FlowValue);
                        }

                        IteratorDriverPlan.SyncIterationSlots(plan, iterationEnvironment, context);
                    }

                    var bodyResult = plan.Body.EvaluateStatementJsValue(iterationEnvironment, context, loopLabel);
                    if (!bodyResult.IsUnit)
                    {
                        lastValueJs = bodyResult;
                    }

                    if (context.IsThrow)
                    {
                        throw new ThrowSignal(context.FlowValue);
                    }

                    if (context.IsReturn || context.IsThrow)
                    {
                        break;
                    }

                    if (context.TryClearContinue(loopLabel))
                    {
                        continue;
                    }

                    if (context.TryClearBreak(loopLabel))
                    {
                        break;
                    }

                    continue; // Skip the iterator protocol handling below
                }

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();

                    if (state.IteratorObject is not null && !iteratorDone)
                    {
                        state.IteratorObject.IteratorClose(context, true,
                            thrown);

                        if (context.IsThrow)
                        {
                            thrown = context.FlowValue;
                            context.Clear();
                        }
                    }

                    throw new ThrowSignal(thrown);
                }

                // Unwrap JsValue struct if present (only for iterator protocol path)
                if (nextResult is JsValue jsVal)
                {
                    nextResult = jsVal.Kind == JsValueKind.Object ? jsVal.ObjectValue : null;
                }

                if (nextResult is IJsObjectLike resultObj)
                {
                    var done = resultObj.TryGetProperty("done", out var doneValue) &&
                               JsOps.ToBoolean(doneValue);
                    if (done)
                    {
                        iteratorDone = true;
                        break;
                    }

                    JsValue value;
                    // TryGetProperty returns JsValue, keep as JsValue to avoid boxing
                    var gotValue = resultObj.TryGetProperty("value", out var yielded);
                    value = gotValue ? yielded : JsValue.Undefined;

                    var iterationEnvironment = plan.DeclarationKind is VariableKind.Let or VariableKind.Const
                        or VariableKind.Using or VariableKind.AwaitUsing
                        ? rentIterationEnvironment?.Invoke() ?? new JsEnvironment(loopEnvironment,
                            creatingSource: plan.Body.Source, description: "for-each-iteration")
                        : loopEnvironment;

                    try
                    {
                        // OPTIMIZATION: For simple identifier bindings, write directly to slot
                        if (cachedVarVariable.IsValid)
                        {
                            // Fastest path: pre-resolved JsVariable for 'var' bindings
                            cachedVarVariable.Write(value);
                        }
                        else if (canUseSlotFastPath && iterationEnvironment.HasSlots)
                        {
                            // Fast path: direct slot write for let/const bindings
                            iterationEnvironment.GetSlotRef(fastPathSlotIndex) = value;
                        }
                        else
                        {
                            plan.Target.AssignLoopBinding(value, iterationEnvironment, outerEnvironment, context,
                                plan.DeclarationKind);
                            if (context.IsThrow)
                            {
                                throw new ThrowSignal(context.FlowValue);
                            }

                            IteratorDriverPlan.SyncIterationSlots(plan, iterationEnvironment, context);
                        }

                        // Per ES spec 14.7.5.7 ForIn/OfBodyEvaluation step 5.k-l:
                        // Only update V (completion value) if result.[[Value]] is not empty
                        var bodyResult = plan.Body.EvaluateStatementJsValue(iterationEnvironment, context, loopLabel);
                        if (!bodyResult.IsUnit)
                        {
                            lastValueJs = bodyResult;
                        }

                        if (context.IsThrow)
                        {
                            throw new ThrowSignal(context.FlowValue);
                        }
                    }
                    catch (ThrowSignal)
                    {
                        if (state.IteratorObject is not null && !iteratorDone)
                        {
                            state.IteratorObject.IteratorClose(context, true);
                        }

                        throw;
                    }
                }
                else
                {
                    if (state.IteratorObject is not null)
                    {
                        var typeError = StandardLibrary.CreateTypeError(
                            "Iterator.next() did not return an object", context, context.RealmState);
                        context.RealmState.Logger?.LogInformation(
                            "Iterator.next non-object result; throwing TypeError (label={Label})",
                            loopLabel?.Name ?? "<none>");
                        context.SetThrow(typeError);
                        iteratorDone =
                            false; // force IteratorClose on exit for abrupt completion paths that require it
                        throw new ThrowSignal(typeError);
                    }

                    // Enumerator path (non-object next)
                    var iterationEnvironment = plan.DeclarationKind is VariableKind.Let or VariableKind.Const
                        or VariableKind.Using or VariableKind.AwaitUsing
                        ? rentIterationEnvironment?.Invoke() ?? new JsEnvironment(loopEnvironment,
                            creatingSource: plan.Body.Source, description: "for-each-iteration")
                        : loopEnvironment;

                    // OPTIMIZATION: For simple identifier bindings, write directly to slot
                    var nextJsValue = JsValue.FromObjectUnsafe(nextResult);
                    if (cachedVarVariable.IsValid)
                    {
                        // Fastest path: pre-resolved JsVariable for 'var' bindings
                        cachedVarVariable.Write(nextJsValue);
                    }
                    else if (canUseSlotFastPath && iterationEnvironment.HasSlots)
                    {
                        // Fast path: direct slot write for let/const bindings
                        iterationEnvironment.GetSlotRef(fastPathSlotIndex) = nextJsValue;
                    }
                    else
                    {
                        plan.Target.AssignLoopBinding(nextJsValue, iterationEnvironment,
                            outerEnvironment, context,
                            plan.DeclarationKind);
                        if (context.IsThrow)
                        {
                            throw new ThrowSignal(context.FlowValue);
                        }

                        IteratorDriverPlan.SyncIterationSlots(plan, iterationEnvironment, context);
                    }

                    // Per ES spec 14.7.5.7 ForIn/OfBodyEvaluation step 5.k-l:
                    // Only update V (completion value) if result.[[Value]] is not empty
                    var bodyResult2 = plan.Body.EvaluateStatementJsValue(iterationEnvironment, context, loopLabel);
                    if (!bodyResult2.IsUnit)
                    {
                        lastValueJs = bodyResult2;
                    }

                    if (context.IsThrow)
                    {
                        throw new ThrowSignal(context.FlowValue);
                    }
                }

                if (context.IsReturn || context.IsThrow)
                {
                    break;
                }

                if (context.TryClearContinue(loopLabel))
                {
                    continue;
                }

                if (context.TryClearBreak(loopLabel))
                {
                    break;
                }
            }

            if (state.IteratorObject is not null && !iteratorDone)
            {
                state.IteratorObject.IteratorClose(context, context.IsThrow);
                if (context.IsThrow)
                {
                    return lastValueJs;
                }
            }

            return lastValueJs;
        }

        private static void SyncIterationSlots(IteratorDriverPlan driverPlan, JsEnvironment iterationEnvironment,
            EvaluationContext context)
        {
            if (driverPlan.IterationSlotCount < 0 ||
                driverPlan.IterationScopeId < 0 ||
                driverPlan.PerIterationSlotIndices.IsDefaultOrEmpty ||
                driverPlan.PerIterationBindings.IsDefaultOrEmpty)
            {
                return;
            }

            if (iterationEnvironment.ScopeId != driverPlan.IterationScopeId)
            {
                return;
            }

            if (!iterationEnvironment.HasSlots)
            {
                return;
            }

            var slotIndices = driverPlan.PerIterationSlotIndices;
            var bindingNames = driverPlan.PerIterationBindings;
            var count = Math.Min(slotIndices.Length, bindingNames.Length);

            for (var i = 0; i < count; i++)
            {
                var slotIndex = slotIndices[i];
                if (slotIndex < 0)
                {
                    continue;
                }

                var binding = bindingNames[i];
                if (!iterationEnvironment.TryGetIdentifierJsValue(binding, context, out var value))
                {
                    continue;
                }

                iterationEnvironment.GetSlotRef(slotIndex) = value;
            }
        }
    }
}
