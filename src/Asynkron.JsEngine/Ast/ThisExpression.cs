using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents the "this" keyword.
/// </summary>
public sealed record ThisExpression(SourceReference? Source) : ExpressionNode(Source);
