using System.Threading;

namespace Asynkron.JsEngine.Execution;

internal static class SyntheticScopeIdAllocator
{
    private static int _nextScopeId = -1;
    private static int _nextPositiveScopeId = 1_000_000;

    public static int Next()
    {
        return Interlocked.Decrement(ref _nextScopeId);
    }

    public static int NextPositive()
    {
        return Interlocked.Increment(ref _nextPositiveScopeId);
    }
}
