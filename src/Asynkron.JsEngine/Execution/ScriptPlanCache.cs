#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Caches the execution plan for a script (ProgramNode).
/// Similar to ExecutionPlanCache but creates a synthetic function from the script body.
/// </summary>
internal sealed class ScriptPlanCache
{
    private ScriptPlanCache(ExecutionPlan? plan, FunctionExpression? syntheticFunction, string? failureReason)
    {
        Plan = plan;
        SyntheticFunction = syntheticFunction;
        FailureReason = failureReason;
    }

    public ExecutionPlan? Plan { get; }

    /// <summary>
    /// The synthetic function expression that wraps the script body.
    /// Needed for ExecutionPlanRunner which requires a FunctionExpression.
    /// </summary>
    public FunctionExpression? SyntheticFunction { get; }

    public string? FailureReason { get; }

    public bool Succeeded => Plan is not null;

    public static ScriptPlanCache Build(ProgramNode program)
    {
        // Create a synthetic function expression that wraps the script body.
        // This allows us to reuse the existing ExecutionPlanBuilder infrastructure.
        var syntheticFunction = new FunctionExpression(
            program.Source,
            Name: null, // Scripts don't have a function name
            Parameters: [], // Scripts don't have parameters
            Body: new BlockStatement(program.Source, program.Body, program.IsStrict),
            IsAsync: program.HasTopLevelAwait, // Top-level await makes it async-like
            IsGenerator: false,
            IsArrow: false,
            WasAsync: false,
            IsHoistableDefaultExport: false,
            IsDefaultDerivedConstructor: false,
            SlotCount: program.SlotCount,
            ScopeId: program.ScopeId,
            HasClosures: false // Will be determined by analysis
        );

        // Don't report diagnostics for script-level IR builds - these are separate from
        // function IR builds that tests track via ExecutionPlanDiagnostics.
        if (ExecutionPlanBuilder.TryBuild(syntheticFunction, out var plan, out var failureReason, reportDiagnostics: false))
        {
            return new ScriptPlanCache(plan, syntheticFunction, null);
        }

        return new ScriptPlanCache(null, null, failureReason ?? "Script contains unsupported construct for execution plan.");
    }
}
