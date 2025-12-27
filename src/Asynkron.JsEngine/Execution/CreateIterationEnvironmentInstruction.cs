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
/// <param name="Next">Next instruction index.</param>
/// <param name="PerIterationBindings">Symbols that need per-iteration copying.</param>
/// <param name="ScopeId">The scope ID for this loop's iteration environments.</param>
/// <param name="SlotCount">Number of slots in the iteration environment.</param>
/// <param name="SlotMap">Mapping from symbols to slot indices.</param>
/// <param name="ParentScopeId">
///     The scope ID of the parent scope where iteration environments should be parented.
///     Use -1 to indicate the current environment should be used as parent.
///     This prevents scope chain corruption in nested loops where the current environment
///     may be from an inner loop.
/// </param>
/// <param name="AllowPooling">Whether environment pooling is allowed (no closures in body).</param>
internal sealed record CreateIterationEnvironmentInstruction(
    int Next,
    ImmutableArray<Symbol> PerIterationBindings,
    int ScopeId,
    int SlotCount,
    ImmutableDictionary<Symbol, int> SlotMap,
    int ParentScopeId = -1,
    bool AllowPooling = false) : ExecutionInstruction(Next);
