#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
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
        if (block.ReuseEnclosingEnvironment)
        {
            return ctx.TryBuildStatementList(block.Statements, nextIndex, out entryIndex);
        }

        // If the block needs its own scope (has let/const declarations),
        // we need to create an environment for it.
        var hoistPlan = ((IAstCacheable<HoistPlan>)block).GetOrCreateCache();
        if (hoistPlan.NeedsEnvironment)
        {
            // Always emit proper IR for blocks with lexical bindings:
            // PushEnvironment + individual statements + PopEnvironment
            // This ensures exception handling works correctly (fix for #432)
            return TryEmitBlockWithEnvironment(ctx, block, hoistPlan, nextIndex, out entryIndex);
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
        var functionDeclarations = ImmutableArray.CreateBuilder<StatementNode>(block.Statements.Length);
        var nonFunctionStatements = ImmutableArray.CreateBuilder<StatementNode>(block.Statements.Length);
        foreach (var statement in block.Statements)
        {
            if (statement is FunctionDeclaration)
            {
                functionDeclarations.Add(statement);
            }
            else
            {
                nonFunctionStatements.Add(statement);
            }
        }

        // Build slot map at IR build time because scope analysis runs AFTER IR is built.
        // TopLevelLexicalNames covers let/const/class; direct function declarations also
        // need a block slot so Annex B evaluation can copy that value to the var binding.
        var slotMap = BuildSlotMap(hoistPlan.TopLevelLexicalNames, functionDeclarations);
        var slotCount = slotMap.Count;

        // IMPORTANT FIX for #432: If TopLevelLexicalNames is empty, we don't need a block environment.
        // This happens when NeedsEnvironment returns true due to NESTED lexical bindings (e.g.,
        // for-await-of with let), but those bindings are handled by the child emitter (ForOfEmitter).
        // Emitting an unnecessary PushEnvironment here breaks exception handling because the
        // try/catch frame's EntryEnvironment doesn't account for these extra environments.
        if (slotCount == 0)
        {
            // No top-level bindings - just emit the statements directly
            return ctx.TryBuildStatementList(block.Statements, nextIndex, out entryIndex);
        }

        var instructionStart = ctx.InstructionCount;

        // Check if we can pool the environment (no closures or dynamic scope)
        var allowPooling = !DynamicScopeDetector.ContainsWithOrDirectEval(block) && !ContainsInnerFunctionExpression(block);

        // Allocate a scope ID for this block (will be remapped during scope analysis)
        var scopeId = ctx.AllocateScopeId();

        // Build instructions bottom-up (reverse order):
        // 1. PopEnvironmentInstruction pointing to nextIndex
        // 2. Body statements pointing to PopEnvironment
        // 3. PushEnvironmentInstruction pointing to body entry

        // 1. Pop environment (exit the block scope)
        var popEnvIndex = ctx.Append(new PopEnvironmentInstruction(scopeId, allowPooling, nextIndex));

        ctx.PushScope(scopeId, allowPooling);

        var bodyEntry = popEnvIndex;
        if (nonFunctionStatements.Count > 0 &&
            !ctx.TryBuildStatementList(nonFunctionStatements.ToImmutable(), popEnvIndex, out bodyEntry))
        {
            ctx.PopScope(scopeId);
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        // 2a. Hoist block-scoped function declarations before other statements
        var hoistEntry = bodyEntry;
        if (functionDeclarations.Count > 0 &&
            !ctx.TryBuildStatementList(functionDeclarations.ToImmutable(), bodyEntry, out hoistEntry))
        {
            ctx.PopScope(scopeId);
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        ctx.PopScope(scopeId);

        // 3. Push environment (enter the block scope)
        // For blocks, PerIterationBindings MUST be empty (no loop iteration semantics).
        // This is critical: the ExecutionPlanRunner uses PerIterationBindings to detect
        // whether a PUSH_ENV is for a loop iteration (non-empty) vs a regular block (empty).
        // Passing LexicalTemplate here would incorrectly mark this as a loop iteration scope.
        //
        // Compute LexicalBindings for TDZ enforcement: let/const names that should be
        // marked Uninitialized when the block scope is entered. Exclude function declarations
        // since those are initialized immediately by hoisting.
        var lexicalBindings = ComputeBlockLexicalBindings(hoistPlan.TopLevelLexicalNames, functionDeclarations);

        entryIndex = ctx.Append(new PushEnvironmentInstruction(
            hoistEntry,
            ImmutableArray<Symbol>.Empty,
            scopeId,
            slotCount,
            slotMap,
            allowPooling,
            LexicalBindings: lexicalBindings,
            SourceBlock: block));

        return true;
    }

    /// <summary>
    /// Computes the set of lexical bindings that need TDZ enforcement.
    /// Includes let/const names, excludes function declaration names (which are
    /// initialized immediately by hoisting and don't have a temporal dead zone).
    /// </summary>
    private static ImmutableHashSet<Symbol>? ComputeBlockLexicalBindings(
        HashSet<Symbol> topLevelLexicalNames,
        ImmutableArray<StatementNode>.Builder functionDeclarations)
    {
        if (topLevelLexicalNames.Count == 0)
        {
            return null;
        }

        // Collect function declaration names to exclude from TDZ
        HashSet<Symbol>? funcNames = null;
        foreach (var stmt in functionDeclarations)
        {
            if (stmt is FunctionDeclaration { Name: { } name })
            {
                funcNames ??= new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
                funcNames.Add(name);
            }
        }

        // Build the lexical bindings set (let/const only, not function declarations)
        var builder = ImmutableHashSet.CreateBuilder(ReferenceEqualityComparer<Symbol>.Instance);
        foreach (var name in topLevelLexicalNames)
        {
            if (funcNames is null || !funcNames.Contains(name))
            {
                builder.Add(name);
            }
        }

        return builder.Count > 0 ? builder.ToImmutable() : null;
    }

    /// <summary>
    /// Builds an immutable slot map from a set of lexical names.
    /// Each symbol gets a sequential slot index starting from 0.
    /// </summary>
    private static ImmutableDictionary<Symbol, int> BuildSlotMap(
        HashSet<Symbol> lexicalNames,
        ImmutableArray<StatementNode>.Builder functionDeclarations)
    {
        if (lexicalNames.Count == 0 && functionDeclarations.Count == 0)
        {
            return ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);
        }

        var builder = ImmutableDictionary.CreateBuilder<Symbol, int>(ReferenceEqualityComparer<Symbol>.Instance);
        var slotIndex = 0;
        foreach (var name in lexicalNames)
        {
            builder[name] = slotIndex++;
        }

        foreach (var statement in functionDeclarations)
        {
            if (statement is FunctionDeclaration { Name: { } name } && !builder.ContainsKey(name))
            {
                builder[name] = slotIndex++;
            }
        }

        return builder.ToImmutable();
    }
}
