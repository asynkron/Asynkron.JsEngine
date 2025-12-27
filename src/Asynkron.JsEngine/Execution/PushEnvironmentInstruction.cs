#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Pushes a new environment onto the scope stack.
///     Used for block scopes, loop iterations, and other lexical scopes.
///     For loop iterations, this ensures closures capture separate values per iteration.
/// </summary>
/// <param name="Next">Next instruction index.</param>
/// <param name="PerIterationBindings">
///     For loop iterations: symbols that need copying from previous iteration.
///     Empty for regular block scopes.
/// </param>
/// <param name="ScopeId">The scope ID for this environment.</param>
/// <param name="SlotCount">Number of slots in the environment.</param>
/// <param name="SlotMap">Mapping from symbols to slot indices.</param>
/// <param name="AllowPooling">Whether environment pooling is allowed (no closures capture this env).</param>
internal sealed record PushEnvironmentInstruction(
    int Next,
    ImmutableArray<Symbol> PerIterationBindings,
    int ScopeId,
    int SlotCount,
    ImmutableDictionary<Symbol, int> SlotMap,
    bool AllowPooling = false) : ExecutionInstruction(Next);
