namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents a <c>continue</c> statement.
///     Pops environments until reaching TargetScopeId before jumping.
/// </summary>
/// <param name="TargetIndex">The instruction index to jump to.</param>
/// <param name="TargetScopeId">The scope ID to pop to before jumping.</param>
internal sealed record ContinueInstruction(
    int TargetIndex,
    int TargetScopeId = -1) : ExecutionInstruction(TargetIndex);
