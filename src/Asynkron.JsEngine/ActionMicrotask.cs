using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine;

/// <summary>
///     Poolable wrapper that allows arbitrary Action delegates to be queued as microtasks.
///     Used for legacy code paths and user-provided callbacks.
/// </summary>
internal sealed class ActionMicrotask : IMicrotask, IRentable
{
    private static readonly ObjectPool<ActionMicrotask> Pool = new(32, static () => new ActionMicrotask());

    private Action? _action;

    public int Epoch { get; set; }

    public static IMicrotask Rent(Action action)
    {
        var task = Pool.Rent();
        task._action = action;
        return task;
    }

    public void Execute()
    {
        var action = _action!;
        try
        {
            action();
        }
        finally
        {
            Pool.Return(this);
        }
    }

    public void Activate(ILogger? logger) { }

    public void Reset(ILogger? logger)
    {
        _action = null;
        Epoch = 0;
    }
}
