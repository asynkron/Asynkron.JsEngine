namespace Asynkron.JsEngine;

internal sealed class ReturnSignal : Exception
{
    public ReturnSignal(object? value)
    {
        Value = value;
    }

    public object? Value { get; }
}
