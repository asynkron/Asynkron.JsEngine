using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeCompiler
{
    public static bool TryCompile(
        ExecutionPlan plan,
        out UnifiedBytecodeProgram program,
        out string reason)
    {
        if ((uint)plan.EntryPoint >= (uint)plan.Instructions.Length)
        {
            program = EmptyProgram();
            reason = "Unsupported entrypoint.";
            return false;
        }

        if (plan.Instructions[plan.EntryPoint] is not ReturnInstruction { ReturnProgram: { } returnProgram, AwaitedProgram: null })
        {
            program = EmptyProgram();
            reason = "Entrypoint must be a non-awaited ReturnInstruction with ReturnProgram.";
            return false;
        }

        if (plan.ActivationSlots is null)
        {
            program = EmptyProgram();
            reason = "Activation slot metadata is required.";
            return false;
        }

        var unified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>(returnProgram.OperationCount + 1);
        foreach (var operation in returnProgram.EnumerateOperations())
        {
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadIdentifier:
                    if (operation.IsArguments)
                    {
                        program = EmptyProgram();
                        reason = "arguments is not supported.";
                        return false;
                    }

                    var identifier = operation.GetIdentifier(returnProgram.IdentifierConstants.AsSpan());
                    if (!TryResolveParameterSlot(identifier, plan.ActivationSlots, out var slotIndex))
                    {
                        program = EmptyProgram();
                        reason = $"Unsupported identifier '{identifier.Name.Name}'.";
                        return false;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, slotIndex));
                    break;

                case ExpressionOpKind.Binary when operation.Operator == BinaryOperator.Add:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Add));
                    break;

                default:
                    program = EmptyProgram();
                    reason = $"Unsupported expression op '{operation.Kind}'.";
                    return false;
            }
        }

        unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return));
        program = new UnifiedBytecodeProgram(unified.MoveToImmutable(), returnProgram.MaxStackDepth);
        reason = string.Empty;
        return true;
    }

    private static UnifiedBytecodeProgram EmptyProgram() => new(ImmutableArray<UnifiedBytecodeInstruction>.Empty, 0);

    private static bool TryResolveParameterSlot(IdentifierOperand identifier, ActivationSlotShape activationSlots, out int slotIndex)
    {
        if (identifier.ScopeId == activationSlots.ScopeId &&
            identifier.SlotIndex >= 0 &&
            ContainsParameterSlot(activationSlots.ParameterSlotIndices, identifier.SlotIndex))
        {
            slotIndex = identifier.SlotIndex;
            return true;
        }

        if (activationSlots.SlotMap.TryGetValue(identifier.Name, out var mappedSlot) &&
            ContainsParameterSlot(activationSlots.ParameterSlotIndices, mappedSlot))
        {
            slotIndex = mappedSlot;
            return true;
        }

        slotIndex = -1;
        return false;
    }

    private static bool ContainsParameterSlot(ImmutableArray<int> parameterSlots, int candidate)
    {
        foreach (var parameterSlot in parameterSlots)
        {
            if (parameterSlot == candidate)
            {
                return true;
            }
        }

        return false;
    }
}
