#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
///     Exception used at boundaries to propagate JavaScript throw statements across C# call stacks.
///     Within the evaluator, throws are managed via EvaluationContext state machine.
///     This exception is thrown when a throw escapes a function boundary or reaches the top level.
/// </summary>
public sealed class ThrowSignal(JsValue thrownValue) : Exception(FormatThrowMessage(thrownValue))
{
    public JsValue ThrownValue { get; } = thrownValue;

    private static string FormatThrowMessage(JsValue thrownValue)
    {
        if (thrownValue.IsNull)
        {
            return "Unhandled JavaScript throw: null";
        }

        if (thrownValue.IsUndefined)
        {
            return "Unhandled JavaScript throw: undefined";
        }

        if (thrownValue.TryGetString(out var str))
        {
            return $"Unhandled JavaScript throw: \"{str}\"";
        }

        if (thrownValue.TryGetObject<JsObject>(out var jsObj))
        {
            // Try to get error message or name from the object
            if (jsObj.TryGetProperty("message", out var message) && message is { IsNull: false, IsUndefined: false })
            {
                var msgStr = JsOps.ToJsString(message);
                if (jsObj.TryGetProperty("name", out var name) && name is { IsNull: false, IsUndefined: false })
                {
                    return $"Unhandled JavaScript throw: '{JsOps.ToJsString(name)}': '{msgStr}'";
                }

                return $"Unhandled JavaScript throw: {msgStr}";
            }

            if (jsObj.TryGetProperty("name", out var errorName) && errorName is { IsNull: false, IsUndefined: false })
            {
                return $"Unhandled JavaScript throw: {JsOps.ToJsString(errorName)}";
            }
        }

        return $"Unhandled JavaScript throw: {JsOps.ToJsString(thrownValue)}";
    }
}
