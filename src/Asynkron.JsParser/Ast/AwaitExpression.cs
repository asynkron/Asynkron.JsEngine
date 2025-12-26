namespace Asynkron.JsParser;

/// <summary>
///     Represents an await expression.
/// </summary>
public sealed record AwaitExpression(SourceReference? Source, ExpressionNode Expression)
    : ExpressionNode(Source);
