using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript Set collection.
///     Sets store unique values of any type and remember the original insertion order.
/// </summary>
public sealed class JsSet : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl, IPrototypeAccessorProvider
{
    private readonly JsObject _properties = new();

    // Use List to maintain insertion order for iteration
    private readonly List<object?> _insertionOrder = [];
    // Use HashSet for O(1) lookups
    private readonly HashSet<object> _set = new(SameValueZeroComparer.Instance);
    // Track null/undefined separately (can't be HashSet members with our comparer)
    private bool _hasNull;
    private bool _hasUndefined;

    /// <summary>
    ///     Indicates whether this Set is "plain" - i.e., has no custom properties,
    ///     no modified prototype, and can use fast-path optimizations.
    /// </summary>
    internal bool IsPlain { get; private set; } = true;

    /// <summary>
    ///     Gets the number of values in the Set.
    /// </summary>
    public int Size => _set.Count + (_hasNull ? 1 : 0) + (_hasUndefined ? 1 : 0);

    public bool IsExtensible => _properties.IsExtensible;

    public void PreventExtensions()
    {
        _properties.PreventExtensions();
    }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        // Handle special 'size' property
        if (string.Equals(name, "size", StringComparison.Ordinal))
        {
            value = (double)Size;
            return true;
        }

        return _properties.TryGetProperty(name, receiver.IsUndefined ? (JsValue)this : receiver, out value);
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        return TryGetProperty(name, (JsValue)this, out value);
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        IsPlain = false;
        _properties.SetProperty(name, value, receiver.IsUndefined ? (JsValue)this : receiver);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, (JsValue)this);
    }

    public JsObject? Prototype => _properties.Prototype;
    public IJsPropertyAccessor? PrototypeAccessor =>
        _properties is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

    public bool IsSealed => _properties.IsSealed;
    public bool IsFrozen => _properties.IsFrozen;

    IEnumerable<string> IJsObjectLike.Keys => _properties.Keys;

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        IsPlain = false;
        _properties.DefineProperty(name, descriptor);
    }

    public void SetPrototype(object? candidate)
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

    internal int ValueCount => _insertionOrder.Count;

    internal object? GetValue(int index)
    {
        return _insertionOrder[index];
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        IsPlain = false;
        return _properties.TryDefineProperty(name, descriptor);
    }

    /// <summary>
    ///     Adds a value to the Set. Returns the Set object to allow chaining.
    ///     If the value is already in the Set, it is not added again.
    /// </summary>
    public JsSet Add(object? value)
    {
        // Handle null
        if (value is null)
        {
            if (!_hasNull)
            {
                _hasNull = true;
                _insertionOrder.Add(null);
            }
            return this;
        }

        // Handle undefined
        if (ReferenceEquals(value, Symbol.Undefined))
        {
            if (!_hasUndefined)
            {
                _hasUndefined = true;
                _insertionOrder.Add(Symbol.Undefined);
            }
            return this;
        }

        // Regular value - use HashSet
        if (_set.Add(value))
        {
            _insertionOrder.Add(value);
        }
        return this;
    }

    /// <summary>
    ///     Returns true if the value exists in the Set, false otherwise.
    /// </summary>
    public bool Has(object? value)
    {
        // Handle null
        if (value is null)
        {
            return _hasNull;
        }

        // Handle undefined
        if (ReferenceEquals(value, Symbol.Undefined))
        {
            return _hasUndefined;
        }

        // Regular value - use HashSet
        return _set.Contains(value);
    }

    /// <summary>
    ///     Removes the specified value from the Set.
    ///     Returns true if the value was in the Set and has been removed, false otherwise.
    /// </summary>
    public bool Delete(object? value)
    {
        // Handle null
        if (value is null)
        {
            if (!_hasNull) return false;
            _hasNull = false;
            _insertionOrder.Remove(null);
            return true;
        }

        // Handle undefined
        if (ReferenceEquals(value, Symbol.Undefined))
        {
            if (!_hasUndefined) return false;
            _hasUndefined = false;
            _insertionOrder.Remove(Symbol.Undefined);
            return true;
        }

        // Regular value - use HashSet
        if (!_set.Remove(value)) return false;
        _insertionOrder.Remove(value);
        return true;
    }

    /// <summary>
    ///     Removes all values from the Set.
    /// </summary>
    public void Clear()
    {
        _set.Clear();
        _insertionOrder.Clear();
        _hasNull = false;
        _hasUndefined = false;
    }

    /// <summary>
    ///     Executes a provided function once per each value in the Set, in insertion order.
    ///     The callback receives (value, value, set) - value is passed twice for consistency with Map.
    /// </summary>
    public void ForEach(IJsCallable callback, object? thisArg)
    {
        foreach (var value in _insertionOrder)
            // In Set.forEach, the value is passed as both the first and second argument
        {
            callback.Invoke([JsValue.FromObject(value), JsValue.FromObject(value), (JsValue)this], JsValue.FromObject(thisArg));
        }
    }

    /// <summary>
    ///     Returns an array of values in the Set, in insertion order.
    /// </summary>
    public JsArray Values()
    {
        return new JsArray(_insertionOrder.ToList());
    }

    /// <summary>
    ///     Returns an array of [value, value] pairs for every entry in the Set, in insertion order.
    ///     The value is duplicated for consistency with Map.entries().
    /// </summary>
    public JsArray Entries()
    {
        var entries = new List<object?>();
        foreach (var value in _insertionOrder)
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
    ///     Equality comparer implementing SameValueZero algorithm for Set values.
    ///     Similar to strict equality (===) but treats NaN as equal to NaN.
    /// </summary>
    private sealed class SameValueZeroComparer : IEqualityComparer<object>
    {
        public static readonly SameValueZeroComparer Instance = new();

        private SameValueZeroComparer() { }

        public new bool Equals(object? x, object? y)
        {
            // Handle null (shouldn't happen - we handle null/undefined separately)
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;

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

        public int GetHashCode(object obj)
        {
            // Handle NaN - all NaN values should hash the same
            if (obj is double d && double.IsNaN(d))
            {
                return 0; // All NaN values get the same hash
            }

            return obj.GetHashCode();
        }
    }
}
