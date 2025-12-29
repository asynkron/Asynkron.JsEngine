#region

using System.Collections.Immutable;
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
/// <param name="TdzBindings">
///     Symbols that need TDZ bindings during iterable evaluation (for let/const declarations).
///     When non-empty, a TDZ environment is created before evaluating the iterable expression.
///     This ensures `for (const x of [x])` throws ReferenceError for accessing x before initialization.
/// </param>
/// <param name="TdzIsConst">Whether the TDZ bindings are const (true) or let (false).</param>
internal sealed record IteratorInitInstruction(
    IteratorDriverKind Kind,
    ExpressionNode IterableExpression,
    Symbol IteratorSlot,
    int IteratorSlotIndex,
    int Next,
    ImmutableArray<Symbol> TdzBindings = default,
    bool TdzIsConst = false)
    : ExecutionInstruction(Next);
