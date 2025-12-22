#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents the "super" keyword.
/// </summary>
public sealed record SuperExpression(SourceReference? Source) : ExpressionNode(Source);
