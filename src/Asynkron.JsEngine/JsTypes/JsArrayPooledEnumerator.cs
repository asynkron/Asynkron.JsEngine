using System.Collections;
using System.Runtime.CompilerServices;

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
        if (_index >= array.Length)
        {
            return false;
        }

        _current = array.GetElement(_index);
        _index++;
        return true;

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
