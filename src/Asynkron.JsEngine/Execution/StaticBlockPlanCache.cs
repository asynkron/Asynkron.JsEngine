#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Caches the execution plan for a class static block.
/// The block lowers through a synthetic parameterless function so the existing
/// function-body IR builder can reuse the block's scope metadata.
/// </summary>
internal sealed class StaticBlockPlanCache
{
    private StaticBlockPlanCache(ExecutionPlanBuildResult result)
    {
        Plan = result.Plan;
        Failure = result.Failure;
    }

    public ExecutionPlan? Plan { get; }

    public ExecutionPlanBuildFailure? Failure { get; }

    public string? FailureReason => Failure?.Detail;

    public bool Succeeded => Plan is not null;

    public static StaticBlockPlanCache Build(ClassStaticBlock block)
    {
        var body = block.Body;
        var syntheticFunction = new FunctionExpression(
            block.Source,
            null,
            ImmutableArray<FunctionParameter>.Empty,
            body,
            false,
            false,
            SlotCount: body.SlotCount,
            ScopeId: body.ScopeId,
            HasClosures: TypedAstEvaluator.ContainsInnerFunctionExpression(body));

        return new StaticBlockPlanCache(
            ExecutionPlanBuilder.Build(syntheticFunction, reportDiagnostics: false));
    }
}
