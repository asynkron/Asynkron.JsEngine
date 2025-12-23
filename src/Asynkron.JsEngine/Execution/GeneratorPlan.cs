#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Intermediate representation for generator functions. The plan contains a flat list of instructions
///     that model sequential execution, branching, and yield points. The interpreter maintains a program counter
///     and executes the instructions synchronously, allowing .next/.throw/.return to resume exactly where the generator
///     paused.
/// </summary>
/// <param name="Instructions">The instruction sequence.</param>
/// <param name="EntryPoint">Index of the first instruction to execute.</param>
/// <param name="SlotCount">Number of slots to allocate for generator-internal variables (iterator states, values, etc.).</param>
/// <param name="SlotSymbols">Symbols mapped to slot indices for O(1) variable access.</param>
internal sealed record GeneratorPlan(
    ImmutableArray<GeneratorInstruction> Instructions,
    int EntryPoint,
    int SlotCount = 0,
    ImmutableArray<Symbol> SlotSymbols = default);
