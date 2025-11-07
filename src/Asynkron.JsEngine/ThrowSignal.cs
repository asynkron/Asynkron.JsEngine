namespace Asynkron.JsEngine;

internal sealed class ThrowSignal : Exception
{
    public ThrowSignal(object? value)
    {
        Value = value;
    }

    public object? Value { get; }
}
