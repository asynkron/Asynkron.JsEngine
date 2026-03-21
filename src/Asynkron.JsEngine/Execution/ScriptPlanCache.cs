#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Caches the execution plan for a script (ProgramNode).
/// Similar to ExecutionPlanCache but creates a synthetic function from the script body.
/// </summary>
internal sealed class ScriptPlanCache
{
    private ScriptPlanCache(ExecutionPlanBuildResult result, FunctionExpression? syntheticFunction)
    {
        Plan = result.Plan;
        SyntheticFunction = syntheticFunction;
        Failure = result.Failure;
    }

    public ExecutionPlan? Plan { get; }

    /// <summary>
    /// The synthetic function expression that wraps the script body.
    /// Needed for ExecutionPlanRunner which requires a FunctionExpression.
    /// </summary>
    public FunctionExpression? SyntheticFunction { get; }

    public ExecutionPlanBuildFailure? Failure { get; }

    public string? FailureReason => Failure?.Detail;

    public bool Succeeded => Plan is not null;

    public static ScriptPlanCache Build(ProgramNode program)
    {
        // Create a synthetic function expression that wraps the script body.
        // This allows us to reuse the existing ExecutionPlanBuilder infrastructure.
        var syntheticFunction = new FunctionExpression(
            program.Source,
            null, // Scripts don't have a function name
            [], // Scripts don't have parameters
            new BlockStatement(program.Source, program.Body, program.IsStrict),
            program.HasTopLevelAwait, // Top-level await makes it async-like
            false,
            false,
            false,
            false,
            false,
            program.SlotCount,
            program.ScopeId,
            false // Will be determined by analysis
        );

        // Script builds are tracked separately from function builds so cache reads do not
        // inflate the function-only counters that older tests still assert on.
        var result = ExecutionPlanBuilder.Build(syntheticFunction, false, true);
        ExecutionPlanDiagnostics.ReportScriptResult(program, result);

        if (result.Succeeded)
        {
            return new ScriptPlanCache(result, syntheticFunction);
        }

        var failure = result.Failure ?? new ExecutionPlanBuildFailure(
            ExecutionPlanFailureCode.UnsupportedConstruct,
            "Script contains unsupported construct for execution plan.");
        return new ScriptPlanCache(
            ExecutionPlanBuildResult.FailureResult(failure.Code, failure.Detail),
            null);
    }
}
