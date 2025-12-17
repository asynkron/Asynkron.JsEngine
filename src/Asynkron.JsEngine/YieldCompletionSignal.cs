using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
///     Signal indicating a yield expression was encountered (in generator context).
/// </summary>
internal sealed class YieldCompletionSignal(JsValue jsValue, IJsObjectLike? iteratorResultObject = null)
    : ICompletionSignal
{
    public JsValue JsValue { get; } = jsValue;
    public IJsObjectLike? IteratorResultObject { get; } = iteratorResultObject;
}
