using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine;

/// <summary>
/// A disposable handle to a pooled object. Returns the object to the pool on dispose.
/// This is a struct to avoid allocation - use with 'using' statement.
/// </summary>
/// <example>
/// using var handle = pool.RentOrCreate();
/// var item = handle.Item;
/// // item is automatically returned when handle goes out of scope
/// </example>
internal readonly struct PooledObject<T> : IDisposable where T : class
{
    public readonly T Item;
    private readonly ObjectPool<T> _pool;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal PooledObject(T item, ObjectPool<T> pool)
    {
        Item = item;
        _pool = pool;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        _pool.Return(Item);
    }
}
