#region

using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// Pool for JsSlot[] arrays using fixed-size buckets.
/// Avoids allocating new arrays for slot storage in environments.
/// Bucket sizes: 8, 16, 32, 64, 128, 256, 512, 1024
/// </summary>
internal static class JsSlotArrayPool
{
    private const int PoolSize = 100; // Pre-heat size per bucket

    // Fixed-size bucket pools
    private static readonly ObjectPool<JsSlot[]> Pool8 = new(PoolSize, static () => new JsSlot[8]);
    private static readonly ObjectPool<JsSlot[]> Pool16 = new(PoolSize, static () => new JsSlot[16]);
    private static readonly ObjectPool<JsSlot[]> Pool32 = new(PoolSize, static () => new JsSlot[32]);
    private static readonly ObjectPool<JsSlot[]> Pool64 = new(PoolSize, static () => new JsSlot[64]);
    private static readonly ObjectPool<JsSlot[]> Pool128 = new(PoolSize, static () => new JsSlot[128]);
    private static readonly ObjectPool<JsSlot[]> Pool256 = new(PoolSize, static () => new JsSlot[256]);
    private static readonly ObjectPool<JsSlot[]> Pool512 = new(PoolSize, static () => new JsSlot[512]);
    private static readonly ObjectPool<JsSlot[]> Pool1024 = new(PoolSize, static () => new JsSlot[1024]);

    /// <summary>
    /// Rents an array from the pool with at least the requested capacity.
    /// The actual array may be larger than requested (rounded up to bucket size).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsSlot[] Rent(int minimumLength)
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
            _ => new JsSlot[minimumLength]
        };
    }

    /// <summary>
    /// Returns an array to the pool.
    /// NOTE: We skip Array.Clear() here because InitializeSlots() always initializes
    /// all slots which overwrites all entries anyway.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(JsSlot[]? array)
    {
        if (array is null)
        {
            return;
        }

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
