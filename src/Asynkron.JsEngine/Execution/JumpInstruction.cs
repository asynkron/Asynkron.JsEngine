namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents an unconditional jump to another instruction index.
/// </summary>
internal sealed record JumpInstruction(int TargetIndex) : GeneratorInstruction(TargetIndex);
