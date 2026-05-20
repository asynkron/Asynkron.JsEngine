#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Driver interface for loop-type-specific emission.
/// Each loop type (condition-based, for-in, for-of) implements this to provide
/// its init, moveNext, and cleanup instructions.
/// </summary>
internal interface ILoopDriver
{
    /// <summary>
    /// Emit the MoveNext/condition block.
    /// </summary>
    /// <param name="ctx">The emit context.</param>
    /// <param name="moveNextEntry">Where to jump to enter the MoveNext (may include prologue).</param>
    /// <param name="moveNextBranch">The instruction index to patch with body/exit targets.</param>
    /// <returns>True if successful.</returns>
    bool EmitMoveNext(EmitContext ctx, out int moveNextEntry, out int moveNextBranch);

    /// <summary>
    /// Patch the MoveNext/condition instruction with real body and exit targets.
    /// </summary>
    void WireMoveNext(EmitContext ctx, int moveNextBranch, int bodyTarget, int exitTarget);

    /// <summary>
    /// Emit init instructions and wire them to the loop entrance.
    /// Returns the overall entry index, or -1 if no init instructions.
    /// </summary>
    int EmitInitAndWire(EmitContext ctx, int loopEnterTarget);

    /// <summary>
    /// Emit cleanup block (e.g. IteratorClose + EndFinally for for-of).
    /// Returns (cleanupEntry, endFinallyIndex), or (-1, -1) if no cleanup.
    /// </summary>
    (int CleanupEntry, int EndFinallyIndex) EmitCleanup(EmitContext ctx, int nextIndex);
}

/// <summary>
/// Configuration for the shared loop skeleton.
/// Captures all the information needed to emit the shared IR structure.
/// </summary>
internal readonly struct LoopSkeletonConfig
{
    // The loop body statement
    public required StatementNode Body { get; init; }

    // Optional binding statement (for-in/for-of loop variable assignment)
    public StatementNode? BindingStatement { get; init; }

    // Post-iteration statements (for-loop increment, emitted with SuppressCompletionValue)
    public ImmutableArray<StatementNode> PostIteration { get; init; }

    // Leading statements (for-loop initializer, emitted with SuppressCompletionValue)
    public ImmutableArray<StatementNode> LeadingStatements { get; init; }

    // Per-iteration binding info
    public ImmutableArray<Symbol> PerIterationBindings { get; init; }
    public int IterationScopeId { get; init; }
    public int IterationSlotCount { get; init; }
    public ImmutableArray<int> PerIterationSlotIndices { get; init; }
    public bool CanReuseIterationEnvironment { get; init; }
    public ImmutableHashSet<Symbol>? LexicalBindings { get; init; }

    // Loop scope (for For-loops with per-iteration bindings)
    // -1 if no loop scope needed
    public int LoopScopeId { get; init; }

    // Do-while: first iteration skips condition, goes directly to body
    public bool ConditionAfterBody { get; init; }

    // For-loop: per-iteration env is created AFTER body, BEFORE PostIteration
    // ForIn/ForOf: per-iteration env is created BEFORE body
    public bool PerIterationEnvAfterBody { get; init; }

    // Try/finally wrapping (for-of: IteratorClose in finally)
    public bool NeedsTryFinally { get; init; }

    public bool HasPerIterationBindings => !PerIterationBindings.IsDefaultOrEmpty;
    public bool HasLoopScope => LoopScopeId >= 0;
    public bool HasPostIteration => !PostIteration.IsDefaultOrEmpty;
    public bool HasLeadingStatements => !LeadingStatements.IsDefaultOrEmpty;
}

internal static class LoopEmitterHelpers
{
    /// <summary>
    /// Emit the shared loop IR structure. Each loop type provides a driver for its
    /// type-specific init, moveNext, and cleanup instructions. The skeleton handles
    /// the shared structure: BreakableEnter/Exit, per-iteration environments, body
    /// building, PostIteration, and try/finally wrapping.
    /// </summary>
    internal static bool EmitLoopSkeleton<TDriver>(
        EmitContext ctx,
        ref TDriver driver,
        in LoopSkeletonConfig config,
        int nextIndex,
        Symbol? label,
        out int entryIndex) where TDriver : struct, ILoopDriver
    {
        var instructionStart = ctx.InstructionCount;
        entryIndex = -1;

        // Pre-compute slot map and names for per-iteration bindings
        ImmutableDictionary<Symbol, int>? slotMap = null;
        ImmutableArray<(Symbol Name, int SlotIndex)> slotNames = default;
        if (config.HasPerIterationBindings)
        {
            slotMap = EmitContext.BuildSlotMap(config.PerIterationBindings, config.PerIterationSlotIndices);
            slotNames = EmitContext.BuildSlotNames(config.PerIterationBindings, config.PerIterationSlotIndices);
        }

        // ================================================================
        // Phase A: Emit MoveNext/condition placeholder
        // ================================================================
        if (!driver.EmitMoveNext(ctx, out var moveNextEntry, out var moveNextBranch))
        {
            ctx.Rollback(instructionStart);
            return false;
        }

        // ================================================================
        // Phase B: Emit exit structure
        // ================================================================
        var cleanupEntry = -1;
        var endFinallyIndex = -1;
        var leaveTryIndex = -1;
        int afterLoopIndex;

        if (config.NeedsTryFinally)
        {
            (cleanupEntry, endFinallyIndex) = driver.EmitCleanup(ctx, nextIndex);
            leaveTryIndex = ctx.Append(new LeaveTryInstruction(nextIndex));
            afterLoopIndex = leaveTryIndex;
        }
        else
        {
            afterLoopIndex = nextIndex;
        }

        var loopExitIndex = ctx.Append(new BreakableExitInstruction(afterLoopIndex));

        // PopEnv chain for break exit path
        var loopExitTarget = loopExitIndex;
        if (config.HasPerIterationBindings)
        {
            if (config.HasLoopScope)
            {
                // Pop loop scope (parent of per-iteration scopes) at loop exit
                loopExitTarget = ctx.Append(new PopEnvironmentInstruction(
                    config.LoopScopeId,
                    config.CanReuseIterationEnvironment,
                    loopExitIndex));
            }

            // Pop per-iteration scope
            loopExitTarget = ctx.Append(new PopEnvironmentInstruction(
                config.IterationScopeId,
                config.CanReuseIterationEnvironment,
                loopExitTarget));
        }

        // ================================================================
        // Phase C: Emit body return path
        // ================================================================

        // Build PostIteration → moveNextEntry (suppressed completion value)
        var postIterEntry = moveNextEntry;
        if (config.HasPostIteration)
        {
            var savedSuppressCompletionValue = ctx.SuppressCompletionValue;
            ctx.SuppressCompletionValue = true;
            var built = ctx.TryBuildStatementList(config.PostIteration, moveNextEntry, out postIterEntry);
            ctx.SuppressCompletionValue = savedSuppressCompletionValue;
            if (!built)
            {
                ctx.Rollback(instructionStart);
                return false;
            }
        }

        // Compute bodyEndTarget and continueTarget based on loop type
        var continueTarget = postIterEntry;
        var bodyEndTarget = postIterEntry;

        if (config.HasPerIterationBindings)
        {
            if (config.PerIterationEnvAfterBody)
            {
                // For-loops: PushEnv(new iteration) AFTER body, BEFORE PostIteration
                // Per ES spec (ForBodyEvaluation step 3.e):
                //   1. Evaluate condition
                //   2. Evaluate body (closures capture current environment)
                //   3. CreatePerIterationEnvironment (create new env, copy values)
                //   4. Evaluate increment (modifies new env, not the captured one)
                var createEnvIndex = ctx.Append(new PushEnvironmentInstruction(
                    postIterEntry,
                    config.PerIterationBindings,
                    config.IterationScopeId,
                    config.IterationSlotCount,
                    slotMap!,
                    config.CanReuseIterationEnvironment,
                    config.LexicalBindings,
                    SlotNames: slotNames));
                continueTarget = createEnvIndex;
                bodyEndTarget = createEnvIndex;
            }
            else
            {
                // ForIn/ForOf: PopEnv on continue path, before jumping back to MoveNext
                var popEnvForContinue = ctx.Append(new PopEnvironmentInstruction(
                    config.IterationScopeId,
                    config.CanReuseIterationEnvironment,
                    moveNextEntry));
                continueTarget = popEnvForContinue;
                bodyEndTarget = popEnvForContinue;
            }
        }

        // ================================================================
        // Phase D: Build body
        // ================================================================
        var targetBoundary = config.IterationScopeId >= 0
            ? new ScopeExitBoundary(config.IterationScopeId, null)
            : ctx.CurrentScopeExitBoundary;
        ctx.PushLoopScope(label, continueTarget, loopExitTarget, targetBoundary);

        var pushedIterationScope = false;
        if (config.HasPerIterationBindings && config.IterationScopeId >= 0)
        {
            ctx.PushScope(config.IterationScopeId, config.CanReuseIterationEnvironment);
            pushedIterationScope = true;
        }

        // Build body statements
        var bodyBuilt = ctx.TryBuildStatement(config.Body, bodyEndTarget, out var bodyEntry, label);

        // Build binding statement (for-in/for-of loop variable assignment)
        if (bodyBuilt && config.BindingStatement is not null)
        {
            bodyBuilt = ctx.TryBuildStatement(config.BindingStatement, bodyEntry, out bodyEntry, label);
        }

        if (pushedIterationScope)
        {
            ctx.PopScope(config.IterationScopeId);
        }

        ctx.PopLoopScope();

        if (!bodyBuilt)
        {
            ctx.Rollback(instructionStart);
            return false;
        }

        // Per-iteration PushEnv BEFORE body (for non-For loops: ForIn/ForOf)
        var iterBodyEntry = bodyEntry;
        if (config.HasPerIterationBindings && !config.PerIterationEnvAfterBody)
        {
            var createEnvIndex = ctx.Append(new PushEnvironmentInstruction(
                bodyEntry,
                config.PerIterationBindings,
                config.IterationScopeId,
                config.IterationSlotCount,
                slotMap!,
                config.CanReuseIterationEnvironment,
                config.LexicalBindings,
                SlotNames: slotNames));
            iterBodyEntry = createEnvIndex;
        }

        // ================================================================
        // Phase E: Wire and emit entry
        // ================================================================

        // Wire MoveNext with body and exit targets
        driver.WireMoveNext(ctx, moveNextBranch, iterBodyEntry, loopExitTarget);

        // Determine loop entry: condition first (normal) or body first (do-while)
        var loopEntryTarget = config.ConditionAfterBody ? iterBodyEntry : moveNextEntry;

        // For-loop specific entry path: initial per-iteration env + leading stmts + loop scope
        if (config.HasPerIterationBindings && config.PerIterationEnvAfterBody)
        {
            // Per ES spec 13.7.4.9 ForBodyEvaluation step 3:
            // CreatePerIterationEnvironment is called BEFORE the first condition test.
            loopEntryTarget = ctx.Append(new PushEnvironmentInstruction(
                loopEntryTarget,
                config.PerIterationBindings,
                config.IterationScopeId,
                config.IterationSlotCount,
                slotMap!,
                config.CanReuseIterationEnvironment,
                config.LexicalBindings,
                SlotNames: slotNames));
        }

        if (config.HasLeadingStatements)
        {
            // Per ES spec 13.7.4.7: initializer doesn't contribute to completion value
            var savedSuppressCompletionValue = ctx.SuppressCompletionValue;
            ctx.SuppressCompletionValue = true;
            var built = ctx.TryBuildStatementList(config.LeadingStatements, loopEntryTarget, out loopEntryTarget);
            ctx.SuppressCompletionValue = savedSuppressCompletionValue;
            if (!built)
            {
                ctx.Rollback(instructionStart);
                return false;
            }
        }

        if (config.HasLoopScope)
        {
            // Per ES spec 13.7.4.7 ForLoopEvaluation steps 3-7:
            // Loop scope environment wraps the initializer.
            // Parent of per-iteration scopes, holds let/const bindings.
            loopEntryTarget = ctx.Append(new PushEnvironmentInstruction(
                loopEntryTarget,
                ImmutableArray<Symbol>.Empty,
                config.LoopScopeId,
                config.IterationSlotCount,
                slotMap!,
                config.CanReuseIterationEnvironment,
                config.LexicalBindings,
                SlotNames: slotNames));
        }

        // BreakableEnter - pushes context for break/continue from AST-evaluated code
        var breakableEnterIndex = ctx.Append(new BreakableEnterInstruction(
            loopEntryTarget,
            label,
            loopExitTarget,
            continueTarget));

        // Try/finally wrapping (for-of)
        var outerTarget = breakableEnterIndex;
        if (config.NeedsTryFinally && cleanupEntry >= 0)
        {
            outerTarget = ctx.Append(new EnterTryInstruction(
                breakableEnterIndex,
                -1,
                null,
                cleanupEntry,
                endFinallyIndex,
                leaveTryIndex,
                continueTarget,
                loopExitTarget));
        }

        // Driver init (ForInInit, IteratorInit, or nothing for condition-based)
        var initIndex = driver.EmitInitAndWire(ctx, outerTarget);
        entryIndex = initIndex >= 0 ? initIndex : outerTarget;

        return true;
    }
}
