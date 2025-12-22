#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Placeholder expression node for decorator syntax. Semantics are not yet implemented.
/// </summary>
public sealed record DecoratorExpression(SourceReference? Source, ExpressionNode Expression) : ExpressionNode(Source);
