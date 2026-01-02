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
        // Count hoisted lexical declarations (let/const/class at script level).
        // At execution time, these will occupy slots 0..N-1 before IR slots.
        // We need to offset IR slot indices to avoid collision.
        var hoistedSlotCount = CountHoistedLexicalDeclarations(program);

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
        // Pass isScriptLevel: true so var declarations will update the global object.
        // Pass hoistedSlotCount so IR slot indices start after hoisted bindings.
        if (ExecutionPlanBuilder.TryBuild(syntheticFunction, out var plan, out var failureReason,
            reportDiagnostics: false, isScriptLevel: true, initialSlotOffset: hoistedSlotCount))
        {
            return new ScriptPlanCache(plan, syntheticFunction, null);
        }

        return new ScriptPlanCache(null, null, failureReason ?? "Script contains unsupported construct for execution plan.");
    }

    /// <summary>
    /// Counts the number of top-level lexical declarations (let/const/class) that will be
    /// hoisted and occupy slots at execution time. This is used to offset IR slot indices
    /// to avoid collision with hoisted bindings.
    /// </summary>
    private static int CountHoistedLexicalDeclarations(ProgramNode program)
    {
        var count = 0;
        foreach (var statement in program.Body)
        {
            switch (statement)
            {
                case VariableDeclaration { Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing } lexDecl:
                    count += CountBindingTargets(lexDecl);
                    break;
                case ClassDeclaration:
                    count++;
                    break;
            }
        }
        return count;
    }

    /// <summary>
    /// Counts the number of individual bindings in a variable declaration.
    /// For simple declarations like 'const x = 1', this is 1.
    /// For destructuring like 'const {a, b} = obj', this counts each binding.
    /// </summary>
    private static int CountBindingTargets(VariableDeclaration decl)
    {
        var count = 0;
        foreach (var declarator in decl.Declarators)
        {
            count += CountBindingNames(declarator.Target);
        }
        return count;
    }

    private static int CountBindingNames(BindingTarget target)
    {
        return target switch
        {
            IdentifierBinding => 1,
            ArrayBinding arrayBinding => CountArrayBindingNames(arrayBinding),
            ObjectBinding objectBinding => CountObjectBindingNames(objectBinding),
            _ => 0
        };
    }

    private static int CountArrayBindingNames(ArrayBinding binding)
    {
        var count = 0;
        foreach (var element in binding.Elements)
        {
            if (element.Target is not null)
            {
                count += CountBindingNames(element.Target);
            }
        }
        if (binding.RestElement is not null)
        {
            count += CountBindingNames(binding.RestElement);
        }
        return count;
    }

    private static int CountObjectBindingNames(ObjectBinding binding)
    {
        var count = 0;
        foreach (var prop in binding.Properties)
        {
            count += CountBindingNames(prop.Target);
        }
        if (binding.RestElement is not null)
        {
            count += CountBindingNames(binding.RestElement);
        }
        return count;
    }
}
