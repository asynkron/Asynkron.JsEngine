#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Initializes the iterator for a <c>for...of</c> or <c>for await...of</c> loop.
/// </summary>
/// <param name="Kind">Whether this is a sync or async iterator.</param>
/// <param name="IterableExpression">Expression that produces the iterable.</param>
/// <param name="IteratorSlot">Symbol for the iterator state.</param>
/// <param name="IteratorSlotIndex">Pre-resolved slot index for fast iterator state access (-1 if not resolved).</param>
/// <param name="Next">Jump target after initialization.</param>
internal sealed record IteratorInitInstruction(
    IteratorDriverKind Kind,
    ExpressionNode IterableExpression,
    Symbol IteratorSlot,
    int IteratorSlotIndex,
    int Next)
    : GeneratorInstruction(Next);
