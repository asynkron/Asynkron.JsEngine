#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
///     Signal indicating a throw statement was encountered.
/// </summary>
internal sealed class ThrowFlowCompletionSignal(JsValue jsValue) : ICompletionSignal
{
    public JsValue JsValue { get; } = jsValue;
}
