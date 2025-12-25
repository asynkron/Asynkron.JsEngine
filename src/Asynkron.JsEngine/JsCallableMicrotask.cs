using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
///     Poolable microtask that wraps a JavaScript callable (IJsCallable).
///     Used by the queueMicrotask global function to schedule user callbacks.
/// </summary>
internal sealed class JsCallableMicrotask : IMicrotask
{
    [ThreadStatic]
    private static JsCallableMicrotask? Cached;

    private IJsCallable? _callback;

    public int Epoch { get; set; }

    public static IMicrotask Rent(IJsCallable callback)
    {
        var task = Cached ?? new JsCallableMicrotask();
        Cached = null;
        task._callback = callback;
        return task;
    }

    public void Execute()
    {
        var callback = _callback!;
        _callback = null;

        try
        {
            callback.Invoke([], JsValue.Undefined);
        }
        finally
        {
            Cached = this;
        }
    }
}
