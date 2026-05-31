using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a sequence expression (comma operator).
/// </summary>
public sealed record SequenceExpression(SourceReference? Source, ExpressionNode Left, ExpressionNode Right)
    : ExpressionNode(Source);
