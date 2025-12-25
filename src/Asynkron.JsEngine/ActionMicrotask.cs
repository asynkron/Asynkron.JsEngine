namespace Asynkron.JsEngine;

/// <summary>
///     Poolable wrapper that allows arbitrary Action delegates to be queued as microtasks.
///     Used for legacy code paths and user-provided callbacks.
/// </summary>
internal sealed class ActionMicrotask : IMicrotask
{
    [ThreadStatic]
    private static ActionMicrotask? Cached;

    private Action? _action;

    public int Epoch { get; set; }

    public static IMicrotask Rent(Action action)
    {
        var task = Cached ?? new ActionMicrotask();
        Cached = null;
        task._action = action;
        return task;
    }

    public void Execute()
    {
        var action = _action!;
        _action = null;

        try
        {
            action();
        }
        finally
        {
            Cached = this;
        }
    }
}
