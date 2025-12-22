#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Base type for expressions.
/// </summary>
public abstract record ExpressionNode(SourceReference? Source) : AstNode(Source);
