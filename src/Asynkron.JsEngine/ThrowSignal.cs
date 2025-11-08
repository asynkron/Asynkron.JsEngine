namespace Asynkron.JsEngine;

internal sealed class ThrowSignal(object? value) : Exception
{
    public object? Value { get; } = value;
}
