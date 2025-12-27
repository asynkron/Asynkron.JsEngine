#region

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Rent(ILogger? logger = null)
    {
        var items = _items;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item is not null && Interlocked.CompareExchange(ref items[i], null, item) == item)
            {
                if (IsRentable)
                {
                    ((IRentable)item).Activate(logger);
                }
                return item;
            }
        }

        var newItem = factory();
        if (IsRentable)
        {
            ((IRentable)newItem).Activate(logger);
        }
        return newItem;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(T item, ILogger? logger = null)
    {
        if (IsRentable)
        {
            ((IRentable)item).Reset(logger);
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
