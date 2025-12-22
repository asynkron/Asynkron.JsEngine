#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents a delegated <c>yield*</c> expression that iterates another iterable.
/// </summary>
internal sealed record YieldStarInstruction(
    int Next,
    ExpressionNode IterableExpression,
    Symbol StateSlotSymbol,
    Symbol? ResultSlotSymbol) : GeneratorInstruction(Next);
