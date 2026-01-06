#region

using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// Generic pool for arrays using fixed-size buckets.
/// Avoids allocating new arrays for slot storage in environments.
/// Bucket sizes: 8, 16, 32, 64, 128, 256, 512, 1024
/// </summary>
internal sealed class BucketedArrayPool<T>
{
    private const int PoolSize = 100; // Pre-heat size per bucket

    // Fixed-size bucket pools
    private readonly ObjectPool<T[]> _pool8 = new(PoolSize, static () => new T[8]);
    private readonly ObjectPool<T[]> _pool16 = new(PoolSize, static () => new T[16]);
    private readonly ObjectPool<T[]> _pool32 = new(PoolSize, static () => new T[32]);
    private readonly ObjectPool<T[]> _pool64 = new(PoolSize, static () => new T[64]);
    private readonly ObjectPool<T[]> _pool128 = new(PoolSize, static () => new T[128]);
    private readonly ObjectPool<T[]> _pool256 = new(PoolSize, static () => new T[256]);
    private readonly ObjectPool<T[]> _pool512 = new(PoolSize, static () => new T[512]);
    private readonly ObjectPool<T[]> _pool1024 = new(PoolSize, static () => new T[1024]);

    /// <summary>
    /// Rents an array from the pool with at least the requested capacity.
    /// The actual array may be larger than requested (rounded up to bucket size).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T[] Rent(int minimumLength)
    {
        // Round up to nearest bucket size
        return minimumLength switch
        {
            <= 8 => _pool8.Rent(),
            <= 16 => _pool16.Rent(),
            <= 32 => _pool32.Rent(),
            <= 64 => _pool64.Rent(),
            <= 128 => _pool128.Rent(),
            <= 256 => _pool256.Rent(),
            <= 512 => _pool512.Rent(),
            <= 1024 => _pool1024.Rent(),
            // For very large arrays, just allocate (rare case)
            _ => new T[minimumLength]
        };
    }

    /// <summary>
    /// Returns an array to the pool.
    /// NOTE: We skip Array.Clear() here because callers are expected to
    /// reinitialize all elements which overwrites all entries anyway.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(T[]? array)
    {
        if (array is null)
        {
            return;
        }

        // Return to appropriate pool based on length
        switch (array.Length)
        {
            case 8:
                _pool8.Return(array);
                break;
            case 16:
                _pool16.Return(array);
                break;
            case 32:
                _pool32.Return(array);
                break;
            case 64:
                _pool64.Return(array);
                break;
            case 128:
                _pool128.Return(array);
                break;
            case 256:
                _pool256.Return(array);
                break;
            case 512:
                _pool512.Return(array);
                break;
            case 1024:
                _pool1024.Return(array);
                break;
                // Non-pooled sizes are just discarded (will be GC'd)
        }
    }
}
