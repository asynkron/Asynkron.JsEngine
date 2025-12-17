namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Marks the end of a <c>finally</c> block so pending completions can resume.
/// </summary>
internal sealed record EndFinallyInstruction(int Next) : GeneratorInstruction(Next);
