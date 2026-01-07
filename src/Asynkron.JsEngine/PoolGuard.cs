namespace Asynkron.JsEngine;

internal static class PoolGuard
{
    private static readonly string? GuardEnv =
        Environment.GetEnvironmentVariable("JSENGINE_DEBUG_POOL_GUARDS");
    private static int _nextLeaseId;

    internal static bool Enabled =>
        string.Equals(GuardEnv, "true", StringComparison.OrdinalIgnoreCase);

    internal static int NextLeaseId()
    {
        return Interlocked.Increment(ref _nextLeaseId);
    }
}
