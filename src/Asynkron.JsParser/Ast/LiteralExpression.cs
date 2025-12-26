namespace Asynkron.JsParser;

/// <summary>
///     Represents a literal (number, string, boolean, null, undefined, BigInt).
/// </summary>
public sealed record LiteralExpression(SourceReference? Source, LiteralValue Value) : ExpressionNode(Source);
