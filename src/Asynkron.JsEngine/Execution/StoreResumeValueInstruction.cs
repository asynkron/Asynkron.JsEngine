using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Stores the most recent <c>.next(value)</c> payload into a synthetic slot (or discards it) before execution
///     continues.
/// </summary>
internal sealed record StoreResumeValueInstruction(int Next, Symbol? TargetSymbol) : GeneratorInstruction(Next);
