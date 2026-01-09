#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for destructuring patterns in variable declarations.
/// Phase 1: Simple array destructuring with identifier-only elements (no defaults, no nested patterns).
/// </summary>
internal static class DestructuringEmitter
{
    /// <summary>
    /// Try to emit IR for an array destructuring declaration.
    /// Only handles simple cases: identifier bindings, no defaults, no nested patterns.
    /// </summary>
    /// <returns>True if IR was emitted, false if should fall back to ComplexVariableDeclarationInstruction.</returns>
    public static bool TryEmitArrayDestructuring(
        EmitContext ctx,
        ArrayBinding binding,
        ExpressionNode sourceExpression,
        VariableKind varKind,
        int nextIndex,
        out int entryIndex)
    {
        // Don't emit destructuring IR when in a nested scope (e.g., per-iteration scope for for-of).
        // The slot count was pre-computed and additional slot allocations won't work correctly.
        if (ctx.IsInNestedScope)
        {
            entryIndex = -1;
            return false;
        }

        // Phase 1: Only handle simple cases
        if (!IsSimpleArrayBinding(binding))
        {
            entryIndex = -1;
            return false;
        }

        // Create iterator slot with globally unique symbol
        var iteratorSymbol = Symbol.Synthetic("__arrDestr_iter");
        var iteratorSlotIndex = ctx.AllocateSlot(iteratorSymbol);

        // Build instruction chain bottom-up:
        // Close -> nextIndex
        // Rest (if any) -> Close
        // Elements -> Rest or Close
        // Init -> first Element

        // Close instruction
        var closeIndex = ctx.Append(new ArrayDestructuringCloseInstruction(
            iteratorSymbol,
            iteratorSlotIndex,
            nextIndex));

        var currentNext = closeIndex;

        // Rest element (if any)
        if (binding.RestElement is IdentifierBinding restId)
        {
            var restIndex = ctx.Append(new ArrayDestructuringRestInstruction(
                iteratorSymbol,
                iteratorSlotIndex,
                restId.Name,
                -1, // Resolved at runtime
                varKind,
                currentNext));
            currentNext = restIndex;
        }

        // Elements (in reverse order to build chain correctly)
        for (var i = binding.Elements.Length - 1; i >= 0; i--)
        {
            var element = binding.Elements[i];

            // Handle holes (null target) and identifier bindings
            Symbol? targetSymbol = null;

            if (element.Target is IdentifierBinding id)
            {
                targetSymbol = id.Name;
            }
            // else: hole - targetSymbol stays null

            var elementIndex = ctx.Append(new ArrayDestructuringElementInstruction(
                iteratorSymbol,
                iteratorSlotIndex,
                targetSymbol,
                -1, // Resolved at runtime
                varKind,
                currentNext));
            currentNext = elementIndex;
        }

        // Init instruction
        var initIndex = ctx.Append(new ArrayDestructuringInitInstruction(
            iteratorSymbol,
            iteratorSlotIndex,
            currentNext,
            SourceExpression: sourceExpression));

        entryIndex = initIndex;
        return true;
    }

    /// <summary>
    /// Check if this array binding can be handled by Phase 1 IR emission.
    /// Phase 1 only handles: identifier bindings, no defaults, no nested patterns.
    /// </summary>
    private static bool IsSimpleArrayBinding(ArrayBinding binding)
    {
        // Check all elements
        foreach (var element in binding.Elements)
        {
            // Holes are OK (null target)
            if (element.Target is null)
            {
                continue;
            }

            // Must be simple identifier binding
            if (element.Target is not IdentifierBinding)
            {
                return false;
            }

            // No default values in Phase 1
            if (element.DefaultValue is not null)
            {
                return false;
            }
        }

        // Rest element must be identifier (if present)
        return binding.RestElement is null || binding.RestElement is IdentifierBinding;
    }
}
