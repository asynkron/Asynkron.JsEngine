namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Poolable microtask for resolved promise pass-through (no callback).
///     Implements IMicrotask directly to avoid Action delegate allocation.
/// </summary>
internal sealed class ResolvedPromisePassthroughMicrotask : IMicrotask, IRentable
{
    private static readonly ObjectPool<ResolvedPromisePassthroughMicrotask> Pool = new(32,
        static () => new ResolvedPromisePassthroughMicrotask());

    private JsValue _value;
    private JsPromise? _nextPromise;

    public int Epoch { get; set; }

    public static IMicrotask Rent(JsValue value, JsPromise promise)
    {
        var task = Pool.Rent();
        task._value = value;
        task._nextPromise = promise;
        return task;
    }

    public void Execute()
    {
        var value = _value;
        var nextPromise = _nextPromise!;

        nextPromise.Resolve(value);
        Pool.Return(this);
    }

    public void Reset()
    {
        _value = JsValue.Undefined;
        _nextPromise = null;
        Epoch = 0;
    }
}

/// <summary>
///     Poolable microtask for finally handlers.
///     Implements IMicrotask directly to avoid Action delegate allocation.
/// </summary>
internal sealed class ResolvedPromiseFinallyMicrotask : IMicrotask, IRentable
{
    private static readonly ObjectPool<ResolvedPromiseFinallyMicrotask> Pool = new(32,
        static () => new ResolvedPromiseFinallyMicrotask());

    private IJsCallable? _callback;
    private JsValue _value;
    private JsPromise? _nextPromise;

    public int Epoch { get; set; }

    public static IMicrotask Rent(IJsCallable callback, JsValue value, JsPromise promise)
    {
        var task = Pool.Rent();
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
            Pool.Return(this);
        }
    }

    public void Reset()
    {
        _callback = null;
        _value = JsValue.Undefined;
        _nextPromise = null;
        Epoch = 0;
    }
}
