#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Intermediate representation for pauseable functions (generators, async functions, async generators).
///     The plan contains a flat list of instructions that model sequential execution, branching, and yield/await points.
///     The interpreter maintains a program counter and executes the instructions synchronously, allowing
///     .next/.throw/.return to resume exactly where execution paused.
/// </summary>
/// <param name="Instructions">The instruction sequence.</param>
/// <param name="EntryPoint">Index of the first instruction to execute.</param>
/// <param name="SlotCount">Number of slots to allocate for internal variables (iterator states, values, etc.).</param>
/// <param name="SlotSymbols">Symbols mapped to slot indices for O(1) variable access.</param>
/// <param name="RootSlotCount">Slot count required for the root (function) scope user bindings.</param>
/// <param name="RootSlotMap">Slot map for the root (function) scope user bindings.</param>
/// <param name="RootLexicalBindings">Lexical bindings in the root scope (for TDZ).</param>
/// <param name="ScopeLexicalBindings">Lexical bindings per scope id.</param>
internal sealed record ExecutionPlan(
    ImmutableArray<ExecutionInstruction> Instructions,
    int EntryPoint,
    int SlotCount = 0,
    ImmutableArray<Symbol> SlotSymbols = default,
    int RootSlotCount = 0,
    ImmutableDictionary<Symbol, int>? RootSlotMap = null,
    ImmutableHashSet<Symbol>? RootLexicalBindings = null,
    ImmutableDictionary<int, ImmutableHashSet<Symbol>>? ScopeLexicalBindings = null)
{
    public ImmutableDictionary<Symbol, int> SafeRootSlotMap =>
        RootSlotMap ?? ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);

    public ImmutableHashSet<Symbol> SafeRootLexicalBindings =>
        RootLexicalBindings ?? ImmutableHashSet<Symbol>.Empty.WithComparer(ReferenceEqualityComparer<Symbol>.Instance);

    public ImmutableDictionary<int, ImmutableHashSet<Symbol>> SafeScopeLexicalBindings =>
        ScopeLexicalBindings ?? ImmutableDictionary<int, ImmutableHashSet<Symbol>>.Empty;
}
