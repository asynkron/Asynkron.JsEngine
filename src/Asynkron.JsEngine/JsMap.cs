namespace Asynkron.JsEngine;

/// <summary>
/// Represents a JavaScript Map collection.
/// Maps hold key-value pairs and remember the original insertion order of keys.
/// Unlike objects, Map keys can be any value (including objects and functions).
/// </summary>
public sealed class JsMap
{
    // Use List to maintain insertion order
    private readonly List<KeyValuePair<object?, object?>> _entries = [];
    private readonly JsObject _properties = new();

    /// <summary>
    /// Gets the number of key-value pairs in the Map.
    /// </summary>
    public int Size => _entries.Count;

    public bool TryGetProperty(string name, out object? value)
    {
        return _properties.TryGetProperty(name, out value);
    }

    public void SetProperty(string name, object? value)
    {
        _properties.SetProperty(name, value);
    }

    /// <summary>
    /// Sets the value for the key in the Map. Returns the Map object to allow chaining.
    /// </summary>
    public JsMap Set(object? key, object? value)
    {
        // Find existing entry with the same key
        for (var i = 0; i < _entries.Count; i++)
            if (SameValueZero(_entries[i].Key, key))
            {
                _entries[i] = new KeyValuePair<object?, object?>(key, value);
                return this;
            }

        // Key not found, add new entry
        _entries.Add(new KeyValuePair<object?, object?>(key, value));
        return this;
    }

    /// <summary>
    /// Gets the value associated with the key, or undefined if the key doesn't exist.
    /// </summary>
    public object? Get(object? key)
    {
        foreach (var entry in _entries)
        {
            if (SameValueZero(entry.Key, key))
            {
                return entry.Value;
            }
        }

        return JsSymbols.Undefined;
    }

    /// <summary>
    /// Returns true if the key exists in the Map, false otherwise.
    /// </summary>
    public bool Has(object? key)
    {
        foreach (var entry in _entries)
        {
            if (SameValueZero(entry.Key, key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes the specified key and its value from the Map.
    /// Returns true if the key was in the Map and has been removed, false otherwise.
    /// </summary>
    public bool Delete(object? key)
    {
        for (var i = 0; i < _entries.Count; i++)
            if (SameValueZero(_entries[i].Key, key))
            {
                _entries.RemoveAt(i);
                return true;
            }

        return false;
    }

    /// <summary>
    /// Removes all key-value pairs from the Map.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
    }

    /// <summary>
    /// Executes a provided function once per each key-value pair in the Map, in insertion order.
    /// </summary>
    public void ForEach(IJsCallable callback, object? thisArg)
    {
        foreach (var entry in _entries)
        {
            callback.Invoke([entry.Value, entry.Key, this], thisArg);
        }
    }

    /// <summary>
    /// Returns an array of [key, value] pairs for every entry in the Map, in insertion order.
    /// </summary>
    public JsArray Entries()
    {
        var entries = new List<object?>();
        foreach (var entry in _entries)
        {
            var pair = new JsArray([entry.Key, entry.Value]);
            entries.Add(pair);
        }

        return new JsArray(entries);
    }

    /// <summary>
    /// Returns an array of keys in the Map, in insertion order.
    /// </summary>
    public JsArray Keys()
    {
        var keys = _entries.Select(e => e.Key).ToList();
        return new JsArray(keys);
    }

    /// <summary>
    /// Returns an array of values in the Map, in insertion order.
    /// </summary>
    public JsArray Values()
    {
        var values = _entries.Select(e => e.Value).ToList();
        return new JsArray(values);
    }

    /// <summary>
    /// Implements the SameValueZero comparison algorithm used by Map.
    /// Similar to strict equality (===) but treats NaN as equal to NaN.
    /// </summary>
    private static bool SameValueZero(object? x, object? y)
    {
        // Handle null/undefined
        if (x == null && y == null)
        {
            return true;
        }

        if (x == null || y == null)
        {
            return false;
        }

        // Handle NaN (NaN is equal to NaN in SameValueZero)
        if (x is double dx && double.IsNaN(dx) && y is double dy && double.IsNaN(dy))
        {
            return true;
        }

        // Handle strings - use value equality
        if (x is string sx && y is string sy)
        {
            return sx == sy;
        }

        // For reference types, use reference equality
        if (!x.GetType().IsValueType || !y.GetType().IsValueType)
        {
            return ReferenceEquals(x, y);
        }

        // For value types, use Equals
        return x.Equals(y);
    }
}