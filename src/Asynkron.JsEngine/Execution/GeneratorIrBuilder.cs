#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Dispatches generator IR building to the appropriate builder based on the
///     function kind (synchronous vs async). For now only synchronous generators
///     are supported; async generator functions always fall back to the replay
///     engine and are reported as IR failures via <see cref="GeneratorIrDiagnostics" />.
/// </summary>
internal static class GeneratorIrBuilder
{
    public static bool TryBuild(FunctionExpression function, out GeneratorPlan plan, out string? failureReason,
        bool reportDiagnostics = true)
    {
        // All function kinds (generators, async generators, pure async) use the same IR builder.
        // The difference is in how they're executed:
        // - Sync generators: caller drives via .next()/.return()/.throw()
        // - Async generators: same but return Promises
        // - Pure async functions: run to completion with await suspension
        var succeeded = SyncGeneratorIrBuilder.TryBuild(function, out plan, out failureReason);

        if (reportDiagnostics)
        {
            GeneratorIrDiagnostics.ReportResult(function, succeeded, failureReason);
        }

        return succeeded;
    }
}
