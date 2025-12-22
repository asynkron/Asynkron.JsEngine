#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a return statement.
/// </summary>
/// <param name="Source">Source reference.</param>
/// <param name="Expression">The return expression, or null for bare return.</param>
/// <param name="UseArgumentPreEvaluation">When true, the return expression contains multiple calls
/// that can benefit from pre-evaluating all arguments before making any calls. This enables
/// environment reuse for ALL calls, not just the rightmost one.</param>
public sealed record ReturnStatement(
    SourceReference? Source,
    ExpressionNode? Expression,
    bool UseArgumentPreEvaluation = false) : StatementNode(Source);
