#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

#endregion

// ResolvedPromiseValue is internal in JsTypes namespace, accessible here

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Centralizes await handling so evaluators can share the same blocking vs
///     pending behaviour and we have a single place to evolve toward a
///     non-blocking scheduler.
/// </summary>
internal static class AwaitScheduler
{
    // Reusable callback delegates to avoid allocations in hot path
    [ThreadStatic] private static PromiseAwaitState? TCachedState;

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
        // Fast path: check internal promise slot/marker without property lookups
        if (JsPromise.TryGetInternalPromise(candidate, out _))
        {
            return true;
        }

        if (!candidate.TryGetObject(out var obj))
        {
            return false;
        }

        return obj.TryGetProperty("then", out var thenValue) &&
               thenValue.TryGetCallable(out _);
    }

    /// <summary>
    ///     Fast path: Try to get the resolved value from an already-settled promise
    ///     without any allocations or microtask processing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetSettledValueFast(JsValue candidate, out JsValue value, out bool isRejected)
    {
        // Direct JsPromise check (fastest path)
        if (candidate is { IsObject: true, ObjectValue: JsPromise directPromise })
        {
            return directPromise.TryGetSettled(out value, out isRejected);
        }

        // JsObject wrapping a JsPromise - use the internal slot first, then fallback to storage.
        if (candidate.TryGetObject<JsObject>(out var jsObject) &&
            (jsObject.TryGetPromiseSlot(out var slotPromise) ||
             (jsObject.TryGetJsValue(JsPromise.InternalPromiseKey, out var inner) &&
              inner.TryGetPromise(out slotPromise))) &&
            slotPromise is not null)
        {
            return slotPromise.TryGetSettled(out value, out isRejected);
        }

        // Fallback for other IJsObjectLike types (JsArray etc.)
        if (candidate.IsObject &&
            candidate.TryGetObjectLike(out var obj) &&
            obj.TryGetProperty(JsPromise.InternalPromiseKey, out var fallbackInner) &&
            fallbackInner.TryGetPromise(out var fallbackPromise))
        {
            return fallbackPromise.TryGetSettled(out value, out isRejected);
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

        // Slow path: need to attach handlers - but NO BLOCKING
        while (resolvedValue.IsObject && IsPromiseLike(resolvedValue))
        {
            // Use IJsObjectLike to handle JsArray etc. that might be promise-like
            if (!resolvedValue.TryGetObjectLike(out var promiseObj))
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
                !thenValue.TryGetCallable(out var thenCallable))
            {
                break;
            }

            // Rent a reusable state object to minimize allocations
            var awaitState = RentState();

            // Create lightweight callbacks
            var onFulfilled = new AwaitFulfilledCallback(awaitState);
            var onRejected = new AwaitRejectedCallback(awaitState);

            // Wrap callbacks in HostFunction for JsValue compatibility
            var onFulfilledFn =
                new HostFunction(args => onFulfilled.Invoke(args, JsValue.Undefined), isConstructor: false);
            var onRejectedFn =
                new HostFunction(args => onRejected.Invoke(args, JsValue.Undefined), isConstructor: false);

            try
            {
                thenCallable.Invoke([(JsValue)onFulfilledFn, (JsValue)onRejectedFn],
                    JsValue.FromObjectUnsafe(promiseObj));
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

            // Drain microtasks ONCE - no blocking/spinning
            if (drainMicrotasks)
            {
                engine?.DrainMicrotasks(force: true);
            }

            // Check if completed after drain
            if (Volatile.Read(ref awaitState.Completed) == 0)
            {
                // Promise is still pending - return false to signal caller should yield
                // Don't block! Let the caller handle suspension/resumption
                ReturnState(awaitState);
                return false;
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

    public static bool TryResolvePromiseOrYield(JsValue candidate, bool asyncStepMode, ref JsValue pendingPromise,
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

        // Async-aware mode: per ECMAScript spec, await should ALWAYS create an
        // async boundary, even for already-settled promises. This ensures correct
        // ordering where synchronous code after the async function call runs before
        // the awaited value is processed.
        //
        // Example:
        //   let x = 0;
        //   (async () => { x = await Promise.resolve(42); })();
        //   console.log(x);  // Should print 0, not 42
        //
        // So we must suspend even for settled promises to maintain spec compliance.
        if (IsPromiseLike(candidate))
        {
            // Promise is pending - suspend and attach handlers
            pendingPromise = candidate;
            resolvedValue = JsValue.Undefined;
            return false;
        }

        // Non-promise value: wrap in a lightweight resolved promise to ensure proper
        // microtask scheduling. This is critical for `for await` over sync iterables -
        // each value must suspend to allow synchronous code after the async function
        // call to execute before the loop continues.
        //
        // Use ResolvedPromiseValue instead of full Promise.resolve() to avoid
        // JsPromise + JsObject allocation overhead. Pool is used to reduce allocations.
        var engine = context.RealmState.Engine;
        if (engine is not null)
        {
            var resolvedPromise = ResolvedPromiseValue.Rent(candidate, engine);
            pendingPromise = resolvedPromise.AsJsValue;
            resolvedValue = JsValue.Undefined;
            return false;
        }

        // Fallback: if we can't get the engine, pass through synchronously
        // (this shouldn't happen in normal operation)
        resolvedValue = candidate;
        return true;
    }

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
