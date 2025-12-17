using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Initializes the iterator for a <c>for...of</c> or <c>for await...of</c> loop.
/// </summary>
internal sealed record IteratorInitInstruction(
    IteratorDriverKind Kind,
    ExpressionNode IterableExpression,
    Symbol IteratorSlot,
    int Next)
    : GeneratorInstruction(Next);
