namespace Asynkron.JsEngine;

/// <summary>
/// Captures superclass metadata for use by class constructors and methods when resolving <c>super</c> references.
/// </summary>
internal sealed class SuperBinding(JsFunction? constructor, JsObject? prototype, object? thisValue)
{
    public JsFunction? Constructor { get; } = constructor;

    public JsObject? Prototype { get; } = prototype;

    public object? ThisValue { get; } = thisValue;

    public bool TryGetProperty(string name, out object? value)
    {
        if (Prototype is null)
        {
            value = null;
            return false;
        }

        return Prototype.TryGetProperty(name, out value);
    }
}