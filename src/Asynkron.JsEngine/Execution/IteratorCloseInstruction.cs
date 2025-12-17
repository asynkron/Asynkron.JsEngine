using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Closes an iterator stored in the given slot. Used in finally blocks for for-of loops.
/// </summary>
internal sealed record IteratorCloseInstruction(
    Symbol IteratorSlot,
    int Next)
    : GeneratorInstruction(Next);
