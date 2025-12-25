#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;

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
    bool CanPoolLoopEnvironment = false)
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
    /// Cached iteration environment for reuse across multiple executions of this for-of loop.
    /// Only used when CanReuseIterationEnvironment is true (no closures in body).
    /// </summary>
    private JsEnvironment? _cachedIterationEnvironment;

    /// <summary>
    /// Gets or creates a cached iteration environment, resetting it with the new parent if already cached.
    /// This avoids allocating a new JsEnvironment on each entry to the for-of loop.
    /// </summary>
    /// <param name="parent">The parent environment for this iteration.</param>
    /// <param name="source">Source reference for debugging.</param>
    /// <returns>A reusable iteration environment.</returns>
    public JsEnvironment GetOrResetIterationEnvironment(JsEnvironment parent, SourceReference? source)
    {
        var cached = _cachedIterationEnvironment;
        if (cached is not null)
        {
            // Reset the cached environment with the new parent
            cached.Reset(parent, false, false, source, "for-each-iteration-cached");
            if (IterationSlotCount > 0 && IterationScopeId >= 0)
            {
                cached.InitializeSlots(IterationSlotCount, IterationScopeId);
            }
            return cached;
        }

        // First time - create and cache the environment
        var env = new JsEnvironment(parent, false, false, source, "for-each-iteration-cached");
        if (IterationSlotCount > 0 && IterationScopeId >= 0)
        {
            env.InitializeSlots(IterationSlotCount, IterationScopeId);
        }
        _cachedIterationEnvironment = env;
        return env;
    }
}
