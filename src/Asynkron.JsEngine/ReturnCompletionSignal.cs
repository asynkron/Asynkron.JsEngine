using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
///     Signal indicating a return statement was encountered.
/// </summary>
internal sealed class ReturnCompletionSignal : ICompletionSignal
{
    public JsValue JsValue { get; }

    public ReturnCompletionSignal(JsValue jsValue)
    {
        JsValue = jsValue;
    }
}
