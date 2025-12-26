



namespace Asynkron.JsParser;

/// <summary>
///     Represents the "this" keyword.
/// </summary>
public sealed record ThisExpression(SourceReference? Source) : ExpressionNode(Source);
