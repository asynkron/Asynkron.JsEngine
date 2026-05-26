using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeCompiler
{
    public static bool TryCompile(
        ExecutionPlan plan,
        bool isAsync,
        bool isGenerator,
        out UnifiedBytecodeProgram program,
        out string reason)
    {
        if (isAsync || isGenerator)
        {
            program = EmptyProgram();
            reason = "Async and generator functions are not eligible for unified bytecode compilation.";
            return false;
        }

        if ((uint)plan.EntryPoint >= (uint)plan.Instructions.Length)
        {
            program = EmptyProgram();
            reason = "Unsupported entrypoint.";
            return false;
        }

        if (plan.ActivationSlots is null)
        {
            program = EmptyProgram();
            reason = "Activation slot metadata is required.";
            return false;
        }

        var entryInstruction = plan.Instructions[plan.EntryPoint];
        if (entryInstruction is ReturnInstruction { ReturnProgram: { } returnProgram, AwaitedProgram: null })
        {
            var unified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>(returnProgram.OperationCount + 1);
            if (!TryAppendExpressionProgramOps(returnProgram, plan.ActivationSlots, unified, out reason))
            {
                program = EmptyProgram();
                return false;
            }

            unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return));
            program = new UnifiedBytecodeProgram(unified.MoveToImmutable(), returnProgram.MaxStackDepth);
            reason = string.Empty;
            return true;
        }

        if (entryInstruction is not SimpleVariableDeclarationInstruction
            {
                InitializerProgram: { } initializerProgram,
                AwaitedProgram: null,
                TargetSymbol: { } targetSymbol
            } declaration)
        {
            program = EmptyProgram();
            reason = "Entrypoint must be a non-awaited ReturnInstruction or a simple local declaration followed by return.";
            return false;
        }

        if ((uint)declaration.Next >= (uint)plan.Instructions.Length ||
            plan.Instructions[declaration.Next] is not ReturnInstruction { ReturnProgram: { } linearReturnProgram, AwaitedProgram: null })
        {
            program = EmptyProgram();
            reason = "Simple declaration entrypoint must be followed immediately by a non-awaited ReturnInstruction with ReturnProgram.";
            return false;
        }

        if (!TryResolveActivationSlot(targetSymbol, plan.ActivationSlots, out var storeSlot))
        {
            program = EmptyProgram();
            reason = $"Unsupported declaration target '{targetSymbol.Name}'.";
            return false;
        }

        var capacity = initializerProgram.OperationCount + linearReturnProgram.OperationCount + 2;
        var linearUnified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>(capacity);
        if (!TryAppendExpressionProgramOps(initializerProgram, plan.ActivationSlots, linearUnified, out reason))
        {
            program = EmptyProgram();
            return false;
        }

        linearUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.StoreSlot, storeSlot));
        if (!TryAppendExpressionProgramOps(linearReturnProgram, plan.ActivationSlots, linearUnified, out reason))
        {
            program = EmptyProgram();
            return false;
        }

        linearUnified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return));
        program = new UnifiedBytecodeProgram(
            linearUnified.MoveToImmutable(),
            Math.Max(initializerProgram.MaxStackDepth, linearReturnProgram.MaxStackDepth));
        reason = string.Empty;
        return true;
    }

    private static UnifiedBytecodeProgram EmptyProgram() => new(ImmutableArray<UnifiedBytecodeInstruction>.Empty, 0);

    private static bool TryAppendExpressionProgramOps(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        out string reason)
    {
        foreach (var operation in expressionProgram.EnumerateOperations())
        {
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadIdentifier:
                    if (operation.IsArguments)
                    {
                        reason = "arguments is not supported.";
                        return false;
                    }

                    var identifier = operation.GetIdentifier(expressionProgram.IdentifierConstants.AsSpan());
                    if (!TryResolveActivationSlot(identifier, activationSlots, out var slotIndex))
                    {
                        reason = $"Unsupported identifier '{identifier.Name.Name}'.";
                        return false;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadSlot, slotIndex));
                    break;

                case ExpressionOpKind.Binary when operation.Operator == BinaryOperator.Add:
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Binary, (int)operation.Operator));
                    break;

                default:
                    reason = $"Unsupported expression op '{operation.Kind}'.";
                    return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryResolveActivationSlot(IdentifierOperand identifier, ActivationSlotShape activationSlots, out int slotIndex)
    {
        if (identifier.ScopeId == activationSlots.ScopeId && identifier.SlotIndex >= 0)
        {
            slotIndex = identifier.SlotIndex;
            return true;
        }

        if (activationSlots.SlotMap.TryGetValue(identifier.Name, out var mappedSlot))
        {
            slotIndex = mappedSlot;
            return true;
        }

        slotIndex = -1;
        return false;
    }

    private static bool TryResolveActivationSlot(Symbol symbol, ActivationSlotShape activationSlots, out int slotIndex) =>
        activationSlots.SlotMap.TryGetValue(symbol, out slotIndex);
}
