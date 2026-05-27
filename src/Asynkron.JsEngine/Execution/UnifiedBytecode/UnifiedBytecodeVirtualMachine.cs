using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeVirtualMachine
{
    public static JsValue Execute(UnifiedBytecodeProgram program, Span<JsValue> slots, EvaluationContext context)
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
                    stack[stackPointer++] = ApplyBinaryOperator(op, left, right, context);
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

    private static JsValue ApplyBinaryOperator(
        BinaryOperator op,
        in JsValue left,
        in JsValue right,
        EvaluationContext context)
    {
        return op switch
        {
            BinaryOperator.Add => TypedAstEvaluator.AddValue(left, right, context),
            BinaryOperator.Subtract => TypedAstEvaluator.SubtractValue(left, right, context),
            BinaryOperator.Multiply => TypedAstEvaluator.MultiplyValue(left, right, context),
            BinaryOperator.Divide => TypedAstEvaluator.DivideValue(left, right, context),
            BinaryOperator.Modulo => TypedAstEvaluator.ModuloValue(left, right, context),
            BinaryOperator.Equal => JsOps.LooseEquals(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.LessThan => JsOps.LessThan(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.LessThanOrEqual => JsOps.LessThanOrEqual(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.GreaterThan => JsOps.GreaterThan(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.GreaterThanOrEqual => JsOps.GreaterThanOrEqual(left, right, context) ? JsValue.True : JsValue.False,
            _ => throw new InvalidOperationException($"Unsupported unified binary operator '{op}'.")
        };
    }
}
