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
        ctx.PushWithScope(withScopeSlot);
        if (!ctx.TryBuildStatement(statement.Body, leaveWithIndex, out var bodyEntry, activeLabel))
        {
            ctx.PopWithScope(withScopeSlot);
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }
        ctx.PopWithScope(withScopeSlot);

        // Build the enter with instruction (comes before the body)
        if (statement.Object is AwaitExpression awaitObject)
        {
            if (!ExpressionProgramCompiler.TryCompile(awaitObject.Expression, out var awaitedProgram, out var awaitFailure))
            {
                ctx.SetExpressionProgramFailure("EnterWithInstruction", awaitObject.Expression, awaitFailure);
                ctx.Rollback(instructionStart);
                entryIndex = -1;
                return false;
            }

            entryIndex = ctx.Append(new EnterWithInstruction(
                withScopeSlot,
                bodyEntry,
                AwaitStateKey: ((IAstCacheable<Symbol>)awaitObject).GetOrCreateCache(),
                AwaitedProgram: awaitedProgram,
                ObjectSource: statement.Object.Source));
            return true;
        }

        if (!ExpressionProgramCompiler.TryCompile(statement.Object, out var objectProgram, out var failureReason))
        {
            ctx.SetExpressionProgramFailure("EnterWithInstruction", statement.Object, failureReason);
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        entryIndex = ctx.Append(new EnterWithInstruction(
            withScopeSlot,
            bodyEntry,
            ObjectProgram: objectProgram,
            ObjectSource: statement.Object.Source));
        return true;
    }
}
