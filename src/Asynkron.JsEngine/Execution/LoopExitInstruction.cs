namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Marks exit from a loop. Pops the loop context from the loop stack.
/// </summary>
/// <param name="Next">The next instruction index after the loop.</param>
internal sealed record LoopExitInstruction(int Next) : ExecutionInstruction(Next);
