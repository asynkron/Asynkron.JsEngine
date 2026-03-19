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

    /// <summary>
    ///     The [[TypedArrayName]] internal slot value (e.g. "Int8Array", "Float64Array").
    /// </summary>
    public abstract string TypedArrayName { get; }

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
        // Integer-indexed exotic object [[Get]] (P, Receiver)
        // 1. If Type(P) is String, let numericIndex be CanonicalNumericIndexString(P).
        // 2. If numericIndex is not undefined, return IntegerIndexedElementGet(O, numericIndex).
        // 3. Return OrdinaryGet(O, P, Receiver).
        if (TryCanonicalNumericIndex(name, out var numericIndex))
        {
            // This is a canonical numeric index -- do NOT consult the prototype chain.
            if (!IsValidIntegerIndex(numericIndex))
            {
                value = JsValue.Undefined;
                return false;
            }

            value = GetValueForIndex((int)numericIndex);
            return true;
        }

        // OrdinaryGet path -- check own properties and prototype chain.
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

        // Integer-indexed exotic object [[Set]] (P, V, Receiver)
        // If the key is a canonical numeric index string, handle via IntegerIndexedElementSet.
        if (TryCanonicalNumericIndex(name, out var numericIndex))
        {
            if (IsBigIntArray)
            {
                // Integer-indexed exotic objects always coerce the value, even if the write becomes a no-op.
                var coerced = ToBigInt(value, realmState: _buffer.RealmState);

                // If the view is detached/out-of-bounds or the index is invalid, ignore the write.
                if (!IsValidIntegerIndex(numericIndex))
                {
                    return;
                }

                SetValue((int)numericIndex, new JsValue(coerced));
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
            if (!IsValidIntegerIndex(numericIndex))
            {
                return;
            }

            SetElement((int)numericIndex, numeric);
            return;
        }

        // OrdinarySet for non-numeric-index properties.
        // We implement this directly rather than delegating to _properties.SetProperty
        // to avoid receiver mismatch issues (the receiver is the typed array, not _properties).
        OrdinarySet(name, value);
    }

    /// <summary>
    ///     Implements OrdinarySet for ordinary (non-numeric-index) properties.
    ///     Creates an own data property on the typed array if the property doesn't exist,
    ///     or updates it if writable.
    /// </summary>
    private void OrdinarySet(string name, JsValue value)
    {
        // Check own property first
        var ownDesc = _properties.GetOwnPropertyDescriptor(name);
        if (ownDesc is not null)
        {
            if (ownDesc.IsAccessorDescriptor)
            {
                ownDesc.Set?.Invoke(new SingleValueArgs(value), _cachedJsValue);
                return;
            }

            if (ownDesc.Writable)
            {
                // Update existing own data property
                ownDesc.JsValue = value;
                _properties.TryDefineProperty(name, new PropertyDescriptor { JsValue = value });
            }

            return;
        }

        // Walk prototype chain to find inherited property
        IJsPropertyAccessor? proto = _properties.Prototype;
        if (proto is null && _properties is IPrototypeAccessorProvider { PrototypeAccessor: { } protoAccessor })
        {
            proto = protoAccessor;
        }

        while (proto is not null)
        {
            if (proto is IJsObjectLike protoLike)
            {
                var protoDesc = protoLike.GetOwnPropertyDescriptor(name);
                if (protoDesc is not null)
                {
                    if (protoDesc.IsAccessorDescriptor)
                    {
                        protoDesc.Set?.Invoke(new SingleValueArgs(value), _cachedJsValue);
                        return;
                    }

                    if (!protoDesc.Writable)
                    {
                        return; // Silently fail
                    }

                    // Inherited writable data property -- create own property on this object
                    break;
                }

                proto = protoLike.Prototype;
                if (proto is null && protoLike is IPrototypeAccessorProvider { PrototypeAccessor: { } pa })
                {
                    proto = pa;
                }
            }
            else
            {
                break;
            }
        }

        // Create a new own data property
        _properties.TryDefineProperty(name, new PropertyDescriptor
        {
            JsValue = value,
            Writable = true,
            Enumerable = true,
            Configurable = true
        });
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
        // Integer-indexed exotic object [[Delete]] (P)
        if (TryCanonicalNumericIndex(name, out var numericIndex))
        {
            // If IsValidIntegerIndex(O, numericIndex) is false, return true.
            // Otherwise, return false (cannot delete indexed elements).
            return !IsValidIntegerIndex(numericIndex);
        }

        return _properties.DeleteOwnProperty(name);
    }

    /// <summary>
    ///     Integer-indexed exotic object [[HasProperty]] (P).
    ///     For canonical numeric index strings, returns whether the index is valid
    ///     WITHOUT consulting the prototype chain.
    /// </summary>
    internal bool HasProperty(string name)
    {
        if (TryCanonicalNumericIndex(name, out var numericIndex))
        {
            return IsValidIntegerIndex(numericIndex);
        }

        // OrdinaryHasProperty: check own properties then prototype chain.
        if (_properties.GetOwnPropertyDescriptor(name) is not null)
        {
            return true;
        }

        // Check built-in property names.
        switch (name)
        {
            case "length":
            case "byteLength":
            case "byteOffset":
            case "buffer":
            case "BYTES_PER_ELEMENT":
            case "set":
            case "subarray":
            case "indexOf":
            case "includes":
                return true;
        }

        // Walk prototype chain. Check PrototypeAccessor first to handle Proxy prototypes.
        IJsPropertyAccessor? protoAccessor = _properties is IPrototypeAccessorProvider { PrototypeAccessor: { } pa }
            ? pa
            : _properties.Prototype;

        if (protoAccessor is JsProxy protoProxy)
        {
            return protoProxy.HasProperty(name);
        }

        if (protoAccessor is JsObject protoObj)
        {
            return protoObj.HasProperty(name);
        }

        return false;
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        // Integer-indexed exotic object [[GetOwnProperty]] (P)
        if (TryCanonicalNumericIndex(name, out var numericIndex))
        {
            if (!IsValidIntegerIndex(numericIndex))
            {
                return null;
            }

            return new PropertyDescriptor
            {
                Value = GetValueForIndex((int)numericIndex),
                Writable = true,
                Enumerable = true,
                Configurable = true
            };
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
        // Integer-indexed exotic object [[DefineOwnProperty]] (P, Desc)
        if (TryCanonicalNumericIndex(name, out var numericIndex))
        {
            // If IsValidIntegerIndex(O, numericIndex) is false, return false.
            if (!IsValidIntegerIndex(numericIndex))
            {
                return false;
            }

            // Per ES2024 spec 10.4.5.3:
            // If Desc has [[Configurable]] and it is false, return false.
            // If Desc has [[Enumerable]] and it is false, return false.
            // If IsAccessorDescriptor(Desc), return false.
            // If Desc has [[Writable]] and it is false, return false.
            if (descriptor is { HasConfigurable: true, Configurable: false } ||
                descriptor is { HasEnumerable: true, Enumerable: false } ||
                descriptor.IsAccessorDescriptor ||
                descriptor is { HasWritable: true, Writable: false })
            {
                return false;
            }

            // If Desc has a [[Value]] field, perform IntegerIndexedElementSet.
            if (descriptor.HasValue)
            {
                SetValue((int)numericIndex, descriptor.JsValue);
            }

            return true;
        }

        // Canonical numeric index strings that are not valid integer indices
        // (e.g. "-0", "-1") always return false (define fails silently).
        if (TryCanonicalNumericIndex(name, out _))
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

        // Per IntegerIndexedElementSet: after coercion, if buffer is detached or index
        // is no longer valid, the write is a no-op.
        if (IsDetachedOrOutOfBounds() || index < 0 || index >= Length)
        {
            return;
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

    /// <summary>
    ///     Creates a new typed array of the same type viewing the given buffer at the specified byte offset and element length.
    ///     Used by subarray's species create path.
    /// </summary>
    public abstract TypedArrayBase CreateSubarrayView(JsArrayBuffer buffer, int byteOffset, int length);

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
        if (offset < 0 || (long)offset + source.Length > targetLength)
        {
            throw CreateOutOfBoundsTypeError();
        }

        // Per spec: if SameValue(srcBuffer, targetBuffer), clone srcBuffer first
        // to avoid overlapping writes.
        if (ReferenceEquals(source.Buffer, _buffer))
        {
            // Clone source bytes to avoid overlapping copy issues
            var srcByteOffset = source.ByteOffset;
            var srcByteLength = source.Length * source.BytesPerElement;
            var clonedBytes = new byte[srcByteLength];
            Array.Copy(source.Buffer.Buffer, srcByteOffset, clonedBytes, 0, srcByteLength);

            var targetByteOffset = _byteOffset + offset * _bytesPerElement;
            // Same type: byte-level copy
            if (source.BytesPerElement == _bytesPerElement &&
                source.GetType() == GetType())
            {
                Array.Copy(clonedBytes, 0, _buffer.Buffer, targetByteOffset, srcByteLength);
            }
            else
            {
                // Different types: element-by-element with temporary buffer
                var tempBuffer = new JsArrayBuffer(srcByteLength);
                Array.Copy(clonedBytes, 0, tempBuffer.Buffer, 0, srcByteLength);
                var tempSource = source.CreateSubarrayView(tempBuffer, 0, source.Length);
                for (var i = 0; i < source.Length; i++)
                {
                    SetValue(offset + i, tempSource.GetValueForIndex(i));
                }
            }
        }
        else
        {
            for (var i = 0; i < source.Length; i++)
            {
                SetValue(offset + i, source.GetValueForIndex(i));
            }
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
        // Integer-indexed exotic object [[Delete]] (P)
        if (TryCanonicalNumericIndex(name, out var numericIndex))
        {
            return !IsValidIntegerIndex(numericIndex);
        }

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
    ///     Implements the ECMAScript CanonicalNumericIndexString abstract operation.
    ///     Returns true if the string is a canonical numeric index string, with the
    ///     numeric value in <paramref name="numericIndex"/>.
    ///     A string is a canonical numeric index string if ToString(ToNumber(s)) === s,
    ///     or if s is "-0".
    /// </summary>
    internal static bool TryCanonicalNumericIndex(string candidate, out double numericIndex)
    {
        // Step 1: If argument is "-0", return -0.
        if (string.Equals(candidate, "-0", StringComparison.Ordinal))
        {
            numericIndex = -0.0d;
            return true;
        }

        // Handle "Infinity" and "-Infinity" explicitly since double.TryParse may
        // not handle them depending on culture.
        if (string.Equals(candidate, "Infinity", StringComparison.Ordinal))
        {
            numericIndex = double.PositiveInfinity;
            return true;
        }

        if (string.Equals(candidate, "-Infinity", StringComparison.Ordinal))
        {
            numericIndex = double.NegativeInfinity;
            return true;
        }

        // Step 2: Let n be ToNumber(argument).
        if (!double.TryParse(candidate, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out numericIndex))
        {
            return false;
        }

        // Step 3: If ToString(n) is not argument, return undefined.
        var roundTripped = JsOps.ToCanonicalNumberString(numericIndex);
        if (!string.Equals(roundTripped, candidate, StringComparison.Ordinal))
        {
            numericIndex = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Checks whether the given canonical numeric index is a valid integer index
    ///     for this typed array. Per the spec, a valid integer index is an integer that
    ///     is not -0, is non-negative, and is less than the array length.
    /// </summary>
    internal bool IsValidIntegerIndex(double numericIndex)
    {
        // IsInteger check
        if (double.IsNaN(numericIndex) || double.IsInfinity(numericIndex))
        {
            return false;
        }

        // Check for -0
        if (numericIndex == 0 && double.IsNegative(numericIndex))
        {
            return false;
        }

        // Must be a non-negative integer
        if (numericIndex != Math.Floor(numericIndex) || numericIndex < 0)
        {
            return false;
        }

        var intIndex = (int)numericIndex;
        return intIndex >= 0 && intIndex < Length;
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
