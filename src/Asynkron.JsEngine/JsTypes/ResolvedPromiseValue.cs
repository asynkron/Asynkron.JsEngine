namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     A lightweight resolved promise that avoids the overhead of creating a full JsPromise + JsObject.
///     Used for wrapping non-promise values in async/await contexts where we need promise-like semantics
///     but the value is already known and resolved.
/// </summary>
internal sealed class ResolvedPromiseValue : IJsPropertyAccessor
{
    private readonly JsValue _value;
    private readonly JsEngine _engine;

    public ResolvedPromiseValue(JsValue value, JsEngine engine)
    {
        _value = value;
        _engine = engine;
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        switch (name)
        {
            case "then":
                value = JsValue.FromObjectUnsafe(new ThenMethod(this, _engine));
                return true;
            case "catch":
                // catch is just then(undefined, onRejected) - but we're resolved, so it returns this
                value = JsValue.FromObjectUnsafe(new CatchMethod(this, _engine));
                return true;
            case "finally":
                value = JsValue.FromObjectUnsafe(new FinallyMethod(this, _engine));
                return true;
            default:
                value = JsValue.Undefined;
                return false;
        }
    }

    public void SetProperty(string name, JsValue value)
    {
        // ResolvedPromiseValue is immutable - property sets are ignored
    }

    /// <summary>
    ///     Gets the resolved value.
    /// </summary>
    internal JsValue Value => _value;

    /// <summary>
    ///     Lightweight 'then' method for resolved promises.
    /// </summary>
    private sealed class ThenMethod : IJsCallable
    {
        private readonly ResolvedPromiseValue _resolved;
        private readonly JsEngine _engine;

        public ThenMethod(ResolvedPromiseValue resolved, JsEngine engine)
        {
            _resolved = resolved;
            _engine = engine;
        }

        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            var onFulfilled = args.Count > 0 ? args[0] : JsValue.Undefined;
            var onRejected = args.Count > 1 ? args[1] : JsValue.Undefined;

            // Create a new promise for chaining
            var nextPromise = new JsPromise(_engine);

            if (onFulfilled.TryGetCallable(out var fulfilledCallback))
            {
                // Schedule the callback via microtask to maintain proper async semantics
                _engine.QueueMicrotask(() =>
                {
                    try
                    {
                        var result = fulfilledCallback.Invoke([_resolved._value], JsValue.Undefined);
                        nextPromise.Resolve(result);
                    }
                    catch (ThrowSignal signal)
                    {
                        nextPromise.Reject(signal.ThrownValue);
                    }
                    catch (Exception ex)
                    {
                        nextPromise.Reject(new JsValue(ex.Message));
                    }
                });
            }
            else
            {
                // No onFulfilled callback - pass through the value
                _engine.QueueMicrotask(() => nextPromise.Resolve(_resolved._value));
            }

            return new JsValue(nextPromise.JsObject);
        }
    }

    /// <summary>
    ///     Lightweight 'catch' method for resolved promises - just returns this (as resolved promises don't catch).
    /// </summary>
    private sealed class CatchMethod : IJsCallable
    {
        private readonly ResolvedPromiseValue _resolved;
        private readonly JsEngine _engine;

        public CatchMethod(ResolvedPromiseValue resolved, JsEngine engine)
        {
            _resolved = resolved;
            _engine = engine;
        }

        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            // For a resolved promise, catch() is equivalent to then(undefined, onRejected)
            // Since we're resolved, we just pass through the value
            var nextPromise = new JsPromise(_engine);
            _engine.QueueMicrotask(() => nextPromise.Resolve(_resolved._value));
            return new JsValue(nextPromise.JsObject);
        }
    }

    /// <summary>
    ///     Lightweight 'finally' method for resolved promises.
    /// </summary>
    private sealed class FinallyMethod : IJsCallable
    {
        private readonly ResolvedPromiseValue _resolved;
        private readonly JsEngine _engine;

        public FinallyMethod(ResolvedPromiseValue resolved, JsEngine engine)
        {
            _resolved = resolved;
            _engine = engine;
        }

        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            var onFinally = args.Count > 0 ? args[0] : JsValue.Undefined;
            var nextPromise = new JsPromise(_engine);

            if (onFinally.TryGetCallable(out var finallyCallback))
            {
                _engine.QueueMicrotask(() =>
                {
                    try
                    {
                        finallyCallback.Invoke([], JsValue.Undefined);
                        // Finally always passes through the original value
                        nextPromise.Resolve(_resolved._value);
                    }
                    catch (ThrowSignal signal)
                    {
                        nextPromise.Reject(signal.ThrownValue);
                    }
                    catch (Exception ex)
                    {
                        nextPromise.Reject(new JsValue(ex.Message));
                    }
                });
            }
            else
            {
                _engine.QueueMicrotask(() => nextPromise.Resolve(_resolved._value));
            }

            return new JsValue(nextPromise.JsObject);
        }
    }
}
