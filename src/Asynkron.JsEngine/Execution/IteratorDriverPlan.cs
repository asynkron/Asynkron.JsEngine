#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Plan for executing for-of/for-in iteration loops.
/// Cached per AST node to enable environment reuse across executions.
/// </summary>
internal sealed class IteratorDriverPlan(
    IteratorDriverKind Kind,
    ExpressionNode Iterable,
    BindingTarget Target,
    VariableKind? DeclarationKind,
    BlockStatement Body,
    int IterationScopeId = -1,
    int IterationSlotCount = -1,
    ImmutableArray<int> PerIterationSlotIndices = default,
    ImmutableArray<Symbol> PerIterationBindings = default,
    bool CanReuseIterationEnvironment = false,
    bool CanPoolLoopEnvironment = false,
    int LoopEnvSlotIndex = -1,
    int LoopEnvScopeId = -1)
{
    public IteratorDriverKind Kind { get; } = Kind;
    public ExpressionNode Iterable { get; } = Iterable;
    public BindingTarget Target { get; } = Target;
    public VariableKind? DeclarationKind { get; } = DeclarationKind;
    public BlockStatement Body { get; } = Body;
    public int IterationScopeId { get; } = IterationScopeId;
    public int IterationSlotCount { get; } = IterationSlotCount;
    public ImmutableArray<int> PerIterationSlotIndices { get; } = PerIterationSlotIndices;
    public ImmutableArray<Symbol> PerIterationBindings { get; } = PerIterationBindings;
    public bool CanReuseIterationEnvironment { get; } = CanReuseIterationEnvironment;
    public bool CanPoolLoopEnvironment { get; } = CanPoolLoopEnvironment;

    /// <summary>
    /// Slot index in the enclosing function scope where the cached iteration environment is stored.
    /// -1 if no slot is allocated (var bindings or closures exist).
    /// </summary>
    public int LoopEnvSlotIndex { get; } = LoopEnvSlotIndex;

    /// <summary>
    /// Scope ID of the function scope containing the LoopEnvSlotIndex slot.
    /// Used with TryResolveSlot to find the correct environment at runtime.
    /// </summary>
    public int LoopEnvScopeId { get; } = LoopEnvScopeId;
}
