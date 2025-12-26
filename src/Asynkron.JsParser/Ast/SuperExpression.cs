



namespace Asynkron.JsParser;

/// <summary>
///     Represents the "super" keyword.
/// </summary>
public sealed record SuperExpression(SourceReference? Source) : ExpressionNode(Source);
