namespace Asynkron.JsEngine;

/// <summary>
/// Tracks progress through a generator's yield points so re-executing the
/// generator body can skip values that have already been produced.
/// </summary>
public sealed class YieldTracker(int skipCount)
{
    private int _currentIndex;

    /// <summary>
    /// Returns <c>true</c> when the current execution pass should emit the
    /// value produced by the active <c>yield</c> expression.
    /// </summary>
    public bool ShouldYield(out int yieldIndex)
    {
        yieldIndex = _currentIndex;
        var should = _currentIndex >= skipCount;
        _currentIndex++;
        return should;
    }
}
