using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for control flow constructs (if, break, continue, return, labeled statements).
/// </summary>
internal static class ControlFlowEmitter
{
    /// <summary>
    /// Emit IR for an if statement.
    /// Per ES spec 14.6.2, empty branches and missing else produce undefined completion value.
    /// </summary>
    public static bool TryEmitIf(
        EmitContext ctx,
        IfStatement statement,
        int nextIndex,
        Symbol? activeLabel,
        out int entryIndex)
    {
        // Check for yield in condition
        if (AstShapeAnalyzer.ContainsYield(statement.Condition))
        {
            ctx.SetFailureReason("If condition contains unsupported yield shape.");
            entryIndex = -1;
            return false;
        }

        var instructionStart = ctx.InstructionCount;

        // Build else branch first (bottom-up building)
        int elseEntry;
        if (statement.Else is not null)
        {
            // Check for empty else block - emit SetCompletionValue for UpdateEmpty semantics
            if (IsEmptyBlock(statement.Else))
            {
                elseEntry = ctx.Append(new SetCompletionValueInstruction(nextIndex));
            }
            else if (!ctx.TryBuildStatement(statement.Else, nextIndex, out elseEntry, activeLabel))
            {
                ctx.Rollback(instructionStart);
                entryIndex = -1;
                return false;
            }
        }
        else
        {
            // No else branch - per ES spec, if condition is false, completion is undefined
            elseEntry = ctx.Append(new SetCompletionValueInstruction(nextIndex));
        }

        // Build then branch - check for empty block
        int thenEntry;
        if (IsEmptyBlock(statement.Then))
        {
            thenEntry = ctx.Append(new SetCompletionValueInstruction(nextIndex));
        }
        else if (!ctx.TryBuildStatement(statement.Then, nextIndex, out thenEntry, activeLabel))
        {
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        // Emit branch instruction
        entryIndex = ctx.Append(new BranchInstruction(statement.Condition, thenEntry, elseEntry));
        return true;
    }

    /// <summary>
    /// Checks if a statement is an empty block (BlockStatement with no statements).
    /// </summary>
    private static bool IsEmptyBlock(StatementNode statement)
    {
        return statement is BlockStatement { Statements.Length: 0 };
    }

    /// <summary>
    /// Emit IR for a break statement.
    /// </summary>
    public static bool TryEmitBreak(
        EmitContext ctx,
        BreakStatement statement,
        out int entryIndex)
    {
        if (!ctx.TryFindBreakTarget(statement.Label, out var target, out var scopeId))
        {
            ctx.SetFailureReason($"Break target not found for label: {statement.Label?.Name ?? "(none)"}");
            entryIndex = -1;
            return false;
        }

        entryIndex = ctx.Append(new BreakInstruction(target, scopeId));
        return true;
    }

    /// <summary>
    /// Emit IR for a continue statement.
    /// </summary>
    public static bool TryEmitContinue(
        EmitContext ctx,
        ContinueStatement statement,
        out int entryIndex)
    {
        if (!ctx.TryFindContinueTarget(statement.Label, out var target, out var scopeId))
        {
            ctx.SetFailureReason($"Continue target not found for label: {statement.Label?.Name ?? "(none)"}");
            entryIndex = -1;
            return false;
        }

        entryIndex = ctx.Append(new ContinueInstruction(target, scopeId));
        return true;
    }

    /// <summary>
    /// Emit IR for a labeled non-loop statement (enables labeled break within the statement body).
    /// </summary>
    public static bool TryEmitLabeledNonLoop(
        EmitContext ctx,
        LabeledStatement labeled,
        int nextIndex,
        out int entryIndex)
    {
        var instructionStart = ctx.InstructionCount;

        // Create LoopExitInstruction first (we build bottom-up)
        // This pops the loop stack when exiting the labeled statement
        var loopExitIndex = ctx.Append(new LoopExitInstruction(nextIndex));

        // Push scope so that labeled break can be resolved during IR building.
        // ContinueTarget is -1 because continue is not valid for non-loop labeled statements.
        ctx.PushLoopScope(labeled.Label, continueTarget: -1, breakTarget: loopExitIndex, targetScopeId: -1);

        var bodyBuilt = ctx.TryBuildStatement(labeled.Statement, loopExitIndex, out var bodyEntry);
        ctx.PopLoopScope();

        if (!bodyBuilt)
        {
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        // Wrap entry with LoopEnterInstruction to push loop context at runtime
        // This enables labeled break statements from AST-evaluated code to resolve their jump targets.
        entryIndex = ctx.Append(new LoopEnterInstruction(
            bodyEntry,
            labeled.Label,
            loopExitIndex,
            ContinueTarget: -1));

        return true;
    }
}
