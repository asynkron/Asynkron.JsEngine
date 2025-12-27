#region

using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// Interface for poolable objects that can reset their state before being returned to the pool.
/// </summary>
internal interface IRentable
{
    /// <summary>
    /// Called when the object is rented from the pool.
    /// Use this to rent sub-objects or initialize transient state.
    /// </summary>
    void Activate();

    /// <summary>
    /// Resets the object to a clean state for reuse.
    /// Called automatically when the object is returned to the pool.
    /// Use this to return sub-objects to their pools.
    /// </summary>
    void Reset();
}

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

/// <summary>
/// A fast, lock-free object pool using a fixed-size array.
/// Uses Interlocked operations for thread-safety with minimal contention.
/// If T implements IRentable, Reset() is called automatically on return.
/// </summary>
internal sealed class ObjectPool<T>(int size, Func<T> factory)
    where T : class
{
    private static readonly bool IsRentable = typeof(IRentable).IsAssignableFrom(typeof(T));

    private readonly T?[] _items = new T?[size];

    /// <summary>
    /// Rents an object from the pool, or creates a new one if the pool is empty.
    /// Returns a disposable handle that auto-returns the object on dispose.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PooledObject<T> RentOrCreate()
    {
        return new PooledObject<T>(Rent(), this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Rent()
    {
        var items = _items;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item is not null && Interlocked.CompareExchange(ref items[i], null, item) == item)
            {
                if (IsRentable)
                {
                    ((IRentable)item).Activate();
                }
                return item;
            }
        }

        var newItem = factory();
        if (IsRentable)
        {
            ((IRentable)newItem).Activate();
        }
        return newItem;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(T item)
    {
        if (IsRentable)
        {
            ((IRentable)item).Reset();
        }

        var items = _items;
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i] is null && Interlocked.CompareExchange(ref items[i], item, null) is null)
            {
                return;
            }
        }
        // Pool full, item will be GC'd
    }
}
