using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript WeakMap collection.
///     WeakMaps hold key-value pairs where keys must be objects and are held weakly.
///     Unlike Map, WeakMap does not prevent garbage collection of keys and does not support iteration.
/// </summary>
public sealed class JsWeakMap : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl
{
    // Use ConditionalWeakTable for weak reference semantics
    private readonly ConditionalWeakTable<object, object?> _entries = new();
    private readonly JsObject _properties = new();

    public bool TryGetProperty(string name, object? receiver, out object? value)
    {
        return _properties.TryGetProperty(name, receiver ?? this, out value);
    }

    public bool TryGetProperty(string name, out object? value)
    {
        return TryGetProperty(name, this, out value);
    }

    public void SetProperty(string name, object? value, object? receiver)
    {
        _properties.SetProperty(name, value, receiver ?? this);
    }

    public void SetProperty(string name, object? value)
    {
        SetProperty(name, value, this);
    }

    public JsObject? Prototype => _properties.Prototype;

    public bool IsSealed => _properties.IsSealed;
    public bool IsExtensible => _properties.IsExtensible;

    public IEnumerable<string> Keys => _properties.Keys;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        _properties.DefineProperty(name, descriptor);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        return _properties.TryDefineProperty(name, descriptor);
    }

    public void SetPrototype(object? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    public void PreventExtensions()
    {
        _properties.PreventExtensions();
    }

    public void Seal()
    {
        _properties.Seal();
    }

    public bool Delete(string name)
    {
        return _properties.DeleteOwnProperty(name);
    }

    /// <summary>
    ///     Sets the value for the key in the WeakMap. Returns the WeakMap object to allow chaining.
    ///     The key must be an object (not a primitive value).
    /// </summary>
    public JsWeakMap Set(object? key, object? value)
    {
        // WeakMap only accepts objects as keys
        if (key == null || !IsObject(key))
        {
            throw new Exception("Invalid value used as weak map key");
        }

        // Use AddOrUpdate to set the value
        _entries.Remove(key);
        _entries.Add(key, value);
        return this;
    }

    /// <summary>
    ///     Gets the value associated with the key, or undefined if the key doesn't exist.
    /// </summary>
    public object? Get(object? key)
    {
        if (key == null || !IsObject(key))
        {
            return Symbol.Undefined;
        }

        if (_entries.TryGetValue(key, out var value))
        {
            return value;
        }

        return Symbol.Undefined;
    }

    /// <summary>
    ///     Returns true if the key exists in the WeakMap, false otherwise.
    /// </summary>
    public bool Has(object? key)
    {
        if (key == null || !IsObject(key))
        {
            return false;
        }

        return _entries.TryGetValue(key, out _);
    }

    /// <summary>
    ///     Removes the specified key and its value from the WeakMap.
    ///     Returns true if the key was in the WeakMap and has been removed, false otherwise.
    /// </summary>
    public bool Delete(object? key)
    {
        if (key == null || !IsObject(key))
        {
            return false;
        }

        return _entries.Remove(key);
    }

    /// <summary>
    ///     Checks if a value is considered an object for WeakMap purposes.
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

        // Check if it's a reference type that can be used as a WeakMap key
        // Strings are reference types in .NET but are treated as primitives in JavaScript
        if (value is string)
        {
            return false;
        }

        // Value types (numbers, bools, etc.) are not valid WeakMap keys
        if (value.GetType().IsValueType)
        {
            return false;
        }

        // Symbol is a special case - not allowed as WeakMap key
        if (value is TypedAstSymbol)
        {
            return false;
        }

        return true;
    }
}
