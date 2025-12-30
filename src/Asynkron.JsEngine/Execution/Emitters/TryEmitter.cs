using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

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
            if (!ctx.TryBuildStatement(statement.Finally, endFinallyIndex, out finallyEntry, activeLabel))
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
            if (!TryEmitCatchBlock(ctx, statement.Catch, leaveTryIndex, activeLabel, out catchEntry))
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

        // Emit EnterTry instruction as the entry point
        // Note: CatchSlotSymbol is null because we use EnterCatchInstruction now
        entryIndex = ctx.Append(new EnterTryInstruction(tryEntry, catchEntry, null, finallyEntry, endFinallyIndex));
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
        Symbol? activeLabel,
        out int catchEntry)
    {
        // Get catch parameter symbol (if any - ES2019 allows optional catch binding)
        Symbol? catchParamSymbol = null;
        if (catchClause.Binding is IdentifierBinding identifierBinding)
        {
            catchParamSymbol = identifierBinding.Name;
        }
        else if (catchClause.Binding is not null)
        {
            // For destructuring patterns, fall back to AST-based approach
            // TODO: Implement pure IR for destructuring catch parameters
            catchEntry = -1;
            return false;
        }

        // Allocate a scope ID for the catch environment
        var catchScopeId = ctx.AllocateScopeId();

        // Build slot map for catch parameter
        var slotMap = catchParamSymbol != null
            ? ImmutableDictionary<Symbol, int>.Empty.Add(catchParamSymbol, 0)
            : ImmutableDictionary<Symbol, int>.Empty;
        var slotCount = catchParamSymbol != null ? 1 : 0;

        // Build instructions bottom-up:
        // 1. PopEnvironmentInstruction → leaveTryIndex
        // 2. Body statements → PopEnvironment
        // 3. EnterCatchInstruction → body entry

        // 1. Pop catch environment at the end
        var popCatchEnv = ctx.Append(new PopEnvironmentInstruction(catchScopeId, false, leaveTryIndex));

        // 2. Emit catch body statements (directly, not as a BlockStatement to avoid double scope)
        if (!ctx.TryBuildStatementList(catchClause.Body.Statements, popCatchEnv, out var bodyEntry))
        {
            catchEntry = -1;
            return false;
        }

        // 3. Emit EnterCatch as the catch handler entry point
        catchEntry = ctx.Append(new EnterCatchInstruction(
            bodyEntry,
            catchParamSymbol,
            catchScopeId,
            slotCount,
            slotMap));

        return true;
    }
}
