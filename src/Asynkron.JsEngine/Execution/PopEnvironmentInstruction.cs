namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Pops an environment from the scope stack.
///     If the current environment's ScopeId matches, sets environment = environment.Enclosing.
///     If ScopeId doesn't match (scope was never entered, e.g., loop ran 0 times), this is a no-op.
/// </summary>
/// <param name="ScopeId">The scope ID to pop. Only pops if current env matches.</param>
/// <param name="AllowPooling">Whether to return the popped environment to pool.</param>
/// <param name="Next">Next instruction index.</param>
internal sealed record PopEnvironmentInstruction(
    int ScopeId,
    bool AllowPooling,
    int Next) : ExecutionInstruction(Next);
