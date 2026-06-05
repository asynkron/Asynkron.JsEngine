using System.Collections.Immutable;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeWithDepthAnalysis
{
    public static bool TryBuildActiveWithDepths(
        ImmutableArray<ExecutionInstruction> instructions,
        int entryPoint,
        out int[] activeWithDepths,
        out string reason)
    {
        activeWithDepths = new int[instructions.Length];
        Array.Fill(activeWithDepths, -1);

        if ((uint)entryPoint >= (uint)instructions.Length)
        {
            reason = "Unsupported entrypoint.";
            return false;
        }

        var pending = new Stack<(int InstructionIndex, int WithDepth)>();
        pending.Push((entryPoint, 0));
        while (pending.Count > 0)
        {
            var (instructionIndex, withDepth) = pending.Pop();
            if ((uint)instructionIndex >= (uint)instructions.Length)
            {
                reason = "Instruction flow reached an invalid target index.";
                return false;
            }

            if (withDepth < 0)
            {
                reason = "Instruction flow leaves a with environment when none is active.";
                return false;
            }

            var existingDepth = activeWithDepths[instructionIndex];
            if (existingDepth >= 0)
            {
                if (existingDepth != withDepth)
                {
                    reason = "Instruction flow reaches the same instruction with inconsistent with-environment depth.";
                    return false;
                }

                continue;
            }

            activeWithDepths[instructionIndex] = withDepth;
            var instruction = instructions[instructionIndex];
            if (!TryGetSuccessorWithDepth(instruction, withDepth, out var successorWithDepth, out reason))
            {
                return false;
            }

            switch (instruction)
            {
                case BranchInstruction branch:
                    if (!TryPush(branch.AlternateIndex, successorWithDepth, instructions, pending, out reason) ||
                        !TryPush(branch.ConsequentIndex, successorWithDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    break;

                case EnterTryInstruction enterTry:
                    if (!TryPush(enterTry.Next, successorWithDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    if ((enterTry.HandlerIndex >= 0 &&
                         !TryPush(enterTry.HandlerIndex, successorWithDepth, instructions, pending, out reason)) ||
                        (enterTry.FinallyIndex >= 0 &&
                         !TryPush(enterTry.FinallyIndex, successorWithDepth, instructions, pending, out reason)))
                    {
                        return false;
                    }

                    break;

                case JumpInstruction jump:
                    if (!TryPush(jump.TargetIndex, successorWithDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    break;

                case BreakInstruction breakInstruction:
                    if (!TryPush(breakInstruction.TargetIndex, successorWithDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    break;

                case ContinueInstruction continueInstruction:
                    if (!TryPush(continueInstruction.TargetIndex, successorWithDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    break;

                case ReturnInstruction:
                case ThrowInstruction:
                    break;

                default:
                    if (instruction.Next >= 0 &&
                        !TryPush(instruction.Next, successorWithDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    break;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryGetSuccessorWithDepth(
        ExecutionInstruction instruction,
        int withDepth,
        out int successorWithDepth,
        out string reason)
    {
        switch (instruction)
        {
            case EnterWithInstruction:
                successorWithDepth = withDepth + 1;
                reason = string.Empty;
                return true;

            case LeaveWithInstruction:
                if (withDepth == 0)
                {
                    successorWithDepth = 0;
                    reason = "LeaveWith instruction is not preceded by an active with environment.";
                    return false;
                }

                successorWithDepth = withDepth - 1;
                reason = string.Empty;
                return true;

            default:
                successorWithDepth = withDepth;
                reason = string.Empty;
                return true;
        }
    }

    private static bool TryPush(
        int instructionIndex,
        int withDepth,
        ImmutableArray<ExecutionInstruction> instructions,
        Stack<(int InstructionIndex, int WithDepth)> pending,
        out string reason)
    {
        if ((uint)instructionIndex >= (uint)instructions.Length)
        {
            reason = "Instruction flow reached an invalid target index.";
            return false;
        }

        pending.Push((instructionIndex, withDepth));
        reason = string.Empty;
        return true;
    }
}
