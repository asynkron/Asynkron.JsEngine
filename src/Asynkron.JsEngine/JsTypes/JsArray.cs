using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

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
    private readonly IJsCallable? _rangeErrorCtor;
    private readonly IJsCallable? _typeErrorCtor;
    private readonly JsObject? _arrayPrototype;

    public JsArray()
    {
        _rangeErrorCtor = StandardLibrary.RangeErrorConstructor;
        _typeErrorCtor = StandardLibrary.TypeErrorConstructor;
        _arrayPrototype = StandardLibrary.ArrayPrototype;
        _length = 0;
        if (StandardLibrary.ArrayPrototype is not null)
        {
            _properties.SetPrototype(StandardLibrary.ArrayPrototype);
        }
        DefineInitialLengthProperty();
        SetupIterator();
    }

    public JsArray(IEnumerable<object?> items)
    {
        _rangeErrorCtor = StandardLibrary.RangeErrorConstructor;
        _typeErrorCtor = StandardLibrary.TypeErrorConstructor;
        _arrayPrototype = StandardLibrary.ArrayPrototype;
        if (items is not null)
        {
            _items.AddRange(items);
        }

        _length = (uint)_items.Count;
        if (StandardLibrary.ArrayPrototype is not null)
        {
            _properties.SetPrototype(StandardLibrary.ArrayPrototype);
        }
        DefineInitialLengthProperty();
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

    public JsObject? Prototype
    {
        get
        {
            if (_properties.Prototype is null && (_arrayPrototype is not null || StandardLibrary.ArrayPrototype is not null))
            {
                _properties.SetPrototype(_arrayPrototype ?? StandardLibrary.ArrayPrototype);
            }

            return _properties.Prototype;
        }
    }
    public bool IsSealed => _properties.IsSealed;
    public IEnumerable<string> Keys => _properties.Keys;

    public bool TryGetProperty(string name, out object? value)
    {
        if (_properties.Prototype is null && StandardLibrary.ArrayPrototype is not null)
        {
            _properties.SetPrototype(StandardLibrary.ArrayPrototype);
        }

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
            SetLength(value, null);
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

    /// <summary>
    /// Enumerates own, present indices (dense + sparse) without exposing holes.
    /// </summary>
    public IEnumerable<uint> GetOwnIndices()
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (!ReferenceEquals(_items[i], ArrayHole))
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
        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            DefineLength(descriptor, null, throwOnWritableFailure: true);
            return;
        }

        if (TryParseArrayIndex(name, out var index) && !descriptor.IsAccessorDescriptor)
        {
            // Keep the indexed storage in sync with defined data properties.
            SetElement(index, descriptor.Value);
        }

        _properties.DefineProperty(name, descriptor);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (TryParseArrayIndex(name, out var index))
        {
            if (index < _length && TryGetOwnIndex(index, out var value))
            {
                return new PropertyDescriptor
                {
                    Value = value,
                    Writable = true,
                    Enumerable = true,
                    Configurable = true
                };
            }
        }

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
        return TrySetArrayLength(hasValue: true, value, hasWritable: false, writableValue: true, context,
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
            if ((descriptor.HasConfigurable && descriptor.Configurable) ||
                (descriptor.HasEnumerable && descriptor.Enumerable != lengthDescriptor.Enumerable))
            {
                return FailTypeError(context, throwOnWritableFailure);
            }

            if (!lengthDescriptor.Writable && descriptor.HasWritable && descriptor.Writable)
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
        if ((descriptor.HasConfigurable && descriptor.Configurable) ||
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
        _properties.DefineProperty("length", new PropertyDescriptor
        {
            Value = (double)_length,
            Writable = true,
            Enumerable = false,
            Configurable = false
        });
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
                if (indexHolder[0] < _length)
                {
                    var value = GetElement(indexHolder[0]);
                    result.SetProperty("value", value);
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

    private bool TrySetArrayLength(bool hasValue, object? value, bool hasWritable, bool writableValue,
        EvaluationContext? context, bool throwOnWritableFailure)
    {
        uint newLength = _length;
        double numberLen = _length;
        if (hasValue)
        {
            var numberForUint32 = JsOps.ToNumberWithContext(value, context);
            if (context is not null && context.IsThrow)
            {
                return false;
            }

            var coercedUint = unchecked((uint)(long)numberForUint32);
            numberLen = JsOps.ToNumberWithContext(value, context);
            if (context is not null && context.IsThrow)
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

    private ThrowSignal CreateRangeError(string message)
    {
        var ctor = StandardLibrary.RangeErrorConstructor ?? _rangeErrorCtor;
        if (ctor is IJsCallable callable)
        {
            var errorObj = callable.Invoke([message], null);
            return new ThrowSignal(errorObj);
        }

        var fallback = new JsObject
        {
            ["name"] = "RangeError",
            ["message"] = message
        };

        return new ThrowSignal(fallback);
    }

    private ThrowSignal CreateTypeError(string message)
    {
        var ctor = StandardLibrary.TypeErrorConstructor ?? _typeErrorCtor;
        if (ctor is IJsCallable callable)
        {
            var errorObj = callable.Invoke([message], null);
            return new ThrowSignal(errorObj);
        }

        var fallback = new JsObject
        {
            ["name"] = "TypeError",
            ["message"] = message
        };

        return new ThrowSignal(fallback);
    }

    private bool FailRangeError(EvaluationContext? context)
    {
        var signal = CreateRangeError("Invalid array length");
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

        var signal = CreateTypeError("Invalid array length");
        if (context is not null)
        {
            context.SetThrow(signal.ThrownValue);
            return false;
        }

        throw signal;
    }
}
