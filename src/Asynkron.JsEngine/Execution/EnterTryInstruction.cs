using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Marks the beginning of a <c>try</c> region.
/// </summary>
internal sealed record EnterTryInstruction(int Next, int HandlerIndex, Symbol? CatchSlotSymbol, int FinallyIndex)
    : GeneratorInstruction(Next);
