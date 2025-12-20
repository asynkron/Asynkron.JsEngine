using System.Collections.Immutable;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a function or generator expression.
/// </summary>
/// <param name="SlotCount">Number of slots needed for local variables in this function's scope.
/// Set by ScopeAnalyzer for O(1) variable access. -1 means not analyzed.</param>
/// <param name="ScopeId">Unique ID for the scope created by this function. -1 means not analyzed.</param>
/// <param name="HasClosures">True if any inner functions capture variables from this function's scope.
/// When true, environment reuse optimization is disabled for calls within this function.</param>
/// <param name="FunctionNameScopeId">For named function expressions, the scope ID of the intermediate scope
/// that contains the function name binding. -1 if not a named function expression.</param>
public sealed partial record FunctionExpression(
    SourceReference? Source,
    Symbol? Name,
    ImmutableArray<FunctionParameter> Parameters,
    BlockStatement Body,
    bool IsAsync,
    bool IsGenerator,
    bool IsArrow = false,
    bool WasAsync = false,
    bool IsHoistableDefaultExport = false,
    bool IsDefaultDerivedConstructor = false,
    int SlotCount = -1,
    int ScopeId = -1,
    bool HasClosures = false,
    int FunctionNameScopeId = -1)
    : ExpressionNode(Source),
        IAstCacheable<FunctionParameterNamesPlan>,
        IAstCacheable<GeneratorPlanCache>;

public sealed partial record FunctionExpression
{
    private FunctionParameterNamesPlan? _cachedParameterNames;
    private GeneratorPlanCache? _cachedGeneratorPlan;

    internal ImmutableDictionary<Symbol, int> SlotMap { get; init; } =
        ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);

    FunctionParameterNamesPlan IAstCacheable<FunctionParameterNamesPlan>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(ref _cachedParameterNames, this, static function => FunctionParameterNamesPlan.Build(function));
    }

    GeneratorPlanCache IAstCacheable<GeneratorPlanCache>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(ref _cachedGeneratorPlan, this, static function => GeneratorPlanCache.Build(function));
    }
}
