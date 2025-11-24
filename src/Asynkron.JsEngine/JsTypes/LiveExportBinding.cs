using System;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a live export binding whose value is resolved on demand.
/// </summary>
internal sealed class LiveExportBinding
{
    private readonly Func<object?> _getter;

    public LiveExportBinding(Func<object?> getter)
    {
        _getter = getter ?? throw new ArgumentNullException(nameof(getter));
    }

    public object? GetValue()
    {
        return _getter();
    }
}
