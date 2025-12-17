namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents a <c>break</c> statement.
/// </summary>
internal sealed record BreakInstruction(int TargetIndex) : GeneratorInstruction(TargetIndex);
