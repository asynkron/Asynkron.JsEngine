#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Evaluates a binary operation directly without going through the generic
///     BinaryExpression AST evaluator. Stores the result in the specified slot
///     (if provided) or discards it.
/// </summary>
/// <remarks>
///     This instruction provides a fast path for common arithmetic and comparison
///     operations in generators/async functions by avoiding the AST dispatch overhead.
///     Only non-short-circuiting operators are supported (logical operators still
///     need BranchInstruction for correct semantics).
/// </remarks>
internal sealed record BinaryOpInstruction(
    int Next,
    BinaryOperator Operator,
    ExpressionNode Left,
    ExpressionNode Right,
    Symbol? ResultSlot = null) : ExecutionInstruction(Next);
