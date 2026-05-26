using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeVirtualMachine
{
    public static JsValue Execute(UnifiedBytecodeProgram program, ReadOnlySpan<JsValue> slots)
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

                case UnifiedBytecodeOpCode.Add:
                    var right = stack[--stackPointer];
                    var left = stack[--stackPointer];
                    stack[stackPointer++] = JsValue.FromDouble(left.AsDouble() + right.AsDouble());
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
