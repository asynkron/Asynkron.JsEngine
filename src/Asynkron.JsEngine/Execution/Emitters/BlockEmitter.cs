using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using static Asynkron.JsEngine.Ast.TypedAstEvaluator;

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for block statements.
/// </summary>
internal static class BlockEmitter
{
    /// <summary>
    /// Try to build IR for a block statement.
    /// </summary>
    public static bool TryEmitBlock(
        EmitContext ctx,
        BlockStatement block,
        int nextIndex,
        out int entryIndex)
    {
        // If the block needs its own scope (has let/const declarations),
        // we need to create an environment for it.
        var hoistPlan = ((IAstCacheable<HoistPlan>)block).GetOrCreateCache();
        if (hoistPlan.NeedsEnvironment)
        {
            // If the block contains yield or await, we must NOT use StatementInstruction
            // because that causes duplicate execution on resume. Instead, emit
            // PushEnvironment + individual statements + PopEnvironment.
            if (AstShapeAnalyzer.StatementContainsYield(block) ||
                AstShapeAnalyzer.StatementContainsAwait(block))
            {
                return TryEmitBlockWithEnvironment(ctx, block, hoistPlan, nextIndex, out entryIndex);
            }

            // For blocks without yield/await, StatementInstruction is fine
            entryIndex = ctx.Append(new StatementInstruction(nextIndex, block));
            return true;
        }

        return ctx.TryBuildStatementList(block.Statements, nextIndex, out entryIndex);
    }

    /// <summary>
    /// Builds a block that needs its own environment AND contains yield/await.
    /// Instead of using StatementInstruction (which causes duplicate execution on resume),
    /// we emit PushEnvironment + individual statements + PopEnvironment.
    /// </summary>
    private static bool TryEmitBlockWithEnvironment(
        EmitContext ctx,
        BlockStatement block,
        HoistPlan hoistPlan,
        int nextIndex,
        out int entryIndex)
    {
        var instructionStart = ctx.InstructionCount;

        // Check if we can pool the environment (no closures or dynamic scope)
        var allowPooling = !ContainsWithOrDirectEval(block) && !ContainsInnerFunctionExpression(block);

        // Get scope info from the block (stamped by scope analysis)
        var scopeId = block.ScopeId >= 0 ? block.ScopeId : -1;
        var slotCount = block.SlotCount >= 0 ? block.SlotCount : 0;
        var slotMap = block.SlotMap.IsEmpty
            ? ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance)
            : block.SlotMap;

        // Build instructions bottom-up (reverse order):
        // 1. PopEnvironmentInstruction pointing to nextIndex
        // 2. Body statements pointing to PopEnvironment
        // 3. PushEnvironmentInstruction pointing to body entry

        // 1. Pop environment (exit the block scope)
        var popEnvIndex = ctx.Append(new PopEnvironmentInstruction(scopeId, allowPooling, nextIndex));

        // 2. Build the body statements, they flow to PopEnvironment
        if (!ctx.TryBuildStatementList(block.Statements, popEnvIndex, out var bodyEntry))
        {
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        // 3. Push environment (enter the block scope)
        // For blocks, PerIterationBindings is empty (no loop iteration semantics)
        entryIndex = ctx.Append(new PushEnvironmentInstruction(
            bodyEntry,
            hoistPlan.LexicalTemplate,
            scopeId,
            slotCount,
            slotMap,
            allowPooling));

        return true;
    }
}
