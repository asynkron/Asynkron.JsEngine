#region

using System.Diagnostics;
using Asynkron.JsEngine.Runtime;

#endregion

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
        // DEBUG: Validate that the thrown value is properly formed.
        // A common mistake is passing a ThrowSignal wrapped as JsValue instead of the actual error object.
        // This catches cases like using ThrowTypeError() (returns ThrowSignal) vs CreateTypeError() (returns JsValue).
        Debug.Assert(
            thrownValue.Kind != JsValueKind.Object || thrownValue.ObjectValue is not ThrowSignal,
            "ThrowSignal should not contain another ThrowSignal. Use CreateTypeError() instead of ThrowTypeError().");
        Debug.Assert(
            thrownValue.Kind != JsValueKind.Object || thrownValue.ObjectValue is not ICompletionSignal,
            "ThrowSignal should not contain ICompletionSignal. The thrown value should be a JavaScript error object.");

        ThrownValue = thrownValue;
    }

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

        if (thrownValue.TryUnwrap<Ast.JsSymbol>(out var symbol))
        {
            return $"Unhandled JavaScript throw: {symbol}";
        }

        if (thrownValue.IsSymbol)
        {
            return $"Unhandled JavaScript throw: {thrownValue.ObjectValue?.ToString() ?? "symbol"}";
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
