using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

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
    bool AllowIterationEnvironmentPooling = false)
{
    // Cached analysis for fast-path loop body execution
    private StatementNode? _singleBodyStatement;
    private bool _singleBodyStatementComputed;
    private bool _bodyNeedsEnvironment;
    private bool _bodyNeedsEnvironmentComputed;

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
