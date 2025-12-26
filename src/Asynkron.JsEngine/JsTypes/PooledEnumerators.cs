#region

using System.Collections;
using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Pooled enumerator for JsArray values.
/// Avoids allocating a new state machine for each for-of iteration.
/// </summary>
internal sealed class JsArrayPooledEnumerator : IEnumerator<JsValue>, IRentable
{
    private static readonly ObjectPool<JsArrayPooledEnumerator> Pool = new(32, () => new JsArrayPooledEnumerator());

    private JsArray? _array;
    private uint _index;
    private JsValue _current;

    private JsArrayPooledEnumerator()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsArrayPooledEnumerator Rent(JsArray array)
    {
        var enumerator = Pool.Rent();
        enumerator._array = array;
        enumerator._index = 0;
        enumerator._current = default;
        return enumerator;
    }

    public JsValue Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current;
    }

    object IEnumerator.Current => _current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        var array = _array;
        if (array is null)
        {
            return false;
        }

        // Check _length on each iteration - array may be modified during iteration
        if (_index < array.Length)
        {
            _current = array.GetElement(_index);
            _index++;
            return true;
        }

        return false;
    }

    public void Reset()
    {
        _array = null;
        _index = 0;
        _current = default;
    }

    public void Dispose()
    {
        Pool.Return(this);
    }
}

/// <summary>
/// Pooled enumerator for string character iteration (Unicode code points).
/// Handles surrogate pairs correctly per ECMAScript spec.
/// </summary>
internal sealed class StringPooledEnumerator : IEnumerator<JsValue>, IRentable
{
    private static readonly ObjectPool<StringPooledEnumerator> Pool = new(32, () => new StringPooledEnumerator());

    private string? _value;
    private int _index;
    private JsValue _current;

    private StringPooledEnumerator()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringPooledEnumerator Rent(string value)
    {
        var enumerator = Pool.Rent();
        enumerator._value = value;
        enumerator._index = 0;
        enumerator._current = default;
        return enumerator;
    }

    public JsValue Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current;
    }

    object IEnumerator.Current => _current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        var value = _value;
        if (value is null || _index >= value.Length)
        {
            return false;
        }

        var ch = value[_index];
        if (char.IsHighSurrogate(ch) && _index + 1 < value.Length && char.IsLowSurrogate(value[_index + 1]))
        {
            // Surrogate pair - yield both chars as one string
            _current = value.Substring(_index, 2);
            _index += 2;
        }
        else
        {
            // Single character (BMP or unpaired surrogate)
            _current = ch.ToString();
            _index++;
        }

        return true;
    }

    public void Reset()
    {
        _value = null;
        _index = 0;
        _current = default;
    }

    public void Dispose()
    {
        Pool.Return(this);
    }
}

/// <summary>
/// Pooled enumerator for TypedArray values.
/// Checks buffer validity on each iteration per ECMAScript spec.
/// </summary>
internal sealed class TypedArrayPooledEnumerator : IEnumerator<JsValue>, IRentable
{
    private static readonly ObjectPool<TypedArrayPooledEnumerator> Pool = new(16, () => new TypedArrayPooledEnumerator());

    private TypedArrayBase? _typedArray;
    private int _index;
    private JsValue _current;

    private TypedArrayPooledEnumerator()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TypedArrayPooledEnumerator Rent(TypedArrayBase typedArray)
    {
        var enumerator = Pool.Rent();
        enumerator._typedArray = typedArray;
        enumerator._index = 0;
        enumerator._current = default;
        return enumerator;
    }

    public JsValue Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current;
    }

    object IEnumerator.Current => _current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        var typedArray = _typedArray;
        if (typedArray is null)
        {
            return false;
        }

        // Check length and bounds on each iteration - buffer may be resized during iteration
        if (_index < typedArray.Length)
        {
            // Check if buffer is still valid on each iteration
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                return false;
            }

            _current = typedArray.GetValueForIndex(_index);
            _index++;
            return true;
        }

        return false;
    }

    public void Reset()
    {
        _typedArray = null;
        _index = 0;
        _current = default;
    }

    public void Dispose()
    {
        Pool.Return(this);
    }
}
