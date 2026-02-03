#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript WeakMap collection.
///     WeakMaps hold key-value pairs where keys must be objects and are held weakly.
///     Unlike Map, WeakMap does not prevent garbage collection of keys and does not support iteration.
/// </summary>
public sealed class JsWeakMap : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl,
    IPrototypeAccessorProvider, IAsJsValue
{
    // Use ConditionalWeakTable for weak reference semantics
    // Keys must be objects, values stored as object? (boxing unavoidable due to ConditionalWeakTable constraint)
    private readonly ConditionalWeakTable<object, object?> _entries = new();
    private readonly JsObject _properties = new();
    private readonly JsValue _cachedJsValue;
    public bool IsExtensible => _properties.IsExtensible;

    public JsWeakMap()
    {
        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
    }

    public void PreventExtensions()
    {
        _properties.PreventExtensions();
    }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        return _properties.TryGetProperty(name, receiver.IsUndefined ? _cachedJsValue : receiver, out value);
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        return TryGetProperty(name, _cachedJsValue, out value);
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        _properties.SetProperty(name, value, receiver.IsUndefined ? _cachedJsValue : receiver);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, _cachedJsValue);
    }

    public JsObject? Prototype => _properties.Prototype;

    public bool IsSealed => _properties.IsSealed;
    public bool IsFrozen => _properties.IsFrozen;

    public IEnumerable<string> Keys => _properties.Keys;

    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        _properties.DefineProperty(name, descriptor);
    }

    public void SetPrototype(IJsPropertyAccessor? candidate)
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

    public IJsPropertyAccessor? PrototypeAccessor =>
        _properties is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

    /// <summary>
    ///     Sets the value for the key in the WeakMap. Returns the WeakMap object to allow chaining.
    ///     The key must be an object (not a primitive value).
    /// </summary>
    public JsWeakMap Set(JsValue key, JsValue value)
    {
        var keyObj = JsWeakCollectionHelpers.ExtractWeakKeyObject(key);

        // WeakMap only accepts objects as keys
        if (keyObj == null)
        {
            throw StandardLibrary.ThrowTypeError("Invalid value used as weak map key");
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
            JsValueKind.Boolean => value.NumberValue != 0, // Box boolean
            JsValueKind.Number => value.NumberValue, // Box number
            JsValueKind.String => value.ObjectValue ?? string.Empty,
            JsValueKind.Symbol => value.ObjectValue ??
                                  throw new InvalidOperationException("Symbol value cannot be null"),
            JsValueKind.BigInt => value.ObjectValue ??
                                  throw new InvalidOperationException("BigInt value cannot be null"),
            JsValueKind.Object => value.ObjectValue ??
                                  throw new InvalidOperationException("Object value cannot be null"),
            _ => throw new InvalidOperationException($"Unexpected value kind: {value.Kind}")
        };
    }

    /// <summary>
    ///     Gets the value associated with the key, or undefined if the key doesn't exist.
    /// </summary>
    public JsValue Get(JsValue key)
    {
        var keyObj = JsWeakCollectionHelpers.ExtractWeakKeyObject(key);

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
        var keyObj = JsWeakCollectionHelpers.ExtractWeakKeyObject(key);
        return keyObj != null && _entries.TryGetValue(keyObj, out _);
    }

    /// <summary>
    ///     Removes the specified key and its value from the WeakMap.
    ///     Returns true if the key was in the WeakMap and has been removed, false otherwise.
    /// </summary>
    public bool Delete(JsValue key)
    {
        var keyObj = JsWeakCollectionHelpers.ExtractWeakKeyObject(key);
        return keyObj != null && _entries.Remove(keyObj);
    }

}
