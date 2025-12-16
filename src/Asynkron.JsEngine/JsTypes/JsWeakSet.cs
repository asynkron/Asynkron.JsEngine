using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript WeakSet collection.
///     WeakSets store unique objects where values are held weakly.
///     Unlike Set, WeakSet does not prevent garbage collection of values and does not support iteration.
/// </summary>
public sealed class JsWeakSet : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl, IPrototypeAccessorProvider
{
    private readonly JsObject _properties = new();

    // Use ConditionalWeakTable to track object membership
    // We use a dummy value since we only care about key presence
    private readonly ConditionalWeakTable<object, object?> _values = new();
    public bool IsExtensible => _properties.IsExtensible;

    public void PreventExtensions()
    {
        _properties.PreventExtensions();
    }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        return _properties.TryGetProperty(name, receiver.IsUndefined ? (JsValue)this : receiver, out value);
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        return TryGetProperty(name, (JsValue)this, out value);
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        _properties.SetProperty(name, value, receiver.IsUndefined ? (JsValue)this : receiver);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, (JsValue)this);
    }

    public JsObject? Prototype => _properties.Prototype;
    public IJsPropertyAccessor? PrototypeAccessor =>
        _properties is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

    public bool IsSealed => _properties.IsSealed;
    public bool IsFrozen => _properties.IsFrozen;

    public IEnumerable<string> Keys => _properties.Keys;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        _properties.DefineProperty(name, descriptor);
    }

    public void SetPrototype(object? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    public void Seal()
    {
        _properties.Seal();
    }

    public bool Delete(string name)
    {
        return _properties.DeleteOwnProperty(name);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        return _properties.TryDefineProperty(name, descriptor);
    }

    /// <summary>
    ///     Adds a value to the WeakSet. Returns the WeakSet object to allow chaining.
    ///     The value must be an object (not a primitive value).
    /// </summary>
    public JsWeakSet Add(JsValue value)
    {
        var obj = value.ToObject();

        // WeakSet only accepts objects as values
        if (obj == null || !IsObject(obj))
        {
            throw new Exception("Invalid value used in weak set");
        }

        // If already present, do nothing; otherwise add it
        if (!_values.TryGetValue(obj, out _))
        {
            _values.Add(obj, null);
        }

        return this;
    }

    /// <summary>
    ///     Returns true if the value exists in the WeakSet, false otherwise.
    /// </summary>
    public bool Has(JsValue value)
    {
        var obj = value.ToObject();

        if (obj == null || !IsObject(obj))
        {
            return false;
        }

        return _values.TryGetValue(obj, out _);
    }

    /// <summary>
    ///     Removes the specified value from the WeakSet.
    ///     Returns true if the value was in the WeakSet and has been removed, false otherwise.
    /// </summary>
    public bool Delete(JsValue value)
    {
        var obj = value.ToObject();

        if (obj == null || !IsObject(obj))
        {
            return false;
        }

        return _values.Remove(obj);
    }

    /// <summary>
    ///     Checks if a value is considered an object for WeakSet purposes.
    ///     In JavaScript, objects, arrays, functions, etc. are valid, but primitives are not.
    /// </summary>
    private static bool IsObject(object? value)
    {
        if (value == null)
        {
            return false;
        }

        // Check for undefined symbol
        if (value is Symbol sym && ReferenceEquals(sym, Symbol.Undefined))
        {
            return false;
        }

        // Check if it's a reference type that can be used as a WeakSet value
        // Strings are reference types in .NET but are treated as primitives in JavaScript
        if (value is string)
        {
            return false;
        }

        // Value types (numbers, bools, etc.) are not valid WeakSet values
        if (value.GetType().IsValueType)
        {
            return false;
        }

        // Symbol is a special case - not allowed as WeakSet value
        if (value is TypedAstSymbol)
        {
            return false;
        }

        return true;
    }
}
