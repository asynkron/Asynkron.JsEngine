namespace Asynkron.JsEngine.Execution;

internal static class SyntheticScopeIdAllocator
{
    // Use disjoint positive ranges so every synthetic scope id is globally unique.
    // Non-function scopes (loops, blocks, per-iteration bindings) come from the lower range,
    // function roots have their own range to avoid collisions.
    private static int _nextScopeId = 1_000_000;
    private static int _nextFunctionScopeId = 2_000_000;

    public static int Next()
    {
        return Interlocked.Increment(ref _nextScopeId);
    }

    public static int NextFunctionRoot()
    {
        return Interlocked.Increment(ref _nextFunctionScopeId);
    }
}
