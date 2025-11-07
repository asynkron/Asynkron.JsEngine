namespace Asynkron.JsEngine;

/// <summary>
/// Minimal JavaScript-like array that tracks indexed elements and behaves like an object for property access.
/// </summary>
internal sealed class JsArray
{
    private readonly JsObject _properties = new();
    private readonly List<object?> _items = new();

    public JsArray()
    {
        UpdateLength();
    }

    public JsArray(IEnumerable<object?> items)
    {
        if (items is not null)
        {
            _items.AddRange(items);
        }

        UpdateLength();
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

    private void UpdateLength()
    {
        _properties.SetProperty("length", (double)_items.Count);
    }
}
