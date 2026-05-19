#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for exception handling constructs (try/catch/finally, throw).
/// </summary>
internal static class TryEmitter
{
    /// <summary>
    /// Emit IR for a try statement with optional catch and finally blocks.
    /// </summary>
    public static bool TryEmitTry(
        EmitContext ctx,
        TryStatement statement,
        int nextIndex,
        Symbol? activeLabel,
        out int entryIndex)
    {
        var hasCatch = statement.Catch is not null;
        var hasFinally = statement.Finally is not null;

        if (!hasCatch && !hasFinally)
        {
            entryIndex = -1;
            return false;
        }

        var instructionStart = ctx.InstructionCount;

        // Build finally block first (bottom-up)
        var finallyEntry = -1;
        var endFinallyIndex = -1;
        if (hasFinally && statement.Finally is not null)
        {
            endFinallyIndex = ctx.Append(new EndFinallyInstruction(nextIndex));

            var builtFinally = ctx.TryBuildStatement(statement.Finally, endFinallyIndex, out finallyEntry, activeLabel);
            if (!builtFinally)
            {
                ctx.Rollback(instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        // LeaveTry marks normal exit from try block
        var leaveTryIndex = ctx.Append(new LeaveTryInstruction(nextIndex));

        // Build catch block using pure IR (no AST delegation)
        var catchEntry = -1;
        if (hasCatch && statement.Catch is not null)
        {
            if (!TryEmitCatchBlock(ctx, statement.Catch, leaveTryIndex, out catchEntry))
            {
                ctx.Rollback(instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        // Build try block
        if (!ctx.TryBuildStatement(statement.TryBlock, leaveTryIndex, out var tryEntry, activeLabel))
        {
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        // Emit EnterTry instruction as the entry point.
        entryIndex = ctx.Append(new EnterTryInstruction(
            tryEntry,
            catchEntry,
            null,
            finallyEntry,
            endFinallyIndex,
            leaveTryIndex));
        return true;
    }

    /// <summary>
    /// Emit IR for a catch block with pure IR instructions.
    /// Creates EnterCatchInstruction → body instructions → PopEnvironmentInstruction.
    /// </summary>
    private static bool TryEmitCatchBlock(
        EmitContext ctx,
        CatchClause catchClause,
        int leaveTryIndex,
        out int catchEntry)
    {
        // Allocate a scope ID for the catch environment
        var catchScopeId = ctx.AllocateScopeId();

        // Build slot map and determine the direct catch binding payload.
        ImmutableDictionary<Symbol, int> slotMap;
        int slotCount;
        BindingTargetProgram? catchBindingProgram = null;
        if (catchClause.Binding is IdentifierBinding identifierBinding)
        {
            catchBindingProgram = new IdentifierBindingTargetProgram(identifierBinding.Name);
            slotMap = ImmutableDictionary<Symbol, int>.Empty.Add(identifierBinding.Name, 0);
            slotCount = 1;
        }
        else if (catchClause.Binding is not null)
        {
            if (!BindingTargetProgramCompiler.TryCompile(
                    catchClause.Binding,
                    out var compiledBindingProgram,
                    out var failureReason))
            {
                ctx.SetFailureReason(
                    $"Catch binding target '{catchClause.Binding.GetType().Name}' could not lower to a binding program: {failureReason ?? "unknown reason"}.",
                    ExecutionPlanFailureCode.UnsupportedBindingProgram);
                catchEntry = -1;
                return false;
            }

            catchBindingProgram = compiledBindingProgram;
            var bindingSymbols = new List<Symbol>();
            compiledBindingProgram.CollectSymbols(bindingSymbols);
            var slotMapBuilder = ImmutableDictionary.CreateBuilder<Symbol, int>();
            foreach (var symbol in bindingSymbols)
            {
                if (!slotMapBuilder.ContainsKey(symbol))
                {
                    slotMapBuilder.Add(symbol, slotMapBuilder.Count);
                }
            }

            slotMap = slotMapBuilder.ToImmutable();
            slotCount = slotMap.Count;
        }
        else
        {
            // ES2019 optional catch binding: catch { }
            slotMap = ImmutableDictionary<Symbol, int>.Empty;
            slotCount = 0;
        }

        // Build instructions bottom-up:
        // 1. PopEnvironmentInstruction → leaveTryIndex
        // 2. Body statements (possibly with its own block environment) → PopEnvironment
        // 3. EnterCatchInstruction → body entry

        // 1. Pop catch environment at the end
        var popCatchEnv = ctx.Append(new PopEnvironmentInstruction(catchScopeId, false, leaveTryIndex));

        // 2. Emit catch body - use TryEmitBlock to properly handle block-scoped declarations
        // Per ECMAScript, the catch block body gets its own lexical environment for let/const,
        // separate from the catch parameter environment.
        ctx.PushScope(catchScopeId);
        if (!BlockEmitter.TryEmitBlock(ctx, catchClause.Body, popCatchEnv, out var bodyEntry))
        {
            ctx.PopScope(catchScopeId);
            catchEntry = -1;
            return false;
        }
        ctx.PopScope(catchScopeId);

        // 3. Emit the catch instruction.
        catchEntry = ctx.Append(new EnterCatchInstruction(
            bodyEntry,
            catchBindingProgram,
            catchScopeId,
            slotCount,
            slotMap));

        return true;
    }
}
