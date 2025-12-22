#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
///     Signal indicating a return statement was encountered.
/// </summary>
internal sealed class ReturnCompletionSignal(JsValue jsValue) : ICompletionSignal
{
    public JsValue JsValue { get; } = jsValue;
}
