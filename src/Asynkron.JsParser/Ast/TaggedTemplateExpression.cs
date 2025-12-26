
using System.Collections.Immutable;



namespace Asynkron.JsParser;

/// <summary>
///     Represents a tagged template literal expression.
/// </summary>
public sealed record TaggedTemplateExpression(
    SourceReference? Source,
    ExpressionNode Tag,
    ExpressionNode StringsArray,
    ExpressionNode RawStringsArray,
    ImmutableArray<ExpressionNode> Expressions)
    : ExpressionNode(Source);
