using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
///     Signal indicating a return statement was encountered.
/// </summary>
internal sealed class ReturnCompletionSignal(JsValue jsValue) : ICompletionSignal
{
    public JsValue JsValue { get; } = jsValue;
}
