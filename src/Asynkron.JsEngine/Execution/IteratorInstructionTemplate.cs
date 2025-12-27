#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

internal static class IteratorInstructionTemplate
{
    /// <summary>
    /// Appends iterator instructions with pre-allocated symbols and slot indices for O(1) value access.
    /// </summary>
    /// <param name="instructions">The instruction list to append to.</param>
    /// <param name="plan">The iterator driver plan.</param>
    /// <param name="breakIndex">Jump target when iteration completes.</param>
    /// <param name="iteratorSymbol">Pre-created symbol for the iterator state.</param>
    /// <param name="valueSymbol">Pre-created symbol for the iteration value.</param>
    /// <param name="iteratorSlotIndex">Pre-allocated slot index for the iterator state.</param>
    /// <param name="valueSlotIndex">Pre-allocated slot index for the iteration value.</param>
    /// <returns>The instruction plan with slot indices.</returns>
    public static IteratorInstructionPlan AppendInstructions(List<ExecutionInstruction> instructions,
        IteratorDriverPlan plan,
        int breakIndex,
        Symbol iteratorSymbol,
        Symbol valueSymbol,
        int iteratorSlotIndex,
        int valueSlotIndex)
    {
        var initIndex = instructions.Count;
        instructions.Add(new IteratorInitInstruction(plan.Kind, plan.Iterable, iteratorSymbol, iteratorSlotIndex, -1));

        var moveNextIndex = instructions.Count;
        instructions.Add(new IteratorMoveNextInstruction(plan.Kind, iteratorSymbol, valueSymbol, iteratorSlotIndex, valueSlotIndex, breakIndex, -1));

        return new IteratorInstructionPlan(iteratorSymbol, valueSymbol, iteratorSlotIndex, valueSlotIndex, initIndex, moveNextIndex);
    }

    public static void Wire(IteratorInstructionPlan plan, int bodyEntryIndex, List<ExecutionInstruction> instructions)
    {
        instructions[plan.MoveNextIndex] =
            (IteratorMoveNextInstruction)instructions[plan.MoveNextIndex] with { Next = bodyEntryIndex };

        instructions[plan.InitIndex] =
            (IteratorInitInstruction)instructions[plan.InitIndex] with { Next = plan.MoveNextIndex };
    }
}
