using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

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

        var planBody = statement.Body is BlockStatement blockBody
            ? blockBody
            : new BlockStatement(statement.Source, [statement.Body], EmitContext.IsStrictBlock(statement.Body));
        var iteratorPlan = IteratorDriverFactory.CreatePlan(statement, planBody);

        var instructionStart = ctx.InstructionCount;

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

        // LoopExit - pops loop context from runtime stack, flows to LeaveTry
        // This enables break/continue from AST-evaluated code (StatementInstruction)
        // to resolve their jump targets using the runtime loop stack.
        var loopExitIndex = ctx.Append(new LoopExitInstruction(leaveTryIndex));

        // For loops with per-iteration bindings, create PopEnvironment before LoopExit
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

        // For per-iteration bindings, we need to POP the iteration environment at the END of each
        // iteration body, BEFORE going back to ITER_MOVE_NEXT. This ensures the environment stack
        // stays balanced and we return to the correct scope for the next iteration.
        var bodyNextTarget = iteratorInstructions.MoveNextIndex;
        var continueTarget = iteratorInstructions.MoveNextIndex;
        if (iteratorPlan.DeclarationKind is VariableKind.Let or VariableKind.Const &&
            !iteratorPlan.PerIterationBindings.IsDefaultOrEmpty)
        {
            // Create POP_ENV that goes to ITER_MOVE_NEXT - body will flow to this
            var popEnvForContinue = ctx.Append(new PopEnvironmentInstruction(
                iteratorPlan.IterationScopeId,
                iteratorPlan.CanReuseIterationEnvironment,
                iteratorInstructions.MoveNextIndex));
            bodyNextTarget = popEnvForContinue;
            continueTarget = popEnvForContinue;
        }

        ctx.PushLoopScope(label, continueTarget, loopExitTarget, targetScopeId);
        var iterationEntry = -1;
        var bodyBuilt = ctx.TryBuildStatement(iteratorPlan.Body, bodyNextTarget, out var bodyEntry, label);
        if (bodyBuilt)
        {
            bodyBuilt = ctx.TryBuildStatement(bindingStatement, bodyEntry, out iterationEntry, label);
        }
        ctx.PopLoopScope();

        if (!bodyBuilt)
        {
            ctx.Rollback(instructionStart);
            entryIndex = -1;
            return false;
        }

        // For lexical declarations (let/const), emit PushEnvironmentInstruction
        // to create fresh per-iteration bindings. This ensures closures capture separate values.
        var loopEntry = iterationEntry;
        if (iteratorPlan.DeclarationKind is VariableKind.Let or VariableKind.Const &&
            !iteratorPlan.PerIterationBindings.IsDefaultOrEmpty)
        {
            var slotMap = EmitContext.BuildSlotMap(iteratorPlan.PerIterationBindings, iteratorPlan.PerIterationSlotIndices);

            var createEnvIndex = ctx.Append(new PushEnvironmentInstruction(
                iterationEntry,
                iteratorPlan.PerIterationBindings,
                iteratorPlan.IterationScopeId,
                iteratorPlan.IterationSlotCount,
                slotMap,
                iteratorPlan.CanReuseIterationEnvironment));
            loopEntry = createEnvIndex;
        }

        // Wire up the MoveNext to point to the loop entry (env instruction or body)
        IteratorInstructionTemplate.Wire(iteratorInstructions, loopEntry, ctx.Instructions);

        // LoopEnter - pushes loop context to runtime stack for break/continue from AST-evaluated code
        var loopEnterIndex = ctx.Append(new LoopEnterInstruction(
            iteratorInstructions.MoveNextIndex,
            label,
            loopExitTarget,
            continueTarget));

        // EnterTry - wraps the loop in a try/finally, points to LoopEnter
        var enterTryIndex =
            ctx.Append(new EnterTryInstruction(loopEnterIndex, -1, null, iteratorCloseIndex));

        // Wire IteratorInit to point to EnterTry
        ctx.Patch(iteratorInstructions.InitIndex,
            (IteratorInitInstruction)ctx.Instructions[iteratorInstructions.InitIndex] with { Next = enterTryIndex });

        entryIndex = iteratorInstructions.InitIndex;
        return true;
    }
}
