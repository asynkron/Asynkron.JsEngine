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
    private readonly List<object?> _insertionOrder = [];

    private readonly JsObject _properties = new();

    // Use HashSet for O(1) lookups
    private readonly HashSet<object> _set = new(SameValueZeroComparer.Instance);

    // Cached JsValue to avoid repeated struct creation
    // ReSharper disable once ReplaceWithFieldKeyword
    private readonly JsValue _cachedJsValue;

    public JsSet()
    {
        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
    }

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

    internal JsValue GetValue(int index)
    {
        return JsValue.FromObjectUnsafe(_insertionOrder[index]);
    }

    /// <summary>
    ///     Adds a value to the Set. Returns the Set object to allow chaining.
    ///     If the value is already in the Set, it is not added again.
    /// </summary>
    public JsSet Add(JsValue jsValue)
    {
        // Handle null
        if (jsValue.IsNull)
        {
            if (_hasNull)
            {
                return this;
            }

            _hasNull = true;
            _insertionOrder.Add(null);

            return this;
        }

        // Handle undefined
        if (jsValue.IsUndefined)
        {
            if (_hasUndefined)
            {
                return this;
            }

            _hasUndefined = true;
            _insertionOrder.Add(Symbol.Undefined);

            return this;
        }

        // Regular value - convert to object for HashSet storage
        var value = ExtractValueObject(jsValue);
        if (_set.Add(value))
        {
            _insertionOrder.Add(value);
        }

        return this;
    }

    /// <summary>
    ///     Returns true if the value exists in the Set, false otherwise.
    /// </summary>
    public bool Has(JsValue jsValue)
    {
        // Handle null
        if (jsValue.IsNull)
        {
            return _hasNull;
        }

        // Handle undefined
        if (jsValue.IsUndefined)
        {
            return _hasUndefined;
        }

        // Regular value - use HashSet
        var value = ExtractValueObject(jsValue);
        return _set.Contains(value);
    }

    /// <summary>
    ///     Removes the specified value from the Set.
    ///     Returns true if the value was in the Set and has been removed, false otherwise.
    /// </summary>
    public bool Delete(JsValue jsValue)
    {
        // Handle null
        if (jsValue.IsNull)
        {
            if (!_hasNull)
            {
                return false;
            }

            _hasNull = false;
            _insertionOrder.Remove(null);
            return true;
        }

        // Handle undefined
        if (jsValue.IsUndefined)
        {
            if (!_hasUndefined)
            {
                return false;
            }

            _hasUndefined = false;
            _insertionOrder.Remove(Symbol.Undefined);
            return true;
        }

        // Regular value - use HashSet
        var value = ExtractValueObject(jsValue);
        if (!_set.Remove(value))
        {
            return false;
        }

        _insertionOrder.Remove(value);
        return true;
    }

    /// <summary>
    /// Extracts the underlying value from a JsValue for HashSet storage.
    /// For primitives, returns a boxed value. For objects, returns the object reference.
    /// </summary>
    private static object ExtractValueObject(JsValue jsValue)
    {
        return jsValue.Kind switch
        {
            JsValueKind.Boolean => jsValue.NumberValue != 0, // Box boolean
            JsValueKind.Number => jsValue.NumberValue, // Box number
            JsValueKind.String => jsValue.ObjectValue ?? string.Empty,
            JsValueKind.Symbol => jsValue.ObjectValue ??
                                  throw new InvalidOperationException("Symbol value cannot be null"),
            JsValueKind.BigInt => jsValue.ObjectValue ??
                                  throw new InvalidOperationException("BigInt value cannot be null"),
            JsValueKind.Object => jsValue.ObjectValue ??
                                  throw new InvalidOperationException("Object value cannot be null"),
            _ => throw new InvalidOperationException($"Unexpected value kind: {jsValue.Kind}")
        };
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
    public void ForEach(IJsCallable callback, JsValue thisArg)
    {
        // Pre-allocate callback args array to avoid per-iteration allocation
        var callbackArgs = new JsValue[3];
        callbackArgs[2] = _cachedJsValue;

        foreach (var value in _insertionOrder)
        {
            // In Set.forEach, the value is passed as both the first and second argument
            var jsValue = JsValue.FromObjectUnsafe(value);
            callbackArgs[0] = jsValue;
            callbackArgs[1] = jsValue;
            callback.Invoke(callbackArgs, thisArg);
        }
    }

    /// <summary>
    ///     Returns an array of values in the Set, in insertion order.
    /// </summary>
    public JsArray Values()
    {
        var values = new List<JsValue>(_insertionOrder.Count);
        foreach (var value in _insertionOrder)
        {
            values.Add(JsValue.FromObjectUnsafe(value));
        }

        return new JsArray(values);
    }

    /// <summary>
    ///     Equality comparer implementing SameValueZero algorithm for Set values.
    ///     Similar to strict equality (===) but treats NaN as equal to NaN.
    /// </summary>
    private sealed class SameValueZeroComparer : IEqualityComparer<object>
    {
        public static readonly SameValueZeroComparer Instance = new();

        private SameValueZeroComparer()
        {
        }

        public new bool Equals(object? x, object? y)
        {
            // Handle null (shouldn't happen - we handle null/undefined separately)
            if (x == null && y == null)
            {
                return true;
            }

            if (x == null || y == null)
            {
                return false;
            }

            // Handle NaN (NaN is equal to NaN in SameValueZero)
            if (x is double.NaN && y is double.NaN)
            {
                return true;
            }

            // Handle strings - use ordinal value equality (JavaScript semantics)
            if (x is string sx && y is string sy)
            {
                return string.Equals(sx, sy, StringComparison.Ordinal);
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
            if (obj is double.NaN)
            {
                return 0; // All NaN values get the same hash
            }

            return obj.GetHashCode();
        }
    }
}
