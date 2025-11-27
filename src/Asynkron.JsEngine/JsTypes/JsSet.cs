namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript Set collection.
///     Sets store unique values of any type and remember the original insertion order.
/// </summary>
public sealed class JsSet : IJsObjectLike
{
    private readonly JsObject _properties = new();

    // Use List to maintain insertion order
    private readonly List<object?> _values = [];

    /// <summary>
    ///     Gets the number of values in the Set.
    /// </summary>
    public int Size => _values.Count;

    public bool TryGetProperty(string name, object? receiver, out object? value)
    {
        // Handle special 'size' property
        if (string.Equals(name, "size", StringComparison.Ordinal))
        {
            value = (double)Size;
            return true;
        }

        return _properties.TryGetProperty(name, receiver ?? this, out value);
    }

    public bool TryGetProperty(string name, out object? value)
    {
        return TryGetProperty(name, this, out value);
    }

    public void SetProperty(string name, object? value, object? receiver)
    {
        _properties.SetProperty(name, value, receiver ?? this);
    }

    public void SetProperty(string name, object? value)
    {
        SetProperty(name, value, this);
    }

    public JsObject? Prototype => _properties.Prototype;

    public bool IsSealed => _properties.IsSealed;

    IEnumerable<string> IJsObjectLike.Keys => _properties.Keys;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        _properties.DefineProperty(name, descriptor);
    }

    public void SetPrototype(object? candidate)
    {
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

    /// <summary>
    ///     Adds a value to the Set. Returns the Set object to allow chaining.
    ///     If the value is already in the Set, it is not added again.
    /// </summary>
    public JsSet Add(object? value)
    {
        // Check if value already exists
        if (!Has(value))
        {
            _values.Add(value);
        }

        return this;
    }

    /// <summary>
    ///     Returns true if the value exists in the Set, false otherwise.
    /// </summary>
    public bool Has(object? value)
    {
        foreach (var item in _values)
        {
            if (SameValueZero(item, value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Removes the specified value from the Set.
    ///     Returns true if the value was in the Set and has been removed, false otherwise.
    /// </summary>
    public bool Delete(object? value)
    {
        for (var i = 0; i < _values.Count; i++)
        {
            if (SameValueZero(_values[i], value))
            {
                _values.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Removes all values from the Set.
    /// </summary>
    public void Clear()
    {
        _values.Clear();
    }

    /// <summary>
    ///     Executes a provided function once per each value in the Set, in insertion order.
    ///     The callback receives (value, value, set) - value is passed twice for consistency with Map.
    /// </summary>
    public void ForEach(IJsCallable callback, object? thisArg)
    {
        foreach (var value in _values)
            // In Set.forEach, the value is passed as both the first and second argument
        {
            callback.Invoke([value, value, this], thisArg);
        }
    }

    /// <summary>
    ///     Returns an array of values in the Set, in insertion order.
    /// </summary>
    public JsArray Values()
    {
        return new JsArray(_values);
    }

    /// <summary>
    ///     Returns an array of [value, value] pairs for every entry in the Set, in insertion order.
    ///     The value is duplicated for consistency with Map.entries().
    /// </summary>
    public JsArray Entries()
    {
        var entries = new List<object?>();
        foreach (var value in _values)
        {
            var pair = new JsArray([value, value]);
            entries.Add(pair);
        }

        return new JsArray(entries);
    }

    /// <summary>
    ///     Returns an array of values in the Set, in insertion order.
    ///     This is an alias for Values() for consistency with Map.
    /// </summary>
    public JsArray Keys()
    {
        return Values();
    }

    /// <summary>
    ///     Implements the SameValueZero comparison algorithm used by Set.
    ///     Similar to strict equality (===) but treats NaN as equal to NaN.
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
        if (x is double and double.NaN && y is double and double.NaN)
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
