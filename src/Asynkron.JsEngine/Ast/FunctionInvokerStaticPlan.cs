
using System.Collections.Immutable;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Caches static analysis results for a function that are computed once per FunctionExpression
/// and reused on every invocation, avoiding repeated AST traversals in SyncFunctionInvoker..ctor.
/// </summary>
internal sealed class FunctionInvokerStaticPlan
{
    private FunctionInvokerStaticPlan(
        bool hasParameterVarDeclarationWithoutInitializer,
        bool hasFunctionDeclarationParameterConflict,
        bool hasNonParameterCalleeCall,
        bool hasInnerFunctionExpression)
    {
        HasParameterVarDeclarationWithoutInitializer = hasParameterVarDeclarationWithoutInitializer;
        HasFunctionDeclarationParameterConflict = hasFunctionDeclarationParameterConflict;
        HasNonParameterCalleeCall = hasNonParameterCalleeCall;
        HasInnerFunctionExpression = hasInnerFunctionExpression;
    }

    internal bool HasParameterVarDeclarationWithoutInitializer { get; }
    internal bool HasFunctionDeclarationParameterConflict { get; }
    internal bool HasNonParameterCalleeCall { get; }
    internal bool HasInnerFunctionExpression { get; }

    internal static FunctionInvokerStaticPlan Build(FunctionExpression function)
    {
        var parameterNames = ((IAstCacheable<FunctionParameterNamesPlan>)function).GetOrCreateCache().ParameterNames;

        var parameterNameSet = BuildParameterNameSet(parameterNames);

        var hasParamVarDecl = TypedAstEvaluator.ContainsParameterVarDeclarationWithoutInitializer(function, parameterNames);
        var hasFuncDeclConflict = TypedAstEvaluator.ContainsFunctionDeclarationParameterConflict(function, parameterNameSet);
        var hasNonParamCallee = TypedAstEvaluator.ContainsNonParameterCalleeIdentifier(function, parameterNameSet);
        var hasInnerFunc = TypedAstEvaluator.ContainsInnerFunctionExpression(function);

        return new FunctionInvokerStaticPlan(
            hasParamVarDecl,
            hasFuncDeclConflict,
            hasNonParamCallee,
            hasInnerFunc);
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
