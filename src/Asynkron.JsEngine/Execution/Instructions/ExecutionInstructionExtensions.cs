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
