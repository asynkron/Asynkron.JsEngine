using System.Threading;

namespace Asynkron.JsEngine.Execution;

internal static class SyntheticScopeIdAllocator
{
    private static int _nextScopeId = -1;

    public static int Next()
    {
        return Interlocked.Decrement(ref _nextScopeId);
    }
}
