namespace Asynkron.JsEngine;

/// <summary>
/// Captures superclass metadata for use by class constructors and methods when resolving <c>super</c> references.
/// </summary>
internal sealed class SuperBinding
{
    public SuperBinding(JsFunction? constructor, JsObject? prototype, object? thisValue)
    {
        Constructor = constructor;
        Prototype = prototype;
        ThisValue = thisValue;
    }

    public JsFunction? Constructor { get; }

    public JsObject? Prototype { get; }

    public object? ThisValue { get; }

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
