using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Collections;

/// <summary>
/// A dictionary optimized for small Symbol-keyed storage (typical JS environment bindings).
/// Uses a simple array-based storage for environments with few bindings (cutover at 8 items),
/// then switches to a full Dictionary when the environment grows beyond that threshold.
///
/// Uses reference equality for Symbol keys since Symbols are interned.
/// Most function environments have very few bindings (parameters + this + arguments).
/// </summary>
public sealed class SymbolHybridDictionary<TValue>
{
    private const int CutoverPoint = 8;

    // Small storage - array of key-value pairs
    private Entry[]? _entries;
    private int _count;

    // Large storage - full dictionary with reference equality
    private Dictionary<Symbol, TValue>? _dictionary;

    private struct Entry
    {
        public Symbol Key;
        public TValue Value;
    }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _dictionary?.Count ?? _count;
    }

    public TValue this[Symbol key]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_dictionary is not null)
            {
                return _dictionary[key];
            }

            for (var i = 0; i < _count; i++)
            {
                if (ReferenceEquals(_entries![i].Key, key))
                {
                    return _entries[i].Value;
                }
            }

            throw new KeyNotFoundException($"Key not found: {key}");
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (_dictionary is not null)
            {
                _dictionary[key] = value;
                return;
            }

            // Try to find existing entry using reference equality
            for (var i = 0; i < _count; i++)
            {
                if (ReferenceEquals(_entries![i].Key, key))
                {
                    _entries[i].Value = value;
                    return;
                }
            }

            // Not found, add new
            AddInternal(key, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Symbol key, TValue value)
    {
        if (_dictionary is not null)
        {
            _dictionary.Add(key, value);
            return;
        }

        AddInternal(key, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddInternal(Symbol key, TValue value)
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

    /// <summary>
    /// Gets a reference to the value for the given key, or returns a null ref if not found.
    /// This mirrors CollectionsMarshal.GetValueRefOrNullRef for Dictionary.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TValue GetValueRefOrNullRef(Symbol key)
    {
        if (_dictionary is not null)
        {
            return ref CollectionsMarshal.GetValueRefOrNullRef(_dictionary, key);
        }

        for (var i = 0; i < _count; i++)
        {
            if (ReferenceEquals(_entries![i].Key, key))
            {
                return ref _entries[i].Value;
            }
        }

        return ref Unsafe.NullRef<TValue>();
    }

    private void SwitchToDictionary()
    {
        _dictionary = new Dictionary<Symbol, TValue>(_count + 4, ReferenceEqualityComparer<Symbol>.Instance);
        for (var i = 0; i < _count; i++)
        {
            _dictionary[_entries![i].Key] = _entries[i].Value;
        }
        _entries = null;
        _count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(Symbol key)
    {
        if (_dictionary is not null)
        {
            return _dictionary.ContainsKey(key);
        }

        for (var i = 0; i < _count; i++)
        {
            if (ReferenceEquals(_entries![i].Key, key))
            {
                return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(Symbol key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_dictionary is not null)
        {
            return _dictionary.TryGetValue(key, out value);
        }

        for (var i = 0; i < _count; i++)
        {
            if (ReferenceEquals(_entries![i].Key, key))
            {
                value = _entries[i].Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool Remove(Symbol key)
    {
        if (_dictionary is not null)
        {
            return _dictionary.Remove(key);
        }

        for (var i = 0; i < _count; i++)
        {
            if (ReferenceEquals(_entries![i].Key, key))
            {
                // Shift entries down to fill the gap
                for (var j = i; j < _count - 1; j++)
                {
                    _entries![j] = _entries[j + 1];
                }
                _entries![--_count] = default;
                return true;
            }
        }

        return false;
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

    public IEnumerable<Symbol> Keys
    {
        get
        {
            if (_dictionary is not null)
            {
                return _dictionary.Keys;
            }

            var keys = new Symbol[_count];
            for (var i = 0; i < _count; i++)
            {
                keys[i] = _entries![i].Key;
            }
            return keys;
        }
    }

    public IEnumerable<TValue> Values
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

    public IEnumerable<KeyValuePair<Symbol, TValue>> GetEntries()
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
            yield return new KeyValuePair<Symbol, TValue>(_entries![i].Key, _entries[i].Value);
        }
    }

    public IEnumerator<KeyValuePair<Symbol, TValue>> GetEnumerator()
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
            yield return new KeyValuePair<Symbol, TValue>(_entries![i].Key, _entries[i].Value);
        }
    }
}
