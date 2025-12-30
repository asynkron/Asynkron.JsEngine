using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for loop constructs (for, while, do-while).
/// All loop types are first normalized to a LoopPlan, then emitted uniformly.
/// </summary>
internal static class LoopEmitter
{
    /// <summary>
    /// Emit IR for a normalized loop plan.
    /// </summary>
    /// <param name="ctx">The emit context.</param>
    /// <param name="plan">The normalized loop plan.</param>
    /// <param name="nextIndex">Index of the instruction to execute after the loop.</param>
    /// <param name="label">Optional label for this loop.</param>
    /// <param name="entryIndex">Output: the entry point instruction index.</param>
    /// <returns>True if successful, false if the loop couldn't be emitted.</returns>
    public static bool TryEmitLoopPlan(
        EmitContext ctx,
        LoopPlan plan,
        int nextIndex,
        Symbol? label,
        out int entryIndex)
    {
        var instructionStart = ctx.InstructionCount;

        // Create LoopExitInstruction first (we build bottom-up)
        // This pops the loop stack when exiting the loop (normal exit or break)
        var loopExitIndex = ctx.Append(new LoopExitInstruction(nextIndex));

        var conditionJumpIndex = ctx.Append(new JumpInstruction(-1));

        // Build post-iteration (increment) statements
        // Per ES spec, update expressions don't contribute to the loop's completion value
        var postIterationEntry = conditionJumpIndex;
        if (!plan.PostIteration.IsDefaultOrEmpty)
        {
            var savedSuppressCompletionValue = ctx.SuppressCompletionValue;
            ctx.SuppressCompletionValue = true;
            var built = ctx.TryBuildStatementList(plan.PostIteration, conditionJumpIndex, out postIterationEntry);
            ctx.SuppressCompletionValue = savedSuppressCompletionValue;
            if (!built)
            {
                ctx.Rollback(instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        // For lexical declarations (let/const), emit PushEnvironmentInstruction
        // AFTER the body but BEFORE the increment (PostIteration).
        // This matches the ECMAScript spec (ForBodyEvaluation step 3.e):
        //   1. Evaluate condition
        //   2. Evaluate body (closures capture current environment)
        //   3. CreatePerIterationEnvironment (create new env, copy values)
        //   4. Evaluate increment (modifies new env, not the captured one)
        //
        // NOTE: For per-iteration environments, PushEnvironment copies values from the
        // current iteration environment but creates the new env with currentEnv.Enclosing
        // as parent (handled in ScriptRunner/ExecutionPlanRunner). This ensures per-iteration
        // environments are SIBLINGS (same parent), not nested.
        var continueTarget = postIterationEntry;
        int createEnvIndex = -1;
        if (!plan.PerIterationBindings.IsDefaultOrEmpty && plan.Kind == LoopKind.For)
        {
            var slotMap = EmitContext.BuildSlotMap(plan.PerIterationBindings, plan.PerIterationSlotIndices);

            // CreateEnv flows to PostIteration (increment)
            createEnvIndex = ctx.Append(new PushEnvironmentInstruction(
                postIterationEntry,
                plan.PerIterationBindings,
                plan.IterationScopeId,
                plan.IterationSlotCount,
                slotMap,
                plan.AllowIterationEnvironmentPooling));

            // Continue should go through CreateEnv before increment
            continueTarget = createEnvIndex;
        }

        // Create Pop instruction for loop exit BEFORE building body
        // This ensures breaks also go through the pop, then through LoopExitInstruction
        var loopExitTarget = loopExitIndex;
        if (!plan.PerIterationBindings.IsDefaultOrEmpty)
        {
            loopExitTarget = ctx.Append(new PopEnvironmentInstruction(
                plan.IterationScopeId,
                plan.AllowIterationEnvironmentPooling,
                loopExitIndex));
        }

        // TargetScopeId is the scope to pop TO when break/continue
        // For loops with per-iteration bindings, we pop to the iteration scope's parent
        var targetScopeId = plan.IterationScopeId >= 0 ? plan.IterationScopeId : -1;
        ctx.PushLoopScope(label, continueTarget, loopExitTarget, targetScopeId);

        // Body flows to CreateEnv (if present) or PostIteration (increment)
        var bodyNextTarget = createEnvIndex >= 0 ? createEnvIndex : postIterationEntry;
        if (!ctx.TryBuildStatement(plan.Body, bodyNextTarget, out var bodyEntry, label))
        {
            ctx.PopLoopScope();
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        ctx.PopLoopScope();

        // For non-For loops (while, do-while), create environment before body (original behavior)
        var iterationBodyEntry = bodyEntry;
        if (!plan.PerIterationBindings.IsDefaultOrEmpty && plan.Kind != LoopKind.For)
        {
            var slotMap = EmitContext.BuildSlotMap(plan.PerIterationBindings, plan.PerIterationSlotIndices);

            var createEnvBeforeBody = ctx.Append(new PushEnvironmentInstruction(
                bodyEntry,
                plan.PerIterationBindings,
                plan.IterationScopeId,
                plan.IterationSlotCount,
                slotMap,
                plan.AllowIterationEnvironmentPooling));
            iterationBodyEntry = createEnvBeforeBody;
        }

        // Branch flows to body directly (for For loops) or CreateEnv (for others)
        // loopExitTarget (with Pop if needed) was created earlier for LoopScope break target
        var branchIndex = ctx.Append(new BranchInstruction(plan.Condition, iterationBodyEntry, loopExitTarget));

        var conditionEntry = branchIndex;
        if (!plan.ConditionPrologue.IsDefaultOrEmpty)
        {
            if (!ctx.TryBuildStatementList(plan.ConditionPrologue, branchIndex, out conditionEntry))
            {
                ctx.Rollback(instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        ctx.Patch(conditionJumpIndex, new JumpInstruction(conditionEntry));

        var loopEntry = plan.ConditionAfterBody ? iterationBodyEntry : conditionJumpIndex;

        if (!plan.LeadingStatements.IsDefaultOrEmpty)
        {
            // Per ES spec 13.7.4.7 (ForLoopEvaluation), the initializer expression/declaration
            // does NOT contribute to the loop's completion value. Only the body contributes.
            var savedSuppressCompletionValue = ctx.SuppressCompletionValue;
            ctx.SuppressCompletionValue = true;
            var built = ctx.TryBuildStatementList(plan.LeadingStatements, loopEntry, out loopEntry);
            ctx.SuppressCompletionValue = savedSuppressCompletionValue;
            if (!built)
            {
                ctx.Rollback(instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        // Wrap entry with LoopEnterInstruction to push loop context at runtime
        // This enables break/continue from AST-evaluated code (via StatementInstruction)
        // to resolve their jump targets using the runtime loop stack.
        entryIndex = ctx.Append(new LoopEnterInstruction(
            loopEntry,
            label,
            loopExitTarget,
            continueTarget));

        return true;
    }
}
