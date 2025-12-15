using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript Promise object that can be resolved or rejected.
/// </summary>
public sealed class JsPromise
{
    internal const string InternalPromiseKey = "__promise__";
    private readonly JsEngine _engine;
    private readonly List<(IJsCallable? onFulfilled, IJsCallable? onRejected, JsPromise next)> _handlers = [];
    private bool _handlersScheduled;

    private PromiseState _state = PromiseState.Pending;
    private JsValue _value;

    public JsPromise(JsEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        JsObject = new JsObject();
        JsObject.DefineProperty(InternalPromiseKey,
            new PropertyDescriptor { Value = this, Writable = false, Enumerable = false, Configurable = false });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetInternalPromise(JsValue candidate, out JsPromise? promise)
    {
        if (candidate.IsObject &&
            candidate.AsObject().TryGetProperty(InternalPromiseKey, out var inner) &&
            inner.TryGetObject<JsPromise>(out var jsPromise))
        {
            promise = jsPromise;
            return true;
        }

        promise = null;
        return false;
    }

    /// <summary>
    ///     Gets the underlying JsObject for property access.
    /// </summary>
    public JsObject JsObject { get; }

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
        var resolveCallback = new ThenableResolveCallback(this);
        var rejectCallback = new ThenableRejectCallback(this);

        try
        {
            // Call thenable.then(resolve, reject)
            // Wrap callbacks in HostFunction so they can be passed as JsValue
            var resolveFn = new HostFunction(args => { resolveCallback.Invoke(args, JsValue.Undefined); return JsValue.Undefined; }, isConstructor: false);
            var rejectFn = new HostFunction(args => { rejectCallback.Invoke(args, JsValue.Undefined); return JsValue.Undefined; }, isConstructor: false);
            var thenableValue = JsValue.FromObject(thenable);
            thenMethod.Invoke([JsValue.FromObject(resolveFn), JsValue.FromObject(rejectFn)], thenableValue);
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
        _handlers.Add((onFulfilled, onRejected, nextPromise));

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
        // Use the synchronous microtask queue for promise handlers
        // This ensures proper ordering during top-level await
        _engine.QueueMicrotask(() =>
        {
            try
            {
                ProcessHandlersCore();
            }
            finally
            {
                _handlersScheduled = false;
            }
        });
    }

    private void ProcessHandlersCore()
    {
        if (_state == PromiseState.Pending)
        {
            return;
        }

        // Process handlers without allocating if possible
        var count = _handlers.Count;
        if (count == 0)
        {
            return;
        }

        // Copy handlers to local array to allow mutation during processing
        // For small counts, use stack allocation pattern
        var handlersToProcess = count <= 4
            ? _handlers.ToArray()  // Small array, minimal overhead
            : _handlers.ToList().ToArray();  // Larger, use list for efficiency
        _handlers.Clear();

        for (var i = 0; i < handlersToProcess.Length; i++)
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

        if (_handlers.Count > 0)
        {
            ScheduleProcessing();
        }
    }

    private void ProcessFulfilledHandler(IJsCallable? onFulfilled, JsPromise nextPromise)
    {
        if (onFulfilled != null)
        {
            var result = onFulfilled.Invoke([_value], JsValue.Undefined);
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
            var result = onRejected.Invoke([_value], JsValue.Undefined);
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
            result.AsObject().TryGetProperty("then", out var thenMethod) &&
            thenMethod.TryGetObject<IJsCallable>(out var thenCallable))
        {
            // Use lightweight callback objects instead of closures
            var resolveCallback = new ChainResolveCallback(nextPromise);
            var rejectCallback = new ChainRejectCallback(nextPromise);
            // Wrap callbacks in HostFunction so they can be passed as JsValue
            var resolveFn = new HostFunction(args => { resolveCallback.Invoke(args, JsValue.Undefined); return JsValue.Undefined; }, isConstructor: false);
            var rejectFn = new HostFunction(args => { rejectCallback.Invoke(args, JsValue.Undefined); return JsValue.Undefined; }, isConstructor: false);
            thenCallable.Invoke([JsValue.FromObject(resolveFn), JsValue.FromObject(rejectFn)], result);
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
    private sealed class ThenableResolveCallback : IJsCallable
    {
        private readonly JsPromise _promise;

        public ThenableResolveCallback(JsPromise promise) => _promise = promise;

        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            if (_promise._state != PromiseState.Pending)
            {
                return JsValue.Undefined;
            }

            var result = args.Count > 0 ? args[0] : JsValue.Undefined;
            _promise.Resolve(result);
            return JsValue.Undefined;
        }
    }

    /// <summary>
    ///     Lightweight callback for thenable rejection - avoids closure allocation.
    /// </summary>
    private sealed class ThenableRejectCallback : IJsCallable
    {
        private readonly JsPromise _promise;

        public ThenableRejectCallback(JsPromise promise) => _promise = promise;

        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            if (_promise._state != PromiseState.Pending)
            {
                return JsValue.Undefined;
            }

            var reason = args.Count > 0 ? args[0] : JsValue.Undefined;
            _promise.Reject(reason);
            return JsValue.Undefined;
        }
    }

    /// <summary>
    ///     Lightweight callback for promise chain resolution - avoids closure allocation.
    /// </summary>
    private sealed class ChainResolveCallback : IJsCallable
    {
        private readonly JsPromise _nextPromise;

        public ChainResolveCallback(JsPromise nextPromise) => _nextPromise = nextPromise;

        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            _nextPromise.Resolve(args.Count > 0 ? args[0] : JsValue.Undefined);
            return JsValue.Undefined;
        }
    }

    /// <summary>
    ///     Lightweight callback for promise chain rejection - avoids closure allocation.
    /// </summary>
    private sealed class ChainRejectCallback : IJsCallable
    {
        private readonly JsPromise _nextPromise;

        public ChainRejectCallback(JsPromise nextPromise) => _nextPromise = nextPromise;

        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            _nextPromise.Reject(args.Count > 0 ? args[0] : JsValue.Undefined);
            return JsValue.Undefined;
        }
    }
}
