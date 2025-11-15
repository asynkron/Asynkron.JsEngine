using System.Globalization;

namespace Asynkron.JsEngine;

/// <summary>
/// Minimal JavaScript-like array that tracks indexed elements and behaves like an object for property access.
/// </summary>
public sealed class JsArray
{
    // Sentinel value to represent holes in sparse arrays (indices that have never been set)
    private static readonly object ArrayHole = new();
    
    private readonly JsObject _properties = new();
    private readonly List<object?> _items = [];

    public JsArray()
    {
        UpdateLength();
        SetupIterator();
    }

    public JsArray(IEnumerable<object?> items)
    {
        if (items is not null) _items.AddRange(items);

        UpdateLength();
        SetupIterator();
    }

    public IReadOnlyList<object?> Items => _items;

    /// <summary>
    /// Gets the length of the array
    /// </summary>
    public int Length => _items.Count;

    /// <summary>
    /// Gets an element at the specified index (alias for GetElement)
    /// </summary>
    public object? Get(int index) => GetElement(index);

    public void SetPrototype(object? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    public bool TryGetProperty(string name, out object? value)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            value = (double)_items.Count;
            return true;
        }

        if (TryParseArrayIndex(name, out var index))
        {
            value = GetElement(index);
            return true;
        }

        return _properties.TryGetProperty(name, out value);
    }

    public void SetProperty(string name, object? value)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            if (!TryCoerceLength(value, out var newLength))
                throw new InvalidOperationException("RangeError: Invalid array length");

            if (newLength < _items.Count)
            {
                _items.RemoveRange(newLength, _items.Count - newLength);
            }
            else if (newLength > _items.Count)
            {
                while (_items.Count < newLength)
                    _items.Add(ArrayHole);
            }

            UpdateLength();
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
        if (index < 0 || index >= _items.Count) return null;

        var item = _items[index];
        // Return null (representing undefined) for holes in the array
        return ReferenceEquals(item, ArrayHole) ? null : item;
    }

    public void SetElement(int index, object? value)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

        // Fill gaps with ArrayHole sentinel to represent sparse array holes
        while (_items.Count <= index) _items.Add(ArrayHole);

        _items[index] = value;
        UpdateLength();
    }

    public void Push(object? value)
    {
        _items.Add(value);
        UpdateLength();
    }

    public object? Pop()
    {
        if (_items.Count == 0) return JsSymbols.Undefined;

        var lastIndex = _items.Count - 1;
        var value = _items[lastIndex];
        _items.RemoveAt(lastIndex);
        UpdateLength();
        
        // Return undefined for holes
        return ReferenceEquals(value, ArrayHole) ? JsSymbols.Undefined : value;
    }

    public object? Shift()
    {
        if (_items.Count == 0) return JsSymbols.Undefined;

        var value = _items[0];
        _items.RemoveAt(0);
        UpdateLength();
        
        // Return undefined for holes
        return ReferenceEquals(value, ArrayHole) ? JsSymbols.Undefined : value;
    }

    public void Unshift(params object?[] values)
    {
        _items.InsertRange(0, values);
        UpdateLength();
    }

    public JsArray Splice(int start, int deleteCount, params object?[] itemsToInsert)
    {
        // Normalize start index
        if (start < 0)
            start = Math.Max(0, _items.Count + start);
        else
            start = Math.Min(start, _items.Count);

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
        if (itemsToInsert.Length > 0) _items.InsertRange(start, itemsToInsert);

        UpdateLength();
        return deleted;
    }

    public void Reverse()
    {
        _items.Reverse();
    }

    private void UpdateLength()
    {
        _properties.SetProperty("length", (double)_items.Count);
    }

    private void SetupIterator()
    {
        // Set up Symbol.iterator
        var iteratorSymbol = JsSymbol.For("Symbol.iterator");
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
                    result.SetProperty("value", ReferenceEquals(value, ArrayHole) ? JsSymbols.Undefined : value);
                    result.SetProperty("done", false);
                    indexHolder[0]++;
                }
                else
                {
                    result.SetProperty("value", JsSymbols.Undefined);
                    result.SetProperty("done", true);
                }

                return result;
            }));

            return iterator;
        });

        _properties.SetProperty(iteratorKey, iteratorFunction);
    }

    private static bool TryParseArrayIndex(string propertyName, out int index)
    {
        index = 0;

        if (string.IsNullOrEmpty(propertyName))
            return false;

        if (!uint.TryParse(propertyName, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            return false;

        if (parsed == uint.MaxValue)
            return false;

        if (!string.Equals(parsed.ToString(CultureInfo.InvariantCulture), propertyName, StringComparison.Ordinal))
            return false;

        if (parsed > int.MaxValue)
            return false;

        index = (int)parsed;
        return true;
    }

    private static bool TryCoerceLength(object? value, out int length)
    {
        length = 0;

        double numericValue = value switch
        {
            null => 0d,
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            uint ui => ui,
            long l => l,
            ulong ul => ul,
            short s => s,
            ushort us => us,
            byte b => b,
            sbyte sb => sb,
            bool flag => flag ? 1d : 0d,
            string str when double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => double.NaN
        };

        if (double.IsNaN(numericValue) || double.IsInfinity(numericValue) || numericValue < 0)
            return false;

        var truncated = Math.Truncate(numericValue);
        if (Math.Abs(numericValue - truncated) > double.Epsilon)
            return false;

        if (truncated > uint.MaxValue - 1)
            return false;

        var coerced = (uint)truncated;
        if (coerced > int.MaxValue)
            return false;

        length = (int)coerced;
        return true;
    }
}
