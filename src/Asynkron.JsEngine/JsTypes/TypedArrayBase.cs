#region

using System.Diagnostics;
using System.Globalization;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Abstract base class for all JavaScript typed arrays.
///     Provides shared logic for property access so the evaluator
///     can treat typed arrays like regular <see cref="IJsObjectLike" /> instances.
/// </summary>
public abstract class TypedArrayBase : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl,
    IPrototypeAccessorProvider, IAsJsValue
{
    protected readonly JsArrayBuffer _buffer;
    protected readonly int _byteOffset;
    protected readonly int _bytesPerElement;
    private readonly HostFunction _includesFunction;
    private readonly HostFunction _indexOfFunction;
    protected readonly int _initialLength;
    protected readonly bool _isLengthTracking;

    private readonly JsObject _properties = new();
    private readonly JsValue _cachedJsValue;
    private readonly HostFunction _setFunction;
    private readonly HostFunction _sliceFunction;
    private readonly HostFunction _subarrayFunction;

    protected TypedArrayBase(
        JsArrayBuffer buffer,
        int byteOffset,
        int length,
        int bytesPerElement,
        bool isLengthTracking = false)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);

        if (byteOffset < 0 || byteOffset > buffer.ByteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        }

        if (byteOffset % bytesPerElement != 0)
        {
            throw new ArgumentException("Byte offset must be aligned to element size", nameof(byteOffset));
        }

        if (length < 0 || byteOffset + length * bytesPerElement > buffer.ByteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        _byteOffset = byteOffset;
        _initialLength = length;
        _bytesPerElement = bytesPerElement;
        _isLengthTracking = isLengthTracking;

        // Provide built-in instance methods that operate on whichever typed array
        // is used as the `this` value at invocation time. This mirrors the behaviour
        // we previously emulated in the evaluator when handling these properties.
        _setFunction = new HostFunction((thisValue, args) =>
        {
            var target = ResolveThis(thisValue, this);

            if (args.Count == 0)
            {
                return JsValue.Undefined;
            }

            var offset = 0;
            if (args.Count > 1 && args[1].IsNumber)
            {
                offset = (int)args[1].AsDouble();
            }

            var firstArg = args[0];
            if (firstArg.IsObject)
            {
                var obj = firstArg.AsObject<IJsObjectLike>();
                switch (obj)
                {
                    case TypedArrayBase sourceTypedArray:
                        target.Set(sourceTypedArray, offset);
                        break;
                    case JsArray sourceArray:
                        target.Set(sourceArray, offset);
                        break;
                }
            }

            return JsValue.Undefined;
        });

        _subarrayFunction = new HostFunction((thisValue, args) =>
        {
            var target = ResolveThis(thisValue, this);
            var begin = 0;
            var end = target.Length;

            if (args.Count > 0 && args[0].IsNumber)
            {
                begin = (int)args[0].AsDouble();
            }

            if (args.Count > 1 && args[1].IsNumber)
            {
                end = (int)args[1].AsDouble();
            }

            return (JsValue)target.Subarray(begin, end);
        });

        _sliceFunction = new HostFunction((thisValue, args) =>
        {
            var target = ResolveThis(thisValue, this);
            var begin = 0;
            var end = target.Length;

            if (args.Count > 0 && args[0].IsNumber)
            {
                begin = (int)args[0].AsDouble();
            }

            if (args.Count > 1 && args[1].IsNumber)
            {
                end = (int)args[1].AsDouble();
            }

            return JsValue.FromObjectUnsafe(CreateSlice(target, begin, end));
        });

        _indexOfFunction = new HostFunction((thisValue, args) => IndexOfInternal(ResolveThis(thisValue, this), args));
        _includesFunction = new HostFunction((thisValue, args) => IncludesInternal(ResolveThis(thisValue, this), args));
    }

    /// <summary>
    ///     Gets the ArrayBuffer referenced by this typed array.
    /// </summary>
    public JsArrayBuffer Buffer => _buffer;

    /// <summary>
    ///     Gets the offset in bytes from the start of the ArrayBuffer.
    /// </summary>
    public int ByteOffset
    {
        get
        {
            if (_buffer.IsDetached || IsDetachedOrOutOfBounds())
            {
                return 0;
            }

            return _byteOffset;
        }
    }

    /// <summary>
    ///     Gets the length in bytes of the typed array.
    /// </summary>
    public int ByteLength
    {
        get
        {
            if (_buffer.IsDetached || IsDetachedOrOutOfBounds())
            {
                return 0;
            }

            if (_isLengthTracking)
            {
                var availableBytes = Math.Max(_buffer.ByteLength - _byteOffset, 0);
                return availableBytes - availableBytes % _bytesPerElement;
            }

            return _initialLength * _bytesPerElement;
        }
    }

    /// <summary>
    ///     Gets the number of elements in the typed array.
    /// </summary>
    public virtual int Length => ComputeLength();

    /// <summary>
    ///     Gets the size in bytes of each element in the array.
    /// </summary>
    public int BytesPerElement => _bytesPerElement;

    /// <summary>
    ///     True when this typed array stores BigInt elements.
    /// </summary>
    public virtual bool IsBigIntArray => false;

    internal bool IsLengthTracking => _isLengthTracking;

    public bool IsExtensible => _properties.IsExtensible;

    public void PreventExtensions()
    {
        _properties.PreventExtensions();
    }

    public JsObject? Prototype => _properties.Prototype;

    public bool IsSealed => _properties.IsSealed;
    public bool IsFrozen => _properties.IsFrozen;

    public IEnumerable<string> Keys => _properties.Keys;

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        // Allow dynamically assigned properties and prototype chain lookups first.
        if (_properties.TryGetProperty(name, receiver.IsUndefined ? _cachedJsValue : receiver, out value))
        {
            return true;
        }

        switch (name)
        {
            case "length":
                value = new JsValue((double)Length);
                return true;
            case "byteLength":
                value = new JsValue((double)ByteLength);
                return true;
            case "byteOffset":
                value = new JsValue((double)ByteOffset);
                return true;
            case "buffer":
                value = JsValue.FromObjectUnsafe(Buffer);
                return true;
            case "BYTES_PER_ELEMENT":
                value = new JsValue((double)BytesPerElement);
                return true;
            case "set":
                value = (JsValue)_setFunction;
                return true;
            case "subarray":
                value = (JsValue)_subarrayFunction;
                return true;
            case "indexOf":
                value = (JsValue)_indexOfFunction;
                return true;
            case "includes":
                value = (JsValue)_includesFunction;
                return true;
        }

        if (TryParseIndex(name, out var index))
        {
            if (IsDetachedOrOutOfBounds())
            {
                value = JsValue.Undefined;
                return false;
            }

            var length = Length;
            if (index >= 0 && index < length)
            {
                if (_buffer.IsDetached)
                {
                    value = JsValue.Undefined;
                    return false;
                }

                value = GetValueForIndex(index);
                return true;
            }
        }

        value = JsValue.Undefined;
        return false;
    }

    [Conditional("DEBUG")]
    internal void AssertInvariants(string usage)
    {
        if (_buffer.IsDetached || IsDetachedOrOutOfBounds())
        {
            return;
        }

        if (_byteOffset < 0 || _byteOffset > _buffer.ByteLength)
        {
            throw new InvalidOperationException(
                $"TypedArray offset out of range ({usage}). offset={_byteOffset} bufferLength={_buffer.ByteLength}");
        }

        if (_byteOffset % _bytesPerElement != 0)
        {
            throw new InvalidOperationException(
                $"TypedArray offset not aligned ({usage}). offset={_byteOffset} bpe={_bytesPerElement}");
        }

        var byteLength = ByteLength;
        var length = Length;
        if (byteLength != length * _bytesPerElement)
        {
            throw new InvalidOperationException(
                $"TypedArray length mismatch ({usage}). length={length} byteLength={byteLength} bpe={_bytesPerElement}");
        }

        if (_byteOffset + byteLength > _buffer.ByteLength)
        {
            throw new InvalidOperationException(
                $"TypedArray range exceeds buffer ({usage}). offset={_byteOffset} byteLength={byteLength} bufferLength={_buffer.ByteLength}");
        }
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        return TryGetProperty(name, _cachedJsValue, out value);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, _cachedJsValue);
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        switch (name)
        {
            case "length":
            case "byteLength":
            case "byteOffset":
            case "BYTES_PER_ELEMENT":
            case "buffer":
                throw new InvalidOperationException($"Cannot assign to read-only property '{name}' on typed arrays.");
        }

        if (TryParseIndex(name, out var index))
        {
            if (IsBigIntArray)
            {
                // Integer-indexed exotic objects always coerce the value, even if the write becomes a no-op.
                var coerced = ToBigInt(value, realmState: _buffer.RealmState);

                // If the view is detached/out-of-bounds or the index is invalid, ignore the write.
                if (IsDetachedOrOutOfBounds() || index < 0 || index >= Length)
                {
                    return;
                }

                SetValue(index, new JsValue(coerced));
                return;
            }

            var context = _buffer.RealmState?.CreateContext();
            if (value.IsBigInt)
            {
                throw StandardLibrary.ThrowTypeError("Cannot convert a BigInt value to a number", context,
                    _buffer.RealmState);
            }

            // Integer-indexed exotic objects always coerce the value, even if the write becomes a no-op.
            var numeric = JsOps.ToNumber(value, context);
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            // If the view is detached/out-of-bounds or the index is invalid, ignore the write.
            if (IsDetachedOrOutOfBounds() || index < 0 || index >= Length)
            {
                return;
            }

            SetElement(index, numeric);
            return;
        }

        // Canonical numeric index strings that are not valid integer indices
        // (e.g. "-0", "-1") are silently ignored per the spec.
        if (IsCanonicalNumericIndexString(name))
        {
            return;
        }

        _properties.SetProperty(name, value, receiver.IsUndefined ? _cachedJsValue : receiver);
    }

    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

    /// <summary>
    ///     Allows consumers (e.g. Object.setPrototypeOf) to attach a prototype object.
    /// </summary>
    public void SetPrototype(IJsPropertyAccessor? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (!TryDefineProperty(name, descriptor))
        {
            throw StandardLibrary.ThrowTypeError("Cannot redefine property on typed array",
                realm: _buffer.RealmState);
        }
    }

    public void Seal()
    {
        _properties.Seal();
    }

    public bool Delete(string name)
    {
        return _properties.DeleteOwnProperty(name);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (TryParseIndex(name, out var index) && index >= 0)
        {
            if (IsDetachedOrOutOfBounds() || index >= Length)
            {
                return null;
            }

            return new PropertyDescriptor
            {
                Value = GetValueForIndex(index),
                Writable = true,
                Enumerable = true,
                Configurable = true
            };
        }

        // If the key is a canonical numeric index string (e.g. "-0", "-1"),
        // it is handled by the integer-indexed path and returns undefined.
        if (IsCanonicalNumericIndexString(name))
        {
            return null;
        }

        return _properties.GetOwnPropertyDescriptor(name);
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        if (IsDetachedOrOutOfBounds())
        {
            return _properties.GetOwnPropertyNames();
        }

        var keys = new List<string>();
        var length = Length;
        for (var i = 0; i < length; i++)
        {
            keys.Add(JsValueCache.GetIndexString(i));
        }

        keys.AddRange(_properties.GetOwnPropertyNames());
        return keys;
    }

    public IEnumerable<string> GetOwnPropertyKeysInOrder(bool includeSymbols = true, bool includeNonEnumerable = true)
    {
        if (_buffer.IsDetached)
        {
            return _properties.GetOwnPropertyKeysInOrder(includeSymbols, includeNonEnumerable);
        }

        var keys = new List<string>();
        var length = ComputeLength();
        for (var i = 0; i < length; i++)
        {
            keys.Add(JsValueCache.GetIndexString(i));
        }

        keys.AddRange(_properties.GetOwnPropertyKeysInOrder(includeSymbols, includeNonEnumerable));
        return keys;
    }

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        if (_buffer.IsDetached)
        {
            return _properties.GetEnumerablePropertyNames();
        }

        var keys = new List<string>();
        var length = ComputeLength();
        for (var i = 0; i < length; i++)
        {
            keys.Add(JsValueCache.GetIndexString(i));
        }

        keys.AddRange(_properties.GetEnumerablePropertyNames());
        return keys;
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (TryParseIndex(name, out var index))
        {
            if (index < 0)
            {
                return false;
            }

            // Integer-indexed exotic objects reject accessor descriptors and
            // attribute changes that conflict with the fixed attributes
            // {writable: true, enumerable: true, configurable: true}.
            if (descriptor.IsAccessorDescriptor ||
                descriptor is { HasWritable: true, Writable: false } ||
                descriptor is { HasEnumerable: true, Enumerable: false } ||
                descriptor is { HasConfigurable: true, Configurable: false })
            {
                return false;
            }

            if (_buffer.IsDetached || IsDetachedOrOutOfBounds() || index >= Length)
            {
                throw CreateOutOfBoundsTypeError();
            }

            var value = descriptor.HasValue ? descriptor.JsValue : JsValue.Undefined;
            SetValue(index, value);
            return true;
        }

        // Canonical numeric index strings that are not valid integer indices
        // (e.g. "-0", "-1") always return false (define fails silently).
        if (IsCanonicalNumericIndexString(name))
        {
            return false;
        }

        return _properties.TryDefineProperty(name, descriptor);
    }

    public IJsPropertyAccessor? PrototypeAccessor =>
        _properties is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

    private int ComputeLength()
    {
        if (_buffer.IsDetached)
        {
            return 0;
        }

        int length;
        if (_isLengthTracking)
        {
            var availableBytes = Math.Max(_buffer.ByteLength - _byteOffset, 0);
            length = availableBytes / _bytesPerElement;
        }
        else
        {
            // Fixed-length view on resizable buffer stays at initial length when in-bounds.
            var requiredBytes = _byteOffset + _initialLength * _bytesPerElement;
            length = _buffer.ByteLength >= requiredBytes ? _initialLength : 0;
        }

        _buffer.RealmState?.Logger?.LogInformation(
            "TypedArray.ComputeLength tracking={Tracking} initial={Initial} byteLength={ByteLength} offset={Offset} bpe={Bpe} result={Result}",
            _isLengthTracking,
            _initialLength,
            _buffer.ByteLength,
            _byteOffset,
            _bytesPerElement,
            length);

        return length;
    }

    private static double ToIntegerOrInfinity(JsValue value, EvaluationContext? context)
    {
        var number = JsOps.ToNumber(value, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (double.IsNaN(number))
        {
            return 0;
        }

        if (double.IsInfinity(number) || number == 0)
        {
            return number;
        }

        return Math.Sign(number) * Math.Floor(Math.Abs(number));
    }

    internal static JsValue IndexOfInternal(TypedArrayBase target, IReadOnlyList<JsValue> args)
    {
        if (target.IsDetachedOrOutOfBounds())
        {
            throw target.CreateOutOfBoundsTypeError();
        }

        var evalContext = target._buffer.RealmState?.CreateContext();
        var searchElement = args.GetArgument(0);
        // Snapshot the length before coercion, as required by the spec.
        var initialLength = target.Length;
        if (initialLength <= 0)
        {
            return new JsValue(-1d);
        }

        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], evalContext) : 0d;

        if (target.IsDetachedOrOutOfBounds())
        {
            return new JsValue(-1d);
        }

        var currentLength = target.Length;
        // Length-tracking views use the pre-coercion length; fixed views clamp to current.
        var len = target._isLengthTracking ? initialLength : Math.Min(initialLength, currentLength);
        if (len <= 0)
        {
            return new JsValue(-1d);
        }

        double startIndexNumber;
        if (double.IsPositiveInfinity(fromIndex))
        {
            return new JsValue(-1d);
        }

        if (double.IsNegativeInfinity(fromIndex))
        {
            startIndexNumber = 0;
        }
        else if (fromIndex < 0)
        {
            startIndexNumber = Math.Max(len + Math.Ceiling(fromIndex), 0);
        }
        else
        {
            startIndexNumber = Math.Min(fromIndex, len);
        }

        var start = (int)startIndexNumber;
        for (var i = start; i < len; i++)
        {
            if (target.IsDetachedOrOutOfBounds())
            {
                return new JsValue(-1d);
            }

            if (i >= target.Length)
            {
                continue;
            }

            if (TryFindElementIndex(target, i, searchElement, out var indexResult))
            {
                return indexResult;
            }
        }

        return new JsValue(-1d);
    }

    internal static JsValue IncludesInternal(TypedArrayBase target, IReadOnlyList<JsValue> args)
    {
        var evalContext = target._buffer.RealmState?.CreateContext();
        var searchElement = args.GetArgument(0);
        var initialLength = target.Length;
        if (initialLength <= 0)
        {
            return JsValue.False;
        }

        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], evalContext) : 0d;

        var len = initialLength;
        if (double.IsPositiveInfinity(fromIndex))
        {
            return JsValue.False;
        }

        double startIndexNumber;
        if (double.IsNegativeInfinity(fromIndex))
        {
            startIndexNumber = 0;
        }
        else if (fromIndex < 0)
        {
            startIndexNumber = Math.Max(len + Math.Ceiling(fromIndex), 0);
        }
        else
        {
            startIndexNumber = Math.Min(fromIndex, len);
        }

        var start = (int)startIndexNumber;
        for (var i = start; i < len; i++)
        {
            var key = ToIndexString(i);
            var _ = JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(target), key, out var element, evalContext);
            if (evalContext?.IsThrow == true)
            {
                throw new ThrowSignal(evalContext.FlowValue);
            }

            if (SameValueZero(element, searchElement))
            {
                return JsValue.True;
            }
        }

        return JsValue.False;
    }

    internal static JsValue LastIndexOfInternal(TypedArrayBase target, IReadOnlyList<JsValue> args)
    {
        if (target.IsDetachedOrOutOfBounds())
        {
            throw target.CreateOutOfBoundsTypeError();
        }

        var evalContext = target._buffer.RealmState?.CreateContext();
        var searchElement = args.GetArgument(0);
        var initialLength = target.Length;
        if (initialLength <= 0)
        {
            return new JsValue(-1d);
        }

        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], evalContext) : initialLength - 1;

        if (target.IsDetachedOrOutOfBounds())
        {
            return new JsValue(-1d);
        }

        var currentLength = target.Length;
        var len = target._isLengthTracking ? initialLength : Math.Min(initialLength, currentLength);
        if (len <= 0)
        {
            return new JsValue(-1d);
        }

        double startIndexNumber;
        if (double.IsPositiveInfinity(fromIndex))
        {
            startIndexNumber = len - 1;
        }
        else if (double.IsNegativeInfinity(fromIndex))
        {
            return new JsValue(-1d);
        }
        else if (fromIndex >= 0)
        {
            startIndexNumber = Math.Min(fromIndex, len - 1);
        }
        else
        {
            startIndexNumber = len + fromIndex;
            if (startIndexNumber < 0)
            {
                return new JsValue(-1d);
            }
        }

        var startIndex = (int)startIndexNumber;

        for (var i = startIndex; i >= 0; i--)
        {
            if (target.IsDetachedOrOutOfBounds())
            {
                return new JsValue(-1d);
            }

            var loopLength = target.Length;
            if (i >= loopLength)
            {
                continue;
            }

            if (TryFindElementIndex(target, i, searchElement, out var indexResult))
            {
                return indexResult;
            }
        }

        return new JsValue(-1d);
    }

    /// <summary>
    /// Compares an element at the given index with the search element using strict equality.
    /// Returns true if found, with the index as a JsValue in indexResult.
    /// </summary>
    private static bool TryFindElementIndex(TypedArrayBase target, int index, JsValue searchElement, out JsValue indexResult)
    {
        var element = target switch
        {
            JsBigInt64Array bi64 => new JsValue(bi64.GetBigIntElement(index)),
            JsBigUint64Array bu64 => new JsValue(bu64.GetBigIntElement(index)),
            _ => new JsValue(target.GetElement(index))
        };

        if (JsOps.StrictEquals(element, searchElement))
        {
            indexResult = new JsValue((double)index);
            return true;
        }

        indexResult = default;
        return false;
    }

    /// <summary>
    ///     Sets an element using the appropriate coercion for numeric typed arrays.
    ///     BigInt arrays override to enforce BigInt conversion.
    /// </summary>
    public virtual void SetValue(int index, JsValue value)
    {
        var context = _buffer.RealmState?.CreateContext();
        if (value.IsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot convert a BigInt value to a number", context,
                _buffer.RealmState);
        }

        var numeric = JsOps.ToNumber(value, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        SetElement(index, numeric);
    }

    internal virtual JsValue GetValueForIndex(int index)
    {
        if (index < 0)
        {
            return JsValue.Undefined;
        }

        if (_buffer.IsDetached)
        {
            return JsValue.Undefined;
        }

        if (IsDetachedOrOutOfBounds())
        {
            return JsValue.Undefined;
        }

        var currentLength = ComputeLength();
        if (index >= currentLength)
        {
            return JsValue.Undefined;
        }

        return new JsValue(GetElement(index));
    }

    /// <summary>
    ///     Checks if the given index is valid for this typed array.
    /// </summary>
    protected void CheckBounds(int index)
    {
        var length = Length;
        if (index < 0 || index >= length)
        {
            throw CreateOutOfBoundsTypeError();
        }
    }

    /// <summary>
    ///     Gets the byte offset for a given element index.
    /// </summary>
    protected int GetByteIndex(int index)
    {
        return _byteOffset + index * _bytesPerElement;
    }

    /// <summary>
    ///     Creates a new typed array that is a view on the same buffer, from begin (inclusive) to end (exclusive).
    /// </summary>
    public abstract TypedArrayBase Subarray(int begin, int end);

    protected abstract TypedArrayBase CreateNewSameType(int length);

    internal TypedArrayBase CreateSpeciesDefault(int length)
    {
        var result = CreateNewSameType(length);
        // Copy prototype from source to result for spec compliance
        if (Prototype is not null)
        {
            result.SetPrototype(Prototype);
        }
        return result;
    }

    public TypedArrayBase Slice(int begin, int end)
    {
        var (start, finalEnd) = NormalizeSliceIndices(begin, end);
        var newLength = Math.Max(finalEnd - start, 0);
        var newArray = CreateNewSameType(newLength);
        for (var i = 0; i < newLength; i++)
        {
            newArray.SetValue(i, GetValueForIndex(start + i));
        }

        return newArray;
    }

    /// <summary>
    ///     Gets the element at the specified index as a double (for JavaScript compatibility).
    /// </summary>
    public abstract double GetElement(int index);

    /// <summary>
    ///     Sets the element at the specified index from a double (for JavaScript compatibility).
    /// </summary>
    public abstract void SetElement(int index, double value);

    /// <summary>
    ///     Copies elements from source array into this array.
    /// </summary>
    public void Set(TypedArrayBase source, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(source);

        var targetLength = Length;
        if (offset < 0 || offset + source.Length > targetLength)
        {
            throw CreateOutOfBoundsTypeError();
        }

        for (var i = 0; i < source.Length; i++)
        {
            SetValue(offset + i, source.GetValueForIndex(i));
        }
    }

    /// <summary>
    ///     Copies elements from a regular array into this typed array.
    /// </summary>
    public void Set(JsArray source, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(source);

        var targetLength = Length;
        var sourceLength = (int)source.Length;
        if (offset < 0 || offset + sourceLength > targetLength)
        {
            throw CreateOutOfBoundsTypeError();
        }

        for (var i = 0; i < sourceLength; i++)
        {
            var value = source.GetElement(i);
            SetValue(offset + i, value);
        }
    }

    /// <summary>
    ///     Helper method to normalize slice indices.
    /// </summary>
    protected (int start, int end) NormalizeSliceIndices(int begin, int end)
    {
        var len = Length;
        var relativeStart = begin < 0 ? Math.Max(len + begin, 0) : Math.Min(begin, len);
        var relativeEnd = end < 0 ? Math.Max(len + end, 0) : Math.Min(end, len);
        return (relativeStart, relativeEnd);
    }

    /// <summary>
    ///     Deletes a dynamically assigned property. Built-in properties are non-configurable.
    /// </summary>
    public bool DeleteProperty(string name)
    {
        switch (name)
        {
            case "length":
            case "byteLength":
            case "byteOffset":
            case "BYTES_PER_ELEMENT":
            case "buffer":
            case "set":
            case "subarray":
            case "slice":
                return false;
        }

        return _properties.Remove(name);
    }

    /// <summary>
    ///     Implements CanonicalNumericIndexString to determine if a string is the
    ///     canonical representation of a numeric value. Returns true for strings like
    ///     "0", "1", "-0", "-1", "NaN", "Infinity", etc. when ToString(ToNumber(s)) === s.
    ///     This is used to distinguish integer-indexed access from ordinary property access
    ///     on typed arrays.
    /// </summary>
    private static bool IsCanonicalNumericIndexString(string candidate)
    {
        // Fast path for "-0"
        if (candidate == "-0")
        {
            return true;
        }

        // Try to parse as a number. If it fails, it's not a canonical numeric index.
        if (!double.TryParse(candidate, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        // ToString(number) must equal the original candidate.
        return NumberHelper.NumberToString(number, 10) == candidate;
    }

    /// <summary>
    ///     Tries to parse a string as a valid non-negative integer index for
    ///     integer-indexed exotic objects. Returns true only when the candidate
    ///     is a canonical string representation of a non-negative integer
    ///     (e.g. "0", "1", "42"), not "-0", "+1", "1.0", etc.
    /// </summary>
    private static bool TryParseIndex(string candidate, out int index)
    {
        index = 0;

        // "-0" is a canonical numeric index string but maps to -0, which
        // is never a valid integer index.
        if (candidate == "-0")
        {
            return false;
        }

        // Use NumberStyles.None to reject leading signs (+/-), whitespace, etc.
        if (!int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out index))
        {
            return false;
        }

        // Verify canonical form: ToString(parsed) must equal the original string.
        // This rejects non-canonical representations like "+1", "01", etc.
        return index.ToString(CultureInfo.InvariantCulture) == candidate;
    }

    private static TypedArrayBase ResolveThis(JsValue thisValue, TypedArrayBase fallback)
    {
        if (thisValue.IsObject && thisValue.AsObject<IJsObjectLike>() is TypedArrayBase typedArray)
        {
            return typedArray;
        }

        return fallback;
    }

    private static object CreateSlice(TypedArrayBase typedArray, int begin, int end)
    {
        return typedArray.Slice(begin, end);
    }

    internal bool IsDetachedOrOutOfBounds()
    {
        if (_buffer.IsDetached)
        {
            return true;
        }

        if (_isLengthTracking)
        {
            return _byteOffset > _buffer.ByteLength;
        }

        // Fixed-length view is out-of-bounds only when required bytes exceed current buffer length.
        return _byteOffset + _initialLength * _bytesPerElement > _buffer.ByteLength;
    }

    private static bool SameValueZero(JsValue left, JsValue right)
    {
        if (left.IsNumber && double.IsNaN(left.AsDouble()) && right.IsNumber && double.IsNaN(right.AsDouble()))
        {
            return true;
        }

        return JsOps.StrictEquals(left, right);
    }

    internal ThrowSignal CreateOutOfBoundsTypeError()
    {
        if (_buffer.RealmState?.TypeErrorConstructor is IJsCallable ctor)
        {
            var obj = ctor.Invoke(new SingleValueArgs(new JsValue("Out of bounds access on TypedArray")), JsValue.Undefined);
            if (!obj.IsUndefined)
            {
                return new ThrowSignal(obj);
            }
        }

        var fallback = new JsObject { ["name"] = "TypeError", ["message"] = "Out of bounds access on TypedArray" };

        return new ThrowSignal(new JsValue(fallback));
    }
}
