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
    object? thisValue,
    bool isThisInitialized = false)
    : IJsPropertyAccessor
{
    public IJsEnvironmentAwareCallable? Constructor { get; } = constructor;

    public IJsPropertyAccessor? Prototype { get; } = prototype;

    public object? ThisValue { get; } = thisValue;
    public bool IsThisInitialized { get; } = isThisInitialized;

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
        TrySetProperty(name, value, out _);
    }

    /// <summary>
    /// Tries to set a property through super. Returns false if setting failed (e.g., frozen object).
    /// Per ES spec 6.2.3.2 PutValue, in strict mode a failed set should throw TypeError.
    /// </summary>
    public bool TrySetProperty(string name, object? value, out bool usedSetter)
    {
        usedSetter = false;

        // First, check for a setter in the super prototype chain
        if (Prototype is IJsObjectLike objectLike)
        {
            var descriptor = objectLike.GetOwnPropertyDescriptor(name);
            if (descriptor?.Set is IJsCallable setter)
            {
                setter.Invoke([value], ThisValue);
                usedSetter = true;
                return true;
            }
        }

        if (Constructor is IJsObjectLike ctorObject)
        {
            var descriptor = ctorObject.GetOwnPropertyDescriptor(name);
            if (descriptor?.Set is IJsCallable setter)
            {
                setter.Invoke([value], ThisValue);
                usedSetter = true;
                return true;
            }
        }

        // Set on the receiver (thisValue) - this is where strict mode matters
        if (ThisValue is IJsObjectLike thisObject)
        {
            // Check if the object is frozen or the property is non-writable
            if (thisObject.IsFrozen)
            {
                return false; // Caller should throw TypeError in strict mode
            }

            if (thisObject.IsSealed && !thisObject.Keys.Contains(name))
            {
                return false; // Can't add new properties to sealed object
            }

            var existingDescriptor = thisObject.GetOwnPropertyDescriptor(name);
            if (existingDescriptor is { Writable: false })
            {
                return false; // Property is non-writable
            }

            thisObject.SetProperty(name, value);
            return true;
        }

        if (ThisValue is IJsPropertyAccessor receiver)
        {
            receiver.SetProperty(name, value);
            return true;
        }

        if (Prototype is not null)
        {
            Prototype.SetProperty(name, value);
            return true;
        }

        throw new InvalidOperationException("Assigning through super is not supported.");
    }
}
