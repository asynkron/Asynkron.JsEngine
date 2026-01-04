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
    private static readonly BucketedArrayPool<JsSlot> Pool = new();

    /// <summary>
    /// Rents an array from the pool with at least the requested capacity.
    /// The actual array may be larger than requested (rounded up to bucket size).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsSlot[] Rent(int minimumLength) => Pool.Rent(minimumLength);

    /// <summary>
    /// Returns an array to the pool.
    /// NOTE: We skip Array.Clear() here because InitializeSlots() always initializes
    /// all slots which overwrites all entries anyway.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(JsSlot[]? array) => Pool.Return(array);
}
