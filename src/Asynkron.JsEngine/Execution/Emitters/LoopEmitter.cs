#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
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

        // Compute loop scope ID for For-loops with per-iteration bindings
        var loopScopeId = -1;
        if (!plan.PerIterationBindings.IsDefaultOrEmpty && plan.Kind == LoopKind.For)
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
            ConditionAfterBody = plan.ConditionAfterBody,
            PerIterationEnvAfterBody = plan.Kind == LoopKind.For && !plan.PerIterationBindings.IsDefaultOrEmpty,
            NeedsTryFinally = false,
        };

        var driver = new ConditionLoopDriver(plan, ctx);

        return LoopEmitterHelpers.EmitLoopSkeleton(ctx, ref driver, in config, nextIndex, label, out entryIndex);
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
