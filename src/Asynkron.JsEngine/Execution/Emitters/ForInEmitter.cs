#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;

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
        // Use Synthetic() for globally unique symbols (Intern + instructionStart is only unique per-plan)
        var stateSymbol = Symbol.Synthetic("__forIn_state");
        var valueSymbol = Symbol.Synthetic("__forIn_value");

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
        var bindingStatement = ctx.CreateIteratorBindingStatement(iteratorPlan, valueSymbol, valueSlotIndex);
        var targetScopeId = iteratorPlan.IterationScopeId >= 0 ? iteratorPlan.IterationScopeId : -1;

        if (!LoopEmitterHelpers.TryBuildLoopBody(ctx, iteratorPlan, iteratorPlan.Body, bindingStatement,
                label, moveNextIndex, loopExitTarget, targetScopeId, lexicalBindings,
                instructionStart, out var loopBodyResult))
        {
            entryIndex = -1;
            return false;
        }

        var continueTarget = loopBodyResult.ContinueTarget;

        // Wire up the MoveNext to point to the loop entry (env instruction or body)
        ctx.Patch(moveNextIndex,
            (ForInMoveNextInstruction)ctx.Instructions[moveNextIndex] with { Next = loopBodyResult.LoopEntry });

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
