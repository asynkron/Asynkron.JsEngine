namespace Asynkron.JsEngine;

internal sealed class HostFunction : IJsCallable
{
    private readonly Func<object?, IReadOnlyList<object?>, object?> _handler;
    private readonly JsObject _properties = new();

    public HostFunction(Func<IReadOnlyList<object?>, object?> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        _handler = (_, args) => handler(args);
        _properties.SetProperty("prototype", new JsObject());
    }

    public HostFunction(Func<object?, IReadOnlyList<object?>, object?> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _properties.SetProperty("prototype", new JsObject());
    }

    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue) => _handler(thisValue, arguments);

    public bool TryGetProperty(string name, out object? value) => _properties.TryGetProperty(name, out value);

    public void SetProperty(string name, object? value) => _properties.SetProperty(name, value);
}
