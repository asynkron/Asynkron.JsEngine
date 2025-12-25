namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Poolable microtask for resolved promise pass-through (no callback).
///     Implements IMicrotask directly to avoid Action delegate allocation.
/// </summary>
internal sealed class ResolvedPromisePassthroughMicrotask : IMicrotask
{
    [ThreadStatic]
    private static ResolvedPromisePassthroughMicrotask? Cached;

    private JsValue _value;
    private JsPromise? _nextPromise;

    public int Epoch { get; set; }

    public static IMicrotask Rent(JsValue value, JsPromise promise)
    {
        var task = Cached ?? new ResolvedPromisePassthroughMicrotask();
        Cached = null;
        task._value = value;
        task._nextPromise = promise;
        return task;
    }

    public void Execute()
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
///     Implements IMicrotask directly to avoid Action delegate allocation.
/// </summary>
internal sealed class ResolvedPromiseFinallyMicrotask : IMicrotask
{
    [ThreadStatic]
    private static ResolvedPromiseFinallyMicrotask? Cached;

    private IJsCallable? _callback;
    private JsValue _value;
    private JsPromise? _nextPromise;

    public int Epoch { get; set; }

    public static IMicrotask Rent(IJsCallable callback, JsValue value, JsPromise promise)
    {
        var task = Cached ?? new ResolvedPromiseFinallyMicrotask();
        Cached = null;
        task._callback = callback;
        task._value = value;
        task._nextPromise = promise;
        return task;
    }

    public void Execute()
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
