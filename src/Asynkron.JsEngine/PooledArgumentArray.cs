using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine;

/// <summary>
/// A disposable wrapper for pooled argument arrays that returns them on dispose.
/// Use with 'using' statement to ensure arrays are returned to the pool.
/// </summary>
public readonly struct PooledArgumentArray : IDisposable, IReadOnlyList<object?>
{
    private readonly object?[] _array;
    private readonly int _length;

    public PooledArgumentArray(object?[] array, int length)
    {
        _array = array;
        _length = length;
    }

    public object? this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => index < _length ? _array[index] : throw new IndexOutOfRangeException();
    }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length;
    }

    public object?[] Array
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _array;
    }

    public void Dispose()
    {
        if (_array?.Length <= 4)
        {
            JsValueCache.ReturnArgumentArray(_array);
        }
    }

    public IEnumerator<object?> GetEnumerator()
    {
        for (var i = 0; i < _length; i++)
        {
            yield return _array[i];
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
