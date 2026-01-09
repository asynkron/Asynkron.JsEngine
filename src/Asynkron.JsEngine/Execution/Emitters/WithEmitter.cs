#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for with statements.
/// </summary>
internal static class WithEmitter
{
    /// <summary>
    /// Emit IR for a with statement.
    /// </summary>
    public static bool TryEmitWith(
        EmitContext ctx,
        WithStatement statement,
        int nextIndex,
        Symbol? activeLabel,
        out int entryIndex)
    {
        var instructionStart = ctx.InstructionCount;
        var withScopeSlot = ctx.CreateWithScopeSlotSymbol();

        // Build the leave with instruction first (it comes after the body)
        var leaveWithIndex = ctx.Append(new LeaveWithInstruction(withScopeSlot, nextIndex));

        // Build the body (jumps to the leave with instruction when done)
        if (!ctx.TryBuildStatement(statement.Body, leaveWithIndex, out var bodyEntry, activeLabel))
        {
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        // Build the enter with instruction (comes before the body)
        entryIndex = ctx.Append(new EnterWithInstruction(withScopeSlot, bodyEntry, ObjectExpression: statement.Object));
        return true;
    }
}
