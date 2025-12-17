using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Dispatches generator IR building to the appropriate builder based on the
///     function kind (synchronous vs async). For now only synchronous generators
///     are supported; async generator functions always fall back to the replay
///     engine and are reported as IR failures via <see cref="GeneratorIrDiagnostics" />.
/// </summary>
internal static class GeneratorIrBuilder
{
    public static bool TryBuild(FunctionExpression function, out GeneratorPlan plan, out string? failureReason)
    {
        var succeeded = function switch
        {
            { IsAsync: true, IsGenerator: true } => AsyncGeneratorIrBuilder.TryBuild(function, out plan,
                out failureReason),
            _ => SyncGeneratorIrBuilder.TryBuild(function, out plan, out failureReason),
        };

        GeneratorIrDiagnostics.ReportResult(function, succeeded, failureReason);
        return succeeded;
    }
}
