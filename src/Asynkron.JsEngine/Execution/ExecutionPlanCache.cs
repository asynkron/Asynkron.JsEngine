#region

using Asynkron.JsEngine.Ast;

#endregion

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

    public static ExecutionPlanCache Build(FunctionExpression function, bool reportDiagnostics = true)
    {
        return new ExecutionPlanCache(ExecutionPlanBuilder.Build(function, reportDiagnostics));
    }
}
