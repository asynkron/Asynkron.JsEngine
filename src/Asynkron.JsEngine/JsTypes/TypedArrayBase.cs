using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Abstract base class for all JavaScript typed arrays.
///     Provides shared logic for property access so the evaluator
///     can treat typed arrays like regular <see cref="IJsObjectLike" /> instances.
/// </summary>
public abstract class TypedArrayBase : IJsObjectLike
{
    protected readonly JsArrayBuffer _buffer;
    protected readonly int _byteOffset;
    protected readonly int _bytesPerElement;
    private readonly HostFunction _indexOfFunction;
    private readonly HostFunction _includesFunction;
    protected readonly int _initialLength;
    protected readonly bool _isLengthTracking;

    private readonly JsObject _properties = new();
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
                return Symbols.Undefined;
            }

            var offset = args.Count > 1 && args[1] is double d ? (int)d : 0;

            switch (args[0])
            {
                case TypedArrayBase sourceTypedArray:
                    target.Set(sourceTypedArray, offset);
                    break;
                case JsArray sourceArray:
                    target.Set(sourceArray, offset);
                    break;
            }

            return Symbols.Undefined;
        });

        _subarrayFunction = new HostFunction((thisValue, args) =>
        {
            var target = ResolveThis(thisValue, this);
            var begin = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : target.Length;

            return target.Subarray(begin, end);
        });

        _sliceFunction = new HostFunction((thisValue, args) =>
        {
            var target = ResolveThis(thisValue, this);
            var begin = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : target.Length;

            return CreateSlice(target, begin, end);
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
    public int ByteOffset => _byteOffset;

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
    public virtual int Length
    {
        get
        {
            if (IsDetachedOrOutOfBounds())
            {
                return 0;
            }

            return GetCurrentLength();
        }
    }

    /// <summary>
    ///     Gets the size in bytes of each element in the array.
    /// </summary>
    public int BytesPerElement => _bytesPerElement;

    public JsObject? Prototype => _properties.Prototype;

    public bool IsSealed => _properties.IsSealed;

    public IEnumerable<string> Keys => _properties.Keys;

    /// <summary>
    ///     True when this typed array stores BigInt elements.
    /// </summary>
    public virtual bool IsBigIntArray => false;

    protected int GetCurrentLength()
    {
        if (_buffer.IsDetached)
        {
            return 0;
        }

        if (_isLengthTracking)
        {
            var availableBytes = Math.Max(_buffer.ByteLength - _byteOffset, 0);
            return availableBytes / _bytesPerElement;
        }

        return _initialLength;
    }

    public bool TryGetProperty(string name, object? receiver, out object? value)
    {
        // Allow dynamically assigned properties and prototype chain lookups first.
        if (_properties.TryGetProperty(name, receiver ?? this, out value))
        {
            return true;
        }

        switch (name)
        {
            case "length":
                value = (double)Length;
                return true;
            case "byteLength":
                value = (double)ByteLength;
                return true;
            case "byteOffset":
                value = (double)ByteOffset;
                return true;
            case "buffer":
                value = Buffer;
                return true;
            case "BYTES_PER_ELEMENT":
                value = (double)BytesPerElement;
                return true;
            case "set":
                value = _setFunction;
                return true;
            case "subarray":
                value = _subarrayFunction;
                return true;
            case "slice":
                value = _sliceFunction;
                return true;
            case "indexOf":
                value = _indexOfFunction;
                return true;
            case "includes":
                value = _includesFunction;
                return true;
        }

        if (TryParseIndex(name, out var index))
        {
            if (IsDetachedOrOutOfBounds())
            {
                value = null;
                return false;
            }

            var length = Length;
            if (index >= 0 && index < length)
            {
                if (_buffer.IsDetached)
                {
                    value = null;
                    return false;
                }

                value = GetValueForIndex(index);
                return true;
            }

            value = null;
            return false;
        }

        value = null;
        return false;
    }

    public bool TryGetProperty(string name, out object? value)
    {
        return TryGetProperty(name, this, out value);
    }

    public void SetProperty(string name, object? value)
    {
        SetProperty(name, value, this);
    }

    public void SetProperty(string name, object? value, object? receiver)
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
            if (index < 0)
            {
                throw new InvalidOperationException($"Invalid typed array index '{name}'.");
            }

            if (IsDetachedOrOutOfBounds())
            {
                throw CreateOutOfBoundsTypeError();
            }

            if (index >= Length)
            {
                throw CreateOutOfBoundsTypeError();
            }

            SetValue(index, value);
            return;
        }

        _properties.SetProperty(name, value, receiver ?? this);
    }

    private static double ToIntegerOrInfinity(object? value, EvaluationContext? context)
    {
        var number = JsOps.ToNumberWithContext(value, context);
        if (context is not null && context.IsThrow)
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

    internal static object IndexOfInternal(TypedArrayBase target, IReadOnlyList<object?> args)
    {
        if (target.IsDetachedOrOutOfBounds())
        {
            throw target.CreateOutOfBoundsTypeError();
        }

        var evalContext = target._buffer.RealmState is { } realmState ? new EvaluationContext(realmState) : null;
        var searchElement = args.Count > 0 ? args[0] : Symbols.Undefined;
        // Snapshot the length before coercion, as required by the spec.
        var initialLength = target.Length;
        if (initialLength <= 0)
        {
            return -1d;
        }

        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], evalContext) : 0d;

        if (target.IsDetachedOrOutOfBounds())
        {
            return -1d;
        }

        var currentLength = target.Length;
        // Length-tracking views use the pre-coercion length; fixed views clamp to current.
        var len = target._isLengthTracking ? initialLength : Math.Min(initialLength, currentLength);
        if (len <= 0)
        {
            return -1d;
        }

        double startIndexNumber;
        if (double.IsPositiveInfinity(fromIndex))
        {
            return -1d;
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
                return -1d;
            }

            if (i >= target.Length)
            {
                continue;
            }

            object? element = target switch
            {
                JsBigInt64Array bi64 => bi64.GetBigIntElement(i),
                JsBigUint64Array bu64 => bu64.GetBigIntElement(i),
                _ => target.GetElement(i)
            };

            if (JsOps.StrictEquals(element, searchElement))
            {
                return (double)i;
            }
        }

        return -1d;
    }

    internal static object IncludesInternal(TypedArrayBase target, IReadOnlyList<object?> args)
    {
        if (target.IsDetachedOrOutOfBounds())
        {
            throw target.CreateOutOfBoundsTypeError();
        }

        var evalContext = target._buffer.RealmState is { } realmState ? new EvaluationContext(realmState) : null;
        var searchElement = args.Count > 0 ? args[0] : Symbols.Undefined;
        var initialLength = target.Length;
        if (initialLength <= 0)
        {
            return false;
        }

        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], evalContext) : 0d;

        if (target.IsDetachedOrOutOfBounds())
        {
            return false;
        }

        var currentLength = target.Length;
        var len = target._isLengthTracking ? initialLength : Math.Min(initialLength, currentLength);
        if (len <= 0)
        {
            return false;
        }

        if (double.IsPositiveInfinity(fromIndex))
        {
            return false;
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
            if (target.IsDetachedOrOutOfBounds())
            {
                return false;
            }

            if (i >= target.Length)
            {
                continue;
            }

            object? element = target switch
            {
                JsBigInt64Array bi64 => bi64.GetBigIntElement(i),
                JsBigUint64Array bu64 => bu64.GetBigIntElement(i),
                _ => target.GetElement(i)
            };

            if (SameValueZero(element, searchElement))
            {
                return true;
            }
        }

        return false;
    }

    internal static object LastIndexOfInternal(TypedArrayBase target, IReadOnlyList<object?> args)
    {
        if (target.IsDetachedOrOutOfBounds())
        {
            throw target.CreateOutOfBoundsTypeError();
        }

        var evalContext = target._buffer.RealmState is { } realmState ? new EvaluationContext(realmState) : null;
        var searchElement = args.Count > 0 ? args[0] : Symbols.Undefined;
        var initialLength = target.Length;
        if (initialLength <= 0)
        {
            return -1d;
        }

        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], evalContext) : initialLength - 1;

        if (target.IsDetachedOrOutOfBounds())
        {
            return -1d;
        }

        var currentLength = target.Length;
        var len = target._isLengthTracking ? initialLength : Math.Min(initialLength, currentLength);
        if (len <= 0)
        {
            return -1d;
        }

        double startIndexNumber;
        if (double.IsPositiveInfinity(fromIndex))
        {
            startIndexNumber = len - 1;
        }
        else if (double.IsNegativeInfinity(fromIndex))
        {
            return -1d;
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
                return -1d;
            }
        }

        var startIndex = (int)startIndexNumber;

        for (var i = startIndex; i >= 0; i--)
        {
            if (target.IsDetachedOrOutOfBounds())
            {
                return -1d;
            }

            var loopLength = target.Length;
            if (i >= loopLength)
            {
                continue;
            }

            object? element = target switch
            {
                JsBigInt64Array bi64 => bi64.GetBigIntElement(i),
                JsBigUint64Array bu64 => bu64.GetBigIntElement(i),
                _ => target.GetElement(i)
            };

            if (JsOps.StrictEquals(element, searchElement))
            {
                return (double)i;
            }
        }

        return -1d;
    }

    /// <summary>
    ///     Allows consumers (e.g. Object.setPrototypeOf) to attach a prototype object.
    /// </summary>
    public void SetPrototype(object? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        _properties.DefineProperty(name, descriptor);
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
    ///     Sets an element using the appropriate coercion for numeric typed arrays.
    ///     BigInt arrays override to enforce BigInt conversion.
    /// </summary>
    public virtual void SetValue(int index, object? value)
    {
        var context = _buffer.RealmState is not null ? new EvaluationContext(_buffer.RealmState) : null;
        if (value is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot convert a BigInt value to a number", context,
                _buffer.RealmState);
        }

        var numeric = JsOps.ToNumberWithContext(value, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        SetElement(index, numeric);
    }

    internal virtual object? GetValueForIndex(int index)
    {
        return GetElement(index);
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
        if (offset < 0 || offset + source.Items.Count > targetLength)
        {
            throw CreateOutOfBoundsTypeError();
        }

        for (var i = 0; i < source.Items.Count; i++)
        {
            var value = source.Items[i];
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

    private static bool TryParseIndex(string candidate, out int index)
    {
        return int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
    }

    private static TypedArrayBase ResolveThis(object? thisValue, TypedArrayBase fallback)
    {
        return thisValue as TypedArrayBase ?? fallback;
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

        return _buffer.ByteLength < _byteOffset + _initialLength * _bytesPerElement;
    }

    private static bool SameValueZero(object? left, object? right)
    {
        if (left is double dl && double.IsNaN(dl) && right is double dr && double.IsNaN(dr))
        {
            return true;
        }

        return JsOps.StrictEquals(left, right);
    }

    internal ThrowSignal CreateOutOfBoundsTypeError()
    {
        if (_buffer.RealmState?.TypeErrorConstructor is IJsCallable ctor)
        {
            var obj = ctor.Invoke(["Out of bounds access on TypedArray"], null);
            if (obj is not null)
            {
                return new ThrowSignal(obj);
            }
        }

        var fallback = new JsObject { ["name"] = "TypeError", ["message"] = "Out of bounds access on TypedArray" };

        return new ThrowSignal(fallback);
    }
}
