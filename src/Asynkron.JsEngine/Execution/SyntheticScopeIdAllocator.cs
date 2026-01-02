#region

using System.Threading;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Generates unique synthetic (negative) scope ids for IR constructs that need
/// distinct scopes even when the analyzer did not stamp an id (e.g. per-iteration scopes).
/// Using unique ids avoids collisions when SlotAssignmentRewriter remaps negatives
/// to positive scope ids.
/// </summary>
internal static class SyntheticScopeIdAllocator
{
    private static int _next = -2;

    public static int Next() => Interlocked.Decrement(ref _next);
}
