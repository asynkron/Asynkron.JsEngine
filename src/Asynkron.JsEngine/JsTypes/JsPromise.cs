#region

using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript Promise object that can be resolved or rejected.
///     Implements IMicrotask directly to avoid delegate allocation when scheduling handler processing.
/// </summary>
public sealed class JsPromise(JsEngine engine) : IMicrotask
{
    internal const string InternalPromiseKey = "__promise__";

    private readonly JsEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private List<(IJsCallable? onFulfilled, IJsCallable? onRejected, JsPromise next)>? _handlers;

    private bool _handlersScheduled;

    // Spare list for swapping during ProcessHandlersCore - avoids ToArray and locks
    private List<(IJsCallable? onFulfilled, IJsCallable? onRejected, JsPromise next)>? _spareHandlers;

    private PromiseState _state = PromiseState.Pending;
    private JsValue _value;
    private JsObject? _jsObject;

    /// <summary>
    ///     The epoch in which this promise's handler processing was scheduled.
    ///     Used for selective microtask draining during module loading.
    /// </summary>
    int IMicrotask.Epoch { get; set; }

    // JsObject is now created lazily to avoid allocation when not needed

    // Debug helpers for instrumentation
    internal int DebugHandlerCount => _handlers?.Count ?? 0;
    internal string DebugState => _state.ToString();
    internal int DebugId => RuntimeHelpers.GetHashCode(this);

    /// <summary>
    ///     Gets the underlying JsObject for property access.
    ///     Created lazily to avoid allocation when only internal promise machinery is used.
    /// </summary>
    public JsObject JsObject
    {
        get
        {
            if (_jsObject is null)
            {
                _jsObject = new JsObject();
                _jsObject.SetPromiseSlot(this);
            }
            return _jsObject;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetInternalPromise(JsValue candidate, out JsPromise? promise)
    {
        if (!candidate.IsObject)
        {
            promise = null;
            return false;
        }

        if (candidate.TryGetObject<JsPromise>(out var directPromise))
        {
            promise = directPromise;
            return true;
        }

        // Fast path: direct JsObject with internal storage or slot
        if (candidate.TryGetObject<JsObject>(out var jsObject) &&
            (jsObject.TryGetPromiseSlot(out var slotPromise) ||
             (jsObject.TryGetJsValue(InternalPromiseKey, out var inner) &&
              inner.TryGetObject(out slotPromise))) &&
            slotPromise is not null)
        {
            promise = slotPromise;
            return true;
        }

        // Fallback for other object types or when property was set differently
        if (candidate.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
            accessor.TryGetProperty(InternalPromiseKey, out var fallbackInner) &&
            fallbackInner.TryGetObject<JsPromise>(out var fallbackPromise))
        {
            promise = fallbackPromise;
            return true;
        }

        promise = null;
        return false;
    }

    /// <summary>
    ///     Resolves the promise with the given value.
    ///     If the value is a thenable, the promise adopts the thenable's eventual state.
    /// </summary>
    public void Resolve(JsValue value)
    {
        if (_state != PromiseState.Pending)
        {
            return;
        }

        // Check if value is a thenable (has a callable 'then' property)
        if (value.IsObject &&
            value.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
            accessor.TryGetProperty("then", out var thenValue) &&
            thenValue.TryGetObject<IJsCallable>(out var thenMethod))
        {
            // Value is a thenable - adopt its state
            ResolveThenable(accessor, thenMethod);
            return;
        }

        // Value is not a thenable - fulfill directly
        _state = PromiseState.Fulfilled;
        _value = value;
        ScheduleProcessing();
    }

    /// <summary>
    ///     Resolves the promise by adopting the state of a thenable.
    /// </summary>
    private void ResolveThenable(IJsPropertyAccessor thenable, IJsCallable thenMethod)
    {
        // Create resolve and reject callbacks for the thenable
        // Use lightweight IJsCallable implementations directly - no HostFunction wrapper needed
        var resolveCallback = new ThenableResolveCallback(this);
        var rejectCallback = new ThenableRejectCallback(this);

        try
        {
            // Call thenable.then(resolve, reject)
            // IJsCallable converts to JsValue via FromObjectUnsafe without allocation
            var thenableValue = JsValue.FromObjectUnsafe(thenable);
            thenMethod.Invoke([JsValue.FromObjectUnsafe(resolveCallback), JsValue.FromObjectUnsafe(rejectCallback)],
                thenableValue);
        }
        catch (ThrowSignal signal)
        {
            // If then() throws, reject the promise
            if (_state == PromiseState.Pending)
            {
                Reject(signal.ThrownValue);
            }
        }
        catch (Exception)
        {
            // If then() throws any other exception, reject with undefined
            if (_state == PromiseState.Pending)
            {
                Reject(JsValue.Undefined);
            }
        }
    }

    /// <summary>
    ///     Rejects the promise with the given reason.
    /// </summary>
    public void Reject(JsValue reason)
    {
        if (_state != PromiseState.Pending)
        {
            return;
        }

        _state = PromiseState.Rejected;
        _value = reason;
        ScheduleProcessing();
    }

    /// <summary>
    ///     Registers callbacks for when the promise is fulfilled or rejected.
    /// </summary>
    public JsPromise Then(IJsCallable? onFulfilled, IJsCallable? onRejected = null)
    {
        var nextPromise = new JsPromise(_engine);
        (_handlers ??= []).Add((onFulfilled, onRejected, nextPromise));

        if (_state != PromiseState.Pending)
        {
            ScheduleProcessing();
        }

        return nextPromise;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetSettled(out JsValue value, out bool isRejected)
    {
        if (_state == PromiseState.Pending)
        {
            value = JsValue.Undefined;
            isRejected = false;
            return false;
        }

        value = _value;
        isRejected = _state == PromiseState.Rejected;
        return true;
    }

    private void ScheduleProcessing()
    {
        if (_handlersScheduled)
        {
            return;
        }

        _handlersScheduled = true;
        // Queue this promise directly as a microtask - no delegate allocation needed
        _engine.QueueMicrotask(this);
    }

    /// <summary>
    ///     IMicrotask.Execute - processes all registered handlers for this promise.
    /// </summary>
    void IMicrotask.Execute()
    {
        try
        {
            ProcessHandlersCore();
        }
        finally
        {
            _handlersScheduled = false;
        }
    }

    private void ProcessHandlersCore()
    {
        if (_state == PromiseState.Pending)
        {
            return;
        }

        // Process handlers without allocating if possible
        var count = _handlers?.Count ?? 0;
        if (count == 0)
        {
            return;
        }

        // Swap handlers list with spare to avoid ToArray allocation (no locks needed)
        // This allows new handlers to be added during processing
        var handlersToProcess = _handlers!;
        _handlers = _spareHandlers;
        _spareHandlers = null;

        for (var i = 0; i < handlersToProcess.Count; i++)
        {
            var (onFulfilled, onRejected, nextPromise) = handlersToProcess[i];
            try
            {
                if (++_engine.PromiseCallDepth > _engine.MaxCallDepth)
                {
                    throw new InvalidOperationException(
                        $"Exceeded maximum call depth of {_engine.MaxCallDepth} while resolving promise callbacks.");
                }

                if (_state == PromiseState.Fulfilled)
                {
                    ProcessFulfilledHandler(onFulfilled, nextPromise);
                }
                else if (_state == PromiseState.Rejected)
                {
                    ProcessRejectedHandler(onRejected, nextPromise);
                }
            }
            catch (Exception ex)
            {
                nextPromise.Reject(new JsValue(ex.Message));
            }
            finally
            {
                _engine.PromiseCallDepth = Math.Max(0, _engine.PromiseCallDepth - 1);
            }
        }

        // Save processed list as spare for next time (no allocation next call)
        handlersToProcess.Clear();
        _spareHandlers = handlersToProcess;

        if (_handlers?.Count > 0)
        {
            ScheduleProcessing();
        }
    }

    private void ProcessFulfilledHandler(IJsCallable? onFulfilled, JsPromise nextPromise)
    {
        if (onFulfilled != null)
        {
            var result = onFulfilled.Invoke(new SingleValueArgs(_value), JsValue.Undefined);
            ResolveWithPossibleThenable(result, nextPromise);
        }
        else
        {
            nextPromise.Resolve(_value);
        }
    }

    private void ProcessRejectedHandler(IJsCallable? onRejected, JsPromise nextPromise)
    {
        if (onRejected != null)
        {
            var result = onRejected.Invoke(new SingleValueArgs(_value), JsValue.Undefined);
            ResolveWithPossibleThenable(result, nextPromise);
        }
        else
        {
            // No rejection handler, propagate rejection
            nextPromise.Reject(_value);
        }
    }

    private static void ResolveWithPossibleThenable(JsValue result, JsPromise nextPromise)
    {
        // If the result is a promise (JsObject with "then" method), chain it
        if (result.IsObject &&
            result.AsObject()!.TryGetProperty("then", out var thenMethod) &&
            thenMethod.TryGetObject<IJsCallable>(out var thenCallable))
        {
            // Use lightweight callback objects directly - no HostFunction wrapper needed
            // IJsCallable converts to JsValue via FromObjectUnsafe without allocation
            var resolveCallback = new ChainResolveCallback(nextPromise);
            var rejectCallback = new ChainRejectCallback(nextPromise);
            thenCallable.Invoke([JsValue.FromObjectUnsafe(resolveCallback), JsValue.FromObjectUnsafe(rejectCallback)],
                result);
        }
        else
        {
            nextPromise.Resolve(result);
        }
    }

    private enum PromiseState
    {
        Pending,
        Fulfilled,
        Rejected
    }

    /// <summary>
    ///     Lightweight callback for thenable resolution - avoids closure allocation.
    /// </summary>
    private sealed class ThenableResolveCallback(JsPromise promise) : IJsCallable
    {
        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            if (promise._state != PromiseState.Pending)
            {
                return JsValue.Undefined;
            }

            var result = args.Count > 0 ? args[0] : JsValue.Undefined;
            promise.Resolve(result);
            return JsValue.Undefined;
        }
    }

    /// <summary>
    ///     Lightweight callback for thenable rejection - avoids closure allocation.
    /// </summary>
    private sealed class ThenableRejectCallback(JsPromise promise) : IJsCallable
    {
        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            if (promise._state != PromiseState.Pending)
            {
                return JsValue.Undefined;
            }

            var reason = args.Count > 0 ? args[0] : JsValue.Undefined;
            promise.Reject(reason);
            return JsValue.Undefined;
        }
    }

    /// <summary>
    ///     Lightweight callback for promise chain resolution - avoids closure allocation.
    /// </summary>
    private sealed class ChainResolveCallback(JsPromise nextPromise) : IJsCallable
    {
        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            nextPromise.Resolve(args.Count > 0 ? args[0] : JsValue.Undefined);
            return JsValue.Undefined;
        }
    }

    /// <summary>
    ///     Lightweight callback for promise chain rejection - avoids closure allocation.
    /// </summary>
    private sealed class ChainRejectCallback(JsPromise nextPromise) : IJsCallable
    {
        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            nextPromise.Reject(args.Count > 0 ? args[0] : JsValue.Undefined);
            return JsValue.Undefined;
        }
    }
}
