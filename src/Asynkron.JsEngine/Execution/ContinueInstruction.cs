namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents a <c>continue</c> statement.
/// </summary>
internal sealed record ContinueInstruction(int TargetIndex) : ExecutionInstruction(TargetIndex);
