using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine;

/// <summary>
///     Exception used at boundaries to propagate JavaScript throw statements across C# call stacks.
///     Within the evaluator, throws are managed via EvaluationContext state machine.
///     This exception is thrown when a throw escapes a function boundary or reaches the top level.
/// </summary>
public sealed class ThrowSignal : Exception
{
    public JsValue ThrownValue { get; }

    public ThrowSignal(JsValue thrownValue) : base(FormatThrowMessage(thrownValue))
    {
        ThrownValue = thrownValue;
    }

    private static string FormatThrowMessage(JsValue thrownValue)
    {
        if (thrownValue.IsNull || thrownValue.IsUndefined)
        {
            return $"Unhandled JavaScript throw: {thrownValue.Kind.ToString().ToLowerInvariant()}";
        }

        if (thrownValue.TryGetString(out var str))
        {
            return $"Unhandled JavaScript throw: \"{str}\"";
        }

        if (thrownValue.TryGetObject<JsObject>(out var jsObj))
        {
            // Try to get error message or name from the object
            if (jsObj.TryGetProperty("message", out var message) && !message.IsNull && !message.IsUndefined)
            {
                var msgStr = JsOps.ToJsString(message.ToObject());
                if (jsObj.TryGetProperty("name", out var name) && !name.IsNull && !name.IsUndefined)
                {
                    return $"Unhandled JavaScript throw: '{JsOps.ToJsString(name.ToObject())}': '{msgStr}'";
                }

                return $"Unhandled JavaScript throw: {msgStr}";
            }

            if (jsObj.TryGetProperty("name", out var errorName) && !errorName.IsNull && !errorName.IsUndefined)
            {
                return $"Unhandled JavaScript throw: {JsOps.ToJsString(errorName.ToObject())}";
            }
        }

        return $"Unhandled JavaScript throw: {JsOps.ToJsString(thrownValue.ToObject())}";
    }
}
