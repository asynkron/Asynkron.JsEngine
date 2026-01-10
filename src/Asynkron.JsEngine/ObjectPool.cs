#region

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// A fast, lock-free object pool using a fixed-size array.
/// Uses Interlocked operations for thread-safety with minimal contention.
/// If T implements IRentable, OnRent() is called on rent, OnReturn() on return.
/// When DISABLE_POOLING is defined, always creates new instances and Return is a no-op.
/// </summary>
internal sealed class ObjectPool<T>(int size, Func<T> factory)
    where T : class
{
    private static readonly bool IsRentable = typeof(IRentable).IsAssignableFrom(typeof(T));

    private readonly T?[] _items = new T?[size];

    [MethodImpl(JsEngineConstants.Inlining)]
    public T Rent(ILogger? logger = null)
    {
#if DISABLE_POOLING
        // Kill switch: always create fresh instance
        var freshItem = factory();
        if (IsRentable)
        {
            ((IRentable)freshItem).OnRent(logger);
        }
        return freshItem;
#else
        var items = _items;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item is not null && Interlocked.CompareExchange(ref items[i], null, item) == item)
            {
                if (IsRentable)
                {
                    ((IRentable)item).OnRent(logger);
                }
                PoolDebug.MarkLeased(item);
                return item;
            }
        }

        var newItem = factory();
        if (IsRentable)
        {
            ((IRentable)newItem).OnRent(logger);
        }
        PoolDebug.MarkLeased(newItem);
        return newItem;
#endif
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public void Return(T item, ILogger? logger = null)
    {
#if DISABLE_POOLING
        // Kill switch: do nothing, let GC handle it
        _ = item;
        _ = logger;
#else
        if (IsRentable)
        {
            ((IRentable)item).OnReturn(logger);
        }
        PoolDebug.MarkReturned(item);

        var items = _items;
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i] is null && Interlocked.CompareExchange(ref items[i], item, null) is null)
            {
                return;
            }
        }
        // Pool full, item will be GC'd
#endif
    }
}
