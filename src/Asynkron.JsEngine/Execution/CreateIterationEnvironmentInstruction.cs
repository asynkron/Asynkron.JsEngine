#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Creates a fresh environment for each loop iteration when using let/const bindings.
///     This ensures closures created in the loop body capture separate values per iteration.
///     For example: for (let x of [1,2,3]) { funcs.push(() => x); } - each closure gets its own x.
/// </summary>
internal sealed record CreateIterationEnvironmentInstruction(
    int Next,
    ImmutableArray<Symbol> PerIterationBindings,
    int ScopeId,
    int SlotCount,
    ImmutableDictionary<Symbol, int> SlotMap) : GeneratorInstruction(Next);
