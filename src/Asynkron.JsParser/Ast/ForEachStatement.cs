using System.Collections.Immutable;

namespace Asynkron.JsParser;

/// <summary>
///     Represents for...in / for...of / for await...of loops.
/// </summary>
public sealed record ForEachStatement(
    SourceReference? Source,
    BindingTarget Target,
    ExpressionNode Iterable,
    StatementNode Body,
    ForEachKind Kind,
    VariableKind? DeclarationKind,
    int PerIterationScopeId = -1,
    int PerIterationSlotCount = -1,
    ImmutableArray<int> PerIterationSlotIndices = default,
    ImmutableArray<Symbol> PerIterationBindings = default) : StatementNode(Source);
