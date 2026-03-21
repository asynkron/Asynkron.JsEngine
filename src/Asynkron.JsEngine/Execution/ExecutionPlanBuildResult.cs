#region

using System.Collections.Immutable;

#endregion

namespace Asynkron.JsEngine.Execution;

public enum ExecutionPlanFailureCode
{
    UnsupportedConstruct,
    YieldLoweringFailed,
    UnsupportedYieldShape,
    NormalizationFailed,
    MissingControlFlowTarget,
    AstReentryDetected
}

internal sealed record ExecutionPlanBuildFailure(ExecutionPlanFailureCode Code, string Detail)
{
    public override string ToString() => $"{Code}: {Detail}";
}

internal sealed class ExecutionPlanBuildResult
{
    private ExecutionPlanBuildResult(ExecutionPlan? plan, ExecutionPlanBuildFailure? failure)
    {
        Plan = plan;
        Failure = failure;
    }

    public ExecutionPlan? Plan { get; }

    public ExecutionPlanBuildFailure? Failure { get; }

    public bool Succeeded => Plan is not null;

    public string? FailureReason => Failure?.Detail;

    public static ExecutionPlanBuildResult Success(ExecutionPlan plan)
    {
        return new ExecutionPlanBuildResult(plan, null);
    }

    public static ExecutionPlanBuildResult FailureResult(ExecutionPlanFailureCode code, string detail)
    {
        return new ExecutionPlanBuildResult(null, new ExecutionPlanBuildFailure(code, detail));
    }
}

public readonly record struct ExecutionPlanDiagnosticCounters(int Attempts, int Succeeded, int Failed);

public readonly record struct ExecutionPlanDiagnosticSnapshot(
    ExecutionPlanDiagnosticCounters Functions,
    ExecutionPlanDiagnosticCounters Scripts,
    ImmutableDictionary<ExecutionPlanFailureCode, int> FailureCodes,
    ExecutionPlanFailureCode? LastFailureCode,
    int FunctionCacheHits,
    int ScriptCacheHits);
