using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal enum UnifiedBytecodeProductionDeclineCode
{
    None = 0,
    AsyncLikeFunction,
    GeneratorFunction,
    CapturedOrDynamicActivation,
    ArgumentsObjectDependency,
    ThisDependency,
    NewTargetDependency,
    CallDependency,
    DynamicLookupDependency,
    LabelControlFlow,
    BreakOrContinueControlFlow,
    PrototypeOnlyBinaryOpcode,
    PrototypeOnlyJumpOpcode,
    PrototypeOnlyJumpIfFalseOpcode,
    UnsupportedPlanShape
}

internal readonly record struct UnifiedBytecodeProductionActivationDescriptor(
    bool IsAsyncLike = false,
    bool IsGenerator = false,
    bool HasCapturedOrDynamicActivation = false,
    bool HasArgumentsObjectDependency = false,
    bool HasThisDependency = false,
    bool HasNewTargetDependency = false,
    bool HasCallDependency = false,
    bool HasDynamicLookupDependency = false);

internal readonly record struct UnifiedBytecodeProductionEligibilityResult(
    bool IsEligible,
    UnifiedBytecodeProgram Program,
    UnifiedBytecodeProductionDeclineCode Code,
    string Reason)
{
    public static UnifiedBytecodeProductionEligibilityResult Accept(UnifiedBytecodeProgram program) =>
        new(true, program, UnifiedBytecodeProductionDeclineCode.None, string.Empty);

    public static UnifiedBytecodeProductionEligibilityResult Decline(
        UnifiedBytecodeProductionDeclineCode code,
        string reason) =>
        new(false, EmptyProgram(), code, reason);

    private static UnifiedBytecodeProgram EmptyProgram() =>
        new(ImmutableArray<UnifiedBytecodeInstruction>.Empty, 0, ImmutableArray<JsTypes.JsValue>.Empty);
}

internal static class UnifiedBytecodeProductionEligibility
{
    public static UnifiedBytecodeProductionEligibilityResult Evaluate(
        ExecutionPlan plan,
        in UnifiedBytecodeProductionActivationDescriptor activation)
    {
        if (activation.IsAsyncLike)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.AsyncLikeFunction,
                "Async-like functions are not eligible for production unified bytecode routing.");
        }

        if (activation.IsGenerator)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.GeneratorFunction,
                "Generator functions are not eligible for production unified bytecode routing.");
        }

        if (activation.HasCapturedOrDynamicActivation)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.CapturedOrDynamicActivation,
                "Captured or dynamic activation is not eligible for production unified bytecode routing.");
        }

        if (activation.HasArgumentsObjectDependency)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency,
                "Arguments-object-dependent execution is not eligible for production unified bytecode routing.");
        }

        if (activation.HasThisDependency)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.ThisDependency,
                "'this' dependency is not eligible for production unified bytecode routing.");
        }

        if (activation.HasNewTargetDependency)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.NewTargetDependency,
                "new.target dependency is not eligible for production unified bytecode routing.");
        }

        if (activation.HasCallDependency)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.CallDependency,
                "Call/construct dependency is not eligible for production unified bytecode routing.");
        }

        if (activation.HasDynamicLookupDependency)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency,
                "Dynamic lookup dependency is not eligible for production unified bytecode routing.");
        }

        if (plan.ActivationSlots is not { } activationSlots)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape,
                "Activation slot metadata is required.");
        }

        if (TryFindPlanDecline(plan, activationSlots, out var declineCode, out var declineReason))
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(declineCode, declineReason);
        }

        if (!UnifiedBytecodeCompiler.TryCompile(plan, isAsync: false, isGenerator: false, out var program, out var compileReason))
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape,
                $"Plan is not eligible for production unified bytecode routing: {compileReason}");
        }

        if (TryFindPrototypeOnlyOpcode(program, out var prototypeDeclineCode, out var prototypeReason))
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(prototypeDeclineCode, prototypeReason);
        }

        return UnifiedBytecodeProductionEligibilityResult.Accept(program);
    }

    private static bool TryFindPlanDecline(
        ExecutionPlan plan,
        ActivationSlotShape activationSlots,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        foreach (var instruction in plan.Instructions)
        {
            if (instruction is BreakableEnterInstruction { Label: not null })
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.LabelControlFlow;
                declineReason = "Label control flow is not eligible for production unified bytecode routing.";
                return true;
            }

            if (instruction is BreakInstruction or ContinueInstruction)
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.BreakOrContinueControlFlow;
                declineReason = "break/continue control flow is not eligible for production unified bytecode routing.";
                return true;
            }

            if (TryGetExpressionProgram(instruction, out var program) &&
                TryFindExpressionDecline(program, activationSlots, out declineCode, out declineReason))
            {
                return true;
            }
        }

        declineCode = UnifiedBytecodeProductionDeclineCode.None;
        declineReason = string.Empty;
        return false;
    }

    private static bool TryFindExpressionDecline(
        ExpressionProgram program,
        ActivationSlotShape activationSlots,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        var identifierConstants = program.IdentifierConstants.AsSpan();
        foreach (var operation in program.EnumerateOperations())
        {
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadThis:
                    declineCode = UnifiedBytecodeProductionDeclineCode.ThisDependency;
                    declineReason = "'this' expression access is not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.LoadNewTarget:
                    declineCode = UnifiedBytecodeProductionDeclineCode.NewTargetDependency;
                    declineReason = "new.target expression access is not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.Call:
                case ExpressionOpKind.Construct:
                case ExpressionOpKind.LoadNamedCallTarget:
                case ExpressionOpKind.LoadComputedCallTarget:
                case ExpressionOpKind.LoadIdentifierCallTarget:
                    declineCode = UnifiedBytecodeProductionDeclineCode.CallDependency;
                    declineReason = "Call/construct expression shape is not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.LoadIdentifier:
                    if (operation.IsArguments)
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency;
                        declineReason =
                            "arguments object access is not eligible for production unified bytecode routing.";
                        return true;
                    }

                    var identifier = operation.GetIdentifier(identifierConstants);
                    if (!TryResolveActivationSlot(identifier, activationSlots))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                        declineReason = $"Identifier '{identifier.Name.Name}' requires dynamic lookup and is not eligible for production unified bytecode routing.";
                        return true;
                    }

                    break;
            }
        }

        declineCode = UnifiedBytecodeProductionDeclineCode.None;
        declineReason = string.Empty;
        return false;
    }

    private static bool TryGetExpressionProgram(
        ExecutionInstruction instruction,
        out ExpressionProgram program)
    {
        switch (instruction)
        {
            case SimpleVariableDeclarationInstruction { AwaitedProgram: null, InitializerProgram: { } initializerProgram }:
                program = initializerProgram;
                return true;

            case AssignmentSlotInstruction { AwaitedProgram: null, ValueProgram: { } valueProgram }:
                program = valueProgram;
                return true;

            case CompoundAssignmentSlotInstruction { AwaitedProgram: null, RhsProgram: { } rhsProgram }:
                program = rhsProgram;
                return true;

            case BranchInstruction branch:
                program = branch.ConditionProgram;
                return true;

            case ReturnInstruction { AwaitedProgram: null, ReturnProgram: { } returnProgram }:
                program = returnProgram;
                return true;

            default:
                program = default;
                return false;
        }
    }

    private static bool TryResolveActivationSlot(IdentifierOperand identifier, ActivationSlotShape activationSlots)
    {
        if (identifier.ScopeId == activationSlots.ScopeId && identifier.SlotIndex >= 0)
        {
            return true;
        }

        return activationSlots.SlotMap.ContainsKey(identifier.Name);
    }

    private static bool TryFindPrototypeOnlyOpcode(
        UnifiedBytecodeProgram program,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        var hasJump = false;
        var hasJumpIfFalse = false;
        foreach (var instruction in program.Instructions)
        {
            switch (instruction.OpCode)
            {
                case UnifiedBytecodeOpCode.Binary:
                    TryGetPrototypeOnlyBinaryDecline(instruction, out declineCode, out declineReason);
                    return true;

                case UnifiedBytecodeOpCode.Jump:
                    hasJump = true;
                    break;

                case UnifiedBytecodeOpCode.JumpIfFalse:
                    hasJumpIfFalse = true;
                    break;

                case UnifiedBytecodeOpCode.LoadSlot:
                case UnifiedBytecodeOpCode.LoadLiteral:
                case UnifiedBytecodeOpCode.StoreSlot:
                case UnifiedBytecodeOpCode.Return:
                    break;

                default:
                    declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                    declineReason =
                        $"Opcode '{instruction.OpCode}' is outside the first production unified bytecode subset.";
                    return true;
            }
        }

        if (hasJump)
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.PrototypeOnlyJumpOpcode;
            declineReason = "Jump opcode is prototype-only for production unified bytecode routing.";
            return true;
        }

        if (hasJumpIfFalse && !IsDirectForwardJumpIfFalseBranchReturnProgram(program))
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.PrototypeOnlyJumpIfFalseOpcode;
            declineReason = "JumpIfFalse opcode is prototype-only for production unified bytecode routing.";
            return true;
        }

        declineCode = UnifiedBytecodeProductionDeclineCode.None;
        declineReason = string.Empty;
        return false;
    }

    private static bool IsDirectForwardJumpIfFalseBranchReturnProgram(UnifiedBytecodeProgram program)
    {
        var instructions = program.Instructions;
        var jumpIfFalseIndex = -1;
        var jumpIfFalseTarget = -1;
        for (var index = 0; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            if (instruction.OpCode != UnifiedBytecodeOpCode.JumpIfFalse)
            {
                continue;
            }

            if (jumpIfFalseIndex >= 0)
            {
                return false;
            }

            jumpIfFalseIndex = index;
            jumpIfFalseTarget = instruction.Operand;
        }

        if (jumpIfFalseIndex < 0)
        {
            return false;
        }

        if (jumpIfFalseTarget <= jumpIfFalseIndex ||
            jumpIfFalseTarget != jumpIfFalseIndex + 3 ||
            jumpIfFalseTarget + 1 >= instructions.Length ||
            jumpIfFalseTarget + 2 != instructions.Length)
        {
            return false;
        }

        return IsImmediateReturnPair(instructions, jumpIfFalseIndex + 1) &&
               IsImmediateReturnPair(instructions, jumpIfFalseTarget);
    }

    private static bool IsImmediateReturnPair(
        ImmutableArray<UnifiedBytecodeInstruction> instructions,
        int startIndex)
    {
        if (startIndex < 0 || startIndex + 1 >= instructions.Length)
        {
            return false;
        }

        var loadInstruction = instructions[startIndex];
        if (loadInstruction.OpCode is not (UnifiedBytecodeOpCode.LoadSlot or UnifiedBytecodeOpCode.LoadLiteral))
        {
            return false;
        }

        return instructions[startIndex + 1].OpCode == UnifiedBytecodeOpCode.Return;
    }

    private static void TryGetPrototypeOnlyBinaryDecline(
        UnifiedBytecodeInstruction instruction,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        declineCode = UnifiedBytecodeProductionDeclineCode.PrototypeOnlyBinaryOpcode;
        if (instruction.Operand is < byte.MinValue or > byte.MaxValue)
        {
            declineReason =
                $"Binary opcode is prototype-only for production unified bytecode routing (unknown operator operand {instruction.Operand}).";
            return;
        }

        var binaryOperator = (BinaryOperator)(byte)instruction.Operand;
        if (!Enum.IsDefined(binaryOperator))
        {
            declineReason =
                $"Binary opcode is prototype-only for production unified bytecode routing (unknown operator operand {instruction.Operand}).";
            return;
        }

        declineReason =
            $"Binary operator '{FormatBinaryOperator(binaryOperator)}' is prototype-only for production unified bytecode routing.";
    }

    private static string FormatBinaryOperator(BinaryOperator binaryOperator) =>
        binaryOperator switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            BinaryOperator.Modulo => "%",
            BinaryOperator.LessThan => "<",
            BinaryOperator.LessThanOrEqual => "<=",
            BinaryOperator.GreaterThan => ">",
            BinaryOperator.GreaterThanOrEqual => ">=",
            _ => binaryOperator.ToString()
        };
}
