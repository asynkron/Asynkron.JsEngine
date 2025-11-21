using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Minimal JavaScript-like array that tracks indexed elements and behaves like an object for property access.
/// </summary>
public sealed class JsArray : IJsObjectLike
{
    // Sentinel value to represent holes in sparse arrays (indices that have never been set)
    private static readonly object ArrayHole = new();

    private const uint DenseIndexLimit = 1_000_000;
    private const uint MaxArrayLength = uint.MaxValue;

    private readonly JsObject _properties = new();
    private readonly List<object?> _items = [];
    private Dictionary<uint, object?>? _sparseItems;
    private uint _length;

    public JsArray()
    {
        _length = 0;
        if (StandardLibrary.ArrayPrototype is not null)
        {
            _properties.SetPrototype(StandardLibrary.ArrayPrototype);
        }
        UpdateLengthProperty();
        SetupIterator();
    }

    public JsArray(IEnumerable<object?> items)
    {
        if (items is not null)
        {
            _items.AddRange(items);
        }

        _length = (uint)_items.Count;
        if (StandardLibrary.ArrayPrototype is not null)
        {
            _properties.SetPrototype(StandardLibrary.ArrayPrototype);
        }
        UpdateLengthProperty();
        SetupIterator();
    }

    public IReadOnlyList<object?> Items => _items;

    /// <summary>
    /// Gets the length of the array
    /// </summary>
    public double Length => _length;

    public override string ToString()
    {
        // Match the behaviour of Array.prototype.toString / join with
        // the default separator so arrays used as property keys (e.g.
        // reverse[colorName[key]] in Babel’s color modules) produce a
        // stable comma-joined string rather than a CLR type name.
        if (_items.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(_items.Count);
        foreach (var item in _items)
        {
            if (ReferenceEquals(item, ArrayHole) || item is null || ReferenceEquals(item, Symbols.Undefined))
            {
                parts.Add(string.Empty);
                continue;
            }

            parts.Add(Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return string.Join(",", parts);
    }

    /// <summary>
    /// Gets an element at the specified index (alias for GetElement)
    /// </summary>
    public object? Get(int index) => GetElement(index);

    public void SetPrototype(object? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    public JsObject? Prototype => _properties.Prototype;
    public bool IsSealed => _properties.IsSealed;
    public IEnumerable<string> Keys => _properties.Keys;

    public bool TryGetProperty(string name, out object? value)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            value = (double)_length;
            return true;
        }

        if (TryParseArrayIndex(name, out var index))
        {
            if (TryGetOwnIndex(index, out value))
            {
                return true;
            }

            return _properties.TryGetProperty(name, out value);
        }

        return _properties.TryGetProperty(name, out value);
    }

    public void SetProperty(string name, object? value)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            if (!TryCoerceLength(value, out var newLength))
            {
                throw new InvalidOperationException("RangeError: Invalid array length");
            }

            SetExplicitLength(newLength);
            return;
        }

        if (TryParseArrayIndex(name, out var index))
        {
            SetElement(index, value);
            return;
        }

        _properties.SetProperty(name, value);
    }

    public object? GetElement(int index)
    {
        if (index < 0)
        {
            return Symbols.Undefined;
        }

        return GetElement((uint)index);
    }

    public object? GetElement(uint index)
    {
        if (index < _items.Count)
        {
            var item = _items[(int)index];
            // Return undefined for holes in the array
            return ReferenceEquals(item, ArrayHole) ? Symbols.Undefined : item;
        }

        if (_sparseItems is not null && _sparseItems.TryGetValue(index, out var value))
        {
            return value;
        }

        return Symbols.Undefined;
    }

    /// <summary>
    /// Returns true if the given index is an own data property on this array
    /// (i.e. within bounds and not a hole).
    /// </summary>
    public bool HasOwnIndex(uint index)
    {
        if (index < _items.Count)
        {
            return !ReferenceEquals(_items[(int)index], ArrayHole);
        }

        return _sparseItems is not null && _sparseItems.ContainsKey(index);
    }

    public bool HasOwnIndex(int index)
    {
        if (index < 0)
        {
            return false;
        }

        return HasOwnIndex((uint)index);
    }

    private bool TryGetOwnIndex(uint index, out object? value)
    {
        if (index < _items.Count)
        {
            var item = _items[(int)index];
            if (!ReferenceEquals(item, ArrayHole))
            {
                value = item;
                return true;
            }
        }

        if (_sparseItems is not null && _sparseItems.TryGetValue(index, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    public void SetElement(int index, object? value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        SetElement((uint)index, value);
    }

    public void SetElement(uint index, object? value)
    {
        var extended = false;
        if (index < DenseIndexLimit)
        {
            var denseIndex = (int)index;
            // Fill gaps with ArrayHole sentinel to represent sparse array holes
            while (_items.Count <= denseIndex)
            {
                _items.Add(ArrayHole);
                extended = true;
            }

            _items[denseIndex] = value;
        }
        else
        {
            _sparseItems ??= new Dictionary<uint, object?>();
            _sparseItems[index] = value;
        }

        if (extended)
        {
            BumpLength((uint)_items.Count);
            return;
        }

        BumpLength(index + 1);
    }

    /// <summary>
    /// Removes the element at the specified index without affecting the array length.
    /// JavaScript's delete operator leaves holes behind, which we represent via <see cref="ArrayHole"/>.
    /// </summary>
    public bool DeleteElement(int index)
    {
        if (index < 0)
        {
            return true;
        }

        var uintIndex = (uint)index;

        if (uintIndex < _items.Count)
        {
            _items[index] = ArrayHole;
            return true;
        }

        if (_sparseItems is not null)
        {
            _sparseItems.Remove(uintIndex);
        }

        return true;
    }

    /// <summary>
    /// Deletes a named property from the backing object storage.
    /// </summary>
    public bool DeleteProperty(string name)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            return false;
        }

        return _properties.Remove(name);
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        _properties.DefineProperty(name, descriptor);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        return _properties.GetOwnPropertyDescriptor(name);
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        return _properties.GetOwnPropertyNames();
    }

    public void Seal()
    {
        _properties.Seal();
    }

    public void Push(object? value)
    {
        _items.Add(value);
        BumpLength((uint)_items.Count);
    }

    public object? Pop()
    {
        if (_length == 0)
        {
            return Symbols.Undefined;
        }

        var lastIndex = _length - 1;
        object? value = Symbols.Undefined;

        if (lastIndex < _items.Count)
        {
            var denseIndex = (int)lastIndex;
            value = _items[denseIndex];
            _items.RemoveAt(denseIndex);
        }
        else if (_sparseItems is not null && _sparseItems.TryGetValue(lastIndex, out var sparseValue))
        {
            value = sparseValue;
            _sparseItems.Remove(lastIndex);
        }

        SetExplicitLength(_length - 1);

        // Return undefined for holes
        return ReferenceEquals(value, ArrayHole) ? Symbols.Undefined : value;
    }

    public object? Shift()
    {
        if (_length == 0 || _items.Count == 0)
        {
            return Symbols.Undefined;
        }

        var value = _items[0];
        _items.RemoveAt(0);
        SetExplicitLength(_length - 1);

        // Return undefined for holes
        return ReferenceEquals(value, ArrayHole) ? Symbols.Undefined : value;
    }

    public void Unshift(params object?[] values)
    {
        _items.InsertRange(0, values);
        BumpLength((uint)_items.Count);
    }

    public JsArray Splice(int start, int deleteCount, params object?[] itemsToInsert)
    {
        // Normalize start index
        if (start < 0)
        {
            start = Math.Max(0, _items.Count + start);
        }
        else
        {
            start = Math.Min(start, _items.Count);
        }

        // Normalize delete count
        deleteCount = Math.Max(0, Math.Min(deleteCount, _items.Count - start));

        // Create array of deleted items
        var deleted = new JsArray();
        for (var i = 0; i < deleteCount; i++)
        {
            deleted.Push(_items[start]);
            _items.RemoveAt(start);
        }

        // Insert new items
        if (itemsToInsert.Length > 0)
        {
            _items.InsertRange(start, itemsToInsert);
        }

        BumpLength((uint)_items.Count);
        return deleted;
    }

    public void Reverse()
    {
        _items.Reverse();
    }

    private void BumpLength(uint candidateLength)
    {
        if (candidateLength > MaxArrayLength)
        {
            throw new InvalidOperationException("RangeError: Invalid array length");
        }

        if (candidateLength > _length)
        {
            _length = candidateLength;
            UpdateLengthProperty();
        }
    }

    private void SetExplicitLength(uint newLength)
    {
        if (newLength > MaxArrayLength)
        {
            throw new InvalidOperationException("RangeError: Invalid array length");
        }

        _length = newLength;

        if (_items.Count > newLength)
        {
            _items.RemoveRange((int)newLength, _items.Count - (int)newLength);
        }

        if (_sparseItems is not null)
        {
            var keysToRemove = _sparseItems.Keys.Where(k => k >= newLength).ToArray();
            foreach (var key in keysToRemove)
            {
                _sparseItems.Remove(key);
            }
        }

        UpdateLengthProperty();
    }

    private void UpdateLengthProperty()
    {
        _properties.SetProperty("length", (double)_length);
    }

    private void SetupIterator()
    {
        // Set up Symbol.iterator
        var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

        // Create iterator function that returns an iterator object
        var iteratorFunction = new HostFunction((thisValue, args) =>
        {
            // Use array to hold index so it can be mutated in closure
            var indexHolder = new int[] { 0 };
            var iterator = new JsObject();

            // Add next() method to iterator
            iterator.SetProperty("next", new HostFunction((nextThisValue, nextArgs) =>
            {
                var result = new JsObject();
                if (indexHolder[0] < _items.Count)
                {
                    var value = _items[indexHolder[0]];
                    // Return undefined for holes in the array
                    result.SetProperty("value", ReferenceEquals(value, ArrayHole) ? Symbols.Undefined : value);
                    result.SetProperty("done", false);
                    indexHolder[0]++;
                }
                else
                {
                    result.SetProperty("value", Symbols.Undefined);
                    result.SetProperty("done", true);
                }

                return result;
            }));

            return iterator;
        });

        _properties.SetProperty(iteratorKey, iteratorFunction);
    }

    private static bool TryParseArrayIndex(string propertyName, out uint index)
    {
        index = 0;

        if (string.IsNullOrEmpty(propertyName))
        {
            return false;
        }

        if (!uint.TryParse(propertyName, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        // 2^32 - 1 is not a valid array index
        if (parsed == uint.MaxValue)
        {
            return false;
        }

        if (!string.Equals(parsed.ToString(CultureInfo.InvariantCulture), propertyName, StringComparison.Ordinal))
        {
            return false;
        }

        index = parsed;
        return true;
    }

    private static bool TryCoerceLength(object? value, out uint length)
    {
        length = 0;

        var numericValue = JsOps.ToNumber(value);

        if (double.IsNaN(numericValue) || double.IsInfinity(numericValue) || numericValue < 0)
        {
            return false;
        }

        var truncated = Math.Truncate(numericValue);
        if (Math.Abs(numericValue - truncated) > double.Epsilon)
        {
            return false;
        }

        if (truncated > MaxArrayLength)
        {
            return false;
        }

        length = (uint)truncated;
        return true;
    }
}
