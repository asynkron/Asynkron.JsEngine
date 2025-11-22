using System.Globalization;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Abstract base class for all JavaScript typed arrays.
/// Provides shared logic for property access so the evaluator
/// can treat typed arrays like regular <see cref="IJsPropertyAccessor"/> instances.
/// </summary>
public abstract class TypedArrayBase : IJsPropertyAccessor
{
    protected readonly JsArrayBuffer _buffer;
    protected readonly int _byteOffset;
    protected readonly int _length;
    protected readonly int _bytesPerElement;

    private readonly JsObject _properties = new();
    private readonly HostFunction _setFunction;
    private readonly HostFunction _subarrayFunction;
    private readonly HostFunction _sliceFunction;

    protected TypedArrayBase(JsArrayBuffer buffer, int byteOffset, int length, int bytesPerElement)
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
        _length = length;
        _bytesPerElement = bytesPerElement;

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
    }

    /// <summary>
    /// Gets the ArrayBuffer referenced by this typed array.
    /// </summary>
    public JsArrayBuffer Buffer => _buffer;

    /// <summary>
    /// Gets the offset in bytes from the start of the ArrayBuffer.
    /// </summary>
    public int ByteOffset => _byteOffset;

    /// <summary>
    /// Gets the length in bytes of the typed array.
    /// </summary>
    public int ByteLength => _length * _bytesPerElement;

    /// <summary>
    /// Gets the number of elements in the typed array.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Gets the size in bytes of each element in the array.
    /// </summary>
    public int BytesPerElement => _bytesPerElement;

    /// <summary>
    /// Allows consumers (e.g. Object.setPrototypeOf) to attach a prototype object.
    /// </summary>
    public void SetPrototype(object? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    /// <summary>
    /// Checks if the given index is valid for this typed array.
    /// </summary>
    protected void CheckBounds(int index)
    {
        if (IsDetachedOrOutOfBounds())
        {
            throw CreateOutOfBoundsTypeError();
        }

        if (index < 0 || index >= _length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range");
        }
    }

    /// <summary>
    /// Gets the byte offset for a given element index.
    /// </summary>
    protected int GetByteIndex(int index)
    {
        return _byteOffset + index * _bytesPerElement;
    }

    /// <summary>
    /// Creates a new typed array that is a view on the same buffer, from begin (inclusive) to end (exclusive).
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
            newArray.SetElement(i, GetElement(start + i));
        }
        return newArray;
    }

    /// <summary>
    /// Gets the element at the specified index as a double (for JavaScript compatibility).
    /// </summary>
    public abstract double GetElement(int index);

    /// <summary>
    /// Sets the element at the specified index from a double (for JavaScript compatibility).
    /// </summary>
    public abstract void SetElement(int index, double value);

    /// <summary>
    /// Copies elements from source array into this array.
    /// </summary>
    public void Set(TypedArrayBase source, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (offset < 0 || offset + source.Length > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        for (var i = 0; i < source.Length; i++) SetElement(offset + i, source.GetElement(i));
    }

    /// <summary>
    /// Copies elements from a regular array into this typed array.
    /// </summary>
    public void Set(JsArray source, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (offset < 0 || offset + source.Items.Count > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        for (var i = 0; i < source.Items.Count; i++)
            {
                var value = source.Items[i];
                var numValue = value switch
                {
                    double d => d,
                    int iv => (double)iv,
                    long lv => (double)lv,
                    float fv => (double)fv,
                    JsBigInt bi => (double)bi.Value,
                    _ => 0.0
                };
                SetElement(offset + i, numValue);
            }
        }

    /// <summary>
    /// Helper method to normalize slice indices.
    /// </summary>
    protected (int start, int end) NormalizeSliceIndices(int begin, int end)
    {
        var len = _length;
        var relativeStart = begin < 0 ? Math.Max(len + begin, 0) : Math.Min(begin, len);
        var relativeEnd = end < 0 ? Math.Max(len + end, 0) : Math.Min(end, len);
        return (relativeStart, relativeEnd);
    }

    public bool TryGetProperty(string name, out object? value)
    {
        // Allow dynamically assigned properties and prototype chain lookups first.
        if (_properties.TryGetProperty(name, out value))
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
        }

        if (TryParseIndex(name, out var index) && index >= 0 && index < Length)
        {
            if (IsDetachedOrOutOfBounds())
            {
                throw CreateOutOfBoundsTypeError();
            }

            value = GetElement(index);
            return true;
        }

        value = null;
        return false;
    }

    public void SetProperty(string name, object? value)
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

            var numericValue = value switch
            {
                double d => d,
                int i => (double)i,
                long l => (double)l,
                float f => (double)f,
                null => 0.0,
                _ => 0.0
            };

            if (index >= Length)
            {
                throw new ArgumentOutOfRangeException(nameof(name), $"Index {index} is outside the bounds of the typed array.");
            }

            SetElement(index, numericValue);
            return;
        }

        _properties.SetProperty(name, value);
    }

    /// <summary>
    /// Deletes a dynamically assigned property. Built-in properties are non-configurable.
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

    private bool IsDetachedOrOutOfBounds()
    {
        return _buffer.ByteLength < _byteOffset + _length * _bytesPerElement;
    }

    private static ThrowSignal CreateOutOfBoundsTypeError()
    {
        if (StandardLibrary.TypeErrorConstructor is IJsCallable ctor)
        {
            var obj = ctor.Invoke(["Out of bounds access on TypedArray"], null);
            if (obj is not null)
            {
                return new ThrowSignal(obj);
            }
        }

        var fallback = new JsObject
        {
            ["name"] = "TypeError",
            ["message"] = "Out of bounds access on TypedArray"
        };

        return new ThrowSignal(fallback);
    }
}
