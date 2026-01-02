using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     A lightweight resolved promise that avoids the overhead of creating a full JsPromise + JsObject.
///     Used for wrapping non-promise values in async/await contexts where we need promise-like semantics
///     but the value is already known and resolved.
/// </summary>
internal sealed class ResolvedPromiseValue : IJsPropertyAccessor, IAsJsValue
{
    private static readonly ObjectPool<ResolvedPromiseValue> Pool = new(64, static () => new ResolvedPromiseValue());

    private JsValue _value;
    private JsEngine? _engine;
    private JsValue _cachedJsValue;

    private ResolvedPromiseValue()
    {
    }

    /// <summary>
    /// Rents a ResolvedPromiseValue from the pool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ResolvedPromiseValue Rent(JsValue value, JsEngine engine)
    {
        var instance = Pool.Rent();
        instance._value = value;
        instance._engine = engine;
        return instance;
    }

    /// <summary>
    /// Returns this instance to the pool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return()
    {
        _value = default;
        _engine = null;
        Pool.Return(this);
    }

    public ref readonly JsValue AsJsValue
    {
        get
        {
            if (_cachedJsValue.ObjectValue is null)
            {
                _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
            }
            return ref _cachedJsValue;
        }
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        switch (name)
        {
            case "then":
                value = JsValue.FromObjectUnsafe(new ThenMethod(this, _engine!));
                return true;
            case "catch":
                // catch is just then(undefined, onRejected) - but we're resolved, so it returns this
                value = JsValue.FromObjectUnsafe(new CatchMethod(this, _engine!));
                return true;
            case "finally":
                value = JsValue.FromObjectUnsafe(new FinallyMethod(this, _engine!));
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
    private sealed class ThenMethod(ResolvedPromiseValue resolved, JsEngine engine) : IJsCallable
    {
        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            var onFulfilled = args.Count > 0 ? args[0] : JsValue.Undefined;

            // Capture value before returning to pool (JsValue is a struct, so this is a copy)
            var resolvedValue = resolved._value;

            // Create a new promise for chaining
            var nextPromise = new JsPromise(engine);

            if (onFulfilled.TryGetCallable(out var fulfilledCallback))
            {
                // Use pooled microtask to avoid lambda closure allocation
                engine.QueueMicrotask(
                    ResolvedPromiseFulfilledMicrotask.Rent(fulfilledCallback, resolvedValue, nextPromise));
            }
            else
            {
                // No onFulfilled callback - pass through the value
                engine.QueueMicrotask(
                    ResolvedPromisePassthroughMicrotask.Rent(resolvedValue, nextPromise));
            }

            // Return the ResolvedPromiseValue to pool - it's no longer needed after .then()
            // The value has been captured and passed to the microtask
            resolved.Return();

            // Return the JsPromise directly without forcing JsObject creation
            // The await machinery can find the promise via TryGetInternalPromise
            return JsValue.FromJsPromise(nextPromise);
        }
    }

    /// <summary>
    ///     Lightweight 'catch' method for resolved promises - just returns this (as resolved promises don't catch).
    /// </summary>
    private sealed class CatchMethod(ResolvedPromiseValue resolved, JsEngine engine) : IJsCallable
    {
        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            // For a resolved promise, catch() is equivalent to then(undefined, onRejected)
            // Since we're resolved, we just pass through the value
            var nextPromise = new JsPromise(engine);
            engine.QueueMicrotask(
                ResolvedPromisePassthroughMicrotask.Rent(resolved._value, nextPromise));
            return JsValue.FromJsPromise(nextPromise);
        }
    }

    /// <summary>
    ///     Lightweight 'finally' method for resolved promises.
    /// </summary>
    private sealed class FinallyMethod(ResolvedPromiseValue resolved, JsEngine engine) : IJsCallable
    {
        public JsValue Invoke(IReadOnlyList<JsValue> args, JsValue thisValue)
        {
            var onFinally = args.Count > 0 ? args[0] : JsValue.Undefined;
            var nextPromise = new JsPromise(engine);

            if (onFinally.TryGetCallable(out var finallyCallback))
            {
                engine.QueueMicrotask(
                    ResolvedPromiseFinallyMicrotask.Rent(finallyCallback, resolved._value, nextPromise));
            }
            else
            {
                engine.QueueMicrotask(
                    ResolvedPromisePassthroughMicrotask.Rent(resolved._value, nextPromise));
            }

            return JsValue.FromJsPromise(nextPromise);
        }
    }
}
