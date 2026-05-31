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
    PropertyReadCandidateRequiresVmSupport,
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
    BreakOrContinueControlFlow,
    PrototypeOnlyBinaryOpcode,
    PrototypeOnlyJumpOpcode,
    PrototypeOnlyJumpIfFalseOpcode,
    UnsupportedPlanShape,
    CallInvocationBoundary
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

    public static UnifiedBytecodeProductionEligibilityResult EvaluateResumable(
        ExecutionPlan plan,
        in UnifiedBytecodeProductionActivationDescriptor activation)
    {
        if (activation.IsAsyncLike && activation.IsGenerator)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.AsyncLikeFunction,
                "Async-like generator activation is not eligible for resumable unified bytecode routing.");
        }

        if (!activation.IsAsyncLike && !activation.IsGenerator)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape,
                "Only async-like or generator functions are currently eligible for resumable unified bytecode routing.");
        }

        if (activation.HasCapturedOrDynamicActivation)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.CapturedOrDynamicActivation,
                "Captured or dynamic activation is not eligible for resumable unified bytecode routing.");
        }

        if (activation.HasArgumentsObjectDependency)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency,
                "Arguments-object-dependent execution is not eligible for resumable unified bytecode routing.");
        }

        // 'this'-dependent resumable programs are accepted: the strict/sloppy-coerced binding is
        // threaded through UnifiedBytecodeResumeState and pushed by the ExecuteResumable LoadThis
        // case (mirrors the production sync route landed in #2633/#2643). new.target, captured/dynamic
        // activation, arguments-object, call, and dynamic-lookup shapes still decline below.
        if (activation.HasNewTargetDependency)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.NewTargetDependency,
                "new.target dependency is not eligible for resumable unified bytecode routing.");
        }

        if (activation.HasCallDependency)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.CallDependency,
                "Call/construct dependency is not eligible for resumable unified bytecode routing.");
        }

        if (activation.HasDynamicLookupDependency)
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency,
                "Dynamic lookup dependency is not eligible for resumable unified bytecode routing.");
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

    private static bool TryFindPlanDecline(
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

            // Labeled breakable regions are admitted (loop-control targets are compiler-owned,
            // ADR 0253). The labeled shape that is not yet provably safe is a labeled break/continue
            // that transfers control out of an enclosing iterator/for-in driver loop it is not
            // directly targeting: the VM's single-level driver cleanup only closes the driver whose
            // break target equals the jump target, so an intervening inner iterator would be leaked.
            // Decline that crossing shape conservatively before VM execution to preserve
            // no-mixed-execution.
            if (instruction is BreakInstruction breakInstruction &&
                IsLabeledAbruptCrossingDriver(plan, instructionIndex, breakInstruction.TargetIndex, isBreak: true))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.LabelControlFlow;
                declineReason =
                    "Labeled break that crosses an enclosing iterator/for-in driver loop is not eligible for production unified bytecode routing.";
                return true;
            }

            if (instruction is ContinueInstruction continueInstruction &&
                IsLabeledAbruptCrossingDriver(plan, instructionIndex, continueInstruction.TargetIndex, isBreak: false))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.LabelControlFlow;
                declineReason =
                    "Labeled continue that crosses an enclosing iterator/for-in driver loop is not eligible for production unified bytecode routing.";
                return true;
            }

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

            if (instruction is BindingVariableDeclarationInstruction)
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.DestructuringDependency;
                declineReason = "Binding/destructuring declarations are not eligible for production unified bytecode routing.";
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
            case AwaitAndDiscardInstruction:
            case ReturnInstruction { AwaitedProgram: not null }:
            case StoreResumeValueInstruction:
                declineReason = string.Empty;
                return true;
            case YieldStarInstruction:
                declineReason =
                    "YieldStar is not eligible for resumable unified bytecode routing until delegated return/throw is modeled.";
                return false;
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
                UnifiedBytecodeOpCode.Jump or
                UnifiedBytecodeOpCode.JumpIfFalse or
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

        // Pre-scan: any ArraySpread whose immediately-preceding op is non-simple must decline with
        // ObjectLiteralOrSpreadDependency before the main loop processes the source ops (which may
        // otherwise trigger a less-specific decline code such as CallDependency).
        for (var i = 0; i < operationCount; i++)
        {
            if (program.GetOperation(i).Kind == ExpressionOpKind.ArraySpread &&
                (i == 0 || !IsSimpleOperand(program.GetOperation(i - 1), identifierConstants, activationSlots)))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency;
                declineReason =
                    "Array spread with non-simple source is not eligible for production unified bytecode routing.";
                return true;
            }
        }

        var isCallTargetPreparationCandidate = TryIsFirstBoundaryCallTargetPreparationCandidate(
            program,
            identifierConstants,
            stringConstants,
            activationSlots,
            allowsDynamicIdentifiers);
        var hasOptionalChainOperation = HasOptionalChainOperation(program);
        for (var operationIndex = 0; operationIndex < operationCount; operationIndex++)
        {
            var operation = program.GetOperation(operationIndex);
            if (IsPrivateNamedPropertyOperation(operation, stringConstants))
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
                        declineCode = UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency;
                        declineReason =
                            "arguments call targets are not eligible for production unified bytecode routing.";
                        return true;
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

                    // Synchronous spread calls are admitted (gh2676); the call-target
                    // preparation candidate check accepts them. Anything reaching here is
                    // an out-of-boundary call shape.
                    //
                    // Use CallInvocationBoundary (not CallDependency) for this plan-structural
                    // decline so IsPlanStructuralDecline can distinguish it from the
                    // descriptor-level HasCallDependency decline and cache it permanently.
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
                    // Synchronous non-spread construct calls (`new F(...)`) are admitted (gh2690):
                    // the constructor value and simple-operand arguments are pushed left-to-right
                    // and the ConstructInvocationBoundary opcode invokes [[Construct]] with the
                    // constructor as new.target. Spread-onto-construct stays declined — spread
                    // flattening for construct is not yet modeled at the invocation boundary.
                    if (operation.SpreadMaskConstantIndex >= 0)
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency;
                        declineReason =
                            "Spread construct arguments are not eligible for production unified bytecode routing.";
                        return true;
                    }

                    break;

                case ExpressionOpKind.SuperConstruct:
                case ExpressionOpKind.LoadNamedSuperCallTarget:
                case ExpressionOpKind.LoadComputedSuperCallTarget:
                    // super(...) and super-member call targets only appear inside derived
                    // constructors, which the activation gate in
                    // SyncFunctionInvoker.CanUseProductionUnifiedBytecode already declines
                    // (IsClassConstructor / IsDefaultDerivedConstructor / _superConstructor /
                    // _lexicalThisEnvironment / _instanceFields). Admitting them here would be
                    // unreachable, unprovable dead code, so they stay explicitly declined
                    // (gh2690 ADR 0286).
                    declineCode = UnifiedBytecodeProductionDeclineCode.SuperPropertyDependency;
                    declineReason =
                        "super call semantics are not eligible for production unified bytecode routing.";
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
                case ExpressionOpKind.LoadResolvedIdentifierValue:
                case ExpressionOpKind.StoreResolvedIdentifier:
                case ExpressionOpKind.PopResolvedIdentifierReference:
                case ExpressionOpKind.StoreIdentifier:
                    if (allowsDynamicIdentifiers)
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                    declineReason =
                        "Dynamic identifier assignment references are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.TypeOfIdentifier:
                    if (operation.IsArguments)
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency;
                        declineReason =
                            "arguments object access is not eligible for production unified bytecode routing.";
                        return true;
                    }

                    var typeOfIdentifier = operation.GetIdentifier(identifierConstants);
                    if (!TryResolveActivationSlot(typeOfIdentifier, activationSlots))
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
                        // Continuation hop of a multi-hop optional named chain (a?.b.c / a?.b?.c).
                        if (TryIsFirstBoundaryOptionalNamedChainCandidate(program, identifierConstants, activationSlots))
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
                        // Simple a?.b form — admitted when the program is exactly [activation-resolved base, GetNamedPropertyOptional].
                        if (TryIsFirstBoundaryOptionalNamedPropertyReadCandidate(program, identifierConstants, activationSlots))
                        {
                            break;
                        }

                        // a?.b.c / a?.b?.c chain, or a?.b[k] shape.
                        if (TryIsFirstBoundaryOptionalNamedChainCandidate(program, identifierConstants, activationSlots) ||
                            TryIsFirstBoundaryOptionalNamedThenComputedCandidate(program, identifierConstants, activationSlots))
                        {
                            break;
                        }

                        declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                        declineReason =
                            "Optional-chain property reads are outside the first production property-read boundary.";
                        return true;
                    }

                    if (isCallTargetPreparationCandidate)
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryNamedPropertyReadCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsNamedPropertyReadAtLogicalShortCircuitBoundary(program, operationIndex, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryNamedCompoundPropertyWriteCandidate(program, identifierConstants, activationSlots))
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

                    if (ContainsPropertyWriteOperation(program))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency;
                        declineReason =
                            "Compound/logical property writes are outside the first production property-write boundary.";
                        return true;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.PropertyReadBoundaryOutOfScope;
                    declineReason =
                        "Named property reads are outside the first production property-read boundary unless they are direct activation-resolved base reads or exact two-hop named chains.";
                    return true;

                case ExpressionOpKind.GetComputedProperty:
                    if (operation.ShortCircuitOnNullishTarget)
                    {
                        // a?.b[k] shape — admitted when the program matches the optional named then computed shape.
                        if (TryIsFirstBoundaryOptionalNamedThenComputedCandidate(program, identifierConstants, activationSlots))
                        {
                            break;
                        }

                        declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                        declineReason =
                            "Optional-chain computed property reads are outside the first production property-read boundary.";
                        return true;
                    }

                    if (TryIsFirstBoundaryComputedPropertyReadCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryOptionalComputedPropertyReadCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryComputedCompoundPropertyWriteCandidate(program, identifierConstants, activationSlots))
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

                case ExpressionOpKind.SetNamedSuperProperty:
                case ExpressionOpKind.SetComputedSuperProperty:
                case ExpressionOpKind.UpdateNamedSuperProperty:
                case ExpressionOpKind.UpdateComputedSuperProperty:
                    declineCode = UnifiedBytecodeProductionDeclineCode.SuperPropertyDependency;
                    declineReason = "super property writes/updates are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.SetNamedProperty:
                case ExpressionOpKind.SetComputedProperty:
                    if (TryIsFirstBoundaryPropertyWriteCandidate(program, identifierConstants, activationSlots) ||
                        TryIsFirstBoundaryNamedCompoundPropertyWriteCandidate(program, identifierConstants, activationSlots) ||
                        TryIsFirstBoundaryComputedCompoundPropertyWriteCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency;
                    declineReason =
                        "Property writes are outside the first production boundary unless they use an activation-resolved base with simple key/value operands.";
                    return true;

                case ExpressionOpKind.UpdateIdentifier:
                    if (allowsDynamicIdentifiers && !operation.IsArguments)
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.PropertyUpdateDependency;
                    declineReason = "Update expressions are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.UpdateNamedProperty:
                case ExpressionOpKind.UpdateComputedProperty:
                    if (TryIsFirstBoundaryPropertyUpdateCandidate(program, identifierConstants, activationSlots))
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
                case ExpressionOpKind.DeleteComputedProperty:
                    declineCode = UnifiedBytecodeProductionDeclineCode.DeleteDependency;
                    declineReason = "delete expressions are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.GetNamedSuperProperty:
                case ExpressionOpKind.GetComputedSuperProperty:
                case ExpressionOpKind.EnsureSuperReference:
                    declineCode = UnifiedBytecodeProductionDeclineCode.SuperPropertyDependency;
                    declineReason = "super property access is not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.JumpIfFalse:
                case ExpressionOpKind.JumpIfConditionalFalse:
                case ExpressionOpKind.JumpIfTrue:
                case ExpressionOpKind.JumpIfNotNullish:
                case ExpressionOpKind.Jump:
                case ExpressionOpKind.Pop:
                    // Admitted: Jump and Pop appear in the conditional (?:) expression IR.
                    // Pop discards the condition value on the taken/not-taken path;
                    // Jump is the unconditional forward branch to the end of the ternary.
                    break;

                case ExpressionOpKind.JumpIfNullish:
                    if (isCallTargetPreparationCandidate || !operation.ReplaceWithUndefined)
                    {
                        break;
                    }

                    // Nullish guard of a?.[k] — admitted when the program matches the optional computed read shape.
                    if (TryIsFirstBoundaryOptionalComputedPropertyReadCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                    declineReason =
                        "Optional-chain short-circuiting is outside the first production property-read boundary.";
                    return true;

                case ExpressionOpKind.JumpIfShortCircuited:
                    if (isCallTargetPreparationCandidate)
                    {
                        break;
                    }

                    // JumpIfShortCircuited only appears in call-target programs; property-read chains
                    // use GetNamedProperty(ShortCircuitOnNullishTarget:true) instead.
                    if (TryIsFirstBoundaryOptionalNamedPropertyReadChainCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                    declineReason =
                        "Optional-chain short-circuiting is outside the first production property-read boundary.";
                    return true;

                case ExpressionOpKind.LoadClassLiteral:
                {
                    var nextIndex = operationIndex + 1;
                    if (nextIndex < operationCount)
                    {
                        var nextOp = program.GetOperation(nextIndex);
                        if ((nextOp.Kind == ExpressionOpKind.DefineObjectProperty ||
                             nextOp.Kind == ExpressionOpKind.DefineComputedObjectProperty) &&
                            nextOp.AllowNameInference)
                        {
                            break;
                        }
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency;
                    declineReason =
                        "Class literal values are not eligible for production unified bytecode routing.";
                    return true;
                }

                case ExpressionOpKind.ArraySpread:
                    if (operationIndex > 0 &&
                        IsSimpleOperand(program.GetOperation(operationIndex - 1), identifierConstants, activationSlots))
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
                case ExpressionOpKind.ObjectSpread:
                    declineCode = UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency;
                    declineReason =
                        "Object methods, object accessors, and object spread are not eligible for production unified bytecode routing.";
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
                    declineCode = UnifiedBytecodeProductionDeclineCode.PrivateFieldDependency;
                    declineReason = "Private-field expressions are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.ApplyBindingTarget:
                    declineCode = UnifiedBytecodeProductionDeclineCode.DestructuringDependency;
                    declineReason = "Destructuring expressions are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.Binary:
                    if (!IsProductionBinaryOperator(operation.Operator))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.PrototypeOnlyBinaryOpcode;
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

    /// <summary>
    ///     Detects a labeled <c>break</c>/<c>continue</c> that transfers control out of an enclosing
    ///     iterator/for-in driver loop that it is not directly targeting. The VM closes only the
    ///     driver whose break target equals the abrupt jump target (single-level cleanup); an abrupt
    ///     that exits an <em>intervening</em> driver loop would leave that inner iterator active
    ///     (leaked). Single-level break out of a driver's own loop and continue of the loop itself
    ///     stay eligible; the crossing shape is declined to preserve no-mixed-execution.
    /// </summary>
    private static bool IsLabeledAbruptCrossingDriver(
        ExecutionPlan plan,
        int abruptInstructionIndex,
        int abruptTargetIndex,
        bool isBreak)
    {
        var instructions = plan.Instructions;
        var effectiveTarget = ResolveCleanupChainTarget(instructions, abruptTargetIndex);

        for (var moveNextIndex = 0; moveNextIndex < instructions.Length; moveNextIndex++)
        {
            if (instructions[moveNextIndex] is not (IteratorMoveNextInstruction or ForInMoveNextInstruction))
            {
                continue;
            }

            var breakIndex = instructions[moveNextIndex] switch
            {
                IteratorMoveNextInstruction iteratorMoveNext => iteratorMoveNext.BreakIndex,
                ForInMoveNextInstruction forInMoveNext => forInMoveNext.BreakIndex,
                _ => -1
            };

            var region = ComputeDriverLoopBodyRegion(instructions, moveNextIndex, breakIndex);

            // The abrupt must execute inside this driver loop...
            if (!region.Contains(abruptInstructionIndex))
            {
                continue;
            }

            // ...and the abrupt stays "within" this driver loop when it either re-enters/continues
            // the loop (target lands inside the body or at the move-next header) or breaks out of
            // this loop directly (target is this loop's own break index). Anything else exits this
            // intervening driver loop without the single-level VM cleanup unwinding it.
            if (region.Contains(effectiveTarget) || effectiveTarget == moveNextIndex)
            {
                continue;
            }

            if (isBreak &&
                (effectiveTarget == breakIndex ||
                 effectiveTarget == ResolveCleanupChainTarget(instructions, breakIndex)))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    ///     Follows the environment-cleanup chain (<see cref="PopEnvironmentInstruction" /> /
    ///     <see cref="LeaveWithInstruction" />) that a break/continue jumps through before reaching
    ///     the loop continue/break point it actually targets.
    /// </summary>
    private static int ResolveCleanupChainTarget(ImmutableArray<ExecutionInstruction> instructions, int targetIndex)
    {
        var visited = new HashSet<int>();
        var current = targetIndex;
        while ((uint)current < (uint)instructions.Length && visited.Add(current))
        {
            var next = instructions[current] switch
            {
                PopEnvironmentInstruction popEnvironment => popEnvironment.Next,
                LeaveWithInstruction leaveWith => leaveWith.Next,
                _ => current
            };

            if (next == current)
            {
                break;
            }

            current = next;
        }

        return current;
    }

    /// <summary>
    ///     Computes the structured body of the driver loop headed by the move-next instruction at
    ///     <paramref name="moveNextIndex" />. The walk starts at the loop body entry, never crosses
    ///     the loop exit (<paramref name="breakIndex" />) or re-enters the move-next header, and
    ///     treats break/continue as sinks so a labeled abrupt that targets an outer construct does
    ///     not leak outer instructions into this loop's body.
    /// </summary>
    private static HashSet<int> ComputeDriverLoopBodyRegion(
        ImmutableArray<ExecutionInstruction> instructions,
        int moveNextIndex,
        int breakIndex)
    {
        var region = new HashSet<int>();
        var bodyEntry = instructions[moveNextIndex].Next;
        if ((uint)bodyEntry >= (uint)instructions.Length)
        {
            return region;
        }

        var stack = new Stack<int>();
        stack.Push(bodyEntry);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if ((uint)current >= (uint)instructions.Length ||
                current == breakIndex ||
                current == moveNextIndex ||
                !region.Add(current))
            {
                continue;
            }

            // Break/continue transfer control out of the local flow; treat them as sinks so the
            // region stays within this loop's structured body.
            if (instructions[current] is BreakInstruction or ContinueInstruction)
            {
                continue;
            }

            foreach (var successor in instructions[current].GetSuccessors())
            {
                stack.Push(successor);
            }

            // GetSuccessors does not surface the for-in move-next break edge; include it so nested
            // for-in loop exits remain inside the enclosing region.
            if (instructions[current] is ForInMoveNextInstruction { BreakIndex: var nestedBreak } &&
                nestedBreak >= 0)
            {
                stack.Push(nestedBreak);
            }
        }

        return region;
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

    private static bool TryIsFirstBoundaryNamedPropertyReadCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 2)
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        for (var index = 1; index < program.OperationCount; index++)
        {
            var operation = program.GetOperation(index);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.GetString(program.StringConstants.AsSpan()).IsPrivateName() ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget)
            {
                return false;
            }
        }

        return true;
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

    private static bool TryIsFirstBoundaryComputedPropertyReadCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount != 5)
        {
            return false;
        }

        var baseLoad = program.GetOperation(0);
        if (!TryGetActivationResolvedValue(baseLoad, identifierConstants, activationSlots))
        {
            return false;
        }

        var keyLoad = program.GetOperation(1);
        if (keyLoad.Kind == ExpressionOpKind.LoadIdentifier &&
            !TryGetActivationResolvedValue(keyLoad, identifierConstants, activationSlots))
        {
            return false;
        }

        if (keyLoad.Kind is not (ExpressionOpKind.LoadIdentifier or ExpressionOpKind.LoadLiteral))
        {
            return false;
        }

        var requireObjectCoercible = program.GetOperation(2);
        if (requireObjectCoercible.Kind != ExpressionOpKind.RequireObjectCoercible ||
            requireObjectCoercible.Depth != 1)
        {
            return false;
        }

        var resolvePropertyKey = program.GetOperation(3);
        if (resolvePropertyKey.Kind != ExpressionOpKind.ResolvePropertyKey)
        {
            return false;
        }

        var getComputedProperty = program.GetOperation(4);
        return getComputedProperty.Kind == ExpressionOpKind.GetComputedProperty &&
               !getComputedProperty.ShortCircuitOnNullishTarget;
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

    // Admits multi-hop optional named chains a?.b.c and a?.b?.c:
    // [activation-resolved base,
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

        var firstHop = program.GetOperation(1);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        for (var index = 2; index < program.OperationCount; index++)
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

    // Admits the a?.b[k] shape:
    // [activation-resolved base, GetNamedProperty(IsOptional:true, !SC, non-private), simple-key, GetComputedProperty(SC:true)]
    private static bool TryIsFirstBoundaryOptionalNamedThenComputedCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount != 4)
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

        if (!IsSimpleComputedPropertyKey(program.GetOperation(2), identifierConstants, activationSlots))
        {
            return false;
        }

        var computedOp = program.GetOperation(3);
        return computedOp.Kind == ExpressionOpKind.GetComputedProperty &&
               computedOp.ShortCircuitOnNullishTarget;
    }


    // Admits the simple a?.[k] shape:
    // [activation-resolved base, JumpIfNullish(ReplaceWithUndefined:true), simple key, GetComputedProperty(!ShortCircuitOnNullishTarget)].
    private static bool TryIsFirstBoundaryOptionalComputedPropertyReadCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount != 4)
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var jumpOp = program.GetOperation(1);
        if (jumpOp.Kind != ExpressionOpKind.JumpIfNullish || !jumpOp.ReplaceWithUndefined)
        {
            return false;
        }

        var keyOp = program.GetOperation(2);
        if (!IsSimpleComputedPropertyKey(keyOp, identifierConstants, activationSlots))
        {
            return false;
        }

        var getComputedOp = program.GetOperation(3);
        return getComputedOp.Kind == ExpressionOpKind.GetComputedProperty &&
               !getComputedOp.ShortCircuitOnNullishTarget;
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

        // Optional-call shapes (box?.read(), box.read?.(), box[key]?.()) carry a
        // JumpIfNullish short-circuit and, for callee-optional cases, a trailing
        // Jump/SwapTopTwo/Pop structure that the non-optional branches below would
        // reject (or never reach, because they end in Pop rather than Call). Detect
        // them first so the dedicated optional candidates own these shapes.
        if (TryIsFirstBoundaryReceiverOptionalNamedCallCandidate(program, identifierConstants, stringConstants, activationSlots) ||
            TryIsFirstBoundaryCalleeOptionalNamedCallCandidate(program, identifierConstants, stringConstants, activationSlots) ||
            TryIsFirstBoundaryCalleeOptionalComputedCallCandidate(program, identifierConstants, activationSlots))
        {
            return true;
        }

        var callIndex = program.OperationCount - 1;
        var call = program.GetOperation(callIndex);
        // Synchronous spread calls are admitted (gh2676); spread args are flattened at
        // the invocation boundary. Direct eval stays out of scope.
        if (call.Kind != ExpressionOpKind.Call ||
            !call.HasExplicitThis ||
            call.IsDirectEval)
        {
            return false;
        }

        var firstOperation = program.GetOperation(0);
        if (firstOperation.Kind == ExpressionOpKind.LoadIdentifierCallTarget)
        {
            return !firstOperation.IsArguments &&
                   (TryGetActivationResolvedIdentifier(firstOperation, identifierConstants, activationSlots) ||
                    allowsDynamicIdentifiers) &&
                   HasSimpleCallArguments(program, identifierConstants, activationSlots, argsStartIndex: 1, call);
        }

        var namedCallTargetIndex = FindFirstOperation(program, ExpressionOpKind.LoadNamedCallTarget);
        if (namedCallTargetIndex > 0)
        {
            var namedCallTarget = program.GetOperation(namedCallTargetIndex);
            return !namedCallTarget.IsOptional &&
                   !namedCallTarget.ShortCircuitOnNullishTarget &&
                   !namedCallTarget.GetString(stringConstants).IsPrivateName() &&
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
            var keyIndex = computedCallTargetIndex - 1;
            return !computedCallTarget.IsOptional &&
                   !computedCallTarget.ShortCircuitOnNullishTarget &&
                   IsSupportedNamedReceiverChain(
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
                       computedCallTargetIndex + 1,
                       call);
        }

        return false;
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
        ActivationSlotShape activationSlots)
    {
        return operation.Kind switch
        {
            ExpressionOpKind.LoadLiteral => true,
            ExpressionOpKind.LoadIdentifier => TryGetActivationResolvedValue(
                operation,
                identifierConstants,
                activationSlots),
            _ => false
        };
    }

    private static bool HasSimpleCallArguments(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        int argsStartIndex,
        PackedExpressionOp call)
    {
        return HasSimpleCallArguments(
            program,
            identifierConstants,
            activationSlots,
            argsStartIndex,
            call,
            program.OperationCount - 1);
    }

    private static bool HasSimpleCallArguments(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        int argsStartIndex,
        PackedExpressionOp call,
        int callIndex)
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
                if (!TryMeasureSimpleArrayLiteralSpan(program, operationIndex, identifierConstants, activationSlots, out var spanLen))
                {
                    return false;
                }

                operationIndex += spanLen;
            }
            else if (op.Kind == ExpressionOpKind.CreateObject)
            {
                if (!TryMeasureSimpleObjectLiteralSpan(program, operationIndex, identifierConstants, activationSlots, out var spanLen))
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
                if (TryMeasureSimpleTemplateLiteralSpan(program, operationIndex, identifierConstants, activationSlots, out var spanLen) && spanLen > 1)
                {
                    operationIndex += spanLen;
                }
                else
                {
                    // Standalone literal — same as IsSimpleOperand.
                    operationIndex++;
                }
            }
            else if (IsSimpleOperand(op, identifierConstants, activationSlots))
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
        out int spanLength)
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

            if (!IsSimpleOperand(elementOp, identifierConstants, activationSlots))
            {
                // Non-simple op terminates the element scan — the array literal ends here.
                break;
            }

            i++;
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
    // Admitted shapes (CreateObject followed by N ≥ 0 property triples):
    //   Static:   [simple-value-operand, DefineObjectProperty(non-private, no name inference)]
    //   Computed: [simple-key-operand, ResolvePropertyKey, simple-value-operand, DefineComputedObjectProperty(no name inference)]
    // DefineObjectMethod, ObjectSpread, private names, name inference, and complex key expressions are declined.
    private static bool TryMeasureSimpleObjectLiteralSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength)
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
            var firstOp = program.GetOperation(i);
            if (!IsSimpleOperand(firstOp, identifierConstants, activationSlots))
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
        out int spanLength)
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

            // Substitution part: simple-operand, ToString, Binary(Add)
            if (IsSimpleOperand(op, identifierConstants, activationSlots))
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
        ActivationSlotShape activationSlots)
    {
        // Shape: [base, DuplicateTop, GetNamedProperty, rhs..., Binary, SetNamedProperty]
        // Minimum: 6 ops (rhs is a single simple operand).
        if (program.OperationCount < 6)
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var duplicateTarget = program.GetOperation(1);
        var propertyRead = program.GetOperation(2);
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

        var stringConstants = program.StringConstants.AsSpan();
        var propertyName = propertyRead.GetString(stringConstants);
        if (propertyName.IsPrivateName() || propertyName != propertyWrite.GetString(stringConstants))
        {
            return false;
        }

        var rhsStart = 3;
        var rhsEnd = program.OperationCount - 3;

        if (rhsStart == rhsEnd)
        {
            return IsSimpleOperand(program.GetOperation(rhsStart), identifierConstants, activationSlots);
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
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount != 9)
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots) ||
            !IsSimpleOperand(program.GetOperation(1), identifierConstants, activationSlots))
        {
            return false;
        }

        var requireObjectCoercible = program.GetOperation(2);
        var resolvePropertyKey = program.GetOperation(3);
        var duplicateTargetAndKey = program.GetOperation(4);
        var propertyRead = program.GetOperation(5);
        var rhs = program.GetOperation(6);
        var binary = program.GetOperation(7);
        var propertyWrite = program.GetOperation(8);
        return requireObjectCoercible.Kind == ExpressionOpKind.RequireObjectCoercible &&
               requireObjectCoercible.Depth == 1 &&
               resolvePropertyKey.Kind == ExpressionOpKind.ResolvePropertyKey &&
               duplicateTargetAndKey.Kind == ExpressionOpKind.DuplicateTopTwo &&
               propertyRead.Kind == ExpressionOpKind.GetComputedProperty &&
               !propertyRead.ShortCircuitOnNullishTarget &&
               IsSimpleOperand(rhs, identifierConstants, activationSlots) &&
               binary.Kind == ExpressionOpKind.Binary &&
               IsProductionBinaryOperator(binary.Operator) &&
               propertyWrite.Kind == ExpressionOpKind.SetComputedProperty &&
               !propertyWrite.AllowNameInference;
    }

    private static bool TryIsFirstBoundaryPropertyWriteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 3)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var lastOp = program.GetOperation(program.OperationCount - 1);

        // Named property write: [base, rhs..., SetNamedProperty]
        if (lastOp.Kind == ExpressionOpKind.SetNamedProperty &&
            !lastOp.GetString(stringConstants).IsPrivateName() &&
            !lastOp.AllowNameInference &&
            TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            var rhsStart = 1;
            var rhsEnd = program.OperationCount - 2;

            if (rhsStart == rhsEnd)
            {
                return IsSimpleOperand(program.GetOperation(rhsStart), identifierConstants, activationSlots);
            }

            // Multi-op RHS — try template literal span.
            return TryMeasureSimpleTemplateLiteralSpan(
                       program, rhsStart, identifierConstants, activationSlots, out var spanLen) &&
                   spanLen > 1 &&
                   rhsStart + spanLen - 1 == rhsEnd;
        }

        // Computed property write: [base, key, value, SetComputedProperty]
        if (program.OperationCount == 4)
        {
            var computedWrite = program.GetOperation(3);
            return computedWrite.Kind == ExpressionOpKind.SetComputedProperty &&
                   !computedWrite.AllowNameInference &&
                   TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots) &&
                   IsSimpleOperand(program.GetOperation(1), identifierConstants, activationSlots) &&
                   IsSimpleOperand(program.GetOperation(2), identifierConstants, activationSlots);
        }

        return false;
    }

    private static bool TryIsFirstBoundaryPropertyUpdateCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount == 2)
        {
            var propertyUpdate = program.GetOperation(1);
            return propertyUpdate.Kind == ExpressionOpKind.UpdateNamedProperty &&
                   !propertyUpdate.GetString(program.StringConstants.AsSpan()).IsPrivateName() &&
                   TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots);
        }

        if (program.OperationCount != 3)
        {
            return false;
        }

        return program.GetOperation(2).Kind == ExpressionOpKind.UpdateComputedProperty &&
               TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots) &&
               IsSimpleOperand(program.GetOperation(1), identifierConstants, activationSlots);
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
        ActivationSlotShape activationSlots)
    {
        return operation.Kind switch
        {
            ExpressionOpKind.LoadLiteral => true,
            ExpressionOpKind.LoadIdentifier => TryGetActivationResolvedValue(
                operation,
                identifierConstants,
                activationSlots),
            ExpressionOpKind.LoadThis => true,
            ExpressionOpKind.LoadNewTarget => true,
            _ => false
        };
    }

    private static bool IsPrivateNamedPropertyOperation(
        PackedExpressionOp operation,
        ReadOnlySpan<string> stringConstants)
    {
        return (operation.Kind is ExpressionOpKind.GetNamedProperty
                               or ExpressionOpKind.SetNamedProperty
                               or ExpressionOpKind.UpdateNamedProperty) &&
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
                        TryGetPrototypeOnlyBinaryDecline(instruction, out declineCode, out declineReason);
                        return true;
                    }

                    break;

                case UnifiedBytecodeOpCode.LoadSlot:
                case UnifiedBytecodeOpCode.LoadDynamicIdentifier:
                case UnifiedBytecodeOpCode.LoadThis:
                case UnifiedBytecodeOpCode.LoadNewTarget:
                case UnifiedBytecodeOpCode.LoadLiteral:
                case UnifiedBytecodeOpCode.PrepareIdentifierCallTarget:
                case UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget:
                case UnifiedBytecodeOpCode.PrepareNamedCallTarget:
                case UnifiedBytecodeOpCode.PrepareComputedCallTarget:
                case UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget:
                case UnifiedBytecodeOpCode.PrepareComputedOptionalCallTarget:
                case UnifiedBytecodeOpCode.StoreSlot:
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
                case UnifiedBytecodeOpCode.GetComputedProperty:
                case UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet:
                case UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet:
                case UnifiedBytecodeOpCode.SetNamedProperty:
                case UnifiedBytecodeOpCode.SetComputedProperty:
                case UnifiedBytecodeOpCode.UpdateNamedProperty:
                case UnifiedBytecodeOpCode.UpdateComputedProperty:
                case UnifiedBytecodeOpCode.UpdateDynamicIdentifier:
                case UnifiedBytecodeOpCode.TypeOf:
                case UnifiedBytecodeOpCode.TypeOfIdentifier:
                case UnifiedBytecodeOpCode.TypeOfDynamicIdentifier:
                case UnifiedBytecodeOpCode.DeleteDynamicIdentifier:
                case UnifiedBytecodeOpCode.UnaryPlus:
                case UnifiedBytecodeOpCode.UnaryMinus:
                case UnifiedBytecodeOpCode.UnaryLogicalNot:
                case UnifiedBytecodeOpCode.UnaryBitwiseNot:
                case UnifiedBytecodeOpCode.UnaryVoid:
                case UnifiedBytecodeOpCode.ToString:
                case UnifiedBytecodeOpCode.Pop:
                case UnifiedBytecodeOpCode.CreateArray:
                case UnifiedBytecodeOpCode.ArrayPush:
                case UnifiedBytecodeOpCode.ArrayPushHole:
                case UnifiedBytecodeOpCode.ArraySpread:
                case UnifiedBytecodeOpCode.CreateObject:
                case UnifiedBytecodeOpCode.DefineObjectProperty:
                case UnifiedBytecodeOpCode.DefineComputedObjectProperty:
                case UnifiedBytecodeOpCode.LoadFunctionLiteral:
                case UnifiedBytecodeOpCode.EnsureHasName:
                case UnifiedBytecodeOpCode.Return:
                case UnifiedBytecodeOpCode.ReturnUndefined:
                case UnifiedBytecodeOpCode.Throw:
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

    private static void TryGetPrototypeOnlyBinaryDecline(
        UnifiedBytecodeInstruction instruction,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason)
    {
        declineCode = UnifiedBytecodeProductionDeclineCode.PrototypeOnlyBinaryOpcode;
        if (!TryDecodeBinaryOperator(instruction, out var binaryOperator))
        {
            declineReason =
                $"Binary opcode is prototype-only for production unified bytecode routing (unknown operator operand {instruction.Operand}).";
            return;
        }

        declineReason =
            $"Binary operator '{FormatBinaryOperator(binaryOperator)}' is prototype-only for production unified bytecode routing.";
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
