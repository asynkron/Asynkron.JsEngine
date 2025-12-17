namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Marks normal completion of a <c>try</c> or <c>catch</c> block.
/// </summary>
internal sealed record LeaveTryInstruction(int Next) : GeneratorInstruction(Next);
