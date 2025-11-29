using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IteratorDriverPlan plan)
    {
        private object? ExecuteIteratorDriver(JsObject iterator,
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
                IsAsyncIterator = plan.Kind == IteratorDriverKind.Await
            };

            while (!context.ShouldStopEvaluation)
            {
                context.ThrowIfCancellationRequested();

                object? nextResult = null;
                if (state.IteratorObject is not null)
                {
                    nextResult = InvokeIteratorNext(state.IteratorObject);
                }
                else if (state.Enumerator is not null)
                {
                    if (!state.Enumerator.MoveNext())
                    {
                        break;
                    }

                    nextResult = state.Enumerator.Current;
                }

                if (nextResult is JsObject resultObj)
                {
                    var done = resultObj.TryGetProperty("done", out var doneValue) &&
                               doneValue is bool and true;
                    if (done)
                    {
                        iteratorDone = true;
                        break;
                    }

                    var value = resultObj.TryGetProperty("value", out var yielded)
                        ? yielded
                        : Symbol.Undefined;

                    var iterationEnvironment = plan.DeclarationKind is VariableKind.Let or VariableKind.Const
                        ? new JsEnvironment(loopEnvironment, creatingSource: plan.Body.Source,
                            description: "for-each-iteration")
                        : loopEnvironment;

                    AssignLoopBinding(plan.Target, value, iterationEnvironment, outerEnvironment, context,
                        plan.DeclarationKind);
                    lastValue = EvaluateStatement(plan.Body, iterationEnvironment, context, loopLabel);
                }
                else
                {
                    if (state.IteratorObject is not null)
                    {
                        throw new ThrowSignal(StandardLibrary.CreateTypeError(
                            "Iterator.next() did not return an object", context, context.RealmState));
                    }

                    // Enumerator path (non-object next)
                    var iterationEnvironment = plan.DeclarationKind is VariableKind.Let or VariableKind.Const
                        ? new JsEnvironment(loopEnvironment, creatingSource: plan.Body.Source,
                            description: "for-each-iteration")
                        : loopEnvironment;

                    AssignLoopBinding(plan.Target, nextResult, iterationEnvironment, outerEnvironment, context,
                        plan.DeclarationKind);
                    lastValue = EvaluateStatement(plan.Body, iterationEnvironment, context, loopLabel);
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
            }

            return lastValue;
        }
    }
}
