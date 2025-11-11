namespace Asynkron.JsEngine.Tests.Test262;

/// <summary>
/// Custom state for Asynkron.JsEngine Test262 tests.
/// </summary>
public static partial class State
{
    /// <summary>
    /// Pre-loaded test harness scripts for execution.
    /// </summary>
    public static readonly Dictionary<string, string> Sources = new(StringComparer.OrdinalIgnoreCase);
}
