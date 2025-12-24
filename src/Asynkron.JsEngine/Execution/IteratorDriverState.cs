#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Execution;

internal sealed class IteratorDriverState
{
    public IJsObjectLike? IteratorObject { get; init; }
    public IEnumerator<JsValue>? Enumerator { get; init; }
    public bool IsAsyncIterator { get; init; }
    public bool AwaitingNextResult { get; set; }
    public bool AwaitingValue { get; set; }
    public IJsCallable? NextMethod { get; set; }

    /// <summary>
    /// Pre-resolved JsVariable for fast iterator state access (avoids dictionary lookups per iteration).
    /// </summary>
    public JsVariable IteratorVariable { get; set; }

    /// <summary>
    /// Pre-resolved JsVariable for fast value access (avoids dictionary lookups per iteration).
    /// </summary>
    public JsVariable ValueVariable { get; set; }
}
