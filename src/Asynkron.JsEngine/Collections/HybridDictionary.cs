#region

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine.Collections;

/// <summary>
/// <para>
/// A dictionary optimized for small object storage (typical JS objects).
/// Uses a simple array-based storage for objects with few properties (cutover at 9 items),
/// then switches to a full Dictionary when the object grows beyond that threshold.
/// </para>
/// <para>Based on Jint's HybridDictionary optimization - most JS objects have &lt; 5 properties.</para>
/// </summary>
public sealed class HybridDictionary<TValue> : IDictionary<string, TValue>
{
    private const int CutoverPoint = 9;
    private int _count;

    // Large object storage - full dictionary
    private Dictionary<string, TValue>? _dictionary;

    // Small object storage - array of key-value pairs
    private Entry[]? _entries;

    public int Count
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        get => _dictionary?.Count ?? _count;
    }

    public bool IsReadOnly => false;

    public ICollection<string> Keys
    {
        get
        {
            if (_dictionary is not null)
            {
                return _dictionary.Keys;
            }

            var keys = new string[_count];
            for (var i = 0; i < _count; i++)
            {
                keys[i] = _entries![i].Key;
            }

            return keys;
        }
    }

    public ICollection<TValue> Values
    {
        get
        {
            if (_dictionary is not null)
            {
                return _dictionary.Values;
            }

            var values = new TValue[_count];
            for (var i = 0; i < _count; i++)
            {
                values[i] = _entries![i].Value;
            }

            return values;
        }
    }

    public TValue this[string key]
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        get
        {
            if (_dictionary is not null)
            {
                return _dictionary[key];
            }

            if (TryFindEntry(key, out var index))
            {
                return _entries![index].Value;
            }

            throw new KeyNotFoundException($"Key not found: {key}");
        }
        [MethodImpl(JsEngineConstants.Inlining)]
        set
        {
            if (_dictionary is not null)
            {
                _dictionary[key] = value;
                return;
            }

            if (TryFindEntry(key, out var index))
            {
                _entries![index].Value = value;
                return;
            }

            AddInternal(key, value);
        }
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public void Add(string key, TValue value)
    {
        if (_dictionary is not null)
        {
            _dictionary.Add(key, value);
            return;
        }

        if (TryFindEntry(key, out _))
        {
            throw new ArgumentException($"Key already exists: {key}");
        }

        AddInternal(key, value);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public bool ContainsKey(string key)
    {
        if (_dictionary is not null)
        {
            return _dictionary.ContainsKey(key);
        }

        return TryFindEntry(key, out _);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_dictionary is not null)
        {
            return _dictionary.TryGetValue(key, out value);
        }

        if (TryFindEntry(key, out var index))
        {
            value = _entries![index].Value;
            return true;
        }

        value = default;
        return false;
    }

    public bool Remove(string key)
    {
        if (_dictionary is not null)
        {
            return _dictionary.Remove(key);
        }

        if (!TryFindEntry(key, out var index))
        {
            return false;
        }

        // Shift entries down to fill the gap
        for (var i = index; i < _count - 1; i++)
        {
            _entries![i] = _entries[i + 1];
        }

        _entries![--_count] = default;
        return true;
    }

    public void Clear()
    {
        if (_dictionary is not null)
        {
            _dictionary.Clear();
            return;
        }

        if (_entries is not null)
        {
            Array.Clear(_entries, 0, _count);
        }

        _count = 0;
    }

    public void Add(KeyValuePair<string, TValue> item)
    {
        Add(item.Key, item.Value);
    }

    public bool Contains(KeyValuePair<string, TValue> item)
    {
        if (TryGetValue(item.Key, out var value))
        {
            return EqualityComparer<TValue>.Default.Equals(value, item.Value);
        }

        return false;
    }

    public void CopyTo(KeyValuePair<string, TValue>[] array, int arrayIndex)
    {
        if (_dictionary is not null)
        {
            ((ICollection<KeyValuePair<string, TValue>>)_dictionary).CopyTo(array, arrayIndex);
            return;
        }

        for (var i = 0; i < _count; i++)
        {
            array[arrayIndex + i] = new KeyValuePair<string, TValue>(_entries![i].Key, _entries[i].Value);
        }
    }

    public bool Remove(KeyValuePair<string, TValue> item)
    {
        if (Contains(item))
        {
            return Remove(item.Key);
        }

        return false;
    }

    public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
    {
        if (_dictionary is not null)
        {
            foreach (var kvp in _dictionary)
            {
                yield return kvp;
            }

            yield break;
        }

        for (var i = 0; i < _count; i++)
        {
            yield return new KeyValuePair<string, TValue>(_entries![i].Key, _entries[i].Value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private void AddInternal(string key, TValue value)
    {
        if (_count >= CutoverPoint)
        {
            SwitchToDictionary();
            _dictionary!.Add(key, value);
            return;
        }

        _entries ??= new Entry[CutoverPoint];
        _entries[_count++] = new Entry { Key = key, Value = value };
    }

    private void SwitchToDictionary()
    {
        _dictionary = new Dictionary<string, TValue>(_count + 4, StringComparer.Ordinal);
        for (var i = 0; i < _count; i++)
        {
            _dictionary[_entries![i].Key] = _entries[i].Value;
        }

        _entries = null;
        _count = 0;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private bool TryFindEntry(string key, out int index)
    {
        for (var i = 0; i < _count; i++)
        {
            if (string.Equals(_entries![i].Key, key, StringComparison.Ordinal))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private struct Entry
    {
        public string Key;
        public TValue Value;
    }
}
