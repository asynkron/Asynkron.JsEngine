#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using static Asynkron.JsEngine.Ast.TypedAstEvaluator;

#endregion

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

            // AST fallback: block with lexical bindings but no yield/await
            // Reason: Environment creation without yield/await is simpler via AST eval
            // Tracking: #398, #416 (IR-only execution epic)
            entryIndex = ctx.Append(new StatementInstruction(nextIndex, block));
            return true;
        }

        return ctx.TryBuildStatementList(block.Statements, nextIndex, out entryIndex);
    }

    /// <summary>
    /// Builds a block that needs its own environment.
    /// Emits PushEnvironment + individual statements + PopEnvironment for proper IR execution.
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
        var allowPooling = !DynamicScopeDetector.ContainsWithOrDirectEval(block) && !ContainsInnerFunctionExpression(block);

        // Build slot map from hoistPlan.TopLevelLexicalNames at IR build time.
        // This is necessary because scope analysis runs AFTER IR is built - we can't rely on
        // block.SlotMap being populated yet. TopLevelLexicalNames contains all lexical bindings
        // (let/const/class/function declarations) directly in this block.
        var slotMap = BuildSlotMap(hoistPlan.TopLevelLexicalNames);
        var slotCount = slotMap.Count;

        // Allocate a scope ID for this block (will be remapped during scope analysis)
        var scopeId = ctx.AllocateScopeId();

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
        // For blocks, PerIterationBindings MUST be empty (no loop iteration semantics).
        // This is critical: the ExecutionPlanRunner uses PerIterationBindings to detect
        // whether a PUSH_ENV is for a loop iteration (non-empty) vs a regular block (empty).
        // Passing LexicalTemplate here would incorrectly mark this as a loop iteration scope.
        entryIndex = ctx.Append(new PushEnvironmentInstruction(
            bodyEntry,
            ImmutableArray<Symbol>.Empty,
            scopeId,
            slotCount,
            slotMap,
            allowPooling,
            SourceBlock: block));

        return true;
    }

    /// <summary>
    /// Builds an immutable slot map from a set of lexical names.
    /// Each symbol gets a sequential slot index starting from 0.
    /// </summary>
    private static ImmutableDictionary<Symbol, int> BuildSlotMap(HashSet<Symbol> lexicalNames)
    {
        if (lexicalNames.Count == 0)
        {
            return ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);
        }

        var builder = ImmutableDictionary.CreateBuilder<Symbol, int>(ReferenceEqualityComparer<Symbol>.Instance);
        var slotIndex = 0;
        foreach (var name in lexicalNames)
        {
            builder[name] = slotIndex++;
        }

        return builder.ToImmutable();
    }
}
