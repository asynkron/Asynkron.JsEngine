using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using System.Threading;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Centralizes await handling so evaluators can share the same blocking vs
///     pending behaviour and we have a single place to evolve toward a
///     non-blocking scheduler.
/// </summary>
internal static class AwaitScheduler
{
    // Reusable callback delegates to avoid allocations in hot path
    [ThreadStatic]
    private static PromiseAwaitState? t_cachedState;

    private sealed class PromiseAwaitState
    {
        public int Completed;
        public int Fulfilled;
        public JsValue Value;

        public void Reset()
        {
            Completed = 0;
            Fulfilled = 0;
            Value = JsValue.Undefined;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PromiseAwaitState RentState()
    {
        var state = t_cachedState;
        if (state != null)
        {
            t_cachedState = null;
            state.Reset();
            return state;
        }
        return new PromiseAwaitState();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReturnState(PromiseAwaitState state)
    {
        state.Reset();
        t_cachedState = state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPromiseLike(JsValue candidate)
    {
        // Unwrapped JsPromise instances should still count as promise-like
        if (candidate.ObjectValue is JsPromise)
        {
            return true;
        }

        if (!candidate.TryGetObject(out var obj))
        {
            return false;
        }

        return obj.TryGetProperty("then", out var thenValue) &&
               thenValue.TryGetObject<IJsCallable>(out _);
    }

    /// <summary>
    ///     Fast path: Try to get the resolved value from an already-settled promise
    ///     without any allocations or microtask processing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetSettledValueFast(JsValue candidate, out JsValue value, out bool isRejected)
    {
        // Direct JsPromise check (fastest path)
        if (candidate.IsObject && candidate.ObjectValue is JsPromise directPromise)
        {
            return directPromise.TryGetSettled(out value, out isRejected);
        }

        // JsObject wrapping a JsPromise
        if (candidate.IsObject &&
            candidate.AsObject().TryGetProperty(JsPromise.InternalPromiseKey, out var inner) &&
            inner.TryGetObject<JsPromise>(out var wrappedPromise))
        {
            return wrappedPromise.TryGetSettled(out value, out isRejected);
        }

        value = JsValue.Undefined;
        isRejected = false;
        return false;
    }

    public static bool TryAwaitPromiseSync(
        JsValue candidate,
        EvaluationContext context,
        out JsValue resolvedValue,
        bool drainMicrotasks = true)
    {
        resolvedValue = candidate;

        // Fast path: non-promise values pass through immediately
        if (!candidate.IsObject)
        {
            // Check for direct JsPromise (without JsObject wrapper)
            if (candidate.ObjectValue is JsPromise directPromise)
            {
                return HandleDirectPromise(directPromise, context, out resolvedValue, drainMicrotasks);
            }
            return true;
        }

        var engine = context.RealmState?.Engine;

        // Fast path: Check if promise is already settled BEFORE draining microtasks
        if (TryGetSettledValueFast(candidate, out var settledValue, out var isRejected))
        {
            if (isRejected)
            {
                context.SetThrow(JsValue.FromObject(settledValue));
                resolvedValue = JsValue.Undefined;
                return false;
            }
            resolvedValue = settledValue;
            // Continue to check if settled value is itself a promise
            if (!resolvedValue.IsObject)
            {
                return true;
            }
        }

        // Only drain microtasks if we haven't resolved yet
        if (drainMicrotasks)
        {
            engine?.DrainMicrotasks(force: true);
        }

        // Re-check after draining - promise might have settled
        if (TryGetSettledValueFast(resolvedValue, out settledValue, out isRejected))
        {
            if (isRejected)
            {
                context.SetThrow(JsValue.FromObject(settledValue));
                resolvedValue = JsValue.Undefined;
                return false;
            }
            resolvedValue = settledValue;
            if (!resolvedValue.IsObject)
            {
                return true;
            }
        }

        // Slow path: need to attach handlers and wait
        while (resolvedValue.IsObject && IsPromiseLike(resolvedValue))
        {
            var promiseObj = resolvedValue.AsObject();

            // Check settled state again (might have changed)
            if (TryGetSettledValueFast(resolvedValue, out var loopSettled, out var rejected))
            {
                if (rejected)
                {
                    context.SetThrow(JsValue.FromObject(loopSettled));
                    resolvedValue = JsValue.Undefined;
                    return false;
                }
                resolvedValue = loopSettled;
                continue;
            }

            if (!promiseObj.TryGetProperty("then", out var thenValue) ||
                !thenValue.TryGetObject<IJsCallable>(out var thenCallable))
            {
                break;
            }

            // Rent a reusable state object to minimize allocations
            var awaitState = RentState();

            // Create lightweight callbacks
            var onFulfilled = new AwaitFulfilledCallback(awaitState);
            var onRejected = new AwaitRejectedCallback(awaitState);

            // Wrap callbacks in HostFunction for JsValue compatibility
            var onFulfilledFn = new HostFunction(args => onFulfilled.Invoke(args, JsValue.Undefined), isConstructor: false);
            var onRejectedFn = new HostFunction(args => onRejected.Invoke(args, JsValue.Undefined), isConstructor: false);

            try
            {
                thenCallable.Invoke([JsValue.FromObject(onFulfilledFn), JsValue.FromObject(onRejectedFn)], new JsValue(promiseObj));
            }
            catch (ThrowSignal signal)
            {
                ReturnState(awaitState);
                context.SetThrow(signal.ThrownValue);
                resolvedValue = JsValue.Undefined;
                return false;
            }
            catch (Exception ex)
            {
                ReturnState(awaitState);
                context.SetThrow(JsValue.FromObject(ex.Message));
                resolvedValue = JsValue.Undefined;
                return false;
            }

            // Optimized spin-wait with minimal overhead
            try
            {
                if (Volatile.Read(ref awaitState.Completed) == 0 && drainMicrotasks)
                {
                    // Single microtask drain often resolves synchronous promises
                    engine?.DrainMicrotasks(force: true);

                    if (Volatile.Read(ref awaitState.Completed) == 0)
                    {
                        // Need to wait - use optimized loop
                        WaitForCompletion(awaitState, context, engine);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                ReturnState(awaitState);
                throw;
            }
            catch (ThrowSignal signal)
            {
                ReturnState(awaitState);
                context.SetThrow(signal.ThrownValue);
                resolvedValue = JsValue.Undefined;
                return false;
            }
            catch (Exception ex)
            {
                ReturnState(awaitState);
                context.SetThrow(JsValue.FromObject(ex.Message));
                resolvedValue = JsValue.Undefined;
                return false;
            }

            if (Volatile.Read(ref awaitState.Completed) == 0 && !drainMicrotasks)
            {
                ReturnState(awaitState);
                throw new NotSupportedException(
                    "Promise cannot be awaited synchronously without draining microtasks.");
            }

            var fulfilled = awaitState.Fulfilled;
            var value = awaitState.Value;
            ReturnState(awaitState);

            if (fulfilled == 0)
            {
                context.SetThrow(JsValue.FromObject(value));
                resolvedValue = JsValue.Undefined;
                return false;
            }

            resolvedValue = value;
        }

        return true;
    }

    private static bool HandleDirectPromise(JsPromise promise, EvaluationContext context,
        out JsValue resolvedValue, bool drainMicrotasks)
    {
        var engine = context.RealmState?.Engine;

        // Fast path: already settled
        if (promise.TryGetSettled(out var value, out var isRejected))
        {
            if (isRejected)
            {
                context.SetThrow(JsValue.FromObject(value));
                resolvedValue = JsValue.Undefined;
                return false;
            }
            resolvedValue = value;
            return true;
        }

        // Drain and re-check
        if (drainMicrotasks)
        {
            engine?.DrainMicrotasks(force: true);

            if (promise.TryGetSettled(out value, out isRejected))
            {
                if (isRejected)
                {
                    context.SetThrow(JsValue.FromObject(value));
                    resolvedValue = JsValue.Undefined;
                    return false;
                }
                resolvedValue = value;
                return true;
            }
        }

        // Need to wait via JsObject wrapper
        resolvedValue = new JsValue(promise.JsObject);
        return TryAwaitPromiseSync(resolvedValue, context, out resolvedValue, drainMicrotasks);
    }

    private static void WaitForCompletion(PromiseAwaitState awaitState, EvaluationContext context, JsEngine? engine)
    {
        var iterations = 0;
        const int maxIterations = 10_000;

        while (Volatile.Read(ref awaitState.Completed) == 0)
        {
            context.ThrowIfCancellationRequested();

            // Try draining microtasks first
            engine?.DrainMicrotasks(force: true);

            if (Volatile.Read(ref awaitState.Completed) != 0)
            {
                break;
            }

            // Check event loop if engine available
            if (engine is not null && !engine.IsEventLoopDrained())
            {
                engine.StartEventLoop();
                var drainTask = engine.DrainEventLoopAsync(CancellationToken.None);

                // SpinWait for short-running tasks, then yield
                var spinWait = new SpinWait();
                while (!drainTask.IsCompleted)
                {
                    context.ThrowIfCancellationRequested();
                    spinWait.SpinOnce();
                }

                if (drainTask.IsFaulted)
                {
                    throw drainTask.Exception?.GetBaseException() ??
                          new InvalidOperationException("Event loop drain failed.");
                }

                if (drainTask.IsCanceled)
                {
                    throw new OperationCanceledException(
                        "Event loop drain was canceled while awaiting a promise.");
                }

                engine.DrainMicrotasks(force: true);
            }
            else
            {
                // Use SpinWait for better CPU efficiency
                Thread.SpinWait(10);
            }

            if (++iterations > maxIterations)
            {
                throw new InvalidOperationException(
                    "Promise did not resolve after draining microtasks and the event loop.");
            }
        }
    }

    public static bool TryAwaitPromiseOrSchedule(JsValue candidate, bool asyncStepMode, ref JsValue pendingPromise,
        EvaluationContext context, out JsValue resolvedValue)
    {
        // Normalize raw JsPromise to its object wrapper so async-generator
        // plumbing (which expects JsObject) can attach handlers.
        if (candidate.ObjectValue is JsPromise jsPromise)
        {
            candidate = new JsValue(jsPromise.JsObject);
        }

        // When not running under async-generator step execution, keep the
        // existing blocking semantics.
        if (!asyncStepMode)
        {
            return TryAwaitPromiseSync(candidate, context, out resolvedValue, context.DrainAwaitMicrotasks);
        }

        // Async-aware mode: if this is a promise-like object, surface it as
        // a pending step instead of blocking.
        if (IsPromiseLike(candidate))
        {
            pendingPromise = candidate;
            resolvedValue = JsValue.Undefined;
            return false;
        }

        // Non-promise value in async mode: no need to suspend, just pass
        // the value through.
        resolvedValue = candidate;
        return true;
    }

    /// <summary>
    ///     Lightweight callback for fulfilled promises - avoids closure allocation.
    /// </summary>
    private sealed class AwaitFulfilledCallback : IJsCallable
    {
        private readonly PromiseAwaitState _state;

        public AwaitFulfilledCallback(PromiseAwaitState state) => _state = state;

        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            _state.Value = args.Count > 0 ? args[0] : JsValue.Undefined;
            _state.Fulfilled = 1;
            Interlocked.Exchange(ref _state.Completed, 1);
            return JsValue.Undefined;
        }

        public bool IsConstructor => false;
    }

    /// <summary>
    ///     Lightweight callback for rejected promises - avoids closure allocation.
    /// </summary>
    private sealed class AwaitRejectedCallback : IJsCallable
    {
        private readonly PromiseAwaitState _state;

        public AwaitRejectedCallback(PromiseAwaitState state) => _state = state;

        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            _state.Value = args.Count > 0 ? args[0] : JsValue.Undefined;
            _state.Fulfilled = 0;
            Interlocked.Exchange(ref _state.Completed, 1);
            return JsValue.Undefined;
        }

        public bool IsConstructor => false;
    }
}
