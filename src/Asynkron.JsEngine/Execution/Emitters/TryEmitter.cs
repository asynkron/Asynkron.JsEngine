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
        if (hasFinally && statement.Finally is not null)
        {
            var endFinallyIndex = ctx.Append(new EndFinallyInstruction(nextIndex));
            if (!ctx.TryBuildStatement(statement.Finally, endFinallyIndex, out finallyEntry, activeLabel))
            {
                ctx.Rollback(instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        // LeaveTry marks normal exit from try block
        var leaveTryIndex = ctx.Append(new LeaveTryInstruction(nextIndex));

        // Build catch block
        var catchEntry = -1;
        Symbol? catchSlotSymbol = null;
        if (hasCatch && statement.Catch is not null)
        {
            catchSlotSymbol = ctx.CreateCatchSlotSymbol();
            var catchBlock = ctx.BuildCatchBlock(statement.Catch, catchSlotSymbol);
            if (!ctx.TryBuildStatement(catchBlock, leaveTryIndex, out catchEntry, activeLabel))
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
        entryIndex = ctx.Append(new EnterTryInstruction(tryEntry, catchEntry, catchSlotSymbol, finallyEntry));
        return true;
    }
}
