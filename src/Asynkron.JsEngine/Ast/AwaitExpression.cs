using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an await expression.
/// </summary>
public sealed record AwaitExpression(SourceReference? Source, ExpressionNode Expression) : ExpressionNode(Source);
