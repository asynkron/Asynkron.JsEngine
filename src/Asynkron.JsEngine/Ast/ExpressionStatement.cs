#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an expression statement.
/// </summary>
/// <param name="SuppressCompletionValue">
///     When true, this statement's result does not contribute to the script completion value.
///     Used for synthetic/internal assignments (e.g., switch lowering's __done flag).
/// </param>
public sealed record ExpressionStatement(
    SourceReference? Source,
    ExpressionNode Expression,
    bool SuppressCompletionValue = false) : StatementNode(Source);
