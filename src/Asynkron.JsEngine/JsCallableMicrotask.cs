using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine;

/// <summary>
///     Poolable microtask that wraps a JavaScript callable (IJsCallable).
///     Used by the queueMicrotask global function to schedule user callbacks.
/// </summary>
internal sealed class JsCallableMicrotask : IMicrotask, IRentable
{
    private static readonly ObjectPool<JsCallableMicrotask> Pool = new(32, static () => new JsCallableMicrotask());

    private IJsCallable? _callback;

    public int Epoch { get; set; }

    public static IMicrotask Rent(IJsCallable callback)
    {
        var task = Pool.Rent();
        task._callback = callback;
        return task;
    }

    public void Execute()
    {
        AssertOwnership(nameof(Execute));
        var callback = _callback!;
        try
        {
            callback.Invoke([], JsValue.Undefined);
        }
        finally
        {
            Pool.Return(this);
        }
    }

    public void OnRent(ILogger? logger) { }

    public void OnReturn(ILogger? logger)
    {
        _callback = null;
        Epoch = 0;
    }

    [Conditional("DEBUG")]
    internal void AssertOwnership(string usage) => PoolDebug.AssertOwned(this, usage);
}
