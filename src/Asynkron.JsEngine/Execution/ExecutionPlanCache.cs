#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

internal sealed class ExecutionPlanCache
{
    private ExecutionPlanCache(ExecutionPlan? plan, string? failureReason)
    {
        Plan = plan;
        FailureReason = failureReason;
    }

    public ExecutionPlan? Plan { get; }

    public string? FailureReason { get; }

    public bool Succeeded => Plan is not null;

    public static ExecutionPlanCache Build(FunctionExpression function, bool reportDiagnostics = true)
    {
        if (ExecutionPlanBuilder.TryBuild(function, out var plan, out var failureReason, reportDiagnostics))
        {
            return new ExecutionPlanCache(plan, null);
        }

        return new ExecutionPlanCache(null,
            failureReason ?? "Function contains unsupported construct for execution plan.");
    }
}
