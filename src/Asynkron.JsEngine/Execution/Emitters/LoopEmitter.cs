#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for loop constructs (for, while, do-while).
/// All loop types are first normalized to a LoopPlan, then emitted uniformly.
/// </summary>
internal static class LoopEmitter
{
    /// <summary>
    /// Emit IR for a normalized loop plan.
    /// </summary>
    public static bool TryEmitLoopPlan(
        EmitContext ctx,
        LoopPlan plan,
        int nextIndex,
        Symbol? label,
        out int entryIndex)
    {
        var lexicalBindings = plan.PerIterationBindings.IsDefaultOrEmpty
            ? null
            : plan.PerIterationBindings.ToImmutableHashSet(ReferenceEqualityComparer<Symbol>.Instance);

        var elideLoopScopeEnvironment = CanElideNonCapturingForLoopScope(plan);

        // Compute loop scope ID for For-loops with per-iteration bindings
        var loopScopeId = -1;
        if (!elideLoopScopeEnvironment && !plan.PerIterationBindings.IsDefaultOrEmpty && plan.Kind == LoopKind.For)
        {
            loopScopeId = plan.IterationParentScopeId >= 0
                ? plan.IterationParentScopeId
                : plan.IterationScopeId >= 0
                    ? plan.IterationScopeId + 1000
                    : -1;
        }

        var config = new LoopSkeletonConfig
        {
            Body = plan.Body,
            BindingStatement = null,
            PostIteration = plan.PostIteration,
            LeadingStatements = plan.LeadingStatements,
            PerIterationBindings = plan.PerIterationBindings,
            IterationScopeId = plan.IterationScopeId,
            IterationSlotCount = plan.IterationSlotCount,
            PerIterationSlotIndices = plan.PerIterationSlotIndices,
            CanReuseIterationEnvironment = plan.AllowIterationEnvironmentPooling,
            LexicalBindings = lexicalBindings,
            LoopScopeId = loopScopeId,
            ElideLoopScopeEnvironment = elideLoopScopeEnvironment,
            ConditionAfterBody = plan.ConditionAfterBody,
            PerIterationEnvAfterBody = plan.Kind == LoopKind.For && !plan.PerIterationBindings.IsDefaultOrEmpty,
            NeedsTryFinally = false,
        };

        var driver = new ConditionLoopDriver(plan, ctx);

        return LoopEmitterHelpers.EmitLoopSkeleton(ctx, ref driver, in config, nextIndex, label, out entryIndex);
    }

    private static bool CanElideNonCapturingForLoopScope(LoopPlan plan)
    {
        if (plan.Kind != LoopKind.For ||
            plan.PerIterationBindings.IsDefaultOrEmpty ||
            plan.PerIterationBindings.Length != 1 ||
            !plan.AllowIterationEnvironmentPooling ||
            plan.ConditionAfterBody ||
            plan.BodyNeedsEnvironment ||
            !plan.ConditionPrologue.IsDefaultOrEmpty ||
            plan.IterationScopeId < 0 ||
            plan.IterationSlotCount <= 0 ||
            plan.PerIterationSlotIndices.IsDefaultOrEmpty ||
            plan.PerIterationSlotIndices.Length != 1 ||
            plan.PerIterationSlotIndices[0] < 0)
        {
            return false;
        }

        if (plan.LeadingStatements.Length != 1 ||
            plan.LeadingStatements[0] is not VariableDeclaration
            {
                Kind: VariableKind.Let,
                Declarators.Length: 1
            } declaration)
        {
            return false;
        }

        var declarator = declaration.Declarators[0];
        if (declarator.Target is not IdentifierBinding identifier ||
            declarator.Initializer is null ||
            !ReferenceEquals(identifier.Name, plan.PerIterationBindings[0]))
        {
            return false;
        }

        return !ContainsDynamicScope(plan.Body) &&
               !ContainsDynamicScope(plan.LeadingStatements) &&
               !ContainsDirectEval(plan.Condition) &&
               !ContainsDynamicScope(plan.PostIteration) &&
               !ContainsSuspension(plan.Body) &&
               !ContainsSuspension(plan.LeadingStatements) &&
               !ContainsSuspension(plan.Condition) &&
               !ContainsSuspension(plan.PostIteration);
    }

    private static bool ContainsDynamicScope(BlockStatement block)
    {
        return DynamicScopeDetector.ContainsWithOrDirectEval(block);
    }

    private static bool ContainsDynamicScope(ImmutableArray<StatementNode> statements)
    {
        if (statements.IsDefaultOrEmpty)
        {
            return false;
        }

        return DynamicScopeDetector.ContainsWithOrDirectEval(new BlockStatement(null, statements, false));
    }

    private static bool ContainsDirectEval(ExpressionNode expression)
    {
        return DynamicScopeDetector.ContainsDirectEval(expression);
    }

    private static bool ContainsSuspension(BlockStatement block)
    {
        return AstShapeAnalyzer.StatementContainsAwait(block) ||
               AstShapeAnalyzer.StatementContainsYield(block);
    }

    private static bool ContainsSuspension(ImmutableArray<StatementNode> statements)
    {
        if (statements.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var statement in statements)
        {
            if (AstShapeAnalyzer.StatementContainsAwait(statement) ||
                AstShapeAnalyzer.StatementContainsYield(statement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSuspension(ExpressionNode expression)
    {
        return AstShapeAnalyzer.ContainsAwait(expression) ||
               AstShapeAnalyzer.ContainsYield(expression);
    }
}

/// <summary>
/// Driver for condition-based loops (for, while, do-while).
/// Provides BranchInstruction for the condition test.
/// </summary>
internal struct ConditionLoopDriver : ILoopDriver
{
    private readonly LoopPlan _plan;
    private readonly EmitContext _ctx;

    public ConditionLoopDriver(LoopPlan plan, EmitContext ctx)
    {
        _plan = plan;
        _ctx = ctx;
    }

    public bool EmitMoveNext(EmitContext ctx, out int moveNextEntry, out int moveNextBranch)
    {
        moveNextEntry = -1;
        moveNextBranch = -1;

        if (!ExpressionProgramCompiler.TryCompile(_plan.Condition, out var conditionProgram, out var conditionFailure))
        {
            ctx.SetExpressionProgramFailure("BranchInstruction", _plan.Condition, conditionFailure);
            return false;
        }

        // BranchInstruction - body and exit targets will be patched by WireMoveNext
        var branchIndex = ctx.Append(new BranchInstruction(
            ConsequentIndex: -1,
            AlternateIndex: -1,
            ConditionProgram: conditionProgram));

        moveNextEntry = branchIndex;
        moveNextBranch = branchIndex;

        // Build ConditionPrologue → BranchInstruction
        if (!_plan.ConditionPrologue.IsDefaultOrEmpty)
        {
            if (!ctx.TryBuildStatementList(_plan.ConditionPrologue, branchIndex, out moveNextEntry))
            {
                return false;
            }
        }

        return true;
    }

    public void WireMoveNext(EmitContext ctx, int moveNextBranch, int bodyTarget, int exitTarget)
    {
        ctx.Patch(moveNextBranch,
            (BranchInstruction)ctx.Instructions[moveNextBranch] with
            {
                ConsequentIndex = bodyTarget,
                AlternateIndex = exitTarget
            });
    }

    public int EmitInitAndWire(EmitContext ctx, int loopEnterTarget) => -1;

    public (int CleanupEntry, int EndFinallyIndex) EmitCleanup(EmitContext ctx, int nextIndex) => (-1, -1);
}
