#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Execution;

internal sealed class IteratorDriverState : IRentable
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
    /// Resets the state for reuse from pool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
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
/// Pool for IteratorDriverState instances to reduce per-loop allocations.
/// </summary>
internal static class IteratorDriverStatePool
{
    private static readonly ObjectPool<IteratorDriverState> Pool = new(32, static () => new IteratorDriverState());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IteratorDriverState Rent() => Pool.Rent();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(IteratorDriverState state) => Pool.Return(state);
}
