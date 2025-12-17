using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
///     Signal indicating a yield expression was encountered (in generator context).
/// </summary>
internal sealed class YieldCompletionSignal : ICompletionSignal
{
    public JsValue JsValue { get; }
    public IJsObjectLike? IteratorResultObject { get; }

    public YieldCompletionSignal(JsValue jsValue, IJsObjectLike? iteratorResultObject = null)
    {
        JsValue = jsValue;
        IteratorResultObject = iteratorResultObject;
    }
}
