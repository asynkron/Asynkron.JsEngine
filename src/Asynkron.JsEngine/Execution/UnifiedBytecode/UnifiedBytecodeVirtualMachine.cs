using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeVirtualMachine
{
    public static JsValue Execute(UnifiedBytecodeProgram program, Span<JsValue> slots)
    {
        var stack = new JsValue[Math.Max(program.MaxStackDepth, 2)];
        var stackPointer = 0;

        foreach (var instruction in program.Instructions)
        {
            switch (instruction.OpCode)
            {
                case UnifiedBytecodeOpCode.LoadSlot:
                    stack[stackPointer++] = slots[instruction.Operand];
                    break;

                case UnifiedBytecodeOpCode.LoadLiteral:
                    stack[stackPointer++] = program.LiteralConstants[instruction.Operand];
                    break;

                case UnifiedBytecodeOpCode.StoreSlot:
                    slots[instruction.Operand] = stack[--stackPointer];
                    break;

                case UnifiedBytecodeOpCode.Binary:
                    var op = (BinaryOperator)instruction.Operand;
                    var right = stack[--stackPointer];
                    var left = stack[--stackPointer];
                    stack[stackPointer++] = op switch
                    {
                        BinaryOperator.Add => JsValue.FromDouble(left.AsDouble() + right.AsDouble()),
                        BinaryOperator.Subtract => JsValue.FromDouble(left.AsDouble() - right.AsDouble()),
                        BinaryOperator.Multiply => JsValue.FromDouble(left.AsDouble() * right.AsDouble()),
                        BinaryOperator.Divide => JsValue.FromDouble(left.AsDouble() / right.AsDouble()),
                        BinaryOperator.Modulo => JsValue.FromDouble(JsOps.MathMod(left.AsDouble(), right.AsDouble())),
                        _ => throw new InvalidOperationException($"Unsupported unified binary operator '{op}'.")
                    };
                    break;

                case UnifiedBytecodeOpCode.Return:
                    return stack[stackPointer - 1];

                default:
                    throw new InvalidOperationException($"Unsupported unified opcode '{instruction.OpCode}'.");
            }
        }

        throw new InvalidOperationException("Program terminated without Return.");
    }
}
