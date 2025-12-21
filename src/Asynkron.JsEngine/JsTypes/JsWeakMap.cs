using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript WeakMap collection.
///     WeakMaps hold key-value pairs where keys must be objects and are held weakly.
///     Unlike Map, WeakMap does not prevent garbage collection of keys and does not support iteration.
/// </summary>
public sealed class JsWeakMap : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl, IPrototypeAccessorProvider
{
    // Use ConditionalWeakTable for weak reference semantics
    // Keys must be objects, values stored as object? (boxing unavoidable due to ConditionalWeakTable constraint)
    private readonly ConditionalWeakTable<object, object?> _entries = new();
    private readonly JsObject _properties = new();
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
    ///     Sets the value for the key in the WeakMap. Returns the WeakMap object to allow chaining.
    ///     The key must be an object (not a primitive value).
    /// </summary>
    public JsWeakMap Set(JsValue key, JsValue value)
    {
        var keyObj = ExtractKeyObject(key);

        // WeakMap only accepts objects as keys
        if (keyObj == null)
        {
            throw new Exception("Invalid value used as weak map key");
        }

        // Use AddOrUpdate to set the value
        // Store the underlying object (boxing unavoidable for ConditionalWeakTable)
        _entries.Remove(keyObj);
        _entries.Add(keyObj, ExtractValueObject(value));
        return this;
    }

    /// <summary>
    /// Extracts the underlying value from a JsValue for storage.
    /// For primitives, returns a boxed value. For objects, returns the object reference.
    /// </summary>
    private static object? ExtractValueObject(JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => Symbol.Undefined,
            JsValueKind.Null => null,
            JsValueKind.Boolean => value.NumberValue != 0,  // Box boolean
            JsValueKind.Number => value.NumberValue,         // Box number
            JsValueKind.String => value.ObjectValue ?? string.Empty,
            JsValueKind.Symbol => value.ObjectValue ?? throw new InvalidOperationException("Symbol value cannot be null"),
            JsValueKind.BigInt => value.ObjectValue ?? throw new InvalidOperationException("BigInt value cannot be null"),
            JsValueKind.Object => value.ObjectValue ?? throw new InvalidOperationException("Object value cannot be null"),
            _ => throw new InvalidOperationException($"Unexpected value kind: {value.Kind}")
        };
    }

    /// <summary>
    ///     Gets the value associated with the key, or undefined if the key doesn't exist.
    /// </summary>
    public JsValue Get(JsValue key)
    {
        var keyObj = ExtractKeyObject(key);

        if (keyObj == null)
        {
            return JsValue.Undefined;
        }

        if (_entries.TryGetValue(keyObj, out var value))
        {
            return JsValue.FromObjectUnsafe(value);
        }

        return JsValue.Undefined;
    }

    /// <summary>
    ///     Returns true if the key exists in the WeakMap, false otherwise.
    /// </summary>
    public bool Has(JsValue key)
    {
        var keyObj = ExtractKeyObject(key);
        return keyObj != null && _entries.TryGetValue(keyObj, out _);
    }

    /// <summary>
    ///     Removes the specified key and its value from the WeakMap.
    ///     Returns true if the key was in the WeakMap and has been removed, false otherwise.
    /// </summary>
    public bool Delete(JsValue key)
    {
        var keyObj = ExtractKeyObject(key);
        return keyObj != null && _entries.Remove(keyObj);
    }

    /// <summary>
    ///     Extracts the object reference from a JsValue for use as a WeakMap key.
    ///     Returns null if the value is not a valid weak key (primitives, null, undefined).
    /// </summary>
    private static object? ExtractKeyObject(JsValue key)
    {
        // Fast path: primitives can't be weak keys
        if (key.IsNull || key.IsUndefined || key.IsString || key.IsNumber || key.IsBoolean)
        {
            return null;
        }

        // Extract the object directly from the JsValue
        var obj = key.ObjectValue;

        // Handle potentially boxed JsValue (shouldn't happen normally)
        while (obj is JsValue nested)
        {
            obj = nested.ObjectValue;
        }

        return IsObject(obj) ? obj : null;
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
