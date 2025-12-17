using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
///     Signal indicating a throw statement was encountered.
/// </summary>
internal sealed class ThrowFlowCompletionSignal : ICompletionSignal
{
    public JsValue JsValue { get; }

    public ThrowFlowCompletionSignal(JsValue jsValue)
    {
        JsValue = jsValue;
    }
}
