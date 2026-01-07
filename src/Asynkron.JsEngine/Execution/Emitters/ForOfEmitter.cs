#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for for-of and for-await-of loops.
/// These are iterator-based loops that require try/finally for iterator cleanup.
/// </summary>
internal static class ForOfEmitter
{
    /// <summary>
    /// Emit IR for a for-of or for-await-of statement.
    /// </summary>
    public static bool TryEmit(
        EmitContext ctx,
        ForEachStatement statement,
        int nextIndex,
        Symbol? label,
        out int entryIndex)
    {
        if (AstShapeAnalyzer.ContainsYield(statement.Iterable))
        {
            entryIndex = -1;
            return false;
        }

        // Use the cached iterator plan to ensure any synthetic scope ids (for per-iteration bindings)
        // are stable across IR emission, slot analysis, and later plan stamping.
        var iteratorPlan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();

        var instructionStart = ctx.InstructionCount;
        var lexicalBindings = iteratorPlan.PerIterationBindings.IsDefaultOrEmpty
            ? null
            : iteratorPlan.PerIterationBindings.ToImmutableHashSet(ReferenceEqualityComparer<Symbol>.Instance);

        // Build the structure bottom-up:
        // 1. EndFinally -> nextIndex
        // 2. IteratorClose -> EndFinally
        // 3. LeaveTry -> nextIndex
        // 4. Body -> MoveNext
        // 5. MoveNext -> Body, BreakIndex -> LeaveTry
        // 6. EnterTry -> MoveNext, finally -> IteratorClose
        // 7. IteratorInit -> EnterTry

        // Pre-create symbols and allocate slots for O(1) access
        var iteratorSymbol = Symbol.Intern(iteratorPlan.Kind == IteratorDriverKind.Await
            ? $"__forAwait_iter_{instructionStart}"
            : $"__forOf_iter_{instructionStart}");
        var valueSymbol = Symbol.Intern(iteratorPlan.Kind == IteratorDriverKind.Await
            ? $"__forAwait_value_{instructionStart}"
            : $"__forOf_value_{instructionStart}");

        var iteratorSlotIndex = ctx.AllocateSlot(iteratorSymbol);
        var valueSlotIndex = ctx.AllocateSlot(valueSymbol);

        // First, create the iterator instructions with pre-allocated slots
        var iteratorInstructions =
            IteratorInstructionTemplate.AppendInstructions(ctx.Instructions, iteratorPlan,
                -1, // breakIndex will be LeaveTry
                iteratorSymbol,
                valueSymbol,
                iteratorSlotIndex,
                valueSlotIndex);

        // EndFinally - this is the end of the finally block
        var endFinallyIndex = ctx.Append(new EndFinallyInstruction(nextIndex));

        // IteratorClose - the finally block content
        var iteratorCloseIndex =
            ctx.Append(new IteratorCloseInstruction(iteratorInstructions.IteratorSlot, endFinallyIndex));

        // LeaveTry - normal exit from the loop
        var leaveTryIndex = ctx.Append(new LeaveTryInstruction(nextIndex));

        // BreakableExit - pops breakable context from runtime stack, flows to LeaveTry
        // This enables break/continue from AST-evaluated code (StatementInstruction)
        // to resolve their jump targets using the runtime breakable stack.
        var loopExitIndex = ctx.Append(new BreakableExitInstruction(leaveTryIndex));

        // For loops with per-iteration bindings, create PopEnvironment before BreakableExit
        var loopExitTarget = loopExitIndex;
        if (iteratorPlan.DeclarationKind is VariableKind.Let or VariableKind.Const &&
            !iteratorPlan.PerIterationBindings.IsDefaultOrEmpty)
        {
            loopExitTarget = ctx.Append(new PopEnvironmentInstruction(
                iteratorPlan.IterationScopeId,
                iteratorPlan.CanReuseIterationEnvironment,
                loopExitIndex));
        }

        // Now update the MoveNext break target to point to Pop (or LeaveTry if no per-iteration bindings)
        ctx.Patch(iteratorInstructions.MoveNextIndex,
            (IteratorMoveNextInstruction)ctx.Instructions[iteratorInstructions.MoveNextIndex] with
            {
                BreakIndex = loopExitTarget
            });

        // Build the loop body.
        // IMPORTANT: Do NOT wrap the per-iteration binding in a synthetic BlockStatement with a lexical
        // declaration. That causes TryBuildStatement(BlockStatement) to fall back to StatementInstruction
        // (HoistPlan.NeedsEnvironment) which prevents the IR loop machinery from handling break/continue
        // correctly. Build the binding statement and the user body as a straight instruction chain instead.
        var bindingStatement = EmitContext.CreateIteratorBindingStatement(iteratorPlan, iteratorInstructions.ValueSlot,
            iteratorInstructions.ValueSlotIndex);
        var targetScopeId = iteratorPlan.IterationScopeId >= 0 ? iteratorPlan.IterationScopeId : -1;

        if (!LoopEmitterHelpers.TryBuildLoopBody(ctx, iteratorPlan, iteratorPlan.Body, bindingStatement,
                label, iteratorInstructions.MoveNextIndex, loopExitTarget, targetScopeId, lexicalBindings,
                instructionStart, out var loopBodyResult))
        {
            entryIndex = -1;
            return false;
        }

        var continueTarget = loopBodyResult.ContinueTarget;

        // Wire up the MoveNext to point to the loop entry (env instruction or body)
        IteratorInstructionTemplate.Wire(iteratorInstructions, loopBodyResult.LoopEntry, ctx.Instructions);

        // BreakableEnter - pushes context to runtime stack for break/continue from AST-evaluated code.
        // Default is ResetsCompletionValue - loops need runtime to reset completion value per ES spec.
        var loopEnterIndex = ctx.Append(new BreakableEnterInstruction(
            iteratorInstructions.MoveNextIndex,
            label,
            loopExitTarget,
            continueTarget));

        // EnterTry - wraps the loop in a try/finally, points to BreakableEnter
        // Pass the loop continue target so that continue within the loop skips the finally
        // (we don't want to close the iterator on continue, only on break/throw/return)
        var enterTryIndex =
            ctx.Append(new EnterTryInstruction(
                loopEnterIndex,
                -1,
                null,
                iteratorCloseIndex,
                endFinallyIndex,
                continueTarget,
                loopExitTarget));

        // Wire IteratorInit to point to EnterTry
        ctx.Patch(iteratorInstructions.InitIndex,
            (IteratorInitInstruction)ctx.Instructions[iteratorInstructions.InitIndex] with { Next = enterTryIndex });

        entryIndex = iteratorInstructions.InitIndex;
        return true;
    }
}
