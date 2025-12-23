namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Poolable microtask for resolved promise callbacks with onFulfilled handler.
///     Avoids lambda closure allocations by caching the delegate.
/// </summary>
internal sealed class ResolvedPromiseFulfilledMicrotask
{
    private readonly Action _executeDelegate;

    [ThreadStatic]
    private static ResolvedPromiseFulfilledMicrotask? TCached;

    private IJsCallable? _callback;
    private JsValue _value;
    private JsPromise? _nextPromise;

    public ResolvedPromiseFulfilledMicrotask()
    {
        _executeDelegate = Execute;
    }

    public static Action Rent(IJsCallable callback, JsValue value, JsPromise promise)
    {
        var task = TCached ?? new ResolvedPromiseFulfilledMicrotask();
        TCached = null;
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

        // Clear state before execution to allow re-pooling even if exception occurs
        _callback = null;
        _value = JsValue.Undefined;
        _nextPromise = null;

        try
        {
            var result = callback.Invoke([value], JsValue.Undefined);
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
        finally
        {
            TCached = this;
        }
    }
}

/// <summary>
///     Poolable microtask for resolved promise pass-through (no callback).
///     Avoids lambda closure allocations by caching the delegate.
/// </summary>
internal sealed class ResolvedPromisePassthroughMicrotask
{
    private readonly Action _executeDelegate;

    [ThreadStatic]
    private static ResolvedPromisePassthroughMicrotask? TCached;

    private JsValue _value;
    private JsPromise? _nextPromise;

    public ResolvedPromisePassthroughMicrotask()
    {
        _executeDelegate = Execute;
    }

    public static Action Rent(JsValue value, JsPromise promise)
    {
        var task = TCached ?? new ResolvedPromisePassthroughMicrotask();
        TCached = null;
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
        TCached = this;
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
    private static ResolvedPromiseFinallyMicrotask? TCached;

    private IJsCallable? _callback;
    private JsValue _value;
    private JsPromise? _nextPromise;

    public ResolvedPromiseFinallyMicrotask()
    {
        _executeDelegate = Execute;
    }

    public static Action Rent(IJsCallable callback, JsValue value, JsPromise promise)
    {
        var task = TCached ?? new ResolvedPromiseFinallyMicrotask();
        TCached = null;
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
            TCached = this;
        }
    }
}
