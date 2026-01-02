#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Normalized description of a loop that flattens initializer/test/body/increment
///     into explicit statement lists the IR builder can consume without re-parsing
///     individual loop syntaxes.
/// </summary>
internal sealed record LoopPlan(
    LoopKind Kind,
    ImmutableArray<StatementNode> LeadingStatements,
    ImmutableArray<StatementNode> ConditionPrologue,
    ExpressionNode Condition,
    BlockStatement Body,
    ImmutableArray<StatementNode> PostIteration,
    bool ConditionAfterBody,
    ImmutableArray<Symbol> PerIterationBindings = default,
    bool AllowIterationEnvironmentPooling = false,
    int IterationScopeId = -1,
    int IterationParentScopeId = -1,
    int IterationSlotCount = -1,
    ImmutableArray<int> PerIterationSlotIndices = default)
{
    private bool _bodyNeedsEnvironment;

    private bool _bodyNeedsEnvironmentComputed;

    // Cached slot map for per-iteration bindings (built once, reused each iteration)
    private ImmutableDictionary<Symbol, int>? _iterationSlotMap;
    private bool _iterationSlotMapComputed;

    // Cached analysis for fast-path loop body execution
    private StatementNode? _singleBodyStatement;
    private bool _singleBodyStatementComputed;

    /// <summary>
    /// Returns the slot map for per-iteration bindings.
    /// Built lazily once and cached for reuse across iterations.
    /// </summary>
    public ImmutableDictionary<Symbol, int> IterationSlotMap
    {
        get
        {
            if (!_iterationSlotMapComputed)
            {
                ComputeIterationSlotMap();
            }

            return _iterationSlotMap!;
        }
    }

    /// <summary>
    /// Returns the single statement inside the body block, or null if the body has
    /// multiple statements or needs a block environment. Used to skip block dispatch overhead.
    /// </summary>
    public StatementNode? SingleBodyStatement
    {
        get
        {
            if (!_singleBodyStatementComputed)
            {
                ComputeSingleBodyStatement();
            }

            return _singleBodyStatement;
        }
    }

    /// <summary>
    /// Returns true if the body block needs its own lexical environment.
    /// </summary>
    public bool BodyNeedsEnvironment
    {
        get
        {
            if (!_bodyNeedsEnvironmentComputed)
            {
                ComputeBodyNeedsEnvironment();
            }

            return _bodyNeedsEnvironment;
        }
    }

    private void ComputeIterationSlotMap()
    {
        _iterationSlotMapComputed = true;

        if (PerIterationBindings.IsDefaultOrEmpty || PerIterationSlotIndices.IsDefaultOrEmpty)
        {
            _iterationSlotMap = ImmutableDictionary<Symbol, int>.Empty
                .WithComparers(ReferenceEqualityComparer<Symbol>.Instance);
            return;
        }

        var builder = ImmutableDictionary.CreateBuilder<Symbol, int>(ReferenceEqualityComparer<Symbol>.Instance);
        var count = Math.Min(PerIterationBindings.Length, PerIterationSlotIndices.Length);
        for (var i = 0; i < count; i++)
        {
            builder[PerIterationBindings[i]] = PerIterationSlotIndices[i];
        }

        _iterationSlotMap = builder.ToImmutable();
    }

    private void ComputeSingleBodyStatement()
    {
        _singleBodyStatementComputed = true;

        // Only optimize if body has exactly one statement and doesn't need its own environment
        if (Body.Statements.Length == 1 && !BodyNeedsEnvironment)
        {
            _singleBodyStatement = Body.Statements[0];
        }
    }

    private void ComputeBodyNeedsEnvironment()
    {
        _bodyNeedsEnvironmentComputed = true;

        // Check if body has lexical declarations that need a block scope
        var hoistPlan = ((IAstCacheable<HoistPlan>)Body).GetOrCreateCache();
        _bodyNeedsEnvironment = hoistPlan.NeedsEnvironment;
    }
}
