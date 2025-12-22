#region

using System.Collections;
using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// A disposable wrapper for pooled argument arrays that returns them on dispose.
/// Use with 'using' statement to ensure arrays are returned to the pool.
/// </summary>
public readonly struct PooledArgumentArray(object?[] array, int length) : IDisposable, IReadOnlyList<object?>
{
    public object? this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => index < length ? array[index] : throw new IndexOutOfRangeException();
    }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => length;
    }

    public object?[] Array
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => array;
    }

    public void Dispose()
    {
        if (array?.Length <= 4)
        {
            JsValueCache.ReturnArgumentArray(array);
        }
    }

    public IEnumerator<object?> GetEnumerator()
    {
        for (var i = 0; i < length; i++)
        {
            yield return array[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
