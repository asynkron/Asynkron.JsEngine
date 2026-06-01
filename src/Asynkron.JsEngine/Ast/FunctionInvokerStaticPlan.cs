
using System.Collections.Immutable;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Caches static analysis results for a function that are computed once per FunctionExpression
/// and reused on every invocation, avoiding repeated AST traversals in SyncFunctionInvoker..ctor
/// and ExecutionPlanRunner.CreateExecutionEnvironment.
/// </summary>
internal sealed class FunctionInvokerStaticPlan
{
    private FunctionInvokerStaticPlan(
        bool hasFunctionDeclarationParameterConflict,
        bool hasNonParameterCalleeCall,
        bool hasInnerFunctionExpression,
        bool usesArguments,
        bool needsArgumentsBinding)
    {
        HasFunctionDeclarationParameterConflict = hasFunctionDeclarationParameterConflict;
        HasNonParameterCalleeCall = hasNonParameterCalleeCall;
        HasInnerFunctionExpression = hasInnerFunctionExpression;
        UsesArguments = usesArguments;
        NeedsArgumentsBinding = needsArgumentsBinding;
    }

    internal bool HasFunctionDeclarationParameterConflict { get; }
    internal bool HasNonParameterCalleeCall { get; }
    internal bool HasInnerFunctionExpression { get; }
    /// <summary>True when the function body references the 'arguments' identifier (not crossing inner-function boundaries).</summary>
    internal bool UsesArguments { get; }
    /// <summary>True when an arguments object must be created eagerly because it is observable.</summary>
    internal bool NeedsArgumentsBinding { get; }

    internal static FunctionInvokerStaticPlan Build(FunctionExpression function)
    {
        var parameterNames = ((IAstCacheable<FunctionParameterNamesPlan>)function).GetOrCreateCache().ParameterNames;

        var parameterNameSet = BuildParameterNameSet(parameterNames);

        var hasFuncDeclConflict = TypedAstEvaluator.ContainsFunctionDeclarationParameterConflict(function, parameterNameSet);
        var hasNonParamCallee = TypedAstEvaluator.ContainsNonParameterCalleeIdentifier(function, parameterNameSet);
        var hasInnerFunc = TypedAstEvaluator.ContainsInnerFunctionExpression(function);
        var usesArguments = TypedAstEvaluator.UsesArgumentsIdentifier(function);
        var needsArgumentsBinding = TypedAstEvaluator.NeedsArgumentsBinding(function);

        return new FunctionInvokerStaticPlan(
            hasFuncDeclConflict,
            hasNonParamCallee,
            hasInnerFunc,
            usesArguments,
            needsArgumentsBinding);
    }

    private static HashSet<Symbol> BuildParameterNameSet(ImmutableArray<Symbol> parameterNames)
    {
        var set = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        foreach (var name in parameterNames)
        {
            set.Add(name);
        }

        set.Add(Symbol.Arguments);
        return set;
    }
}
