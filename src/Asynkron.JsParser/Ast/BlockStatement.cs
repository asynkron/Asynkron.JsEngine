using System.Collections.Immutable;

namespace Asynkron.JsParser;

/// <summary>
///     Represents a block statement with optional strict mode.
/// </summary>
public sealed record BlockStatement(SourceReference? Source, ImmutableArray<StatementNode> Statements, bool IsStrict)
    : StatementNode(Source)
{
    internal int ScopeId { get; init; } = -1;
    internal int SlotCount { get; init; } = -1;

    internal ImmutableDictionary<Symbol, int> SlotMap { get; init; } =
        ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);
}
