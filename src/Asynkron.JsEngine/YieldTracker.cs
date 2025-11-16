namespace Asynkron.JsEngine;

/// <summary>
/// Tracks progress through a generator's yield points so re-executing the
/// generator body can skip values that have already been produced.
/// </summary>
public sealed class YieldTracker
{
    private readonly int _skipCount;
    private int _currentIndex;

    public YieldTracker(int skipCount)
    {
        _skipCount = skipCount;
    }

    /// <summary>
    /// Returns <c>true</c> when the current execution pass should emit the
    /// value produced by the active <c>yield</c> expression.
    /// </summary>
    public bool ShouldYield()
    {
        var should = _currentIndex >= _skipCount;
        _currentIndex++;
        return should;
    }
}
