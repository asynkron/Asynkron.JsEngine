#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Increments or decrements a variable stored in a slot directly, without
///     going through the generic UnaryExpression AST evaluator.
/// </summary>
/// <remarks>
///     This instruction provides a fast path for <c>i++</c>, <c>++i</c>, <c>i--</c>,
///     and <c>--i</c> operations on simple identifiers in generators/async functions.
///     For the common case of loop counters, this avoids identifier lookup and
///     ToNumber conversion overhead when the value is already a number.
/// </remarks>
internal sealed record IncrementSlotInstruction(
    int Next,
    Symbol TargetSymbol,
    bool IsIncrement,
    bool IsPrefix) : ExecutionInstruction(Next);
