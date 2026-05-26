using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

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

        var instructionIndex = plan.EntryPoint;
        var instructions = plan.Instructions;
        var unified = ImmutableArray.CreateBuilder<UnifiedBytecodeInstruction>();
        var literalConstants = ImmutableArray.CreateBuilder<JsValue>();
        var maxStackDepth = 0;

        while (true)
        {
            if ((uint)instructionIndex >= (uint)instructions.Length)
            {
                program = EmptyProgram();
                reason = "Linear instruction flow reached an invalid target index.";
                return false;
            }

            switch (instructions[instructionIndex])
            {
                case SimpleVariableDeclarationInstruction
                    {
                        InitializerProgram: { } initializerProgram,
                        AwaitedProgram: null,
                        TargetSymbol: { } targetSymbol
                    } declaration:
                    if (!TryResolveActivationSlot(targetSymbol, plan.ActivationSlots, out var storeSlot))
                    {
                        program = EmptyProgram();
                        reason = $"Unsupported declaration target '{targetSymbol.Name}'.";
                        return false;
                    }

                    if (!TryAppendExpressionProgramOps(initializerProgram, plan.ActivationSlots, unified, literalConstants, out reason))
                    {
                        program = EmptyProgram();
                        return false;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.StoreSlot, storeSlot));
                    maxStackDepth = Math.Max(maxStackDepth, initializerProgram.MaxStackDepth);
                    instructionIndex = declaration.Next;
                    continue;

                case ReturnInstruction { ReturnProgram: { } returnProgram, AwaitedProgram: null }:
                    if (!TryAppendExpressionProgramOps(returnProgram, plan.ActivationSlots, unified, literalConstants, out reason))
                    {
                        program = EmptyProgram();
                        return false;
                    }

                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.Return));
                    maxStackDepth = Math.Max(maxStackDepth, returnProgram.MaxStackDepth);
                    program = new UnifiedBytecodeProgram(unified.ToImmutable(), maxStackDepth, literalConstants.ToImmutable());
                    reason = string.Empty;
                    return true;

                default:
                    program = EmptyProgram();
                    reason = "Entrypoint must be a linear chain of non-awaited simple local declarations followed by a non-awaited ReturnInstruction.";
                    return false;
            }
        }
    }

    private static UnifiedBytecodeProgram EmptyProgram() =>
        new(ImmutableArray<UnifiedBytecodeInstruction>.Empty, 0, ImmutableArray<JsValue>.Empty);

    private static bool TryAppendExpressionProgramOps(
        ExpressionProgram expressionProgram,
        ActivationSlotShape activationSlots,
        ImmutableArray<UnifiedBytecodeInstruction>.Builder unified,
        ImmutableArray<JsValue>.Builder literalConstants,
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

                case ExpressionOpKind.LoadLiteral:
                    var literal = operation.GetLiteral(expressionProgram.LiteralConstants.AsSpan());
                    var literalIndex = literalConstants.Count;
                    literalConstants.Add(literal);
                    unified.Add(new UnifiedBytecodeInstruction(UnifiedBytecodeOpCode.LoadLiteral, literalIndex));
                    break;

                case ExpressionOpKind.Binary when operation.Operator is
                    BinaryOperator.Add or
                    BinaryOperator.Subtract or
                    BinaryOperator.Multiply or
                    BinaryOperator.Divide or
                    BinaryOperator.Modulo:
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
