using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Poolable microtask for resolved promise callbacks with onFulfilled handler.
///     Implements IMicrotask directly to avoid Action delegate allocation.
/// </summary>
internal sealed class ResolvedPromiseFulfilledMicrotask : IMicrotask, IRentable
{
    private static readonly ObjectPool<ResolvedPromiseFulfilledMicrotask> Pool = new(32,
        static () => new ResolvedPromiseFulfilledMicrotask());

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
        AssertOwnership(nameof(Execute));
        var callback = _callback!;
        var value = _value;
        var nextPromise = _nextPromise!;

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
            Pool.Return(this);
        }
    }

    public void Activate(ILogger? logger) { }

    public void Reset(ILogger? logger)
    {
        _callback = null;
        _value = JsValue.Undefined;
        _nextPromise = null;
        Epoch = 0;
    }

    [Conditional("DEBUG")]
    internal void AssertOwnership(string usage) => PoolDebug.AssertOwned(this, usage);
}
