using System.Numerics;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeVirtualMachine
{
    public static JsValue Execute(
        UnifiedBytecodeProgram program,
        Span<JsValue> slots,
        EvaluationContext context,
        bool isStrict = false)
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
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.RequireObjectCoercible:
                    var checkIndex = stackPointer - 1 - instruction.Operand;
                    if (stack[checkIndex].IsNullOrUndefined)
                    {
                        context.SetThrow(StandardLibrary.CreateTypeError(
                            "Cannot read properties of null or undefined",
                            context,
                            context.RealmState));
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ResolvePropertyKey:
                    stack[stackPointer - 1] = ResolvePropertyKey(stack[stackPointer - 1], context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetNamedProperty:
                    stack[stackPointer - 1] = GetNamedPropertyValue(
                        stack[stackPointer - 1],
                        program.StringConstants[instruction.Operand],
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetComputedProperty:
                    var propertyKey = stack[--stackPointer];
                    var target = stack[stackPointer - 1];
                    stack[stackPointer - 1] = JsOps.TryGetPropertyValueJsValue(target, propertyKey, out var computedValue, context)
                        ? computedValue
                        : JsValue.Undefined;
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet:
                    var namedCompoundTarget = stack[stackPointer - 1];
                    stack[stackPointer++] = GetNamedPropertyValue(
                        namedCompoundTarget,
                        program.StringConstants[instruction.Operand],
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet:
                    var computedCompoundKey = stack[stackPointer - 1];
                    var computedCompoundTarget = stack[stackPointer - 2];
                    stack[stackPointer++] = JsOps.TryGetPropertyValueJsValue(
                            computedCompoundTarget,
                            computedCompoundKey,
                            out var computedCompoundValue,
                            context)
                        ? computedCompoundValue
                        : JsValue.Undefined;
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SetNamedProperty:
                    var namedPropertyValue = stack[--stackPointer];
                    var namedSetTarget = stack[stackPointer - 1];
                    SetPropertyValue(
                        namedSetTarget,
                        program.StringConstants[instruction.Operand],
                        namedPropertyValue,
                        context,
                        isStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    stack[stackPointer - 1] = namedPropertyValue;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.SetComputedProperty:
                    var computedPropertyValue = stack[--stackPointer];
                    var computedSetKey = stack[--stackPointer];
                    var computedSetTarget = stack[stackPointer - 1];
                    var computedSetName = JsOps.GetRequiredPropertyName(computedSetKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    SetPropertyValue(computedSetTarget, computedSetName, computedPropertyValue, context, isStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    stack[stackPointer - 1] = computedPropertyValue;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateNamedProperty:
                    var namedUpdateTarget = stack[stackPointer - 1];
                    stack[stackPointer - 1] = UpdatePropertyValue(
                        namedUpdateTarget,
                        program.StringConstants[DecodeStringOperand(instruction.Operand)],
                        DecodeIsIncrement(instruction.Operand),
                        DecodeIsPrefix(instruction.Operand),
                        context,
                        isStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UpdateComputedProperty:
                    var computedUpdateKey = stack[--stackPointer];
                    var computedUpdateTarget = stack[stackPointer - 1];
                    var computedUpdateName = JsOps.GetRequiredPropertyName(computedUpdateKey, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    stack[stackPointer - 1] = UpdatePropertyValue(
                        computedUpdateTarget,
                        computedUpdateName,
                        DecodeIsIncrement(instruction.Operand),
                        DecodeIsPrefix(instruction.Operand),
                        context,
                        isStrict);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.TypeOf:
                    stack[stackPointer - 1] = new JsValue(GetTypeofStringValue(stack[stackPointer - 1]));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.TypeOfIdentifier:
                    stack[stackPointer++] = new JsValue(GetTypeofStringValue(slots[instruction.Operand]));
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryPlus:
                    var plusOperand = stack[stackPointer - 1];
                    stack[stackPointer - 1] = new JsValue(JsOps.ToNumber(in plusOperand, context));
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryMinus:
                    stack[stackPointer - 1] = TypedAstEvaluator.NegateValue(stack[stackPointer - 1], context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryLogicalNot:
                    stack[stackPointer - 1] = stack[stackPointer - 1].IsTruthy ? JsValue.False : JsValue.True;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryBitwiseNot:
                    stack[stackPointer - 1] = TypedAstEvaluator.BitwiseNot(stack[stackPointer - 1], context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.UnaryVoid:
                    stack[stackPointer - 1] = JsValue.Undefined;
                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.ToString:
                    stack[stackPointer - 1] = new JsValue(JsOps.ToJsString(stack[stackPointer - 1], context));
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    programCounter++;
                    break;

                case UnifiedBytecodeOpCode.Pop:
                    stackPointer--;
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
            BinaryOperator.StrictEqual => JsOps.StrictEquals(left, right) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictNotEqual => JsOps.StrictEquals(left, right) ? JsValue.False : JsValue.True,
            BinaryOperator.LessThan => JsOps.LessThan(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.LessThanOrEqual => JsOps.LessThanOrEqual(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.GreaterThan => JsOps.GreaterThan(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.GreaterThanOrEqual => JsOps.GreaterThanOrEqual(left, right, context) ? JsValue.True : JsValue.False,
            _ => throw new InvalidOperationException($"Unsupported unified binary operator '{op}'.")
        };
    }

    private static string GetTypeofStringValue(in JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "object",
            JsValueKind.Boolean => "boolean",
            JsValueKind.Number => "number",
            JsValueKind.BigInt => "bigint",
            JsValueKind.String => "string",
            JsValueKind.Symbol => "symbol",
            JsValueKind.Object => GetTypeofStringForObject(value.ObjectValue),
            _ => "undefined"
        };
    }

    private static string GetTypeofStringForObject(object? value)
    {
        if (value is IIsHtmlDda)
        {
            return "undefined";
        }

        if (value is JsProxy proxy)
        {
            return proxy.IsCallableTarget() ? "function" : "object";
        }

        return value is IJsCallable ? "function" : "object";
    }

    private static JsValue ResolvePropertyKey(JsValue propertyKey, EvaluationContext context)
    {
        var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
        return context.ShouldStopEvaluation ? JsValue.Undefined : new JsValue(propertyName);
    }

    private static JsValue GetNamedPropertyValue(JsValue target, string propertyName, EvaluationContext context)
    {
        if (target.IsNullOrUndefined)
        {
            context.SetThrow(StandardLibrary.CreateTypeError(
                "Cannot read properties of null or undefined",
                context,
                context.RealmState));
            return JsValue.Undefined;
        }

        return JsOps.TryGetPropertyValue(target, propertyName, out var directValue, context)
            ? directValue
            : JsValue.Undefined;
    }

    private static void SetPropertyValue(
        JsValue target,
        string propertyName,
        JsValue propertyValue,
        EvaluationContext context,
        bool isStrict)
    {
        var handle = PropertyHandle.Resolve(
            target,
            propertyName,
            context,
            context.CurrentScope.IsStrict || isStrict,
            allowPrivate: false);
        handle.SetValue(propertyValue);
    }

    private static JsValue UpdatePropertyValue(
        JsValue target,
        string propertyName,
        bool isIncrement,
        bool isPrefix,
        EvaluationContext context,
        bool isStrict)
    {
        var handle = PropertyHandle.Resolve(
            target,
            propertyName,
            context,
            context.CurrentScope.IsStrict || isStrict,
            allowPrivate: false);
        var currentValue = handle.GetJsValue();
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        GetUpdatedNumericValue(
            currentValue,
            isIncrement,
            context,
            out var oldNumericValue,
            out var newValue);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        handle.SetValue(newValue);
        return isPrefix ? newValue : oldNumericValue;
    }

    private static void GetUpdatedNumericValue(
        JsValue currentValue,
        bool isIncrement,
        EvaluationContext context,
        out JsValue oldNumericValue,
        out JsValue newValue)
    {
        if (currentValue.Kind == JsValueKind.Number)
        {
            oldNumericValue = currentValue;
            newValue = JsValueCache.GetNumberJsValue(
                isIncrement
                    ? currentValue.NumberValue + 1.0
                    : currentValue.NumberValue - 1.0);
            return;
        }

        var numericValue = currentValue.IsBigInt
            ? currentValue
            : JsOps.ToNumericAsJsValue(in currentValue, context);
        if (context.ShouldStopEvaluation)
        {
            oldNumericValue = JsValue.Undefined;
            newValue = JsValue.Undefined;
            return;
        }

        oldNumericValue = numericValue;
        newValue = isIncrement
            ? IncrementValue(numericValue)
            : DecrementValue(numericValue);
    }

    private static JsValue IncrementValue(in JsValue value)
    {
        if (value.IsNumber)
        {
            return JsValue.FromDouble(value.NumberValue + 1.0);
        }

        if (value.IsBigInt)
        {
            return new JsValue(new JsBigInt(value.AsBigInt().Value + BigInteger.One));
        }

        return JsValue.NaN;
    }

    private static JsValue DecrementValue(in JsValue value)
    {
        if (value.IsNumber)
        {
            return JsValue.FromDouble(value.NumberValue - 1.0);
        }

        if (value.IsBigInt)
        {
            return new JsValue(new JsBigInt(value.AsBigInt().Value - BigInteger.One));
        }

        return JsValue.NaN;
    }

    private static int DecodeStringOperand(int operand) => operand >> 2;

    private static bool DecodeIsIncrement(int operand) => (operand & 1) != 0;

    private static bool DecodeIsPrefix(int operand) => (operand & 2) != 0;
}
