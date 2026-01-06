namespace Asynkron.JsEngine.Execution.Instructions;

/// <summary>
/// Extension methods for ExecutionInstruction traversal.
/// </summary>
internal static class ExecutionInstructionExtensions
{
    /// <summary>
    /// Gets the successor instruction indices for control flow analysis.
    /// </summary>
    public static IEnumerable<int> GetSuccessors(this ExecutionInstruction instruction)
    {
        switch (instruction)
        {
            case BranchInstruction branch:
                yield return branch.ConsequentIndex;
                yield return branch.AlternateIndex;
                yield break;
            case EnterTryInstruction enterTry:
                if (enterTry.Next >= 0)
                {
                    yield return enterTry.Next;
                }
                if (enterTry.HandlerIndex >= 0)
                {
                    yield return enterTry.HandlerIndex;
                }
                if (enterTry.FinallyIndex >= 0)
                {
                    yield return enterTry.FinallyIndex;
                }
                if (enterTry.EndFinallyIndex >= 0)
                {
                    yield return enterTry.EndFinallyIndex;
                }
                if (enterTry.LoopContinueTarget >= 0)
                {
                    yield return enterTry.LoopContinueTarget;
                }
                if (enterTry.LoopBreakTarget >= 0)
                {
                    yield return enterTry.LoopBreakTarget;
                }
                yield break;
            case IteratorMoveNextInstruction moveNext:
                if (moveNext.Next >= 0)
                {
                    yield return moveNext.Next;
                }
                if (moveNext.BreakIndex >= 0)
                {
                    yield return moveNext.BreakIndex;
                }
                yield break;
            case JumpInstruction jump:
                yield return jump.TargetIndex;
                yield break;
            default:
                if (instruction.Next >= 0)
                {
                    yield return instruction.Next;
                }

                yield break;
        }
    }
}
