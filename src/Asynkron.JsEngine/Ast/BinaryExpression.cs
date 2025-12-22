#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a binary expression such as a + b.
/// </summary>
public sealed record BinaryExpression(
    SourceReference? Source,
    BinaryOperator Operator,
    ExpressionNode Left,
    ExpressionNode Right) : ExpressionNode(Source);
