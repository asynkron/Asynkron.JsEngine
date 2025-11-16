using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
/// Captures superclass metadata for use by class constructors and methods when resolving <c>super</c> references.
/// </summary>
public sealed class SuperBinding(IJsEnvironmentAwareCallable? constructor, JsObject? prototype, object? thisValue)
{
    public IJsEnvironmentAwareCallable? Constructor { get; } = constructor;

    public JsObject? Prototype { get; } = prototype;

    public object? ThisValue { get; } = thisValue;

    public bool TryGetProperty(string name, out object? value)
    {
        if (Prototype is not null)
        {
            return Prototype.TryGetProperty(name, out value);
        }

        value = null;
        return false;

    }
}
