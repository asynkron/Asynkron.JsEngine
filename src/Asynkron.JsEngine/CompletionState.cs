#region

#endregion

namespace Asynkron.JsEngine;

/// <summary>
///     Represents a saved completion state for try-finally handling.
/// </summary>
public readonly struct CompletionState(bool isReturn, JsValue returnValue, ICompletionSignal? signal)
{
    public bool IsReturn { get; } = isReturn;
    public JsValue ReturnValue { get; } = returnValue;
    public ICompletionSignal? Signal { get; } = signal;

    /// <summary>
    ///     Returns true if there's any completion to restore.
    /// </summary>
    public bool HasCompletion => IsReturn || Signal is not null;
}
