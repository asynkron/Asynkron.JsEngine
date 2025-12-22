#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

internal sealed class GeneratorPlanCache
{
    private GeneratorPlanCache(GeneratorPlan? plan, string? failureReason)
    {
        Plan = plan;
        FailureReason = failureReason;
    }

    public GeneratorPlan? Plan { get; }

    public string? FailureReason { get; }

    public bool Succeeded => Plan is not null;

    public static GeneratorPlanCache Build(FunctionExpression function, bool reportDiagnostics = true)
    {
        if (GeneratorIrBuilder.TryBuild(function, out var plan, out var failureReason, reportDiagnostics))
        {
            return new GeneratorPlanCache(plan, null);
        }

        return new GeneratorPlanCache(null, failureReason ?? "Generator contains unsupported construct for IR.");
    }
}
