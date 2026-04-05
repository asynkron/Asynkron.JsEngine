#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution;

public enum ExecutionPlanFailureCode
{
    UnsupportedConstruct,
    UnsupportedExpressionProgram,
    UnsupportedBindingProgram,
    YieldLoweringFailed,
    UnsupportedYieldShape,
    NormalizationFailed,
    MissingControlFlowTarget,
    AstPayloadLeak,
    AstReentryDetected
}

internal sealed record ExecutionPlanBuildFailure(
    ExecutionPlanFailureCode Code,
    string Detail,
    ExpressionProgramFailureCode? ExpressionFailureCode = null)
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

    public static ExecutionPlanBuildResult FailureResult(
        ExecutionPlanFailureCode code,
        string detail,
        ExpressionProgramFailureCode? expressionFailureCode = null)
    {
        return new ExecutionPlanBuildResult(
            null,
            new ExecutionPlanBuildFailure(code, detail, expressionFailureCode));
    }
}

internal readonly record struct FunctionExecutionPlanSeed(
    ExecutionPlan? Plan,
    ExecutionPlanBuildFailure? Failure)
{
    public bool Succeeded => Plan is not null;

    public string? FailureReason => Failure?.Detail;

    public static FunctionExecutionPlanSeed FromResult(ExecutionPlanBuildResult result)
    {
        return new FunctionExecutionPlanSeed(result.Plan, result.Failure);
    }
}

public readonly record struct ExecutionPlanDiagnosticCounters(int Attempts, int Succeeded, int Failed);

public readonly record struct ExecutionPlanDiagnosticSnapshot(
    ExecutionPlanDiagnosticCounters Functions,
    ExecutionPlanDiagnosticCounters Scripts,
    ImmutableDictionary<ExecutionPlanFailureCode, int> FailureCodes,
    ImmutableDictionary<ExpressionProgramFailureCode, int> ExpressionFailureCodes,
    ExecutionPlanFailureCode? LastFailureCode,
    ExpressionProgramFailureCode? LastExpressionFailureCode,
    int FunctionCacheHits,
    int ScriptCacheHits);
