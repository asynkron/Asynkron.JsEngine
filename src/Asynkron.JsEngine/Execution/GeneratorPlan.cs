#region

using System.Collections.Immutable;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Intermediate representation for generator functions. The plan contains a flat list of instructions
///     that model sequential execution, branching, and yield points. The interpreter maintains a program counter
///     and executes the instructions synchronously, allowing .next/.throw/.return to resume exactly where the generator
///     paused.
/// </summary>
internal sealed record GeneratorPlan(
    ImmutableArray<GeneratorInstruction> Instructions,
    int EntryPoint);
