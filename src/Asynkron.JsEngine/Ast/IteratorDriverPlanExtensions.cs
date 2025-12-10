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
        private object? ExecuteIteratorDriver(IJsObjectLike iterator,
            IEnumerator<object?>? enumerator,
            JsEnvironment loopEnvironment,
            JsEnvironment outerEnvironment,
            EvaluationContext context,
            Symbol? loopLabel)
        {
            object? lastValue = Symbol.Undefined;
            var iteratorDone = false;

            var state = new IteratorDriverState
            {
                IteratorObject = iterator,
                Enumerator = enumerator,
                IsAsyncIterator = plan.Kind == IteratorDriverKind.Await,
                NextMethod = iterator.GetIteratorNextCallable(context)
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

                    nextResult = state.Enumerator.Current;
                }

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();

                    if (state.IteratorObject is not null && !iteratorDone)
                    {
                        IteratorClose(state.IteratorObject, context, preserveExistingThrow: true,
                            existingThrowOverride: thrown);

                        if (context.IsThrow)
                        {
                            thrown = context.FlowValue;
                            context.Clear();
                        }
                    }

                    throw new ThrowSignal(thrown);
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

                    object? value;
                    try
                    {
                        value = resultObj.TryGetProperty("value", out var yielded)
                            ? yielded
                            : Symbol.Undefined;
                    }
                    catch (ThrowSignal)
                    {
                        // IteratorValue abrupts do not trigger IteratorClose (per 7.4.4).
                        throw;
                    }

                    var iterationEnvironment = plan.DeclarationKind is VariableKind.Let or VariableKind.Const
                        ? new JsEnvironment(loopEnvironment, creatingSource: plan.Body.Source,
                            description: "for-each-iteration")
                        : loopEnvironment;

                    try
                    {
                        AssignLoopBinding(plan.Target, value, iterationEnvironment, outerEnvironment, context,
                            plan.DeclarationKind);
                        if (context.IsThrow)
                        {
                            throw new ThrowSignal(context.FlowValue);
                        }

                        lastValue = EvaluateStatement(plan.Body, iterationEnvironment, context, loopLabel);
                        if (context.IsThrow)
                        {
                            throw new ThrowSignal(context.FlowValue);
                        }
                    }
                    catch (ThrowSignal sig)
                    {
                        if (state.IteratorObject is not null && !iteratorDone)
                        {
                            IteratorClose(state.IteratorObject, context, preserveExistingThrow: true);
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
                    iteratorDone = false; // force IteratorClose on exit for abrupt completion paths that require it
                    throw new ThrowSignal(typeError);
                }

                // Enumerator path (non-object next)
                var iterationEnvironment = plan.DeclarationKind is VariableKind.Let or VariableKind.Const
                    ? new JsEnvironment(loopEnvironment, creatingSource: plan.Body.Source,
                        description: "for-each-iteration")
                    : loopEnvironment;

                AssignLoopBinding(plan.Target, nextResult, iterationEnvironment, outerEnvironment, context,
                    plan.DeclarationKind);
                if (context.IsThrow)
                {
                    throw new ThrowSignal(context.FlowValue);
                }

                lastValue = EvaluateStatement(plan.Body, iterationEnvironment, context, loopLabel);
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
                IteratorClose(state.IteratorObject, context, context.IsThrow);
                if (context.IsThrow)
                {
                    return lastValue;
                }
            }

            return lastValue;
        }
    }
}
