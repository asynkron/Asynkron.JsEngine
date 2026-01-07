#region

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Execution;

internal sealed class IteratorDriverState : IRentable, IActiveIteratorState, IAsJsValue
{
    private JsValue _cachedJsValue;
    internal int PoolLeaseId { get; private set; }

    public IJsObjectLike? IteratorObject { get; set; }
    public IEnumerator<JsValue>? Enumerator { get; set; }
    public bool IsAsyncIterator { get; set; }
    public bool AwaitingNextResult { get; set; }
    public bool AwaitingValue { get; set; }
    public IJsCallable? NextMethod { get; set; }

    /// <summary>
    /// When true, the iterator has been closed (via IteratorClose or loop completion).
    /// Used to prevent double-closing when generator.return() is called.
    /// </summary>
    public bool IteratorClosed { get; set; }

    /// <summary>
    /// When true, at least one successful next() call has completed and we have entered the loop body.
    /// Per ES spec 13.6.4.13 step 5.d, if IteratorStep (calling next()) throws before
    /// we enter the loop body, we should NOT call IteratorClose.
    /// IteratorClose is only called when the loop body throws, or break/return is executed.
    /// </summary>
    public bool HasEnteredLoop { get; set; }

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

    /// <inheritdoc />
    public bool TryGetActiveIterator(out IJsObjectLike iterator)
    {
        if (IteratorObject is not null && !IteratorClosed)
        {
            iterator = IteratorObject;
            return true;
        }

        iterator = null!;
        return false;
    }

    /// <inheritdoc />
    public void MarkIteratorClosed()
    {
        IteratorClosed = true;
        // Keep IteratorObject so it can still be queried, but mark as closed
    }

    public ref readonly JsValue AsJsValue
    {
        get
        {
            if (_cachedJsValue.ObjectValue is null)
            {
                _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
            }

            return ref _cachedJsValue;
        }
    }

    /// <summary>
    /// Called when state is rented from pool.
    /// </summary>
    void IRentable.Activate(ILogger? logger)
    {
        logger?.LogInformation("IteratorDriverState.Activate");
    }

    /// <summary>
    /// Resets the state for reuse from pool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset(ILogger? logger)
    {
        logger?.LogInformation("IteratorDriverState.Reset");
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
        IteratorClosed = false;
        HasEnteredLoop = false;
        PoolLeaseId = 0;
    }

    internal void MarkLeased(int leaseId)
    {
        if (!PoolGuard.Enabled)
        {
            return;
        }

        if (PoolLeaseId != 0)
        {
            throw new InvalidOperationException("IteratorDriverState double lease detected.");
        }

        PoolLeaseId = leaseId;
    }

    internal void MarkReturned()
    {
        if (!PoolGuard.Enabled)
        {
            return;
        }

        if (PoolLeaseId == 0)
        {
            throw new InvalidOperationException("IteratorDriverState returned without an active lease.");
        }

        PoolLeaseId = 0;
    }

    internal void AssertLease(int expectedLeaseId, string usage)
    {
        if (!PoolGuard.Enabled)
        {
            return;
        }

        if (PoolLeaseId != expectedLeaseId)
        {
            throw new InvalidOperationException(
                $"IteratorDriverState lease mismatch for {usage}. expected={expectedLeaseId} actual={PoolLeaseId}");
        }
    }

    [Conditional("DEBUG")]
    internal void MarkLeasedDebug()
    {
        PoolDebug.MarkLeased(this);
    }

    [Conditional("DEBUG")]
    internal void MarkReturnedDebug()
    {
        PoolDebug.MarkReturned(this);
    }

    [Conditional("DEBUG")]
    internal void AssertOwnership(string usage)
        => PoolDebug.AssertOwned(this, usage);
}

/// <summary>
/// Pool for IteratorDriverState instances to reduce per-loop allocations.
/// </summary>
internal static class IteratorDriverStatePool
{
    private static readonly ObjectPool<IteratorDriverState> Pool = new(32, static () => new IteratorDriverState());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IteratorDriverState Rent()
    {
        var state = Pool.Rent();
        if (PoolGuard.Enabled)
        {
            state.MarkLeased(PoolGuard.NextLeaseId());
        }

        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(IteratorDriverState state)
    {
        state.MarkReturned();
        Pool.Return(state);
    }
}
