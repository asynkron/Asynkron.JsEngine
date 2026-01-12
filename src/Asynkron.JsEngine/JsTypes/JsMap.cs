#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript Map collection.
///     Maps hold key-value pairs and remember the original insertion order of keys.
///     Unlike objects, Map keys can be any value (including objects and functions).
/// </summary>
public sealed class JsMap : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl, IPrototypeAccessorProvider,
    IAsJsValue
{
    // Use List to maintain insertion order for iteration
    private readonly List<object?> _insertionOrder = [];

    // Use Dictionary for O(1) lookups
    private readonly Dictionary<object, JsValue> _map = new(SameValueZeroComparer.Instance);

    private readonly JsObject _properties = new();

    // Cached JsValue to avoid repeated struct creation
    private readonly JsValue _cachedJsValue;

    public JsMap()
    {
        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
    }

    // Track null/undefined keys separately (can't be dictionary keys)
    private bool _hasNullKey;
    private bool _hasUndefinedKey;
    private JsValue _nullValue;
    private JsValue _undefinedValue;

    /// <summary>
    ///     Indicates whether this Map is "plain" - i.e., has no custom properties,
    ///     no modified prototype, and can use fast-path optimizations.
    /// </summary>
    internal bool IsPlain { get; private set; } = true;

    /// <summary>
    ///     Gets the number of key-value pairs in the Map.
    /// </summary>
    public int Size => _map.Count + (_hasNullKey ? 1 : 0) + (_hasUndefinedKey ? 1 : 0);

    internal int EntryCount => _insertionOrder.Count;

    public bool IsExtensible => _properties.IsExtensible;

    /// <inheritdoc />
    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

    public void PreventExtensions()
    {
        _properties.PreventExtensions();
    }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        // Handle special 'size' property
        if (!string.Equals(name, "size", StringComparison.Ordinal))
        {
            return _properties.TryGetProperty(name, receiver.IsUndefined ? _cachedJsValue : receiver, out value);
        }

        value = (double)Size;
        return true;
    }

    public bool TryGetProperty(string name, out JsValue value) => TryGetProperty(name, _cachedJsValue, out value);

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        IsPlain = false;
        _properties.SetProperty(name, value, receiver.IsUndefined ? _cachedJsValue : receiver);
    }

    public void SetProperty(string name, JsValue value) => SetProperty(name, value, _cachedJsValue);

    public JsObject? Prototype => _properties.Prototype;

    public bool IsSealed => _properties.IsSealed;
    public bool IsFrozen => _properties.IsFrozen;

    IEnumerable<string> IJsObjectLike.Keys => _properties.Keys;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        IsPlain = false;
        _properties.DefineProperty(name, descriptor);
    }

    public void SetPrototype(IJsPropertyAccessor? candidate)
    {
        // Only mark as non-plain if prototype is being changed after initial setup
        if (_properties.Prototype is not null)
        {
            IsPlain = false;
        }

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
        IsPlain = false;
        return _properties.TryDefineProperty(name, descriptor);
    }

    public IJsPropertyAccessor? PrototypeAccessor =>
        _properties is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

    internal KeyValuePair<object?, JsValue> GetEntry(int index)
    {
        var key = _insertionOrder[index];
        var value = GetByObjectKey(key);
        return new KeyValuePair<object?, JsValue>(key, value);
    }

    /// <summary>
    ///     Internal method to get value by object key (used by iteration methods).
    /// </summary>
    private JsValue GetByObjectKey(object? key)
    {
        if (key is null)
        {
            return _hasNullKey ? _nullValue : JsValue.Undefined;
        }

        if (ReferenceEquals(key, Symbol.Undefined))
        {
            return _hasUndefinedKey ? _undefinedValue : JsValue.Undefined;
        }

        return _map.TryGetValue(key, out var value) ? value : JsValue.Undefined;
    }

    /// <summary>
    ///     Sets the value for the key in the Map. Returns the Map object to allow chaining.
    /// </summary>
    public JsMap Set(JsValue key, JsValue value)
    {
        // Handle null key
        if (key.IsNull)
        {
            if (!_hasNullKey)
            {
                _hasNullKey = true;
                _insertionOrder.Add(null);
            }

            _nullValue = value;
            return this;
        }

        // Handle undefined key
        if (key.IsUndefined)
        {
            if (!_hasUndefinedKey)
            {
                _hasUndefinedKey = true;
                _insertionOrder.Add(Symbol.Undefined);
            }

            _undefinedValue = value;
            return this;
        }

        // Regular key - extract object for dictionary storage
        var keyObj = JsValueExtractor.Extract(key);
        if (!_map.ContainsKey(keyObj))
        {
            _insertionOrder.Add(keyObj);
        }

        _map[keyObj] = value;
        return this;
    }

    /// <summary>
    ///     Gets the value associated with the key, or undefined if the key doesn't exist.
    /// </summary>
    public JsValue Get(JsValue key)
    {
        // Handle null key
        if (key.IsNull)
        {
            return _hasNullKey ? _nullValue : JsValue.Undefined;
        }

        // Handle undefined key
        if (key.IsUndefined)
        {
            return _hasUndefinedKey ? _undefinedValue : JsValue.Undefined;
        }

        // Regular key - use dictionary
        var keyObj = JsValueExtractor.Extract(key);
        return _map.TryGetValue(keyObj, out var value) ? value : JsValue.Undefined;
    }

    /// <summary>
    ///     Returns true if the key exists in the Map, false otherwise.
    /// </summary>
    public bool Has(JsValue key)
    {
        // Handle null key
        if (key.IsNull)
        {
            return _hasNullKey;
        }

        // Handle undefined key
        if (key.IsUndefined)
        {
            return _hasUndefinedKey;
        }

        // Regular key - use dictionary
        var keyObj = JsValueExtractor.Extract(key);
        return _map.ContainsKey(keyObj);
    }

    /// <summary>
    ///     Removes the specified key and its value from the Map.
    ///     Returns true if the key was in the Map and has been removed, false otherwise.
    /// </summary>
    public bool Delete(JsValue key)
    {
        // Handle null key
        if (key.IsNull)
        {
            if (!_hasNullKey)
            {
                return false;
            }

            _hasNullKey = false;
            _nullValue = JsValue.Undefined;
            _insertionOrder.Remove(null);
            return true;
        }

        // Handle undefined key
        if (key.IsUndefined)
        {
            if (!_hasUndefinedKey)
            {
                return false;
            }

            _hasUndefinedKey = false;
            _undefinedValue = JsValue.Undefined;
            _insertionOrder.Remove(Symbol.Undefined);
            return true;
        }

        // Regular key - use dictionary
        var keyObj = JsValueExtractor.Extract(key);
        if (!_map.Remove(keyObj))
        {
            return false;
        }

        _insertionOrder.Remove(keyObj);
        return true;
    }

    /// <summary>
    ///     Removes all key-value pairs from the Map.
    /// </summary>
    public void Clear()
    {
        _map.Clear();
        _insertionOrder.Clear();
        _hasNullKey = false;
        _nullValue = JsValue.Undefined;
        _hasUndefinedKey = false;
        _undefinedValue = JsValue.Undefined;
    }

    /// <summary>
    ///     Executes a provided function once per each key-value pair in the Map, in insertion order.
    /// </summary>
    public void ForEach(IJsCallable callback, JsValue thisArg)
    {
        foreach (var key in _insertionOrder)
        {
            var value = GetByObjectKey(key);
            callback.Invoke([value, JsValue.FromObjectUnsafe(key), _cachedJsValue], thisArg);
        }
    }

    /// <summary>
    ///     Returns an array of [key, value] pairs for every entry in the Map, in insertion order.
    /// </summary>
    public JsArray Entries()
    {
        var entries = new List<JsValue>(_insertionOrder.Count);
        foreach (var key in _insertionOrder)
        {
            var pair = new JsArray([JsValue.FromObjectUnsafe(key), GetByObjectKey(key)]);
            entries.Add(JsValue.FromJsArray(pair));
        }

        return new JsArray(entries);
    }

    /// <summary>
    ///     Returns an array of keys in the Map, in insertion order.
    /// </summary>
    public JsArray Keys()
    {
        var keys = new List<JsValue>(_insertionOrder.Count);
        foreach (var key in _insertionOrder)
        {
            keys.Add(JsValue.FromObjectUnsafe(key));
        }

        return new JsArray(keys);
    }

    /// <summary>
    ///     Returns an array of values in the Map, in insertion order.
    /// </summary>
    public JsArray Values()
    {
        var values = new List<JsValue>(_insertionOrder.Count);
        foreach (var key in _insertionOrder)
        {
            values.Add(GetByObjectKey(key));
        }

        return new JsArray(values);
    }
}
