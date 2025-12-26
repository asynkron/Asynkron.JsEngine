#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// Pool for JsValue[] arrays using fixed-size buckets.
/// Avoids allocating new arrays for slot storage in environments.
/// Bucket sizes: 8, 16, 32, 64, 128, 256, 512, 1024
/// </summary>
internal static class JsValueArrayPool
{
    private const int PoolSize = 100; // Pre-heat size per bucket

    // Fixed-size bucket pools
    private static readonly ObjectPool<JsValue[]> Pool8 = new(PoolSize, static () => new JsValue[8]);
    private static readonly ObjectPool<JsValue[]> Pool16 = new(PoolSize, static () => new JsValue[16]);
    private static readonly ObjectPool<JsValue[]> Pool32 = new(PoolSize, static () => new JsValue[32]);
    private static readonly ObjectPool<JsValue[]> Pool64 = new(PoolSize, static () => new JsValue[64]);
    private static readonly ObjectPool<JsValue[]> Pool128 = new(PoolSize, static () => new JsValue[128]);
    private static readonly ObjectPool<JsValue[]> Pool256 = new(PoolSize, static () => new JsValue[256]);
    private static readonly ObjectPool<JsValue[]> Pool512 = new(PoolSize, static () => new JsValue[512]);
    private static readonly ObjectPool<JsValue[]> Pool1024 = new(PoolSize, static () => new JsValue[1024]);

    /// <summary>
    /// Rents an array from the pool with at least the requested capacity.
    /// The actual array may be larger than requested (rounded up to bucket size).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue[] Rent(int minimumLength)
    {
        // Round up to nearest bucket size
        return minimumLength switch
        {
            <= 8 => Pool8.Rent(),
            <= 16 => Pool16.Rent(),
            <= 32 => Pool32.Rent(),
            <= 64 => Pool64.Rent(),
            <= 128 => Pool128.Rent(),
            <= 256 => Pool256.Rent(),
            <= 512 => Pool512.Rent(),
            <= 1024 => Pool1024.Rent(),
            // For very large arrays, just allocate (rare case)
            _ => new JsValue[minimumLength]
        };
    }

    /// <summary>
    /// Returns an array to the pool. The array will be cleared before reuse.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(JsValue[]? array)
    {
        if (array is null)
        {
            return;
        }

        // Clear the array to avoid holding references
        Array.Clear(array);

        // Return to appropriate pool based on length
        switch (array.Length)
        {
            case 8:
                Pool8.Return(array);
                break;
            case 16:
                Pool16.Return(array);
                break;
            case 32:
                Pool32.Return(array);
                break;
            case 64:
                Pool64.Return(array);
                break;
            case 128:
                Pool128.Return(array);
                break;
            case 256:
                Pool256.Return(array);
                break;
            case 512:
                Pool512.Return(array);
                break;
            case 1024:
                Pool1024.Return(array);
                break;
            // Non-pooled sizes are just discarded (will be GC'd)
        }
    }
}
