#region

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// Global kill switch to disable all object pooling.
/// When disabled, Rent() always creates new instances and Return() is a no-op.
/// </summary>
internal static class PoolingConfig
{
    private static bool? _disabled;

    /// <summary>
    /// When true, all pooling is disabled. Rent() creates fresh instances, Return() does nothing.
    /// Set via JSENGINE_DISABLE_POOLING=1 environment variable or programmatically.
    /// </summary>
    public static bool Disabled
    {
        get => _disabled ??= IsEnvDisabled();
        set => _disabled = value;
    }

    private static bool IsEnvDisabled()
    {
        var setting = Environment.GetEnvironmentVariable("JSENGINE_DISABLE_POOLING");
        if (string.IsNullOrWhiteSpace(setting)) return false;
        return setting == "1" ||
               string.Equals(setting, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(setting, "on", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// A fast, lock-free object pool using a fixed-size array.
/// Uses Interlocked operations for thread-safety with minimal contention.
/// If T implements IRentable, OnRent() is called on rent, OnReturn() on return.
/// When PoolingConfig.Disabled is true, always creates new instances and Return is a no-op.
/// </summary>
internal sealed class ObjectPool<T>(int size, Func<T> factory)
    where T : class
{
    private static readonly bool IsRentable = typeof(IRentable).IsAssignableFrom(typeof(T));

    private readonly T?[] _items = new T?[size];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Rent(ILogger? logger = null)
    {
        // Kill switch: always create fresh instance
        if (PoolingConfig.Disabled)
        {
            var freshItem = factory();
            if (IsRentable)
            {
                ((IRentable)freshItem).OnRent(logger);
            }
            return freshItem;
        }

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
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(T item, ILogger? logger = null)
    {
        // Kill switch: do nothing, let GC handle it
        if (PoolingConfig.Disabled)
        {
            return;
        }

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
    }
}
