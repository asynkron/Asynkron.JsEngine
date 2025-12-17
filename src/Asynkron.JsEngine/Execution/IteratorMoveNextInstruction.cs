using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Advances the iterator for a <c>for...of</c> or <c>for await...of</c> loop.
/// </summary>
internal sealed record IteratorMoveNextInstruction(
    IteratorDriverKind Kind,
    Symbol IteratorSlot,
    Symbol ValueSlot,
    int BreakIndex,
    int Next)
    : GeneratorInstruction(Next);
