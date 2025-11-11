namespace Asynkron.JsEngine;

/// <summary>
/// Represents a callable object in JavaScript.
/// </summary>
public interface IJsCallable
{
    object? Invoke(IReadOnlyList<object?> arguments, object? thisValue);
}