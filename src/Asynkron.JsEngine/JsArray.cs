namespace Asynkron.JsEngine;

/// <summary>
/// Minimal JavaScript-like array that tracks indexed elements and behaves like an object for property access.
/// </summary>
internal sealed class JsArray
{
    private readonly JsObject _properties = new();
    private readonly List<object?> _items = [];

    public JsArray()
    {
        UpdateLength();
        SetupIterator();
    }

    public JsArray(IEnumerable<object?> items)
    {
        if (items is not null)
        {
            _items.AddRange(items);
        }

        UpdateLength();
        SetupIterator();
    }

    public IReadOnlyList<object?> Items => _items;

    public void SetPrototype(object? candidate) => _properties.SetPrototype(candidate);

    public bool TryGetProperty(string name, out object? value) => _properties.TryGetProperty(name, out value);

    public void SetProperty(string name, object? value) => _properties.SetProperty(name, value);

    public object? GetElement(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return null; // mirror JavaScript's undefined for out of range reads
        }

        return _items[index];
    }

    public void SetElement(int index, object? value)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        while (_items.Count <= index)
        {
            _items.Add(null);
        }

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
        if (_items.Count == 0)
        {
            return null;
        }

        var lastIndex = _items.Count - 1;
        var value = _items[lastIndex];
        _items.RemoveAt(lastIndex);
        UpdateLength();
        return value;
    }

    public object? Shift()
    {
        if (_items.Count == 0)
        {
            return null;
        }

        var value = _items[0];
        _items.RemoveAt(0);
        UpdateLength();
        return value;
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
        for (int i = 0; i < deleteCount; i++)
        {
            deleted.Push(_items[start]);
            _items.RemoveAt(start);
        }

        // Insert new items
        if (itemsToInsert.Length > 0)
        {
            _items.InsertRange(start, itemsToInsert);
        }

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
            var index = 0;
            var iterator = new JsObject();
            
            // Add next() method to iterator
            iterator.SetProperty("next", new HostFunction((nextThisValue, nextArgs) =>
            {
                var result = new JsObject();
                if (index < _items.Count)
                {
                    result.SetProperty("value", _items[index]);
                    result.SetProperty("done", false);
                    index++;
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
}
