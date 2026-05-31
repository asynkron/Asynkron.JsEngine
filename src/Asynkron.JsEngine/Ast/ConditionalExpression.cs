using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a conditional (ternary) expression.
/// </summary>
public sealed record ConditionalExpression(
    SourceReference? Source,
    ExpressionNode Test,
    ExpressionNode Consequent,
    ExpressionNode Alternate) : ExpressionNode(Source);
