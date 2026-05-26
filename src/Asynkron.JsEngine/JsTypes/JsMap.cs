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
    private readonly Dictionary<JsValue, JsValue> _map = new(SameValueZeroComparer.JsValueInstance);
    private readonly Dictionary<JsValue, MapEntryRecord> _entryRecords = new(SameValueZeroComparer.JsValueInstance);

    private readonly JsObject _properties = new();

    // Cached JsValue to avoid repeated struct creation
    private readonly JsValue _cachedJsValue;

    public JsMap()
    {
        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
    }

    /// <summary>
    ///     Indicates whether this Map is "plain" - i.e., has no custom properties,
    ///     no modified prototype, and can use fast-path optimizations.
    /// </summary>
    internal bool IsPlain { get; private set; } = true;

    /// <summary>
    ///     Gets the number of key-value pairs in the Map.
    /// </summary>
    public int Size => _map.Count;

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

    internal KeyValuePair<JsValue, JsValue> GetEntry(int index)
    {
        var entry = _insertionOrder[index];
        var value = GetByKey(entry.Key);
        return new KeyValuePair<JsValue, JsValue>(entry.Key, value);
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
    ///     Internal method to get value by key (used by iteration methods).
    /// </summary>
    private JsValue GetByKey(JsValue key)
    {
        key = CanonicalizeKey(key);
        return _map.TryGetValue(key, out var value) ? value : JsValue.Undefined;
    }

    /// <summary>
    ///     Sets the value for the key in the Map. Returns the Map object to allow chaining.
    /// </summary>
    public JsMap Set(JsValue key, JsValue value)
    {
        key = CanonicalizeKey(key);
        var hadEntry = _map.TryGetValue(key, out var previousValue);
        if (!hadEntry)
        {
            var entry = new MapEntryRecord(key);
            _insertionOrder.Add(entry);
            _entryRecords[key] = entry;
        }

        _map[key] = value;
        if (!hadEntry || !previousValue.Equals(value))
        {
            JsObject.MarkGlobalMutation();
        }

        return this;
    }

    /// <summary>
    ///     Gets the value associated with the key, or undefined if the key doesn't exist.
    /// </summary>
    public JsValue Get(JsValue key)
    {
        key = CanonicalizeKey(key);
        return _map.TryGetValue(key, out var value) ? value : JsValue.Undefined;
    }

    /// <summary>
    ///     Returns true if the key exists in the Map, false otherwise.
    /// </summary>
    public bool Has(JsValue key)
    {
        key = CanonicalizeKey(key);
        return _map.ContainsKey(key);
    }

    /// <summary>
    ///     Removes the specified key and its value from the Map.
    ///     Returns true if the key was in the Map and has been removed, false otherwise.
    /// </summary>
    public bool Delete(JsValue key)
    {
        key = CanonicalizeKey(key);
        if (!_map.Remove(key))
        {
            return false;
        }

        if (_entryRecords.Remove(key, out var entry))
        {
            entry.Delete();
        }

        // Don't remove from _insertionOrder — ForEach uses index-based iteration
        // and removing would shift indices, corrupting the loop.
        JsObject.MarkGlobalMutation();
        return true;
    }

    /// <summary>
    ///     Removes all key-value pairs from the Map.
    /// </summary>
    public void Clear()
    {
        if (_map.Count == 0)
        {
            return;
        }

        _map.Clear();
        _insertionOrder.Clear();
        _entryRecords.Clear();
        JsObject.MarkGlobalMutation();
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
            var value = GetByKey(key);
            callback.Invoke([value, key, _cachedJsValue], thisArg);
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
            var pair = new JsArray(new JsValue[] { key, GetByKey(key) });
            entries.Add(JsValue.FromJsArray(pair));
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

            keys.Add(entry.Key);
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

            values.Add(GetByKey(entry.Key));
        }

        return new JsArray(values);
    }

    internal bool AnyEntryComponentMatches(Predicate<JsValue> predicate)
    {
        foreach (var entry in _insertionOrder)
        {
            if (!entry.IsAlive || !_map.TryGetValue(entry.Key, out var value))
            {
                continue;
            }

            if (predicate(entry.Key) || predicate(value))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class MapEntryRecord(JsValue key)
    {
        internal JsValue Key { get; } = key;
        internal bool IsAlive { get; private set; } = true;

        internal void Delete()
        {
            IsAlive = false;
        }
    }

    private static JsValue CanonicalizeKey(JsValue key)
    {
        return key.IsNumber && key.NumberValue == 0d
            ? JsValue.Zero
            : key;
    }
}
