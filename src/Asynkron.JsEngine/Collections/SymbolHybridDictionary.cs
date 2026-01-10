#region

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Collections;

/// <summary>
/// <para>
/// A dictionary optimized for small Symbol-keyed storage (typical JS environment bindings).
/// Uses a simple array-based storage for environments with few bindings (cutover at 8 items),
/// then switches to a full Dictionary when the environment grows beyond that threshold.
/// </para>
/// <para>
/// Uses reference equality for Symbol keys since Symbols are interned.
/// Most function environments have very few bindings (parameters + this + arguments).
/// </para>
/// </summary>
public sealed class SymbolHybridDictionary<TValue>
{
    private const int CutoverPoint = 8;
    private const int InitialArraySize = 4;
    private int _count;

    // Large storage - full dictionary with reference equality
    private Dictionary<Symbol, TValue>? _dictionary;

    // Small storage - array of key-value pairs
    private Entry[]? _entries;

    public TValue this[Symbol key]
    {
        [MethodImpl(JsEngineConstants.Inlining)]
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
        [MethodImpl(JsEngineConstants.Inlining)]
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
                if (!ReferenceEquals(_entries![i].Key, key))
                {
                    continue;
                }

                _entries[i].Value = value;
                return;
            }

            // Not found, add new
            AddInternal(key, value);
        }
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

    [MethodImpl(JsEngineConstants.Inlining)]
    private void AddInternal(Symbol key, TValue value)
    {
        if (_count >= CutoverPoint)
        {
            SwitchToDictionary();
            _dictionary!.Add(key, value);
            return;
        }

        if (_entries is null)
        {
            // Start with small array - most environments have few bindings
            _entries = new Entry[InitialArraySize];
        }
        else if (_count >= _entries.Length)
        {
            // Grow array when needed (double size up to cutover)
            var newSize = Math.Min(_entries.Length * 2, CutoverPoint);
            var newEntries = new Entry[newSize];
            Array.Copy(_entries, newEntries, _count);
            _entries = newEntries;
        }

        _entries[_count++] = new Entry { Key = key, Value = value };
    }

    /// <summary>
    /// Gets a reference to the value for the given key, or returns a null ref if not found.
    /// This mirrors CollectionsMarshal.GetValueRefOrNullRef for Dictionary.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
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

    [MethodImpl(JsEngineConstants.Inlining)]
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

    [MethodImpl(JsEngineConstants.Inlining)]
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

    private struct Entry
    {
        public Symbol Key;
        public TValue Value;
    }
}
