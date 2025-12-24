namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Poolable microtask for resolved promise pass-through (no callback).
///     Avoids lambda closure allocations by caching the delegate.
/// </summary>
internal sealed class ResolvedPromisePassthroughMicrotask
{
    private readonly Action _executeDelegate;

    [ThreadStatic]
    private static ResolvedPromisePassthroughMicrotask? Cached;

    private JsValue _value;
    private JsPromise? _nextPromise;

    private ResolvedPromisePassthroughMicrotask()
    {
        _executeDelegate = Execute;
    }

    public static Action Rent(JsValue value, JsPromise promise)
    {
        var task = Cached ?? new ResolvedPromisePassthroughMicrotask();
        Cached = null;
        task._value = value;
        task._nextPromise = promise;
        return task._executeDelegate;
    }

    private void Execute()
    {
        var value = _value;
        var nextPromise = _nextPromise!;

        _value = JsValue.Undefined;
        _nextPromise = null;

        nextPromise.Resolve(value);
        Cached = this;
    }
}

/// <summary>
///     Poolable microtask for finally handlers.
///     Avoids lambda closure allocations by caching the delegate.
/// </summary>
internal sealed class ResolvedPromiseFinallyMicrotask
{
    private readonly Action _executeDelegate;

    [ThreadStatic]
    private static ResolvedPromiseFinallyMicrotask? Cached;

    private IJsCallable? _callback;
    private JsValue _value;
    private JsPromise? _nextPromise;

    private ResolvedPromiseFinallyMicrotask()
    {
        _executeDelegate = Execute;
    }

    public static Action Rent(IJsCallable callback, JsValue value, JsPromise promise)
    {
        var task = Cached ?? new ResolvedPromiseFinallyMicrotask();
        Cached = null;
        task._callback = callback;
        task._value = value;
        task._nextPromise = promise;
        return task._executeDelegate;
    }

    private void Execute()
    {
        var callback = _callback!;
        var value = _value;
        var nextPromise = _nextPromise!;

        _callback = null;
        _value = JsValue.Undefined;
        _nextPromise = null;

        try
        {
            callback.Invoke([], JsValue.Undefined);
            nextPromise.Resolve(value);
        }
        catch (ThrowSignal signal)
        {
            nextPromise.Reject(signal.ThrownValue);
        }
        catch (Exception ex)
        {
            nextPromise.Reject(new JsValue(ex.Message));
        }
        finally
        {
            Cached = this;
        }
    }
}
