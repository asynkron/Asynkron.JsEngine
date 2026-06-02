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
    ArrowLexicalThisDependency,
    ClassConstructorActivation,
    CallDependency,
    DynamicLookupDependency,
    PropertyReadBoundaryOutOfScope,
    PropertyWriteDependency,
    PropertyUpdateDependency,
    DeleteDependency,
    SuperPropertyDependency,
    OptionalChainDependency,
    ObjectLiteralOrSpreadDependency,
    PrivateFieldDependency,
    ForInDriverStateDependency,
    DestructuringDependency,
    LabelControlFlow,
    UnsupportedPlanShape,
    CallInvocationBoundary
}

internal readonly record struct UnifiedBytecodeProductionActivationDescriptor(
    bool IsAsyncLike = false,
    bool IsGenerator = false,
    bool HasCapturedOrDynamicActivation = false,
    bool HasArgumentsObjectDependency = false,
    bool HasArrowLexicalThisDependency = false,
    bool HasClassConstructorActivation = false,
    bool HasDynamicLookupDependency = false,
    bool AllowsOrdinaryDynamicIdentifierEnvironmentOperations = false);

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
        new(
            ImmutableArray<UnifiedBytecodeInstruction>.Empty,
            0,
            0,
            ImmutableArray<JsTypes.JsValue>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string?>.Empty,
            ImmutableArray<int>.Empty,
            ImmutableArray<int>.Empty,
            ImmutableArray<UnifiedBytecodeCallTarget>.Empty,
            ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty,
            ImmutableArray<UnifiedBytecodeTryDescriptor>.Empty,
            ImmutableArray<UnifiedBytecodeCatchDescriptor>.Empty,
            ImmutableArray<UnifiedBytecodeDriverDescriptor>.Empty);
}

internal static class UnifiedBytecodeProductionEligibility
{
    internal static bool ContainsOnlyImplicitArgumentsObjectDynamicIdentifierDependency(ExecutionPlan plan)
    {
        if (plan.ActivationSlots is not { } activationSlots)
        {
            return false;
        }

        var foundArgumentsRead = false;
        for (var instructionIndex = 0; instructionIndex < plan.Instructions.Length; instructionIndex++)
        {
            if (!TryGetExpressionProgram(plan.Instructions[instructionIndex], out var program))
            {
                continue;
            }

            var identifierConstants = program.IdentifierConstants.AsSpan();
            for (var operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
            {
                var operation = program.GetOperation(operationIndex);
                if (operation.Kind is
                    ExpressionOpKind.ResolveIdentifierReference or
                    ExpressionOpKind.StoreResolvedIdentifier or
                    ExpressionOpKind.StoreIdentifier or
                    ExpressionOpKind.DeleteIdentifier)
                {
                    if (IsImplicitArgumentsIdentifier(operation, identifierConstants, activationSlots))
                    {
                        return false;
                    }

                    continue;
                }

                if (operation.Kind is not (
                    ExpressionOpKind.LoadIdentifier or
                    ExpressionOpKind.LoadIdentifierCallTarget or
                    ExpressionOpKind.TypeOfIdentifier or
                    ExpressionOpKind.UpdateIdentifier))
                {
                    continue;
                }

                if (!IsImplicitArgumentsIdentifier(operation, identifierConstants, activationSlots))
                {
                    continue;
                }

                foundArgumentsRead = true;
            }
        }

        return foundArgumentsRead;
    }

    private static bool IsImplicitArgumentsIdentifier(
        PackedExpressionOp operation,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots) =>
        IsImplicitArgumentsIdentifier(
            operation.GetIdentifier(identifierConstants),
            activationSlots);

    private static bool IsImplicitArgumentsIdentifier(
        IdentifierOperand identifier,
        ActivationSlotShape activationSlots) =>
        ReferenceEquals(identifier.Name, Symbol.Arguments) &&
        !TryResolveActivationSlot(identifier, activationSlots);

    internal static bool ContainsOrdinaryDynamicIdentifierDependency(ExecutionPlan plan)
    {
        if (plan.ActivationSlots is not { } activationSlots ||
            !UnifiedBytecodeWithDepthAnalysis.TryBuildActiveWithDepths(
                plan.Instructions,
                plan.EntryPoint,
                out var activeWithDepths,
                out _))
        {
            return false;
        }

        for (var instructionIndex = 0; instructionIndex < plan.Instructions.Length; instructionIndex++)
        {
            if (activeWithDepths[instructionIndex] != 0)
            {
                continue;
            }

            var instruction = plan.Instructions[instructionIndex];
            if (HasOrdinaryDynamicInstructionDependency(instruction, activationSlots))
            {
                return true;
            }

            if (TryGetExpressionProgram(instruction, out var program) &&
                HasOrdinaryDynamicExpressionDependency(program, activationSlots))
            {
                return true;
            }
        }

        return false;
    }

    public static UnifiedBytecodeProductionEligibilityResult Evaluate(
        ExecutionPlan plan,
        in UnifiedBytecodeProductionActivationDescriptor activation)
    {
        if (TryFindOrdinarySyncActivationDecline(activation, out var activationDeclineCode, out var activationDeclineReason))
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(activationDeclineCode, activationDeclineReason);
        }

        if (plan.ActivationSlots is not { } activationSlots)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape,
                "Activation slot metadata is required.");
        }

        if (TryFindPlanDecline(
                plan,
                activationSlots,
                activation.AllowsOrdinaryDynamicIdentifierEnvironmentOperations,
                out var declineCode,
                out var declineReason))
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(declineCode, declineReason);
        }

        if (!UnifiedBytecodeCompiler.TryCompile(
                plan,
                isAsync: false,
                isGenerator: false,
                out var program,
                out var compileReason,
                activation.AllowsOrdinaryDynamicIdentifierEnvironmentOperations))
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

    internal static bool TryFindOrdinarySyncActivationDecline(
        in UnifiedBytecodeProductionActivationDescriptor activation,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        if (TryFindSharedActivationDecline(activation, isResumable: false, out declineCode, out declineReason))
        {
            return true;
        }

        return TryFindOrdinarySyncOnlyActivationDecline(activation, out declineCode, out declineReason);
    }

    public static UnifiedBytecodeProductionEligibilityResult EvaluateResumable(
        ExecutionPlan plan,
        in UnifiedBytecodeProductionActivationDescriptor activation)
    {
        if (TryFindSharedActivationDecline(activation, isResumable: true, out var activationDeclineCode, out var activationDeclineReason))
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(activationDeclineCode, activationDeclineReason);
        }

        if (!activation.IsAsyncLike && !activation.IsGenerator)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape,
                "Only async-like or generator functions are currently eligible for resumable unified bytecode routing.");
        }

        // 'this'-dependent resumable programs are accepted: the strict/sloppy-coerced binding is
        // threaded through UnifiedBytecodeResumeState and pushed by the ExecuteResumable LoadThis
        // case (mirrors the production sync route landed in #2633/#2643). new.target, captured/dynamic
        // activation, arguments-object, call, and dynamic-lookup shapes still decline below.
        if (TryFindOrdinarySyncOnlyActivationDecline(activation, out activationDeclineCode, out activationDeclineReason))
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(activationDeclineCode, activationDeclineReason);
        }

        if (plan.ActivationSlots is not { } activationSlots)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape,
                "Activation slot metadata is required.");
        }

        if (TryFindResumablePlanDecline(plan, activationSlots, out var declineCode, out var declineReason))
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(declineCode, declineReason);
        }

        if (!UnifiedBytecodeCompiler.TryCompile(
                plan,
                activation.IsAsyncLike,
                activation.IsGenerator,
                out var program,
                out var compileReason))
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape,
                $"Plan is not eligible for resumable unified bytecode routing: {compileReason}");
        }

        if (TryFindUnsupportedResumableOpcode(program, out declineReason))
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape,
                declineReason);
        }

        return UnifiedBytecodeProductionEligibilityResult.Accept(program);
    }

    private static bool TryFindSharedActivationDecline(
        in UnifiedBytecodeProductionActivationDescriptor activation,
        bool isResumable,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        if (activation.IsAsyncLike && activation.IsGenerator)
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.AsyncLikeFunction;
            declineReason = isResumable
                ? "Async-like generator activation is not eligible for resumable unified bytecode routing."
                : "Async-like functions are not eligible for production unified bytecode routing.";
            return true;
        }

        if (!isResumable && activation.IsAsyncLike)
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.AsyncLikeFunction;
            declineReason = "Async-like functions are not eligible for production unified bytecode routing.";
            return true;
        }

        if (!isResumable && activation.IsGenerator)
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.GeneratorFunction;
            declineReason = "Generator functions are not eligible for production unified bytecode routing.";
            return true;
        }

        if (activation.HasCapturedOrDynamicActivation)
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.CapturedOrDynamicActivation;
            declineReason = isResumable
                ? "Captured or dynamic activation is not eligible for resumable unified bytecode routing."
                : "Captured or dynamic activation is not eligible for production unified bytecode routing.";
            return true;
        }

        if (activation.HasArgumentsObjectDependency)
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency;
            declineReason = isResumable
                ? "Arguments-object-dependent execution is not eligible for resumable unified bytecode routing."
                : "Arguments-object-dependent execution is not eligible for production unified bytecode routing.";
            return true;
        }

        if (activation.HasDynamicLookupDependency)
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
            declineReason = isResumable
                ? "Dynamic lookup dependency is not eligible for resumable unified bytecode routing."
                : "Dynamic lookup dependency is not eligible for production unified bytecode routing.";
            return true;
        }

        declineCode = UnifiedBytecodeProductionDeclineCode.None;
        declineReason = string.Empty;
        return false;
    }

    private static bool TryFindOrdinarySyncOnlyActivationDecline(
        in UnifiedBytecodeProductionActivationDescriptor activation,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        if (activation.HasArrowLexicalThisDependency)
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.ArrowLexicalThisDependency;
            declineReason = "Arrow lexical this/new.target activation is not eligible for production unified bytecode routing.";
            return true;
        }

        if (activation.HasClassConstructorActivation)
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.ClassConstructorActivation;
            declineReason = "Class constructor activation is not eligible for production unified bytecode routing.";
            return true;
        }

        declineCode = UnifiedBytecodeProductionDeclineCode.None;
        declineReason = string.Empty;
        return false;
    }

    private static bool TryFindPlanDecline(
        ExecutionPlan plan,
        ActivationSlotShape activationSlots,
        bool allowsOrdinaryDynamicIdentifiers,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        if (!UnifiedBytecodeWithDepthAnalysis.TryBuildActiveWithDepths(
                plan.Instructions,
                plan.EntryPoint,
                out var activeWithDepths,
                out var withDepthReason))
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
            declineReason = withDepthReason;
            return true;
        }

        for (var instructionIndex = 0; instructionIndex < plan.Instructions.Length; instructionIndex++)
        {
            if (activeWithDepths[instructionIndex] < 0)
            {
                continue;
            }

            var instruction = plan.Instructions[instructionIndex];
            var allowsDynamicIdentifiers = activeWithDepths[instructionIndex] > 0 ||
                                           allowsOrdinaryDynamicIdentifiers;

            if (instruction is IteratorInitInstruction iteratorInit &&
                !IsSupportedIteratorInit(iteratorInit, out declineReason))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                return true;
            }

            if (instruction is ForInInitInstruction forInInit &&
                !IsSupportedForInInit(forInInit, out declineReason))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.ForInDriverStateDependency;
                return true;
            }

            if (instruction is ArrayDestructuringElementInstruction { TargetSymbol: null } or
                ArrayDestructuringInitInstruction or
                ArrayDestructuringElementInstruction or
                ArrayDestructuringRestInstruction or
                ArrayDestructuringCloseInstruction)
            {
                if (!IsSupportedArrayDestructuringInstruction(instruction, out declineReason))
                {
                    declineCode = UnifiedBytecodeProductionDeclineCode.DestructuringDependency;
                    return true;
                }
            }

            if (instruction is ObjectDestructuringInitInstruction or
                ObjectDestructuringPropertyInstruction or
                ObjectDestructuringRestInstruction or
                ObjectDestructuringCloseInstruction)
            {
                if (!IsSupportedObjectDestructuringInstruction(instruction, out declineReason))
                {
                    declineCode = UnifiedBytecodeProductionDeclineCode.DestructuringDependency;
                    return true;
                }
            }

            if (instruction is EnterWithInstruction { AwaitedProgram: not null })
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.AsyncLikeFunction;
                declineReason = "Awaited with-object evaluation is not eligible for production unified bytecode routing.";
                return true;
            }

            if (instruction is BindingVariableDeclarationInstruction { AwaitedProgram: not null })
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.AsyncLikeFunction;
                declineReason =
                    "Awaited binding/destructuring declarations are not eligible for production unified bytecode routing.";
                return true;
            }

            if (instruction is BindingVariableDeclarationInstruction
                {
                    VarKind: VariableKind.Using or VariableKind.AwaitUsing
                })
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                declineReason = "using declarations require scope-exit disposal and are not eligible for production unified bytecode routing.";
                return true;
            }

            if (instruction is FunctionDeclarationInstruction { Descriptor: not null })
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                declineReason =
                    "Descriptor-backed block-scoped function declarations require an admitted lexical environment shape before production unified bytecode routing.";
                return true;
            }

            if (instruction is SimpleVariableDeclarationInstruction
                {
                    VarKind: VariableKind.Using or VariableKind.AwaitUsing
                })
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                declineReason = "using declarations require scope-exit disposal and are not eligible for production unified bytecode routing.";
                return true;
            }

            if (instruction is PushEnvironmentInstruction pushEnvironment &&
                !IsSupportedPushEnvironment(pushEnvironment, plan.FlatSlotMappings))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                declineReason =
                    "Only non-iterating lexical block environments with flat slot mappings are eligible for production unified bytecode routing.";
                return true;
            }

            if (!allowsDynamicIdentifiers &&
                TryFindInstructionDynamicIdentifierDecline(
                    instruction,
                    activationSlots,
                    out declineCode,
                    out declineReason))
            {
                return true;
            }

            if (TryGetExpressionProgram(instruction, out var program) &&
                TryFindExpressionDecline(
                    program,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    out declineCode,
                    out declineReason))
            {
                return true;
            }
        }

        declineCode = UnifiedBytecodeProductionDeclineCode.None;
        declineReason = string.Empty;
        return false;
    }

    private static bool TryFindResumablePlanDecline(
        ExecutionPlan plan,
        ActivationSlotShape activationSlots,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        if (!UnifiedBytecodeWithDepthAnalysis.TryBuildActiveWithDepths(
                plan.Instructions,
                plan.EntryPoint,
                out var activeWithDepths,
                out var withDepthReason))
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
            declineReason = withDepthReason;
            return true;
        }

        for (var instructionIndex = 0; instructionIndex < plan.Instructions.Length; instructionIndex++)
        {
            if (activeWithDepths[instructionIndex] < 0)
            {
                continue;
            }

            var instruction = plan.Instructions[instructionIndex];
            var allowsDynamicIdentifiers = activeWithDepths[instructionIndex] > 0;
            if (!IsSupportedResumableInstruction(instruction, out declineReason))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                return true;
            }

            if (!allowsDynamicIdentifiers &&
                TryFindInstructionDynamicIdentifierDecline(
                    instruction,
                    activationSlots,
                    out declineCode,
                    out declineReason))
            {
                return true;
            }

            if (TryGetResumableExpressionProgram(instruction, out var program) &&
                TryFindExpressionDecline(
                    program,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    out declineCode,
                    out declineReason))
            {
                return true;
            }
        }

        declineCode = UnifiedBytecodeProductionDeclineCode.None;
        declineReason = string.Empty;
        return false;
    }

    private static bool IsSupportedResumableInstruction(ExecutionInstruction instruction, out string declineReason)
    {
        switch (instruction)
        {
            case SimpleVariableDeclarationInstruction { AwaitedProgram: null, InitializerProgram: { } }:
            case SimpleVariableDeclarationInstruction { AwaitedProgram: null, InitializerProgram: null }:
            case AssignmentSlotInstruction { AwaitedProgram: null, ValueProgram: { } }:
            case EvaluateAndDiscardInstruction { ExpressionProgram: { } }:
            case BranchInstruction:
            case JumpInstruction:
            case ReturnInstruction { AwaitedProgram: null }:
            case ThrowInstruction { AwaitedProgram: null, ThrowProgram: { } }:
            case YieldInstruction { AwaitedProgram: null, YieldProgram: { } or null }:
            case YieldStarInstruction { AwaitedProgram: null, IterableProgram: { } }:
            case AwaitAndDiscardInstruction:
            case ReturnInstruction { AwaitedProgram: not null }:
            case StoreResumeValueInstruction:
                declineReason = string.Empty;
                return true;
            default:
                declineReason =
                    $"Instruction '{instruction.GetType().Name}' is not eligible for resumable unified bytecode routing.";
                return false;
        }
    }

    private static bool TryGetResumableExpressionProgram(
        ExecutionInstruction instruction,
        out ExpressionProgram program)
    {
        switch (instruction)
        {
            case YieldInstruction { AwaitedProgram: null, YieldProgram: { } yieldProgram }:
                program = yieldProgram;
                return true;
            case AwaitAndDiscardInstruction awaitAndDiscard:
                program = awaitAndDiscard.AwaitedProgram;
                return true;
            case ReturnInstruction { AwaitedProgram: { } awaitedReturnProgram }:
                program = awaitedReturnProgram;
                return true;
            case YieldStarInstruction { AwaitedProgram: null, IterableProgram: { } iterableProgram }:
                program = iterableProgram;
                return true;
            default:
                return TryGetExpressionProgram(instruction, out program);
        }
    }

    private static bool TryFindUnsupportedResumableOpcode(
        UnifiedBytecodeProgram program,
        out string declineReason)
    {
        foreach (var instruction in program.Instructions)
        {
            if (instruction.OpCode is
                UnifiedBytecodeOpCode.LoadSlot or
                UnifiedBytecodeOpCode.LoadLiteral or
                UnifiedBytecodeOpCode.LoadThis or
                UnifiedBytecodeOpCode.StoreSlot or
                UnifiedBytecodeOpCode.InitializeSlot or
                UnifiedBytecodeOpCode.Binary or
                UnifiedBytecodeOpCode.Pop or
                UnifiedBytecodeOpCode.DuplicateTop or
                UnifiedBytecodeOpCode.DuplicateTopTwo or
                UnifiedBytecodeOpCode.SwapTopTwo or
                UnifiedBytecodeOpCode.RotateTopThreeRight or
                UnifiedBytecodeOpCode.Jump or
                UnifiedBytecodeOpCode.JumpIfFalse or
                UnifiedBytecodeOpCode.JumpIfShortCircuitFalse or
                UnifiedBytecodeOpCode.JumpIfShortCircuitTrue or
                UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish or
                UnifiedBytecodeOpCode.Return or
                UnifiedBytecodeOpCode.ReturnUndefined or
                UnifiedBytecodeOpCode.Throw or
                UnifiedBytecodeOpCode.Yield or
                UnifiedBytecodeOpCode.StoreResumeValue or
                UnifiedBytecodeOpCode.AwaitAndDiscard or
                UnifiedBytecodeOpCode.AwaitedReturn or
                UnifiedBytecodeOpCode.YieldStar)
            {
                continue;
            }

            declineReason =
                $"Unified bytecode opcode '{instruction.OpCode}' is not supported by resumable production routing.";
            return true;
        }

        declineReason = string.Empty;
        return false;
    }

    private static bool TryFindInstructionDynamicIdentifierDecline(
        ExecutionInstruction instruction,
        ActivationSlotShape activationSlots,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        switch (instruction)
        {
            case AssignmentSlotInstruction { TargetSymbol: { } targetSymbol, FlatSlotId: var flatSlotId }:
                if (!TryResolveActivationSymbolSlot(targetSymbol, flatSlotId, activationSlots))
                {
                    declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                    declineReason =
                        $"Assignment target '{targetSymbol.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                    return true;
                }

                break;

            case CompoundAssignmentSlotInstruction { TargetSymbol: { } targetSymbol, FlatSlotId: var flatSlotId }:
                if (!TryResolveActivationSymbolSlot(targetSymbol, flatSlotId, activationSlots))
                {
                    declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                    declineReason =
                        $"Compound assignment target '{targetSymbol.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                    return true;
                }

                break;

            case LogicalCompoundAssignmentSlotInstruction { TargetSymbol: { } targetSymbol, FlatSlotId: var flatSlotId }:
                if (!TryResolveActivationSymbolSlot(targetSymbol, flatSlotId, activationSlots))
                {
                    declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                    declineReason =
                        $"Logical compound assignment target '{targetSymbol.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                    return true;
                }

                break;

            case IncrementSlotInstruction { TargetSymbol: { } targetSymbol, FlatSlotId: var flatSlotId }:
                if (!TryResolveActivationSymbolSlot(targetSymbol, flatSlotId, activationSlots))
                {
                    declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                    declineReason =
                        $"Update target '{targetSymbol.Name}' requires dynamic lookup and is not eligible outside an active with environment.";
                    return true;
                }

                break;
        }

        declineCode = UnifiedBytecodeProductionDeclineCode.None;
        declineReason = string.Empty;
        return false;
    }

    private static bool HasOrdinaryDynamicInstructionDependency(
        ExecutionInstruction instruction,
        ActivationSlotShape activationSlots)
    {
        return instruction switch
        {
            AssignmentSlotInstruction { TargetSymbol: { } targetSymbol, FlatSlotId: var flatSlotId } =>
                !TryResolveActivationSymbolSlot(targetSymbol, flatSlotId, activationSlots),
            CompoundAssignmentSlotInstruction { TargetSymbol: { } targetSymbol, FlatSlotId: var flatSlotId } =>
                !TryResolveActivationSymbolSlot(targetSymbol, flatSlotId, activationSlots),
            LogicalCompoundAssignmentSlotInstruction { TargetSymbol: { } targetSymbol, FlatSlotId: var flatSlotId } =>
                !TryResolveActivationSymbolSlot(targetSymbol, flatSlotId, activationSlots),
            IncrementSlotInstruction { TargetSymbol: { } targetSymbol, FlatSlotId: var flatSlotId } =>
                !TryResolveActivationSymbolSlot(targetSymbol, flatSlotId, activationSlots),
            _ => false
        };
    }

    private static bool TryFindExpressionDecline(
        ExpressionProgram program,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        var operationCount = program.OperationCount;
        var identifierConstants = program.IdentifierConstants.AsSpan();
        var stringConstants = program.StringConstants.AsSpan();
        var isFirstBoundaryPropertyWriteCandidate =
            TryIsFirstBoundaryPropertyWriteCandidate(program, identifierConstants, activationSlots, allowsDynamicIdentifiers);
        var isFirstBoundaryPropertyUpdateCandidate =
            TryIsFirstBoundaryPropertyUpdateCandidate(program, identifierConstants, activationSlots, allowsDynamicIdentifiers);
        var isFirstBoundaryNamedCompoundPropertyWriteCandidate =
            TryIsFirstBoundaryNamedCompoundPropertyWriteCandidate(
                program,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers);
        var isFirstBoundaryNamedLogicalPropertyWriteCandidate =
            TryIsFirstBoundaryNamedLogicalPropertyWriteCandidate(
                program,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers);
        var isPrivateNamedMutationCandidate =
            isFirstBoundaryPropertyWriteCandidate ||
            isFirstBoundaryPropertyUpdateCandidate ||
            isFirstBoundaryNamedCompoundPropertyWriteCandidate ||
            isFirstBoundaryNamedLogicalPropertyWriteCandidate;

        // Pre-scan spread source operands before the main loop processes the source ops, which may
        // otherwise trigger a less-specific decline code such as CallDependency.
        for (var i = 0; i < operationCount; i++)
        {
            if (IsPrivateNamedPropertyMutationOperation(program.GetOperation(i), stringConstants) &&
                !isPrivateNamedMutationCandidate)
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.PrivateFieldDependency;
                declineReason = "Private-field expressions are not eligible for production unified bytecode routing.";
                return true;
            }

            if (program.GetOperation(i).Kind == ExpressionOpKind.ArraySpread &&
                !IsOperationInSimpleArrayLiteralSpan(program, i, identifierConstants, activationSlots))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency;
                declineReason =
                    "Array spread with non-simple source is not eligible for production unified bytecode routing.";
                return true;
            }

            if (program.GetOperation(i).Kind == ExpressionOpKind.ObjectSpread &&
                !IsOperationInSimpleObjectLiteralSpan(program, i, identifierConstants, activationSlots))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency;
                declineReason =
                    "Object spread with non-simple source or outside an admitted object literal span is not eligible for production unified bytecode routing.";
                return true;
            }
        }

        var isCallTargetPreparationCandidate = TryIsFirstBoundaryCallTargetPreparationCandidate(
            program,
            identifierConstants,
            stringConstants,
            activationSlots,
            allowsDynamicIdentifiers);
        var isConstructInvocationCandidate = TryIsConstructInvocationCandidate(program, identifierConstants, activationSlots);
        var hasOptionalChainOperation = HasOptionalChainOperation(program);
        for (var operationIndex = 0; operationIndex < operationCount; operationIndex++)
        {
            var operation = program.GetOperation(operationIndex);
            if (IsPrivateNamedPropertyMutationOperation(operation, stringConstants) &&
                !isPrivateNamedMutationCandidate)
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.PrivateFieldDependency;
                declineReason = "Private-field expressions are not eligible for production unified bytecode routing.";
                return true;
            }

            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadIdentifierCallTarget:
                    if (isCallTargetPreparationCandidate)
                    {
                        break;
                    }

                    if (operation.IsArguments)
                    {
                        if (!allowsDynamicIdentifiers)
                        {
                            declineCode = UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency;
                            declineReason =
                                "arguments call targets are not eligible for production unified bytecode routing.";
                            return true;
                        }

                        break;
                    }

                    var callIdentifier = operation.GetIdentifier(identifierConstants);
                    if (callIdentifier.Name.Name == "eval")
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.CallDependency;
                        declineReason =
                            "Direct eval invocation semantics are not eligible for production unified bytecode routing.";
                        return true;
                    }

                    if (!TryResolveActivationSlot(callIdentifier, activationSlots))
                    {
                        if (!allowsDynamicIdentifiers &&
                            !CanUseMaterializedActivationDynamicLookup(callIdentifier, activationSlots))
                        {
                            declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                            declineReason =
                                $"Identifier call target '{callIdentifier.Name.Name}' requires dynamic lookup and is not eligible for production unified bytecode routing.";
                            return true;
                        }

                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.CallDependency;
                    declineReason =
                        "Identifier call-target preparation is outside the first production invocation boundary.";
                    return true;

                case ExpressionOpKind.LoadNamedCallTarget:
                case ExpressionOpKind.LoadComputedCallTarget:
                    if (isCallTargetPreparationCandidate)
                    {
                        break;
                    }

                    if (IsOperationInSimpleArrayLiteralSpan(program, operationIndex, identifierConstants, activationSlots) ||
                        IsOperationInSimpleObjectLiteralSpan(program, operationIndex, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (operation.IsOptional || operation.ShortCircuitOnNullishTarget || hasOptionalChainOperation)
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                        declineReason =
                            "Optional-chain call-target preparation is outside the first production invocation boundary.";
                        return true;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.CallDependency;
                    declineReason =
                        "Member call-target preparation is outside the first production invocation boundary.";
                    return true;

                case ExpressionOpKind.Call:
                    if (isCallTargetPreparationCandidate)
                    {
                        break;
                    }

                    if (IsOperationInSimpleArrayLiteralSpan(program, operationIndex, identifierConstants, activationSlots) ||
                        IsOperationInSimpleObjectLiteralSpan(program, operationIndex, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    // Synchronous spread calls are admitted (gh2676); the call-target
                    // preparation candidate check accepts them. Anything reaching here is
                    // an out-of-boundary call shape.
                    //
                    // Use CallInvocationBoundary for plan-structural call boundaries.
                    // Direct eval remains CallDependency because it is context-sensitive.
                    declineCode = operation.IsDirectEval
                        ? UnifiedBytecodeProductionDeclineCode.CallDependency  // direct eval is context-sensitive
                        : UnifiedBytecodeProductionDeclineCode.CallInvocationBoundary;
                    if (operation.IsDirectEval)
                    {
                        declineReason =
                            "Direct eval invocation semantics are not eligible for production unified bytecode routing.";
                        return true;
                    }

                    declineReason =
                        "Call invocation is not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.Construct:
                    break;

                case ExpressionOpKind.SuperConstruct:
                    break;

                case ExpressionOpKind.LoadNamedSuperCallTarget:
                case ExpressionOpKind.LoadComputedSuperCallTarget:
                    if (isCallTargetPreparationCandidate)
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.SuperPropertyDependency;
                    declineReason =
                        "super call-target preparation is outside the first production invocation boundary.";
                    return true;

                case ExpressionOpKind.LoadIdentifier:
                    var identifier = operation.GetIdentifier(identifierConstants);
                    var hasActivationSlot = TryResolveActivationSlot(identifier, activationSlots);
                    if (IsImplicitArgumentsIdentifier(identifier, activationSlots))
                    {
                        if (!allowsDynamicIdentifiers)
                        {
                            declineCode = UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency;
                            declineReason =
                                "arguments object access is not eligible for production unified bytecode routing.";
                            return true;
                        }

                        break;
                    }

                    if (!hasActivationSlot)
                    {
                        if (!allowsDynamicIdentifiers &&
                            !CanUseMaterializedActivationDynamicLookup(identifier, activationSlots))
                        {
                            declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                            declineReason = $"Identifier '{identifier.Name.Name}' requires dynamic lookup and is not eligible for production unified bytecode routing.";
                            return true;
                        }
                    }

                    break;

                case ExpressionOpKind.ResolveIdentifierReference:
                    var referenceIdentifier = operation.GetIdentifier(identifierConstants);
                    if (IsImplicitArgumentsIdentifier(referenceIdentifier, activationSlots))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency;
                        declineReason =
                            "arguments assignment references are not eligible for production unified bytecode routing.";
                        return true;
                    }

                    if (TryResolveActivationSlot(referenceIdentifier, activationSlots))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                        declineReason =
                            $"Identifier assignment reference '{referenceIdentifier.Name.Name}' resolves to an activation slot and is outside the ordinary dynamic-name production slice.";
                        return true;
                    }

                    if (allowsDynamicIdentifiers)
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                    declineReason =
                        "Dynamic identifier assignment references are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.LoadResolvedIdentifierValue:
                case ExpressionOpKind.PopResolvedIdentifierReference:
                    if (allowsDynamicIdentifiers)
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                    declineReason =
                        "Dynamic identifier assignment references are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.StoreResolvedIdentifier:
                case ExpressionOpKind.StoreIdentifier:
                    var storeIdentifier = operation.GetIdentifier(identifierConstants);
                    if (IsImplicitArgumentsIdentifier(storeIdentifier, activationSlots))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency;
                        declineReason =
                            "arguments assignment references are not eligible for production unified bytecode routing.";
                        return true;
                    }

                    if (TryResolveActivationSlot(storeIdentifier, activationSlots))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                        declineReason =
                            $"Identifier assignment target '{storeIdentifier.Name.Name}' resolves to an activation slot and is outside the ordinary dynamic-name production slice.";
                        return true;
                    }

                    if (allowsDynamicIdentifiers)
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                    declineReason =
                        "Dynamic identifier assignment references are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.TypeOfIdentifier:
                    var typeOfIdentifier = operation.GetIdentifier(identifierConstants);
                    var hasTypeOfActivationSlot = TryResolveActivationSlot(typeOfIdentifier, activationSlots);
                    if (IsImplicitArgumentsIdentifier(typeOfIdentifier, activationSlots))
                    {
                        break;
                    }

                    if (!hasTypeOfActivationSlot)
                    {
                        if (!allowsDynamicIdentifiers)
                        {
                            declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                            declineReason = $"typeof identifier '{typeOfIdentifier.Name.Name}' requires dynamic lookup and is not eligible for production unified bytecode routing.";
                            return true;
                        }
                    }

                    break;

                case ExpressionOpKind.GetNamedProperty:
                    if (operation.ShortCircuitOnNullishTarget)
                    {
                        if (TryIsEmbeddedOptionalReadOperandOperation(program, operationIndex, identifierConstants, activationSlots))
                        {
                            break;
                        }

                        // Continuation hop of a multi-hop optional named chain (a?.b.c / a?.b?.c).
                        if (TryIsFirstBoundaryOptionalNamedChainCandidate(program, identifierConstants, activationSlots) ||
                            TryIsFirstBoundaryOptionalComputedPropertyReadChainCandidate(program, identifierConstants, activationSlots) ||
                            TryIsFirstBoundaryOptionalNamedThenComputedReadChainCandidate(program, identifierConstants, activationSlots))
                        {
                            break;
                        }

                        declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                        declineReason =
                            "Optional-chain property reads are outside the first production property-read boundary.";
                        return true;
                    }

                    if (operation.IsOptional)
                    {
                        // Named optional reads now lower through the general expression loop.
                        // The VM owns the short-circuit provenance bit needed by
                        // JumpIfShortCircuited, while unsupported adjacent optional-chain
                        // shapes still decline through their own operation checks.
                        break;
                    }

                    if (isCallTargetPreparationCandidate)
                    {
                        break;
                    }

                    if (isConstructInvocationCandidate)
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryNamedPropertyReadCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryObjectLiteralNamedPropertyReadCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryBinaryNamedPropertyReadCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsConditionalExpressionActivationResolvedNamedPropertyReadOperand(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryOptionalNamedChainCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryComputedPropertyReadChainCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers) ||
                        TryIsFirstBoundaryOptionalComputedPropertyReadChainCandidate(program, identifierConstants, activationSlots) ||
                        TryIsFirstBoundaryOptionalNamedThenComputedReadChainCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsNamedPropertyReadAtLogicalShortCircuitBoundary(program, operationIndex, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (isFirstBoundaryNamedCompoundPropertyWriteCandidate)
                    {
                        break;
                    }
                    if (isFirstBoundaryNamedLogicalPropertyWriteCandidate)
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryNestedNamedPropertyWriteCandidate(program, identifierConstants, activationSlots) ||
                        TryIsFirstBoundaryNestedNamedPropertyUpdateCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryPropertyReadBinaryExpressionCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryPropertyReadShortCircuitExpressionCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryNamedPropertyDeleteCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryComputedPropertyDeleteCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryOptionalComputedPropertyDeleteCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryOptionalNamedThenOptionalComputedPropertyDeleteCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryOptionalNamedPropertyDeleteCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (HasOptionalDeleteOperation(program))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                        declineReason =
                            "Optional-chain delete expressions are not eligible for production unified bytecode routing.";
                        return true;
                    }

                    if (EndsWithComputedPropertyDelete(program))
                    {
                        if (!allowsDynamicIdentifiers &&
                            HasOrdinaryDynamicExpressionDependency(program, activationSlots))
                        {
                            declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                            declineReason =
                                "Computed property delete key requires dynamic lookup and is not eligible for production unified bytecode routing.";
                            return true;
                        }

                        declineCode = UnifiedBytecodeProductionDeclineCode.DeleteDependency;
                        declineReason =
                            "Computed property deletes are outside the first production boundary unless they use activation-resolved/simple base and key operands.";
                        return true;
                    }

                    if (ContainsPropertyWriteOperation(program))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency;
                        declineReason =
                            "Compound/logical property writes are outside the first production property-write boundary.";
                        return true;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.PropertyReadBoundaryOutOfScope;
                    declineReason =
                        "Named property reads are outside the first production property-read boundary unless they are activation-resolved or admitted dynamic-identifier base reads.";
                    return true;

                case ExpressionOpKind.GetComputedProperty:
                    if (TryIsEmbeddedOptionalReadOperandOperation(program, operationIndex, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (operation.ShortCircuitOnNullishTarget)
                    {
                        if (TryIsEmbeddedOptionalReadOperandOperation(program, operationIndex, identifierConstants, activationSlots))
                        {
                            break;
                        }

                        // a?.b[k] shape — admitted when the program matches the optional named then computed shape.
                        if (TryIsFirstBoundaryOptionalNamedThenComputedReadChainCandidate(program, identifierConstants, activationSlots))
                        {
                            break;
                        }

                        declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                        declineReason =
                            "Optional-chain computed property reads are outside the first production property-read boundary.";
                        return true;
                    }

                    if (TryIsFirstBoundaryComputedPropertyReadChainCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryOptionalComputedPropertyReadChainCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (isConstructInvocationCandidate)
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryComputedCompoundPropertyWriteCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }
                    if (TryIsFirstBoundaryComputedLogicalPropertyWriteCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    if (ContainsPropertyWriteOperation(program))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency;
                        declineReason =
                            "Compound/logical computed property writes are outside the first production property-write boundary.";
                        return true;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.PropertyReadBoundaryOutOfScope;
                    declineReason =
                        "Computed property reads are outside the first production property-read boundary unless they use RequireObjectCoercible(Depth: 1) then ResolvePropertyKey immediately before GetComputedProperty.";
                    return true;

                case ExpressionOpKind.SetNamedProperty:
                case ExpressionOpKind.SetComputedProperty:
                    if (TryIsFirstBoundaryPropertyWriteCandidate(program, identifierConstants, activationSlots, allowsDynamicIdentifiers) ||
                        TryIsFirstBoundaryNestedNamedPropertyWriteCandidate(program, identifierConstants, activationSlots) ||
                        isFirstBoundaryNamedCompoundPropertyWriteCandidate ||
                        isFirstBoundaryNamedLogicalPropertyWriteCandidate ||
                        TryIsFirstBoundaryComputedCompoundPropertyWriteCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers) ||
                        TryIsFirstBoundaryComputedLogicalPropertyWriteCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency;
                    declineReason =
                        "Property writes are outside the first production boundary unless they use an activation-resolved base with simple key/value operands.";
                    return true;

                case ExpressionOpKind.UpdateIdentifier:
                    var updateIdentifier = operation.GetIdentifier(identifierConstants);
                    if (TryResolveActivationSlot(updateIdentifier, activationSlots))
                    {
                        break;
                    }

                    if (allowsDynamicIdentifiers)
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.PropertyUpdateDependency;
                    declineReason = "Update expressions are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.UpdateNamedProperty:
                case ExpressionOpKind.UpdateComputedProperty:
                    if (TryIsFirstBoundaryPropertyUpdateCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers) ||
                        TryIsFirstBoundaryNestedNamedPropertyUpdateCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.PropertyUpdateDependency;
                    declineReason =
                        "Property updates are outside the first production boundary unless they use an activation-resolved base with a simple optional-free key.";
                    return true;

                case ExpressionOpKind.DeleteIdentifier:
                    if (allowsDynamicIdentifiers)
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.DeleteDependency;
                    declineReason = "delete expressions are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.DeleteNamedProperty:
                    if (TryIsFirstBoundaryOptionalNamedPropertyDeleteCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (HasOptionalDeleteOperation(program))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                        declineReason =
                            "Optional-chain delete expressions are not eligible for production unified bytecode routing.";
                        return true;
                    }

                    if (TryIsFirstBoundaryNamedPropertyDeleteCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.DeleteDependency;
                    declineReason =
                        "Named property deletes are outside the first production boundary unless they use an activation-resolved non-private base/property chain.";
                    return true;

                case ExpressionOpKind.DeleteComputedProperty:
                    if (TryIsFirstBoundaryOptionalNamedThenComputedPropertyDeleteCandidate(program, identifierConstants, activationSlots) ||
                        TryIsFirstBoundaryOptionalNamedThenOptionalComputedPropertyDeleteCandidate(program, identifierConstants, activationSlots) ||
                        TryIsFirstBoundaryOptionalComputedPropertyDeleteCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (HasOptionalDeleteOperation(program))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                        declineReason =
                            "Optional-chain delete expressions are not eligible for production unified bytecode routing.";
                        return true;
                    }

                    if (TryIsFirstBoundaryComputedPropertyDeleteCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.DeleteDependency;
                    declineReason =
                        "Computed property deletes are outside the first production boundary unless they use activation-resolved/simple base and key operands.";
                    return true;

                case ExpressionOpKind.EnsureSuperReference:
                case ExpressionOpKind.GetNamedSuperProperty:
                case ExpressionOpKind.GetComputedSuperProperty:
                case ExpressionOpKind.SetNamedSuperProperty:
                case ExpressionOpKind.SetComputedSuperProperty:
                case ExpressionOpKind.UpdateNamedSuperProperty:
                case ExpressionOpKind.UpdateComputedSuperProperty:
                    break;

                case ExpressionOpKind.JumpIfFalse:
                case ExpressionOpKind.JumpIfConditionalFalse:
                case ExpressionOpKind.JumpIfTrue:
                case ExpressionOpKind.JumpIfNotNullish:
                case ExpressionOpKind.Jump:
                case ExpressionOpKind.Pop:
                    if (TryIsFirstBoundaryOptionalComputedPropertyDeleteCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    // Admitted: Jump and Pop appear in the conditional (?:) expression IR.
                    // Pop discards the condition value on the taken/not-taken path;
                    // Jump is the unconditional forward branch to the end of the ternary.
                    break;

                case ExpressionOpKind.JumpIfNullish:
                    if (TryIsFirstBoundaryOptionalComputedPropertyDeleteCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (isCallTargetPreparationCandidate || !operation.ReplaceWithUndefined)
                    {
                        break;
                    }

                    if (TryIsEmbeddedOptionalReadOperandOperation(program, operationIndex, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    // Nullish guard of a?.[k] — admitted when the program matches the optional computed read shape.
                    if (TryIsFirstBoundaryOptionalComputedPropertyReadChainCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                    declineReason =
                        "Optional-chain short-circuiting is outside the first production property-read boundary.";
                    return true;

                case ExpressionOpKind.JumpIfShortCircuited:
                    break;

                case ExpressionOpKind.LoadClassLiteral:
                    break;

                case ExpressionOpKind.ArraySpread:
                    if (IsOperationInSimpleArrayLiteralSpan(program, operationIndex, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency;
                    declineReason =
                        "Array spread with non-simple source is not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.DefineObjectMethod:
                case ExpressionOpKind.DefineComputedObjectMethod:
                case ExpressionOpKind.DefineObjectAccessor:
                case ExpressionOpKind.DefineComputedObjectAccessor:
                    break;

                case ExpressionOpKind.ObjectSpread:
                    if (IsOperationInSimpleObjectLiteralSpan(program, operationIndex, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency;
                    declineReason =
                        "Object spread with non-simple source or outside an admitted object literal span is not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.DefineObjectProperty:
                    if (operation.GetString(stringConstants).IsPrivateName())
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.PrivateFieldDependency;
                        declineReason =
                            "Private-field expressions are not eligible for production unified bytecode routing.";
                        return true;
                    }

                    break;

                case ExpressionOpKind.DefineComputedObjectProperty:
                    break;

                case ExpressionOpKind.PrivateFieldIn:
                    break;

                case ExpressionOpKind.ApplyBindingTarget:
                    break;

                case ExpressionOpKind.Binary:
                    if (!IsProductionBinaryOperator(operation.Operator))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                        declineReason =
                            $"Binary operator '{FormatBinaryOperator(operation.Operator)}' is not eligible for production unified bytecode routing.";
                        return true;
                    }

                    break;
            }
        }

        declineCode = UnifiedBytecodeProductionDeclineCode.None;
        declineReason = string.Empty;
        return false;
    }

    private static bool HasOrdinaryDynamicExpressionDependency(
        ExpressionProgram program,
        ActivationSlotShape activationSlots)
    {
        var identifierConstants = program.IdentifierConstants.AsSpan();
        for (var operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
        {
            var operation = program.GetOperation(operationIndex);
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadIdentifier:
                case ExpressionOpKind.ResolveIdentifierReference:
                case ExpressionOpKind.StoreResolvedIdentifier:
                case ExpressionOpKind.StoreIdentifier:
                case ExpressionOpKind.TypeOfIdentifier:
                case ExpressionOpKind.UpdateIdentifier:
                case ExpressionOpKind.DeleteIdentifier:
                    var identifier = operation.GetIdentifier(identifierConstants);
                    if (IsImplicitArgumentsIdentifier(identifier, activationSlots))
                    {
                        break;
                    }

                    if (IsOrdinaryDynamicIdentifier(identifier, activationSlots))
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private static bool HasOptionalChainOperation(ExpressionProgram program)
    {
        for (var operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
        {
            var operation = program.GetOperation(operationIndex);
            if (operation is { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: true } ||
                operation.Kind == ExpressionOpKind.JumpIfShortCircuited)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasOptionalDeleteOperation(ExpressionProgram program)
    {
        var hasDelete = false;
        var hasNullishJump = false;
        for (var operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
        {
            var operation = program.GetOperation(operationIndex);
            hasDelete |= operation.Kind is ExpressionOpKind.DeleteNamedProperty or ExpressionOpKind.DeleteComputedProperty;
            hasNullishJump |= operation.Kind == ExpressionOpKind.JumpIfNullish;
        }

        return hasDelete && hasNullishJump;
    }

    private static bool EndsWithComputedPropertyDelete(ExpressionProgram program) =>
        program.OperationCount > 0 &&
        program.GetOperation(program.OperationCount - 1).Kind == ExpressionOpKind.DeleteComputedProperty;

    private static bool IsTrueLiteral(ExpressionProgram program, int operationIndex)
    {
        var operation = program.GetOperation(operationIndex);
        return operation.Kind == ExpressionOpKind.LoadLiteral &&
               operation.GetLiteral(program.LiteralConstants.AsSpan())
                   .Equals(JsTypes.JsValue.True);
    }

    private static bool TryIsConstructInvocationCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 2)
        {
            return false;
        }

        var constructIndex = FindConstructInvocationBoundaryIndex(program);
        if (constructIndex < 1)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        for (var operationIndex = 0; operationIndex < constructIndex; operationIndex++)
        {
            var operation = program.GetOperation(operationIndex);
            if (IsPrivateNamedPropertyOperation(operation, stringConstants))
            {
                return false;
            }

            switch (operation.Kind)
            {
                case ExpressionOpKind.GetNamedProperty:
                    if (operation.IsOptional || operation.ShortCircuitOnNullishTarget)
                    {
                        return false;
                    }

                    break;

                case ExpressionOpKind.GetComputedProperty:
                    if (operation.ShortCircuitOnNullishTarget ||
                        !TryIsConstructComputedPropertyRead(program, operationIndex, identifierConstants, activationSlots))
                    {
                        return false;
                    }

                    break;
            }
        }

        return true;
    }

    private static int FindConstructInvocationBoundaryIndex(ExpressionProgram program)
    {
        var constructIndex = program.OperationCount - 1;
        if (program.GetOperation(constructIndex).Kind == ExpressionOpKind.Construct)
        {
            return constructIndex;
        }

        var stringConstants = program.StringConstants.AsSpan();
        while (constructIndex > 0 &&
               IsPlainNamedPropertyRead(program.GetOperation(constructIndex), stringConstants))
        {
            constructIndex--;
        }

        return program.GetOperation(constructIndex).Kind == ExpressionOpKind.Construct
            ? constructIndex
            : -1;
    }

    private static bool TryIsConstructComputedPropertyRead(
        ExpressionProgram program,
        int getComputedIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (getComputedIndex < 4)
        {
            return false;
        }

        var requireObjectCoercible = program.GetOperation(getComputedIndex - 2);
        if (requireObjectCoercible.Kind != ExpressionOpKind.RequireObjectCoercible ||
            requireObjectCoercible.Depth != 1)
        {
            return false;
        }

        if (program.GetOperation(getComputedIndex - 1).Kind != ExpressionOpKind.ResolvePropertyKey)
        {
            return false;
        }

        return TryGetActivationResolvedValue(program.GetOperation(getComputedIndex - 4), identifierConstants, activationSlots) &&
               IsSimpleComputedPropertyKey(program.GetOperation(getComputedIndex - 3), identifierConstants, activationSlots);
    }

    private static bool IsSupportedPushEnvironment(
        PushEnvironmentInstruction instruction,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>>? flatSlotMappings)
    {
        // Per-iteration binding environments (for (const/let x in/of ...)) are admitted when all
        // per-iteration slots resolve to flat activation slots. The per-iteration rebinding semantics
        // are modeled by ForInMoveNext/IteratorMoveNext writing to __forIn_value/__iter_value, the
        // PushEnvironment resetting the lexical slot to Uninitialized, and the binding statement
        // assigning the value slot to the per-iteration slot — all within the flat-slot model.
        if (instruction.ScopeId < 0 ||
            instruction.SlotCount < 0 ||
            instruction.SlotMap.IsEmpty ||
            flatSlotMappings is null ||
            !flatSlotMappings.TryGetValue(instruction.ScopeId, out var mappings) ||
            mappings.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var lexicalSlotIndex in instruction.LexicalSlotIndices)
        {
            if (!ContainsSlotMapping(mappings, lexicalSlotIndex))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsSlotMapping(
        ImmutableArray<(int SlotIndex, int FlatSlotId)> mappings,
        int slotIndex)
    {
        foreach (var mapping in mappings)
        {
            if (mapping.SlotIndex == slotIndex && mapping.FlatSlotId >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // Exposed to the test assembly (AC-5 negative coverage): the async-kind and awaited-source
    // arms must keep declining with their explicit reasons even though sync TDZ heads are admitted.
    internal static bool IsSupportedIteratorInit(IteratorInitInstruction instruction, out string reason)
    {
        if (instruction.IteratorKind != IteratorDriverKind.Sync)
        {
            reason = "Async iterator driver state is not eligible for production unified bytecode routing.";
            return false;
        }

        if (instruction.IterableProgram is null || instruction.AwaitedProgram is not null)
        {
            reason = "Iterator driver sources must be lowered to synchronous expression bytecode.";
            return false;
        }

        // Slice A (#2678): sync iterator drivers that own a TDZ head environment
        // (for example `for (const x of ...)`) are now admitted. The production
        // compiler resolves the head bindings to flat slots and the VM marks them
        // uninitialized (with const-ness) so the temporal dead zone is enforced on
        // the production path. Async-kind and awaited-source drivers above remain
        // declined pending later slices.
        reason = string.Empty;
        return true;
    }

    // Exposed to the test assembly (AC-5 negative coverage): the awaited-source arm must keep
    // declining with its explicit reason even though sync TDZ heads are admitted.
    internal static bool IsSupportedForInInit(ForInInitInstruction instruction, out string reason)
    {
        if (instruction.ObjectProgram is null || instruction.AwaitedProgram is not null)
        {
            reason = "for-in driver sources must be lowered to synchronous expression bytecode.";
            return false;
        }

        // Slice A (#2678): for-in drivers that own a TDZ head environment
        // (for example `for (const k in ...)`) are now admitted. The production
        // compiler resolves the head bindings to flat slots and the VM marks them
        // uninitialized (with const-ness) so the temporal dead zone is enforced on
        // the production path. Awaited-source drivers above remain declined pending
        // later slices.
        reason = string.Empty;
        return true;
    }

    private static bool IsSupportedArrayDestructuringInstruction(
        ExecutionInstruction instruction,
        out string reason)
    {
        switch (instruction)
        {
            case ArrayDestructuringInitInstruction { SourceProgram.IsEmpty: false }:
            case ArrayDestructuringElementInstruction:
            case ArrayDestructuringRestInstruction:
            case ArrayDestructuringCloseInstruction:
                reason = string.Empty;
                return true;

            default:
                reason =
                    "Only array destructuring driver instructions with lowered expression bytecode are eligible for production unified bytecode routing.";
                return false;
        }
    }

    private static bool IsSupportedObjectDestructuringInstruction(
        ExecutionInstruction instruction,
        out string reason)
    {
        switch (instruction)
        {
            case ObjectDestructuringInitInstruction { SourceProgram.IsEmpty: false }:
            case ObjectDestructuringPropertyInstruction:
            case ObjectDestructuringRestInstruction:
            case ObjectDestructuringCloseInstruction:
                reason = string.Empty;
                return true;

            default:
                reason =
                    "Only object destructuring driver instructions with lowered expression bytecode are eligible for production unified bytecode routing.";
                return false;
        }
    }

    private static bool TryGetActivationResolvedIdentifier(
        PackedExpressionOp operation,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (operation.Kind is not (ExpressionOpKind.LoadIdentifier or ExpressionOpKind.LoadIdentifierCallTarget) ||
            operation.IsArguments)
        {
            return false;
        }

        var identifier = operation.GetIdentifier(identifierConstants);
        return TryResolveActivationSlot(identifier, activationSlots);
    }

    private static bool TryGetActivationResolvedValue(
        PackedExpressionOp operation,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (operation.Kind is ExpressionOpKind.LoadThis or ExpressionOpKind.LoadNewTarget)
        {
            return true;
        }

        if (operation.Kind != ExpressionOpKind.LoadIdentifier || operation.IsArguments)
        {
            return false;
        }

        var identifier = operation.GetIdentifier(identifierConstants);
        return TryResolveActivationSlot(identifier, activationSlots);
    }

    private static bool TryGetActivationOrImplicitArgumentsObjectReadValue(
        PackedExpressionOp operation,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        return TryGetActivationResolvedValue(operation, identifierConstants, activationSlots) ||
               allowsDynamicIdentifiers && (
                   operation.Kind == ExpressionOpKind.LoadIdentifier &&
                   operation.IsArguments ||
                   TryGetPlainDynamicIdentifierReadValue(operation, identifierConstants, activationSlots));
    }

    private static bool TryIsFirstBoundaryNamedPropertyReadCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 2)
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots) &&
            !(allowsDynamicIdentifiers &&
              TryGetPlainDynamicIdentifierReadValue(program.GetOperation(0), identifierConstants, activationSlots)))
        {
            return false;
        }

        for (var index = 1; index < program.OperationCount; index++)
        {
            var operation = program.GetOperation(index);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetPlainDynamicIdentifierReadValue(
        PackedExpressionOp operation,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (operation.Kind != ExpressionOpKind.LoadIdentifier || operation.IsArguments)
        {
            return false;
        }

        var identifier = operation.GetIdentifier(identifierConstants);
        return identifier.FlatSlotId < 0 &&
               !TryResolveActivationSlot(identifier, activationSlots);
    }

    private static bool TryIsFirstBoundaryObjectLiteralNamedPropertyReadCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (!TryMeasureSimpleObjectLiteralSpan(
                program,
                startIndex: 0,
                identifierConstants,
                activationSlots,
                out var objectSpanLength) ||
            objectSpanLength >= program.OperationCount)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        for (var index = objectSpanLength; index < program.OperationCount; index++)
        {
            var operation = program.GetOperation(index);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget ||
                operation.GetString(stringConstants).IsPrivateName())
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryIsFirstBoundaryBinaryNamedPropertyReadCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (!TryMeasureSimpleBinaryOperandSpan(
                program,
                startIndex: 0,
                identifierConstants,
                activationSlots,
                out var binarySpanLength) ||
            binarySpanLength >= program.OperationCount)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        for (var index = binarySpanLength; index < program.OperationCount; index++)
        {
            var operation = program.GetOperation(index);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget ||
                operation.GetString(stringConstants).IsPrivateName())
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryIsConditionalExpressionActivationResolvedNamedPropertyReadOperand(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (FindFirstOperation(program, ExpressionOpKind.JumpIfConditionalFalse) < 0)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var operation = program.GetOperation(operationIndex);
        if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
            operation.IsOptional ||
            operation.ShortCircuitOnNullishTarget ||
            operation.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        var chainStartIndex = operationIndex;
        while (chainStartIndex > 0)
        {
            var previous = program.GetOperation(chainStartIndex - 1);
            if (previous.Kind != ExpressionOpKind.GetNamedProperty)
            {
                break;
            }

            if (previous.IsOptional ||
                previous.ShortCircuitOnNullishTarget ||
                previous.GetString(stringConstants).IsPrivateName())
            {
                return false;
            }

            chainStartIndex--;
        }

        return chainStartIndex > 0 &&
               TryGetActivationResolvedValue(
                   program.GetOperation(chainStartIndex - 1),
                   identifierConstants,
                   activationSlots);
    }

    /// <summary>
    /// Returns <c>true</c> when the <see cref="ExpressionOpKind.GetNamedProperty"/> operation at
    /// <paramref name="operationIndex"/> is the last op before a logical short-circuit jump
    /// (<c>JumpIfFalse</c>, <c>JumpIfTrue</c>, <c>JumpIfNotNullish</c>), and ops 0..<paramref name="operationIndex"/>
    /// form a valid activation-resolved named property read chain (<c>base, GetNamedProperty+</c>).
    /// This covers <c>this.prop &amp;&amp; rhs</c>, <c>slot.prop || rhs</c>, and <c>slot.prop ?? rhs</c>
    /// where the property read is the LHS of the operator.
    /// </summary>
    private static bool TryIsNamedPropertyReadAtLogicalShortCircuitBoundary(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        var nextIndex = operationIndex + 1;
        if (nextIndex >= program.OperationCount)
        {
            return false;
        }

        var nextOp = program.GetOperation(nextIndex);
        if (nextOp.Kind is not (ExpressionOpKind.JumpIfFalse or ExpressionOpKind.JumpIfTrue or ExpressionOpKind.JumpIfNotNullish))
        {
            return false;
        }

        // Validate that ops 0..operationIndex form a valid named property read chain.
        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        for (var index = 1; index <= operationIndex; index++)
        {
            var op = program.GetOperation(index);
            if (op.Kind != ExpressionOpKind.GetNamedProperty ||
                op.GetString(program.StringConstants.AsSpan()).IsPrivateName() ||
                op.IsOptional ||
                op.ShortCircuitOnNullishTarget)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryIsFirstBoundaryComputedPropertyReadChainCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers = false)
    {
        if (!TryGetComputedPropertyKeyPayloadBounds(program, out _, out _))
        {
            return false;
        }

        return TryGetActivationOrImplicitArgumentsObjectReadValue(
            program.GetOperation(0),
            identifierConstants,
            activationSlots,
            allowsDynamicIdentifiers);
    }

    private static bool TryGetComputedPropertyKeyPayloadBounds(
        ExpressionProgram program,
        out int keyStart,
        out int keyEndExclusive)
    {
        keyStart = 0;
        keyEndExclusive = 0;
        if (program.OperationCount < 5)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var computedPrefixEnd = 1;
        while (computedPrefixEnd < program.OperationCount &&
               IsPlainNamedPropertyRead(program.GetOperation(computedPrefixEnd), stringConstants))
        {
            computedPrefixEnd++;
        }

        var computedSuffixStart = program.OperationCount;
        while (computedSuffixStart > computedPrefixEnd + 1 &&
               IsPlainNamedPropertyRead(program.GetOperation(computedSuffixStart - 1), stringConstants))
        {
            computedSuffixStart--;
        }

        var computedIndex = computedSuffixStart - 1;
        if (computedIndex - computedPrefixEnd < 3)
        {
            return false;
        }

        var requireObjectCoercible = program.GetOperation(computedIndex - 2);
        if (requireObjectCoercible.Kind != ExpressionOpKind.RequireObjectCoercible ||
            requireObjectCoercible.Depth != 1)
        {
            return false;
        }

        var resolvePropertyKey = program.GetOperation(computedIndex - 1);
        if (resolvePropertyKey.Kind != ExpressionOpKind.ResolvePropertyKey)
        {
            return false;
        }

        var getComputedProperty = program.GetOperation(computedIndex);
        if (getComputedProperty.Kind != ExpressionOpKind.GetComputedProperty ||
            getComputedProperty.ShortCircuitOnNullishTarget)
        {
            return false;
        }

        keyStart = computedPrefixEnd;
        keyEndExclusive = computedIndex - 2;
        return true;
    }

    private static bool TryIsFirstBoundaryNamedPropertyDeleteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 2)
        {
            return false;
        }

        if (!TryGetActivationOrPlainDynamicIdentifierReadValue(
                program.GetOperation(0),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        for (var index = 1; index < program.OperationCount - 1; index++)
        {
            var operation = program.GetOperation(index);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.GetString(stringConstants).IsPrivateName() ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget)
            {
                return false;
            }
        }

        var deleteProperty = program.GetOperation(program.OperationCount - 1);
        return deleteProperty.Kind == ExpressionOpKind.DeleteNamedProperty &&
               !deleteProperty.GetString(stringConstants).IsPrivateName();
    }

    private static bool TryIsFirstBoundaryComputedPropertyDeleteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers = false)
    {
        if (program.OperationCount < 3)
        {
            return false;
        }

        if (!TryGetActivationOrPlainDynamicIdentifierReadValue(
                program.GetOperation(0),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var keyStartIndex = 1;
        for (; keyStartIndex < program.OperationCount - 1; keyStartIndex++)
        {
            var operation = program.GetOperation(keyStartIndex);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.GetString(stringConstants).IsPrivateName() ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget)
            {
                break;
            }
        }

        return keyStartIndex < program.OperationCount - 1 &&
               IsSupportedComputedPropertyKeySpan(
                   program,
                   startInclusive: keyStartIndex,
                   endExclusive: program.OperationCount - 1,
                   identifierConstants,
                   activationSlots,
                   allowsDynamicIdentifiers) &&
               program.GetOperation(program.OperationCount - 1).Kind == ExpressionOpKind.DeleteComputedProperty;
    }

    // Admits delete a?.b[k]:
    // [activation-resolved base, GetNamedProperty(IsOptional:true, !SC, non-private), simple key, DeleteComputedProperty].
    // The compiler emits the nullish guard before the named hop so key evaluation is skipped when the base short-circuits.
    private static bool TryIsFirstBoundaryOptionalNamedThenComputedPropertyDeleteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 4 ||
            program.GetOperation(program.OperationCount - 1).Kind != ExpressionOpKind.DeleteComputedProperty)
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var firstHop = program.GetOperation(1);
        return firstHop.Kind == ExpressionOpKind.GetNamedProperty &&
               firstHop.IsOptional &&
               !firstHop.ShortCircuitOnNullishTarget &&
               !firstHop.GetString(program.StringConstants.AsSpan()).IsPrivateName() &&
               IsSupportedComputedPropertyKeySpan(
                   program,
                   startInclusive: 2,
                   endExclusive: program.OperationCount - 1,
                   identifierConstants,
                   activationSlots);
    }

    // Admits delete a?.b and delete a.b?.c:
    // [activation-resolved base, GetNamedProperty(non-optional, non-private)*,
    //  JumpIfNullish, DeleteNamedProperty, Jump, Pop, true].
    private static bool TryIsFirstBoundaryOptionalNamedPropertyDeleteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 6 ||
            !TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var jumpIndex = 1;
        while (jumpIndex < program.OperationCount)
        {
            var operation = program.GetOperation(jumpIndex);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget ||
                operation.GetString(stringConstants).IsPrivateName())
            {
                break;
            }

            jumpIndex++;
        }

        if (jumpIndex >= program.OperationCount)
        {
            return false;
        }

        var deleteIndex = program.OperationCount - 4;
        var endJumpIndex = program.OperationCount - 3;
        var popIndex = program.OperationCount - 2;
        var trueIndex = program.OperationCount - 1;
        var deleteProperty = program.GetOperation(deleteIndex);

        return deleteIndex == jumpIndex + 1 &&
               program.GetOperation(jumpIndex) is { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: false } jumpIfNullish &&
               jumpIfNullish.Target == popIndex &&
               deleteProperty.Kind == ExpressionOpKind.DeleteNamedProperty &&
               !deleteProperty.GetString(stringConstants).IsPrivateName() &&
               program.GetOperation(endJumpIndex).Kind == ExpressionOpKind.Jump &&
               program.GetOperation(endJumpIndex).Target == program.OperationCount &&
               program.GetOperation(popIndex).Kind == ExpressionOpKind.Pop &&
               IsTrueLiteral(program, trueIndex);
    }

    // Admits delete a?.b?.[k]:
    // [activation-resolved base, GetNamedProperty(IsOptional:true, !SC, non-private),
    //  JumpIfNullish, supported key span, DeleteComputedProperty, Jump, Pop, true].
    private static bool TryIsFirstBoundaryOptionalNamedThenOptionalComputedPropertyDeleteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 7 ||
            !TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var firstHop = program.GetOperation(1);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        var jumpIndex = 2;
        var deleteIndex = program.OperationCount - 4;
        var endJumpIndex = program.OperationCount - 3;
        var popIndex = program.OperationCount - 2;
        var trueIndex = program.OperationCount - 1;

        return deleteIndex > jumpIndex + 1 &&
               program.GetOperation(jumpIndex) is { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: false } jumpIfNullish &&
               jumpIfNullish.Target == popIndex &&
               IsSupportedComputedPropertyKeySpan(
                   program,
                   startInclusive: jumpIndex + 1,
                   endExclusive: deleteIndex,
                   identifierConstants,
                   activationSlots) &&
               program.GetOperation(deleteIndex).Kind == ExpressionOpKind.DeleteComputedProperty &&
               program.GetOperation(endJumpIndex).Kind == ExpressionOpKind.Jump &&
               program.GetOperation(endJumpIndex).Target == program.OperationCount &&
               program.GetOperation(popIndex).Kind == ExpressionOpKind.Pop &&
               IsTrueLiteral(program, trueIndex);
    }

    // Admits delete a?.[k] and delete a.b?.[k]:
    // [activation-resolved base, GetNamedProperty(non-optional, non-private)*,
    //  JumpIfNullish, simple key, DeleteComputedProperty, Jump, Pop, true].
    private static bool TryIsFirstBoundaryOptionalComputedPropertyDeleteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 7 ||
            !TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var jumpIndex = 1;
        while (jumpIndex < program.OperationCount)
        {
            var operation = program.GetOperation(jumpIndex);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget ||
                operation.GetString(stringConstants).IsPrivateName())
            {
                break;
            }

            jumpIndex++;
        }

        var jumpIfNullish = program.GetOperation(jumpIndex);
        var deleteIndex = program.OperationCount - 4;
        var endJumpIndex = program.OperationCount - 3;
        var popIndex = program.OperationCount - 2;
        var trueIndex = program.OperationCount - 1;

        return jumpIfNullish.Kind == ExpressionOpKind.JumpIfNullish &&
               !jumpIfNullish.ReplaceWithUndefined &&
               jumpIfNullish.Target == popIndex &&
               deleteIndex > jumpIndex + 1 &&
               IsSupportedComputedPropertyKeySpan(
                   program,
                   startInclusive: jumpIndex + 1,
                   endExclusive: deleteIndex,
                   identifierConstants,
                   activationSlots) &&
               program.GetOperation(deleteIndex).Kind == ExpressionOpKind.DeleteComputedProperty &&
               program.GetOperation(endJumpIndex).Kind == ExpressionOpKind.Jump &&
               program.GetOperation(endJumpIndex).Target == program.OperationCount &&
               program.GetOperation(popIndex).Kind == ExpressionOpKind.Pop &&
               IsTrueLiteral(program, trueIndex);
    }

    // Admits the simple a?.b shape: [activation-resolved base, GetNamedProperty(IsOptional:true, !ShortCircuitOnNullishTarget, non-private)].
    private static bool TryIsFirstBoundaryOptionalNamedPropertyReadCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount != 2)
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var getNamedOp = program.GetOperation(1);
        return getNamedOp.Kind == ExpressionOpKind.GetNamedProperty &&
               getNamedOp.IsOptional &&
               !getNamedOp.ShortCircuitOnNullishTarget &&
               !getNamedOp.GetString(program.StringConstants.AsSpan()).IsPrivateName();
    }

    // Admits multi-hop optional named chains a?.b.c, a?.b?.c, and a.b?.c:
    // [activation-resolved base, GetNamedProperty(non-optional, non-private)*,
    //  GetNamedProperty(IsOptional:true, !ShortCircuitOnNullishTarget, non-private),
    //  GetNamedProperty(ShortCircuitOnNullishTarget:true, non-private)+].
    // The compiler lowers this to a jump-based form (JumpIfNullishReplaceUndefined at each
    // optional hop targeting the chain end, plus plain GetNamedProperty reads), so the VM
    // never needs the parallel short-circuit flag array.
    private static bool TryIsFirstBoundaryOptionalNamedChainCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        // Two-op programs are the simple a?.b form handled by the dedicated candidate above.
        if (program.OperationCount < 3)
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var optionalStartIndex = 1;
        while (optionalStartIndex < program.OperationCount)
        {
            var prefixOp = program.GetOperation(optionalStartIndex);
            if (prefixOp.Kind != ExpressionOpKind.GetNamedProperty ||
                prefixOp.IsOptional ||
                prefixOp.ShortCircuitOnNullishTarget ||
                prefixOp.GetString(stringConstants).IsPrivateName())
            {
                break;
            }

            optionalStartIndex++;
        }

        if (optionalStartIndex >= program.OperationCount)
        {
            return false;
        }

        var firstHop = program.GetOperation(optionalStartIndex);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        for (var index = optionalStartIndex + 1; index < program.OperationCount; index++)
        {
            var op = program.GetOperation(index);
            if (op.Kind != ExpressionOpKind.GetNamedProperty ||
                !op.ShortCircuitOnNullishTarget ||
                op.GetString(stringConstants).IsPrivateName())
            {
                return false;
            }
        }

        return true;
    }

    // Admits the a?.b.c chain shape:
    // [activation-resolved base, GetNamedProperty(IsOptional:true, !SC, non-private), GetNamedProperty(!IsOptional, SC:true, non-private)+]
    // The optional guard on the first access is converted to JumpIfNullishReplaceUndefined in the compiler;
    // subsequent accesses are regular GetNamedProperty ops (throwing TypeError on null base, which is correct for a?.b.c).
    private static bool TryIsFirstBoundaryOptionalNamedPropertyReadChainCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 3)
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var firstPropOp = program.GetOperation(1);
        if (firstPropOp.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstPropOp.IsOptional ||
            firstPropOp.ShortCircuitOnNullishTarget ||
            firstPropOp.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        for (var index = 2; index < program.OperationCount; index++)
        {
            var op = program.GetOperation(index);
            if (op.Kind != ExpressionOpKind.GetNamedProperty ||
                op.IsOptional ||
                !op.ShortCircuitOnNullishTarget ||
                op.GetString(stringConstants).IsPrivateName())
            {
                return false;
            }
        }

        return true;
    }

    // Admits the a?.b[k], a?.b[k].c, and a.b?.c[k] shapes:
    // [activation-resolved base, GetNamedProperty(non-optional, non-private)*,
    //  GetNamedProperty(IsOptional:true, !SC, non-private), key..., GetComputedProperty(SC:true),
    //  GetNamedProperty(SC:true)*]
    private static bool TryIsFirstBoundaryOptionalNamedThenComputedReadChainCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 4)
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var optionalStartIndex = 1;
        while (optionalStartIndex < program.OperationCount)
        {
            var prefixOp = program.GetOperation(optionalStartIndex);
            if (prefixOp.Kind != ExpressionOpKind.GetNamedProperty ||
                prefixOp.IsOptional ||
                prefixOp.ShortCircuitOnNullishTarget ||
                prefixOp.GetString(stringConstants).IsPrivateName())
            {
                break;
            }

            optionalStartIndex++;
        }

        if (optionalStartIndex >= program.OperationCount)
        {
            return false;
        }

        var firstPropOp = program.GetOperation(optionalStartIndex);
        if (firstPropOp.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstPropOp.IsOptional ||
            firstPropOp.ShortCircuitOnNullishTarget ||
            firstPropOp.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        var computedSuffixStart = program.OperationCount;
        while (computedSuffixStart > optionalStartIndex + 3)
        {
            var suffixOp = program.GetOperation(computedSuffixStart - 1);
            if (suffixOp.Kind != ExpressionOpKind.GetNamedProperty ||
                suffixOp.IsOptional ||
                !suffixOp.ShortCircuitOnNullishTarget ||
                suffixOp.GetString(stringConstants).IsPrivateName())
            {
                break;
            }

            computedSuffixStart--;
        }

        var computedIndex = computedSuffixStart - 1;
        var computedOp = program.GetOperation(computedIndex);
        return computedOp.Kind == ExpressionOpKind.GetComputedProperty &&
               computedOp.ShortCircuitOnNullishTarget &&
               IsSupportedComputedPropertyKeySpan(
                   program,
                   optionalStartIndex + 1,
                   computedIndex,
                   identifierConstants,
        activationSlots);
    }

    // Admits a?.[k], a.b?.[k], and optional-computed read continuations:
    // [activation-resolved base, GetNamedProperty(non-optional, non-private)*,
    //  JumpIfNullish(ReplaceWithUndefined:true), key..., GetComputedProperty(!ShortCircuitOnNullishTarget),
    //  GetNamedProperty(ShortCircuitOnNullishTarget:true)*].
    private static bool TryIsFirstBoundaryOptionalComputedPropertyReadChainCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 4)
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var jumpIndex = 1;
        while (jumpIndex < program.OperationCount)
        {
            var prefixOp = program.GetOperation(jumpIndex);
            if (prefixOp.Kind != ExpressionOpKind.GetNamedProperty ||
                prefixOp.IsOptional ||
                prefixOp.ShortCircuitOnNullishTarget ||
                prefixOp.GetString(stringConstants).IsPrivateName())
            {
                break;
            }

            jumpIndex++;
        }

        if (jumpIndex >= program.OperationCount)
        {
            return false;
        }

        var jumpOp = program.GetOperation(jumpIndex);
        if (jumpOp.Kind != ExpressionOpKind.JumpIfNullish || !jumpOp.ReplaceWithUndefined)
        {
            return false;
        }

        var computedSuffixStart = program.OperationCount;
        while (computedSuffixStart > jumpIndex + 2 &&
               IsShortCircuitNamedPropertyRead(program.GetOperation(computedSuffixStart - 1), stringConstants))
        {
            computedSuffixStart--;
        }

        var computedIndex = computedSuffixStart - 1;
        if (computedIndex <= jumpIndex + 1 || jumpOp.Target != computedIndex + 1)
        {
            return false;
        }

        var getComputedOp = program.GetOperation(computedIndex);
        return getComputedOp.Kind == ExpressionOpKind.GetComputedProperty &&
               !getComputedOp.ShortCircuitOnNullishTarget &&
               IsSupportedComputedPropertyKeySpan(
                   program,
                   jumpIndex + 1,
                   computedIndex,
                   identifierConstants,
                   activationSlots);
    }

    private static bool TryIsEmbeddedOptionalReadOperandOperation(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (!HasOwningControlExpression(program))
        {
            return false;
        }

        return TryIsEmbeddedOptionalNamedReadSpanOperation(program, operationIndex, identifierConstants, activationSlots) ||
               TryIsEmbeddedOptionalComputedReadSpanOperation(program, operationIndex, identifierConstants, activationSlots);
    }

    private static bool HasOwningControlExpression(ExpressionProgram program)
    {
        for (var index = 0; index < program.OperationCount; index++)
        {
            switch (program.GetOperation(index).Kind)
            {
                case ExpressionOpKind.JumpIfFalse:
                case ExpressionOpKind.JumpIfTrue:
                case ExpressionOpKind.JumpIfNotNullish:
                case ExpressionOpKind.JumpIfConditionalFalse:
                    return true;
            }
        }

        return false;
    }

    private static bool TryIsEmbeddedOptionalNamedReadSpanOperation(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (operationIndex < 0 || operationIndex >= program.OperationCount)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        for (var spanStart = 0; spanStart + 1 < program.OperationCount; spanStart++)
        {
            var spanEnd = spanStart + 1;
            var namedRead = program.GetOperation(spanEnd);
            if (namedRead.Kind != ExpressionOpKind.GetNamedProperty ||
                !namedRead.IsOptional ||
                namedRead.ShortCircuitOnNullishTarget ||
                namedRead.GetString(stringConstants).IsPrivateName())
            {
                continue;
            }

            if (!TryGetActivationResolvedValue(program.GetOperation(spanStart), identifierConstants, activationSlots))
            {
                continue;
            }

            if (operationIndex >= spanStart && operationIndex <= spanEnd)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryIsEmbeddedOptionalComputedReadSpanOperation(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (operationIndex < 0 || operationIndex >= program.OperationCount)
        {
            return false;
        }

        for (var spanStart = 0; spanStart + 3 < program.OperationCount; spanStart++)
        {
            var jumpOp = program.GetOperation(spanStart + 1);
            if (jumpOp.Kind != ExpressionOpKind.JumpIfNullish || !jumpOp.ReplaceWithUndefined)
            {
                continue;
            }

            if (!TryGetActivationResolvedValue(program.GetOperation(spanStart), identifierConstants, activationSlots) ||
                !IsSimpleComputedPropertyKey(program.GetOperation(spanStart + 2), identifierConstants, activationSlots))
            {
                continue;
            }

            var shortShapeComputedRead = program.GetOperation(spanStart + 3);
            if (shortShapeComputedRead.Kind == ExpressionOpKind.GetComputedProperty &&
                !shortShapeComputedRead.ShortCircuitOnNullishTarget &&
                jumpOp.Target == spanStart + 4)
            {
                if (operationIndex >= spanStart && operationIndex < spanStart + 4)
                {
                    return true;
                }
            }

            if (spanStart + 5 >= program.OperationCount)
            {
                continue;
            }

            var requireObjectCoercible = program.GetOperation(spanStart + 3);
            if (requireObjectCoercible.Kind != ExpressionOpKind.RequireObjectCoercible || requireObjectCoercible.Depth != 1)
            {
                continue;
            }

            if (program.GetOperation(spanStart + 4).Kind != ExpressionOpKind.ResolvePropertyKey)
            {
                continue;
            }

            var computedRead = program.GetOperation(spanStart + 5);
            if (computedRead.Kind != ExpressionOpKind.GetComputedProperty || computedRead.ShortCircuitOnNullishTarget)
            {
                continue;
            }

            if (jumpOp.Target != spanStart + 6)
            {
                continue;
            }

            if (operationIndex >= spanStart && operationIndex < spanStart + 6)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryIsFirstBoundaryCallTargetPreparationCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 2)
        {
            return false;
        }

        // Optional-call shapes (fn?.(), box?.read(), box.read?.(), box[key]?.()) carry a
        // JumpIfNullish short-circuit and, for callee-optional cases, a trailing
        // Jump/SwapTopTwo/Pop structure that the non-optional branches below would
        // reject (or never reach, because they end in Pop rather than Call). Detect
        // them first so the dedicated optional candidates own these shapes.
        if (TryIsFirstBoundaryCalleeOptionalIdentifierCallCandidate(
                program,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers) ||
            TryIsFirstBoundaryReceiverOptionalNamedCallCandidate(program, identifierConstants, stringConstants, activationSlots) ||
            TryIsFirstBoundaryCalleeOptionalNamedCallCandidate(program, identifierConstants, stringConstants, activationSlots) ||
            TryIsFirstBoundaryCalleeOptionalComputedCallCandidate(program, identifierConstants, activationSlots) ||
            TryIsFirstBoundaryOptionalChainPlainCallCandidate(program, identifierConstants, stringConstants, activationSlots) ||
            TryIsFirstBoundaryOptionalChainReceiverOptionalCallCandidate(program, identifierConstants, stringConstants, activationSlots) ||
            TryIsFirstBoundaryOptionalChainComputedPlainCallCandidate(program, identifierConstants, stringConstants, activationSlots))
        {
            return true;
        }

        var callIndex = program.OperationCount - 1;
        var call = program.GetOperation(callIndex);
        // Synchronous spread calls are admitted (gh2676); spread args are flattened at
        // the invocation boundary. Direct eval is admitted only for the one-argument
        // non-spread eval identifier shape so the VM can thread caller eval state explicitly.
        if (call.Kind != ExpressionOpKind.Call ||
            (!call.HasExplicitThis && !call.IsDirectEval))
        {
            return false;
        }

        if (call.IsDirectEval)
        {
            return IsFirstBoundaryDirectEvalCallCandidate(
                program,
                identifierConstants,
                call);
        }

        var firstOperation = program.GetOperation(0);
        if (firstOperation.Kind == ExpressionOpKind.LoadIdentifierCallTarget)
        {
            var firstIdentifier = firstOperation.GetIdentifier(identifierConstants);
            var hasActivationCallTargetSlot = TryResolveActivationSlot(firstIdentifier, activationSlots);
            return (hasActivationCallTargetSlot ||
                    allowsDynamicIdentifiers) &&
                   HasSimpleCallArguments(
                       program,
                       identifierConstants,
                       activationSlots,
                       argsStartIndex: 1,
                       call,
                       allowsDynamicIdentifiers);
        }

        if (firstOperation.Kind == ExpressionOpKind.LoadNamedSuperCallTarget)
        {
            return !firstOperation.GetString(stringConstants).IsPrivateName() &&
                   HasSimpleCallArguments(program, identifierConstants, activationSlots, argsStartIndex: 1, call);
        }

        var computedSuperCallTargetIndex = FindFirstOperation(program, ExpressionOpKind.LoadComputedSuperCallTarget);
        if (computedSuperCallTargetIndex > 0)
        {
            var keyEnd = computedSuperCallTargetIndex;
            if (program.GetOperation(keyEnd - 1).Kind == ExpressionOpKind.EnsureSuperReference)
            {
                keyEnd--;
            }

            var hasResolvedKey = keyEnd == 2 &&
                                 program.GetOperation(1).Kind == ExpressionOpKind.ResolvePropertyKey;
            if (keyEnd is not 1 && !hasResolvedKey)
            {
                return false;
            }

            return IsSimpleComputedPropertyKey(
                       program.GetOperation(0),
                       identifierConstants,
                       activationSlots) &&
                   HasSimpleCallArguments(
                       program,
                       identifierConstants,
                       activationSlots,
                       computedSuperCallTargetIndex + 1,
                       call);
        }

        var namedCallTargetIndex = FindFirstOperation(program, ExpressionOpKind.LoadNamedCallTarget);
        if (namedCallTargetIndex > 0)
        {
            var namedCallTarget = program.GetOperation(namedCallTargetIndex);
            return !namedCallTarget.IsOptional &&
                   !namedCallTarget.ShortCircuitOnNullishTarget &&
                   IsSupportedNamedReceiverChain(
                       program,
                       identifierConstants,
                       stringConstants,
                       activationSlots,
                       namedCallTargetIndex,
                       allowDeepChain: true) &&
                   HasSimpleCallArguments(
                       program,
                       identifierConstants,
                       activationSlots,
                       namedCallTargetIndex + 1,
                       call);
        }

        var computedCallTargetIndex = FindFirstOperation(program, ExpressionOpKind.LoadComputedCallTarget);
        if (computedCallTargetIndex >= 2)
        {
            var computedCallTarget = program.GetOperation(computedCallTargetIndex);
            var keyStartIndex = FindComputedCallKeyStart(program, computedCallTargetIndex, stringConstants);
            return !computedCallTarget.IsOptional &&
                   !computedCallTarget.ShortCircuitOnNullishTarget &&
                   IsSupportedNamedReceiverChain(
                       program,
                       identifierConstants,
                       stringConstants,
                       activationSlots,
                       keyStartIndex,
                       allowDeepChain: true) &&
                   IsSupportedComputedPropertyKeySpan(
                       program,
                       startInclusive: keyStartIndex,
                       endExclusive: computedCallTargetIndex,
                       identifierConstants,
                       activationSlots) &&
                   HasSimpleCallArguments(
                       program,
                       identifierConstants,
                       activationSlots,
                       computedCallTargetIndex + 1,
                       call);
        }

        return false;
    }

    private static int FindComputedCallKeyStart(
        ExpressionProgram program,
        int computedCallTargetIndex,
        ReadOnlySpan<string> stringConstants)
    {
        var keyStartIndex = 1;
        while (keyStartIndex < computedCallTargetIndex &&
               IsPlainNamedPropertyRead(program.GetOperation(keyStartIndex), stringConstants))
        {
            keyStartIndex++;
        }

        return keyStartIndex;
    }

    private static bool IsFirstBoundaryDirectEvalCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        PackedExpressionOp call)
    {
        if (call.ArgumentCount != 1 || call.SpreadMaskConstantIndex >= 0)
        {
            return false;
        }

        var callTarget = program.GetOperation(0);
        if (callTarget.Kind != ExpressionOpKind.LoadIdentifierCallTarget || callTarget.IsArguments)
        {
            return false;
        }

        var identifier = callTarget.GetIdentifier(identifierConstants);
        return string.Equals(identifier.Name.Name, "eval", StringComparison.Ordinal) &&
               IsDirectEvalSingleArgumentCandidate(program.GetOperation(1));
    }

    private static bool IsDirectEvalSingleArgumentCandidate(PackedExpressionOp operation) =>
        operation.Kind is ExpressionOpKind.LoadIdentifier or ExpressionOpKind.LoadLiteral;

    // Case 0: fn?.(args) — callee-optional identifier call
    // Expression program: [LoadIdentifierCallTarget, JumpIfNullish, args..., Call, Jump, SwapTopTwo, Pop]
    private static bool TryIsFirstBoundaryCalleeOptionalIdentifierCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 6)
        {
            return false;
        }

        if (!IsCalleeOptionalTrailingStructure(program, out var callIndex))
        {
            return false;
        }

        var call = program.GetOperation(callIndex);
        if (!call.HasExplicitThis || call.IsDirectEval)
        {
            return false;
        }

        var callTarget = program.GetOperation(0);
        if (callTarget.Kind != ExpressionOpKind.LoadIdentifierCallTarget || callTarget.IsArguments)
        {
            return false;
        }

        var jumpOp = program.GetOperation(1);
        if (jumpOp.Kind != ExpressionOpKind.JumpIfNullish || !jumpOp.ReplaceWithUndefined)
        {
            return false;
        }

        var identifier = callTarget.GetIdentifier(identifierConstants);
        return (TryResolveActivationSlot(identifier, activationSlots) ||
                allowsDynamicIdentifiers && identifier.FlatSlotId < 0) &&
               HasSimpleCallArguments(
                   program,
                   identifierConstants,
                   activationSlots,
                   argsStartIndex: 2,
                   call,
                   callIndex,
                   allowsDynamicIdentifiers);
    }

    // Case 1: box?.read(args) — receiver-optional named call
    // Expression program: [Receiver..., JumpIfNullish, LoadNamedCallTarget, args..., Call]
    private static bool TryIsFirstBoundaryReceiverOptionalNamedCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots)
    {
        var callIndex = program.OperationCount - 1;
        var call = program.GetOperation(callIndex);
        if (call.Kind != ExpressionOpKind.Call || !call.HasExplicitThis || call.IsDirectEval)
        {
            return false;
        }

        var namedCallTargetIndex = FindFirstOperation(program, ExpressionOpKind.LoadNamedCallTarget);
        if (namedCallTargetIndex < 2)
        {
            return false;
        }

        var jumpOp = program.GetOperation(namedCallTargetIndex - 1);
        if (jumpOp.Kind != ExpressionOpKind.JumpIfNullish || !jumpOp.ReplaceWithUndefined)
        {
            return false;
        }

        var namedCallTarget = program.GetOperation(namedCallTargetIndex);
        if (namedCallTarget.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        return IsSupportedNamedReceiverChain(
                   program,
                   identifierConstants,
                   stringConstants,
                   activationSlots,
                   namedCallTargetIndex - 1,
                   allowDeepChain: true) &&
               HasSimpleCallArguments(
                   program,
                   identifierConstants,
                   activationSlots,
                   namedCallTargetIndex + 1,
                   call,
                   callIndex);
    }

    // Case 2: box.read?.() — callee-optional named call
    // Expression program: [Receiver..., LoadNamedCallTarget, JumpIfNullish, args..., Call, Jump, SwapTopTwo, Pop]
    private static bool TryIsFirstBoundaryCalleeOptionalNamedCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 7)
        {
            return false;
        }

        if (!IsCalleeOptionalTrailingStructure(program, out var callIndex))
        {
            return false;
        }

        var call = program.GetOperation(callIndex);
        if (!call.HasExplicitThis || call.IsDirectEval)
        {
            return false;
        }

        var namedCallTargetIndex = FindFirstOperation(program, ExpressionOpKind.LoadNamedCallTarget);
        if (namedCallTargetIndex < 1 || namedCallTargetIndex >= callIndex - 1)
        {
            return false;
        }

        var jumpOp = program.GetOperation(namedCallTargetIndex + 1);
        if (jumpOp.Kind != ExpressionOpKind.JumpIfNullish || !jumpOp.ReplaceWithUndefined)
        {
            return false;
        }

        var namedCallTarget = program.GetOperation(namedCallTargetIndex);
        if (namedCallTarget.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        return IsSupportedNamedReceiverChain(
                   program,
                   identifierConstants,
                   stringConstants,
                   activationSlots,
                   namedCallTargetIndex,
                   allowDeepChain: true) &&
               HasSimpleCallArguments(
                   program,
                   identifierConstants,
                   activationSlots,
                   namedCallTargetIndex + 2,
                   call,
                   callIndex);
    }

    // Case 3: box[key]?.() — callee-optional computed call
    // Expression program: [Receiver, Key, LoadComputedCallTarget, JumpIfNullish, args..., Call, Jump, SwapTopTwo, Pop]
    private static bool TryIsFirstBoundaryCalleeOptionalComputedCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 8)
        {
            return false;
        }

        if (!IsCalleeOptionalTrailingStructure(program, out var callIndex))
        {
            return false;
        }

        var call = program.GetOperation(callIndex);
        if (!call.HasExplicitThis || call.IsDirectEval)
        {
            return false;
        }

        var computedCallTargetIndex = FindFirstOperation(program, ExpressionOpKind.LoadComputedCallTarget);
        if (computedCallTargetIndex < 2 || computedCallTargetIndex >= callIndex - 1)
        {
            return false;
        }

        var jumpOp = program.GetOperation(computedCallTargetIndex + 1);
        if (jumpOp.Kind != ExpressionOpKind.JumpIfNullish || !jumpOp.ReplaceWithUndefined)
        {
            return false;
        }

        var keyIndex = computedCallTargetIndex - 1;
        var stringConstants = program.StringConstants.AsSpan();
        return IsSupportedNamedReceiverChain(
                   program,
                   identifierConstants,
                   stringConstants,
                   activationSlots,
                   keyIndex,
                   allowDeepChain: false) &&
               IsSimpleComputedPropertyKey(
                   program.GetOperation(keyIndex),
                   identifierConstants,
                   activationSlots) &&
               HasSimpleCallArguments(
                   program,
                   identifierConstants,
                   activationSlots,
                   computedCallTargetIndex + 2,
                   call,
                   callIndex);
    }

    // Case 4: a?.b.c() / a.x?.b.c() — optional-start chain, plain non-optional call
    // Expression program: [base/prefix..., GetNamedProperty(IsOptional:true,b), JumpIfShortCircuited,
    //                       LoadNamedCallTarget(c), args..., Call]
    private static bool TryIsFirstBoundaryOptionalChainPlainCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots)
    {
        // Minimum: [base, GetNamedProperty, JumpIfShortCircuited, LoadNamedCallTarget, Call] = 5
        if (program.OperationCount < 5)
        {
            return false;
        }

        var callIndex = program.OperationCount - 1;
        var call = program.GetOperation(callIndex);
        if (call.Kind != ExpressionOpKind.Call || !call.HasExplicitThis || call.IsDirectEval)
        {
            return false;
        }

        var namedCallTargetIndex = FindFirstOperation(program, ExpressionOpKind.LoadNamedCallTarget);
        if (namedCallTargetIndex < 3)
        {
            return false;
        }

        var shortCircuitIndex = namedCallTargetIndex - 1;
        if (program.GetOperation(shortCircuitIndex).Kind != ExpressionOpKind.JumpIfShortCircuited)
        {
            return false;
        }

        var optionalHopIndex = shortCircuitIndex - 1;
        if (optionalHopIndex < 1)
        {
            return false;
        }

        var optionalHop = program.GetOperation(optionalHopIndex);
        if (optionalHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !optionalHop.IsOptional ||
            optionalHop.ShortCircuitOnNullishTarget ||
            optionalHop.GetString(program.StringConstants.AsSpan()).IsPrivateName())
        {
            return false;
        }

        if (!IsSupportedNamedReceiverChain(
                program,
                identifierConstants,
                stringConstants,
                activationSlots,
                optionalHopIndex,
                allowDeepChain: true))
        {
            return false;
        }

        var namedCallTarget = program.GetOperation(namedCallTargetIndex);
        if (namedCallTarget.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        return HasSimpleCallArguments(program, identifierConstants, activationSlots, namedCallTargetIndex + 1, call);
    }

    // Case 5: a?.b?.c() — double-optional chain, receiver-optional call
    // Expression program: [base(0), GetNamedProperty(IsOptional:true,b)(1), JumpIfShortCircuited(2),
    //                       JumpIfNullish(ReplaceWithUndefined:true)(3), LoadNamedCallTarget(c)(4), args..., Call]
    private static bool TryIsFirstBoundaryOptionalChainReceiverOptionalCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots)
    {
        // Minimum: [base, GetNamedProperty, JumpIfShortCircuited, JumpIfNullish, LoadNamedCallTarget, Call] = 6
        if (program.OperationCount < 6)
        {
            return false;
        }

        var callIndex = program.OperationCount - 1;
        var call = program.GetOperation(callIndex);
        if (call.Kind != ExpressionOpKind.Call || !call.HasExplicitThis || call.IsDirectEval)
        {
            return false;
        }

        var namedCallTargetIndex = FindFirstOperation(program, ExpressionOpKind.LoadNamedCallTarget);
        // LoadNamedCallTarget must be at index 4 exactly.
        if (namedCallTargetIndex != 4)
        {
            return false;
        }

        // op[3] = JumpIfNullish(ReplaceWithUndefined:true)
        var jumpNullish = program.GetOperation(3);
        if (jumpNullish.Kind != ExpressionOpKind.JumpIfNullish || !jumpNullish.ReplaceWithUndefined)
        {
            return false;
        }

        // op[2] = JumpIfShortCircuited
        if (program.GetOperation(2).Kind != ExpressionOpKind.JumpIfShortCircuited)
        {
            return false;
        }

        // op[1] = GetNamedProperty(IsOptional:true, !SC, non-private)
        var firstHop = program.GetOperation(1);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(program.StringConstants.AsSpan()).IsPrivateName())
        {
            return false;
        }

        // op[0] = activation-resolved base
        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        // LoadNamedCallTarget must not be private
        var namedCallTarget = program.GetOperation(namedCallTargetIndex);
        if (namedCallTarget.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        return HasSimpleCallArguments(program, identifierConstants, activationSlots, namedCallTargetIndex + 1, call);
    }

    // Case 6: a?.b[k]() — optional-start chain, computed plain non-optional call
    // Expression program: [base(0), GetNamedProperty(IsOptional:true,b)(1), JumpIfShortCircuited(2),
    //                       key(3), LoadComputedCallTarget(4), args..., Call]
    private static bool TryIsFirstBoundaryOptionalChainComputedPlainCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots)
    {
        // Minimum: [base, GetNamedProperty, JumpIfShortCircuited, key, LoadComputedCallTarget, Call] = 6
        if (program.OperationCount < 6)
        {
            return false;
        }

        var callIndex = program.OperationCount - 1;
        var call = program.GetOperation(callIndex);
        if (call.Kind != ExpressionOpKind.Call || !call.HasExplicitThis || call.IsDirectEval)
        {
            return false;
        }

        var computedCallTargetIndex = FindFirstOperation(program, ExpressionOpKind.LoadComputedCallTarget);
        if (computedCallTargetIndex != 4)
        {
            return false;
        }

        if (program.GetOperation(2).Kind != ExpressionOpKind.JumpIfShortCircuited)
        {
            return false;
        }

        var firstHop = program.GetOperation(1);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        if (!IsSimpleComputedPropertyKey(program.GetOperation(3), identifierConstants, activationSlots))
        {
            return false;
        }

        var computedCallTarget = program.GetOperation(computedCallTargetIndex);
        return !computedCallTarget.IsOptional &&
               !computedCallTarget.ShortCircuitOnNullishTarget &&
               HasSimpleCallArguments(program, identifierConstants, activationSlots, computedCallTargetIndex + 1, call);
    }

    // Returns true and the index of the Call op when the expression program ends with
    // the callee-optional trailing structure: ..., Call, Jump, SwapTopTwo, Pop
    private static bool IsCalleeOptionalTrailingStructure(ExpressionProgram program, out int callIndex)
    {
        callIndex = program.OperationCount - 4;
        return callIndex >= 0 &&
               program.GetOperation(program.OperationCount - 1).Kind == ExpressionOpKind.Pop &&
               program.GetOperation(program.OperationCount - 2).Kind == ExpressionOpKind.SwapTopTwo &&
               program.GetOperation(program.OperationCount - 3).Kind == ExpressionOpKind.Jump &&
               program.GetOperation(callIndex).Kind == ExpressionOpKind.Call;
    }

    private static int FindFirstOperation(ExpressionProgram program, ExpressionOpKind kind)
    {
        for (var operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
        {
            if (program.GetOperation(operationIndex).Kind == kind)
            {
                return operationIndex;
            }
        }

        return -1;
    }

    private static bool IsSupportedNamedReceiverChain(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots,
        int endExclusive,
        bool allowDeepChain)
    {
        if (endExclusive < 1 || (!allowDeepChain && endExclusive > 3))
        {
            return false;
        }

        var firstOperation = program.GetOperation(0);
        if (!TryGetActivationResolvedValue(firstOperation, identifierConstants, activationSlots))
        {
            return false;
        }

        for (var operationIndex = 1; operationIndex < endExclusive; operationIndex++)
        {
            var receiverOperation = program.GetOperation(operationIndex);
            if (receiverOperation.Kind != ExpressionOpKind.GetNamedProperty ||
                receiverOperation.IsOptional ||
                receiverOperation.ShortCircuitOnNullishTarget ||
                receiverOperation.GetString(stringConstants).IsPrivateName())
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSimpleComputedPropertyKey(
        PackedExpressionOp operation,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers = false)
    {
        return operation.Kind switch
        {
            ExpressionOpKind.LoadLiteral => true,
            ExpressionOpKind.LoadThis => true,
            ExpressionOpKind.LoadNewTarget => true,
            ExpressionOpKind.LoadIdentifier => TryGetActivationResolvedValue(
                operation,
                identifierConstants,
                activationSlots) ||
                allowsDynamicIdentifiers &&
                !operation.IsArguments,
            _ => false
        };
    }

    private static bool IsSupportedComputedPropertyKeySpan(
        ExpressionProgram program,
        int startInclusive,
        int endExclusive,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers = false)
    {
        var stackDepth = 0;
        for (var index = startInclusive; index < endExclusive; index++)
        {
            var operation = program.GetOperation(index);
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadLiteral:
                case ExpressionOpKind.LoadIdentifier:
                case ExpressionOpKind.LoadThis:
                case ExpressionOpKind.LoadNewTarget:
                    if (!IsSimpleComputedPropertyKey(
                            operation,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        return false;
                    }

                    stackDepth++;
                    break;

                case ExpressionOpKind.CreateObject:
                    if (!TryMeasureSimpleObjectLiteralSpan(
                            program,
                            index,
                            identifierConstants,
                            activationSlots,
                            out var objectSpanLength))
                    {
                        return false;
                    }

                    index += objectSpanLength - 1;
                    stackDepth++;
                    break;

                case ExpressionOpKind.UnaryPlus:
                case ExpressionOpKind.UnaryMinus:
                case ExpressionOpKind.UnaryLogicalNot:
                case ExpressionOpKind.UnaryBitwiseNot:
                case ExpressionOpKind.UnaryVoid:
                case ExpressionOpKind.ToString:
                    if (stackDepth < 1)
                    {
                        return false;
                    }

                    break;

                case ExpressionOpKind.Binary:
                    if (stackDepth < 2 || !IsProductionBinaryOperator(operation.Operator))
                    {
                        return false;
                    }

                    stackDepth--;
                    break;

                default:
                    return false;
            }
        }

        return stackDepth == 1;
    }

    private static bool IsPlainNamedPropertyRead(
        PackedExpressionOp operation,
        ReadOnlySpan<string> stringConstants)
    {
        return operation.Kind == ExpressionOpKind.GetNamedProperty &&
               !operation.IsOptional &&
               !operation.ShortCircuitOnNullishTarget &&
               !operation.GetString(stringConstants).IsPrivateName();
    }

    private static bool IsShortCircuitNamedPropertyRead(
        PackedExpressionOp operation,
        ReadOnlySpan<string> stringConstants)
    {
        return operation.Kind == ExpressionOpKind.GetNamedProperty &&
               operation.ShortCircuitOnNullishTarget &&
               !operation.GetString(stringConstants).IsPrivateName();
    }

    private static bool HasSimpleCallArguments(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        int argsStartIndex,
        PackedExpressionOp call,
        bool allowsDynamicIdentifiers = false)
    {
        return HasSimpleCallArguments(
            program,
            identifierConstants,
            activationSlots,
            argsStartIndex,
            call,
            program.OperationCount - 1,
            allowsDynamicIdentifiers);
    }

    private static bool HasSimpleCallArguments(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        int argsStartIndex,
        PackedExpressionOp call,
        int callIndex,
        bool allowsDynamicIdentifiers = false)
    {
        // Span-walk: each logical argument is either a single simple operand or a
        // multi-op array/object/template-literal span. Count logical arguments and
        // verify the total matches call.ArgumentCount.
        var argCount = 0;
        var operationIndex = argsStartIndex;
        while (operationIndex < callIndex)
        {
            var op = program.GetOperation(operationIndex);
            if (op.Kind == ExpressionOpKind.CreateArray)
            {
                if (!TryMeasureSimpleArrayLiteralSpan(
                        program,
                        operationIndex,
                        identifierConstants,
                        activationSlots,
                        out var spanLen,
                        allowsDynamicIdentifiers))
                {
                    return false;
                }

                operationIndex += spanLen;
            }
            else if (op.Kind == ExpressionOpKind.CreateObject)
            {
                if (!TryMeasureSimpleObjectLiteralSpan(
                        program,
                        operationIndex,
                        identifierConstants,
                        activationSlots,
                        out var spanLen,
                        allowsDynamicIdentifiers))
                {
                    return false;
                }

                operationIndex += spanLen;
            }
            else if (op.Kind == ExpressionOpKind.LoadLiteral)
            {
                // A LoadLiteral may be the seed of a multi-op template literal span
                // (`hello ${x}` → LoadLiteral(""), text parts, substitution parts).
                // Use spanLen > 1 to distinguish a real template span from a standalone literal.
                if (TryMeasureSimpleTemplateLiteralSpan(
                        program,
                        operationIndex,
                        identifierConstants,
                        activationSlots,
                        out var spanLen,
                        allowsDynamicIdentifiers) &&
                    spanLen > 1)
                {
                    operationIndex += spanLen;
                }
                else
                {
                    // Standalone literal — same as IsSimpleOperand.
                    operationIndex++;
                }
            }
            else if (TryMeasureSimpleBinaryOperandSpan(
                         program,
                         operationIndex,
                         identifierConstants,
                         activationSlots,
                         allowsDynamicIdentifiers,
                         callIndex,
                         out var binarySpanLen))
            {
                operationIndex += binarySpanLen;
            }
            else if (IsSimpleOperand(op, identifierConstants, activationSlots, allowsDynamicIdentifiers))
            {
                operationIndex++;
            }
            else
            {
                return false;
            }

            argCount++;
        }

        return argCount == call.ArgumentCount;
    }

    // Measures the op span for a simple array literal starting at startIndex.
    // Admitted shapes (CreateArray followed by N ≥ 0 elements, each one of):
    //   Normal:  [simple-operand, ArrayPush]
    //   Spread:  [simple-operand, ArraySpread]
    //   Hole:    ArrayPushHole (standalone)
    // Non-simple operands and any other ops terminate the element scan (end of literal).
    private static bool TryMeasureSimpleArrayLiteralSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        if (program.GetOperation(startIndex).Kind != ExpressionOpKind.CreateArray)
        {
            spanLength = 0;
            return false;
        }

        var i = startIndex + 1;
        while (i < program.OperationCount)
        {
            var elementOp = program.GetOperation(i);

            if (elementOp.Kind == ExpressionOpKind.ArrayPushHole)
            {
                i++;
                continue;
            }

            if (TryMeasureSimpleDirectNamedCallOperandSpan(
                    program,
                    i,
                    identifierConstants,
                    activationSlots,
                    out var callSpanLength))
            {
                i += callSpanLength;
            }
            else if (IsSimpleOperand(elementOp, identifierConstants, activationSlots, allowsDynamicIdentifiers))
            {
                i++;
            }
            else
            {
                // Non-simple op terminates the element scan — the array literal ends here.
                break;
            }

            if (i >= program.OperationCount)
            {
                spanLength = 0;
                return false;
            }

            var pushOp = program.GetOperation(i);
            if (pushOp.Kind is not (ExpressionOpKind.ArrayPush or ExpressionOpKind.ArraySpread))
            {
                spanLength = 0;
                return false;
            }

            i++;
        }

        spanLength = i - startIndex;
        return true;
    }

    // Measures the op span for a simple object literal starting at startIndex.
    // Admitted shapes (CreateObject followed by N >= 0 property triples/spreads):
    //   Static:   [simple-value-operand, DefineObjectProperty(non-private, no name inference)]
    //   Computed: [simple-key-operand or simple-binary-key-expression, ResolvePropertyKey,
    //              simple-value-operand, DefineComputedObjectProperty(no name inference)]
    //   Spread:   [simple-spread-source-operand, ObjectSpread]
    // DefineObjectMethod, accessors, private names, name inference, and complex key expressions are declined.
    private static bool TryMeasureSimpleObjectLiteralSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        if (program.GetOperation(startIndex).Kind != ExpressionOpKind.CreateObject)
        {
            spanLength = 0;
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var i = startIndex + 1;
        while (i < program.OperationCount)
        {
            if (TryMeasureSimpleIdentifierCallOperandSpan(
                    program,
                    i,
                    identifierConstants,
                    activationSlots,
                    out var callKeySpanLength) &&
                i + callKeySpanLength < program.OperationCount &&
                program.GetOperation(i + callKeySpanLength).Kind == ExpressionOpKind.ResolvePropertyKey)
            {
                i += callKeySpanLength + 1;
                if (i >= program.OperationCount)
                {
                    spanLength = 0;
                    return false;
                }

                var valueOp = program.GetOperation(i);
                if (!IsSimpleOperand(valueOp, identifierConstants, activationSlots))
                {
                    spanLength = 0;
                    return false;
                }

                i++;
                if (i >= program.OperationCount)
                {
                    spanLength = 0;
                    return false;
                }

                var computedDefineOp = program.GetOperation(i);
                if (computedDefineOp.Kind != ExpressionOpKind.DefineComputedObjectProperty ||
                    computedDefineOp.AllowNameInference)
                {
                    spanLength = 0;
                    return false;
                }

                i++;
                continue;
            }

            if (TryMeasureSimpleBinaryOperandSpan(
                    program,
                    i,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    program.OperationCount,
                    out var keySpanLength) &&
                i + keySpanLength < program.OperationCount &&
                program.GetOperation(i + keySpanLength).Kind == ExpressionOpKind.ResolvePropertyKey)
            {
                i += keySpanLength + 1;
                if (i >= program.OperationCount)
                {
                    spanLength = 0;
                    return false;
                }

                var valueOp = program.GetOperation(i);
                if (!IsSimpleOperand(valueOp, identifierConstants, activationSlots, allowsDynamicIdentifiers))
                {
                    spanLength = 0;
                    return false;
                }

                i++;
                if (i >= program.OperationCount)
                {
                    spanLength = 0;
                    return false;
                }

                var computedDefineOp = program.GetOperation(i);
                if (computedDefineOp.Kind != ExpressionOpKind.DefineComputedObjectProperty ||
                    computedDefineOp.AllowNameInference)
                {
                    spanLength = 0;
                    return false;
                }

                i++;
                continue;
            }

            var firstOp = program.GetOperation(i);
            if (TryMeasureSimpleDirectNamedCallOperandSpan(
                    program,
                    i,
                    identifierConstants,
                    activationSlots,
                    out var spreadCallSpanLength) &&
                i + spreadCallSpanLength < program.OperationCount &&
                program.GetOperation(i + spreadCallSpanLength).Kind == ExpressionOpKind.ObjectSpread)
            {
                i += spreadCallSpanLength + 1;
                continue;
            }

            if (!IsSimpleOperand(firstOp, identifierConstants, activationSlots, allowsDynamicIdentifiers))
            {
                // Non-simple first op terminates the property scan — the object literal ends here.
                break;
            }

            i++;
            if (i >= program.OperationCount)
            {
                spanLength = 0;
                return false;
            }

            var secondOp = program.GetOperation(i);
            if (secondOp.Kind == ExpressionOpKind.DefineObjectProperty)
            {
                // Static property: firstOp = value, secondOp = DefineObjectProperty.
                if (secondOp.GetString(stringConstants).IsPrivateName())
                {
                    spanLength = 0;
                    return false;
                }

                if (secondOp.AllowNameInference)
                {
                    spanLength = 0;
                    return false;
                }

                i++;
            }
            else if (secondOp.Kind == ExpressionOpKind.ResolvePropertyKey)
            {
                // Computed property: firstOp = key, secondOp = ResolvePropertyKey; expect value then DefineComputedObjectProperty.
                i++;
                if (i >= program.OperationCount)
                {
                    spanLength = 0;
                    return false;
                }

                var valueOp = program.GetOperation(i);
                if (!IsSimpleOperand(valueOp, identifierConstants, activationSlots, allowsDynamicIdentifiers))
                {
                    spanLength = 0;
                    return false;
                }

                i++;
                if (i >= program.OperationCount)
                {
                    spanLength = 0;
                    return false;
                }

                var computedDefineOp = program.GetOperation(i);
                if (computedDefineOp.Kind != ExpressionOpKind.DefineComputedObjectProperty ||
                    computedDefineOp.AllowNameInference)
                {
                    spanLength = 0;
                    return false;
                }

                i++;
            }
            else if (secondOp.Kind == ExpressionOpKind.ObjectSpread)
            {
                i++;
            }
            else
            {
                // Not a static or simple-computed property — decline.
                spanLength = 0;
                return false;
            }
        }

        spanLength = i - startIndex;
        return true;
    }

    private static bool TryMeasureSimpleBinaryOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        return TryMeasureSimpleBinaryOperandSpan(
            program,
            startIndex,
            identifierConstants,
            activationSlots,
            allowsDynamicIdentifiers,
            program.OperationCount,
            out spanLength);
    }

    private static bool TryMeasureSimpleBinaryOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        int endExclusive,
        out int spanLength)
    {
        if (startIndex + 2 >= endExclusive)
        {
            spanLength = 0;
            return false;
        }

        var left = program.GetOperation(startIndex);
        var right = program.GetOperation(startIndex + 1);
        var binary = program.GetOperation(startIndex + 2);
        if (IsSimpleOperand(left, identifierConstants, activationSlots, allowsDynamicIdentifiers) &&
            IsSimpleOperand(right, identifierConstants, activationSlots, allowsDynamicIdentifiers) &&
            binary.Kind == ExpressionOpKind.Binary &&
            IsProductionBinaryOperator(binary.Operator))
        {
            spanLength = 3;
            return true;
        }

        spanLength = 0;
        return false;
    }

    private static bool TryMeasureSimpleIdentifierCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength)
    {
        if (startIndex + 1 >= program.OperationCount)
        {
            spanLength = 0;
            return false;
        }

        var callTarget = program.GetOperation(startIndex);
        var call = program.GetOperation(startIndex + 1);
        if (callTarget.Kind != ExpressionOpKind.LoadIdentifierCallTarget ||
            callTarget.IsArguments ||
            call.Kind != ExpressionOpKind.Call ||
            !call.HasExplicitThis ||
            call.IsDirectEval ||
            call.ArgumentCount != 0 ||
            call.SpreadMaskConstantIndex >= 0)
        {
            spanLength = 0;
            return false;
        }

        var identifier = callTarget.GetIdentifier(identifierConstants);
        if (!TryResolveActivationSlot(identifier, activationSlots))
        {
            spanLength = 0;
            return false;
        }

        spanLength = 2;
        return true;
    }

    private static bool TryMeasureSimpleDirectNamedCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength)
    {
        spanLength = 0;
        if (startIndex + 2 >= program.OperationCount)
        {
            return false;
        }

        if (!IsSimpleOperand(program.GetOperation(startIndex), identifierConstants, activationSlots))
        {
            return false;
        }

        var callTarget = program.GetOperation(startIndex + 1);
        if (callTarget.Kind != ExpressionOpKind.LoadNamedCallTarget ||
            callTarget.IsOptional ||
            callTarget.ShortCircuitOnNullishTarget ||
            callTarget.GetString(program.StringConstants.AsSpan()).IsPrivateName())
        {
            return false;
        }

        var argCount = 0;
        var operationIndex = startIndex + 2;
        while (operationIndex < program.OperationCount &&
               IsSimpleOperand(program.GetOperation(operationIndex), identifierConstants, activationSlots))
        {
            argCount++;
            operationIndex++;
        }

        if (operationIndex >= program.OperationCount)
        {
            return false;
        }

        var call = program.GetOperation(operationIndex);
        if (call.Kind != ExpressionOpKind.Call ||
            !call.HasExplicitThis ||
            call.IsDirectEval ||
            call.SpreadMaskConstantIndex >= 0 ||
            call.ArgumentCount != argCount)
        {
            return false;
        }

        spanLength = operationIndex - startIndex + 1;
        return true;
    }

    private static bool IsOperationInSimpleArrayLiteralSpan(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        for (var startIndex = 0; startIndex <= operationIndex; startIndex++)
        {
            if (program.GetOperation(startIndex).Kind != ExpressionOpKind.CreateArray)
            {
                continue;
            }

            if (TryMeasureSimpleArrayLiteralSpan(
                    program,
                    startIndex,
                    identifierConstants,
                    activationSlots,
                    out var spanLength) &&
                operationIndex < startIndex + spanLength)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOperationInSimpleObjectLiteralSpan(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        for (var startIndex = 0; startIndex <= operationIndex; startIndex++)
        {
            if (program.GetOperation(startIndex).Kind != ExpressionOpKind.CreateObject)
            {
                continue;
            }

            if (TryMeasureSimpleObjectLiteralSpan(
                    program,
                    startIndex,
                    identifierConstants,
                    activationSlots,
                    out var spanLength) &&
                operationIndex < startIndex + spanLength)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOperationInComputedPropertyKeyPayload(
        ExpressionProgram program,
        int operationIndex)
    {
        return TryGetComputedPropertyKeyPayloadBounds(program, out var keyStart, out var keyEndExclusive) &&
               operationIndex >= keyStart &&
               operationIndex < keyEndExclusive;
    }

    // Measures the op span for a simple untagged template literal starting at startIndex.
    // Admitted shape: LoadLiteral (seed), then any number of:
    //   text part:         LoadLiteral(string), Binary(Add)
    //   substitution part: <simple-operand>, ToString, Binary(Add)
    // Returns spanLength=1 for a bare LoadLiteral with no matching continuation
    // (treat as standalone literal at the call site; check spanLen > 1 to detect a real template span).
    // Returns false only when startIndex does not point to LoadLiteral.
    private static bool TryMeasureSimpleTemplateLiteralSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        if (program.GetOperation(startIndex).Kind != ExpressionOpKind.LoadLiteral)
        {
            spanLength = 0;
            return false;
        }

        var i = startIndex + 1;
        while (i < program.OperationCount)
        {
            var op = program.GetOperation(i);

            // Text part: LoadLiteral followed by Binary(Add)
            if (op.Kind == ExpressionOpKind.LoadLiteral)
            {
                if (i + 1 < program.OperationCount)
                {
                    var next = program.GetOperation(i + 1);
                    if (next.Kind == ExpressionOpKind.Binary && next.Operator == BinaryOperator.Add)
                    {
                        i += 2;
                        continue;
                    }
                }

                // LoadLiteral not followed by Binary(Add) — template span ends here.
                break;
            }

            // Substitution part: simple binary expression, ToString, Binary(Add)
            if (i + 4 < program.OperationCount &&
                IsSimpleOperand(op, identifierConstants, activationSlots, allowsDynamicIdentifiers))
            {
                var rightOperand = program.GetOperation(i + 1);
                var binary = program.GetOperation(i + 2);
                var toString = program.GetOperation(i + 3);
                var add = program.GetOperation(i + 4);
                if (IsSimpleOperand(rightOperand, identifierConstants, activationSlots, allowsDynamicIdentifiers) &&
                    binary.Kind == ExpressionOpKind.Binary &&
                    IsProductionBinaryOperator(binary.Operator) &&
                    toString.Kind == ExpressionOpKind.ToString &&
                    add.Kind == ExpressionOpKind.Binary &&
                    add.Operator == BinaryOperator.Add)
                {
                    i += 5;
                    continue;
                }
            }

            // Substitution part: simple operand, ToString, Binary(Add)
            if (IsSimpleOperand(op, identifierConstants, activationSlots, allowsDynamicIdentifiers))
            {
                if (i + 2 < program.OperationCount)
                {
                    var toString = program.GetOperation(i + 1);
                    var add = program.GetOperation(i + 2);
                    if (toString.Kind == ExpressionOpKind.ToString &&
                        add.Kind == ExpressionOpKind.Binary && add.Operator == BinaryOperator.Add)
                    {
                        i += 3;
                        continue;
                    }
                }

                // Simple operand not followed by ToString, Binary(Add) — template span ends here.
                break;
            }

            // Non-matching op — template span ends here.
            break;
        }

        spanLength = i - startIndex;
        return true;
    }

    private static bool TryIsFirstBoundaryNamedCompoundPropertyWriteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        // Shape: [base, GetNamedProperty(non-optional, non-private)*, DuplicateTop, GetNamedProperty, rhs..., Binary, SetNamedProperty]
        // The final target may be private; receiver-chain hops stay ordinary only.
        // Minimum: 6 ops (rhs is a single simple operand).
        if (program.OperationCount < 6)
        {
            return false;
        }

        if (!TryGetActivationOrPlainDynamicIdentifierReadValue(
                program.GetOperation(0),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var duplicateIndex = 1;
        while (duplicateIndex < program.OperationCount)
        {
            var receiverRead = program.GetOperation(duplicateIndex);
            if (receiverRead.Kind != ExpressionOpKind.GetNamedProperty)
            {
                break;
            }

            if (receiverRead.GetString(stringConstants).IsPrivateName() ||
                receiverRead.IsOptional ||
                receiverRead.ShortCircuitOnNullishTarget)
            {
                return false;
            }

            duplicateIndex++;
        }

        if (duplicateIndex + 4 >= program.OperationCount)
        {
            return false;
        }

        var duplicateTarget = program.GetOperation(duplicateIndex);
        var propertyRead = program.GetOperation(duplicateIndex + 1);
        var binary = program.GetOperation(program.OperationCount - 2);
        var propertyWrite = program.GetOperation(program.OperationCount - 1);
        if (duplicateTarget.Kind != ExpressionOpKind.DuplicateTop ||
            propertyRead.Kind != ExpressionOpKind.GetNamedProperty ||
            propertyWrite.Kind != ExpressionOpKind.SetNamedProperty ||
            binary.Kind != ExpressionOpKind.Binary ||
            !IsProductionBinaryOperator(binary.Operator) ||
            propertyRead.IsOptional ||
            propertyRead.ShortCircuitOnNullishTarget ||
            propertyWrite.AllowNameInference)
        {
            return false;
        }

        var propertyName = propertyRead.GetString(stringConstants);
        if (propertyName != propertyWrite.GetString(stringConstants))
        {
            return false;
        }

        var rhsStart = duplicateIndex + 2;
        var rhsEnd = program.OperationCount - 3;

        if (rhsStart == rhsEnd)
        {
            return IsSimpleOperand(
                program.GetOperation(rhsStart),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers);
        }

        // Multi-op RHS — try template literal span.
        return TryMeasureSimpleTemplateLiteralSpan(
                   program, rhsStart, identifierConstants, activationSlots, out var spanLen) &&
               spanLen > 1 &&
               rhsStart + spanLen - 1 == rhsEnd;
    }

    private static bool TryIsFirstBoundaryComputedCompoundPropertyWriteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 9)
        {
            return false;
        }

        if (!TryGetActivationOrPlainDynamicIdentifierReadValue(
                program.GetOperation(0),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var suffixStart = program.OperationCount - 7;
        if (suffixStart <= 1 ||
            !IsSupportedComputedPropertyKeySpan(
                program,
                startInclusive: 1,
                endExclusive: suffixStart,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var requireObjectCoercible = program.GetOperation(suffixStart);
        var resolvePropertyKey = program.GetOperation(suffixStart + 1);
        var duplicateTargetAndKey = program.GetOperation(suffixStart + 2);
        var propertyRead = program.GetOperation(suffixStart + 3);
        var rhs = program.GetOperation(suffixStart + 4);
        var binary = program.GetOperation(suffixStart + 5);
        var propertyWrite = program.GetOperation(suffixStart + 6);
        return requireObjectCoercible.Kind == ExpressionOpKind.RequireObjectCoercible &&
               requireObjectCoercible.Depth == 1 &&
               resolvePropertyKey.Kind == ExpressionOpKind.ResolvePropertyKey &&
               duplicateTargetAndKey.Kind == ExpressionOpKind.DuplicateTopTwo &&
               propertyRead.Kind == ExpressionOpKind.GetComputedProperty &&
               !propertyRead.ShortCircuitOnNullishTarget &&
               IsSimpleOperand(rhs, identifierConstants, activationSlots, allowsDynamicIdentifiers) &&
               binary.Kind == ExpressionOpKind.Binary &&
               IsProductionBinaryOperator(binary.Operator) &&
               propertyWrite.Kind == ExpressionOpKind.SetComputedProperty &&
               !propertyWrite.AllowNameInference;
    }

    private static bool TryIsFirstBoundaryNamedLogicalPropertyWriteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        // Shape: [base, GetNamedProperty(non-optional, non-private)*, DuplicateTop, GetNamedProperty,
        // JumpIf*, Pop, rhs, SetNamedProperty, DuplicateTop, SwapTopTwo, Pop]
        // The final target may be private; receiver-chain hops stay ordinary only.
        if (program.OperationCount < 10)
        {
            return false;
        }

        if (!TryGetActivationOrPlainDynamicIdentifierReadValue(
                program.GetOperation(0),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var duplicateIndex = 1;
        while (duplicateIndex < program.OperationCount)
        {
            var receiverRead = program.GetOperation(duplicateIndex);
            if (receiverRead.Kind != ExpressionOpKind.GetNamedProperty)
            {
                break;
            }

            if (receiverRead.GetString(stringConstants).IsPrivateName() ||
                receiverRead.IsOptional ||
                receiverRead.ShortCircuitOnNullishTarget)
            {
                return false;
            }

            duplicateIndex++;
        }

        if (program.OperationCount != duplicateIndex + 9)
        {
            return false;
        }

        var duplicateTarget = program.GetOperation(duplicateIndex);
        var propertyRead = program.GetOperation(duplicateIndex + 1);
        var jump = program.GetOperation(duplicateIndex + 2);
        var pop = program.GetOperation(duplicateIndex + 3);
        var rhs = program.GetOperation(duplicateIndex + 4);
        var propertyWrite = program.GetOperation(duplicateIndex + 5);
        var duplicateAssignedValue = program.GetOperation(duplicateIndex + 6);
        var swap = program.GetOperation(duplicateIndex + 7);
        var cleanupPop = program.GetOperation(duplicateIndex + 8);
        if (duplicateTarget.Kind != ExpressionOpKind.DuplicateTop ||
            propertyRead.Kind != ExpressionOpKind.GetNamedProperty ||
            jump.Kind is not (ExpressionOpKind.JumpIfFalse or ExpressionOpKind.JumpIfTrue or ExpressionOpKind.JumpIfNotNullish) ||
            pop.Kind != ExpressionOpKind.Pop ||
            propertyWrite.Kind != ExpressionOpKind.SetNamedProperty ||
            duplicateAssignedValue.Kind != ExpressionOpKind.DuplicateTop ||
            swap.Kind != ExpressionOpKind.SwapTopTwo ||
            cleanupPop.Kind != ExpressionOpKind.Pop ||
            propertyWrite.AllowNameInference ||
            propertyRead.IsOptional ||
            propertyRead.ShortCircuitOnNullishTarget ||
            jump.Target != duplicateIndex + 7 ||
            !IsSimpleOperand(rhs, identifierConstants, activationSlots, allowsDynamicIdentifiers))
        {
            return false;
        }

        var propertyName = propertyRead.GetString(stringConstants);
        return propertyName == propertyWrite.GetString(stringConstants);
    }

    private static bool TryIsFirstBoundaryComputedLogicalPropertyWriteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        // Shape: [base, key..., RequireObjectCoercible, ResolvePropertyKey, DuplicateTopTwo, GetComputedProperty,
        // JumpIf*, Pop, rhs..., SetComputedProperty, DuplicateTop, DuplicateTop, RotateTopThreeRight, Pop, Pop]
        if (program.OperationCount < 15)
        {
            return false;
        }

        if (!TryGetActivationOrPlainDynamicIdentifierReadValue(
                program.GetOperation(0),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var propertyWriteIndex = program.OperationCount - 6;
        for (var rhsLength = 1; rhsLength <= 3; rhsLength += 2)
        {
            var suffixStart = propertyWriteIndex - 6 - rhsLength;
            if (suffixStart <= 1 ||
                !IsSupportedComputedPropertyKeySpan(
                    program,
                    startInclusive: 1,
                    endExclusive: suffixStart,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers))
            {
                continue;
            }

            var requireObjectCoercible = program.GetOperation(suffixStart);
            var resolvePropertyKey = program.GetOperation(suffixStart + 1);
            var duplicateTargetAndKey = program.GetOperation(suffixStart + 2);
            var propertyRead = program.GetOperation(suffixStart + 3);
            var jump = program.GetOperation(suffixStart + 4);
            var pop = program.GetOperation(suffixStart + 5);
            var propertyWrite = program.GetOperation(propertyWriteIndex);
            var duplicateAssignedValue = program.GetOperation(propertyWriteIndex + 1);
            var duplicateAssignedValueAgain = program.GetOperation(propertyWriteIndex + 2);
            var rotateTopThreeRight = program.GetOperation(propertyWriteIndex + 3);
            var cleanupPop = program.GetOperation(propertyWriteIndex + 4);
            var cleanupPop2 = program.GetOperation(propertyWriteIndex + 5);
            if (requireObjectCoercible.Kind == ExpressionOpKind.RequireObjectCoercible &&
                requireObjectCoercible.Depth == 1 &&
                resolvePropertyKey.Kind == ExpressionOpKind.ResolvePropertyKey &&
                duplicateTargetAndKey.Kind == ExpressionOpKind.DuplicateTopTwo &&
                propertyRead.Kind == ExpressionOpKind.GetComputedProperty &&
                !propertyRead.ShortCircuitOnNullishTarget &&
                jump.Kind is (ExpressionOpKind.JumpIfFalse or ExpressionOpKind.JumpIfTrue or ExpressionOpKind.JumpIfNotNullish) &&
                pop.Kind == ExpressionOpKind.Pop &&
                IsSupportedComputedLogicalAssignmentRhsSpan(
                    program,
                    suffixStart + 6,
                    propertyWriteIndex,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers) &&
                propertyWrite.Kind == ExpressionOpKind.SetComputedProperty &&
                !propertyWrite.AllowNameInference &&
                duplicateAssignedValue.Kind == ExpressionOpKind.DuplicateTop &&
                duplicateAssignedValueAgain.Kind == ExpressionOpKind.DuplicateTop &&
                rotateTopThreeRight.Kind == ExpressionOpKind.RotateTopThreeRight &&
                cleanupPop.Kind == ExpressionOpKind.Pop &&
                cleanupPop2.Kind == ExpressionOpKind.Pop &&
                jump.Target == propertyWriteIndex + 3)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSupportedComputedLogicalAssignmentRhsSpan(
        ExpressionProgram program,
        int startInclusive,
        int endExclusive,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (startInclusive >= endExclusive)
        {
            return false;
        }

        if (endExclusive - startInclusive == 1)
        {
            return IsSimpleOperand(
                program.GetOperation(startInclusive),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers);
        }

        if (endExclusive - startInclusive != 3)
        {
            return false;
        }

        var left = program.GetOperation(startInclusive);
        var right = program.GetOperation(startInclusive + 1);
        var binary = program.GetOperation(startInclusive + 2);
        return IsSimpleOperand(left, identifierConstants, activationSlots, allowsDynamicIdentifiers) &&
               IsSimpleOperand(right, identifierConstants, activationSlots, allowsDynamicIdentifiers) &&
               binary.Kind == ExpressionOpKind.Binary &&
               IsProductionBinaryOperator(binary.Operator);
    }

    private static bool TryIsFirstBoundaryPropertyWriteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 3)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var lastOp = program.GetOperation(program.OperationCount - 1);

        // Named property write: [base, rhs..., SetNamedProperty]
        if (lastOp.Kind == ExpressionOpKind.SetNamedProperty &&
            !lastOp.AllowNameInference &&
            TryGetActivationOrPlainDynamicIdentifierReadValue(
                program.GetOperation(0),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            var rhsStart = 1;
            var rhsEnd = program.OperationCount - 2;

            if (rhsStart == rhsEnd)
            {
                return IsSimpleOperand(
                    program.GetOperation(rhsStart),
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers);
            }

            // Multi-op RHS — try template literal span.
            return TryMeasureSimpleTemplateLiteralSpan(
                       program, rhsStart, identifierConstants, activationSlots, out var spanLen) &&
                   spanLen > 1 &&
                   rhsStart + spanLen - 1 == rhsEnd;
        }

        // Computed property write: [base, key..., value, SetComputedProperty]
        if (lastOp.Kind == ExpressionOpKind.SetComputedProperty &&
            !lastOp.AllowNameInference &&
            TryGetActivationOrPlainDynamicIdentifierReadValue(
                program.GetOperation(0),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            var valueIndex = program.OperationCount - 2;
            return IsSupportedComputedPropertyKeySpan(
                   program,
                   startInclusive: 1,
                   endExclusive: valueIndex,
                   identifierConstants,
                   activationSlots,
                   allowsDynamicIdentifiers) &&
                   IsSimpleOperand(
                       program.GetOperation(valueIndex),
                       identifierConstants,
                       activationSlots,
                       allowsDynamicIdentifiers);
        }

        return false;
    }

    private static bool TryGetActivationOrPlainDynamicIdentifierReadValue(
        PackedExpressionOp operation,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        return TryGetActivationResolvedValue(operation, identifierConstants, activationSlots) ||
               allowsDynamicIdentifiers &&
               TryGetPlainDynamicIdentifierReadValue(operation, identifierConstants, activationSlots);
    }

    private static bool TryIsFirstBoundaryNestedNamedPropertyWriteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 4)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var lastOp = program.GetOperation(program.OperationCount - 1);
        if (lastOp.Kind != ExpressionOpKind.SetNamedProperty ||
            lastOp.GetString(stringConstants).IsPrivateName() ||
            lastOp.AllowNameInference ||
            !TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var rhsStart = 1;
        while (rhsStart < program.OperationCount - 1)
        {
            var receiverRead = program.GetOperation(rhsStart);
            if (receiverRead.Kind != ExpressionOpKind.GetNamedProperty)
            {
                break;
            }

            if (receiverRead.GetString(stringConstants).IsPrivateName() ||
                receiverRead.IsOptional ||
                receiverRead.ShortCircuitOnNullishTarget)
            {
                return false;
            }

            rhsStart++;
        }

        if (rhsStart < 2)
        {
            return false;
        }

        var rhsEnd = program.OperationCount - 2;
        if (rhsStart == rhsEnd)
        {
            return IsSimpleOperand(program.GetOperation(rhsStart), identifierConstants, activationSlots);
        }

        return TryMeasureSimpleTemplateLiteralSpan(
                   program, rhsStart, identifierConstants, activationSlots, out var spanLen) &&
               spanLen > 1 &&
               rhsStart + spanLen - 1 == rhsEnd;
    }

    private static bool TryIsFirstBoundaryPropertyUpdateCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount == 2)
        {
            var propertyUpdate = program.GetOperation(1);
            return propertyUpdate.Kind == ExpressionOpKind.UpdateNamedProperty &&
                   TryGetActivationOrPlainDynamicIdentifierReadValue(
                       program.GetOperation(0),
                       identifierConstants,
                       activationSlots,
                       allowsDynamicIdentifiers);
        }

        if (program.OperationCount < 3)
        {
            return false;
        }

        var propertyUpdateIndex = program.OperationCount - 1;
        return program.GetOperation(propertyUpdateIndex).Kind == ExpressionOpKind.UpdateComputedProperty &&
               TryGetActivationOrPlainDynamicIdentifierReadValue(
                   program.GetOperation(0),
                   identifierConstants,
                   activationSlots,
                   allowsDynamicIdentifiers) &&
               IsSupportedComputedPropertyKeySpan(
                   program,
                   startInclusive: 1,
                   endExclusive: propertyUpdateIndex,
                   identifierConstants,
                   activationSlots,
                   allowsDynamicIdentifiers);
    }

    private static bool TryIsFirstBoundaryNestedNamedPropertyUpdateCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 3)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var propertyUpdate = program.GetOperation(program.OperationCount - 1);
        if (propertyUpdate.Kind != ExpressionOpKind.UpdateNamedProperty ||
            propertyUpdate.GetString(stringConstants).IsPrivateName() ||
            !TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        for (var operationIndex = 1; operationIndex < program.OperationCount - 1; operationIndex++)
        {
            var receiverRead = program.GetOperation(operationIndex);
            if (receiverRead.Kind != ExpressionOpKind.GetNamedProperty ||
                receiverRead.GetString(stringConstants).IsPrivateName() ||
                receiverRead.IsOptional ||
                receiverRead.ShortCircuitOnNullishTarget)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Admits the shape: [ActivationResolvedValue, GetNamedProperty+, RHS, ProductionBinary].
    ///     The RHS may be a single simple operand or a simple array/object literal span (gh2705).
    ///     Covers expressions like <c>this.prop === value</c> and <c>this.prop === [a, b]</c>.
    /// </summary>
    private static bool TryIsFirstBoundaryPropertyReadBinaryExpressionCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        // Minimum: [base, GetNamedProperty, rhs, Binary] = 4 ops
        if (program.OperationCount < 4)
        {
            return false;
        }

        var lastOp = program.GetOperation(program.OperationCount - 1);
        if (lastOp.Kind != ExpressionOpKind.Binary || !IsProductionBinaryOperator(lastOp.Operator))
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();

        // Walk the GetNamedProperty chain from index 1. At least one GetNamedProperty is required.
        var rhsStart = -1;
        for (var i = 1; i < program.OperationCount - 1; i++)
        {
            var op = program.GetOperation(i);
            if (op.Kind == ExpressionOpKind.GetNamedProperty &&
                !op.GetString(stringConstants).IsPrivateName() &&
                !op.IsOptional &&
                !op.ShortCircuitOnNullishTarget)
            {
                continue;
            }

            // First non-GetNamedProperty op marks the RHS start. Require at least one GetNamedProperty.
            if (i < 2)
            {
                return false;
            }

            rhsStart = i;
            break;
        }

        if (rhsStart < 0)
        {
            return false;
        }

        var rhsEnd = program.OperationCount - 2;
        if (rhsStart > rhsEnd)
        {
            return false;
        }

        if (rhsStart == rhsEnd)
        {
            // Single-op RHS — simple operand.
            return IsSimpleOperand(program.GetOperation(rhsStart), identifierConstants, activationSlots);
        }

        // Multi-op RHS — must be a simple array, object, or template-literal span that exactly fills [rhsStart..rhsEnd].
        if (TryMeasureSimpleArrayLiteralSpan(program, rhsStart, identifierConstants, activationSlots, out var arraySpanLen))
        {
            return rhsStart + arraySpanLen - 1 == rhsEnd;
        }

        if (TryMeasureSimpleObjectLiteralSpan(program, rhsStart, identifierConstants, activationSlots, out var objSpanLen))
        {
            return rhsStart + objSpanLen - 1 == rhsEnd;
        }

        if (TryMeasureSimpleTemplateLiteralSpan(program, rhsStart, identifierConstants, activationSlots, out var templateSpanLen) && templateSpanLen > 1)
        {
            return rhsStart + templateSpanLen - 1 == rhsEnd;
        }

        return false;
    }

    // Accepts: [activation-resolved, GetNamedProperty+, JumpIfFalse|JumpIfTrue|JumpIfNotNullish, Pop, simple-rhs]
    // i.e. this.prop && b / this.prop || b / this.prop ?? b
    private static bool TryIsFirstBoundaryPropertyReadShortCircuitExpressionCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        // Minimum: [base, GetNamedProperty, JumpIfX, Pop, rhs] = 5 ops
        if (program.OperationCount < 5)
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var shortCircuitStart = -1;
        for (var i = 1; i < program.OperationCount - 1; i++)
        {
            var op = program.GetOperation(i);
            if (op.Kind == ExpressionOpKind.GetNamedProperty &&
                !op.GetString(program.StringConstants.AsSpan()).IsPrivateName() &&
                !op.IsOptional &&
                !op.ShortCircuitOnNullishTarget)
            {
                continue;
            }

            if (i < 2)
            {
                return false;
            }

            shortCircuitStart = i;
            break;
        }

        if (shortCircuitStart < 0)
        {
            return false;
        }

        var jumpOp = program.GetOperation(shortCircuitStart);
        if (jumpOp.Kind is not (ExpressionOpKind.JumpIfFalse or ExpressionOpKind.JumpIfTrue or ExpressionOpKind.JumpIfNotNullish))
        {
            return false;
        }

        var popIndex = shortCircuitStart + 1;
        var rhsStart = shortCircuitStart + 2;

        if (rhsStart >= program.OperationCount ||
            program.GetOperation(popIndex).Kind != ExpressionOpKind.Pop ||
            jumpOp.Target != program.OperationCount ||
            rhsStart != program.OperationCount - 1)
        {
            return false;
        }

        return IsSimpleOperand(program.GetOperation(rhsStart), identifierConstants, activationSlots);
    }

    private static bool IsSimpleOperand(
        PackedExpressionOp operation,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers = false)
    {
        return operation.Kind switch
        {
            ExpressionOpKind.LoadLiteral => true,
            ExpressionOpKind.LoadThis => true,
            ExpressionOpKind.LoadNewTarget => true,
            ExpressionOpKind.LoadIdentifier => TryGetActivationResolvedValue(
                operation,
                identifierConstants,
                activationSlots) ||
                allowsDynamicIdentifiers &&
                !operation.IsArguments,
            _ => false
        };
    }

    private static bool IsSimpleComputedPropertyKeyOperand(
        PackedExpressionOp operation,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        return operation.Kind switch
        {
            ExpressionOpKind.LoadLiteral => true,
            ExpressionOpKind.LoadThis => true,
            ExpressionOpKind.LoadNewTarget => true,
            ExpressionOpKind.LoadIdentifier => TryGetActivationResolvedValue(
                operation,
                identifierConstants,
                activationSlots),
            _ => false
        };
    }

    private static bool IsPrivateNamedPropertyMutationOperation(
        PackedExpressionOp operation,
        ReadOnlySpan<string> stringConstants)
    {
        return (operation.Kind is ExpressionOpKind.SetNamedProperty
                               or ExpressionOpKind.UpdateNamedProperty
                               or ExpressionOpKind.DeleteNamedProperty) &&
               operation.GetString(stringConstants).IsPrivateName();
    }

    private static bool IsPrivateNamedPropertyOperation(
        PackedExpressionOp operation,
        ReadOnlySpan<string> stringConstants)
    {
        return (operation.Kind is ExpressionOpKind.GetNamedProperty
                               or ExpressionOpKind.SetNamedProperty
                               or ExpressionOpKind.UpdateNamedProperty
                               or ExpressionOpKind.DeleteNamedProperty) &&
               operation.GetString(stringConstants).IsPrivateName();
    }

    private static bool ContainsPropertyWriteOperation(ExpressionProgram program)
    {
        foreach (var operation in program.EnumerateOperations())
        {
            if (operation.Kind is ExpressionOpKind.SetNamedProperty or ExpressionOpKind.SetComputedProperty)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryGetExpressionProgram(
        ExecutionInstruction instruction,
        out ExpressionProgram program)
    {
        switch (instruction)
        {
            case SimpleVariableDeclarationInstruction { AwaitedProgram: null, InitializerProgram: { } initializerProgram }:
                program = initializerProgram;
                return true;

            case BindingVariableDeclarationInstruction { AwaitedProgram: null, InitializerProgram: { } initializerProgram }:
                program = initializerProgram;
                return true;

            case AssignmentSlotInstruction { AwaitedProgram: null, ValueProgram: { } valueProgram }:
                program = valueProgram;
                return true;

            case CompoundAssignmentSlotInstruction { AwaitedProgram: null, RhsProgram: { } rhsProgram }:
                program = rhsProgram;
                return true;

            case LogicalCompoundAssignmentSlotInstruction { AwaitedProgram: null, RhsProgram: { } logicalRhsProgram }:
                program = logicalRhsProgram;
                return true;

            case EvaluateAndDiscardInstruction { ExpressionProgram: { } expressionProgram }:
                program = expressionProgram;
                return true;

            case ThrowInstruction { AwaitedProgram: null, ThrowProgram: { } throwProgram }:
                program = throwProgram;
                return true;

            case BranchInstruction branch:
                program = branch.ConditionProgram;
                return true;

            case IteratorInitInstruction { AwaitedProgram: null, IterableProgram: { } iterableProgram }:
                program = iterableProgram;
                return true;

            case ForInInitInstruction { AwaitedProgram: null, ObjectProgram: { } objectProgram }:
                program = objectProgram;
                return true;

            case ArrayDestructuringInitInstruction arrayDestructuringInit:
                program = arrayDestructuringInit.SourceProgram;
                return true;

            case ObjectDestructuringInitInstruction objectDestructuringInit:
                program = objectDestructuringInit.SourceProgram;
                return true;

            case ReturnInstruction { AwaitedProgram: null, ReturnProgram: { } returnProgram }:
                program = returnProgram;
                return true;

            case EnterWithInstruction { AwaitedProgram: null, ObjectProgram: { } objectProgram }:
                program = objectProgram;
                return true;

            default:
                program = default;
            return false;
        }
    }

    private static bool IsSupportedDeclarationBindingTarget(BindingTargetProgram target)
    {
        switch (target)
        {
            case IdentifierBindingTargetProgram:
                return true;

            case ArrayBindingTargetProgram arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    if (element.DefaultProgram is not null ||
                        element.Target is { } elementTarget &&
                        !IsSupportedDeclarationBindingTarget(elementTarget))
                    {
                        return false;
                    }
                }

                return arrayBinding.RestElement is null ||
                       IsSupportedDeclarationBindingTarget(arrayBinding.RestElement);

            case ObjectBindingTargetProgram objectBinding:
                foreach (var property in objectBinding.Properties)
                {
                    if (property.DefaultProgram is not null ||
                        property.NameProgram is not null ||
                        !IsSupportedDeclarationBindingTarget(property.Target))
                    {
                        return false;
                    }
                }

                return objectBinding.RestElement is null ||
                       IsSupportedDeclarationBindingTarget(objectBinding.RestElement);

            default:
                return false;
        }
    }

    private static bool TryResolveActivationSlot(IdentifierOperand identifier, ActivationSlotShape activationSlots)
    {
        if (identifier.FlatSlotId >= 0)
        {
            return true;
        }

        if (identifier.ScopeId == activationSlots.ScopeId && identifier.SlotIndex >= 0)
        {
            return true;
        }

        if (identifier.ScopeId >= 0 && identifier.ScopeId != activationSlots.ScopeId)
        {
            return false;
        }

        return activationSlots.SlotMap.ContainsKey(identifier.Name) ||
               IsYieldStarSyntheticResult(identifier.Name);
    }

    private static bool IsYieldStarSyntheticResult(Symbol symbol) =>
        symbol.Name.StartsWith("__yield_lower_resume", StringComparison.Ordinal);

    private static bool TryResolveActivationSymbolSlot(
        Symbol symbol,
        int flatSlotId,
        ActivationSlotShape activationSlots)
    {
        if (flatSlotId >= 0)
        {
            return true;
        }

        return activationSlots.SlotMap.ContainsKey(symbol);
    }

    private static bool CanUseMaterializedActivationDynamicLookup(
        IdentifierOperand identifier,
        ActivationSlotShape activationSlots) =>
        identifier.ScopeId < 0 &&
        activationSlots.MaterializedBindingNames.Contains(identifier.Name);

    private static bool IsOrdinaryDynamicIdentifier(
        IdentifierOperand identifier,
        ActivationSlotShape activationSlots) =>
        identifier.ScopeId < 0 &&
        !CanUseMaterializedActivationDynamicLookup(identifier, activationSlots) &&
        !TryResolveActivationSlot(identifier, activationSlots);

    private static bool TryFindPrototypeOnlyOpcode(
        UnifiedBytecodeProgram program,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        foreach (var instruction in program.Instructions)
        {
            switch (instruction.OpCode)
            {
                case UnifiedBytecodeOpCode.Jump:
                case UnifiedBytecodeOpCode.JumpWithDriverCleanup:
                case UnifiedBytecodeOpCode.JumpIfFalse:
                case UnifiedBytecodeOpCode.JumpIfShortCircuitFalse:
                case UnifiedBytecodeOpCode.JumpIfShortCircuitTrue:
                case UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish:
                case UnifiedBytecodeOpCode.PushEnvironment:
                case UnifiedBytecodeOpCode.PopEnvironment:
                case UnifiedBytecodeOpCode.IteratorInit:
                case UnifiedBytecodeOpCode.IteratorMoveNext:
                case UnifiedBytecodeOpCode.IteratorClose:
                case UnifiedBytecodeOpCode.ForInInit:
                case UnifiedBytecodeOpCode.ForInMoveNext:
                case UnifiedBytecodeOpCode.TdzHeadInit:
                case UnifiedBytecodeOpCode.ArrayDestructuringInit:
                case UnifiedBytecodeOpCode.ArrayDestructuringElement:
                case UnifiedBytecodeOpCode.ArrayDestructuringRest:
                case UnifiedBytecodeOpCode.ArrayDestructuringClose:
                case UnifiedBytecodeOpCode.ObjectDestructuringInit:
                case UnifiedBytecodeOpCode.ObjectDestructuringProperty:
                case UnifiedBytecodeOpCode.ObjectDestructuringRest:
                case UnifiedBytecodeOpCode.ObjectDestructuringClose:
                    break;

                case UnifiedBytecodeOpCode.Binary:
                    if (!TryDecodeBinaryOperator(instruction, out var binaryOperator) ||
                        !IsProductionBinaryOperator(binaryOperator))
                    {
                        TryGetUnsupportedBinaryDecline(instruction, out declineCode, out declineReason);
                        return true;
                    }

                    break;

                case UnifiedBytecodeOpCode.LoadSlot:
                case UnifiedBytecodeOpCode.LoadDynamicIdentifier:
                case UnifiedBytecodeOpCode.LoadThis:
                case UnifiedBytecodeOpCode.LoadNewTarget:
                case UnifiedBytecodeOpCode.LoadImportMeta:
                case UnifiedBytecodeOpCode.LoadTemplateObject:
                case UnifiedBytecodeOpCode.LoadLiteral:
                case UnifiedBytecodeOpCode.LoadRegexLiteral:
                case UnifiedBytecodeOpCode.PrepareIdentifierCallTarget:
                case UnifiedBytecodeOpCode.PrepareIdentifierOptionalCallTarget:
                case UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget:
                case UnifiedBytecodeOpCode.PrepareDynamicIdentifierOptionalCallTarget:
                case UnifiedBytecodeOpCode.PrepareNamedCallTarget:
                case UnifiedBytecodeOpCode.PrepareComputedCallTarget:
                case UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget:
                case UnifiedBytecodeOpCode.PrepareComputedOptionalCallTarget:
                case UnifiedBytecodeOpCode.PrepareNamedSuperCallTarget:
                case UnifiedBytecodeOpCode.PrepareComputedSuperCallTarget:
                case UnifiedBytecodeOpCode.StoreSlot:
                case UnifiedBytecodeOpCode.UpdateSlot:
                case UnifiedBytecodeOpCode.InitializeSlot:
                case UnifiedBytecodeOpCode.DeclareDynamicVar:
                case UnifiedBytecodeOpCode.StoreDynamicIdentifier:
                case UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference:
                case UnifiedBytecodeOpCode.LoadDynamicIdentifierReference:
                case UnifiedBytecodeOpCode.StoreDynamicIdentifierReference:
                case UnifiedBytecodeOpCode.PopDynamicIdentifierReference:
                case UnifiedBytecodeOpCode.RequireObjectCoercible:
                case UnifiedBytecodeOpCode.ResolvePropertyKey:
                case UnifiedBytecodeOpCode.GetNamedProperty:
                case UnifiedBytecodeOpCode.GetNamedPropertyOptional:
                case UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined:
                case UnifiedBytecodeOpCode.JumpIfShortCircuited:
                case UnifiedBytecodeOpCode.GetComputedProperty:
                case UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet:
                case UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet:
                case UnifiedBytecodeOpCode.SetNamedProperty:
                case UnifiedBytecodeOpCode.SetComputedProperty:
                case UnifiedBytecodeOpCode.EnsureSuperReference:
                case UnifiedBytecodeOpCode.GetNamedSuperProperty:
                case UnifiedBytecodeOpCode.GetComputedSuperProperty:
                case UnifiedBytecodeOpCode.SetNamedSuperProperty:
                case UnifiedBytecodeOpCode.SetComputedSuperProperty:
                case UnifiedBytecodeOpCode.UpdateNamedSuperProperty:
                case UnifiedBytecodeOpCode.UpdateComputedSuperProperty:
                case UnifiedBytecodeOpCode.UpdateNamedProperty:
                case UnifiedBytecodeOpCode.UpdateComputedProperty:
                case UnifiedBytecodeOpCode.UpdateDynamicIdentifier:
                case UnifiedBytecodeOpCode.TypeOf:
                case UnifiedBytecodeOpCode.TypeOfIdentifier:
                case UnifiedBytecodeOpCode.TypeOfDynamicIdentifier:
                case UnifiedBytecodeOpCode.DeleteDynamicIdentifier:
                case UnifiedBytecodeOpCode.DeleteNamedProperty:
                case UnifiedBytecodeOpCode.DeleteComputedProperty:
                case UnifiedBytecodeOpCode.UnaryPlus:
                case UnifiedBytecodeOpCode.UnaryMinus:
                case UnifiedBytecodeOpCode.UnaryLogicalNot:
                case UnifiedBytecodeOpCode.UnaryBitwiseNot:
                case UnifiedBytecodeOpCode.UnaryVoid:
                case UnifiedBytecodeOpCode.PrivateFieldIn:
                case UnifiedBytecodeOpCode.ToString:
                case UnifiedBytecodeOpCode.Pop:
                case UnifiedBytecodeOpCode.DuplicateTop:
                case UnifiedBytecodeOpCode.DuplicateTopTwo:
                case UnifiedBytecodeOpCode.SwapTopTwo:
                case UnifiedBytecodeOpCode.RotateTopThreeRight:
                case UnifiedBytecodeOpCode.CreateArray:
                case UnifiedBytecodeOpCode.ArrayPush:
                case UnifiedBytecodeOpCode.ArrayPushHole:
                case UnifiedBytecodeOpCode.ArraySpread:
                case UnifiedBytecodeOpCode.CreateObject:
                case UnifiedBytecodeOpCode.DefineObjectProperty:
                case UnifiedBytecodeOpCode.DefineComputedObjectProperty:
                case UnifiedBytecodeOpCode.DefineObjectMethod:
                case UnifiedBytecodeOpCode.DefineComputedObjectMethod:
                case UnifiedBytecodeOpCode.DefineObjectAccessor:
                case UnifiedBytecodeOpCode.DefineComputedObjectAccessor:
                case UnifiedBytecodeOpCode.ObjectSpread:
                case UnifiedBytecodeOpCode.DeclareClass:
                case UnifiedBytecodeOpCode.DeclareFunction:
                case UnifiedBytecodeOpCode.LoadClassLiteral:
                case UnifiedBytecodeOpCode.LoadFunctionLiteral:
                case UnifiedBytecodeOpCode.ApplyBindingTarget:
                case UnifiedBytecodeOpCode.ApplyDeclarationBindingTarget:
                case UnifiedBytecodeOpCode.EnsureHasName:
                case UnifiedBytecodeOpCode.Return:
                case UnifiedBytecodeOpCode.ReturnUndefined:
                case UnifiedBytecodeOpCode.Throw:
                case UnifiedBytecodeOpCode.ThrowReferenceError:
                case UnifiedBytecodeOpCode.Break:
                case UnifiedBytecodeOpCode.Continue:
                case UnifiedBytecodeOpCode.EnterTry:
                case UnifiedBytecodeOpCode.EnterCatch:
                case UnifiedBytecodeOpCode.LeaveTry:
                case UnifiedBytecodeOpCode.EndFinally:
                case UnifiedBytecodeOpCode.EnterWith:
                case UnifiedBytecodeOpCode.LeaveWith:
                case UnifiedBytecodeOpCode.CallInvocationBoundary:
                case UnifiedBytecodeOpCode.ConstructInvocationBoundary:
                case UnifiedBytecodeOpCode.SuperConstructInvocationBoundary:
                    break;

                default:
                    declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                    declineReason =
                        $"Opcode '{instruction.OpCode}' is outside the first production unified bytecode subset.";
                    return true;
            }
        }

        declineCode = UnifiedBytecodeProductionDeclineCode.None;
        declineReason = string.Empty;
        return false;
    }

    private static void TryGetUnsupportedBinaryDecline(
        UnifiedBytecodeInstruction instruction,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
        if (!TryDecodeBinaryOperator(instruction, out var binaryOperator))
        {
            declineReason =
                $"Binary opcode uses unknown operator operand {instruction.Operand}, so the plan shape is outside production unified bytecode routing.";
            return;
        }

        declineReason =
            $"Binary operator '{FormatBinaryOperator(binaryOperator)}' is outside the production unified bytecode operator subset.";
    }

    private static bool TryDecodeBinaryOperator(
        UnifiedBytecodeInstruction instruction,
        out BinaryOperator binaryOperator)
    {
        if (instruction.Operand is < byte.MinValue or > byte.MaxValue)
        {
            binaryOperator = default;
            return false;
        }

        binaryOperator = (BinaryOperator)(byte)instruction.Operand;
        return Enum.IsDefined(binaryOperator);
    }

    private static bool IsProductionBinaryOperator(BinaryOperator binaryOperator) =>
        binaryOperator is
            BinaryOperator.Add or
            BinaryOperator.Subtract or
            BinaryOperator.Multiply or
            BinaryOperator.Divide or
            BinaryOperator.Modulo or
            BinaryOperator.Power or
            BinaryOperator.Equal or
            BinaryOperator.NotEqual or
            BinaryOperator.StrictEqual or
            BinaryOperator.StrictNotEqual or
            BinaryOperator.LessThan or
            BinaryOperator.LessThanOrEqual or
            BinaryOperator.GreaterThan or
            BinaryOperator.GreaterThanOrEqual or
            BinaryOperator.BitwiseAnd or
            BinaryOperator.BitwiseOr or
            BinaryOperator.BitwiseXor or
            BinaryOperator.LeftShift or
            BinaryOperator.RightShift or
            BinaryOperator.UnsignedRightShift or
            BinaryOperator.In or
            BinaryOperator.InstanceOf;

    private static string FormatBinaryOperator(BinaryOperator binaryOperator) =>
        binaryOperator switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            BinaryOperator.Modulo => "%",
            BinaryOperator.Power => "**",
            BinaryOperator.Equal => "==",
            BinaryOperator.NotEqual => "!=",
            BinaryOperator.StrictEqual => "===",
            BinaryOperator.StrictNotEqual => "!==",
            BinaryOperator.LessThan => "<",
            BinaryOperator.LessThanOrEqual => "<=",
            BinaryOperator.GreaterThan => ">",
            BinaryOperator.GreaterThanOrEqual => ">=",
            BinaryOperator.BitwiseAnd => "&",
            BinaryOperator.BitwiseOr => "|",
            BinaryOperator.BitwiseXor => "^",
            BinaryOperator.LeftShift => "<<",
            BinaryOperator.RightShift => ">>",
            BinaryOperator.UnsignedRightShift => ">>>",
            BinaryOperator.In => "in",
            BinaryOperator.InstanceOf => "instanceof",
            BinaryOperator.LogicalAnd => "&&",
            BinaryOperator.LogicalOr => "||",
            BinaryOperator.NullishCoalescing => "??",
            _ => throw new ArgumentOutOfRangeException(nameof(binaryOperator), binaryOperator, null)
        };
}
