
using System.Collections.Immutable;



namespace Asynkron.JsParser;

/// <summary>
///     Represents a template literal expression.
/// </summary>
public sealed record TemplateLiteralExpression(SourceReference? Source, ImmutableArray<TemplatePart> Parts)
    : ExpressionNode(Source);
