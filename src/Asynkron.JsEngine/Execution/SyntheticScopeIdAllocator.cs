namespace Asynkron.JsEngine.Execution;

internal static class SyntheticScopeIdAllocator
{
    // Use disjoint positive ranges so every synthetic scope id is globally unique.
    // Non-function scopes (loops, blocks, per-iteration bindings) come from the lower range,
    // function roots have their own range to avoid collisions.
    private static int NextScopeId = 1_000_000;
    private static int NextFunctionScopeId = 2_000_000;

    public static int Next()
    {
        return Interlocked.Increment(ref NextScopeId);
    }

    public static int NextFunctionRoot()
    {
        return Interlocked.Increment(ref NextFunctionScopeId);
    }
}
