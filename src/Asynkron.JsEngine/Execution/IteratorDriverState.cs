#region

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
}
