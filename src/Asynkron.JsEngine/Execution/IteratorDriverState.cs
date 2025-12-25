#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Execution;

internal sealed class IteratorDriverState
{
    public IJsObjectLike? IteratorObject { get; set; }
    public IEnumerator<JsValue>? Enumerator { get; set; }
    public bool IsAsyncIterator { get; set; }
    public bool AwaitingNextResult { get; set; }
    public bool AwaitingValue { get; set; }
    public IJsCallable? NextMethod { get; set; }

    /// <summary>
    /// Pre-resolved JsVariable for fast iterator state access (avoids dictionary lookups per iteration).
    /// </summary>
    public JsVariable IteratorVariable { get; set; }

    /// <summary>
    /// Pre-resolved JsVariable for fast value access (avoids dictionary lookups per iteration).
    /// </summary>
    public JsVariable ValueVariable { get; set; }

    /// <summary>
    /// The current per-iteration environment for the enclosing loop(s).
    /// This is updated by CreateIterationEnvironmentInstruction and used to find the
    /// correct loop scope after async resume when the environment is reset to function scope.
    /// </summary>
    public JsEnvironment? CurrentIterationEnvironment { get; set; }

    /// <summary>
    /// The loop scope environment captured at IteratorInit time.
    /// This is the environment BEFORE any per-iteration envs are created for this iterator loop.
    /// Used by CreateIterationEnvironmentInstruction on first iteration after async resume
    /// when CurrentIterationEnvironment is still null but environment has been reset to function scope.
    /// </summary>
    public JsEnvironment? LoopScopeEnvironment { get; set; }

    /// <summary>
    /// Clears the state for reuse from pool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        IteratorObject = null;
        Enumerator = null;
        IsAsyncIterator = false;
        AwaitingNextResult = false;
        AwaitingValue = false;
        NextMethod = null;
        IteratorVariable = default;
        ValueVariable = default;
        CurrentIterationEnvironment = null;
        LoopScopeEnvironment = null;
    }
}

/// <summary>
/// Simple pool for IteratorDriverState instances to reduce per-loop allocations.
/// Uses a lock-free fixed-size array with Interlocked operations for thread-safety.
/// </summary>
internal static class IteratorDriverStatePool
{
    private const int PoolSize = 32;
    private static readonly IteratorDriverState?[] Pool = new IteratorDriverState?[PoolSize];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IteratorDriverState Rent()
    {
        // Try to get from pool using lock-free compare-exchange
        for (var i = 0; i < PoolSize; i++)
        {
            var state = Pool[i];
            if (state is not null && Interlocked.CompareExchange(ref Pool[i], null, state) == state)
            {
                return state;
            }
        }

        return new IteratorDriverState();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(IteratorDriverState state)
    {
        // Clear to a neutral state
        state.Clear();

        // Try to return to pool using lock-free compare-exchange
        for (var i = 0; i < PoolSize; i++)
        {
            if (Pool[i] is null && Interlocked.CompareExchange(ref Pool[i], state, null) is null)
            {
                return;
            }
        }
        // Pool full, state will be GC'd
    }
}
