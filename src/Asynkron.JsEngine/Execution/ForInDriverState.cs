#region

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Holds the iteration state for a for-in loop, including the list of property keys
/// and the current iteration index.
/// </summary>
internal sealed class ForInDriverState : IRentable, IAsJsValue
{
    private JsValue _cachedJsValue;

    /// <summary>
    /// The list of property keys to iterate over. Collected upfront from the object
    /// and its prototype chain.
    /// </summary>
    public List<JsValue> PropertyKeys { get; } = new(16);

    /// <summary>
    /// The current index in the PropertyKeys list.
    /// </summary>
    public int CurrentIndex { get; set; }

    /// <summary>
    /// Pre-resolved JsVariable for fast state access (avoids dictionary lookups per iteration).
    /// </summary>
    public JsVariable StateVariable { get; set; }

    /// <summary>
    /// Pre-resolved JsVariable for fast value access (avoids dictionary lookups per iteration).
    /// </summary>
    public JsVariable ValueVariable { get; set; }

    /// <summary>
    /// The loop scope environment captured at ForInInit time.
    /// </summary>
    public JsEnvironment? LoopScopeEnvironment { get; set; }

    [Conditional("DEBUG")]
    internal void AssertOwnership(string usage) => PoolDebug.AssertOwned(this, usage);

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
        logger?.LogInformation("ForInDriverState.Activate");
    }

    /// <summary>
    /// Resets the state for reuse from pool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset(ILogger? logger = null)
    {
        logger?.LogInformation("ForInDriverState.Reset");
        PropertyKeys.Clear();
        CurrentIndex = 0;
        StateVariable = default;
        ValueVariable = default;
        LoopScopeEnvironment = null;
    }
}

/// <summary>
/// Pool for ForInDriverState instances to reduce per-loop allocations.
/// </summary>
internal static class ForInDriverStatePool
{
    private static readonly ObjectPool<ForInDriverState> Pool = new(32, static () => new ForInDriverState());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ForInDriverState Rent()
    {
        var state = Pool.Rent();
        // CRITICAL: Reset ALL state before use - pooled objects must be fully clean
        state.Reset(null);
        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(ForInDriverState state)
    {
        Pool.Return(state);
    }
}
