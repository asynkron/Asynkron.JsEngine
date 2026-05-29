#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript Set collection.
///     Sets store unique values of any type and remember the original insertion order.
/// </summary>
public sealed class JsSet : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl, IPrototypeAccessorProvider,
    IAsJsValue
{
    // Use List to maintain insertion order for iteration
    private readonly List<JsValue> _insertionOrder = [];

    private readonly JsObject _properties = new();

    // Use HashSet for O(1) lookups
    private readonly HashSet<JsValue> _set = new(SameValueZeroComparer.JsValueInstance);

    // Cached JsValue to avoid repeated struct creation
    // ReSharper disable once ReplaceWithFieldKeyword
    private readonly JsValue _cachedJsValue;

    public JsSet()
    {
        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
    }

    /// <summary>
    ///     Indicates whether this Set is "plain" - i.e., has no custom properties,
    ///     no modified prototype, and can use fast-path optimizations.
    /// </summary>
    internal bool IsPlain { get; private set; } = true;

    /// <summary>
    ///     Gets the number of values in the Set.
    /// </summary>
    public int Size => _set.Count;

    internal int ValueCount => _insertionOrder.Count;

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

    public bool TryGetProperty(string name, out JsValue value)
    {
        return TryGetProperty(name, _cachedJsValue, out value);
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        IsPlain = false;
        _properties.SetProperty(name, value, receiver.IsUndefined ? _cachedJsValue : receiver);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, _cachedJsValue);
    }

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

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name) =>
        _properties.GetOwnPropertyDescriptor(name);

    public IEnumerable<string> GetOwnPropertyNames() =>
        _properties.GetOwnPropertyNames();

    public IEnumerable<string> GetEnumerablePropertyNames() =>
        _properties.GetEnumerablePropertyNames();

    public IEnumerable<string> GetOwnPropertyKeysInOrder(bool includeSymbols = true, bool includeNonEnumerable = true) =>
        _properties.GetOwnPropertyKeysInOrder(includeSymbols, includeNonEnumerable);

    internal JsValue GetValue(int index)
    {
        return _insertionOrder[index];
    }

    /// <summary>
    ///     Adds a value to the Set. Returns the Set object to allow chaining.
    ///     If the value is already in the Set, it is not added again.
    /// </summary>
    public JsSet Add(JsValue jsValue)
    {
        if (_set.Add(jsValue))
        {
            _insertionOrder.Add(jsValue);
            JsObject.MarkGlobalMutation();
        }

        return this;
    }

    /// <summary>
    ///     Returns true if the value exists in the Set, false otherwise.
    /// </summary>
    public bool Has(JsValue jsValue)
    {
        return _set.Contains(jsValue);
    }

    /// <summary>
    ///     Removes the specified value from the Set.
    ///     Returns true if the value was in the Set and has been removed, false otherwise.
    /// </summary>
    public bool Delete(JsValue jsValue)
    {
        if (!_set.Remove(jsValue))
        {
            return false;
        }

        _insertionOrder.Remove(jsValue);
        JsObject.MarkGlobalMutation();
        return true;
    }

    /// <summary>
    ///     Removes all values from the Set.
    /// </summary>
    public void Clear()
    {
        if (_set.Count == 0)
        {
            return;
        }

        _set.Clear();
        _insertionOrder.Clear();
        JsObject.MarkGlobalMutation();
    }

    /// <summary>
    ///     Executes a provided function once per each value in the Set, in insertion order.
    ///     The callback receives (value, value, set) - value is passed twice for consistency with Map.
    ///     Per ES spec, values added during iteration are visited, and values deleted then re-added are revisited.
    /// </summary>
    public void ForEach(IJsCallable callback, JsValue thisArg)
    {
        // Use index-based iteration to handle mutations during iteration:
        // - New values added during callbacks appear at the end and are visited (Count grows).
        // - Deleted values are removed from _insertionOrder, shifting subsequent items left.
        //   After the callback, if the item at position i changed (was shifted), we do not
        //   advance i, so the shifted-in item is visited next.
        var i = 0;
        while (i < _insertionOrder.Count)
        {
            var value = _insertionOrder[i];
            callback.Invoke([value, value, _cachedJsValue], thisArg);

            // If the item at position i is still the same value we just visited, advance past it.
            // If it changed (due to a deletion shifting items left), stay at i to visit the new item.
            if (i < _insertionOrder.Count && Equals(_insertionOrder[i], value))
            {
                i++;
            }
        }
    }

    /// <summary>
    ///     Returns an array of values in the Set, in insertion order.
    /// </summary>
    public JsArray Values()
    {
        var values = new List<JsValue>(_insertionOrder.Count);
        values.AddRange(_insertionOrder);

        return new JsArray(values);
    }

    internal bool AnyValueMatches(Predicate<JsValue> predicate)
    {
        foreach (var value in _insertionOrder)
        {
            if (_set.Contains(value) && predicate(value))
            {
                return true;
            }
        }

        return false;
    }
}
