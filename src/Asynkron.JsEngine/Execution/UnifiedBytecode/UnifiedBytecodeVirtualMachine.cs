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

        var programCounter = 0;
        var instructions = program.Instructions;
        while ((uint)programCounter < (uint)instructions.Length)
        {
            var instruction = instructions[programCounter];
            switch (instruction.OpCode)
            {
                case UnifiedBytecodeOpCode.LoadSlot:
                    stack[stackPointer++] = slots[instruction.Operand];
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.LoadLiteral:
                    stack[stackPointer++] = program.LiteralConstants[instruction.Operand];
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.StoreSlot:
                    slots[instruction.Operand] = stack[--stackPointer];
                    programCounter++;
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
                        BinaryOperator.LessThan => JsOps.LessThan(left, right) ? JsValue.True : JsValue.False,
                        BinaryOperator.LessThanOrEqual => JsOps.LessThanOrEqual(left, right) ? JsValue.True : JsValue.False,
                        BinaryOperator.GreaterThan => JsOps.GreaterThan(left, right) ? JsValue.True : JsValue.False,
                        BinaryOperator.GreaterThanOrEqual => JsOps.GreaterThanOrEqual(left, right) ? JsValue.True : JsValue.False,
                        _ => throw new InvalidOperationException($"Unsupported unified binary operator '{op}'.")
                    };
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.Jump:
                    programCounter = instruction.Operand;
                    break;

                case UnifiedBytecodeOpCode.JumpIfFalse:
                    programCounter = stack[--stackPointer].IsTruthy
                        ? programCounter + 1
                        : instruction.Operand;
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
