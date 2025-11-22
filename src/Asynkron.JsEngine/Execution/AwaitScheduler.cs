using Asynkron.JsEngine.JsTypes;
using JsSymbols = Asynkron.JsEngine.Ast.Symbols;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Centralizes await handling so evaluators can share the same blocking vs
/// pending behaviour and we have a single place to evolve toward a
/// non-blocking scheduler.
/// </summary>
internal static class AwaitScheduler
{
    public static bool IsPromiseLike(object? candidate)
    {
        return candidate is JsObject jsObject &&
               jsObject.TryGetProperty("then", out var thenValue) &&
               thenValue is IJsCallable;
    }

    public static bool TryAwaitPromiseSync(object? candidate, EvaluationContext context, out object? resolvedValue)
    {
        resolvedValue = candidate;

        while (resolvedValue is JsObject promiseObj && IsPromiseLike(promiseObj))
        {
            if (!promiseObj.TryGetProperty("then", out var thenValue) || thenValue is not IJsCallable thenCallable)
            {
                break;
            }

            var tcs = new TaskCompletionSource<(bool Success, object? Value)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var onFulfilled = new HostFunction(args =>
            {
                var value = args.Count > 0 ? args[0] : JsSymbols.Undefined;
                tcs.TrySetResult((true, value));
                return null;
            });

            var onRejected = new HostFunction(args =>
            {
                var value = args.Count > 0 ? args[0] : JsSymbols.Undefined;
                tcs.TrySetResult((false, value));
                return null;
            });

            try
            {
                thenCallable.Invoke([onFulfilled, onRejected], promiseObj);
            }
            catch (Exception ex)
            {
                context.SetThrow(ex.Message);
                resolvedValue = JsSymbols.Undefined;
                return false;
            }

            (bool Success, object? Value) awaited;
            try
            {
                if (tcs.Task.IsCompleted)
                {
                    awaited = tcs.Task.GetAwaiter().GetResult();
                }
                else
                {
                    //TODO: ENSURE ALL CODE IS LOWERED TO ASYNC, THEN REMOVE THIS
                    // DO NOT REPLACE WITH BLOCKING WAIT:
                    throw new InvalidOperationException(
                        "Asynchronous promise resolution is not supported in the synchronous evaluator.");
                }
            }
            catch (Exception ex)
            {
                context.SetThrow(ex.Message);
                resolvedValue = JsSymbols.Undefined;
                return false;
            }

            if (!awaited.Success)
            {
                context.SetThrow(awaited.Value);
                resolvedValue = JsSymbols.Undefined;
                return false;
            }

            resolvedValue = awaited.Value;
        }

        return true;
    }

    public static bool TryAwaitPromiseOrSchedule(object? candidate, bool asyncStepMode, ref object? pendingPromise,
        EvaluationContext context, out object? resolvedValue)
    {
        // When not running under async-generator step execution, keep the
        // existing blocking semantics.
        if (!asyncStepMode)
        {
            return TryAwaitPromiseSync(candidate, context, out resolvedValue);
        }

        // Async-aware mode: if this is a promise-like object, surface it as
        // a pending step instead of blocking.
        if (candidate is JsObject promiseObj && IsPromiseLike(promiseObj))
        {
            pendingPromise = promiseObj;
            resolvedValue = JsSymbols.Undefined;
            return false;
        }

        // Non-promise value in async mode: no need to suspend, just pass
        // the value through.
        resolvedValue = candidate;
        return true;
    }
}
