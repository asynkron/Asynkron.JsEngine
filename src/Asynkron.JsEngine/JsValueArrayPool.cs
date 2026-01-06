#region

using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// Pool for JsValue[] arrays using fixed-size buckets.
/// Avoids allocating new arrays for slot storage in environments.
/// Bucket sizes: 8, 16, 32, 64, 128, 256, 512, 1024
/// </summary>
internal static class JsValueArrayPool
{
    private static readonly BucketedArrayPool<JsValue> Pool = new();

    /// <summary>
    /// Rents an array from the pool with at least the requested capacity.
    /// The actual array may be larger than requested (rounded up to bucket size).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue[] Rent(int minimumLength) => Pool.Rent(minimumLength);

    /// <summary>
    /// Returns an array to the pool.
    /// NOTE: We skip Array.Clear() here because InitializeSlots() always calls
    /// Array.Fill(..., JsValue.Undefined) which overwrites all slots anyway.
    /// This saves ~50ms of ClearWithReferences overhead in hot loops.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(JsValue[]? array) => Pool.Return(array);
}
