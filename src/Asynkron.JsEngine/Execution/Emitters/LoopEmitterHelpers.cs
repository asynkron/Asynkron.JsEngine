#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution.Emitters;

internal static class LoopEmitterHelpers
{
    internal readonly struct LoopBuildResult
    {
        public LoopBuildResult(int continueTarget, int loopEntry)
        {
            ContinueTarget = continueTarget;
            LoopEntry = loopEntry;
        }

        public int ContinueTarget { get; }
        public int LoopEntry { get; }
    }

    internal static bool TryBuildLoopBody(
        EmitContext ctx,
        IteratorDriverPlan iteratorPlan,
        StatementNode body,
        StatementNode bindingStatement,
        Symbol? label,
        int moveNextIndex,
        int loopExitTarget,
        int targetScopeId,
        ImmutableHashSet<Symbol>? lexicalBindings,
        int instructionStart,
        out LoopBuildResult result)
    {
        result = default;

        var bodyNextTarget = moveNextIndex;
        var continueTarget = moveNextIndex;
        if (iteratorPlan.DeclarationKind is VariableKind.Let or VariableKind.Const &&
            !iteratorPlan.PerIterationBindings.IsDefaultOrEmpty)
        {
            var popEnvForContinue = ctx.Append(new PopEnvironmentInstruction(
                iteratorPlan.IterationScopeId,
                iteratorPlan.CanReuseIterationEnvironment,
                moveNextIndex));
            bodyNextTarget = popEnvForContinue;
            continueTarget = popEnvForContinue;
        }

        ctx.PushLoopScope(label, continueTarget, loopExitTarget, targetScopeId);
        var pushedIterationScope = false;
        if (iteratorPlan is { IterationScopeId: >= 0, DeclarationKind: VariableKind.Let or VariableKind.Const, PerIterationBindings.IsDefaultOrEmpty: false })
        {
            ctx.PushScope(iteratorPlan.IterationScopeId);
            pushedIterationScope = true;
        }

        var iterationEntry = -1;
        var bodyBuilt = ctx.TryBuildStatement(body, bodyNextTarget, out var bodyEntry, label);
        if (bodyBuilt)
        {
            bodyBuilt = ctx.TryBuildStatement(bindingStatement, bodyEntry, out iterationEntry, label);
        }

        if (pushedIterationScope)
        {
            ctx.PopScope(iteratorPlan.IterationScopeId);
        }

        ctx.PopLoopScope();

        if (!bodyBuilt)
        {
            ctx.Rollback(instructionStart);
            return false;
        }

        var loopEntry = iterationEntry;
        if (iteratorPlan.DeclarationKind is VariableKind.Let or VariableKind.Const &&
            !iteratorPlan.PerIterationBindings.IsDefaultOrEmpty)
        {
            var slotMap =
                EmitContext.BuildSlotMap(iteratorPlan.PerIterationBindings, iteratorPlan.PerIterationSlotIndices);
            var slotNames =
                EmitContext.BuildSlotNames(iteratorPlan.PerIterationBindings, iteratorPlan.PerIterationSlotIndices);

            var createEnvIndex = ctx.Append(new PushEnvironmentInstruction(
                iterationEntry,
                iteratorPlan.PerIterationBindings,
                iteratorPlan.IterationScopeId,
                iteratorPlan.IterationSlotCount,
                slotMap,
                iteratorPlan.CanReuseIterationEnvironment,
                lexicalBindings,
                SlotNames: slotNames));
            loopEntry = createEnvIndex;
        }

        result = new LoopBuildResult(continueTarget, loopEntry);
        return true;
    }
}
