using System.Collections.Immutable;

namespace Asynkron.JsParser;

/// <summary>
///     Represents a classic C-style for loop.
/// </summary>
public sealed record ForStatement(
    SourceReference? Source,
    StatementNode? Initializer,
    ExpressionNode? Condition,
    ExpressionNode? Increment,
    StatementNode Body,
    int PerIterationScopeId = -1,
    int PerIterationSlotCount = -1,
    ImmutableArray<int> PerIterationSlotIndices = default) : StatementNode(Source);
