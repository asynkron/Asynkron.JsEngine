namespace Asynkron.JsEngine.Tests.Test262;

/// <summary>
/// Custom state for Asynkron.JsEngine Test262 tests.
/// </summary>
public static partial class State
{
    static State()
    {
        if (Test262SuiteDiskCache.Enabled)
        {
            Test262StreamLoader = Test262SuiteDiskCache.LoadAsync;
        }
    }

    /// <summary>
    /// Pre-loaded test harness scripts for execution.
    /// </summary>
    public static readonly Dictionary<string, string> Sources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the disk cache directory if available, null otherwise.
    /// </summary>
    public static string? DiskCacheDirectory => Test262SuiteDiskCache.Enabled
        ? Test262SuiteDiskCache.GetCacheDirectory()
        : null;
}
