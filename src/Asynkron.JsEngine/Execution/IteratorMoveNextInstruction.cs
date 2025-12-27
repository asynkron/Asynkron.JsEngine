#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Advances the iterator for a <c>for...of</c> or <c>for await...of</c> loop.
/// </summary>
/// <param name="Kind">Whether this is a sync or async iterator.</param>
/// <param name="IteratorSlot">Symbol for the iterator state.</param>
/// <param name="ValueSlot">Symbol for the current iteration value.</param>
/// <param name="IteratorSlotIndex">Pre-resolved slot index for fast iterator state access (-1 if not resolved).</param>
/// <param name="ValueSlotIndex">Pre-resolved slot index for fast value access (-1 if not resolved).</param>
/// <param name="BreakIndex">Jump target when iteration completes.</param>
/// <param name="Next">Jump target for the loop body.</param>
internal sealed record IteratorMoveNextInstruction(
    IteratorDriverKind Kind,
    Symbol IteratorSlot,
    Symbol ValueSlot,
    int IteratorSlotIndex,
    int ValueSlotIndex,
    int BreakIndex,
    int Next)
    : ExecutionInstruction(Next);
