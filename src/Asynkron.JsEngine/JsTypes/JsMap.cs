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
    // Use List to maintain insertion order for iteration, including tombstoned entries.
    private readonly List<MapEntryRecord> _insertionOrder = [];

    // Use Dictionary for O(1) lookups
    private readonly Dictionary<object, JsValue> _map = new(SameValueZeroComparer.Instance);
    private readonly Dictionary<object, MapEntryRecord> _entryRecords = new(SameValueZeroComparer.Instance);

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
    private MapEntryRecord? _nullEntry;
    private MapEntryRecord? _undefinedEntry;

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
        var entry = _insertionOrder[index];
        var value = GetByObjectKey(entry.Key);
        return new KeyValuePair<object?, JsValue>(entry.Key, value);
    }

    /// <summary>
    ///     Checks if an entry key is still alive (not deleted).
    ///     Used by iterators to skip tombstones in the insertion order list.
    /// </summary>
    internal bool IsEntryAlive(int index)
    {
        return _insertionOrder[index].IsAlive;
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
                _nullEntry = new MapEntryRecord(null);
                _insertionOrder.Add(_nullEntry);
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
                _undefinedEntry = new MapEntryRecord(Symbol.Undefined);
                _insertionOrder.Add(_undefinedEntry);
            }

            _undefinedValue = value;
            return this;
        }

        // Regular key - extract object for dictionary storage
        var keyObj = JsValueExtractor.Extract(key);
        if (!_map.ContainsKey(keyObj))
        {
            var entry = new MapEntryRecord(keyObj);
            _insertionOrder.Add(entry);
            _entryRecords[keyObj] = entry;
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
            _nullEntry?.Delete();
            _nullEntry = null;
            // Don't remove from _insertionOrder — ForEach uses index-based iteration
            // and removing would shift indices, corrupting the loop.
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
            _undefinedEntry?.Delete();
            _undefinedEntry = null;
            return true;
        }

        // Regular key - use dictionary
        var keyObj = JsValueExtractor.Extract(key);
        if (!_map.Remove(keyObj))
        {
            return false;
        }

        if (_entryRecords.Remove(keyObj, out var entry))
        {
            entry.Delete();
        }

        // Don't remove from _insertionOrder — ForEach uses index-based iteration
        // and removing would shift indices, corrupting the loop.
        return true;
    }

    /// <summary>
    ///     Removes all key-value pairs from the Map.
    /// </summary>
    public void Clear()
    {
        _map.Clear();
        _insertionOrder.Clear();
        _entryRecords.Clear();
        _hasNullKey = false;
        _nullValue = JsValue.Undefined;
        _hasUndefinedKey = false;
        _undefinedValue = JsValue.Undefined;
        _nullEntry = null;
        _undefinedEntry = null;
    }

    /// <summary>
    ///     Executes a provided function once per each key-value pair in the Map, in insertion order.
    /// </summary>
    public void ForEach(IJsCallable callback, JsValue thisArg)
    {
        // Use index-based loop to allow modification during iteration.
        // Per the spec, forEach visits entries that exist at the time each step
        // is taken and also visits entries added during iteration.
        for (var i = 0; i < _insertionOrder.Count; i++)
        {
            var entry = _insertionOrder[i];
            if (!entry.IsAlive)
            {
                continue;
            }

            var key = entry.Key;

            // Handle null key
            if (key is null)
            {
                callback.Invoke([_nullValue, JsValue.Null, _cachedJsValue], thisArg);
                continue;
            }

            // Handle undefined key (stored as Symbol.Undefined sentinel)
            if (ReferenceEquals(key, Symbol.Undefined))
            {
                callback.Invoke([_undefinedValue, JsValue.Undefined, _cachedJsValue], thisArg);
                continue;
            }

            var value = GetByObjectKey(key);
            callback.Invoke([value, JsValue.FromObjectUnsafe(key), _cachedJsValue], thisArg);
        }
    }

    /// <summary>
    ///     Returns an array of [key, value] pairs for every entry in the Map, in insertion order.
    /// </summary>
    public JsArray Entries()
    {
        var entries = new List<JsValue>();
        foreach (var entry in _insertionOrder)
        {
            if (!entry.IsAlive)
            {
                continue;
            }

            var key = entry.Key;
            if (key is null)
            {
                entries.Add(JsValue.FromJsArray(new JsArray([JsValue.Null, _nullValue])));
            }
            else if (ReferenceEquals(key, Symbol.Undefined))
            {
                entries.Add(JsValue.FromJsArray(new JsArray([JsValue.Undefined, _undefinedValue])));
            }
            else
            {
                var pair = new JsArray([JsValue.FromObjectUnsafe(key), GetByObjectKey(key)]);
                entries.Add(JsValue.FromJsArray(pair));
            }
        }

        return new JsArray(entries);
    }

    /// <summary>
    ///     Returns an array of keys in the Map, in insertion order.
    /// </summary>
    public JsArray Keys()
    {
        var keys = new List<JsValue>();
        foreach (var entry in _insertionOrder)
        {
            if (!entry.IsAlive)
            {
                continue;
            }

            var key = entry.Key;
            if (key is null)
            {
                keys.Add(JsValue.Null);
            }
            else if (ReferenceEquals(key, Symbol.Undefined))
            {
                keys.Add(JsValue.Undefined);
            }
            else
            {
                keys.Add(JsValue.FromObjectUnsafe(key));
            }
        }

        return new JsArray(keys);
    }

    /// <summary>
    ///     Returns an array of values in the Map, in insertion order.
    /// </summary>
    public JsArray Values()
    {
        var values = new List<JsValue>();
        foreach (var entry in _insertionOrder)
        {
            if (!entry.IsAlive)
            {
                continue;
            }

            var key = entry.Key;
            if (key is null)
            {
                values.Add(_nullValue);
            }
            else if (ReferenceEquals(key, Symbol.Undefined))
            {
                values.Add(_undefinedValue);
            }
            else
            {
                values.Add(GetByObjectKey(key));
            }
        }

        return new JsArray(values);
    }

    private sealed class MapEntryRecord(object? key)
    {
        internal object? Key { get; } = key;
        internal bool IsAlive { get; private set; } = true;

        internal void Delete()
        {
            IsAlive = false;
        }
    }
}
