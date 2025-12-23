#region

using System.Collections;
using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Minimal JavaScript-like array that tracks indexed elements and behaves like an object for property access.
/// </summary>
public sealed class JsArray : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl, IPrototypeAccessorProvider,
    IEnumerable<JsValue>, IAsJsValue
{
    private const uint DenseIndexLimit = 1_000_000;

    private const uint MaxArrayLength = uint.MaxValue;

    // Sentinel value to represent holes in sparse arrays (indices that have never been set)
    // We use a special JsValue kind or a unique object that we can identify
    private static readonly object ArrayHoleSentinel = new();
    private static readonly JsValue ArrayHole = new(JsValueKind.Object, 0.0, ArrayHoleSentinel);
    private readonly IJsObjectLike? _arrayPrototype;
    private readonly List<JsValue> _items = [];

    private readonly JsObject _properties = new();
    private readonly IJsCallable? _rangeErrorCtor;
    private readonly IJsCallable? _typeErrorCtor;
    private uint _length;
    private Dictionary<uint, JsValue>? _sparseItems;

    // Cached JsValue to avoid repeated struct creation
    private readonly JsValue _cachedJsValue;

    public JsArray(RealmState? realmState = null)
    {
        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
        RealmState = realmState;
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
            RealmState?.Logger?.LogWarning("JsArray constructed without ArrayPrototype");
        }

        DefineInitialLengthProperty();
        SetupIterator();
    }

    /// <inheritdoc />
    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

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
        // Handle case where items contain already-boxed JsValue values
        _items.AddRange(items.Select(static item => item is JsValue js ? js : JsValue.FromObjectUnsafe(item)));
        _length = (uint)_items.Count;
    }

    /// <summary>
    /// Gets the RealmState associated with this array.
    /// </summary>
    public RealmState? RealmState { get; }

    public IReadOnlyList<JsValue> Items => _items;

    /// <summary>
    ///     Gets the length of the array
    /// </summary>
    public double Length => _length;

    /// <summary>
    /// Returns true if this array has custom property descriptors (getters/setters) on numeric indices.
    /// Arrays with custom descriptors cannot use the fast enumeration path because GetElement
    /// bypasses the property descriptor mechanism.
    /// </summary>
    public bool HasCustomIndexedProperties =>
        // Custom descriptors on indices (e.g., Object.defineProperty(arr, '0', {get: ...}))
        // are stored in JsObject's _descriptors dictionary, not in _storage.Keys
        _properties.HasNumericDescriptorKeys();

    /// <summary>
    /// Returns an enumerator that iterates through the array elements.
    /// This provides a fast path for for-of loops, avoiding iterator result object allocations.
    /// Per ES spec, length is checked on each iteration to handle array modifications during iteration.
    ///
    /// WARNING: Only use this if HasCustomIndexedProperties is false. GetElement bypasses
    /// property descriptors and won't invoke custom getters.
    /// </summary>
    public IEnumerator<JsValue> GetEnumerator()
    {
        // Check _length on each iteration - array may be modified during iteration
        // (e.g., array-contract.js, array-expand.js Test262 tests)
        for (uint i = 0; i < _length; i++)
        {
            yield return GetElement(i);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public bool IsExtensible => _properties.IsExtensible;

    public void PreventExtensions()
    {
        _properties.PreventExtensions();
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
    public IEnumerable<string> Keys => _properties.Keys;

    public bool TryGetProperty(string name, out JsValue value)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            value = (double)_length;
            return true;
        }

        if (!TryParseArrayIndex(name, out var index))
        {
            return _properties.TryGetProperty(name, JsValue.FromObjectUnsafe(this), out value);
        }

        // Accessor/data descriptors defined via Object.defineProperty on an
        // index should override the internal dense/sparse storage.
        if (_properties.GetOwnPropertyDescriptor(name) is not null &&
            _properties.TryGetProperty(name, JsValue.FromObjectUnsafe(this), out value))
        {
            return true;
        }

        if (!TryGetOwnIndex(index, out var jsValue))
        {
            return _properties.TryGetProperty(name, JsValue.FromObjectUnsafe(this), out value);
        }

        value = jsValue;
        return true;
    }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            value = (double)_length;
            return true;
        }

        if (!TryParseArrayIndex(name, out var index))
        {
            return _properties.TryGetProperty(name, receiver, out value);
        }

        // Accessor/data descriptors defined via Object.defineProperty on an
        // index should override the internal dense/sparse storage.
        if (_properties.GetOwnPropertyDescriptor(name) is not null &&
            _properties.TryGetProperty(name, receiver, out value))
        {
            return true;
        }

        if (!TryGetOwnIndex(index, out var jsValue))
        {
            return _properties.TryGetProperty(name, receiver, out value);
        }

        value = jsValue;
        return true;
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, JsValue.FromObjectUnsafe(this));
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            // Pass JsValue directly - ToNumericAsJsValue handles boxed JsValue efficiently
            SetLength(value, null);
            return;
        }

        if (TryParseArrayIndex(name, out var index))
        {
            SetElement(index, value);
            return;
        }

        _properties.SetProperty(name, value,
            receiver.IsNull || receiver.IsUndefined ? JsValue.FromObjectUnsafe(this) : receiver);
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

        if (!TryParseArrayIndex(name, out var index))
        {
            return null;
        }

        if (index < _length && TryGetOwnIndex(index, out var value))
        {
            return new PropertyDescriptor { Value = value, Writable = true, Enumerable = true, Configurable = true };
        }

        return null;
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        foreach (var indexKey in EnumerateIndexPropertyNames(true))
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
        foreach (var indexKey in EnumerateIndexPropertyNames(false))
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
        if (!TryParseArrayIndex(name, out var index))
        {
            return DeleteProperty(name);
        }

        var descriptor = _properties.GetOwnPropertyDescriptor(name);
        if (descriptor?.Configurable == false)
        {
            return false;
        }

        _properties.DeleteOwnProperty(name);
        return DeleteElement((int)index);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            return DefineLength(descriptor, null, false);
        }

        if (!TryParseArrayIndex(name, out var index))
        {
            return _properties.TryDefineProperty(name, descriptor);
        }

        if (!descriptor.IsAccessorDescriptor)
        {
            var jsVal = descriptor.HasValue ? descriptor.JsValue : JsValue.Undefined;
            SetElement(index, jsVal);
        }
        else
        {
            BumpLength(index + 1);
        }

        return _properties.TryDefineProperty(name, descriptor);
    }

    public IJsPropertyAccessor? PrototypeAccessor =>
        _properties is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

    private static bool IsArrayHole(JsValue value)
    {
        return value.Kind == JsValueKind.Object && ReferenceEquals(value.ObjectValue, ArrayHoleSentinel);
    }

    public void Freeze()
    {
        _properties.Freeze();
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

            // Convert to string based on JsValue kind to avoid boxing
            var str = item.Kind switch
            {
                JsValueKind.Boolean => item.NumberValue != 0 ? "true" : "false",
                JsValueKind.Number => JsOps.ToCanonicalNumberString(item.NumberValue),
                JsValueKind.String => item.ObjectValue as string ?? string.Empty,
                JsValueKind.BigInt => item.ObjectValue is JsBigInt bi
                    ? bi.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty,
                _ => Convert.ToString(item.ObjectValue, CultureInfo.InvariantCulture) ?? string.Empty
            };
            parts.Add(str);
        }

        return string.Join(',', parts);
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
        return index < 0 ? JsValue.Undefined : GetElement((uint)index);
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

        if (_sparseItems is null)
        {
            yield break;
        }

        foreach (var key in _sparseItems.Keys)
        {
            yield return key;
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
            var enumerable = descriptor is not { HasEnumerable: true } || descriptor.Enumerable;

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

        SetElement((uint)index, JsValue.FromObjectUnsafe(value));
    }

    /// <summary>
    /// Convenience overload that wraps object in JsValue.
    /// </summary>
    public void SetElement(uint index, object? value)
    {
        SetElement(index, JsValue.FromObjectUnsafe(value));
    }

    private void SetElement(uint index, JsValue value)
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
        // Handle case where value is already a boxed JsValue
        var jsVal = value is JsValue jv ? jv : JsValue.FromObjectUnsafe(value);
        _items.Add(jsVal);
        BumpLength((uint)_items.Count);
    }

    public JsValue Pop()
    {
        if (_length == 0)
        {
            return JsValue.Undefined;
        }

        var lastIndex = _length - 1;
        var value = JsValue.Undefined;

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
        start = start < 0 ? Math.Max(0, _items.Count + start) : Math.Min(start, _items.Count);

        // Normalize delete count
        deleteCount = Math.Max(0, Math.Min(deleteCount, _items.Count - start));

        // Create array of deleted items
        var deleted = new JsArray(RealmState);
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

        if (candidateLength <= _length)
        {
            return;
        }

        _length = candidateLength;
        UpdateLengthProperty();
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
            // Collect keys to remove without closure allocation
            List<uint>? keysToRemove = null;
            foreach (var key in _sparseItems.Keys)
            {
                if (key >= newLength)
                {
                    keysToRemove ??= [];
                    keysToRemove.Add(key);
                }
            }

            if (keysToRemove is not null)
            {
                foreach (var key in keysToRemove)
                {
                    _sparseItems.Remove(key);
                }
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
            iterator.SetProperty("next", (JsValue)new HostFunction((_, _) =>
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
                    result.SetProperty("value", JsValue.Undefined);
                    result.SetProperty("done", true);
                }

                return new JsValue(result);
            }));

            return new JsValue(iterator);
        });

        _properties.SetProperty(iteratorKey, (JsValue)iteratorFunction);
    }

    /// <summary>
    /// Creates a values iterator directly, bypassing Symbol.iterator lookup.
    /// This is an optimization for iteration - the iterator created is equivalent
    /// to calling array.values() / array[Symbol.iterator]().
    /// </summary>
    internal IJsObjectLike GetValuesIterator()
    {
        return (IJsObjectLike)StdLib.StandardLibrary.CreateArrayIteratorObject(
            this,
            idx => GetElement(idx),
            RealmState);
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
#pragma warning disable CS0618 // Method takes object? parameter
            var numberForUint32 = JsOps.ToNumberWithContext(JsValue.FromObjectUnsafe(value), context);
            if (context?.IsThrow == true)
            {
                return false;
            }

            var coercedUint = unchecked((uint)(long)numberForUint32);
            numberLen = JsOps.ToNumberWithContext(JsValue.FromObjectUnsafe(value), context);
#pragma warning restore CS0618
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
        var realm = context?.RealmState ?? RealmState;
        var ctor = realm?.RangeErrorConstructor ?? _rangeErrorCtor;
        if (ctor is not null)
        {
            var errorObj = ctor.Invoke([(JsValue)message], JsValue.Undefined);
            return new ThrowSignal(errorObj);
        }

        var fallback = new JsObject { ["name"] = "RangeError", ["message"] = message };

        return new ThrowSignal((JsValue)fallback);
    }

    private ThrowSignal CreateTypeError(string message, EvaluationContext? context = null)
    {
        var realm = context?.RealmState ?? RealmState;
        var ctor = realm?.TypeErrorConstructor ?? _typeErrorCtor;
        if (ctor is not null)
        {
            var errorObj = ctor.Invoke([(JsValue)message], JsValue.Undefined);
            return new ThrowSignal(errorObj);
        }

        var fallback = new JsObject { ["name"] = "TypeError", ["message"] = message };

        return new ThrowSignal((JsValue)fallback);
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
