using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
/// Exception used at boundaries to propagate JavaScript throw statements across C# call stacks.
/// Within the evaluator, throws are managed via EvaluationContext state machine.
/// This exception is thrown when a throw escapes a function boundary or reaches the top level.
/// </summary>
public sealed class ThrowSignal(object? thrownValue = null) : Exception(FormatThrowMessage(thrownValue))
{
    public object? ThrownValue { get; } = thrownValue;

    private static string FormatThrowMessage(object? thrownValue)
    {
        if (thrownValue == null)
        {
            return "Unhandled JavaScript throw: null";
        }

        if (thrownValue is string str)
        {
            return $"Unhandled JavaScript throw: \"{str}\"";
        }

        if (thrownValue is JsObject jsObj)
        {
            // Try to get error message or name from the object
            if (jsObj.TryGetProperty("message", out var message) && message != null)
            {
                var msgStr = message.ToString();
                if (jsObj.TryGetProperty("name", out var name) && name != null)
                {
                    return $"Unhandled JavaScript throw: {name}: {msgStr}";
                }

                return $"Unhandled JavaScript throw: {msgStr}";
            }

            if (jsObj.TryGetProperty("name", out var errorName) && errorName != null)
            {
                return $"Unhandled JavaScript throw: {errorName}";
            }
        }

        return $"Unhandled JavaScript throw: {thrownValue}";
    }
}
