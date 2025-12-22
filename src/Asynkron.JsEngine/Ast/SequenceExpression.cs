#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a sequence expression (comma operator).
/// </summary>
public sealed record SequenceExpression(SourceReference? Source, ExpressionNode Left, ExpressionNode Right)
    : ExpressionNode(Source);
