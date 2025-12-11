using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Centralizes await handling so evaluators can share the same blocking vs
///     pending behaviour and we have a single place to evolve toward a
///     non-blocking scheduler.
/// </summary>
internal static class AwaitScheduler
{
    public static bool IsPromiseLike(object? candidate)
    {
        return candidate is JsObject jsObject &&
               jsObject.TryGetProperty("then", out var thenValue) &&
               thenValue is IJsCallable;
    }

    public static bool TryAwaitPromiseSync(
        object? candidate,
        EvaluationContext context,
        out object? resolvedValue,
        bool drainMicrotasks = true)
    {
        resolvedValue = candidate;

        var engine = context.RealmState?.Engine;
        if (drainMicrotasks)
        {
            engine?.DrainMicrotasks();
        }

        if (drainMicrotasks &&
            candidate is JsPromise jsPromise &&
            jsPromise.TryGetSettled(out var settledValue, out var isRejected))
        {
            if (isRejected)
            {
                context.SetThrow(settledValue);
                resolvedValue = Symbol.Undefined;
                return false;
            }

            resolvedValue = settledValue;
            return true;
        }

        while (resolvedValue is JsObject promiseObj && IsPromiseLike(promiseObj))
        {
            if (drainMicrotasks &&
                promiseObj.TryGetProperty("__promise__", out var internalPromise) &&
                internalPromise is JsPromise promise &&
                promise.TryGetSettled(out var settled, out var rejected))
            {
                if (rejected)
                {
                    context.SetThrow(settled);
                    resolvedValue = Symbol.Undefined;
                    return false;
                }

                resolvedValue = settled;
                continue;
            }

            if (!promiseObj.TryGetProperty("then", out var thenValue) ||
                thenValue is not IJsCallable thenCallable)
            {
                break;
            }

            var tcs = new TaskCompletionSource<(bool Success, object? Value)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var onFulfilled = new HostFunction(args =>
            {
                var value = args.GetArgument(0);
                tcs.TrySetResult((true, value));
                return null;
            });

            var onRejected = new HostFunction(args =>
            {
                var value = args.GetArgument(0);
                tcs.TrySetResult((false, value));
                return null;
            });

            try
            {
                thenCallable.Invoke([onFulfilled, onRejected], promiseObj);
            }
            catch (ThrowSignal signal)
            {
                // JavaScript throw - extract the actual thrown value
                context.SetThrow(signal.ThrownValue);
                resolvedValue = Symbol.Undefined;
                return false;
            }
            catch (Exception ex)
            {
                context.SetThrow(ex.Message);
                resolvedValue = Symbol.Undefined;
                return false;
            }

            (bool Success, object? Value) awaited;
            try
            {
                if (drainMicrotasks)
                {
                    var iterations = 0;
                    while (!tcs.Task.IsCompleted)
                    {
                        engine?.DrainMicrotasks();

                        if (tcs.Task.IsCompleted)
                        {
                            break;
                        }

                        if (engine is not null)
                        {
                            engine.StartEventLoop();
                            engine.DrainEventLoopAsync(CancellationToken.None).GetAwaiter().GetResult();
                            engine.DrainMicrotasks();
                        }

                        if (++iterations > 10_000)
                        {
                            throw new InvalidOperationException(
                                "Promise did not resolve after draining microtasks and the event loop.");
                        }
                    }
                }
                awaited = tcs.Task.GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
                throw; // Re-throw our own exceptions
            }
            catch (ThrowSignal signal)
            {
                // JavaScript throw during microtask processing
                context.SetThrow(signal.ThrownValue);
                resolvedValue = Symbol.Undefined;
                return false;
            }
            catch (Exception ex)
            {
                context.SetThrow(ex.Message);
                resolvedValue = Symbol.Undefined;
                return false;
            }

            if (!awaited.Success)
            {
                context.SetThrow(awaited.Value);
                resolvedValue = Symbol.Undefined;
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
            return TryAwaitPromiseSync(candidate, context, out resolvedValue, context.DrainAwaitMicrotasks);
        }

        // Async-aware mode: if this is a promise-like object, surface it as
        // a pending step instead of blocking.
        if (candidate is JsObject promiseObj && IsPromiseLike(promiseObj))
        {
            pendingPromise = promiseObj;
            resolvedValue = Symbol.Undefined;
            return false;
        }

        // Non-promise value in async mode: no need to suspend, just pass
        // the value through.
        resolvedValue = candidate;
        return true;
    }
}
