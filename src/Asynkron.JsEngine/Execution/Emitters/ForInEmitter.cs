#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for for-in loops.
/// For-in loops enumerate property keys from an object and its prototype chain.
/// Unlike for-of, there is no iterator protocol - property keys are collected upfront.
/// </summary>
internal static class ForInEmitter
{
    /// <summary>
    /// Emit IR for a for-in statement.
    /// </summary>
    public static bool TryEmit(
        EmitContext ctx,
        ForEachStatement statement,
        int nextIndex,
        Symbol? label,
        out int entryIndex)
    {
        // for-in should only be called for ForEachKind.In
        if (statement.Kind != ForEachKind.In)
        {
            entryIndex = -1;
            return false;
        }

        // Yield in the object expression is not supported in IR
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

        // Pre-create symbols and allocate slots for O(1) access
        var stateSymbol = Symbol.Intern($"__forIn_state_{instructionStart}");
        var valueSymbol = Symbol.Intern($"__forIn_value_{instructionStart}");

        var stateSlotIndex = ctx.AllocateSlot(stateSymbol);
        var valueSlotIndex = ctx.AllocateSlot(valueSymbol);

        // For let/const declarations, the per-iteration bindings need TDZ during object evaluation.
        // This ensures `for (const x in {[x]: 1})` throws ReferenceError for accessing x before initialization.
        var tdzBindings =
            iteratorPlan.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                or VariableKind.AwaitUsing
                ? iteratorPlan.PerIterationBindings
                : default;
        var tdzIsConst = iteratorPlan.DeclarationKind is VariableKind.Const or VariableKind.Using
            or VariableKind.AwaitUsing;

        // Build the structure bottom-up:
        // 1. BreakableExit -> nextIndex
        // 2. Body -> MoveNext (with PopEnvironment for per-iteration bindings)
        // 3. MoveNext -> Body, BreakIndex -> BreakableExit
        // 4. BreakableEnter -> MoveNext
        // 5. ForInInit -> BreakableEnter

        // Create ForInInit instruction
        var initIndex = ctx.Append(new ForInInitInstruction(
            statement.Iterable,
            stateSymbol,
            stateSlotIndex,
            valueSymbol,
            valueSlotIndex,
            -1, // Will be wired later
            tdzBindings,
            tdzIsConst));

        // Create ForInMoveNext instruction
        var moveNextIndex = ctx.Append(new ForInMoveNextInstruction(
            stateSymbol,
            valueSymbol,
            stateSlotIndex,
            valueSlotIndex,
            -1, // BreakIndex will be patched later
            -1)); // Next (body) will be wired later

        // BreakableExit - pops breakable context from runtime stack
        var loopExitIndex = ctx.Append(new BreakableExitInstruction(nextIndex));

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

        // Now update the MoveNext break target
        ctx.Patch(moveNextIndex,
            (ForInMoveNextInstruction)ctx.Instructions[moveNextIndex] with
            {
                BreakIndex = loopExitTarget
            });

        // Build the loop body.
        // Create the binding statement for the loop variable
        var bindingStatement = EmitContext.CreateIteratorBindingStatement(iteratorPlan, valueSymbol, valueSlotIndex);
        var targetScopeId = iteratorPlan.IterationScopeId >= 0 ? iteratorPlan.IterationScopeId : -1;

        // For per-iteration bindings, we need to POP the iteration environment at the END of each
        // iteration body, BEFORE going back to FORIN_MOVE_NEXT. This ensures the environment stack
        // stays balanced and we return to the correct scope for the next iteration.
        var bodyNextTarget = moveNextIndex;
        var continueTarget = moveNextIndex;
        if (iteratorPlan.DeclarationKind is VariableKind.Let or VariableKind.Const &&
            !iteratorPlan.PerIterationBindings.IsDefaultOrEmpty)
        {
            // Create POP_ENV that goes to FORIN_MOVE_NEXT - body will flow to this
            var popEnvForContinue = ctx.Append(new PopEnvironmentInstruction(
                iteratorPlan.IterationScopeId,
                iteratorPlan.CanReuseIterationEnvironment,
                moveNextIndex));
            bodyNextTarget = popEnvForContinue;
            continueTarget = popEnvForContinue;
        }

        ctx.PushLoopScope(label, continueTarget, loopExitTarget, targetScopeId);
        var pushedIterationScope = false;
        if (iteratorPlan.IterationScopeId >= 0 &&
            iteratorPlan.DeclarationKind is VariableKind.Let or VariableKind.Const &&
            !iteratorPlan.PerIterationBindings.IsDefaultOrEmpty)
        {
            ctx.PushScope(iteratorPlan.IterationScopeId);
            pushedIterationScope = true;
        }
        var iterationEntry = -1;
        var bodyBuilt = ctx.TryBuildStatement(iteratorPlan.Body, bodyNextTarget, out var bodyEntry, label);
        if (bodyBuilt)
        {
            bodyBuilt = ctx.TryBuildStatement(bindingStatement, bodyEntry, out iterationEntry, label);
        }

        if (pushedIterationScope)
        {
            ctx.PopScope(iteratorPlan.IterationScopeId);
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
            var slotMap =
                EmitContext.BuildSlotMap(iteratorPlan.PerIterationBindings, iteratorPlan.PerIterationSlotIndices);
            var slotNames =
                EmitContext.BuildSlotNames(iteratorPlan.PerIterationBindings, iteratorPlan.PerIterationSlotIndices);

            var createEnvIndex = ctx.Append(new PushEnvironmentInstruction(
                iterationEntry,
                iteratorPlan.PerIterationBindings,
                iteratorPlan.IterationScopeId,
                iteratorPlan.IterationSlotCount,
                slotMap,
                iteratorPlan.CanReuseIterationEnvironment,
                lexicalBindings,
                SlotNames: slotNames));
            loopEntry = createEnvIndex;
        }

        // Wire up the MoveNext to point to the loop entry (env instruction or body)
        ctx.Patch(moveNextIndex,
            (ForInMoveNextInstruction)ctx.Instructions[moveNextIndex] with { Next = loopEntry });

        // BreakableEnter - pushes context to runtime stack for break/continue
        var loopEnterIndex = ctx.Append(new BreakableEnterInstruction(
            moveNextIndex,
            label,
            loopExitTarget,
            continueTarget));

        // Wire ForInInit to point to BreakableEnter
        ctx.Patch(initIndex,
            (ForInInitInstruction)ctx.Instructions[initIndex] with { Next = loopEnterIndex });

        entryIndex = initIndex;
        return true;
    }
}
