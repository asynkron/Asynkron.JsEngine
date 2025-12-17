using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Base type for expressions.
/// </summary>
public abstract record ExpressionNode(SourceReference? Source) : AstNode(Source);
