namespace Asynkron.JsEngine;

/// <summary>
///     Tracks progress through a generator's yield points so re-executing the
///     generator body can skip values that have already been produced.
/// </summary>
public sealed class YieldTracker(ISet<int> consumedYieldIndices)
{
    public int CurrentIndex { get; private set; }

    public int Advance()
    {
        return CurrentIndex++;
    }

    /// <summary>
    ///     Returns <c>true</c> when the current execution pass should emit the
    ///     value produced by the active <c>yield</c> expression.
    /// </summary>
    public bool ShouldYield(out int yieldIndex)
    {
        yieldIndex = CurrentIndex;
        CurrentIndex++;
        return !consumedYieldIndices.Contains(yieldIndex);
    }

    public void MarkConsumed(int yieldIndex)
    {
        consumedYieldIndices.Add(yieldIndex);
    }
}
