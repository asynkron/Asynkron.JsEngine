using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

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

                    plan.Target.AssignLoopBinding(enumeratorValue, iterationEnvironment, outerEnvironment, context,
                        plan.DeclarationKind);
                    if (context.IsThrow)
                    {
                        throw new ThrowSignal(context.FlowValue);
                    }

                    IteratorDriverPlan.SyncIterationSlots(plan, iterationEnvironment, context);

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
                        state.IteratorObject.IteratorClose(context, preserveExistingThrow: true,
                            existingThrowOverride: thrown);

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
                    nextResult = jsVal.ToObject();
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
                    try
                    {
                        // TryGetProperty returns JsValue, keep as JsValue to avoid boxing
                        value = resultObj.TryGetProperty("value", out var yielded)
                            ? yielded
                            : JsValue.Undefined;
                    }
                    catch (ThrowSignal)
                    {
                        // IteratorValue abrupts do not trigger IteratorClose (per 7.4.4).
                        throw;
                    }

                    var iterationEnvironment = plan.DeclarationKind is VariableKind.Let or VariableKind.Const
                        or VariableKind.Using or VariableKind.AwaitUsing
                        ? rentIterationEnvironment?.Invoke() ?? new JsEnvironment(loopEnvironment,
                            creatingSource: plan.Body.Source, description: "for-each-iteration")
                        : loopEnvironment;

                    try
                    {
                        plan.Target.AssignLoopBinding(value, iterationEnvironment, outerEnvironment, context,
                            plan.DeclarationKind);
                        if (context.IsThrow)
                        {
                            throw new ThrowSignal(context.FlowValue);
                        }

                        IteratorDriverPlan.SyncIterationSlots(plan, iterationEnvironment, context);

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
                            state.IteratorObject.IteratorClose(context, preserveExistingThrow: true);
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
                        context.SetThrow(JsValue.FromObjectUnsafe(typeError));
                        iteratorDone =
                            false; // force IteratorClose on exit for abrupt completion paths that require it
                        throw new ThrowSignal(JsValue.FromObjectUnsafe(typeError));
                    }

                    // Enumerator path (non-object next)
                    var iterationEnvironment = plan.DeclarationKind is VariableKind.Let or VariableKind.Const
                        or VariableKind.Using or VariableKind.AwaitUsing
                        ? rentIterationEnvironment?.Invoke() ?? new JsEnvironment(loopEnvironment,
                            creatingSource: plan.Body.Source, description: "for-each-iteration")
                        : loopEnvironment;

                    plan.Target.AssignLoopBinding(nextResult, iterationEnvironment, outerEnvironment, context,
                        plan.DeclarationKind);
                    if (context.IsThrow)
                    {
                        throw new ThrowSignal(context.FlowValue);
                    }

                    IteratorDriverPlan.SyncIterationSlots(plan, iterationEnvironment, context);

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

                iterationEnvironment.SetSlot(0, slotIndex, value);
            }
        }
    }
}
