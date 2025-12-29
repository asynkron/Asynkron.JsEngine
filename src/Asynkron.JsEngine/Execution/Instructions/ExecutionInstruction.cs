namespace Asynkron.JsEngine.Execution.Instructions;

/// <summary>
///     Base class for all execution plan instructions.
///     The Kind property enables fast enum-based dispatch instead of
///     runtime type pattern matching.
/// </summary>
internal abstract record ExecutionInstruction(InstructionKind Kind, int Next);
