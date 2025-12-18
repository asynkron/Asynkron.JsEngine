using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

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
    private static PromiseAwaitState? TCachedState;

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
        var state = TCachedState;
        if (state != null)
        {
            TCachedState = null;
            state.Reset();
            return state;
        }
        return new PromiseAwaitState();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReturnState(PromiseAwaitState state)
    {
        state.Reset();
        TCachedState = state;
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

        // JsObject wrapping a JsPromise (use IJsObjectLike to handle JsArray etc.)
        if (candidate.IsObject &&
            candidate.TryGetObject<IJsObjectLike>(out var obj) &&
            obj.TryGetProperty(JsPromise.InternalPromiseKey, out var inner) &&
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

        var engine = context.RealmState.Engine;

        // Fast path: Check if promise is already settled BEFORE draining microtasks
        if (TryGetSettledValueFast(candidate, out var settledValue, out var isRejected))
        {
            if (isRejected)
            {
                // settledValue is already a JsValue from TryGetSettledValueFast
                context.SetThrow(settledValue);
                resolvedValue = JsValue.Undefined;
                return false;
            }
            resolvedValue = settledValue;
            // Continue to check if a settled value is itself a promise
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
                // settledValue is already JsValue from TryGetSettledValueFast
                context.SetThrow(settledValue);
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
            // Use IJsObjectLike to handle JsArray etc. that might be promise-like
            if (!resolvedValue.TryGetObject<IJsObjectLike>(out var promiseObj))
            {
                return true; // Not an object, we're done
            }

            // Check settled state again (might have changed)
            if (TryGetSettledValueFast(resolvedValue, out var loopSettled, out var rejected))
            {
                if (rejected)
                {
                    // loopSettled is already JsValue from TryGetSettledValueFast
                    context.SetThrow(loopSettled);
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
                thenCallable.Invoke([(JsValue)onFulfilledFn, (JsValue)onRejectedFn], JsValue.FromObjectUnsafe(promiseObj));
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
                context.SetThrow((JsValue)ex.Message);
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
                context.SetThrow((JsValue)ex.Message);
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
            // awaitState.Value is already JsValue
            var value = awaitState.Value;
            ReturnState(awaitState);

            if (fulfilled == 0)
            {
                // value is already JsValue
                context.SetThrow(value);
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
        var engine = context.RealmState.Engine;

        // Fast path: already settled
        if (promise.TryGetSettled(out var value, out var isRejected))
        {
            if (isRejected)
            {
                // value is already JsValue from TryGetSettled
                context.SetThrow(value);
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
                    // value is already JsValue from TryGetSettled
                    context.SetThrow(value);
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
            if (engine?.IsEventLoopDrained() == false)
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

        // Async-aware mode: per ECMAScript spec, await should ALWAYS queue a
        // microtask continuation, even for non-promise values (they get wrapped
        // in Promise.resolve()). This ensures correct ordering of async operations
        // relative to synchronous code.

        if (IsPromiseLike(candidate))
        {
            // Already a promise - suspend and attach handlers
            pendingPromise = candidate;
            resolvedValue = JsValue.Undefined;
            return false;
        }

        // Non-promise value: wrap in Promise.resolve() to ensure proper microtask
        // scheduling. This is critical for `for await` over sync iterables - each
        // value must suspend to allow synchronous code after the async function
        // call to execute before the loop continues.
        var promiseCtor = context.RealmState.PromiseConstructor;
        if (promiseCtor is IJsPropertyAccessor accessor)
        {
            // Use Promise.resolve() to wrap the value
            if (accessor.TryGetProperty("resolve", out var resolveMethod) &&
                resolveMethod.TryUnwrap(out IJsCallable? resolveCallable))
            {
                var promiseCtorValue = JsValue.FromObjectUnsafe(promiseCtor);
                var wrappedPromise = resolveCallable.Invoke([candidate], promiseCtorValue);
                if (wrappedPromise.TryGetObject<JsObject>(out var promiseObj))
                {
                    pendingPromise = new JsValue(promiseObj);
                    resolvedValue = JsValue.Undefined;
                    return false;
                }
            }
        }

        // Fallback: if we can't create a promise, pass through synchronously
        // (this shouldn't happen in normal operation)
        resolvedValue = candidate;
        return true;
    }

    /// <summary>
    ///     Lightweight callback for fulfilled promises - avoids closure allocation.
    /// </summary>
    private sealed class AwaitFulfilledCallback(PromiseAwaitState state) : IJsCallable
    {
        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            state.Value = args.Count > 0 ? args[0] : JsValue.Undefined;
            state.Fulfilled = 1;
            Interlocked.Exchange(ref state.Completed, 1);
            return JsValue.Undefined;
        }
    }

    /// <summary>
    ///     Lightweight callback for rejected promises - avoids closure allocation.
    /// </summary>
    private sealed class AwaitRejectedCallback(PromiseAwaitState state) : IJsCallable
    {
        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            state.Value = args.Count > 0 ? args[0] : JsValue.Undefined;
            state.Fulfilled = 0;
            Interlocked.Exchange(ref state.Completed, 1);
            return JsValue.Undefined;
        }
    }
}
