using System.Collections.Immutable;

namespace Asynkron.JsParser;

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
public sealed record FunctionExpression(
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
    : ExpressionNode(Source)
{
    internal ImmutableDictionary<Symbol, int> SlotMap { get; init; } =
        ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);
}
