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
    private object? _value;

    public JsPromise(JsEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        JsObject = new JsObject();
        JsObject.DefineProperty(InternalPromiseKey,
            new PropertyDescriptor { Value = this, Writable = false, Enumerable = false, Configurable = false });
    }

    internal static bool TryGetInternalPromise(object? candidate, out JsPromise? promise)
    {
        if (candidate is JsObject jsObject &&
            jsObject.TryGetProperty(InternalPromiseKey, out var inner) &&
            inner is JsPromise jsPromise)
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
    public void Resolve(object? value)
    {
        if (_state != PromiseState.Pending)
        {
            return;
        }

        // Check if value is a thenable (has a callable 'then' property)
        if (value is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("then", out var thenValue) &&
            thenValue is IJsCallable thenMethod)
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
            thenMethod.Invoke([resolveCallback, rejectCallback], thenable);
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
                Reject(null);
            }
        }
    }

    /// <summary>
    ///     Rejects the promise with the given reason.
    /// </summary>
    public void Reject(object? reason)
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

    internal bool TryGetSettled(out object? value, out bool isRejected)
    {
        if (_state == PromiseState.Pending)
        {
            value = null;
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
                nextPromise.Reject(ex.Message);
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
            var result = onFulfilled.Invoke([_value], null);
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
            var result = onRejected.Invoke([_value], null);
            ResolveWithPossibleThenable(result, nextPromise);
        }
        else
        {
            // No rejection handler, propagate rejection
            nextPromise.Reject(_value);
        }
    }

    private static void ResolveWithPossibleThenable(object? result, JsPromise nextPromise)
    {
        // If the result is a promise (JsObject with "then" method), chain it
        if (result is JsObject resultObj &&
            resultObj.TryGetProperty("then", out var thenMethod) &&
            thenMethod is IJsCallable thenCallable)
        {
            // Use lightweight callback objects instead of closures
            var resolveCallback = new ChainResolveCallback(nextPromise);
            var rejectCallback = new ChainRejectCallback(nextPromise);
            thenCallable.Invoke([resolveCallback, rejectCallback], resultObj);
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

        public object? Invoke(IReadOnlyList<object?> args, object? thisValue)
        {
            if (_promise._state != PromiseState.Pending)
            {
                return null;
            }

            var result = args.Count > 0 ? args[0] : null;
            _promise.Resolve(result);
            return null;
        }

        public bool IsConstructor => false;
    }

    /// <summary>
    ///     Lightweight callback for thenable rejection - avoids closure allocation.
    /// </summary>
    private sealed class ThenableRejectCallback : IJsCallable
    {
        private readonly JsPromise _promise;

        public ThenableRejectCallback(JsPromise promise) => _promise = promise;

        public object? Invoke(IReadOnlyList<object?> args, object? thisValue)
        {
            if (_promise._state != PromiseState.Pending)
            {
                return null;
            }

            var reason = args.Count > 0 ? args[0] : null;
            _promise.Reject(reason);
            return null;
        }

        public bool IsConstructor => false;
    }

    /// <summary>
    ///     Lightweight callback for promise chain resolution - avoids closure allocation.
    /// </summary>
    private sealed class ChainResolveCallback : IJsCallable
    {
        private readonly JsPromise _nextPromise;

        public ChainResolveCallback(JsPromise nextPromise) => _nextPromise = nextPromise;

        public object? Invoke(IReadOnlyList<object?> args, object? thisValue)
        {
            _nextPromise.Resolve(args.Count > 0 ? args[0] : null);
            return null;
        }

        public bool IsConstructor => false;
    }

    /// <summary>
    ///     Lightweight callback for promise chain rejection - avoids closure allocation.
    /// </summary>
    private sealed class ChainRejectCallback : IJsCallable
    {
        private readonly JsPromise _nextPromise;

        public ChainRejectCallback(JsPromise nextPromise) => _nextPromise = nextPromise;

        public object? Invoke(IReadOnlyList<object?> args, object? thisValue)
        {
            _nextPromise.Reject(args.Count > 0 ? args[0] : null);
            return null;
        }

        public bool IsConstructor => false;
    }
}
