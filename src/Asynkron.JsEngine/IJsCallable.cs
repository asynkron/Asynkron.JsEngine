namespace Asynkron.JsEngine;

internal interface IJsCallable
{
    object? Invoke(IReadOnlyList<object?> arguments, object? thisValue);
}