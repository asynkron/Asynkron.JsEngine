#region

using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Pool for IteratorResultObject instances to reduce per-iteration allocations in for-of loops.
/// </summary>
internal static class IteratorResultObjectPool
{
    private static readonly ObjectPool<IteratorResultObject> Pool = new(64,
        static () => new IteratorResultObject(JsValue.Undefined, false));

    /// <summary>
    /// Rents a pooled iterator result object and initializes it with the given values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IteratorResultObject Rent(JsValue value, bool done)
    {
        var result = Pool.Rent();
        result.Reset(value, done);
        return result;
    }

    /// <summary>
    /// Returns an iterator result object to the pool.
    /// Objects that have been captured (assigned to variables, etc.) are not pooled.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(IteratorResultObject result)
    {
        // Don't return captured objects or the shared singleton
        if (result.IsCaptured || ReferenceEquals(result, IteratorResultObject.DoneUndefined))
        {
            return;
        }

        // Clear value reference to avoid holding onto objects
        result.Reset(JsValue.Undefined, false);
        Pool.Return(result);
    }
}
