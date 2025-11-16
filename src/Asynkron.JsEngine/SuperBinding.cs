using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
/// Captures superclass metadata for use by class constructors and methods when resolving <c>super</c> references.
/// Exposes the prototype through <see cref="IJsPropertyAccessor"/> so the typed evaluator can treat the binding
/// as a regular property accessor when resolving <c>super.prop</c> and <c>super[expr]</c> lookups.
/// </summary>
public sealed class SuperBinding(IJsEnvironmentAwareCallable? constructor, JsObject? prototype, object? thisValue)
    : IJsPropertyAccessor
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

    public void SetProperty(string name, object? value)
    {
        throw new InvalidOperationException("Assigning through super is not supported.");
    }
}
