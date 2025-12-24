namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents a debugger statement in the generator.
///     Triggers a breakpoint if a debugger is attached.
/// </summary>
internal sealed record DebuggerInstruction(int Next) : GeneratorInstruction(Next);
