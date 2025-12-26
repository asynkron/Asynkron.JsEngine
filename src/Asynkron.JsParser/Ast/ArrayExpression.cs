
using System.Collections.Immutable;



namespace Asynkron.JsParser;

/// <summary>
///     Represents an array literal.
/// </summary>
public sealed record ArrayExpression(SourceReference? Source, ImmutableArray<ArrayElement> Elements)
    : ExpressionNode(Source);
