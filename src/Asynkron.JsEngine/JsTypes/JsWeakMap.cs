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
    // Use ConditionalWeakTable for weak reference semantics.
    // Values are wrapped in a reference type because CWT values must be reference types.
    private readonly ConditionalWeakTable<object, WeakMapValueBox> _entries = new();
    private readonly List<WeakReference<object>> _entryKeys = [];
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

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name) =>
        _properties.GetOwnPropertyDescriptor(name);

    public IEnumerable<string> GetOwnPropertyNames() =>
        _properties.GetOwnPropertyNames();

    public IEnumerable<string> GetEnumerablePropertyNames() =>
        _properties.GetEnumerablePropertyNames();

    public IEnumerable<string> GetOwnPropertyKeysInOrder(bool includeSymbols = true, bool includeNonEnumerable = true) =>
        _properties.GetOwnPropertyKeysInOrder(includeSymbols, includeNonEnumerable);

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

        // Use remove/add to replace the entry while keeping CWT semantics.
        _entries.Remove(keyObj);
        _entries.Add(keyObj, new WeakMapValueBox(value));
        TrackKnownKey(keyObj);
        JsObject.MarkGlobalMutation();
        return this;
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
            return value.Value;
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
        if (keyObj == null)
        {
            return false;
        }

        var removed = _entries.Remove(keyObj);
        if (removed)
        {
            RemoveKnownKey(keyObj);
            JsObject.MarkGlobalMutation();
        }

        return removed;
    }

    internal bool AnyMappedValueMatches(Predicate<JsValue> predicate)
    {
        for (var i = _entryKeys.Count - 1; i >= 0; i--)
        {
            var keyRef = _entryKeys[i];
            if (!keyRef.TryGetTarget(out var keyObject))
            {
                _entryKeys.RemoveAt(i);
                continue;
            }

            if (!_entries.TryGetValue(keyObject, out var valueBox))
            {
                _entryKeys.RemoveAt(i);
                continue;
            }

            if (predicate(valueBox.Value))
            {
                return true;
            }
        }

        return false;
    }

    private void TrackKnownKey(object keyObject)
    {
        for (var i = _entryKeys.Count - 1; i >= 0; i--)
        {
            var keyRef = _entryKeys[i];
            if (!keyRef.TryGetTarget(out var existing))
            {
                _entryKeys.RemoveAt(i);
                continue;
            }

            if (ReferenceEquals(existing, keyObject))
            {
                return;
            }
        }

        _entryKeys.Add(new WeakReference<object>(keyObject));
    }

    private void RemoveKnownKey(object keyObject)
    {
        for (var i = _entryKeys.Count - 1; i >= 0; i--)
        {
            var keyRef = _entryKeys[i];
            if (!keyRef.TryGetTarget(out var existing) || ReferenceEquals(existing, keyObject))
            {
                _entryKeys.RemoveAt(i);
            }
        }
    }

    private sealed class WeakMapValueBox
    {
        public WeakMapValueBox(JsValue value)
        {
            Value = value;
        }

        public JsValue Value { get; }
    }

}
