using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

internal sealed class ExecutionPlanCache
{
    private ExecutionPlanCache(ExecutionPlanBuildResult result)
    {
        Plan = result.Plan;
        Failure = result.Failure;
    }

    public ExecutionPlan? Plan { get; }

    public ExecutionPlanBuildFailure? Failure { get; }

    public string? FailureReason => Failure?.Detail;

    public bool Succeeded => Plan is not null;

    internal static ExecutionPlanCache FromPlan(ExecutionPlan plan)
    {
        return new ExecutionPlanCache(ExecutionPlanBuildResult.Success(plan));
    }

    public static ExecutionPlanCache Build(FunctionExpression function, bool reportDiagnostics = true)
    {
        return new ExecutionPlanCache(ExecutionPlanBuilder.Build(function, reportDiagnostics));
    }
}
