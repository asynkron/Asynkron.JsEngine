using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Minimal JavaScript-like array that tracks indexed elements and behaves like an object for property access.
/// </summary>
public sealed class JsArray : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl, IPrototypeAccessorProvider
{
    private const uint DenseIndexLimit = 1_000_000;

    private const uint MaxArrayLength = uint.MaxValue;

    // Sentinel value to represent holes in sparse arrays (indices that have never been set)
    // We use a special JsValue kind or a unique object that we can identify
    private static readonly object ArrayHoleSentinel = new();
    private static readonly JsValue ArrayHole = JsValue.FromObject(ArrayHoleSentinel);

    private static bool IsArrayHole(JsValue value) =>
        value.Kind == JsValueKind.Object && ReferenceEquals(value.AsObject(), ArrayHoleSentinel);
    private readonly IJsObjectLike? _arrayPrototype;
    private readonly List<JsValue> _items = [];

    private readonly JsObject _properties = new();
    private readonly IJsCallable? _rangeErrorCtor;
    private readonly RealmState? _realmState;

    /// <summary>
    /// Gets the RealmState associated with this array.
    /// </summary>
    public RealmState? RealmState => _realmState;
    private readonly IJsCallable? _typeErrorCtor;
    private uint _length;
    private Dictionary<uint, JsValue>? _sparseItems;

    public JsArray(RealmState? realmState = null)
    {
        _realmState = realmState;
        _rangeErrorCtor = realmState?.RangeErrorConstructor;
        _typeErrorCtor = realmState?.TypeErrorConstructor;
        _arrayPrototype = realmState?.ArrayPrototype;
        _length = 0;
        _properties.RealmState = realmState;
        if (_arrayPrototype is not null)
        {
            _properties.SetPrototype(_arrayPrototype);
        }
        else
        {
            _realmState?.Logger?.LogWarning("JsArray constructed without ArrayPrototype");
        }

        DefineInitialLengthProperty();
        SetupIterator();
    }

    public JsArray(IEnumerable<JsValue> items, RealmState? realmState = null)
        : this(realmState)
    {
        _items.AddRange(items);
        _length = (uint)_items.Count;
    }

    /// <summary>
    /// Convenience constructor that wraps objects in JsValue.
    /// </summary>
    public JsArray(IEnumerable<object?> items, RealmState? realmState = null)
        : this(realmState)
    {
        _items.AddRange(items.Select(JsValue.FromObject));
        _length = (uint)_items.Count;
    }

    public IReadOnlyList<JsValue> Items => _items;

    /// <summary>
    ///     Gets the length of the array
    /// </summary>
    public double Length => _length;

    public bool IsExtensible => _properties.IsExtensible;

    public void PreventExtensions()
    {
        _properties.PreventExtensions();
    }

    public void Freeze()
    {
        _properties.Freeze();
    }

    public void SetPrototype(object? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    public JsObject? Prototype
    {
        get
        {
            if (_properties.Prototype is null &&
                _properties is IPrototypeAccessorProvider { PrototypeAccessor: null } &&
                _arrayPrototype is not null)
            {
                _properties.SetPrototype(_arrayPrototype);
            }

            return _properties.Prototype;
        }
    }

    public bool IsSealed => _properties.IsSealed;
    public bool IsFrozen => _properties.IsFrozen;
    public IJsPropertyAccessor? PrototypeAccessor =>
        _properties is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;
    public IEnumerable<string> Keys => _properties.Keys;

    public bool TryGetProperty(string name, out JsValue value)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            value = JsValue.FromObject((double)_length);
            return true;
        }

        if (TryParseArrayIndex(name, out var index))
        {
            // Accessor/data descriptors defined via Object.defineProperty on an
            // index should override the internal dense/sparse storage.
            if (_properties.GetOwnPropertyDescriptor(name) is not null &&
                _properties.TryGetProperty(name, JsValue.FromObject(this), out value))
            {
                return true;
            }

            if (TryGetOwnIndex(index, out var jsValue))
            {
                value = jsValue;
                return true;
            }

            // For holes, continue lookup on the prototype chain.
            return _properties.TryGetProperty(name, JsValue.FromObject(this), out value);
        }

        return _properties.TryGetProperty(name, JsValue.FromObject(this), out value);
    }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            value = JsValue.FromObject((double)_length);
            return true;
        }

        if (TryParseArrayIndex(name, out var index))
        {
            // Accessor/data descriptors defined via Object.defineProperty on an
            // index should override the internal dense/sparse storage.
            if (_properties.GetOwnPropertyDescriptor(name) is not null &&
                _properties.TryGetProperty(name, receiver, out value))
            {
                return true;
            }

            if (TryGetOwnIndex(index, out var jsValue))
            {
                value = jsValue;
                return true;
            }

            // For holes, continue lookup on the prototype chain.
            return _properties.TryGetProperty(name, receiver, out value);
        }

        return _properties.TryGetProperty(name, receiver, out value);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, JsValue.FromObject(this));
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            SetLength(value.ToObject(), null);
            return;
        }

        if (TryParseArrayIndex(name, out var index))
        {
            SetElement(index, value);
            return;
        }

        _properties.SetProperty(name, value, receiver.IsNull || receiver.IsUndefined ? JsValue.FromObject(this) : receiver);
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        TryDefineProperty(name, descriptor);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        // First check if there's an explicit descriptor in _properties (e.g., for frozen/sealed arrays or custom descriptors)
        var explicitDescriptor = _properties.GetOwnPropertyDescriptor(name);
        if (explicitDescriptor is not null)
        {
            return explicitDescriptor;
        }

        if (TryParseArrayIndex(name, out var index))
        {
            if (index < _length && TryGetOwnIndex(index, out var value))
            {
                return new PropertyDescriptor
                {
                    Value = value, Writable = true, Enumerable = true, Configurable = true
                };
            }
        }

        return null;
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        foreach (var indexKey in EnumerateIndexPropertyNames(includeNonEnumerable: true))
        {
            yield return indexKey;
        }

        foreach (var key in _properties.GetOwnPropertyNames())
        {
            if (TryParseArrayIndex(key, out _))
            {
                continue;
            }

            yield return key;
        }
    }

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        foreach (var indexKey in EnumerateIndexPropertyNames(includeNonEnumerable: false))
        {
            yield return indexKey;
        }

        foreach (var key in _properties.GetEnumerablePropertyNames())
        {
            if (TryParseArrayIndex(key, out _))
            {
                continue;
            }

            yield return key;
        }
    }

    public void Seal()
    {
        _properties.Seal();
    }

    public bool Delete(string name)
    {
        if (TryParseArrayIndex(name, out var index))
        {
            var descriptor = _properties.GetOwnPropertyDescriptor(name);
            if (descriptor is not null && !descriptor.Configurable)
            {
                return false;
            }

            _properties.DeleteOwnProperty(name);
            return DeleteElement((int)index);
        }

        return DeleteProperty(name);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            return DefineLength(descriptor, null, false);
        }

        if (TryParseArrayIndex(name, out var index))
        {
            if (!descriptor.IsAccessorDescriptor)
            {
                SetElement(index, JsValue.FromObject(descriptor.HasValue ? descriptor.Value : Symbol.Undefined));
            }
            else
            {
                BumpLength(index + 1);
            }
        }

        return _properties.TryDefineProperty(name, descriptor);
    }

    public void PushHole()
    {
        _items.Add(ArrayHole);
        _length++;
    }

    public override string ToString()
    {
        // Match the behaviour of Array.prototype.toString / join with
        // the default separator so arrays used as property keys (e.g.
        // reverse[colorName[key]] in Babel's color modules) produce a
        // stable comma-joined string rather than a CLR type name.
        if (_items.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(_items.Count);
        foreach (var item in _items)
        {
            if (IsArrayHole(item) || item.IsNull || item.IsUndefined)
            {
                parts.Add(string.Empty);
                continue;
            }

            var obj = item.ToObject();
            parts.Add(Convert.ToString(obj, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return string.Join(",", parts);
    }

    /// <summary>
    ///     Gets an element at the specified index (alias for GetElement)
    /// </summary>
    public JsValue Get(int index)
    {
        return GetElement(index);
    }

    public JsValue GetElement(int index)
    {
        if (index < 0)
        {
            return JsValue.Undefined;
        }

        return GetElement((uint)index);
    }

    public JsValue GetElement(uint index)
    {
        if (index < _items.Count)
        {
            var item = _items[(int)index];
            // Return undefined for holes in the array
            return IsArrayHole(item) ? JsValue.Undefined : item;
        }

        if (_sparseItems is not null && _sparseItems.TryGetValue(index, out var value))
        {
            return value;
        }

        return JsValue.Undefined;
    }

    /// <summary>
    ///     Returns true if the given index is an own data property on this array
    ///     (i.e. within bounds and not a hole).
    /// </summary>
    public bool HasOwnIndex(uint index)
    {
        if (index < _items.Count)
        {
            return !IsArrayHole(_items[(int)index]);
        }

        return _sparseItems?.ContainsKey(index) == true;
    }

    public bool HasOwnIndex(int index)
    {
        if (index < 0)
        {
            return false;
        }

        return HasOwnIndex((uint)index);
    }

    /// <summary>
    ///     Enumerates own, present indices (dense + sparse) without exposing holes.
    /// </summary>
    public IEnumerable<uint> GetOwnIndices()
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (!IsArrayHole(_items[i]))
            {
                yield return (uint)i;
            }
        }

        if (_sparseItems is not null)
        {
            foreach (var key in _sparseItems.Keys)
            {
                yield return key;
            }
        }
    }

    private IEnumerable<string> EnumerateIndexPropertyNames(bool includeNonEnumerable)
    {
        var indices = new SortedSet<uint>();

        for (var i = 0; i < _items.Count; i++)
        {
            if (!IsArrayHole(_items[i]))
            {
                indices.Add((uint)i);
            }
        }

        if (_sparseItems is not null)
        {
            foreach (var key in _sparseItems.Keys)
            {
                indices.Add(key);
            }
        }

        foreach (var key in _properties.GetOwnPropertyNames())
        {
            if (TryParseArrayIndex(key, out var parsed))
            {
                indices.Add(parsed);
            }
        }

        foreach (var index in indices)
        {
            var propertyName = index.ToString(CultureInfo.InvariantCulture);
            var descriptor = GetOwnPropertyDescriptor(propertyName);
            var enumerable = descriptor is { HasEnumerable: true } ? descriptor.Enumerable : true;

            if (!includeNonEnumerable && !enumerable)
            {
                continue;
            }

            if (descriptor is null && !HasOwnIndex(index))
            {
                continue;
            }

            yield return propertyName;
        }
    }

    private bool TryGetOwnIndex(uint index, out JsValue value)
    {
        if (index < _items.Count)
        {
            var item = _items[(int)index];
            if (!IsArrayHole(item))
            {
                value = item;
                return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        if (_sparseItems is not null && _sparseItems.TryGetValue(index, out value))
        {
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    public void SetElement(int index, JsValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        SetElement((uint)index, value);
    }

    /// <summary>
    /// Convenience overload that wraps object in JsValue.
    /// </summary>
    public void SetElement(int index, object? value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        SetElement((uint)index, JsValue.FromObject(value));
    }

    /// <summary>
    /// Convenience overload that wraps object in JsValue.
    /// </summary>
    public void SetElement(uint index, object? value)
    {
        SetElement(index, JsValue.FromObject(value));
    }

    public void SetElement(uint index, JsValue value)
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
            _sparseItems ??= new Dictionary<uint, JsValue>();
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
    ///     Removes the element at the specified index without affecting the array length.
    ///     JavaScript's delete operator leaves holes behind, which we represent via <see cref="ArrayHole" />.
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

        _sparseItems?.Remove(uintIndex);

        return true;
    }

    /// <summary>
    ///     Deletes a named property from the backing object storage.
    /// </summary>
    public bool DeleteProperty(string name)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            return false;
        }

        return _properties.DeleteOwnProperty(name);
    }

    public void Push(JsValue value)
    {
        _items.Add(value);
        BumpLength((uint)_items.Count);
    }

    /// <summary>
    /// Convenience overload that wraps object in JsValue.
    /// </summary>
    public void Push(object? value)
    {
        _items.Add(JsValue.FromObject(value));
        BumpLength((uint)_items.Count);
    }

    public JsValue Pop()
    {
        if (_length == 0)
        {
            return JsValue.Undefined;
        }

        var lastIndex = _length - 1;
        JsValue value = JsValue.Undefined;

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
        return IsArrayHole(value) ? JsValue.Undefined : value;
    }

    public JsValue Shift()
    {
        if (_length == 0 || _items.Count == 0)
        {
            return JsValue.Undefined;
        }

        var value = _items[0];
        _items.RemoveAt(0);
        SetExplicitLength(_length - 1);

        // Return undefined for holes
        return IsArrayHole(value) ? JsValue.Undefined : value;
    }

    public void Unshift(params JsValue[] values)
    {
        _items.InsertRange(0, values);
        BumpLength((uint)_items.Count);
    }

    public JsArray Splice(int start, int deleteCount, params JsValue[] itemsToInsert)
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
        var deleted = new JsArray(_realmState);
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
            throw CreateRangeError("Invalid array length");
        }

        if (candidateLength > _length)
        {
            _length = candidateLength;
            UpdateLengthProperty();
        }
    }

    internal bool SetLength(object? value, EvaluationContext? context, bool throwOnWritableFailure = true)
    {
        return TrySetArrayLength(true, value, false, true, context,
            throwOnWritableFailure);
    }

    internal bool DefineLength(PropertyDescriptor descriptor, EvaluationContext? context, bool throwOnWritableFailure)
    {
        if (descriptor.IsAccessorDescriptor)
        {
            return FailTypeError(context, throwOnWritableFailure);
        }

        var lengthDescriptor = _properties.GetOwnPropertyDescriptor("length") ??
                               new PropertyDescriptor
                               {
                                   Value = (double)_length,
                                   Writable = true,
                                   Enumerable = false,
                                   Configurable = false
                               };

        // When the descriptor omits [[Value]], perform ordinary validation /
        // attribute updates without touching the numeric length.
        if (!descriptor.HasValue)
        {
            // Length is non-configurable and non-enumerable; reject attempts to
            // mutate those attributes.
            if (descriptor is { HasConfigurable: true, Configurable: true } ||
                (descriptor.HasEnumerable && descriptor.Enumerable != lengthDescriptor.Enumerable))
            {
                return FailTypeError(context, throwOnWritableFailure);
            }

            if (!lengthDescriptor.Writable && descriptor is { HasWritable: true, Writable: true })
            {
                return FailTypeError(context, throwOnWritableFailure);
            }

            if (descriptor.HasWritable)
            {
                lengthDescriptor.Writable = descriptor.Writable;
            }

            return true;
        }

        var success = TrySetArrayLength(descriptor.HasValue, descriptor.Value, descriptor.HasWritable,
            descriptor.Writable, context, throwOnWritableFailure);
        if (!success)
        {
            return false;
        }

        // Descriptor validation happens after numeric coercion to match
        // ArraySetLength ordering: RangeError beats descriptor errors.
        if (descriptor is { HasConfigurable: true, Configurable: true } ||
            (descriptor.HasEnumerable && descriptor.Enumerable != lengthDescriptor.Enumerable))
        {
            return FailTypeError(context, throwOnWritableFailure);
        }

        if (descriptor.HasWritable)
        {
            lengthDescriptor.Writable = descriptor.Writable;
        }

        return true;
    }

    private void SetExplicitLength(uint newLength)
    {
        if (newLength > MaxArrayLength)
        {
            throw CreateRangeError("Invalid array length");
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
        var lengthDescriptor = _properties.GetOwnPropertyDescriptor("length");
        if (lengthDescriptor is null)
        {
            DefineInitialLengthProperty();
            return;
        }

        lengthDescriptor.Value = (double)_length;
        _properties["length"] = (double)_length;
    }

    private void DefineInitialLengthProperty()
    {
        _properties.DefineProperty("length",
            new PropertyDescriptor
            {
                Value = (double)_length, Writable = true, Enumerable = false, Configurable = false
            });
    }

    private void SetupIterator()
    {
        if (_arrayPrototype is not null)
        {
            // Delegate to Array.prototype's @@iterator so all arrays share the same iterator function.
            return;
        }

        // Set up Symbol.iterator
        var iteratorKey = SymbolKeys.Iterator;

        // Create iterator function that returns an iterator object
        var iteratorFunction = new HostFunction((_, _) =>
        {
            // Use array to hold index so it can be mutated in closure
            int[] indexHolder = [0];
            var iterator = new JsObject();

            // Add next() method to iterator
            iterator.SetProperty("next", JsValue.FromObject(new HostFunction((_, _) =>
            {
                var result = new JsObject();
                if (indexHolder[0] < _length)
                {
                    var value = GetElement(indexHolder[0]);
                    result.SetProperty("value", value);
                    result.SetProperty("done", false);
                    indexHolder[0]++;
                }
                else
                {
                    result.SetProperty("value", JsValue.FromObject(Symbol.Undefined));
                    result.SetProperty("done", true);
                }

                return new JsValue(result);
            })));

            return new JsValue(iterator);
        });

        _properties.SetProperty(iteratorKey, JsValue.FromObject(iteratorFunction));
    }

    private static bool TryParseArrayIndex(string propertyName, out uint index)
    {
        index = 0;

        if (string.IsNullOrEmpty(propertyName))
        {
            return false;
        }

        // Fast path for common single-digit indices (0-9)
        if (propertyName.Length == 1)
        {
            var c = propertyName[0];
            if (c is >= '0' and <= '9')
            {
                index = (uint)(c - '0');
                return true;
            }
            return false;
        }

        // Fast path: reject strings starting with '0' followed by other chars (invalid canonical form like "01")
        // Also reject strings starting with non-digit
        var firstChar = propertyName[0];
        if (firstChar == '0' || firstChar is < '1' or > '9')
        {
            return false;
        }

        // Use span-based parsing to avoid allocations
        if (!uint.TryParse(propertyName.AsSpan(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        // 2^32 - 1 is not a valid array index
        if (parsed == uint.MaxValue)
        {
            return false;
        }

        // Validate canonical form without ToString allocation:
        // The canonical string representation of a uint has a specific length based on its value.
        // If the parsed value's expected digit count matches the input length, it's canonical.
        // This works because we already rejected leading zeros above.
        var expectedLength = parsed switch
        {
            < 10 => 1,
            < 100 => 2,
            < 1000 => 3,
            < 10000 => 4,
            < 100000 => 5,
            < 1000000 => 6,
            < 10000000 => 7,
            < 100000000 => 8,
            < 1000000000 => 9,
            _ => 10
        };

        if (propertyName.Length != expectedLength)
        {
            return false;
        }

        index = parsed;
        return true;
    }

    private bool TrySetArrayLength(bool hasValue, object? value, bool hasWritable, bool writableValue,
        EvaluationContext? context, bool throwOnWritableFailure)
    {
        var newLength = _length;
        double numberLen = _length;
        if (hasValue)
        {
            var numberForUint32 = JsOps.ToNumberWithContext(value, context);
            if (context?.IsThrow == true)
            {
                return false;
            }

            var coercedUint = unchecked((uint)(long)numberForUint32);
            numberLen = JsOps.ToNumberWithContext(value, context);
            if (context?.IsThrow == true)
            {
                return false;
            }

            if (coercedUint > MaxArrayLength)
            {
                return FailRangeError(context);
            }

            newLength = coercedUint;
        }

        var lengthDescriptor = _properties.GetOwnPropertyDescriptor("length") ??
                               new PropertyDescriptor
                               {
                                   Value = (double)_length,
                                   Writable = true,
                                   Enumerable = false,
                                   Configurable = false
                               };

        var oldLength = _length;

        if (hasValue)
        {
            if (double.IsNaN(numberLen) || double.IsInfinity(numberLen) || numberLen != newLength)
            {
                return FailRangeError(context);
            }
        }

        if (!lengthDescriptor.Writable)
        {
            if (hasValue || (hasWritable && writableValue))
            {
                return FailTypeError(context, throwOnWritableFailure);
            }

            if (hasWritable && !writableValue)
            {
                lengthDescriptor.Writable = false;
            }

            return false;
        }

        var newWritable = lengthDescriptor.Writable;
        if (hasWritable)
        {
            newWritable = writableValue;
        }

        if (hasValue)
        {
            if (newLength < oldLength)
            {
                SetExplicitLength(newLength);
            }
            else if (newLength > oldLength)
            {
                _length = newLength;
                UpdateLengthProperty();
            }

            lengthDescriptor.Writable = newWritable;
            UpdateLengthProperty();
            return true;
        }

        lengthDescriptor.Writable = newWritable;
        return true;
    }

    private ThrowSignal CreateRangeError(string message, EvaluationContext? context = null)
    {
        var realm = context?.RealmState ?? _realmState;
        var ctor = realm?.RangeErrorConstructor ?? _rangeErrorCtor;
        if (ctor is IJsCallable)
        {
            var errorObj = ctor.Invoke([JsValue.FromObject(message)], JsValue.Undefined);
            return new ThrowSignal(errorObj);
        }

        var fallback = new JsObject { ["name"] = "RangeError", ["message"] = message };

        return new ThrowSignal(JsValue.FromObject(fallback));
    }

    private ThrowSignal CreateTypeError(string message, EvaluationContext? context = null)
    {
        var realm = context?.RealmState ?? _realmState;
        var ctor = realm?.TypeErrorConstructor ?? _typeErrorCtor;
        if (ctor is IJsCallable)
        {
            var errorObj = ctor.Invoke([JsValue.FromObject(message)], JsValue.Undefined);
            return new ThrowSignal(errorObj);
        }

        var fallback = new JsObject { ["name"] = "TypeError", ["message"] = message };

        return new ThrowSignal(JsValue.FromObject(fallback));
    }

    private bool FailRangeError(EvaluationContext? context)
    {
        var signal = CreateRangeError("Invalid array length", context);
        if (context is not null)
        {
            context.SetThrow(signal.ThrownValue);
            return false;
        }

        throw signal;
    }

    private bool FailTypeError(EvaluationContext? context, bool throwOnWritableFailure)
    {
        if (!throwOnWritableFailure)
        {
            return false;
        }

        var signal = CreateTypeError("Invalid array length", context);
        if (context is not null)
        {
            context.SetThrow(signal.ThrownValue);
            return false;
        }

        throw signal;
    }
}
