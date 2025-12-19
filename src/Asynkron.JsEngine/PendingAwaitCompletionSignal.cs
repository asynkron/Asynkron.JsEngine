namespace Asynkron.JsEngine;

/// <summary>
///     Signal indicating evaluation should suspend due to a pending await.
/// </summary>
internal sealed class PendingAwaitCompletionSignal : ICompletionSignal
{
    internal static readonly PendingAwaitCompletionSignal Instance = new();

    private PendingAwaitCompletionSignal()
    {
    }
}
