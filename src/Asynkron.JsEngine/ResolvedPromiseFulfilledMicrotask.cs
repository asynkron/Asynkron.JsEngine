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

        // Rent a single-element array from the cache to pass the value
        var args = JsValueCache.RentJsValueArray(1);
        try
        {
            args[0] = value;
            var result = callback.Invoke(args, JsValue.Undefined);
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
            JsValueCache.ReturnJsValueArray(args);
            TCached = this;
        }
    }
}
