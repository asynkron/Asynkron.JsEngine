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
            ImmutableArray<UnifiedBytecodeScopeDescriptor>.Empty);
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

            if (instruction is ForInInitInstruction or ForInMoveNextInstruction)
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.ForInDriverStateDependency;
                declineReason =
                    "for-in driver state instructions are not eligible for production unified bytecode routing.";
                return true;
            }

            if (instruction is ArrayDestructuringInitInstruction or ArrayDestructuringElementInstruction or
                ArrayDestructuringRestInstruction or ArrayDestructuringCloseInstruction)
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.DestructuringDependency;
                declineReason =
                    "Array destructuring driver state instructions are not eligible for production unified bytecode routing.";
                return true;
            }

            if (instruction is EnterWithInstruction or LeaveWithInstruction)
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                declineReason = "with environments are not eligible for production unified bytecode routing.";
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
        var operationCount = program.OperationCount;
        var identifierConstants = program.IdentifierConstants.AsSpan();
        var stringConstants = program.StringConstants.AsSpan();
        var isCallTargetPreparationCandidate = TryIsFirstBoundaryCallTargetPreparationCandidate(
            program,
            identifierConstants,
            stringConstants,
            activationSlots);
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
                        declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                        declineReason =
                            $"Identifier call target '{callIdentifier.Name.Name}' requires dynamic lookup and is not eligible for production unified bytecode routing.";
                        return true;
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

                    declineCode = UnifiedBytecodeProductionDeclineCode.CallDependency;
                    declineReason =
                        "Member call-target preparation is outside the first production invocation boundary.";
                    return true;

                case ExpressionOpKind.Call:
                    if (isCallTargetPreparationCandidate)
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.CallDependency;
                    if (operation.SpreadMaskConstantIndex >= 0)
                    {
                        declineReason =
                            "Spread call arguments are not eligible for production unified bytecode routing.";
                        return true;
                    }

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
                case ExpressionOpKind.SuperConstruct:
                case ExpressionOpKind.LoadNamedSuperCallTarget:
                case ExpressionOpKind.LoadComputedSuperCallTarget:
                    declineCode = UnifiedBytecodeProductionDeclineCode.CallDependency;
                    declineReason =
                        "Construct and super call semantics are not eligible for production unified bytecode routing.";
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
                        declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                        declineReason = $"typeof identifier '{typeOfIdentifier.Name.Name}' requires dynamic lookup and is not eligible for production unified bytecode routing.";
                        return true;
                    }

                    break;

                case ExpressionOpKind.GetNamedProperty:
                    if (operation.IsOptional || operation.ShortCircuitOnNullishTarget)
                    {
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

                    if (TryIsFirstBoundaryNamedCompoundPropertyWriteCandidate(program, identifierConstants, activationSlots))
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
                        declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                        declineReason =
                            "Optional-chain computed property reads are outside the first production property-read boundary.";
                        return true;
                    }

                    if (TryIsFirstBoundaryComputedPropertyReadCandidate(program, identifierConstants, activationSlots))
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

                case ExpressionOpKind.JumpIfNullish:
                case ExpressionOpKind.JumpIfShortCircuited:
                    declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                    declineReason =
                        "Optional-chain short-circuiting is outside the first production property-read boundary.";
                    return true;

                case ExpressionOpKind.LoadFunctionLiteral:
                case ExpressionOpKind.LoadClassLiteral:
                    declineCode = UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency;
                    declineReason =
                        "Function/class literal values are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.ArraySpread:
                case ExpressionOpKind.DefineObjectMethod:
                case ExpressionOpKind.DefineComputedObjectMethod:
                case ExpressionOpKind.DefineObjectAccessor:
                case ExpressionOpKind.DefineComputedObjectAccessor:
                case ExpressionOpKind.ObjectSpread:
                    declineCode = UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency;
                    declineReason =
                        "Literal spread, object methods, and object accessors are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.DefineObjectProperty:
                    if (operation.GetString(stringConstants).IsPrivateName())
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.PrivateFieldDependency;
                        declineReason =
                            "Private-field expressions are not eligible for production unified bytecode routing.";
                        return true;
                    }

                    if (operation.AllowNameInference)
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency;
                        declineReason =
                            "Object literal name inference is not eligible for production unified bytecode routing.";
                        return true;
                    }

                    break;

                case ExpressionOpKind.DefineComputedObjectProperty:
                    if (operation.AllowNameInference)
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency;
                        declineReason =
                            "Computed object literal name inference is not eligible for production unified bytecode routing.";
                        return true;
                    }

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

    private static bool IsSupportedPushEnvironment(
        PushEnvironmentInstruction instruction,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>>? flatSlotMappings)
    {
        if (!instruction.PerIterationBindings.IsDefaultOrEmpty ||
            instruction.ScopeId < 0 ||
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
        if (program.OperationCount is not (2 or 3))
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

    private static bool TryIsFirstBoundaryCallTargetPreparationCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 2)
        {
            return false;
        }

        var callIndex = program.OperationCount - 1;
        var call = program.GetOperation(callIndex);
        if (call.Kind != ExpressionOpKind.Call ||
            !call.HasExplicitThis ||
            call.IsDirectEval ||
            call.SpreadMaskConstantIndex >= 0)
        {
            return false;
        }

        var firstOperation = program.GetOperation(0);
        if (firstOperation.Kind == ExpressionOpKind.LoadIdentifierCallTarget)
        {
            return !firstOperation.IsArguments &&
                   TryGetActivationResolvedIdentifier(firstOperation, identifierConstants, activationSlots) &&
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
                       namedCallTargetIndex) &&
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
                       keyIndex) &&
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
        int endExclusive)
    {
        if (endExclusive is < 1 or > 3)
        {
            return false;
        }

        var firstOperation = program.GetOperation(0);
        if (!TryGetActivationResolvedIdentifier(firstOperation, identifierConstants, activationSlots))
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
        var callIndex = program.OperationCount - 1;
        if (callIndex - argsStartIndex != call.ArgumentCount)
        {
            return false;
        }

        for (var operationIndex = argsStartIndex; operationIndex < callIndex; operationIndex++)
        {
            if (!IsSimpleOperand(program.GetOperation(operationIndex), identifierConstants, activationSlots))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryIsFirstBoundaryNamedCompoundPropertyWriteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount != 6)
        {
            return false;
        }

        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        var duplicateTarget = program.GetOperation(1);
        var propertyRead = program.GetOperation(2);
        var rhs = program.GetOperation(3);
        var binary = program.GetOperation(4);
        var propertyWrite = program.GetOperation(5);
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

        var propertyName = propertyRead.GetString(program.StringConstants.AsSpan());
        return !propertyName.IsPrivateName() &&
               propertyName == propertyWrite.GetString(program.StringConstants.AsSpan()) &&
               IsSimpleOperand(rhs, identifierConstants, activationSlots);
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
        if (program.OperationCount == 3)
        {
            var propertyWrite = program.GetOperation(2);
            return propertyWrite.Kind == ExpressionOpKind.SetNamedProperty &&
                   !propertyWrite.GetString(program.StringConstants.AsSpan()).IsPrivateName() &&
                   !propertyWrite.AllowNameInference &&
                   TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots) &&
                   IsSimpleOperand(program.GetOperation(1), identifierConstants, activationSlots);
        }

        if (program.OperationCount != 4)
        {
            return false;
        }

        var computedWrite = program.GetOperation(3);
        return computedWrite.Kind == ExpressionOpKind.SetComputedProperty &&
               !computedWrite.AllowNameInference &&
               TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots) &&
               IsSimpleOperand(program.GetOperation(1), identifierConstants, activationSlots) &&
               IsSimpleOperand(program.GetOperation(2), identifierConstants, activationSlots);
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

            case EvaluateAndDiscardInstruction { ExpressionProgram: { } expressionProgram }:
                program = expressionProgram;
                return true;

            case ThrowInstruction { AwaitedProgram: null, ThrowProgram: { } throwProgram }:
                program = throwProgram;
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

        return activationSlots.SlotMap.ContainsKey(identifier.Name);
    }

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
                case UnifiedBytecodeOpCode.JumpIfFalse:
                case UnifiedBytecodeOpCode.PushEnvironment:
                case UnifiedBytecodeOpCode.PopEnvironment:
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
                case UnifiedBytecodeOpCode.LoadThis:
                case UnifiedBytecodeOpCode.LoadNewTarget:
                case UnifiedBytecodeOpCode.LoadLiteral:
                case UnifiedBytecodeOpCode.PrepareIdentifierCallTarget:
                case UnifiedBytecodeOpCode.PrepareNamedCallTarget:
                case UnifiedBytecodeOpCode.PrepareComputedCallTarget:
                case UnifiedBytecodeOpCode.StoreSlot:
                case UnifiedBytecodeOpCode.RequireObjectCoercible:
                case UnifiedBytecodeOpCode.ResolvePropertyKey:
                case UnifiedBytecodeOpCode.GetNamedProperty:
                case UnifiedBytecodeOpCode.GetComputedProperty:
                case UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet:
                case UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet:
                case UnifiedBytecodeOpCode.SetNamedProperty:
                case UnifiedBytecodeOpCode.SetComputedProperty:
                case UnifiedBytecodeOpCode.UpdateNamedProperty:
                case UnifiedBytecodeOpCode.UpdateComputedProperty:
                case UnifiedBytecodeOpCode.TypeOf:
                case UnifiedBytecodeOpCode.TypeOfIdentifier:
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
                case UnifiedBytecodeOpCode.CreateObject:
                case UnifiedBytecodeOpCode.DefineObjectProperty:
                case UnifiedBytecodeOpCode.DefineComputedObjectProperty:
                case UnifiedBytecodeOpCode.Return:
                case UnifiedBytecodeOpCode.ReturnUndefined:
                case UnifiedBytecodeOpCode.Throw:
                case UnifiedBytecodeOpCode.CallInvocationBoundary:
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
            BinaryOperator.Equal or
            BinaryOperator.StrictEqual or
            BinaryOperator.StrictNotEqual or
            BinaryOperator.LessThan or
            BinaryOperator.LessThanOrEqual or
            BinaryOperator.GreaterThan or
            BinaryOperator.GreaterThanOrEqual;

    private static string FormatBinaryOperator(BinaryOperator binaryOperator) =>
        binaryOperator switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            BinaryOperator.Modulo => "%",
            BinaryOperator.Equal => "==",
            BinaryOperator.StrictEqual => "===",
            BinaryOperator.StrictNotEqual => "!==",
            BinaryOperator.LessThan => "<",
            BinaryOperator.LessThanOrEqual => "<=",
            BinaryOperator.GreaterThan => ">",
            BinaryOperator.GreaterThanOrEqual => ">=",
            _ => binaryOperator.ToString()
        };
}
