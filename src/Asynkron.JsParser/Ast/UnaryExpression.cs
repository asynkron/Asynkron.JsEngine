



namespace Asynkron.JsParser;

/// <summary>
///     Represents a unary expression such as -a or !a.
/// </summary>
public sealed record UnaryExpression(
    SourceReference? Source,
    UnaryOperator Operator,
    ExpressionNode Operand,
    bool IsPrefix)
    : ExpressionNode(Source);
