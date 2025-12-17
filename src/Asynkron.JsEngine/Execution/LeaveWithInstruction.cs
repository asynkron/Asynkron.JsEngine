using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Marks the end of a <c>with</c> statement. Pops the with-environment from the scope chain.
/// </summary>
internal sealed record LeaveWithInstruction(
    Symbol WithScopeSlot,
    int Next)
    : GeneratorInstruction(Next);
