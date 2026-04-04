#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for for-of and for-await-of loops.
/// These are iterator-based loops that require try/finally for iterator cleanup.
/// </summary>
internal static class ForOfEmitter
{
    /// <summary>
    /// Emit IR for a for-of or for-await-of statement.
    /// </summary>
    public static bool TryEmit(
        EmitContext ctx,
        ForEachStatement statement,
        int nextIndex,
        Symbol? label,
        out int entryIndex)
    {
        if (AstShapeAnalyzer.ContainsYield(statement.Iterable))
        {
            entryIndex = -1;
            return false;
        }

        var iteratorPlan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();

        var lexicalBindings = iteratorPlan.PerIterationBindings.IsDefaultOrEmpty
            ? null
            : iteratorPlan.PerIterationBindings.ToImmutableHashSet(ReferenceEqualityComparer<Symbol>.Instance);

        // Pre-create symbols and allocate slots for O(1) access
        var iteratorSymbol = Symbol.Synthetic(iteratorPlan.Kind == IteratorDriverKind.Await
            ? "__forAwait_iter"
            : "__forOf_iter");
        var valueSymbol = Symbol.Synthetic(iteratorPlan.Kind == IteratorDriverKind.Await
            ? "__forAwait_value"
            : "__forOf_value");

        var iteratorSlotIndex = ctx.AllocateSlot(iteratorSymbol);
        var valueSlotIndex = ctx.AllocateSlot(valueSymbol);

        var bindingStatement = ctx.CreateIteratorBindingStatement(iteratorPlan, valueSymbol, valueSlotIndex);

        var needsPerIterEnv = iteratorPlan.DeclarationKind is (VariableKind.Let or VariableKind.Const
            or VariableKind.Using or VariableKind.AwaitUsing)
            && !iteratorPlan.PerIterationBindings.IsDefaultOrEmpty;

        var config = new LoopSkeletonConfig
        {
            Body = iteratorPlan.Body,
            BindingStatement = bindingStatement,
            PerIterationBindings = needsPerIterEnv ? iteratorPlan.PerIterationBindings : default,
            IterationScopeId = needsPerIterEnv ? iteratorPlan.IterationScopeId : -1,
            IterationSlotCount = iteratorPlan.IterationSlotCount,
            PerIterationSlotIndices = iteratorPlan.PerIterationSlotIndices,
            CanReuseIterationEnvironment = false,
            LexicalBindings = lexicalBindings,
            LoopScopeId = -1,
            ConditionAfterBody = false,
            PerIterationEnvAfterBody = false,
            NeedsTryFinally = true,
        };

        var driver = new ForOfLoopDriver(iteratorPlan, iteratorSymbol, valueSymbol,
            iteratorSlotIndex, valueSlotIndex);

        return LoopEmitterHelpers.EmitLoopSkeleton(ctx, ref driver, in config, nextIndex, label, out entryIndex);
    }
}

/// <summary>
/// Driver for for-of / for-await-of loop emission.
/// Provides IteratorInit, IteratorMoveNext, and IteratorClose instructions.
/// </summary>
internal struct ForOfLoopDriver : ILoopDriver
{
    private readonly IteratorDriverPlan _plan;
    private readonly Symbol _iteratorSymbol;
    private readonly Symbol _valueSymbol;
    private readonly int _iteratorSlotIndex;
    private readonly int _valueSlotIndex;
    private IteratorInstructionPlan _instrPlan;

    public ForOfLoopDriver(
        IteratorDriverPlan plan,
        Symbol iteratorSymbol, Symbol valueSymbol,
        int iteratorSlotIndex, int valueSlotIndex)
    {
        _plan = plan;
        _iteratorSymbol = iteratorSymbol;
        _valueSymbol = valueSymbol;
        _iteratorSlotIndex = iteratorSlotIndex;
        _valueSlotIndex = valueSlotIndex;
    }

    public bool EmitMoveNext(EmitContext ctx, out int moveNextEntry, out int moveNextBranch)
    {
        if (!IteratorInstructionTemplate.TryAppendInstructions(
                ctx.Instructions,
                _plan,
                -1, // breakIndex will be patched by WireMoveNext
                _iteratorSymbol,
                _valueSymbol,
                _iteratorSlotIndex,
                _valueSlotIndex,
                out _instrPlan,
                out var failureReason))
        {
            ctx.SetExpressionProgramFailure("IteratorInitInstruction", _plan.Iterable, failureReason);
            moveNextEntry = -1;
            moveNextBranch = -1;
            return false;
        }

        moveNextEntry = _instrPlan.MoveNextIndex;
        moveNextBranch = _instrPlan.MoveNextIndex;
        return true;
    }

    public void WireMoveNext(EmitContext ctx, int moveNextBranch, int bodyTarget, int exitTarget)
    {
        ctx.Patch(moveNextBranch,
            (IteratorMoveNextInstruction)ctx.Instructions[moveNextBranch] with
            {
                Next = bodyTarget,
                BreakIndex = exitTarget
            });
    }

    public int EmitInitAndWire(EmitContext ctx, int loopEnterTarget)
    {
        ctx.Patch(_instrPlan.InitIndex,
            (IteratorInitInstruction)ctx.Instructions[_instrPlan.InitIndex] with { Next = loopEnterTarget });
        return _instrPlan.InitIndex;
    }

    public (int CleanupEntry, int EndFinallyIndex) EmitCleanup(EmitContext ctx, int nextIndex)
    {
        var endFinallyIndex = ctx.Append(new EndFinallyInstruction(nextIndex));
        var iteratorCloseIndex = ctx.Append(new IteratorCloseInstruction(_instrPlan.IteratorSlot, endFinallyIndex));
        return (iteratorCloseIndex, endFinallyIndex);
    }
}
