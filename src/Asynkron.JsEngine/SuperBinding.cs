using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
///     Captures superclass metadata for use by class constructors and methods when resolving <c>super</c> references.
///     Exposes the prototype through <see cref="IJsPropertyAccessor" /> so the typed evaluator can treat the binding
///     as a regular property accessor when resolving <c>super.prop</c> and <c>super[expr]</c> lookups.
/// </summary>
public sealed class SuperBinding(
    IJsEnvironmentAwareCallable? constructor,
    IJsPropertyAccessor? prototype,
    object? thisValue)
    : IJsPropertyAccessor
{
    public IJsEnvironmentAwareCallable? Constructor { get; } = constructor;

    public IJsPropertyAccessor? Prototype { get; } = prototype;

    public object? ThisValue { get; } = thisValue;

    public bool TryGetProperty(string name, out object? value)
    {
        if (Prototype is IJsObjectLike objectLike)
        {
            var descriptor = objectLike.GetOwnPropertyDescriptor(name);
            if (descriptor is not null)
            {
                if (descriptor.Get is IJsCallable getter)
                {
                    value = getter.Invoke([], ThisValue);
                    return true;
                }

                if (descriptor.IsDataDescriptor)
                {
                    value = descriptor.Value;
                    return true;
                }
            }

            if (Prototype.TryGetProperty(name, out value))
            {
                return true;
            }
        }
        else if (Prototype is not null && Prototype.TryGetProperty(name, out value))
        {
            return true;
        }

        if (Constructor is IJsObjectLike ctorObject)
        {
            var descriptor = ctorObject.GetOwnPropertyDescriptor(name);
            if (descriptor is not null)
            {
                if (descriptor.Get is IJsCallable getter)
                {
                    value = getter.Invoke([], ThisValue);
                    return true;
                }

                if (descriptor.IsDataDescriptor)
                {
                    value = descriptor.Value;
                    return true;
                }
            }
        }

        if (Constructor is IJsPropertyAccessor ctorAccessor &&
            ctorAccessor.TryGetProperty(name, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    public void SetProperty(string name, object? value)
    {
        if (Prototype is IJsObjectLike objectLike)
        {
            var descriptor = objectLike.GetOwnPropertyDescriptor(name);
            if (descriptor?.Set is IJsCallable setter)
            {
                setter.Invoke([value], ThisValue);
                return;
            }
        }

        if (Constructor is IJsObjectLike ctorObject)
        {
            var descriptor = ctorObject.GetOwnPropertyDescriptor(name);
            if (descriptor?.Set is IJsCallable setter)
            {
                setter.Invoke([value], ThisValue);
                return;
            }
        }

        if (ThisValue is IJsPropertyAccessor receiver)
        {
            receiver.SetProperty(name, value);
            return;
        }

        if (Prototype is not null)
        {
            Prototype.SetProperty(name, value);
            return;
        }

        throw new InvalidOperationException("Assigning through super is not supported.");
    }
}
