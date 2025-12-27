namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Base class for all execution plan instructions.
/// </summary>
internal abstract record ExecutionInstruction(int Next);
