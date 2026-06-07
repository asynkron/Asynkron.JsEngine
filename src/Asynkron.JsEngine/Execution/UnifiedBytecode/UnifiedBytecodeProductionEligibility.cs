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
    bool AllowsOrdinaryDynamicIdentifierEnvironmentOperations = false,
    bool AllowsImplicitArgumentsObjectPropertyReadOperands = false,
    bool AllowsRootFunctionDeclarationInstructions = false,
    bool AllowsMaterializedBodyEnvironmentFunctionLiterals = false,
    bool AllowsNestedFunctionLiteralLexicalThisOrPrivateNameContext = false,
    bool IsStrict = false);

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
    private const string NestedFunctionDeclarationBoundary = "<function declaration>";
    private const string LexicalThisOrPrivateNameBoundary = "<lexical this/private name>";
    private const string PrivateNameBoundary = "<private name>";

    internal static bool ContainsOnlyImplicitArgumentsObjectDynamicIdentifierDependency(ExecutionPlan plan)
    {
        if (plan.ActivationSlots is not { } activationSlots)
        {
            return false;
        }

        var foundArgumentsDependency = false;
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
                    ExpressionOpKind.StoreIdentifier)
                {
                    if (IsImplicitArgumentsIdentifier(operation, identifierConstants, activationSlots))
                    {
                        foundArgumentsDependency = true;
                        continue;
                    }

                    continue;
                }

                if (operation.Kind is not (
                    ExpressionOpKind.LoadIdentifier or
                    ExpressionOpKind.LoadIdentifierCallTarget or
                    ExpressionOpKind.TypeOfIdentifier or
                    ExpressionOpKind.UpdateIdentifier or
                    ExpressionOpKind.DeleteIdentifier))
                {
                    continue;
                }

                if (!IsImplicitArgumentsIdentifier(operation, identifierConstants, activationSlots))
                {
                    continue;
                }

                foundArgumentsDependency = true;
            }
        }

        return foundArgumentsDependency;
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
                (HasOrdinaryDynamicExpressionDependency(program, activationSlots) ||
                 HasOrdinaryDynamicCallTargetDependency(program, activationSlots)))
            {
                return true;
            }
        }

        // Zero-depth catch/finally bodies are not part of the main plan-shape scan. They can still contain
        // a finally-return free callee whose dynamic call target enables the ordinary dynamic-name path.
        // Keep this extra pass call-target-only so catch-only free reads do not over-admit the whole body.
        if (!UnifiedBytecodeWithDepthAnalysis.TryBuildActiveWithDepths(
                plan.Instructions,
                plan.EntryPoint,
                out var exceptionRegionDepths,
                out _,
                includeZeroDepthExceptionRegions: true))
        {
            return false;
        }

        for (var instructionIndex = 0; instructionIndex < plan.Instructions.Length; instructionIndex++)
        {
            if (activeWithDepths[instructionIndex] >= 0 ||
                exceptionRegionDepths[instructionIndex] != 0 ||
                !TryGetExpressionProgram(plan.Instructions[instructionIndex], out var program))
            {
                continue;
            }

            if (HasOrdinaryDynamicCallTargetDependency(program, activationSlots))
            {
                return true;
            }
        }

        return false;
    }

    // A10 (burn-down): a FREE identifier used purely as a CALL TARGET (`helper(x)` where helper is a
    // global/free name) is an ordinary dynamic-identifier dependency for the production SYNC route — it
    // lowers to PrepareDynamicIdentifierCallTarget, which walks the threaded environment chain exactly
    // like LoadDynamicIdentifier. HasOrdinaryDynamicExpressionDependency intentionally omits
    // LoadIdentifierCallTarget (the yield* resumable walker relies on that omission), so detect free
    // call targets here so CanUseProductionUnifiedBytecodeOrdinaryDynamicNameFastPath admits a body
    // whose only dynamic dependency is a free callee (e.g. `function f(){ return helper(4); }`).
    private static bool HasOrdinaryDynamicCallTargetDependency(
        ExpressionProgram program,
        ActivationSlotShape activationSlots)
    {
        var identifierConstants = program.IdentifierConstants.AsSpan();
        for (var operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
        {
            var operation = program.GetOperation(operationIndex);
            if (operation.Kind != ExpressionOpKind.LoadIdentifierCallTarget || operation.IsArguments)
            {
                continue;
            }

            var callIdentifier = operation.GetIdentifier(identifierConstants);
            if (IsOrdinaryDynamicIdentifier(callIdentifier, activationSlots))
            {
                return true;
            }
        }

        return false;
    }

    public static UnifiedBytecodeProductionEligibilityResult Evaluate(
        ExecutionPlan plan,
        in UnifiedBytecodeProductionActivationDescriptor activation) =>
        EvaluateCore(plan, activation, isScript: false);

    public static UnifiedBytecodeProductionEligibilityResult EvaluateScript(ExecutionPlan plan) =>
        EvaluateCore(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true),
            isScript: true);

    private static UnifiedBytecodeProductionEligibilityResult EvaluateCore(
        ExecutionPlan plan,
        in UnifiedBytecodeProductionActivationDescriptor activation,
        bool isScript)
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
                activation.AllowsImplicitArgumentsObjectPropertyReadOperands,
                activation.IsStrict,
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
                activation.AllowsOrdinaryDynamicIdentifierEnvironmentOperations,
                isScript))
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

        if (TryFindResumablePlanDecline(
                plan,
                activationSlots,
                activation,
                activation.IsAsyncLike && activation.IsGenerator,
                out var declineCode,
                out var declineReason))
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(declineCode, declineReason);
        }

        if (!UnifiedBytecodeCompiler.TryCompile(
                plan,
                activation.IsAsyncLike,
                activation.IsGenerator,
                out var program,
                out var compileReason,
                // Lower free/dynamic identifier reads and free call targets to the dynamic-environment
                // opcodes (LoadDynamicIdentifier / PrepareDynamicIdentifierCallTarget). The post-compile
                // opcode allowlist (TryFindUnsupportedResumableOpcode) is the gate: any dynamic write /
                // reference opcode the compiler emits under this flag still declines there because it is
                // not on the resumable allowlist.
                allowsOrdinaryDynamicIdentifiers: true))
        {
            return UnifiedBytecodeProductionEligibilityResult.Decline(
                UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape,
                $"Plan is not eligible for resumable unified bytecode routing: {compileReason}");
        }

        if (TryFindUnsupportedResumableOpcode(program, activationSlots, out declineReason))
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
        if (!isResumable && activation.IsAsyncLike && activation.IsGenerator)
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.AsyncLikeFunction;
            declineReason = "Async-like functions are not eligible for production unified bytecode routing.";
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
        bool allowImplicitArgumentsObjectPropertyReadOperands,
        bool isStrict,
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

        // A8 (burn-down) tail-call safety boundary — STRICT-ONLY. A call expression returned from inside a
        // finally block is a tail position per spec (the finally completion overrides the protected block).
        // The production VM has NO same-function tail-call optimization, but the IR runner
        // (SyncFunctionInvoker.TryGetLegacySameFunctionTailRestartTarget) tail-call-optimizes deep STRICT
        // same-function identifier recursion onto a flat native stack — and that restart path fires for a
        // `return <call>;` inside a finally exactly as it does for a `return <call>;` in the try body (proven
        // by StrictSameFunctionTailCall_InFinallyReturnDoesNotGrowCallDepth, 1500-deep, no overflow). Routing
        // such a strict finally-return self-recursion to the VM would re-enter the native stack each iteration
        // and overflow it (uncatchable StackOverflow, crashes the host). So decline any STRICT function whose
        // finally region returns a call, keeping it on the TCO-capable IR runner. This mirrors the A9/A10
        // strict-only gate below: the restart requires strict mode, so a NON-STRICT finally-return call is
        // never tail-call-optimized anywhere and is no worse on the VM — those stay ADMITTED.
        if (isStrict && ContainsCallReturnReachableFromFinally(plan))
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.CallDependency;
            declineReason =
                "A call returned from within a finally block in a strict function is a tail position and requires the tail-call-optimizing IR runner; not eligible for production unified bytecode routing.";
            return true;
        }

        // A9/A10 tail-call safety boundary: the production VM has NO tail-call optimization. The IR runner
        // performs same-function tail-call optimization for STRICT functions (see
        // SyncFunctionInvoker.TryGetLegacySameFunctionTailRestartTarget), so a deep self-recursive tail
        // call there runs on a flat native stack. Admitting the identifier call-target cluster (A9/A10)
        // would route a strict `return f(...)` tail call to the VM, which re-enters the native call stack
        // on every iteration and overflows it for deep recursion (StackOverflow crashes the host). Decline
        // any strict function with a tail-position `return <bare-identifier call>;` so it keeps running on
        // the TCO-capable IR runner. Non-strict functions are never tail-call-optimized anywhere (the IR
        // runner's restart requires strict mode), so a non-strict `return f(...)` is no worse on the VM and
        // stays admitted; non-tail calls (statement calls, call arguments/operands) also stay admitted.
        if (isStrict && ContainsTailPositionIdentifierCallReturn(plan))
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.CallDependency;
            declineReason =
                "A strict tail-position identifier call requires the tail-call-optimizing IR runner; not eligible for production unified bytecode routing.";
            return true;
        }

        // NOTE: the former A9/A10 decline for "a continue that re-enters a loop body with a per-iteration
        // lexical const under the dynamic-name path" has been removed. That shape used to break in the
        // materialized-environment mode because a per-iteration const/let was lowered to dynamic-lexical
        // opcodes (DeclareDynamicLexical / InitializeDynamicLexical) that wrote only the call-environment
        // binding, while the read used a flat slot left in its TDZ (uninitialized) state — throwing a
        // spurious "Cannot access '<name>' before initialization". The production VM now mirrors
        // dynamic-lexical declare/init back into the bound flat slot (see
        // UnifiedBytecodeVirtualMachine.MirrorDynamicLexicalToFlatSlot), so own-scope LoadSlot reads observe
        // the initialized value and the shape routes through production correctly under the dynamic-name
        // path. Pure-slot functions were never affected (the const slot is reset directly).
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

            if (instruction is BindingVariableDeclarationInstruction { VarKind: VariableKind.AwaitUsing })
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                declineReason = "await using declarations require async-dispose settlement and are not eligible for production unified bytecode routing.";
                return true;
            }

            if (instruction is FunctionDeclarationInstruction { Descriptor: { } descriptor } &&
                FunctionCapturesActivationSlot(descriptor.Function, activationSlots, out var capturedName))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                declineReason =
                    $"Descriptor-backed block-scoped function declaration captures activation binding '{capturedName}' and is not eligible for production unified bytecode routing until the VM owns that materialized closure shape.";
                return true;
            }

            if (instruction is SimpleVariableDeclarationInstruction { VarKind: VariableKind.AwaitUsing })
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                declineReason = "await using declarations require async-dispose settlement and are not eligible for production unified bytecode routing.";
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
                    allowImplicitArgumentsObjectPropertyReadOperands &&
                    allowsDynamicIdentifiers,
                    out declineCode,
                    out declineReason,
                    // Sync production route only — A30 optional-computed-start member calls.
                    allowSyncOnlyOptionalComputedStartCalls: true))
            {
                return true;
            }
        }

        declineCode = UnifiedBytecodeProductionDeclineCode.None;
        declineReason = string.Empty;
        return false;
    }

    /// <summary>
    ///     Returns true when the plan contains a <c>return &lt;call&gt;;</c> reachable inside a finally
    ///     block. Such a return is in tail position (the finally completion overrides the protected
    ///     block), but the production VM has no tail-call optimization, so deep self-recursion through it
    ///     would overflow the native stack. These functions must run on the TCO-capable IR runner.
    /// </summary>
    private static bool ContainsCallReturnReachableFromFinally(ExecutionPlan plan)
    {
        var instructions = plan.Instructions;
        for (var i = 0; i < instructions.Length; i++)
        {
            if (instructions[i] is EnterTryInstruction { FinallyIndex: >= 0 } enterTry &&
                FinallyRegionContainsCallReturn(instructions, enterTry.FinallyIndex))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FinallyRegionContainsCallReturn(
        ImmutableArray<ExecutionInstruction> instructions,
        int finallyIndex)
    {
        var visited = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(finallyIndex);
        while (pending.Count > 0)
        {
            var index = pending.Pop();
            if ((uint)index >= (uint)instructions.Length || !visited.Add(index))
            {
                continue;
            }

            var instruction = instructions[index];

            // The finally body terminates at its EndFinally marker; do not traverse past it so that we
            // only inspect instructions that execute as part of the finally block itself.
            if (instruction is EndFinallyInstruction)
            {
                continue;
            }

            if (instruction is ReturnInstruction { AwaitedProgram: null, ReturnProgram: { } returnProgram } &&
                ExpressionProgramContainsCall(returnProgram))
            {
                return true;
            }

            switch (instruction)
            {
                case ReturnInstruction:
                case ThrowInstruction:
                    break;

                case BranchInstruction branch:
                    pending.Push(branch.ConsequentIndex);
                    pending.Push(branch.AlternateIndex);
                    break;

                case JumpInstruction jump:
                    pending.Push(jump.TargetIndex);
                    break;

                case BreakInstruction breakInstruction:
                    pending.Push(breakInstruction.TargetIndex);
                    break;

                case ContinueInstruction continueInstruction:
                    pending.Push(continueInstruction.TargetIndex);
                    break;

                case EnterTryInstruction nestedTry:
                    if (nestedTry.HandlerIndex >= 0)
                    {
                        pending.Push(nestedTry.HandlerIndex);
                    }

                    if (nestedTry.FinallyIndex >= 0)
                    {
                        pending.Push(nestedTry.FinallyIndex);
                    }

                    if (nestedTry.EndFinallyIndex >= 0)
                    {
                        pending.Push(nestedTry.EndFinallyIndex);
                    }

                    if (instruction.Next >= 0)
                    {
                        pending.Push(instruction.Next);
                    }

                    break;

                default:
                    if (instruction.Next >= 0)
                    {
                        pending.Push(instruction.Next);
                    }

                    break;
            }
        }

        return false;
    }

    private static bool ExpressionProgramContainsCall(ExpressionProgram program)
    {
        for (var i = 0; i < program.OperationCount; i++)
        {
            if (program.GetOperation(i).Kind == ExpressionOpKind.Call)
            {
                return true;
            }
        }

        return false;
    }

    // True when the plan has a non-awaited `return <expr>;` whose lowered expression contains an
    // identifier call target (LoadIdentifierCallTarget) — i.e. a `return f(...)` tail-position call by
    // name, including the conditional form `return c ? a : f(...)`. This is exactly the shape the IR
    // runner optimizes via same-function tail-call restart (for strict functions); the production VM does
    // not, so routing a strict self-recursive tail call there overflows the native stack. Member/computed
    // call targets and bare statement calls are deliberately NOT matched: the A9/A10 admission only newly
    // routes IDENTIFIER call targets, so scoping the guard to LoadIdentifierCallTarget keeps the boundary
    // minimal and leaves pre-existing admissions untouched.
    private static bool ContainsTailPositionIdentifierCallReturn(ExecutionPlan plan)
    {
        var instructions = plan.Instructions;
        for (var i = 0; i < instructions.Length; i++)
        {
            if (instructions[i] is ReturnInstruction { AwaitedProgram: null, ReturnProgram: { } returnProgram } &&
                ExpressionProgramContainsIdentifierCallTarget(returnProgram))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpressionProgramContainsIdentifierCallTarget(ExpressionProgram program)
    {
        for (var i = 0; i < program.OperationCount; i++)
        {
            var operation = program.GetOperation(i);
            if (operation.Kind == ExpressionOpKind.LoadIdentifierCallTarget && !operation.IsArguments)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindTryFinallyRegionResumableSuspension(
        ImmutableArray<ExecutionInstruction> instructions,
        int enterTryIndex,
        EnterTryInstruction enterTry,
        ActivationSlotShape activationSlots,
        out string instructionName)
    {
        // The instruction stream is threaded by Next/branch-target pointers, NOT laid out in ascending
        // index order — the catch/finally blocks routinely sit at LOWER indices than the EnterTry. The
        // previous linear `enterTryIndex+1 .. endIndex` scan silently matched nothing whenever the finally
        // index was below the EnterTry (`endIndex < enterTryIndex` bailed). A suspension (yield/await)
        // inside the FINALLY block still cannot be driven by the resumable VM's cleanup path —
        // `.return()`/`.throw()` must run a *suspending* finally, which it does not support — so decline it.
        // Suspensions in the TRY BODY and CATCH block stay admitted: the resumable try frame persists the
        // thrown value, catch-used bit, and pending finally completion across the yield/await boundary.

        var finallyBoundary = new HashSet<int> { enterTryIndex };
        if (enterTry.EndFinallyIndex >= 0)
        {
            finallyBoundary.Add(enterTry.EndFinallyIndex);
        }

        if (enterTry.LeaveTryIndex >= 0)
        {
            finallyBoundary.Add(enterTry.LeaveTryIndex);
        }

        if (CleanupBlockHasSuspension(instructions, enterTry.FinallyIndex, finallyBoundary, out instructionName))
        {
            return true;
        }

        // A try/finally nested INSIDE this try's body produces a chain of simultaneously-pending finally
        // blocks on `.return()`/`.throw()`. The resumable VM's cleanup path runs only a single pending
        // finally, so the inner+outer chaining (e.g. `try { try { yield } finally {} } finally {}`) is
        // mishandled — decline it and keep it on the IR runner. The single-level for-of iterator-close
        // finally is unaffected.
        if (enterTry.FinallyIndex >= 0 && TryBodyContainsNestedFinally(instructions, enterTry))
        {
            instructionName = "nested try/finally cleanup chain";
            return true;
        }

        // SCOPED close-finally guard for the captured/free dynamic mutation admissions. The resumable VM's
        // early-close path (`.return()`/`.throw()` while the generator is suspended at a yield protected by this
        // try) does not re-drive a user finally body — a PRE-EXISTING limitation shared by every non-empty
        // finally (property-write finallies included; those keep routing exactly as on main, so the B32
        // normal-completion pin stays green). This guard does NOT widen that limitation. It only declines
        // admitted dynamic mutations (`n = rhs`, `n++`, `n += rhs`, `n &&= rhs`) inside finally bodies when
        // `n` escapes the activation's slots, keeping early-close cleanup on the IR runner until B32 owns it.
        if (enterTry.FinallyIndex >= 0 &&
            FinallyRegionContainsFreeOrCapturedMutation(instructions, enterTry, activationSlots))
        {
            instructionName = "finally body performs a captured/free dynamic mutation whose early-close execution the resumable VM does not drive";
            return true;
        }

        return false;
    }

    private static bool FinallyRegionContainsFreeOrCapturedMutation(
        ImmutableArray<ExecutionInstruction> instructions,
        EnterTryInstruction enterTry,
        ActivationSlotShape activationSlots)
    {
        if (enterTry.FinallyIndex < 0)
        {
            return false;
        }

        var boundary = new HashSet<int>();
        if (enterTry.EndFinallyIndex >= 0)
        {
            boundary.Add(enterTry.EndFinallyIndex);
        }

        if (enterTry.LeaveTryIndex >= 0)
        {
            boundary.Add(enterTry.LeaveTryIndex);
        }

        // Walk only the finally region (from its first instruction up to EndFinally). Detect mutations whose
        // target does NOT resolve to one of this activation's slots — those lower through the dynamic
        // reference/update opcodes newly admitted by the B27/B29 dynamic-mutation slices.
        var visited = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(enterTry.FinallyIndex);
        while (pending.Count > 0)
        {
            var index = pending.Pop();
            if ((uint)index >= (uint)instructions.Length ||
                (index != enterTry.FinallyIndex && boundary.Contains(index)) ||
                !visited.Add(index))
            {
                continue;
            }

            var instruction = instructions[index];
            if (IsFreeOrCapturedMutationInstruction(instruction, activationSlots))
            {
                return true;
            }

            switch (instruction)
            {
                case BranchInstruction branch:
                    pending.Push(branch.ConsequentIndex);
                    pending.Push(branch.AlternateIndex);
                    break;
                case EnterTryInstruction nested:
                    pending.Push(nested.Next);
                    pending.Push(nested.HandlerIndex);
                    pending.Push(nested.FinallyIndex);
                    break;
                default:
                    pending.Push(instruction.Next);
                    break;
            }
        }

        return false;
    }

    private static bool IsFreeOrCapturedMutationInstruction(
        ExecutionInstruction instruction,
        ActivationSlotShape activationSlots)
    {
        return instruction switch
        {
            AssignmentSlotInstruction { TargetSymbol: { } targetSymbol, FlatSlotId: var flatSlotId } =>
                !TryResolveActivationSymbolSlot(targetSymbol, flatSlotId, activationSlots),
            IncrementSlotInstruction { TargetSymbol: { } targetSymbol, FlatSlotId: var flatSlotId } =>
                !TryResolveActivationSymbolSlot(targetSymbol, flatSlotId, activationSlots),
            CompoundAssignmentSlotInstruction { TargetSymbol: { } targetSymbol, FlatSlotId: var flatSlotId } =>
                !TryResolveActivationSymbolSlot(targetSymbol, flatSlotId, activationSlots),
            LogicalCompoundAssignmentSlotInstruction { TargetSymbol: { } targetSymbol, FlatSlotId: var flatSlotId } =>
                !TryResolveActivationSymbolSlot(targetSymbol, flatSlotId, activationSlots),
            _ => false
        };
    }

    private static bool TryBodyContainsNestedFinally(
        ImmutableArray<ExecutionInstruction> instructions,
        EnterTryInstruction enterTry)
    {
        var boundary = new HashSet<int>();
        if (enterTry.HandlerIndex >= 0)
        {
            boundary.Add(enterTry.HandlerIndex);
        }

        if (enterTry.FinallyIndex >= 0)
        {
            boundary.Add(enterTry.FinallyIndex);
        }

        if (enterTry.EndFinallyIndex >= 0)
        {
            boundary.Add(enterTry.EndFinallyIndex);
        }

        if (enterTry.LeaveTryIndex >= 0)
        {
            boundary.Add(enterTry.LeaveTryIndex);
        }

        var visited = new HashSet<int>();
        var pending = new Stack<int>();
        if (enterTry.Next >= 0)
        {
            pending.Push(enterTry.Next);
        }

        while (pending.Count > 0)
        {
            var index = pending.Pop();
            if ((uint)index >= (uint)instructions.Length || boundary.Contains(index) || !visited.Add(index))
            {
                continue;
            }

            var instruction = instructions[index];
            if (instruction is EnterTryInstruction nested && nested.FinallyIndex >= 0)
            {
                return true;
            }

            switch (instruction)
            {
                case BranchInstruction branch:
                    pending.Push(branch.ConsequentIndex);
                    pending.Push(branch.AlternateIndex);
                    break;
                case EnterTryInstruction innerTry:
                    pending.Push(innerTry.Next);
                    pending.Push(innerTry.HandlerIndex);
                    break;
                default:
                    pending.Push(instruction.Next);
                    break;
            }
        }

        return false;
    }

    private static bool CleanupBlockHasSuspension(
        ImmutableArray<ExecutionInstruction> instructions,
        int start,
        HashSet<int> boundary,
        out string instructionName)
    {
        instructionName = string.Empty;
        if (start < 0)
        {
            return false;
        }

        var visited = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var index = pending.Pop();
            if ((uint)index >= (uint)instructions.Length || boundary.Contains(index) || !visited.Add(index))
            {
                continue;
            }

            var instruction = instructions[index];
            if (InstructionCanSuspendResumableExecution(instruction))
            {
                instructionName = instruction.GetType().Name;
                return true;
            }

            switch (instruction)
            {
                case BranchInstruction branch:
                    pending.Push(branch.ConsequentIndex);
                    pending.Push(branch.AlternateIndex);
                    break;
                case EnterTryInstruction nested:
                    // A nested try inside this cleanup block: a suspension in its body OR its own
                    // catch/finally is still a suspension within the enclosing cleanup region.
                    pending.Push(nested.Next);
                    pending.Push(nested.HandlerIndex);
                    pending.Push(nested.FinallyIndex);
                    break;
                default:
                    pending.Push(instruction.Next);
                    break;
            }
        }

        return false;
    }

    private static bool InstructionCanSuspendResumableExecution(ExecutionInstruction instruction) =>
        instruction switch
        {
            YieldInstruction => true,
            YieldStarInstruction => true,
            ReturnInstruction { AwaitedProgram: not null } => true,
            _ => false
        };

    private static bool TryFindResumablePlanDecline(
        ExecutionPlan plan,
        ActivationSlotShape activationSlots,
        UnifiedBytecodeProductionActivationDescriptor activation,
        bool isAsyncGenerator,
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

        if (!TryBuildActiveScopeDepths(
                plan.Instructions,
                plan.EntryPoint,
                out var activeScopeDepths,
                out var scopeDepthReason))
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
            declineReason = scopeDepthReason;
            return true;
        }

        for (var instructionIndex = 0; instructionIndex < plan.Instructions.Length; instructionIndex++)
        {
            var instruction = plan.Instructions[instructionIndex];
            if (activeWithDepths[instructionIndex] < 0)
            {
                continue;
            }

            if (instruction is EnterWithInstruction { AwaitedProgram: not null })
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                declineReason =
                    "D3 dynamic residue: awaited with-object evaluation is not eligible for resumable unified bytecode routing.";
                return true;
            }

            // Ordinary free/dynamic identifier resolution (a free variable READ or a free function
            // CALL target that escapes this activation's slots, e.g. `yield outerVar` /
            // `yield helper(x)`) is admitted into the resumable route. Resolution runs against the
            // live closure environment threaded onto UnifiedBytecodeResumeState.CallingEnvironment
            // (#3108), which is captured at construction and stable across yield/await suspension, so a
            // resumed step observes the CURRENT value of a captured/outer binding (closure capture and
            // outer mutation between yields both resolve correctly) and an uninitialized free binding
            // still throws ReferenceError. Free dynamic plain writes are admitted through a pre-resolved
            // assignment-reference sequence whose pending AssignmentReference is persisted on
            // UnifiedBytecodeResumeState across a suspending RHS. Free dynamic compound and logical compound
            // writes are admitted for the non-awaited instruction shapes through the same reference stack.
            // Dynamic declarations remain declined by their instruction/opcode gates until their own semantics
            // are proven.
            const bool allowsDynamicIdentifiers = true;
            if (instruction is PushEnvironmentInstruction pushEnvironment)
            {
                if (activation.AllowsMaterializedBodyEnvironmentFunctionLiterals)
                {
                    declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                    declineReason =
                        "Materialized block environments across suspension are not eligible for resumable unified bytecode routing.";
                    return true;
                }

                if (!IsSupportedPushEnvironment(pushEnvironment, plan.FlatSlotMappings))
                {
                    declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                    declineReason =
                        "Only flat-slot lexical block environments are eligible for resumable unified bytecode routing.";
                    return true;
                }
            }

            if (IsUsingDeclarationInstruction(instruction) &&
                activeScopeDepths[instructionIndex] != 0)
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                declineReason =
                    "Only function-body using declarations are eligible for resumable unified bytecode routing.";
                return true;
            }

            if (!IsSupportedResumableInstruction(instruction, activationSlots, activation, out declineReason))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                return true;
            }

            if (TryFindNestedFunctionActivationCaptureDecline(
                    instruction,
                    activationSlots,
                    activation,
                    out declineReason))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                return true;
            }

            // B8a const-slot metadata lives on UnifiedBytecodeResumeState, so resolved lexical-slot writes
            // and updates no longer need a pre-VM decline here. Captured / free updates are still guarded by
            // the environment reference itself; the only special case is a captured/free update inside a
            // finally that protects a yield/await, declined below by the try/finally suspension guard.

            if (instruction is EnterTryInstruction enterTry &&
                TryFindTryFinallyRegionResumableSuspension(
                    plan.Instructions,
                    instructionIndex,
                    enterTry,
                    activationSlots,
                    out var suspendingInstructionName))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                declineReason =
                    $"Try/finally regions that contain yield or await are not eligible for resumable unified bytecode routing ({suspendingInstructionName}).";
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

            if (TryGetResumableExpressionProgram(instruction, out var program))
            {
                if (TryFindExpressionDecline(
                        program,
                        activationSlots,
                        allowsDynamicIdentifiers,
                        allowImplicitArgumentsObjectPropertyReadOperands: false,
                        out declineCode,
                        out declineReason))
                {
                    return true;
                }

                // `__debug()` introspection dependency. The engine's `__debug()` host hook captures the live
                // ENVIRONMENT chain (JsEnvironment.GetAllVariables) to report each local binding's value. A
                // resumable body keeps its own locals in flat slots, NOT as environment bindings, so a resumed
                // `__debug()` step would report those locals as absent — a semantic difference from the IR
                // runner, where the body's bindings live in the environment the hook reads. Decline any
                // resumable body that references `__debug` so it keeps its IR-runner introspection semantics.
                // This is narrow (only debug-instrumented bodies) and was the shape that previously kept such
                // bodies off the resumable route implicitly, before the awaited-declaration admission (B1/B44)
                // newly routed them.
                if (ProgramReferencesDebugIntrospection(program))
                {
                    declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                    declineReason =
                        "Resumable body references __debug() introspection, which reports environment-resident locals; it keeps its IR-runner routing.";
                    return true;
                }
            }
        }

        declineCode = UnifiedBytecodeProductionDeclineCode.None;
        declineReason = string.Empty;
        return false;
    }

    // True when the expression program loads or calls the `__debug` host introspection identifier. Resumable
    // bodies keep their own locals in flat slots, so the environment-walking `__debug()` hook would not see
    // them; declining such bodies preserves the IR-runner introspection semantics.
    private static bool ProgramReferencesDebugIntrospection(ExpressionProgram program)
    {
        var identifierConstants = program.IdentifierConstants.AsSpan();
        for (var operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
        {
            var operation = program.GetOperation(operationIndex);
            if (operation.Kind is not (ExpressionOpKind.LoadIdentifier or ExpressionOpKind.LoadIdentifierCallTarget) ||
                operation.IsArguments)
            {
                continue;
            }

            if (operation.GetIdentifier(identifierConstants).Name == Symbol.DebugIdentifier)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSupportedResumableInstruction(
        ExecutionInstruction instruction,
        ActivationSlotShape activationSlots,
        UnifiedBytecodeProductionActivationDescriptor activation,
        out string declineReason)
    {
        if (instruction is SimpleVariableDeclarationInstruction { VarKind: VariableKind.AwaitUsing } or
            BindingVariableDeclarationInstruction { VarKind: VariableKind.AwaitUsing })
        {
            declineReason =
                "await using declarations require async-dispose settlement and are not eligible for resumable unified bytecode routing.";
            return false;
        }

        if (instruction is SimpleVariableDeclarationInstruction { VarKind: VariableKind.Using } simpleUsing &&
            !activationSlots.SlotMap.ContainsKey(simpleUsing.TargetSymbol))
        {
            declineReason =
                "Only function-body using declarations are eligible for resumable unified bytecode routing.";
            return false;
        }

        if (instruction is BindingVariableDeclarationInstruction { VarKind: VariableKind.Using } bindingUsing &&
            !IsActivationScopeDeclarationBindingTarget(bindingUsing.TargetProgram, activationSlots))
        {
            declineReason =
                "Only function-body using declarations are eligible for resumable unified bytecode routing.";
            return false;
        }

        switch (instruction)
        {
            case ClassDeclarationInstruction classDeclaration:
                return IsResumableClassDeclaration(
                    classDeclaration.Descriptor,
                    activationSlots,
                    out declineReason);
            // B36 narrow slice: function-scoped declarations lower as no-op IR records because the
            // resumable invoker has already populated their flat slots during activation setup. The
            // activation flag is set only after the invoker proves every direct root declaration is
            // non-capturing and slot-mapped; descriptor-backed block declarations remain declined by A43.
            case FunctionDeclarationInstruction { Descriptor: null } when activation.AllowsRootFunctionDeclarationInstructions:
            case SimpleVariableDeclarationInstruction { AwaitedProgram: null, InitializerProgram: { } }:
            case SimpleVariableDeclarationInstruction { AwaitedProgram: null, InitializerProgram: null }:
            // `var x = await p` / `let y = await p` (B1): bind an AWAITED value into a flat slot and read it
            // back across the suspension. The instruction carries an AwaitedProgram (the operand of the
            // `await`) and InitializerProgram is null. The compiler lowers it as `<awaited ops>` ->
            // AwaitValue -> InitializeSlot, mirroring the already-admitted awaited IteratorInit / ForInInit
            // (`<awaited ops>` -> AwaitValue -> consuming op). AwaitValue suspends the body and, on resume,
            // pushes the settled value onto UnifiedBytecodeResumeState.OperandStack (the stable backing store
            // restored across suspension); InitializeSlot then pops it into the declaration's flat slot. The
            // store happens AFTER the suspension completes, so a later LoadSlot reads the correct value, and a
            // rejected promise surfaces as the resumable Throw step from AwaitValue. No new opcode and no
            // allowlist change: AwaitValue and InitializeSlot are both already admitted.
            case SimpleVariableDeclarationInstruction { AwaitedProgram: not null, InitializerProgram: null }:
            // `let [a,b] = await p` / `const {x} = await p` (B44): bind an AWAITED value into a destructuring
            // binding target and read the bindings back across the suspension. Same lowering family as B1:
            // `<awaited ops>` -> AwaitValue -> ApplyDeclarationBindingTarget. AwaitValue suspends and pushes
            // the settled source value on resume; ApplyDeclarationBindingTarget pops it and runs the
            // (synchronous, non-suspending) destructuring against the calling environment, writing each
            // binding to its slot. The destructuring itself cannot suspend, so it always completes inside one
            // resumed step; a non-iterable / non-coercible source surfaces as the resumable Throw step. The
            // ApplyDeclarationBindingTarget opcode is admitted in the resumable allowlist (kept 1:1 with the
            // ExecuteResumable handler).
            case BindingVariableDeclarationInstruction { AwaitedProgram: not null, InitializerProgram: null }:
            // Direct function-body sync `using value = resource` lowers through the generic binding
            // declaration path even for identifier targets. The upfront target-scope gate admits only
            // activation-scope bindings, and the compiler emits DuplicateTop -> RegisterDisposable before
            // ApplyDeclarationBindingTarget stores the const binding.
            case BindingVariableDeclarationInstruction { VarKind: VariableKind.Using, AwaitedProgram: null, InitializerProgram: { } }:
            case AssignmentSlotInstruction { AwaitedProgram: null, ValueProgram: { } }:
            // Compound slot instructions with a non-static free/captured target lower through the same
            // pre-resolved dynamic AssignmentReference stack as B26 plain writes: ResolveDynamicIdentifierReference
            // -> LoadDynamicIdentifierReference -> RHS -> Binary/short-circuit -> StoreDynamicIdentifierReference.
            // StoreDynamicIdentifierReference or PopDynamicIdentifierReference balances the pending reference;
            // RHS expression payloads are screened separately before the compiler emits the sequence.
            case CompoundAssignmentSlotInstruction { AwaitedProgram: null, RhsProgram: { } }:
            case LogicalCompoundAssignmentSlotInstruction { AwaitedProgram: null, RhsProgram: { } }:
            // Slot increment / decrement (`x++`, `x--`, `++x`, `--x`) over an activation slot. The
            // instruction carries no AwaitedProgram (a prefix/postfix update on a slot cannot itself
            // suspend — its operand is `slots[index]`, not a sub-expression that yields/awaits), so it
            // always runs to completion inside one resumable step and never needs operand-stack restoration
            // across a suspension. Const reassignment is enforced by the resume state's const-slot bitmap.
            case IncrementSlotInstruction:
            case EvaluateAndDiscardInstruction { ExpressionProgram: { } }:
            case BranchInstruction:
            case JumpInstruction:
            case SetCompletionValueInstruction:
            case BreakInstruction:
            case ContinueInstruction:
            case ReturnInstruction { AwaitedProgram: null }:
            case ThrowInstruction { AwaitedProgram: null, ThrowProgram: { } }:
            case YieldInstruction { AwaitedProgram: null, YieldProgram: { } or null }:
            case YieldStarInstruction { AwaitedProgram: null, IterableProgram: { } }:
            // `yield* await source` in an async generator lowers to an awaited source payload followed by
            // YieldStar. AwaitValue owns the source suspension and leaves the settled iterable on the
            // operand stack; YieldStar then consumes that iterable and reuses the existing async-generator
            // delegated-next/return/throw pending-await state.
            case YieldStarInstruction { AwaitedProgram: not null, IterableProgram: null }:
            case AwaitAndDiscardInstruction:
            case ReturnInstruction { AwaitedProgram: not null }:
            case StoreResumeValueInstruction:
            case EnterTryInstruction:
            case EnterCatchInstruction { CatchBindingProgram: null or IdentifierBindingTargetProgram }:
            // Plain `with (obj) { ... }` in resumable bodies. The awaited-object form is declined earlier by
            // the D3 gate; the non-awaited form lowers to EnterWith / LeaveWith, and ExecuteResumable persists
            // the active with environment on UnifiedBytecodeResumeState.CurrentEnvironment across suspension.
            case EnterWithInstruction { AwaitedProgram: null, ObjectProgram: { } }:
            case LeaveWithInstruction:
            case PushEnvironmentInstruction:
            case PopEnvironmentInstruction:
            case LeaveTryInstruction:
            case EndFinallyInstruction:
            case IteratorInitInstruction:
            case IteratorMoveNextInstruction:
            case IteratorCloseInstruction:
            case ForInInitInstruction:
            case ForInMoveNextInstruction:
            case ArrayDestructuringInitInstruction:
            case ArrayDestructuringElementInstruction:
            case ArrayDestructuringRestInstruction:
            case ArrayDestructuringCloseInstruction:
            case ObjectDestructuringInitInstruction:
            case ObjectDestructuringPropertyInstruction:
            case ObjectDestructuringRestInstruction:
            case ObjectDestructuringCloseInstruction:
            case BreakableEnterInstruction:
            case BreakableExitInstruction:
                declineReason = string.Empty;
                return true;
            default:
                declineReason =
                    $"Instruction '{instruction.GetType().Name}' is not eligible for resumable unified bytecode routing.";
                return false;
        }
    }

    private static bool TryFindNestedFunctionActivationCaptureDecline(
        ExecutionInstruction instruction,
        ActivationSlotShape activationSlots,
        UnifiedBytecodeProductionActivationDescriptor activation,
        out string declineReason)
    {
        if (!TryGetResumableExpressionProgram(instruction, out var program))
        {
            declineReason = string.Empty;
            return false;
        }

        if (ExpressionProgramHasScopedCapturingFunctionLiteral(
                program,
                activationSlots,
                out var scopedCapturedName))
        {
            declineReason =
                $"Nested function literal captures scoped binding '{scopedCapturedName}' and is not eligible for resumable unified bytecode routing until the resumable route materializes block environments.";
            return true;
        }

        if (!ExpressionProgramHasActivationCapturingFunctionLiteral(
                program,
                activationSlots,
                out var capturedName))
        {
            declineReason = string.Empty;
            return false;
        }

        if (capturedName == NestedFunctionDeclarationBoundary)
        {
            declineReason =
                "Nested function literal contains a function declaration and is not eligible for resumable unified bytecode routing until declaration instantiation is represented by the resumable route.";
            return true;
        }

        if (IsNestedFunctionLiteralLexicalThisOrPrivateNameBoundary(capturedName))
        {
            if (activation.AllowsNestedFunctionLiteralLexicalThisOrPrivateNameContext)
            {
                declineReason = string.Empty;
                return false;
            }

            declineReason =
                $"Nested function literal depends on {capturedName} and is not eligible for resumable unified bytecode routing until the resumable route materializes that closure context.";
            return true;
        }

        if (capturedName.Length > 0 && capturedName[0] == '<')
        {
            declineReason =
                $"Nested function literal depends on {capturedName} and is not eligible for resumable unified bytecode routing until the resumable route materializes that closure context.";
            return true;
        }

        if (activation.AllowsMaterializedBodyEnvironmentFunctionLiterals)
        {
            declineReason = string.Empty;
            return false;
        }

        declineReason =
            $"Nested function literal captures activation binding '{capturedName}' and is not eligible for resumable unified bytecode routing until the resume state owns a materialized body environment.";
        return true;
    }

    internal static bool PlanNeedsMaterializedResumableClassDeclarationEnvironment(ExecutionPlan plan)
    {
        for (var i = 0; i < plan.Instructions.Length; i++)
        {
            if (plan.Instructions[i] is ClassDeclarationInstruction { Descriptor: { } descriptor } &&
                plan.ActivationSlots is { } activationSlots &&
                IsResumableClassDeclaration(descriptor, activationSlots, out _))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool PlanNeedsMaterializedResumableBodyEnvironment(ExecutionPlan plan)
    {
        if (plan.ActivationSlots is not { } activationSlots)
        {
            return false;
        }

        foreach (var instruction in plan.Instructions)
        {
            if (instruction is EnterWithInstruction { AwaitedProgram: null, ObjectProgram: { } })
            {
                // The with-object expression is compiled before the with environment exists. If it lowers
                // to dynamic identifier ops, parameters and locals must already be visible through the
                // resumable invocation environment as well as the flat slot array.
                return true;
            }

            if (instruction is ClassDeclarationInstruction { Descriptor: { } descriptor } &&
                StaticBlockPlansNeedMaterializedResumableBodyEnvironment(
                    descriptor.ProgramCache,
                    activationSlots))
            {
                return true;
            }

            if (!TryGetResumableExpressionProgram(instruction, out var program))
            {
                continue;
            }

            if (ExpressionProgramContainsApplyBindingTarget(program))
            {
                return true;
            }

            if (ExpressionProgramHasActivationCapturingClassLiteralComputedName(
                    program,
                    activationSlots,
                    out var classLiteralCapturedName) &&
                IsMaterializedResumableBodyEnvironmentCapture(classLiteralCapturedName))
            {
                return true;
            }

            if (ExpressionProgramNeedsMaterializedBodyEnvironmentForClassLiteralFieldInitializer(
                    program,
                    activationSlots,
                    out var classLiteralFieldInitializerCapturedName) &&
                IsMaterializedResumableBodyEnvironmentCapture(classLiteralFieldInitializerCapturedName))
            {
                return true;
            }

            if (ExpressionProgramHasActivationCapturingClassLiteralCallable(
                    program,
                    activationSlots,
                    out var classLiteralCallableCapturedName) &&
                IsMaterializedResumableBodyEnvironmentCapture(classLiteralCallableCapturedName))
            {
                return true;
            }

            if (ExpressionProgramHasActivationCapturingFunctionLiteral(
                    program,
                    activationSlots,
                    out var capturedName) &&
                IsMaterializedResumableBodyEnvironmentCapture(capturedName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StaticBlockPlansNeedMaterializedResumableBodyEnvironment(
        ClassDefinitionProgramCache cache,
        ActivationSlotShape activationSlots)
    {
        if (!cache.Succeeded)
        {
            return false;
        }

        foreach (var staticBlockPlan in cache.Definition.StaticBlockPlans)
        {
            foreach (var instruction in staticBlockPlan.Instructions)
            {
                if (!TryGetExpressionProgram(instruction, out var program) ||
                    !ExpressionProgramHasActivationCapturingFunctionLiteral(
                        program,
                        activationSlots,
                        out var capturedName))
                {
                    continue;
                }

                if (capturedName != NestedFunctionDeclarationBoundary &&
                    IsMaterializedResumableBodyEnvironmentCapture(capturedName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ExpressionProgramHasActivationCapturingClassLiteralComputedName(
        ExpressionProgram program,
        ActivationSlotShape activationSlots,
        out string capturedName)
    {
        capturedName = string.Empty;
        if (program.IsEmpty)
        {
            return false;
        }

        var objectConstants = program.ObjectConstants.AsSpan();
        foreach (var operation in program.EnumerateOperations())
        {
            if (operation.Kind != ExpressionOpKind.LoadClassLiteral)
            {
                continue;
            }

            var classExpression = operation.GetObject<ClassExpression>(objectConstants);
            var cache = ((IAstCacheable<ClassDefinitionProgramCache>)classExpression.Definition).GetOrCreateCache();
            if (!cache.Succeeded)
            {
                capturedName = "<unknown>";
                return true;
            }

            if (ClassComputedNameProgramsHaveActivationCapturingFunctionLiteral(
                    cache.MemberNamePrograms,
                    activationSlots,
                    out capturedName) ||
                ClassComputedNameProgramsHaveActivationCapturingFunctionLiteral(
                    cache.FieldNamePrograms,
                    activationSlots,
                    out capturedName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpressionProgramNeedsMaterializedBodyEnvironmentForClassLiteralFieldInitializer(
        ExpressionProgram program,
        ActivationSlotShape activationSlots,
        out string capturedName)
    {
        capturedName = string.Empty;
        if (program.IsEmpty)
        {
            return false;
        }

        var objectConstants = program.ObjectConstants.AsSpan();
        foreach (var operation in program.EnumerateOperations())
        {
            if (operation.Kind != ExpressionOpKind.LoadClassLiteral)
            {
                continue;
            }

            var classExpression = operation.GetObject<ClassExpression>(objectConstants);
            var definition = classExpression.Definition;
            var cache = ((IAstCacheable<ClassDefinitionProgramCache>)definition).GetOrCreateCache();
            if (!cache.Succeeded)
            {
                capturedName = "<unknown>";
                return true;
            }

            for (var i = 0; i < cache.FieldInitializerPrograms.Length; i++)
            {
                if (cache.FieldInitializerPrograms[i] is not { } initializerProgram)
                {
                    continue;
                }

                if (definition.Fields[i].IsStatic)
                {
                    if (!ExpressionProgramCreatesClosure(initializerProgram))
                    {
                        continue;
                    }

                    capturedName = string.Empty;
                    return true;
                }

                if (ExpressionProgramReferencesActivationSlot(
                        initializerProgram,
                        activationSlots,
                        out capturedName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ExpressionProgramHasActivationCapturingClassLiteralCallable(
        ExpressionProgram program,
        ActivationSlotShape activationSlots,
        out string capturedName)
    {
        capturedName = string.Empty;
        if (program.IsEmpty)
        {
            return false;
        }

        var objectConstants = program.ObjectConstants.AsSpan();
        foreach (var operation in program.EnumerateOperations())
        {
            if (operation.Kind != ExpressionOpKind.LoadClassLiteral)
            {
                continue;
            }

            var classExpression = operation.GetObject<ClassExpression>(objectConstants);
            var cache = ((IAstCacheable<ClassDefinitionProgramCache>)classExpression.Definition).GetOrCreateCache();
            if (!cache.Succeeded)
            {
                capturedName = "<unknown>";
                return true;
            }

            if (FunctionCapturesActivationSlot(
                    classExpression.Definition.Constructor,
                    activationSlots,
                    out capturedName))
            {
                return true;
            }

            foreach (var member in classExpression.Definition.Members)
            {
                if (FunctionCapturesActivationSlot(member.Function, activationSlots, out capturedName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ClassComputedNameProgramsHaveActivationCapturingFunctionLiteral(
        ImmutableArray<ExpressionProgram?> programs,
        ActivationSlotShape activationSlots,
        out string capturedName)
    {
        capturedName = string.Empty;
        for (var i = 0; i < programs.Length; i++)
        {
            if (programs[i] is { } program &&
                ExpressionProgramHasActivationCapturingFunctionLiteral(program, activationSlots, out capturedName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMaterializedResumableBodyEnvironmentCapture(string capturedName) =>
        capturedName != NestedFunctionDeclarationBoundary &&
        (capturedName.Length == 0 || capturedName[0] != '<');

    internal static bool PlanNeedsResumableFunctionEnvironmentForDisposal(ExecutionPlan plan)
    {
        if (plan.ActivationSlots is not { } activationSlots)
        {
            return false;
        }

        if (!TryBuildActiveScopeDepths(
                plan.Instructions,
                plan.EntryPoint,
                out var activeScopeDepths,
                out _))
        {
            return false;
        }

        for (var i = 0; i < plan.Instructions.Length; i++)
        {
            switch (plan.Instructions[i])
            {
                case SimpleVariableDeclarationInstruction { VarKind: VariableKind.Using } simpleUsing
                    when activeScopeDepths[i] == 0 &&
                         activationSlots.SlotMap.ContainsKey(simpleUsing.TargetSymbol):
                    return true;
                case BindingVariableDeclarationInstruction { VarKind: VariableKind.Using } bindingUsing
                    when activeScopeDepths[i] == 0 &&
                         IsActivationScopeDeclarationBindingTarget(bindingUsing.TargetProgram, activationSlots):
                    return true;
            }
        }

        return false;
    }

    private static bool IsUsingDeclarationInstruction(ExecutionInstruction instruction) =>
        instruction is SimpleVariableDeclarationInstruction { VarKind: VariableKind.Using or VariableKind.AwaitUsing } or
            BindingVariableDeclarationInstruction { VarKind: VariableKind.Using or VariableKind.AwaitUsing };

    private static bool TryBuildActiveScopeDepths(
        ImmutableArray<ExecutionInstruction> instructions,
        int entryPoint,
        out int[] activeScopeDepths,
        out string reason)
    {
        activeScopeDepths = new int[instructions.Length];
        Array.Fill(activeScopeDepths, -1);

        if ((uint)entryPoint >= (uint)instructions.Length)
        {
            reason = "Unsupported entrypoint.";
            return false;
        }

        var pending = new Stack<(int InstructionIndex, int ScopeDepth)>();
        pending.Push((entryPoint, 0));
        while (pending.Count > 0)
        {
            var (instructionIndex, scopeDepth) = pending.Pop();
            if ((uint)instructionIndex >= (uint)instructions.Length)
            {
                reason = "Instruction flow reached an invalid target index.";
                return false;
            }

            if (scopeDepth < 0)
            {
                reason = "Instruction flow leaves a lexical environment when none is active.";
                return false;
            }

            var existingDepth = activeScopeDepths[instructionIndex];
            if (existingDepth >= 0)
            {
                if (existingDepth != scopeDepth)
                {
                    reason = "Instruction flow reaches the same instruction with inconsistent lexical-environment depth.";
                    return false;
                }

                continue;
            }

            activeScopeDepths[instructionIndex] = scopeDepth;
            var instruction = instructions[instructionIndex];
            if (!TryGetSuccessorScopeDepth(instruction, scopeDepth, out var successorScopeDepth, out reason))
            {
                return false;
            }

            switch (instruction)
            {
                case BranchInstruction branch:
                    if (!TryPushScopeDepth(branch.AlternateIndex, successorScopeDepth, instructions, pending, out reason) ||
                        !TryPushScopeDepth(branch.ConsequentIndex, successorScopeDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    break;

                case EnterTryInstruction enterTry:
                    if (!TryPushScopeDepth(enterTry.Next, successorScopeDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    if (enterTry.HandlerIndex >= 0 &&
                        !TryPushScopeDepth(enterTry.HandlerIndex, successorScopeDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    if (enterTry.FinallyIndex >= 0 &&
                        !TryPushScopeDepth(enterTry.FinallyIndex, successorScopeDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    break;

                case JumpInstruction jump:
                    if (!TryPushScopeDepth(jump.TargetIndex, successorScopeDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    break;

                case BreakInstruction breakInstruction:
                    if (!TryPushScopeDepth(breakInstruction.TargetIndex, successorScopeDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    break;

                case ContinueInstruction continueInstruction:
                    if (!TryPushScopeDepth(continueInstruction.TargetIndex, successorScopeDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    break;

                case ReturnInstruction:
                case ThrowInstruction:
                    break;

                default:
                    if (instruction.Next >= 0 &&
                        !TryPushScopeDepth(instruction.Next, successorScopeDepth, instructions, pending, out reason))
                    {
                        return false;
                    }

                    break;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryGetSuccessorScopeDepth(
        ExecutionInstruction instruction,
        int scopeDepth,
        out int successorScopeDepth,
        out string reason)
    {
        switch (instruction)
        {
            case PushEnvironmentInstruction:
            case EnterCatchInstruction:
                successorScopeDepth = scopeDepth + 1;
                reason = string.Empty;
                return true;

            case PopEnvironmentInstruction:
                if (scopeDepth == 0)
                {
                    successorScopeDepth = 0;
                    reason = "PopEnvironment instruction is not preceded by an active lexical environment.";
                    return false;
                }

                successorScopeDepth = scopeDepth - 1;
                reason = string.Empty;
                return true;

            default:
                successorScopeDepth = scopeDepth;
                reason = string.Empty;
                return true;
        }
    }

    private static bool TryPushScopeDepth(
        int instructionIndex,
        int scopeDepth,
        ImmutableArray<ExecutionInstruction> instructions,
        Stack<(int InstructionIndex, int ScopeDepth)> pending,
        out string reason)
    {
        if ((uint)instructionIndex >= (uint)instructions.Length)
        {
            reason = "Instruction flow reached an invalid target index.";
            return false;
        }

        pending.Push((instructionIndex, scopeDepth));
        reason = string.Empty;
        return true;
    }

    private static bool IsActivationScopeDeclarationBindingTarget(
        BindingTargetProgram target,
        ActivationSlotShape activationSlots)
    {
        switch (target)
        {
            case IdentifierBindingTargetProgram identifier:
                return identifier.ScopeId == activationSlots.ScopeId &&
                       identifier.SlotIndex >= 0 ||
                       activationSlots.SlotMap.TryGetValue(identifier.Name, out var activationSlotIndex) &&
                       identifier.SlotIndex == activationSlotIndex;

            case ArrayBindingTargetProgram arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    if (element.Target is { } elementTarget &&
                        !IsActivationScopeDeclarationBindingTarget(elementTarget, activationSlots))
                    {
                        return false;
                    }
                }

                return arrayBinding.RestElement is null ||
                       IsActivationScopeDeclarationBindingTarget(arrayBinding.RestElement, activationSlots);

            case ObjectBindingTargetProgram objectBinding:
                foreach (var property in objectBinding.Properties)
                {
                    if (!IsActivationScopeDeclarationBindingTarget(property.Target, activationSlots))
                    {
                        return false;
                    }
                }

                return objectBinding.RestElement is null ||
                       IsActivationScopeDeclarationBindingTarget(objectBinding.RestElement, activationSlots);

            default:
                return false;
        }
    }

    private static bool ExpressionProgramContainsApplyBindingTarget(ExpressionProgram program)
    {
        for (var operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
        {
            if (program.GetOperation(operationIndex).Kind == ExpressionOpKind.ApplyBindingTarget)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpressionProgramHasScopedCapturingFunctionLiteral(
        ExpressionProgram program,
        ActivationSlotShape activationSlots,
        out string capturedName)
    {
        capturedName = string.Empty;
        if (program.IsEmpty)
        {
            return false;
        }

        var objectConstants = program.ObjectConstants.AsSpan();
        foreach (var operation in program.EnumerateOperations())
        {
            if (operation.Kind != ExpressionOpKind.LoadFunctionLiteral)
            {
                continue;
            }

            var descriptor = operation.GetObject<FunctionLiteralDescriptor>(objectConstants);
            if (FunctionCapturesScopedBindingOutsideActivation(
                    descriptor.Function,
                    activationSlots,
                    out capturedName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FunctionCapturesScopedBindingOutsideActivation(
        FunctionExpression function,
        ActivationSlotShape outerActivationSlots,
        out string capturedName)
    {
        capturedName = string.Empty;
        var cache = ((IAstCacheable<ExecutionPlanCache>)function).GetOrCreateCache();
        if (!cache.Succeeded || cache.Plan is not { } plan)
        {
            capturedName = "<unknown>";
            return true;
        }

        if (plan.ActivationSlots is not { } nestedActivationSlots)
        {
            capturedName = "<unknown>";
            return true;
        }

        foreach (var instruction in plan.Instructions)
        {
            if (TryGetExpressionProgram(instruction, out var nestedProgram) &&
                ExpressionProgramReferencesScopedBindingOutsideActivation(
                    nestedProgram,
                    nestedActivationSlots,
                    outerActivationSlots,
                    out capturedName))
            {
                return true;
            }

            if (instruction is FunctionDeclarationInstruction { Descriptor: { } descriptor } &&
                FunctionCapturesScopedBindingOutsideActivation(
                    descriptor.Function,
                    outerActivationSlots,
                    out capturedName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpressionProgramReferencesScopedBindingOutsideActivation(
        ExpressionProgram program,
        ActivationSlotShape nestedActivationSlots,
        ActivationSlotShape outerActivationSlots,
        out string capturedName)
    {
        capturedName = string.Empty;
        if (program.IsEmpty)
        {
            return false;
        }

        var identifierConstants = program.IdentifierConstants.AsSpan();
        var objectConstants = program.ObjectConstants.AsSpan();
        foreach (var operation in program.EnumerateOperations())
        {
            if (operation.Kind == ExpressionOpKind.LoadFunctionLiteral)
            {
                var descriptor = operation.GetObject<FunctionLiteralDescriptor>(objectConstants);
                if (FunctionCapturesScopedBindingOutsideActivation(
                        descriptor.Function,
                        outerActivationSlots,
                        out capturedName))
                {
                    return true;
                }

                continue;
            }

            if (!TryGetIdentifierDependency(operation, identifierConstants, out var identifier) ||
                IsScopedBindingInActivation(identifier, nestedActivationSlots) ||
                IsScopedBindingInActivation(identifier, outerActivationSlots))
            {
                continue;
            }

            if (identifier.ScopeId >= 0 || identifier.FlatSlotId >= 0)
            {
                capturedName = identifier.Name.Name;
                return true;
            }
        }

        return false;
    }

    private static bool IsScopedBindingInActivation(
        IdentifierOperand identifier,
        ActivationSlotShape activationSlots) =>
        identifier.ScopeId == activationSlots.ScopeId ||
        identifier.FlatSlotId < 0 && activationSlots.SlotMap.ContainsKey(identifier.Name);

    internal static bool PlanNeedsNestedFunctionLiteralLexicalThisOrPrivateNameContext(ExecutionPlan plan)
    {
        if (plan.ActivationSlots is not { } activationSlots)
        {
            return false;
        }

        foreach (var instruction in plan.Instructions)
        {
            if (!TryGetResumableExpressionProgram(instruction, out var program) ||
                !ExpressionProgramHasActivationCapturingFunctionLiteral(
                    program,
                    activationSlots,
                    out var capturedName))
            {
                continue;
            }

            if (IsNestedFunctionLiteralLexicalThisOrPrivateNameBoundary(capturedName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpressionProgramHasActivationCapturingFunctionLiteral(
        ExpressionProgram program,
        ActivationSlotShape activationSlots,
        out string capturedName)
    {
        capturedName = string.Empty;
        if (program.IsEmpty)
        {
            return false;
        }

        var objectConstants = program.ObjectConstants.AsSpan();
        foreach (var operation in program.EnumerateOperations())
        {
            if (operation.Kind != ExpressionOpKind.LoadFunctionLiteral)
            {
                continue;
            }

            var descriptor = operation.GetObject<FunctionLiteralDescriptor>(objectConstants);
            if (FunctionLiteralNeedsLexicalThisOrPrivateNameContext(descriptor.Function, out capturedName))
            {
                return true;
            }

            if (FunctionCapturesActivationSlot(descriptor.Function, activationSlots, out capturedName))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool FunctionLiteralNeedsLexicalThisOrPrivateNameContext(
        FunctionExpression function,
        out string capturedName)
    {
        capturedName = string.Empty;
        if (DirectFunctionDeclarationsNeedLexicalThisOrPrivateNameContext(function, out capturedName))
        {
            return true;
        }

        var cache = ((IAstCacheable<ExecutionPlanCache>)function).GetOrCreateCache();
        if (!cache.Succeeded || cache.Plan is not { } plan)
        {
            capturedName = "<unknown>";
            return true;
        }

        foreach (var instruction in plan.Instructions)
        {
            if (TryGetExpressionProgram(instruction, out var program) &&
                ExpressionProgramNeedsLexicalThisOrPrivateNameContext(program, function.IsArrow))
            {
                capturedName = function.IsArrow
                    ? LexicalThisOrPrivateNameBoundary
                    : PrivateNameBoundary;
                return true;
            }

            if (instruction is FunctionDeclarationInstruction { Descriptor: { } descriptor } &&
                FunctionLiteralNeedsLexicalThisOrPrivateNameContext(descriptor.Function, out capturedName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DirectFunctionDeclarationsNeedLexicalThisOrPrivateNameContext(
        FunctionExpression function,
        out string capturedName)
    {
        foreach (var statement in function.Body.Statements)
        {
            if (statement is FunctionDeclaration functionDeclaration &&
                FunctionLiteralNeedsLexicalThisOrPrivateNameContext(functionDeclaration.Function, out capturedName))
            {
                return true;
            }
        }

        capturedName = string.Empty;
        return false;
    }

    private static bool IsNestedFunctionLiteralLexicalThisOrPrivateNameBoundary(string capturedName) =>
        string.Equals(capturedName, LexicalThisOrPrivateNameBoundary, StringComparison.Ordinal) ||
        string.Equals(capturedName, PrivateNameBoundary, StringComparison.Ordinal);

    internal static bool FunctionReferencesIdentifierNamed(
        FunctionExpression function,
        IReadOnlySet<Symbol> names,
        out string referencedName)
    {
        referencedName = string.Empty;
        if (names.Count == 0)
        {
            return false;
        }

        var cache = ((IAstCacheable<ExecutionPlanCache>)function).GetOrCreateCache();
        if (!cache.Succeeded || cache.Plan is not { } plan)
        {
            referencedName = "<unknown>";
            return true;
        }

        foreach (var instruction in plan.Instructions)
        {
            if (TryGetExpressionProgram(instruction, out var program) &&
                ExpressionProgramReferencesIdentifierNamed(program, names, out referencedName))
            {
                return true;
            }

            if (instruction is FunctionDeclarationInstruction { Descriptor: { } descriptor } &&
                FunctionReferencesIdentifierNamed(descriptor.Function, names, out referencedName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpressionProgramReferencesIdentifierNamed(
        ExpressionProgram program,
        IReadOnlySet<Symbol> names,
        out string referencedName)
    {
        referencedName = string.Empty;
        if (program.IsEmpty)
        {
            return false;
        }

        var identifierConstants = program.IdentifierConstants.AsSpan();
        var objectConstants = program.ObjectConstants.AsSpan();
        foreach (var operation in program.EnumerateOperations())
        {
            if (operation.Kind == ExpressionOpKind.LoadFunctionLiteral)
            {
                var descriptor = operation.GetObject<FunctionLiteralDescriptor>(objectConstants);
                if (FunctionReferencesIdentifierNamed(descriptor.Function, names, out referencedName))
                {
                    return true;
                }

                continue;
            }

            if (TryGetIdentifierDependency(operation, identifierConstants, out var identifier) &&
                names.Contains(identifier.Name))
            {
                referencedName = identifier.Name.Name;
                return true;
            }
        }

        return false;
    }

    private static bool ExpressionProgramNeedsLexicalThisOrPrivateNameContext(
        ExpressionProgram program,
        bool isArrowFunction)
    {
        if (program.IsEmpty)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        foreach (var operation in program.EnumerateOperations())
        {
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadThis:
                case ExpressionOpKind.LoadNewTarget:
                case ExpressionOpKind.LoadNamedSuperCallTarget:
                case ExpressionOpKind.LoadComputedSuperCallTarget:
                case ExpressionOpKind.EnsureSuperReference:
                case ExpressionOpKind.GetNamedSuperProperty:
                case ExpressionOpKind.GetComputedSuperProperty:
                case ExpressionOpKind.SetNamedSuperProperty:
                case ExpressionOpKind.SetComputedSuperProperty:
                case ExpressionOpKind.UpdateNamedSuperProperty:
                case ExpressionOpKind.UpdateComputedSuperProperty:
                case ExpressionOpKind.SuperConstruct:
                    if (isArrowFunction)
                    {
                        return true;
                    }

                    break;
                case ExpressionOpKind.PrivateFieldIn:
                    return true;
                case ExpressionOpKind.GetNamedProperty:
                case ExpressionOpKind.SetNamedProperty:
                case ExpressionOpKind.UpdateNamedProperty:
                case ExpressionOpKind.DeleteNamedProperty:
                    if (operation.GetString(stringConstants).IsPrivateName())
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    internal static bool FunctionCapturesActivationSlot(
        FunctionExpression function,
        ActivationSlotShape activationSlots,
        out string capturedName)
    {
        capturedName = string.Empty;
        if (DirectFunctionDeclarationsCaptureActivationSlot(function, activationSlots, out capturedName))
        {
            return true;
        }

        var cache = ((IAstCacheable<ExecutionPlanCache>)function).GetOrCreateCache();
        if (!cache.Succeeded || cache.Plan is not { } plan)
        {
            capturedName = "<unknown>";
            return true;
        }

        if (plan.ActivationSlots is not { } nestedActivationSlots)
        {
            capturedName = "<unknown>";
            return true;
        }

        foreach (var instruction in plan.Instructions)
        {
            if (TryGetExpressionProgram(instruction, out var nestedProgram) &&
                ExpressionProgramReferencesOuterActivation(
                    nestedProgram,
                    nestedActivationSlots,
                    activationSlots,
                    out capturedName))
            {
                return true;
            }

            if (instruction is FunctionDeclarationInstruction { Descriptor: { } descriptor } &&
                FunctionCapturesActivationSlot(descriptor.Function, activationSlots, out capturedName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DirectFunctionDeclarationsCaptureActivationSlot(
        FunctionExpression function,
        ActivationSlotShape activationSlots,
        out string capturedName)
    {
        foreach (var statement in function.Body.Statements)
        {
            if (statement is FunctionDeclaration functionDeclaration &&
                FunctionCapturesActivationSlot(functionDeclaration.Function, activationSlots, out capturedName))
            {
                return true;
            }
        }

        capturedName = string.Empty;
        return false;
    }

    private static bool ExpressionProgramReferencesOuterActivation(
        ExpressionProgram program,
        ActivationSlotShape nestedActivationSlots,
        ActivationSlotShape outerActivationSlots,
        out string capturedName)
    {
        capturedName = string.Empty;
        if (program.IsEmpty)
        {
            return false;
        }

        var identifierConstants = program.IdentifierConstants.AsSpan();
        var objectConstants = program.ObjectConstants.AsSpan();
        foreach (var operation in program.EnumerateOperations())
        {
            if (operation.Kind == ExpressionOpKind.LoadFunctionLiteral)
            {
                var descriptor = operation.GetObject<FunctionLiteralDescriptor>(objectConstants);
                if (FunctionCapturesActivationSlot(descriptor.Function, outerActivationSlots, out capturedName))
                {
                    return true;
                }

                continue;
            }

            if (!TryGetIdentifierDependency(operation, identifierConstants, out var identifier))
            {
                continue;
            }

            if (ResolvesToActivationSlot(identifier, nestedActivationSlots))
            {
                continue;
            }

            if (identifier.ScopeId == outerActivationSlots.ScopeId ||
                identifier.FlatSlotId < 0 && outerActivationSlots.SlotMap.ContainsKey(identifier.Name))
            {
                capturedName = identifier.Name.Name;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetIdentifierDependency(
        PackedExpressionOp operation,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        out IdentifierOperand identifier)
    {
        switch (operation.Kind)
        {
            case ExpressionOpKind.LoadIdentifier:
            case ExpressionOpKind.LoadIdentifierCallTarget:
            case ExpressionOpKind.ResolveIdentifierReference:
            case ExpressionOpKind.StoreResolvedIdentifier:
            case ExpressionOpKind.StoreIdentifier:
            case ExpressionOpKind.UpdateIdentifier:
            case ExpressionOpKind.TypeOfIdentifier:
            case ExpressionOpKind.DeleteIdentifier:
                identifier = operation.GetIdentifier(identifierConstants);
                return true;
            default:
                identifier = default;
                return false;
        }
    }

    private static bool ResolvesToActivationSlot(
        IdentifierOperand identifier,
        ActivationSlotShape activationSlots)
    {
        if (identifier.FlatSlotId >= 0)
        {
            return true;
        }

        if (identifier.ScopeId == activationSlots.ScopeId &&
            identifier.SlotIndex >= 0)
        {
            return true;
        }

        return identifier.ScopeId < 0 &&
               activationSlots.SlotMap.ContainsKey(identifier.Name);
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
            case IteratorInitInstruction { AwaitedProgram: { } awaitedIterableProgram }:
                program = awaitedIterableProgram;
                return true;
            case ForInInitInstruction { AwaitedProgram: { } awaitedObjectProgram }:
                program = awaitedObjectProgram;
                return true;
            case ArrayDestructuringInitInstruction arrayDestructuringInit:
                program = arrayDestructuringInit.SourceProgram;
                return true;
            case ObjectDestructuringInitInstruction objectDestructuringInit:
                program = objectDestructuringInit.SourceProgram;
                return true;
            case ReturnInstruction { AwaitedProgram: { } awaitedReturnProgram }:
                program = awaitedReturnProgram;
                return true;
            // B1: `var x = await p` — validate the awaited operand sub-program (the expression the `await`
            // operates on). The slot store itself adds nothing to the expression walk; the awaited program
            // is the only sub-program that can carry a declined shape.
            case SimpleVariableDeclarationInstruction { AwaitedProgram: { } awaitedDeclarationProgram }:
                program = awaitedDeclarationProgram;
                return true;
            // B44: `let [a,b] = await p` — validate the awaited operand sub-program. The destructuring
            // binding target is applied by ApplyDeclarationBindingTarget (admitted in the resumable
            // allowlist) and is not an ExpressionProgram, so only the awaited operand is walked here.
            case BindingVariableDeclarationInstruction { AwaitedProgram: { } awaitedBindingProgram }:
                program = awaitedBindingProgram;
                return true;
            case YieldStarInstruction { AwaitedProgram: null, IterableProgram: { } iterableProgram }:
                program = iterableProgram;
                return true;
            case YieldStarInstruction { AwaitedProgram: { } awaitedProgram, IterableProgram: null }:
                program = awaitedProgram;
                return true;
            default:
                return TryGetExpressionProgram(instruction, out program);
        }
    }

    private static bool TryFindUnsupportedResumableOpcode(
        UnifiedBytecodeProgram program,
        ActivationSlotShape activationSlots,
        out string declineReason)
    {
        foreach (var instruction in program.Instructions)
        {
            if (instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral)
            {
                if ((uint)instruction.Operand >= (uint)program.ClassLiteralConstants.Length)
                {
                    declineReason = "Class literal operand is outside the unified bytecode class literal constants.";
                    return true;
                }

                if (!IsResumableClassLiteral(
                        program,
                        activationSlots,
                        program.ClassLiteralConstants[instruction.Operand],
                        out declineReason))
                {
                    return true;
                }

                continue;
            }

            if (instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass)
            {
                if ((uint)instruction.Operand >= (uint)program.ClassDeclarationConstants.Length)
                {
                    declineReason =
                        "Class declaration operand is outside the unified bytecode class declaration constants.";
                    return true;
                }

                continue;
            }

            if (instruction.OpCode is
                UnifiedBytecodeOpCode.LoadSlot or
                UnifiedBytecodeOpCode.LoadLiteral or
                UnifiedBytecodeOpCode.LoadThis or
                // `new.target` (`LoadNewTarget`) inside a resumable (generator / async / async-arrow)
                // body. A pure meta-property read: the resumable handler resolves Symbol.NewTarget via a
                // single chain lookup against UnifiedBytecodeResumeState.CallingEnvironment (the closure
                // captured at construction and stable across yield/await). A generator/async function is
                // never a constructor, so its own function environment binds new.target to `undefined`;
                // an async arrow inherits it lexically through the same closure chain. The opcode pushes
                // exactly one value, carries no AwaitedProgram, and cannot itself suspend, so it always
                // runs to completion inside one resumable step with no resume-state restoration — the
                // literal twin of the sync VM's LoadNewTarget handler and the IR runner's
                // ExpressionOpKind.LoadNewTarget.
                UnifiedBytecodeOpCode.LoadNewTarget or
                // `import.meta` (B20) inside a resumable async/generator body. A pure meta-property read: the
                // resumable handler resolves the single Symbol.ImportMeta binding against the live closure
                // environment threaded onto UnifiedBytecodeResumeState.CallingEnvironment (#3108 — the captured
                // MODULE environment, stable across yield/await), returning the SAME per-module import.meta object
                // on every step including across a suspension. The opcode pushes exactly one value, carries no
                // AwaitedProgram, and cannot itself suspend, so it always runs to completion inside one resumable
                // step with no resume-state restoration. `import.meta` is only ever bound in a module environment;
                // outside a module the binding is absent and the resumable handler surfaces the same
                // ReferenceError as the sync VM via the resumable Throw step (the resumable loop carries no
                // ThrowSignal catch, so the handler sets the throw directly rather than raising one).
                UnifiedBytecodeOpCode.LoadImportMeta or
                // Tagged-template template-object materialization (B21). The opcode reads the compiled
                // TaggedTemplateDescriptor constant and resolves the per-realm template object through the
                // same GetOrCreateTemplateObject cache as the sync VM. The descriptor is the callsite identity,
                // so the same lowered callsite reuses its object while distinct parsed callsites stay distinct.
                // It pushes one value, carries no AwaitedProgram, and cannot itself suspend; substitutions may
                // suspend before/after it and are restored through the resumable operand stack.
                UnifiedBytecodeOpCode.LoadTemplateObject or
                // Template-substitution / explicit ToString coercion (B37) inside a resumable body — the per-hole
                // String(value) coercion an untagged template literal (`` `v${x}` ``) emits before concatenation.
                // Operates purely on the operand stack: the value to coerce sits on
                // UnifiedBytecodeResumeState.OperandStack and is restored across any suspension in a sibling
                // sub-expression (`` `v${yield 1}` ``), exactly like the admitted unaries. Literal twin of the sync
                // VM handler (JsOps.ToJsString); a throwing coercion (a Symbol, or a throwing
                // toString/Symbol.toPrimitive) surfaces as the resumable Throw step. Replaces the top value in
                // place, carries no AwaitedProgram, cannot itself suspend.
                UnifiedBytecodeOpCode.ToString or
                // Regex LITERAL (`/pat/flags`) inside a resumable body. A pure constant materialization:
                // the opcode reads the interned pattern string and encoded flags byte from the program and
                // builds a fresh RegExp object via RegExpHelper.CreateRegExpLiteral against the realm. It
                // carries no AwaitedProgram and pushes exactly one freshly created object — nothing it
                // produces sits on the operand stack across a suspension, so it always runs to completion
                // inside a single resumable step and needs no resume-state restoration. The resumable
                // handler is the literal twin of the sync VM's, so per-evaluation fresh-object semantics
                // (each evaluation yields a distinct RegExp with its own lastIndex) are preserved.
                UnifiedBytecodeOpCode.LoadRegexLiteral or
                UnifiedBytecodeOpCode.StoreSlot or
                // Direct function-body `using` declarations inside resumable functions. The compiler emits
                // DuplicateTop -> RegisterDisposable before InitializeSlot / ApplyDeclarationBindingTarget,
                // so the resource is registered against the forced resumable function environment while the
                // declaration value remains on the operand stack for binding storage. The invoker finalizes
                // Completed/Throw steps through DisposeCompletedResumableStep, so sync disposers run on
                // normal completion, return, and throw. Block-scope using stays declined by the instruction
                // target-scope gate until ExecuteResumable owns a persisted environment stack.
                UnifiedBytecodeOpCode.RegisterDisposable or
                // Slot increment / decrement (`x++`, `x--`, `++x`, `--x`). The opcode reads
                // `slots[index]`, checks the resume state's const-slot bitmap, computes the numeric ++/--
                // in place, and pushes the old or new value; it never touches the operand stack across a
                // suspension (an update cannot itself yield/await), so no resume-state restoration is
                // involved.
                UnifiedBytecodeOpCode.UpdateSlot or
                UnifiedBytecodeOpCode.InitializeSlot or
                // Declaration binding-target application for `let [a,b] = await p` / `const {x} = await p`
                // (B44). Reached only after an AwaitValue has settled the source onto the operand stack:
                // ApplyDeclarationBindingTarget pops that one value and runs the synchronous destructuring of
                // the lowered binding-target program against the resume state's CallingEnvironment, writing
                // each declared binding into its flat slot (synced via SyncEnvironmentToUnifiedSlots). The
                // opcode carries no AwaitedProgram and cannot itself suspend (destructuring of an in-hand
                // value is synchronous), so it always runs to completion inside one resumed step and needs no
                // operand-stack restoration; a non-iterable / non-coercible source or a throwing element
                // getter surfaces as the resumable Throw step. The ExecuteResumable switch carries the matching
                // handler (kept 1:1 with this allowlist). This opcode is admitted ONLY because the awaited
                // BindingVariableDeclarationInstruction is the only resumable shape that emits it; a plain
                // (non-awaited) destructuring inside a resumable body lowers to the ArrayDestructuring* /
                // ObjectDestructuring* opcode family instead.
                UnifiedBytecodeOpCode.ApplyBindingTarget or
                UnifiedBytecodeOpCode.ApplyDeclarationBindingTarget or
                UnifiedBytecodeOpCode.Binary or
                UnifiedBytecodeOpCode.GetNamedProperty or
                UnifiedBytecodeOpCode.GetComputedProperty or
                // Member compound/logical assignment read halves (`o.x += yield v`, or `o[k] += v`
                // after an earlier await/yield in the resumable body).
                // They are pure stack shapers: named preserves [base, oldValue], computed preserves
                // [base, key, oldValue], and the later Set*Property handler consumes the RHS while leaving
                // the assignment result. Receiver/key are already on UnifiedBytecodeResumeState.OperandStack,
                // so a generator yield in the RHS resumes against the exact LHS chosen before suspension;
                // async bodies use the same handlers after unrelated awaits in the body.
                UnifiedBytecodeOpCode.GetNamedPropertyForCompoundSet or
                UnifiedBytecodeOpCode.GetComputedPropertyForCompoundSet or
                // Property WRITES (`o.x = v`, `o[k] = v`, `this.x = v`) inside a resumable body. The
                // assignment value can suspend (`o.x = yield 1`); the base (and, for the computed form,
                // the key) sit on the operand stack across the suspension and are restored on resume
                // because UnifiedBytecodeResumeState.OperandStack is the stable backing store — the same
                // mechanism the admitted property READS already rely on. The resumable handlers reuse the
                // sync VM's SetPropertyValue helper (which ORs context.CurrentScope.IsStrict for strict
                // semantics) and translate a thrown set (e.g. a strict write to a read-only property) into
                // the resumable Throw step. Super-property reads/writes/updates use the resume state's
                // captured CallingEnvironment to resolve the live method `super` base after suspension.
                UnifiedBytecodeOpCode.SetNamedProperty or
                UnifiedBytecodeOpCode.SetComputedProperty or
                UnifiedBytecodeOpCode.EnsureSuperReference or
                UnifiedBytecodeOpCode.GetNamedSuperProperty or
                UnifiedBytecodeOpCode.GetComputedSuperProperty or
                UnifiedBytecodeOpCode.SetNamedSuperProperty or
                UnifiedBytecodeOpCode.SetComputedSuperProperty or
                UnifiedBytecodeOpCode.UpdateNamedSuperProperty or
                UnifiedBytecodeOpCode.UpdateComputedSuperProperty or
                // Property UPDATES (`o.x++`, `o[k]--`) and DELETES (`delete o.x`, `delete o[k]`) inside a
                // resumable body. Like the property writes above, these opcodes operate purely on the
                // operand stack — the base (and, for the computed form, the key) sit on
                // UnifiedBytecodeResumeState.OperandStack across any suspension in a sibling sub-expression
                // and are restored on resume. The opcodes themselves cannot suspend (no AwaitedProgram), so
                // they always run to completion inside one resumable step. The resumable handlers reuse the
                // sync VM's UpdatePropertyValue / DeleteNamedProperty / DeleteComputedProperty helpers,
                // threading the body's own strictness (state.IsStrict) so a strict update/delete of a
                // read-only / non-configurable property throws and translates to the resumable Throw step.
                UnifiedBytecodeOpCode.UpdateNamedProperty or
                UnifiedBytecodeOpCode.UpdateComputedProperty or
                UnifiedBytecodeOpCode.DeleteNamedProperty or
                UnifiedBytecodeOpCode.DeleteComputedProperty or
                // Optional chains / optional calls. Short-circuit is realized via jumps
                // (JumpIfNullishReplaceUndefined) or the short-circuit-flag column persisted on the
                // resume state (GetNamedPropertyOptional / JumpIfShortCircuited); both survive
                // yield/await suspension because the flag column is stored on UnifiedBytecodeResumeState
                // in lockstep with the operand stack. PrepareComputedOptionalCallTarget admits the
                // computed-member optional CALL (`o[k]?.()`): pop the key, load the method off the receiver,
                // short-circuit to undefined via the chain-end jump if the method is nullish — the literal
                // twin of the sync VM handler, reusing GetComputedCallTargetValue. The OPTIONAL-COMPUTED call
                // `o?.[k]()` is a DIFFERENT shape: its leading optional hop lowers to a JumpIfNullish that the
                // shared production-plan walk (TryFindResumablePlanDecline) declines as OptionalChainDependency
                // ("short-circuiting outside the first production property-read boundary"), so that program
                // never reaches this allowlist — admitting the opcode here does not change that.
                UnifiedBytecodeOpCode.GetNamedPropertyOptional or
                UnifiedBytecodeOpCode.JumpIfNullishReplaceUndefined or
                UnifiedBytecodeOpCode.JumpIfShortCircuited or
                UnifiedBytecodeOpCode.PrepareIdentifierOptionalCallTarget or
                UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget or
                UnifiedBytecodeOpCode.PrepareComputedOptionalCallTarget or
                // Free/dynamic identifier OPTIONAL call target (`freeFn?.()` where `freeFn` is module/script-
                // level or a captured outer binding). Resolves the callee by name against the live closure
                // environment threaded onto UnifiedBytecodeResumeState.CallingEnvironment (#3108, stable across
                // suspension), pushing the <thisValue, callee> pair; a nullish callee short-circuits the whole
                // call to undefined via the chain-end jump. Literal twin of the sync VM handler / the admitted
                // non-optional PrepareDynamicIdentifierCallTarget, plus the optional nullish-callee jump.
                UnifiedBytecodeOpCode.PrepareDynamicIdentifierOptionalCallTarget or
                UnifiedBytecodeOpCode.TypeOf or
                UnifiedBytecodeOpCode.TypeOfIdentifier or
                // `typeof freeVar` of a free/dynamic identifier (module/script-level or a captured outer
                // binding that escapes this activation's slots) inside a resumable body. Resolves the name
                // against the live closure environment threaded onto
                // UnifiedBytecodeResumeState.CallingEnvironment (#3108 — the same env the admitted free
                // dynamic READS / CALL targets / OPTIONAL call targets already use, stable across
                // yield/await), so a resumed step observes the CURRENT binding. `typeof` NEVER throws
                // ReferenceError: the resumable handler reuses the sync VM's TypeOfDynamicIdentifier helper,
                // which swallows the unbound-binding throw and returns "undefined" (an unbound `freeVar`
                // yields "undefined", a bound one yields its type). The opcode pushes exactly one value,
                // carries no AwaitedProgram, and cannot itself suspend, so it always runs to completion
                // inside one resumable step with no resume-state restoration. The dynamic *write* / reference
                // opcodes stay omitted (no resumable handler), so any non-typeof dynamic mutation still
                // declines back to the interpreter.
                UnifiedBytecodeOpCode.TypeOfDynamicIdentifier or
                UnifiedBytecodeOpCode.UnaryPlus or
                UnifiedBytecodeOpCode.UnaryMinus or
                UnifiedBytecodeOpCode.UnaryLogicalNot or
                UnifiedBytecodeOpCode.UnaryBitwiseNot or
                UnifiedBytecodeOpCode.UnaryVoid or
                // `#field in obj` (PrivateFieldIn) inside a resumable body. A pure boolean brand check: the
                // object operand sits on top of UnifiedBytecodeResumeState.OperandStack (pushed by a preceding
                // admitted value load) and is restored across any suspension in that sub-expression (`#x in
                // (yield o)`), exactly like the other admitted unaries. The opcode carries no AwaitedProgram and
                // cannot itself suspend, so it always runs to completion inside one resumable step. The private
                // name is resolved against context.ResolvePrivateNameKey / context.RealmState (stable across
                // yield/await), so the resumable handler is the literal twin of the sync VM's PrivateFieldIn —
                // a non-object operand throws the same TypeError (surfaced as the resumable Throw step), a
                // matching field/brand returns true, otherwise false.
                UnifiedBytecodeOpCode.PrivateFieldIn or
                UnifiedBytecodeOpCode.RequireObjectCoercible or
                UnifiedBytecodeOpCode.ResolvePropertyKey or
                UnifiedBytecodeOpCode.Pop or
                UnifiedBytecodeOpCode.DuplicateTop or
                UnifiedBytecodeOpCode.DuplicateTopTwo or
                UnifiedBytecodeOpCode.SwapTopTwo or
                UnifiedBytecodeOpCode.RotateTopThreeRight or
                UnifiedBytecodeOpCode.Jump or
                // Resumable control-flow opcodes are admitted only because ExecuteResumable owns the
                // same driver-cleanup topology as the sync VM for the currently compiled B41 loop shapes.
                UnifiedBytecodeOpCode.JumpWithDriverCleanup or
                UnifiedBytecodeOpCode.JumpIfFalse or
                UnifiedBytecodeOpCode.JumpIfShortCircuitFalse or
                UnifiedBytecodeOpCode.JumpIfShortCircuitTrue or
                UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish or
                UnifiedBytecodeOpCode.Break or
                UnifiedBytecodeOpCode.Continue or
                UnifiedBytecodeOpCode.EnterTry or
                UnifiedBytecodeOpCode.EnterCatch or
                // Dynamic `with` scopes in resumable bodies. EnterWith converts the object to a with-binding
                // environment and stores that active environment on UnifiedBytecodeResumeState.CurrentEnvironment;
                // LeaveWith restores the enclosing environment. Because dynamic reads/writes/calls and closure
                // creation resolve against the persisted current environment, a yield/await inside the with body
                // resumes with the same dynamic scope chain the sync VM would keep in its local current
                // environment. Awaited with-object evaluation remains declined at the instruction gate.
                UnifiedBytecodeOpCode.EnterWith or
                UnifiedBytecodeOpCode.LeaveWith or
                // Flat-slot lexical block scopes in resumable bodies. The plan-level gate admits only
                // scopes whose lexical slots are mapped into the resume state's flat slot array and whose
                // body does not need a materialized body environment. ExecuteResumable therefore owns the
                // TDZ reset, const-slot marking, and per-iteration copy-list behavior directly in the
                // persisted slot array, without needing an environment stack across suspension. Materialized
                // block environments remain declined before this opcode allowlist.
                UnifiedBytecodeOpCode.PushEnvironment or
                UnifiedBytecodeOpCode.PopEnvironment or
                UnifiedBytecodeOpCode.LeaveTry or
                UnifiedBytecodeOpCode.EndFinally or
                UnifiedBytecodeOpCode.Return or
                UnifiedBytecodeOpCode.ReturnUndefined or
                UnifiedBytecodeOpCode.Throw or
                // ReferenceError materialization emitted by expression bytecode scaffolding (currently
                // reachable only through synthetic tests on the resumable route because delete-super shapes
                // still decline before this opcode). The handler mirrors Throw: create the ReferenceError
                // value, route it through resumable catch/finally abrupt-completion handling, then surface the
                // throw step if no frame handles it.
                UnifiedBytecodeOpCode.ThrowReferenceError or
                // Synchronous call dispatch (non-optional `f()`, `o.m()`, `o[k]()` plus `super.m()` /
                // `super[k]()` in resumable method bodies). The super call-target opcodes resolve through
                // the live closure environment threaded on UnifiedBytecodeResumeState.CallingEnvironment,
                // matching the sync VM helpers and preserving the derived receiver as the call `this`.
                // Super-construct stays declined below: direct async/generator constructors are not legal
                // source shapes, and async arrows that could lexically inherit constructor state still decline
                // on the existing lexical-this activation gate before this opcode allowlist.
                UnifiedBytecodeOpCode.PrepareIdentifierCallTarget or
                UnifiedBytecodeOpCode.PrepareNamedCallTarget or
                UnifiedBytecodeOpCode.PrepareComputedCallTarget or
                UnifiedBytecodeOpCode.EnsureSuperReference or
                UnifiedBytecodeOpCode.PrepareNamedSuperCallTarget or
                UnifiedBytecodeOpCode.PrepareComputedSuperCallTarget or
                // Free/dynamic identifier resolution. A free variable READ (`yield outerVar`) lowers to
                // LoadDynamicIdentifier and a free function CALL target (`yield helper(x)`) lowers to
                // PrepareDynamicIdentifierCallTarget. Both resolve by name against the live closure
                // environment threaded onto UnifiedBytecodeResumeState.CallingEnvironment (#3108), which
                // is stable across suspension, so a resumed step reads the CURRENT value of the captured /
                // outer binding. The dynamic *write* / reference / typeof / delete opcodes are
                // intentionally omitted: those shapes have no resumable VM handler, so leaving them off
                // this allowlist declines them back to the interpreter.
                UnifiedBytecodeOpCode.LoadDynamicIdentifier or
                UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget or
                // Free/dynamic identifier UPDATE (`n++`, `n--`, `++n`, `--n`) where `n` is an
                // enclosing-function local or module/script-level binding that escapes this activation's
                // slots. Resolves the name against the live closure environment threaded onto
                // UnifiedBytecodeResumeState.CallingEnvironment (#3108) — captured at construction and stable
                // across yield/await — so the update mutates the SAME enclosing heap slot the admitted
                // captured READ (LoadDynamicIdentifier) observes; the binding aliases across every suspension.
                // The opcode is ATOMIC: it reads, ++/--, and writes back inside one resumable step (an update
                // expression cannot itself yield/await — its operand is the resolved binding, not a
                // sub-expression), so it never leaves a half-resolved reference on the operand stack across a
                // suspension. const-safety is enforced by the environment itself
                // (ResolveIdentifierAssignmentReference -> reference.SetValue throws the
                // `TypeError: Assignment to constant variable` for a captured `const`). Resolved lexical-slot
                // updates enforce the same semantic through the resume state's const-slot bitmap. The
                // ExecuteResumable switch carries the UpdateDynamicIdentifier handler (kept 1:1 with this
                // allowlist).
                //
                // Captured/free plain STORE (`n = v`) lowers to ResolveDynamicIdentifierReference ->
                // <RHS> -> StoreDynamicIdentifierReference. The resolved AssignmentReference is persisted on
                // UnifiedBytecodeResumeState, so a suspending RHS (`n = yield`) resumes with the exact target
                // reference selected before suspension. This preserves §13.15.2 ordering: RHS side effects
                // cannot change which binding the LHS originally resolved to. The Store handler leaves the
                // assigned value on the operand stack for the following Pop, matching the sync VM. Compound
                // and logical STORE shapes (`n += v`, `n &&= v`) use the same pending reference stack plus
                // LoadDynamicIdentifierReference and either StoreDynamicIdentifierReference or
                // PopDynamicIdentifierReference for short-circuit cleanup. The old single-shot
                // StoreDynamicIdentifier opcode has been retired; dynamic stores lower through the reference
                // pair so the assignment target is explicit.
                UnifiedBytecodeOpCode.UpdateDynamicIdentifier or
                UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference or
                UnifiedBytecodeOpCode.LoadDynamicIdentifierReference or
                UnifiedBytecodeOpCode.StoreDynamicIdentifierReference or
                UnifiedBytecodeOpCode.PopDynamicIdentifierReference or
                // Free/dynamic identifier DELETE (`delete freeVar` where `freeVar` is module/script-level or a
                // captured outer binding that escapes this activation's slots). Resolves the name against the
                // live closure environment threaded onto UnifiedBytecodeResumeState.CallingEnvironment (#3108)
                // — captured at construction and stable across yield/await — and deletes against the CURRENT
                // environment, the literal twin of the sync VM's DeleteDynamicIdentifier handler. Unlike the
                // dynamic plain/compound STORE, delete is SELF-CONTAINED: the DeleteDynamicIdentifier opcode
                // takes name + environment + isStrict and returns a bool directly, never touching the transient
                // dynamicIdentifierReferences array — so there is no pending reference for the resume state to
                // thread. It cannot itself suspend (its operand is the resolved name, not a sub-expression),
                // carries no AwaitedProgram, and pushes exactly one boolean, so it always runs to completion
                // inside one resumable step with no operand-stack restoration. Strictness is the body's own
                // (state.IsStrict): a strict-mode `delete freeVar` of an unqualified identifier is an early
                // SyntaxError that never reaches here, and a strict delete of a non-configurable global property
                // surfaces its false/throw exactly as the sync path. The ExecuteResumable switch carries the
                // matching handler (kept 1:1 with this allowlist).
                UnifiedBytecodeOpCode.DeleteDynamicIdentifier or
                UnifiedBytecodeOpCode.CallInvocationBoundary or
                // Class expression literal creation is admitted only by the B24 shape guard above.
                // Accepted class expressions materialize through the same class-definition program cache and
                // private-name machinery as the sync VM, with the live calling environment available so owned
                // field initializers and private-brand checks close over the correct lexical scope.
                UnifiedBytecodeOpCode.LoadClassLiteral or
                UnifiedBytecodeOpCode.LoadFunctionLiteral or
                UnifiedBytecodeOpCode.EnsureHasName or
                // Synchronous construct dispatch (non-optional `new C(args)`). Mirrors the admitted
                // CallInvocationBoundary (#3108): the constructor value and its simple/spread arguments are
                // lowered onto the operand stack by preceding ops in source order (a regular value load —
                // LoadSlot / LoadDynamicIdentifier / GetNamedProperty for `new ns.C()` — already allowlisted;
                // `new` carries NO dedicated Prepare*ConstructTarget opcode, so nothing extra needs admitting).
                // The boundary opcode reads `[constructor, arg0 .. arg(n-1)]` off the stack and invokes
                // [[Construct]] via ExecutePreparedConstruct with the constructor itself as new.target (per
                // `new C()` semantics), reusing the sync VM handler verbatim so this-binding, prototype wiring,
                // and the non-constructor TypeError are identical. An argument can suspend (`new C(yield 1)`,
                // `new C(o.a)` between two yields); the partially-pushed constructor and already-evaluated
                // arguments sit on UnifiedBytecodeResumeState.OperandStack, the stable backing store restored on
                // resume — exactly like the admitted call boundary. The opcode carries no AwaitedProgram and
                // cannot itself suspend, so it always runs to completion inside one resumable step; a thrown
                // constructor surfaces as the resumable Throw step (the sync handler translates ThrowSignal,
                // and the async case relies on the #3114 ThrowSignal rejection). Spread-onto-construct routes
                // through the same handler's spread branch. Super-construct stays declined: its dedicated
                // SuperConstructInvocationBoundary opcode needs the dynamic super-environment plumbing the
                // resume state does not carry, so leaving it off this allowlist keeps it on the interpreter.
                UnifiedBytecodeOpCode.ConstructInvocationBoundary or
                UnifiedBytecodeOpCode.Yield or
                UnifiedBytecodeOpCode.StoreResumeValue or
                UnifiedBytecodeOpCode.AwaitAndDiscard or
                UnifiedBytecodeOpCode.AwaitValue or
                UnifiedBytecodeOpCode.AwaitedReturn or
                UnifiedBytecodeOpCode.YieldStar or
                UnifiedBytecodeOpCode.TdzHeadInit or
                UnifiedBytecodeOpCode.IteratorInit or
                UnifiedBytecodeOpCode.IteratorMoveNext or
                UnifiedBytecodeOpCode.IteratorClose or
                UnifiedBytecodeOpCode.ForInInit or
                UnifiedBytecodeOpCode.ForInMoveNext or
                UnifiedBytecodeOpCode.ArrayDestructuringInit or
                UnifiedBytecodeOpCode.ArrayDestructuringElement or
                UnifiedBytecodeOpCode.ArrayDestructuringRest or
                UnifiedBytecodeOpCode.ArrayDestructuringClose or
                UnifiedBytecodeOpCode.ObjectDestructuringInit or
                UnifiedBytecodeOpCode.ObjectDestructuringProperty or
                UnifiedBytecodeOpCode.ObjectDestructuringRest or
                UnifiedBytecodeOpCode.ObjectDestructuringClose or
                // OBJECT literals (`{a, b: v, [k]: v, m(){}, get x(){}, set x(v){}, ...spread}`) and ARRAY
                // literals (`[a, , b, ...spread]`) inside a resumable body. Each literal is built bottom-up
                // on the operand stack: a single Create{Object,Array} pushes a FRESH receiver, then the
                // Define*/ArrayPush*/*Spread opcodes mutate that receiver in place, popping their argument(s)
                // while leaving the receiver on the stack. A sub-expression can suspend (`{a: yield 1}`,
                // `[yield 1]`); the partially-built receiver — plus any already-evaluated keys/values below
                // the suspension point — sit on UnifiedBytecodeResumeState.OperandStack, the stable backing
                // store restored on resume, exactly like the admitted property writes. Per ECMAScript a new
                // object/array is materialized on every evaluation (Create* allocates anew each step, never
                // caches), so re-entering a literal across a loop+yield yields a distinct instance. The
                // resumable handlers reuse the sync VM's Define*/ApplyObjectLiteralSpread/EnumerateSpread
                // helpers verbatim, so computed-key coercion, name inference, getter/setter ordering, own-
                // enumerable spread copy, and array-hole semantics are identical. The opcodes that can throw
                // (computed-key ToPropertyKey, a spread getter, an iterator step) surface the throw as the
                // resumable Throw step; none of them carry an AwaitedProgram, so each runs to completion
                // inside one resumable step with no resume-state restoration of its own.
                UnifiedBytecodeOpCode.CreateObject or
                UnifiedBytecodeOpCode.DefineObjectProperty or
                UnifiedBytecodeOpCode.DefineComputedObjectProperty or
                UnifiedBytecodeOpCode.DefineObjectMethod or
                UnifiedBytecodeOpCode.DefineComputedObjectMethod or
                UnifiedBytecodeOpCode.DefineObjectAccessor or
                UnifiedBytecodeOpCode.DefineComputedObjectAccessor or
                UnifiedBytecodeOpCode.ObjectSpread or
                UnifiedBytecodeOpCode.CreateArray or
                UnifiedBytecodeOpCode.ArrayPush or
                UnifiedBytecodeOpCode.ArrayPushHole or
                UnifiedBytecodeOpCode.ArraySpread)
            {
                if (instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral &&
                    !IsResumableClassLiteral(
                        program,
                        activationSlots,
                        program.ClassLiteralConstants[instruction.Operand],
                        out declineReason))
                {
                    return true;
                }

                continue;
            }

            declineReason =
                $"Unified bytecode opcode '{instruction.OpCode}' is not supported by resumable production routing.";
            return true;
        }

        declineReason = string.Empty;
        return false;
    }

    private static bool IsResumableClassLiteral(
        UnifiedBytecodeProgram program,
        ActivationSlotShape activationSlots,
        ClassExpression classExpression,
        out string declineReason)
    {
        var definition = classExpression.Definition;
        if (IsB24cPublicStaticFieldClassLiteral(definition))
        {
            declineReason = string.Empty;
            return true;
        }

        if (IsB24bcPublicStaticAndInstanceFieldClassLiteral(definition))
        {
            if (!AreB24bFieldInitializersActivationSafe(definition, activationSlots, out declineReason))
            {
                return false;
            }

            declineReason = string.Empty;
            return true;
        }

        if (ClassExtendsReadsUnifiedSlot(definition, program.SlotNames))
        {
            declineReason =
                "Class literal is outside B24: extends expressions that read resumable activation slots need a later class-definition environment slice; admitted subsets include the B24c public static-field subset.";
            return false;
        }

        if (TryAdmitB24hComputedPublicClassLiteral(
                definition,
                activationSlots,
                out var b24hCandidate,
                out declineReason))
        {
            return true;
        }

        if (b24hCandidate)
        {
            return false;
        }

        if (IsB24dStaticBlockClassLiteral(definition, out declineReason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(declineReason))
        {
            return false;
        }

        if (TryAdmitB24MixedPublicStaticMemberAndFieldClassLiteral(
                definition,
                activationSlots,
                out var mixedStaticCandidate,
                out declineReason))
        {
            return true;
        }

        if (mixedStaticCandidate)
        {
            return false;
        }

        if (TryAdmitB24PublicStaticMemberClassLiteral(
                definition,
                activationSlots,
                out var staticMemberCandidate,
                out declineReason))
        {
            return true;
        }

        if (staticMemberCandidate)
        {
            return false;
        }

        if (TryAdmitB24PublicStaticFieldExtendsClassLiteral(
                definition,
                activationSlots,
                out var staticFieldExtendsCandidate,
                out declineReason))
        {
            return true;
        }

        if (staticFieldExtendsCandidate)
        {
            return false;
        }

        if (!definition.StaticBlocks.IsDefaultOrEmpty || !definition.StaticElements.IsDefaultOrEmpty)
        {
            declineReason =
                "Class literal is outside B24: static elements remain owned by later B24 static-field/static-block slices; admitted subsets include public static fields, public static members, and static-block-only class literals.";
            return false;
        }

        if (!AreResumableB24ClassFieldsSupported(definition, activationSlots, out declineReason))
        {
            return false;
        }

        var isPrivateInstanceFieldClassLiteral = IsB24ePrivateInstanceFieldClassLiteral(definition);
        if (isPrivateInstanceFieldClassLiteral && !definition.Members.IsDefaultOrEmpty)
        {
            declineReason =
                "Class literal is outside B24e: private-field class literals with member bodies remain owned by later mixed class-member slices.";
            return false;
        }

        if (IsB24fPrivateInstanceMemberClassLiteral(definition))
        {
            if (definition.Extends is not null)
            {
                declineReason =
                    "Class literal is outside B24a and B24f: class literals with extends are not eligible for the B24f resumable private-member route.";
                return false;
            }

            if (FunctionCapturesActivationSlot(definition.Constructor, activationSlots, out var constructorCapturedName))
            {
                declineReason =
                    $"Class literal is outside B24f: constructor body captures activation binding '{constructorCapturedName}' and needs the materialized body environment route.";
                return false;
            }

            foreach (var member in definition.Members)
            {
                if (FunctionCapturesActivationSlot(member.Function, activationSlots, out var capturedName))
                {
                    declineReason =
                        $"Class literal is outside B24f: private member body captures activation binding '{capturedName}' and needs the materialized body environment route.";
                    return false;
                }
            }

            declineReason = string.Empty;
            return true;
        }

        if (IsB24gPublicInstanceAccessorClassLiteral(definition))
        {
            foreach (var member in definition.Members)
            {
                if (FunctionCapturesActivationSlot(member.Function, activationSlots, out var capturedName))
                {
                    declineReason =
                        $"Class literal is outside B24g: public accessor body captures activation binding '{capturedName}' and needs the materialized body environment route.";
                    return false;
                }
            }

            declineReason = string.Empty;
            return true;
        }

        if (!AreResumableB24ClassMembersSupported(
                definition,
                isPrivateInstanceFieldClassLiteral,
                activationSlots,
                out declineReason))
        {
            return false;
        }

        declineReason = string.Empty;
        return true;
    }

    private static bool IsResumableClassDeclaration(
        ClassDeclarationDescriptor descriptor,
        ActivationSlotShape activationSlots,
        out string declineReason)
    {
        if (!descriptor.ProgramCache.Succeeded)
        {
            declineReason =
                $"Class declaration could not lower class runtime metadata for resumable production routing: {descriptor.ProgramCache.FailureReason ?? "unknown failure"}.";
            return false;
        }

        var cache = descriptor.ProgramCache;
        var definition = cache.Definition;
        declineReason = string.Empty;
        if (cache.ExtendsProgram is { } extendsProgram)
        {
            if (!IsB36PlainExtendsClassDeclaration(
                    cache,
                    activationSlots,
                    out var plainExtendsDeclineReason) &&
                !TryAdmitB36SpecializedExtendsClassDeclaration(
                    cache,
                    activationSlots,
                    plainExtendsDeclineReason,
                    out declineReason))
            {
                return false;
            }

            if (!UnifiedBytecodeCompiler.TryCompileStandaloneExpressionProgram(
                    extendsProgram,
                    allowsDynamicIdentifiers: true,
                    out _,
                    out var extendsCompileReason))
            {
                declineReason =
                    $"Class declaration superclass expression is outside B36: {extendsCompileReason}";
                return false;
            }

            declineReason = string.Empty;
            return true;
        }

        if (TryAdmitB36StaticBlockClassDeclaration(cache, out var staticBlockCandidate, out declineReason))
        {
            return true;
        }

        if (staticBlockCandidate)
        {
            return false;
        }

        if (HasClassExpressionProgram(descriptor.ProgramCache.MemberNamePrograms) ||
            HasClassExpressionProgram(descriptor.ProgramCache.FieldNamePrograms) ||
            definition.StaticElements is { IsDefaultOrEmpty: false })
        {
            if (cache.ExtendsProgram is not null)
            {
                declineReason =
                    "Class declaration is outside B36: computed names or static elements with extends remain owned by later class-definition slices.";
                return false;
            }

            if (TryAdmitB36ComputedPublicClassDeclaration(
                    cache,
                    activationSlots,
                    out var b24hCandidate,
                    out declineReason))
            {
                return true;
            }

            if (b24hCandidate)
            {
                return false;
            }

            declineReason =
                "Class declaration is outside B36: computed names or static elements outside the B24h public computed subset remain owned by later class-definition slices.";
            return false;
        }

        declineReason = string.Empty;
        return true;
    }

    private static bool IsB36PlainExtendsClassDeclaration(
        ClassDefinitionProgramCache cache,
        ActivationSlotShape activationSlots,
        out string declineReason)
    {
        var definition = cache.Definition;
        declineReason = string.Empty;
        if (!definition.Members.IsDefaultOrEmpty ||
            !definition.Fields.IsDefaultOrEmpty)
        {
            declineReason =
                "Class declaration is outside B36: only plain extends class declarations without member or field class-definition state are admitted by the current class-definition slice.";
            return false;
        }

        if (!TryValidateB36StaticBlockClassDeclaration(cache, out declineReason))
        {
            return false;
        }

        return IsB36AdmittedPlainExtendsConstructor(
            definition.Constructor,
            activationSlots,
            out declineReason);
    }

    private static bool TryAdmitB36SpecializedExtendsClassDeclaration(
        ClassDefinitionProgramCache cache,
        ActivationSlotShape activationSlots,
        string plainExtendsDeclineReason,
        out string declineReason)
    {
        if (TryAdmitB36ComputedPublicInstanceMemberExtendsClassDeclaration(
                cache,
                activationSlots,
                out var candidate,
                out declineReason))
        {
            return true;
        }

        if (candidate)
        {
            return false;
        }

        if (TryAdmitB36PublicInstanceSuperMemberExtendsClassDeclaration(
                cache,
                activationSlots,
                out candidate,
                out declineReason))
        {
            return true;
        }

        if (candidate)
        {
            return false;
        }

        if (TryAdmitB36PublicStaticSuperMemberExtendsClassDeclaration(
                cache,
                activationSlots,
                out candidate,
                out declineReason))
        {
            return true;
        }

        if (candidate)
        {
            return false;
        }

        if (TryAdmitB36PublicInstanceSuperFieldExtendsClassDeclaration(
                cache,
                activationSlots,
                out candidate,
                out declineReason))
        {
            return true;
        }

        if (candidate)
        {
            return false;
        }

        if (TryAdmitB36PublicStaticFieldExtendsClassDeclaration(
                cache,
                activationSlots,
                out candidate,
                out declineReason))
        {
            return true;
        }

        if (candidate)
        {
            return false;
        }

        declineReason = plainExtendsDeclineReason;
        return false;
    }

    private static bool TryAdmitB36StaticBlockClassDeclaration(
        ClassDefinitionProgramCache cache,
        out bool candidate,
        out string declineReason)
    {
        var definition = cache.Definition;
        candidate = false;
        declineReason = string.Empty;

        if (definition.StaticBlockPlans.IsDefaultOrEmpty)
        {
            return false;
        }

        if (!definition.Members.IsDefaultOrEmpty || !definition.Fields.IsDefaultOrEmpty)
        {
            return false;
        }

        candidate = true;
        return TryValidateB36StaticBlockClassDeclaration(cache, out declineReason);
    }

    private static bool TryValidateB36StaticBlockClassDeclaration(
        ClassDefinitionProgramCache cache,
        out string declineReason)
    {
        var definition = cache.Definition;
        declineReason = string.Empty;
        if (definition.StaticElements.IsDefaultOrEmpty)
        {
            if (definition.StaticBlockPlans.IsDefaultOrEmpty)
            {
                return true;
            }

            declineReason =
                "Class declaration is outside B36: static block plans are missing their static element order metadata.";
            return false;
        }

        if (definition.StaticBlockPlans.IsDefaultOrEmpty ||
            definition.StaticElements.Length != definition.StaticBlockPlans.Length)
        {
            declineReason =
                "Class declaration is outside B36: static elements outside the static-block-only subset remain owned by later class-definition slices.";
            return false;
        }

        foreach (var element in definition.StaticElements)
        {
            if (element.Kind == ClassStaticElementKind.Block)
            {
                continue;
            }

            declineReason =
                "Class declaration is outside B36: static elements outside the static-block-only subset remain owned by later class-definition slices.";
            return false;
        }

        foreach (var staticBlockPlan in definition.StaticBlockPlans)
        {
            if (StaticBlockPlanContainsNestedDeclaration(staticBlockPlan))
            {
                declineReason =
                    "Class declaration is outside B36: static block contains nested declarations that need the broader materialized class-definition environment route.";
                return false;
            }

            var result = Evaluate(
                staticBlockPlan,
                new UnifiedBytecodeProductionActivationDescriptor(
                    AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true,
                    IsStrict: true));
            if (result.IsEligible)
            {
                continue;
            }

            declineReason =
                $"Class declaration static block is outside B36 production routing: {result.Reason}";
            return false;
        }

        return true;
    }

    private static bool TryAdmitB36ComputedPublicInstanceMemberExtendsClassDeclaration(
        ClassDefinitionProgramCache cache,
        ActivationSlotShape activationSlots,
        out bool candidate,
        out string declineReason)
    {
        var definition = cache.Definition;
        candidate = false;
        declineReason = string.Empty;

        if (!definition.Fields.IsDefaultOrEmpty ||
            !definition.StaticElements.IsDefaultOrEmpty ||
            !definition.StaticBlockPlans.IsDefaultOrEmpty ||
            definition.Members.IsDefaultOrEmpty)
        {
            return false;
        }

        var hasComputedInstanceMember = false;
        foreach (var member in definition.Members)
        {
            if (member.IsStatic || member.IsPrivate)
            {
                return false;
            }

            if (member.IsComputed)
            {
                hasComputedInstanceMember = true;
            }
        }

        if (!hasComputedInstanceMember)
        {
            return false;
        }

        candidate = true;
        if (!IsB36AdmittedPlainExtendsConstructor(
                definition.Constructor,
                activationSlots,
                out declineReason))
        {
            return false;
        }

        return TryAdmitB36ComputedPublicClassDeclaration(
            cache,
            activationSlots,
            out _,
            out declineReason);
    }

    private static bool TryAdmitB36PublicInstanceSuperMemberExtendsClassDeclaration(
        ClassDefinitionProgramCache cache,
        ActivationSlotShape activationSlots,
        out bool candidate,
        out string declineReason)
    {
        var definition = cache.Definition;
        candidate = false;
        declineReason = string.Empty;

        if (!definition.Fields.IsDefaultOrEmpty ||
            !definition.StaticBlockPlans.IsDefaultOrEmpty ||
            !definition.StaticElements.IsDefaultOrEmpty ||
            definition.Members.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var member in definition.Members)
        {
            if (member.IsStatic ||
                member.IsPrivate ||
                member.IsComputed ||
                !FunctionContainsSuper(member.Callable.Function))
            {
                return false;
            }
        }

        candidate = true;
        if (!IsB36AdmittedPlainExtendsConstructor(
                definition.Constructor,
                activationSlots,
                out declineReason))
        {
            return false;
        }

        return true;
    }

    private static bool TryAdmitB36PublicStaticSuperMemberExtendsClassDeclaration(
        ClassDefinitionProgramCache cache,
        ActivationSlotShape activationSlots,
        out bool candidate,
        out string declineReason)
    {
        var definition = cache.Definition;
        candidate = false;
        declineReason = string.Empty;

        if (!definition.Fields.IsDefaultOrEmpty ||
            !definition.StaticBlockPlans.IsDefaultOrEmpty ||
            !definition.StaticElements.IsDefaultOrEmpty ||
            definition.Members.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var member in definition.Members)
        {
            if (!member.IsStatic ||
                member.IsPrivate ||
                member.IsComputed ||
                !FunctionContainsSuper(member.Callable.Function))
            {
                return false;
            }
        }

        candidate = true;
        if (!IsB36AdmittedPlainExtendsConstructor(
                definition.Constructor,
                activationSlots,
                out declineReason))
        {
            return false;
        }

        return true;
    }

    private static bool TryAdmitB36PublicInstanceSuperFieldExtendsClassDeclaration(
        ClassDefinitionProgramCache cache,
        ActivationSlotShape activationSlots,
        out bool candidate,
        out string declineReason)
    {
        var definition = cache.Definition;
        candidate = false;
        declineReason = string.Empty;

        if (!definition.Members.IsDefaultOrEmpty ||
            !definition.StaticBlockPlans.IsDefaultOrEmpty ||
            !definition.StaticElements.IsDefaultOrEmpty ||
            definition.Fields.IsDefaultOrEmpty ||
            cache.FieldInitializerPrograms.IsDefaultOrEmpty ||
            cache.FieldInitializerPrograms.Length != definition.Fields.Length)
        {
            return false;
        }

        for (var i = 0; i < definition.Fields.Length; i++)
        {
            var field = definition.Fields[i];
            if (field.IsStatic ||
                field.IsPrivate ||
                field.IsComputed ||
                cache.FieldInitializerPrograms[i] is not { } initializerProgram ||
                !ExpressionProgramContainsSuper(initializerProgram))
            {
                return false;
            }
        }

        candidate = true;
        if (!IsB36AdmittedPlainExtendsConstructor(
                definition.Constructor,
                activationSlots,
                out declineReason))
        {
            return false;
        }

        for (var i = 0; i < cache.FieldInitializerPrograms.Length; i++)
        {
            if (cache.FieldInitializerPrograms[i] is not { } initializerProgram)
            {
                declineReason =
                    "Class declaration is outside B36: public super field initializer is missing its lowered expression program.";
                return false;
            }

        }

        return true;
    }

    private static bool TryAdmitB36PublicStaticFieldExtendsClassDeclaration(
        ClassDefinitionProgramCache cache,
        ActivationSlotShape activationSlots,
        out bool candidate,
        out string declineReason)
    {
        var definition = cache.Definition;
        candidate = false;
        declineReason = string.Empty;

        if (!definition.Members.IsDefaultOrEmpty ||
            !definition.StaticBlockPlans.IsDefaultOrEmpty ||
            definition.Fields.IsDefaultOrEmpty)
        {
            return false;
        }

        if (definition.StaticElements.IsDefaultOrEmpty ||
            definition.StaticElements.Length != definition.Fields.Length)
        {
            return false;
        }

        foreach (var element in definition.StaticElements)
        {
            if (element.Kind != ClassStaticElementKind.Field)
            {
                return false;
            }
        }

        foreach (var field in definition.Fields)
        {
            if (!field.IsStatic || field.IsPrivate || field.IsComputed)
            {
                return false;
            }
        }

        candidate = true;
        if (!IsB36AdmittedPlainExtendsConstructor(
                definition.Constructor,
                activationSlots,
                out declineReason))
        {
            return false;
        }

        foreach (var initializerProgram in cache.FieldInitializerPrograms)
        {
            if (initializerProgram is null)
            {
                continue;
            }

            if (UnifiedBytecodeCompiler.TryCompileStandaloneExpressionProgram(
                    initializerProgram.Value,
                    allowsDynamicIdentifiers: true,
                    out _,
                    out var initializerReason))
            {
                continue;
            }

            declineReason =
                $"Class declaration static field initializer is outside B36 production routing: {initializerReason}";
            return false;
        }

        return true;
    }

    private static bool IsB36AdmittedPlainExtendsConstructor(
        LoweredClassCallable constructor,
        ActivationSlotShape activationSlots,
        out string declineReason)
    {
        declineReason = string.Empty;
        var function = constructor.Function;
        if (function.IsDefaultDerivedConstructor)
        {
            return true;
        }

        if (function.IsAsync ||
            function.IsGenerator ||
            function.IsArrow ||
            function.IsDynamicFunctionConstructorBody ||
            !HasB36AdmittedExplicitDerivedConstructorParameters(function) ||
            constructor.PlanSeed.Plan is not { } plan ||
            !HasB36AdmittedExplicitDerivedConstructorPlanShape(plan))
        {
            declineReason =
                "Class declaration is outside B36: explicit derived constructor is outside the currently admitted public super(...) constructor shape.";
            return false;
        }

        var descriptor = FunctionCapturesActivationSlot(function, activationSlots, out _)
            ? new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: true)
            : new UnifiedBytecodeProductionActivationDescriptor();
        var result = Evaluate(plan, descriptor);
        if (result.IsEligible)
        {
            return true;
        }

        declineReason =
            $"Class declaration is outside B36: explicit derived constructor plan is outside production unified bytecode ({result.Reason}).";
        return false;
    }

    private static bool HasB36AdmittedExplicitDerivedConstructorParameters(FunctionExpression function)
    {
        for (var i = 0; i < function.Parameters.Length; i++)
        {
            var parameter = function.Parameters[i];
            if (parameter is not { Pattern: null, Name: not null })
            {
                return false;
            }

            if (parameter.IsRest)
            {
                return i == function.Parameters.Length - 1 && parameter.DefaultValue is null;
            }

            if (parameter.DefaultValue is not null and not LiteralExpression)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasB36AdmittedExplicitDerivedConstructorPlanShape(ExecutionPlan plan)
    {
        var hasSuperConstruct = false;
        foreach (var instruction in plan.Instructions)
        {
            if (!TryGetExpressionProgram(instruction, out var program))
            {
                continue;
            }

            foreach (var operation in program.EnumerateOperations())
            {
                if (operation.IsArguments)
                {
                    return false;
                }

                switch (operation.Kind)
                {
                    case ExpressionOpKind.LoadThis:
                    case ExpressionOpKind.LoadNamedSuperCallTarget:
                    case ExpressionOpKind.LoadComputedSuperCallTarget:
                    case ExpressionOpKind.EnsureSuperReference:
                        break;
                    case ExpressionOpKind.GetNamedSuperProperty:
                    case ExpressionOpKind.GetComputedSuperProperty:
                    case ExpressionOpKind.SetNamedSuperProperty:
                    case ExpressionOpKind.SetComputedSuperProperty:
                    case ExpressionOpKind.UpdateNamedSuperProperty:
                    case ExpressionOpKind.UpdateComputedSuperProperty:
                        return false;
                    case ExpressionOpKind.SuperConstruct:
                        hasSuperConstruct = true;
                        break;
                }
            }
        }

        return hasSuperConstruct;
    }

    private static bool StaticBlockPlanContainsNestedDeclaration(ExecutionPlan plan)
    {
        foreach (var instruction in plan.Instructions)
        {
            switch (instruction)
            {
                case FunctionDeclarationInstruction:
                case ClassDeclarationInstruction:
                    return true;
            }
        }

        return false;
    }

    private static bool StaticBlockPlanCreatesClosure(ExecutionPlan plan)
    {
        foreach (var instruction in plan.Instructions)
        {
            switch (instruction)
            {
                case FunctionDeclarationInstruction:
                case ClassDeclarationInstruction:
                    return true;
            }

            if (!TryGetExpressionProgram(instruction, out var program))
            {
                continue;
            }

            if (ExpressionProgramCreatesClosure(program))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpressionProgramCreatesClosure(ExpressionProgram program)
    {
        foreach (var operation in program.EnumerateOperations())
        {
            if (operation.Kind is ExpressionOpKind.LoadFunctionLiteral or ExpressionOpKind.LoadClassLiteral)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpressionProgramContainsSuper(ExpressionProgram program)
    {
        foreach (var operation in program.EnumerateOperations())
        {
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadNamedSuperCallTarget:
                case ExpressionOpKind.LoadComputedSuperCallTarget:
                case ExpressionOpKind.EnsureSuperReference:
                case ExpressionOpKind.GetNamedSuperProperty:
                case ExpressionOpKind.GetComputedSuperProperty:
                case ExpressionOpKind.SetNamedSuperProperty:
                case ExpressionOpKind.SetComputedSuperProperty:
                case ExpressionOpKind.UpdateNamedSuperProperty:
                case ExpressionOpKind.UpdateComputedSuperProperty:
                case ExpressionOpKind.SuperConstruct:
                    return true;
            }
        }

        return false;
    }

    private static bool TryAdmitB36ComputedPublicClassDeclaration(
        ClassDefinitionProgramCache cache,
        ActivationSlotShape activationSlots,
        out bool candidate,
        out string declineReason)
    {
        candidate = false;
        declineReason = string.Empty;

        var definition = cache.Definition;
        var hasComputedElement = false;
        foreach (var field in definition.Fields)
        {
            if (field.IsPrivate)
            {
                return false;
            }

            if (field.IsComputed)
            {
                hasComputedElement = true;
            }
        }

        foreach (var member in definition.Members)
        {
            if (member.IsPrivate)
            {
                return false;
            }

            if (member.IsComputed)
            {
                hasComputedElement = true;
            }
        }

        if (!hasComputedElement)
        {
            return false;
        }

        candidate = true;
        if (FunctionCapturesActivationSlot(definition.Constructor.Function, activationSlots, out var constructorCapturedName))
        {
            declineReason =
                $"Class declaration is outside B24h: constructor body captures activation binding '{constructorCapturedName}' and needs the materialized body environment route.";
            return false;
        }

        for (var i = 0; i < cache.MemberNamePrograms.Length; i++)
        {
            if (!definition.Members[i].IsComputed)
            {
                continue;
            }

            if (cache.MemberNamePrograms[i] is not { } nameProgram)
            {
                declineReason =
                    "Class declaration is outside B24h: computed member name is missing its lowered expression program.";
                return false;
            }

            if (ExpressionProgramHasUnsupportedClassComputedNameActivationDependency(
                    nameProgram,
                    activationSlots,
                    allowDirectActivationCall: true,
                    allowImmediateFunctionLiteralCall: true,
                    out var capturedName,
                    out var dependencyReason))
            {
                declineReason =
                    $"Class declaration computed member name captures activation binding '{capturedName}' through {dependencyReason} and is not supported by B24h resumable production routing until the class-definition environment route owns that dependency.";
                return false;
            }
        }

        for (var i = 0; i < cache.FieldNamePrograms.Length; i++)
        {
            if (!definition.Fields[i].IsComputed)
            {
                continue;
            }

            if (cache.FieldNamePrograms[i] is not { } nameProgram)
            {
                declineReason =
                    "Class declaration is outside B24h: computed field name is missing its lowered expression program.";
                return false;
            }

            if (ExpressionProgramHasUnsupportedClassComputedNameActivationDependency(
                    nameProgram,
                    activationSlots,
                    allowDirectActivationCall: true,
                    allowImmediateFunctionLiteralCall: true,
                    out var capturedName,
                    out var dependencyReason))
            {
                declineReason =
                    $"Class declaration computed field name captures activation binding '{capturedName}' through {dependencyReason} and is not supported by B24h resumable production routing until the class-definition environment route owns that dependency.";
                return false;
            }
        }

        for (var i = 0; i < cache.FieldInitializerPrograms.Length; i++)
        {
            if (cache.FieldInitializerPrograms[i] is { } initializerProgram &&
                ExpressionProgramReferencesActivationSlot(initializerProgram, activationSlots, out var capturedName))
            {
                declineReason =
                    $"Class declaration computed field initializer captures activation binding '{capturedName}' and is not supported by B24h resumable production routing until the resume state owns a materialized body environment.";
                return false;
            }
        }

        foreach (var member in definition.Members)
        {
            if (FunctionCapturesActivationSlot(member.Callable.Function, activationSlots, out var capturedName))
            {
                declineReason =
                    $"Class declaration computed member body captures activation binding '{capturedName}' and needs the materialized body environment route.";
                return false;
            }
        }

        return true;
    }

    private static bool HasClassExpressionProgram(ImmutableArray<ExpressionProgram?> programs)
    {
        for (var i = 0; i < programs.Length; i++)
        {
            if (programs[i] is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsB24gPublicInstanceAccessorClassLiteral(ClassDefinition definition)
    {
        if (!definition.Fields.IsDefaultOrEmpty ||
            definition.Members.IsDefaultOrEmpty ||
            definition.Members.Length == 0 ||
            !definition.StaticBlocks.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var member in definition.Members)
        {
            if (member.Kind is not (ClassMemberKind.Getter or ClassMemberKind.Setter) ||
                member.IsStatic ||
                member.IsComputed ||
                member.IsPrivate)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitB24hComputedPublicClassLiteral(
        ClassDefinition definition,
        ActivationSlotShape activationSlots,
        out bool candidate,
        out string declineReason)
    {
        candidate = false;
        declineReason = string.Empty;
        if (!definition.StaticBlocks.IsDefaultOrEmpty)
        {
            return false;
        }

        var hasComputedElement = false;
        foreach (var field in definition.Fields)
        {
            if (field.IsPrivate)
            {
                if (field.IsStatic || field.IsComputed)
                {
                    return false;
                }

                continue;
            }

            if (field.IsComputed)
            {
                if (field.ComputedName is null)
                {
                    return false;
                }

                hasComputedElement = true;
            }
        }

        foreach (var member in definition.Members)
        {
            if (member.IsPrivate)
            {
                if (member.IsStatic ||
                    member.IsComputed ||
                    member.Kind is not (
                        ClassMemberKind.Method or
                        ClassMemberKind.Getter or
                        ClassMemberKind.Setter))
                {
                    return false;
                }

                continue;
            }

            if (member.IsComputed)
            {
                if (member.ComputedName is null)
                {
                    return false;
                }

                hasComputedElement = true;
            }
        }

        if (!hasComputedElement)
        {
            return false;
        }

        candidate = true;
        var cache = ((IAstCacheable<ClassDefinitionProgramCache>)definition).GetOrCreateCache();
        if (!cache.Succeeded)
        {
            declineReason =
                $"Class literal computed-name programs could not lower for B24h resumable production routing: {cache.FailureReason ?? "unknown failure"}.";
            return false;
        }

        for (var i = 0; i < definition.Fields.Length; i++)
        {
            var field = definition.Fields[i];
            if (!field.IsPrivate)
            {
                continue;
            }

            if (field.Initializer is not null && ExpressionContainsSuper(field.Initializer))
            {
                declineReason =
                    "Class literal is outside B24h: private field initializer uses super and needs the class-definition environment route.";
                return false;
            }

        }

        if (FunctionCapturesActivationSlot(definition.Constructor, activationSlots, out var constructorCapturedName) &&
            !IsMaterializedResumableBodyEnvironmentCapture(constructorCapturedName))
        {
            declineReason =
                $"Class literal is outside B24h: constructor body captures activation binding '{constructorCapturedName}' and needs the materialized body environment route.";
            return false;
        }

        for (var i = 0; i < cache.MemberNamePrograms.Length; i++)
        {
            if (!definition.Members[i].IsComputed)
            {
                continue;
            }

            if (cache.MemberNamePrograms[i] is not { } nameProgram)
            {
                declineReason =
                    "Class literal is outside B24h: computed member name is missing its lowered expression program.";
                return false;
            }

            if (ExpressionProgramHasUnsupportedClassComputedNameActivationDependency(
                    nameProgram,
                    activationSlots,
                    allowDirectActivationCall: true,
                    allowImmediateFunctionLiteralCall: true,
                    out var capturedName,
                    out var dependencyReason))
            {
                declineReason =
                    $"Class literal computed member name captures activation binding '{capturedName}' through {dependencyReason} and is not supported by B24h resumable production routing until the class-definition environment route owns that dependency.";
                return false;
            }
        }

        for (var i = 0; i < cache.FieldNamePrograms.Length; i++)
        {
            if (!definition.Fields[i].IsComputed)
            {
                continue;
            }

            if (cache.FieldNamePrograms[i] is not { } nameProgram)
            {
                declineReason =
                    "Class literal is outside B24h: computed field name is missing its lowered expression program.";
                return false;
            }

            if (ExpressionProgramHasUnsupportedClassComputedNameActivationDependency(
                    nameProgram,
                    activationSlots,
                    allowDirectActivationCall: true,
                    allowImmediateFunctionLiteralCall: true,
                    out var capturedName,
                    out var dependencyReason))
            {
                declineReason =
                    $"Class literal computed field name captures activation binding '{capturedName}' through {dependencyReason} and is not supported by B24h resumable production routing until the class-definition environment route owns that dependency.";
                return false;
            }
        }

        foreach (var member in definition.Members)
        {
            if (!FunctionCapturesActivationSlot(member.Function, activationSlots, out var capturedName))
            {
                continue;
            }

            if (!IsMaterializedResumableBodyEnvironmentCapture(capturedName))
            {
                declineReason =
                    $"Class literal is outside B24h: computed member body captures activation binding '{capturedName}' and needs the materialized body environment route.";
                return false;
            }
        }

        return true;
    }

    private static bool ExpressionProgramHasUnsupportedClassComputedNameActivationDependency(
        ExpressionProgram program,
        ActivationSlotShape activationSlots,
        bool allowDirectActivationCall,
        bool allowImmediateFunctionLiteralCall,
        out string capturedName,
        out string dependencyReason)
    {
        capturedName = string.Empty;
        dependencyReason = string.Empty;
        if (program.IsEmpty)
        {
            return false;
        }

        var identifierConstants = program.IdentifierConstants.AsSpan();
        var objectConstants = program.ObjectConstants.AsSpan();
        var hasActivationReference = false;
        var hasCallDependency = false;
        for (var operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
        {
            var operation = program.GetOperation(operationIndex);
            if (operation.Kind == ExpressionOpKind.LoadFunctionLiteral)
            {
                var descriptor = operation.GetObject<FunctionLiteralDescriptor>(objectConstants);
                if (FunctionCapturesActivationSlot(descriptor.Function, activationSlots, out capturedName))
                {
                    if (allowImmediateFunctionLiteralCall &&
                        TrySkipAdmittedClassComputedNameImmediateFunctionCall(
                            program,
                            operationIndex,
                            descriptor,
                            out var callOperationIndex))
                    {
                        operationIndex = callOperationIndex;
                        continue;
                    }

                    dependencyReason = "nested function literal activation capture";
                    return true;
                }

                continue;
            }

            if (operation.Kind is ExpressionOpKind.Call or ExpressionOpKind.Construct or ExpressionOpKind.SuperConstruct)
            {
                hasCallDependency = true;
                continue;
            }

            if (!TryGetIdentifierDependency(operation, identifierConstants, out var identifier) ||
                !ResolvesToActivationSlot(identifier, activationSlots))
            {
                continue;
            }

            capturedName = identifier.Name.Name;
            if (operation.Kind == ExpressionOpKind.LoadIdentifierCallTarget)
            {
                if (allowDirectActivationCall &&
                    TrySkipAdmittedClassComputedNameActivationCall(
                        program,
                        operationIndex,
                        identifierConstants,
                        activationSlots,
                        out var callOperationIndex))
                {
                    operationIndex = callOperationIndex;
                    continue;
                }

                dependencyReason = "call-target preparation";
                return true;
            }

            hasActivationReference = true;
            if (!IsOwnedClassComputedNameActivationOperation(operation.Kind))
            {
                dependencyReason = operation.Kind switch
                {
                    ExpressionOpKind.DeleteIdentifier => "activation binding delete",
                    _ => $"unsupported {operation.Kind} operation"
                };
                return true;
            }
        }

        if (hasActivationReference && hasCallDependency)
        {
            dependencyReason = "activation-dependent call or construct";
            return true;
        }

        return false;
    }

    private static bool TrySkipAdmittedClassComputedNameImmediateFunctionCall(
        ExpressionProgram program,
        int functionLiteralOperationIndex,
        FunctionLiteralDescriptor descriptor,
        out int callOperationIndex)
    {
        callOperationIndex = functionLiteralOperationIndex;
        if (descriptor.Function.IsAsync ||
            descriptor.Function.IsGenerator ||
            descriptor.PlanSeed.Plan is null)
        {
            return false;
        }

        var candidateIndex = functionLiteralOperationIndex + 1;
        if (candidateIndex >= program.OperationCount)
        {
            return false;
        }

        var callOperation = program.GetOperation(candidateIndex);
        if (callOperation.Kind != ExpressionOpKind.Call ||
            callOperation.ArgumentCount != 0 ||
            callOperation.HasExplicitThis ||
            callOperation.IsDirectEval ||
            callOperation.SpreadMaskConstantIndex >= 0)
        {
            return false;
        }

        callOperationIndex = candidateIndex;
        return true;
    }

    private static bool TrySkipAdmittedClassComputedNameActivationCall(
        ExpressionProgram program,
        int callTargetOperationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int callOperationIndex)
    {
        callOperationIndex = callTargetOperationIndex;

        var callTarget = program.GetOperation(callTargetOperationIndex);
        if (callTarget.Kind != ExpressionOpKind.LoadIdentifierCallTarget ||
            callTarget.IsArguments)
        {
            return false;
        }

        var targetIdentifier = callTarget.GetIdentifier(identifierConstants);
        if (string.Equals(targetIdentifier.Name.Name, "eval", StringComparison.Ordinal) ||
            !TryResolveActivationSlot(targetIdentifier, activationSlots))
        {
            return false;
        }

        for (var candidateIndex = callTargetOperationIndex + 1;
             candidateIndex < program.OperationCount;
             candidateIndex++)
        {
            var callOperation = program.GetOperation(candidateIndex);
            if (callOperation.Kind != ExpressionOpKind.Call)
            {
                continue;
            }

            if (!callOperation.HasExplicitThis ||
                callOperation.IsDirectEval ||
                callOperation.SpreadMaskConstantIndex >= 0)
            {
                return false;
            }

            if (TryValidateAdmittedComplexCallArgumentRegion(
                    program,
                    argsStartIndex: callTargetOperationIndex + 1,
                    callIndex: candidateIndex,
                    expectedArgumentCount: callOperation.ArgumentCount,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers: false))
            {
                callOperationIndex = candidateIndex;
                return true;
            }
        }

        return false;
    }

    private static bool IsOwnedClassComputedNameActivationOperation(ExpressionOpKind kind) =>
        kind is ExpressionOpKind.LoadIdentifier or
            ExpressionOpKind.TypeOfIdentifier or
            ExpressionOpKind.ResolveIdentifierReference or
            ExpressionOpKind.StoreResolvedIdentifier or
            ExpressionOpKind.StoreIdentifier or
            ExpressionOpKind.UpdateIdentifier or
            ExpressionOpKind.DeleteIdentifier;

    private static bool IsB24dStaticBlockClassLiteral(
        ClassDefinition definition,
        out string declineReason)
    {
        declineReason = string.Empty;
        if (definition.Extends is not null ||
            !definition.Members.IsDefaultOrEmpty ||
            !definition.Fields.IsDefaultOrEmpty ||
            definition.StaticBlocks.IsDefaultOrEmpty)
        {
            return false;
        }

        if (definition.StaticElements.IsDefaultOrEmpty ||
            definition.StaticElements.Length != definition.StaticBlocks.Length)
        {
            return false;
        }

        foreach (var element in definition.StaticElements)
        {
            if (element.Kind != ClassStaticElementKind.Block)
            {
                return false;
            }
        }

        var cache = ((IAstCacheable<ClassDefinitionProgramCache>)definition).GetOrCreateCache();
        if (!cache.Succeeded)
        {
            declineReason =
                $"Class literal static-block plans could not lower for B24d resumable production routing: {cache.FailureReason ?? "unknown failure"}.";
            return false;
        }

        foreach (var block in definition.StaticBlocks)
        {
            if (ClassStaticBlockClosureDetector.ContainsClosureProducingExpression(block.Body))
            {
                declineReason =
                    "Class literal is outside B24d: static block creates a closure that needs the materialized body environment route.";
                return false;
            }
        }

        return true;
    }

    private static bool IsB24cPublicStaticFieldClassLiteral(ClassDefinition definition)
    {
        if (definition.Extends is not null ||
            !definition.Members.IsDefaultOrEmpty ||
            !definition.StaticBlocks.IsDefaultOrEmpty ||
            definition.Fields.IsDefaultOrEmpty)
        {
            return false;
        }

        if (!definition.StaticElements.IsDefaultOrEmpty &&
            definition.StaticElements.Length != definition.Fields.Length)
        {
            return false;
        }

        foreach (var field in definition.Fields)
        {
            if (!field.IsStatic ||
                field.IsPrivate ||
                field.IsComputed ||
                field.ComputedName is not null ||
                StaticFieldInitializerCanCaptureActivation(field.Initializer))
            {
                return false;
            }
        }

        var constructor = definition.Constructor;
        return !constructor.IsAsync &&
               !constructor.IsGenerator &&
               !constructor.IsDefaultDerivedConstructor &&
               constructor.Parameters.IsDefaultOrEmpty &&
               constructor.Body.Statements.IsDefaultOrEmpty;
    }

    private static bool IsB24bcPublicStaticAndInstanceFieldClassLiteral(ClassDefinition definition)
    {
        if (definition.Extends is not null ||
            !definition.Members.IsDefaultOrEmpty ||
            !definition.StaticBlocks.IsDefaultOrEmpty ||
            definition.Fields.IsDefaultOrEmpty)
        {
            return false;
        }

        var hasStaticField = false;
        var hasInstanceField = false;
        foreach (var field in definition.Fields)
        {
            if (field.IsPrivate || field.IsComputed || field.ComputedName is not null)
            {
                return false;
            }

            if (field.IsStatic)
            {
                hasStaticField = true;
                if (StaticFieldInitializerCanCaptureActivation(field.Initializer))
                {
                    return false;
                }
            }
            else
            {
                hasInstanceField = true;
            }
        }

        if (!hasStaticField || !hasInstanceField)
        {
            return false;
        }

        return definition.StaticElements.IsDefaultOrEmpty ||
               definition.StaticElements.Length == CountStaticFields(definition);
    }

    private static bool TryAdmitB24MixedPublicStaticMemberAndFieldClassLiteral(
        ClassDefinition definition,
        ActivationSlotShape activationSlots,
        out bool candidate,
        out string declineReason)
    {
        candidate = false;
        declineReason = string.Empty;

        if (definition.Fields.IsDefaultOrEmpty ||
            definition.Members.IsDefaultOrEmpty ||
            !definition.StaticBlocks.IsDefaultOrEmpty)
        {
            return false;
        }

        if (definition.StaticElements.IsDefaultOrEmpty ||
            definition.StaticElements.Length != definition.Fields.Length)
        {
            return false;
        }

        foreach (var field in definition.Fields)
        {
            if (!field.IsStatic ||
                field.IsPrivate ||
                field.IsComputed)
            {
                return false;
            }
        }

        foreach (var member in definition.Members)
        {
            if (!member.IsStatic ||
                member.IsPrivate ||
                member.IsComputed)
            {
                return false;
            }
        }

        foreach (var element in definition.StaticElements)
        {
            if (element.Kind != ClassStaticElementKind.Field)
            {
                return false;
            }
        }

        candidate = true;
        if (FunctionCapturesActivationSlot(definition.Constructor, activationSlots, out var constructorCapturedName))
        {
            declineReason =
                $"Class literal mixed static constructor body captures activation binding '{constructorCapturedName}' and is outside B24 until the materialized body environment route owns that dependency.";
            return false;
        }

        foreach (var member in definition.Members)
        {
            if (FunctionCapturesActivationSlot(member.Function, activationSlots, out var capturedName))
            {
                declineReason =
                    $"Class literal mixed static member body captures activation binding '{capturedName}' and is outside B24 until the materialized body environment route owns that dependency.";
                return false;
            }
        }

        var cache = ((IAstCacheable<ClassDefinitionProgramCache>)definition).GetOrCreateCache();
        if (!cache.Succeeded)
        {
            declineReason =
                $"Class literal mixed static field programs could not lower for B24 resumable production routing: {cache.FailureReason ?? "unknown failure"}.";
            return false;
        }

        foreach (var initializerProgram in cache.FieldInitializerPrograms)
        {
            if (initializerProgram is null)
            {
                continue;
            }

            if (ExpressionProgramCreatesClosure(initializerProgram.Value))
            {
                declineReason =
                    "Class literal mixed static field initializer creates a closure that needs the materialized class-definition environment route.";
                return false;
            }

            if (UnifiedBytecodeCompiler.TryCompileStandaloneExpressionProgram(
                    initializerProgram.Value,
                    allowsDynamicIdentifiers: true,
                    out _,
                    out var initializerReason))
            {
                continue;
            }

            declineReason =
                $"Class literal mixed static field initializer is outside B24 production routing: {initializerReason}";
            return false;
        }

        return true;
    }

    private static bool TryAdmitB24PublicStaticMemberClassLiteral(
        ClassDefinition definition,
        ActivationSlotShape activationSlots,
        out bool candidate,
        out string declineReason)
    {
        candidate = false;
        declineReason = string.Empty;

        if (!definition.Fields.IsDefaultOrEmpty ||
            !definition.StaticBlocks.IsDefaultOrEmpty ||
            !definition.StaticElements.IsDefaultOrEmpty ||
            definition.Members.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var member in definition.Members)
        {
            if (!member.IsStatic ||
                member.IsPrivate ||
                member.IsComputed)
            {
                return false;
            }
        }

        candidate = true;
        if (FunctionCapturesActivationSlot(definition.Constructor, activationSlots, out var constructorCapturedName))
        {
            declineReason =
                $"Class literal static member constructor body captures activation binding '{constructorCapturedName}' and is outside B24 until the materialized body environment route owns that dependency.";
            return false;
        }

        foreach (var member in definition.Members)
        {
            if (FunctionCapturesActivationSlot(member.Function, activationSlots, out var capturedName))
            {
                declineReason =
                    $"Class literal static member body captures activation binding '{capturedName}' and is outside B24 until the materialized body environment route owns that dependency.";
                return false;
            }
        }

        return true;
    }

    private static bool TryAdmitB24PublicStaticFieldExtendsClassLiteral(
        ClassDefinition definition,
        ActivationSlotShape activationSlots,
        out bool candidate,
        out string declineReason)
    {
        candidate = false;
        declineReason = string.Empty;

        if (definition.Extends is null ||
            !definition.Members.IsDefaultOrEmpty ||
            !definition.StaticBlocks.IsDefaultOrEmpty ||
            definition.Fields.IsDefaultOrEmpty)
        {
            return false;
        }

        if (definition.StaticElements.IsDefaultOrEmpty ||
            definition.StaticElements.Length != definition.Fields.Length)
        {
            return false;
        }

        foreach (var element in definition.StaticElements)
        {
            if (element.Kind != ClassStaticElementKind.Field)
            {
                return false;
            }
        }

        foreach (var field in definition.Fields)
        {
            if (!field.IsStatic ||
                field.IsPrivate ||
                field.IsComputed)
            {
                return false;
            }
        }

        candidate = true;
        if (FunctionCapturesActivationSlot(definition.Constructor, activationSlots, out var constructorCapturedName))
        {
            declineReason =
                $"Class literal static field constructor body captures activation binding '{constructorCapturedName}' and is outside B24 until the materialized body environment route owns that dependency.";
            return false;
        }

        var cache = ((IAstCacheable<ClassDefinitionProgramCache>)definition).GetOrCreateCache();
        if (!cache.Succeeded)
        {
            declineReason =
                $"Class literal static field programs could not lower for B24 resumable production routing: {cache.FailureReason ?? "unknown failure"}.";
            return false;
        }

        foreach (var initializerProgram in cache.FieldInitializerPrograms)
        {
            if (initializerProgram is null)
            {
                continue;
            }

            if (ExpressionProgramCreatesClosure(initializerProgram.Value))
            {
                declineReason =
                    "Class literal static field initializer creates a closure that needs the materialized class-definition environment route.";
                return false;
            }

            if (UnifiedBytecodeCompiler.TryCompileStandaloneExpressionProgram(
                    initializerProgram.Value,
                    allowsDynamicIdentifiers: true,
                    out _,
                    out var initializerReason))
            {
                continue;
            }

            declineReason =
                $"Class literal static field initializer is outside B24 production routing: {initializerReason}";
            return false;
        }

        return true;
    }

    private static int CountStaticFields(ClassDefinition definition)
    {
        var count = 0;
        foreach (var field in definition.Fields)
        {
            if (field.IsStatic)
            {
                count++;
            }
        }

        return count;
    }

    private static bool StaticFieldInitializerCanCaptureActivation(ExpressionNode? initializer) =>
        initializer is not null &&
        ClassStaticFieldInitializerCaptureDetector.ContainsClosureProducingExpression(initializer);

    private sealed class ClassStaticFieldInitializerCaptureDetector : AstVisitor
    {
        [ThreadStatic] private static ClassStaticFieldInitializerCaptureDetector? _instance;

        private bool _found;

        public static bool ContainsClosureProducingExpression(ExpressionNode expression)
        {
            var detector = _instance ??= new ClassStaticFieldInitializerCaptureDetector();
            detector._found = false;
            detector.ShouldStop = false;
            detector.Visit(expression);
            return detector._found;
        }

        protected override void VisitFunctionExpression(FunctionExpression node)
        {
            _found = true;
            ShouldStop = true;
        }

        protected override void VisitClassExpression(ClassExpression node)
        {
            _found = true;
            ShouldStop = true;
        }

        protected override void VisitObjectExpression(ObjectExpression node)
        {
            foreach (var member in node.Members)
            {
                if (ShouldStop)
                {
                    break;
                }

                if (member.Key is ExpressionNode keyExpression)
                {
                    Visit(keyExpression);
                }

                if (!ShouldStop && member.Value is not null)
                {
                    Visit(member.Value);
                }

                if (!ShouldStop && member.Function is not null)
                {
                    VisitFunctionExpression(member.Function);
                }
            }
        }
    }

    private sealed class ClassStaticBlockClosureDetector : AstVisitor
    {
        [ThreadStatic] private static ClassStaticBlockClosureDetector? _instance;

        private bool _found;

        public static bool ContainsClosureProducingExpression(BlockStatement block)
        {
            var detector = _instance ??= new ClassStaticBlockClosureDetector();
            detector._found = false;
            detector.ShouldStop = false;
            detector.VisitBlockStatement(block);
            return detector._found;
        }

        protected override void VisitFunctionDeclaration(FunctionDeclaration node)
        {
            _found = true;
            ShouldStop = true;
        }

        protected override void VisitFunctionExpression(FunctionExpression node)
        {
            _found = true;
            ShouldStop = true;
        }

        protected override void VisitClassDeclaration(ClassDeclaration node)
        {
            _found = true;
            ShouldStop = true;
        }

        protected override void VisitClassExpression(ClassExpression node)
        {
            _found = true;
            ShouldStop = true;
        }

        protected override void VisitObjectExpression(ObjectExpression node)
        {
            foreach (var member in node.Members)
            {
                if (ShouldStop)
                {
                    break;
                }

                if (member.Key is ExpressionNode keyExpression)
                {
                    VisitExpression(keyExpression);
                }

                if (!ShouldStop && member.Value is not null)
                {
                    VisitExpression(member.Value);
                }

                if (!ShouldStop && member.Function is not null)
                {
                    _found = true;
                    ShouldStop = true;
                }
            }
        }
    }

    private static bool IsB24fPrivateInstanceMemberClassLiteral(ClassDefinition definition)
    {
        if (!definition.Fields.IsDefaultOrEmpty ||
            definition.Members.IsDefaultOrEmpty ||
            definition.Members.Length == 0)
        {
            return false;
        }

        foreach (var member in definition.Members)
        {
            if (!member.IsPrivate || member.IsStatic || member.IsComputed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ExpressionProgramReferencesActivationSlot(
        ExpressionProgram program,
        ActivationSlotShape activationSlots,
        out string capturedName)
    {
        capturedName = string.Empty;
        if (program.IsEmpty)
        {
            return false;
        }

        var identifierConstants = program.IdentifierConstants.AsSpan();
        var objectConstants = program.ObjectConstants.AsSpan();
        foreach (var operation in program.EnumerateOperations())
        {
            if (operation.Kind == ExpressionOpKind.LoadFunctionLiteral)
            {
                var descriptor = operation.GetObject<FunctionLiteralDescriptor>(objectConstants);
                if (FunctionCapturesActivationSlot(descriptor.Function, activationSlots, out capturedName))
                {
                    return true;
                }

                continue;
            }

            if (!TryGetIdentifierDependency(operation, identifierConstants, out var identifier) ||
                !ResolvesToActivationSlot(identifier, activationSlots))
            {
                continue;
            }

            capturedName = identifier.Name.Name;
            return true;
        }

        return false;
    }

    private static bool IsB24ePrivateInstanceFieldClassLiteral(ClassDefinition definition)
    {
        if (definition.Fields.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var field in definition.Fields)
        {
            if (!field.IsPrivate || field.IsStatic || field.IsComputed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreResumableB24ClassFieldsSupported(
        ClassDefinition definition,
        ActivationSlotShape activationSlots,
        out string declineReason)
    {
        declineReason = string.Empty;
        if (definition.Fields.IsDefaultOrEmpty)
        {
            return true;
        }

        if (IsB24bPublicInstanceFieldClassLiteral(definition))
        {
            return AreB24bFieldInitializersActivationSafe(definition, activationSlots, out declineReason);
        }

        foreach (var field in definition.Fields)
        {
            if (field.IsStatic || field.IsComputed)
            {
                declineReason =
                    "Class literal is outside B24: class fields remain owned by later B24 static-field/computed-member slices.";
                return false;
            }

            if (field.IsPrivate &&
                field.Initializer is not null &&
                ExpressionContainsSuper(field.Initializer))
            {
                declineReason =
                    "Class literal is outside B24i: private super-bearing field initializers remain owned by later class-definition environment slices.";
                return false;
            }

            if (!field.IsPrivate &&
                (field.Initializer is null || !ExpressionContainsSuper(field.Initializer)))
            {
                declineReason =
                    "Class literal is outside B24: class fields remain owned by later B24 static-field/computed-member slices.";
                return false;
            }
        }

        return AreB24bFieldInitializersActivationSafe(definition, activationSlots, out declineReason);
    }

    private static bool IsB24bPublicInstanceFieldClassLiteral(ClassDefinition definition)
    {
        if (definition.Extends is not null ||
            definition.Fields.IsDefaultOrEmpty ||
            !definition.Members.IsDefaultOrEmpty ||
            !definition.StaticBlocks.IsDefaultOrEmpty ||
            !definition.StaticElements.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var field in definition.Fields)
        {
            if (field.IsStatic || field.IsPrivate || field.IsComputed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreB24bFieldInitializersActivationSafe(
        ClassDefinition definition,
        ActivationSlotShape activationSlots,
        out string declineReason)
    {
        var cache = ((IAstCacheable<ClassDefinitionProgramCache>)definition).GetOrCreateCache();
        if (!cache.Succeeded)
        {
            declineReason =
                $"Class literal field programs could not lower for B24b resumable production routing: {cache.FailureReason ?? "unknown failure"}.";
            return false;
        }

        var initializerPrograms = cache.FieldInitializerPrograms;
        for (var i = 0; i < initializerPrograms.Length; i++)
        {
            if (initializerPrograms[i] is { } initializerProgram &&
                ExpressionProgramReferencesActivationSlot(initializerProgram, activationSlots, out var capturedName))
            {
                declineReason =
                    $"Class literal field initializer captures activation binding '{capturedName}' and is not supported by B24b/B24i resumable production routing until the resume state owns a materialized body environment.";
                return false;
            }
        }

        declineReason = string.Empty;
        return true;
    }

    private static bool AreResumableB24ClassMembersSupported(
        ClassDefinition definition,
        bool isPrivateInstanceFieldClassLiteral,
        ActivationSlotShape activationSlots,
        out string declineReason)
    {
        declineReason = string.Empty;
        foreach (var member in definition.Members)
        {
            if (member.IsStatic ||
                member.IsComputed ||
                member.IsPrivate ||
                (!isPrivateInstanceFieldClassLiteral && !FunctionContainsSuper(member.Function)))
            {
                declineReason =
                    "Class literal is outside B24: computed or static class members remain later B24 slices; admitted subsets include the B24c public static-field subset.";
                return false;
            }

            if (FunctionCapturesActivationSlot(member.Function, activationSlots, out var capturedName))
            {
                declineReason =
                    $"Class literal member body captures activation binding '{capturedName}' and is outside B24i until the materialized body environment route owns that dependency.";
                return false;
            }
        }

        return true;
    }

    private static bool FunctionContainsSuper(FunctionExpression function) =>
        ExpressionContainsSuper(function);

    private static bool ExpressionContainsSuper(ExpressionNode expression)
    {
        var visitor = new SuperExpressionDetector();
        visitor.Visit(expression);
        return visitor.Found;
    }

    private sealed class SuperExpressionDetector : AstVisitor
    {
        public bool Found { get; private set; }

        protected override void VisitSuperExpression(SuperExpression node)
        {
            Found = true;
            ShouldStop = true;
        }
    }

    private static bool ClassExtendsReadsUnifiedSlot(
        ClassDefinition definition,
        ImmutableArray<string?> slotNames)
    {
        if (definition.Extends is null)
        {
            return false;
        }

        var cache = ((IAstCacheable<ClassDefinitionProgramCache>)definition).GetOrCreateCache();
        if (!cache.Succeeded || cache.ExtendsProgram is not { } extendsProgram)
        {
            return false;
        }

        var identifierConstants = extendsProgram.IdentifierConstants.AsSpan();
        for (var operationIndex = 0; operationIndex < extendsProgram.OperationCount; operationIndex++)
        {
            var operation = extendsProgram.GetOperation(operationIndex);
            if (operation.Kind is not (
                    ExpressionOpKind.LoadIdentifier or
                    ExpressionOpKind.LoadIdentifierCallTarget or
                    ExpressionOpKind.ResolveIdentifierReference or
                    ExpressionOpKind.StoreIdentifier or
                    ExpressionOpKind.UpdateIdentifier or
                    ExpressionOpKind.TypeOfIdentifier or
                    ExpressionOpKind.DeleteIdentifier))
            {
                continue;
            }

            var identifier = operation.GetIdentifier(identifierConstants);
            if (identifier.FlatSlotId >= 0 || SlotNamesContain(slotNames, identifier.Name.Name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SlotNamesContain(ImmutableArray<string?> slotNames, string name)
    {
        for (var i = 0; i < slotNames.Length; i++)
        {
            if (string.Equals(slotNames[i], name, StringComparison.Ordinal))
            {
                return true;
            }
        }

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
        bool allowImplicitArgumentsObjectPropertyReadOperands,
        out UnifiedBytecodeProductionDeclineCode declineCode,
        out string declineReason,
        // A30: optional-computed-START member calls (o?.[k](), a?.b?.[k]()) are admitted only on the
        // SYNC route. The resumable route still declines them as OptionalChainDependency at the plan
        // walk (their leading optional-hop short-circuit is not threaded across yield/await), so this
        // flag stays false for the resumable caller and the candidate predicate skips those shapes.
        bool allowSyncOnlyOptionalComputedStartCalls = false)
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
                !IsOperationInSimpleArrayLiteralSpan(
                    program,
                    i,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.ObjectLiteralOrSpreadDependency;
                declineReason =
                    "Array spread with non-simple source is not eligible for production unified bytecode routing.";
                return true;
            }

            if (program.GetOperation(i).Kind == ExpressionOpKind.ObjectSpread &&
                !IsOperationInSimpleObjectLiteralSpan(
                    program,
                    i,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers))
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
            allowsDynamicIdentifiers,
            allowSyncOnlyOptionalComputedStartCalls);
        var isGeneralIdentifierCallExpressionCandidate = TryIsGeneralIdentifierCallExpressionCandidate(
            program,
            identifierConstants,
            activationSlots,
            allowsDynamicIdentifiers);
        var isGeneralNamedMemberCallExpressionCandidate = TryIsGeneralNamedMemberCallExpressionCandidate(
            program,
            identifierConstants,
            stringConstants,
            activationSlots,
            allowsDynamicIdentifiers);
        var isConstructInvocationCandidate = TryIsConstructInvocationCandidate(program, identifierConstants, activationSlots);
        var hasOptionalChainOperation = HasOptionalChainOperation(program);

        // PROPERTY-WRITE complex RHS: when the program is an admitted property-write candidate
        // (`o.x = <complex>`, `o[k] = <complex>`, `this.x = <complex>`), any nested-call ops it
        // contains belong to the already-validated RHS value region (the base is a simple
        // identifier and computed keys never carry calls), so the per-op call arms below must
        // NOT decline them. Compute the flag once; it gates the call-target/Call escape hatches.
        var lastOperationKind = program.GetOperation(operationCount - 1).Kind;
        var isComplexRhsPropertyWriteCandidate =
            lastOperationKind is ExpressionOpKind.SetNamedProperty or ExpressionOpKind.SetComputedProperty &&
            TryIsFirstBoundaryPropertyWriteCandidate(program, identifierConstants, activationSlots, allowsDynamicIdentifiers);

        // COMPOUND-WRITE complex RHS: when the program is an admitted compound-property-write
        // candidate (`o.x += <complex>`, `o[k] -= <complex>`, `this.x *= <complex>`), any
        // nested-call ops it contains belong to the already-validated RHS value region (the
        // receiver/key are evaluated first as simple read spans, never carry calls), so the per-op
        // call arms below must NOT decline them. The same gate also lets the embedded old-value
        // read (GetNamedProperty/GetComputedProperty) through, which the compound flags already
        // cover. Compute once; it gates the call-target/Call escape hatches.
        var isComplexRhsCompoundPropertyWriteCandidate =
            isFirstBoundaryNamedCompoundPropertyWriteCandidate ||
            (lastOperationKind is ExpressionOpKind.SetComputedProperty &&
             TryIsFirstBoundaryComputedCompoundPropertyWriteCandidate(
                 program,
                 identifierConstants,
                 activationSlots,
                 allowsDynamicIdentifiers));
        const int DynamicIdentifierReferenceSlot = -1;
        List<int>? identifierReferenceSlots = null;
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
                    if (isCallTargetPreparationCandidate ||
                        isGeneralIdentifierCallExpressionCandidate ||
                        isComplexRhsPropertyWriteCandidate ||
                        isComplexRhsCompoundPropertyWriteCandidate)
                    {
                        break;
                    }

                    // A33/A34: a bare identifier call used as a spread source
                    // (`[...f()]`, `{...f()}`, `[...gen()]`, `[...f().items]`) is admitted
                    // when the call-target op falls inside an admitted simple array- or
                    // object-literal span. The member-call (LoadNamedCallTarget) and Call
                    // cases below already carry this escape hatch; identifier call targets
                    // did not.
                    if (IsOperationInSimpleArrayLiteralSpan(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers) ||
                        IsOperationInSimpleObjectLiteralSpan(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
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
                    if (isCallTargetPreparationCandidate ||
                        isGeneralNamedMemberCallExpressionCandidate ||
                        isComplexRhsPropertyWriteCandidate ||
                        isComplexRhsCompoundPropertyWriteCandidate)
                    {
                        break;
                    }

                    if (IsOperationInSimpleArrayLiteralSpan(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers) ||
                        IsOperationInSimpleObjectLiteralSpan(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
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
                    if (isCallTargetPreparationCandidate ||
                        isGeneralIdentifierCallExpressionCandidate ||
                        isGeneralNamedMemberCallExpressionCandidate ||
                        isComplexRhsPropertyWriteCandidate ||
                        isComplexRhsCompoundPropertyWriteCandidate)
                    {
                        break;
                    }

                    if (IsOperationInSimpleArrayLiteralSpan(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers) ||
                        IsOperationInSimpleObjectLiteralSpan(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
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
                    if (isCallTargetPreparationCandidate ||
                        isGeneralNamedMemberCallExpressionCandidate ||
                        isComplexRhsPropertyWriteCandidate ||
                        isComplexRhsCompoundPropertyWriteCandidate)
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
                        if (!allowsDynamicIdentifiers)
                        {
                            declineCode = UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency;
                            declineReason =
                                "arguments assignment references are not eligible for production unified bytecode routing.";
                            return true;
                        }

                        identifierReferenceSlots ??= [];
                        identifierReferenceSlots.Add(DynamicIdentifierReferenceSlot);
                        break;
                    }

                    if (TryResolveExplicitActivationSlot(referenceIdentifier, activationSlots, out var referenceSlotIndex))
                    {
                        identifierReferenceSlots ??= [];
                        identifierReferenceSlots.Add(referenceSlotIndex);
                        break;
                    }

                    if (TryResolveActivationSlot(referenceIdentifier, activationSlots))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                        declineReason =
                            $"Identifier assignment reference '{referenceIdentifier.Name.Name}' resolves only by activation-slot name lookup and is outside the slot-reference production slice.";
                        return true;
                    }

                    if (allowsDynamicIdentifiers)
                    {
                        identifierReferenceSlots ??= [];
                        identifierReferenceSlots.Add(DynamicIdentifierReferenceSlot);
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                    declineReason =
                        "Dynamic identifier assignment references are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.LoadResolvedIdentifierValue:
                    if (identifierReferenceSlots is { Count: > 0 } &&
                        identifierReferenceSlots[^1] >= 0)
                    {
                        break;
                    }

                    if (allowsDynamicIdentifiers)
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                    declineReason =
                        "Dynamic identifier assignment references are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.PopResolvedIdentifierReference:
                    if (identifierReferenceSlots is { Count: > 0 })
                    {
                        var pendingReferenceSlot = identifierReferenceSlots[^1];
                        identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                        if (pendingReferenceSlot >= 0)
                        {
                            break;
                        }
                    }

                    if (allowsDynamicIdentifiers)
                    {
                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                    declineReason =
                        "Dynamic identifier assignment references are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.StoreResolvedIdentifier:
                    var storeReferenceIdentifier = operation.GetIdentifier(identifierConstants);
                    if (IsImplicitArgumentsIdentifier(storeReferenceIdentifier, activationSlots))
                    {
                        if (!allowsDynamicIdentifiers ||
                            identifierReferenceSlots is not { Count: > 0 } ||
                            identifierReferenceSlots[^1] != DynamicIdentifierReferenceSlot)
                        {
                            declineCode = UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency;
                            declineReason =
                                "arguments assignment references are not eligible for production unified bytecode routing.";
                            return true;
                        }

                        identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                        break;
                    }

                    if (TryResolveExplicitActivationSlot(
                            storeReferenceIdentifier,
                            activationSlots,
                            out var storeReferenceSlotIndex))
                    {
                        if (identifierReferenceSlots is not { Count: > 0 } ||
                            identifierReferenceSlots[^1] != storeReferenceSlotIndex)
                        {
                            declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                            declineReason =
                                $"Identifier assignment target '{storeReferenceIdentifier.Name.Name}' does not match the pending slot-reference target.";
                            return true;
                        }

                        identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                        break;
                    }

                    if (TryResolveActivationSlot(storeReferenceIdentifier, activationSlots))
                    {
                        declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                        declineReason =
                            $"Identifier assignment target '{storeReferenceIdentifier.Name.Name}' resolves only by activation-slot name lookup and is outside the slot-reference production slice.";
                        return true;
                    }

                    if (allowsDynamicIdentifiers)
                    {
                        if (identifierReferenceSlots is { Count: > 0 } &&
                            identifierReferenceSlots[^1] == DynamicIdentifierReferenceSlot)
                        {
                            identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                        }

                        break;
                    }

                    declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                    declineReason =
                        "Dynamic identifier assignment references are not eligible for production unified bytecode routing.";
                    return true;

                case ExpressionOpKind.StoreIdentifier:
                    var storeIdentifier = operation.GetIdentifier(identifierConstants);
                    if (IsImplicitArgumentsIdentifier(storeIdentifier, activationSlots))
                    {
                        if (!allowsDynamicIdentifiers)
                        {
                            declineCode = UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency;
                            declineReason =
                                "arguments assignment references are not eligible for production unified bytecode routing.";
                            return true;
                        }

                        break;
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
                    // A33/A34: a plain named property read off a call result used as a
                    // spread source (`[...f().items]`, `{...f().items}`, `{...f().a.b}`) is
                    // admitted when the read op falls inside an admitted simple array- or
                    // object-literal span. The spread-source span only includes such reads
                    // when they terminate in ArraySpread/ObjectSpread, so this does not
                    // widen ordinary property-read scope.
                    if (!operation.IsOptional &&
                        !operation.ShortCircuitOnNullishTarget &&
                        (IsOperationInSimpleArrayLiteralSpan(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers) ||
                         IsOperationInSimpleObjectLiteralSpan(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers)))
                    {
                        break;
                    }

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

                        // Plain continuation read of an optional-start named read chain used as a
                        // call argument (`fn(box?.child.value)`).
                        if (TryIsEmbeddedOptionalNamedReadChainCallArgumentContinuation(
                                program,
                                operationIndex,
                                identifierConstants,
                                activationSlots,
                                allowsDynamicIdentifiers))
                        {
                            break;
                        }

                        // Continuation read of an optional-computed-start read chain used as
                        // a call argument (`fn(box?.[key].value)`, `fn(box?.[key]?.value)`).
                        if (TryIsEmbeddedOptionalComputedReadChainCallArgumentContinuation(
                                program,
                                operationIndex,
                                identifierConstants,
                                activationSlots,
                                allowsDynamicIdentifiers))
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

                    // A17/A18 widening: deep PURE read chains off an object/array literal base
                    // (`({a:{b:1}}).a.b`, `[x][0].a['b']`).
                    if (TryIsFirstBoundaryLiteralBasePropertyReadChainCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    // A19 widening: this is a receiver-prefix read hop of a deep PURE write chain off
                    // an object/array literal base (`({ a: {} }).a.b = v`). The terminal Set op is
                    // validated by the same whole-program walker from the Set* case.
                    if (TryIsFirstBoundaryLiteralBasePropertyWriteChainCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
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

                    if (TryIsEmbeddedSimplePropertyReadOperandOperation(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers,
                            allowImplicitArgumentsObjectPropertyReadOperands))
                    {
                        break;
                    }

                    if (TryIsEmbeddedSuperConstructPropertyReadArgumentOperation(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    if (isFirstBoundaryNamedCompoundPropertyWriteCandidate)
                    {
                        break;
                    }
                    if (isComplexRhsCompoundPropertyWriteCandidate)
                    {
                        break;
                    }
                    if (isFirstBoundaryNamedLogicalPropertyWriteCandidate)
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryNestedNamedPropertyWriteCandidate(program, identifierConstants, activationSlots) ||
                        TryIsFirstBoundaryNestedNamedComputedPropertyWriteCandidate(program, identifierConstants, activationSlots, allowsDynamicIdentifiers) ||
                        TryIsFirstBoundaryNestedNamedPropertyUpdateCandidate(program, identifierConstants, activationSlots) ||
                        TryIsFirstBoundaryNestedNamedComputedPropertyUpdateCandidate(program, identifierConstants, activationSlots, allowsDynamicIdentifiers))
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

                    // A25/A26: deep delete chains with a computed hop before the terminal delete
                    // (`delete box.a[k1][k2]`); the named read hop lands in this case.
                    if (TryIsFirstBoundaryDeepPropertyDeleteChainCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryOptionalComputedReadThenComputedPropertyDeleteCandidate(
                            program,
                            identifierConstants,
                            activationSlots) ||
                        TryIsFirstBoundaryOptionalComputedPropertyDeleteCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryOptionalNamedThenNamedPropertyDeleteCandidate(program, identifierConstants, activationSlots) ||
                        TryIsFirstBoundaryOptionalNamedThenOptionalComputedPropertyDeleteCandidate(program, identifierConstants, activationSlots))
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

                    // Read op of an optional computed read used as a call argument (`fn(box?.[key])`,
                    // `fn(box?.[key]?.[key])`).
                    if (TryIsOptionalComputedReadCallArgumentOperation(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
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

                        // A29: a short-circuiting computed read of a multi-hop optional computed chain
                        // (`a?.[k]?.[j]`). The first hop's GetComputedProperty is non-short-circuiting
                        // (handled below); every subsequent hop's read short-circuits and lands here.
                        if (TryIsFirstBoundaryOptionalComputedPropertyReadChainCandidate(program, identifierConstants, activationSlots))
                        {
                            break;
                        }

                        // `fn(box?.prop[key])` — optional-named-then-plain-computed read used as a
                        // call argument; the program ends in a Call rather than the standalone shape.
                        if (TryIsOptionalNamedThenComputedReadCallArgumentOperation(
                                program,
                                operationIndex,
                                identifierConstants,
                                activationSlots,
                                allowsDynamicIdentifiers))
                        {
                            break;
                        }

                        declineCode = UnifiedBytecodeProductionDeclineCode.OptionalChainDependency;
                        declineReason =
                            "Optional-chain computed property reads are outside the first production property-read boundary.";
                        return true;
                    }

                    if (TryIsEmbeddedSimpleLiteralPropertyReadOperandOperation(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    if (TryIsEmbeddedSimplePropertyReadOperandOperation(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers,
                            allowImplicitArgumentsObjectPropertyReadOperands))
                    {
                        break;
                    }

                    if (TryIsEmbeddedSuperConstructPropertyReadArgumentOperation(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    if (isCallTargetPreparationCandidate)
                    {
                        break;
                    }

                    if (TryIsFirstBoundaryComputedPropertyReadChainCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    // A17/A18 widening: deep PURE read chains off an object/array literal base
                    // ending in a computed read (`({a:{b:1}})['a']['b']`, `[x][0][0]`).
                    if (TryIsFirstBoundaryLiteralBasePropertyReadChainCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    // A19 widening: a computed receiver-prefix read hop of a deep PURE write chain off
                    // an object/array literal base (`[box][0].a = v`, `({ a: {} }).a['b'] = v`). The
                    // terminal Set op is validated by the same whole-program walker from the Set* case.
                    if (TryIsFirstBoundaryLiteralBasePropertyWriteChainCandidate(
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

                    if (TryIsFirstBoundaryOptionalComputedReadThenComputedPropertyDeleteCandidate(
                            program,
                            identifierConstants,
                            activationSlots))
                    {
                        break;
                    }

                    // A25/A26: an intermediate computed read hop of a deep property delete chain
                    // (`delete box[k1][k2]`, `delete box[k1].b`). The terminal delete op is validated
                    // by the same whole-program walker invoked from the Delete* cases.
                    if (TryIsFirstBoundaryDeepPropertyDeleteChainCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    if (isConstructInvocationCandidate)
                    {
                        break;
                    }

                    if (isComplexRhsCompoundPropertyWriteCandidate)
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

                    // Computed read prefix of a `box[key].child = value` named write.
                    if (TryIsFirstBoundaryComputedPrefixNamedPropertyWriteCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }
                    if (TryIsFirstBoundaryComputedPrefixComputedPropertyWriteCandidate(
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
                        TryIsFirstBoundaryNestedNamedComputedPropertyWriteCandidate(program, identifierConstants, activationSlots, allowsDynamicIdentifiers) ||
                        TryIsFirstBoundaryComputedPrefixNamedPropertyWriteCandidate(program, identifierConstants, activationSlots, allowsDynamicIdentifiers) ||
                        TryIsFirstBoundaryComputedPrefixComputedPropertyWriteCandidate(program, identifierConstants, activationSlots, allowsDynamicIdentifiers) ||
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

                    // A19 widening: deep PURE write chains off an object/array literal base
                    // (`({ a: { b: 0 } }).a.b = v`, `({ a: {} }).a['b'] = v`, `[box][0].a = v`).
                    if (TryIsFirstBoundaryLiteralBasePropertyWriteChainCandidate(
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
                        TryIsFirstBoundaryNestedNamedPropertyUpdateCandidate(program, identifierConstants, activationSlots) ||
                        TryIsFirstBoundaryNestedNamedComputedPropertyUpdateCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers) ||
                        TryIsFirstBoundaryComputedPrefixPropertyUpdateCandidate(
                            program,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
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
                    if (TryIsFirstBoundaryOptionalNamedThenNamedPropertyDeleteCandidate(program, identifierConstants, activationSlots) ||
                        TryIsFirstBoundaryOptionalNamedThenOptionalNamedPropertyDeleteCandidate(
                            program,
                            identifierConstants,
                            activationSlots) ||
                        TryIsFirstBoundaryOptionalNamedPropertyDeleteCandidate(program, identifierConstants, activationSlots))
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

                    // A25/A26: deep delete chain whose terminal named delete sits past a computed
                    // read hop (`delete box[k1].b`).
                    if (TryIsFirstBoundaryDeepPropertyDeleteChainCandidate(
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
                        TryIsFirstBoundaryOptionalComputedReadThenComputedPropertyDeleteCandidate(
                            program,
                            identifierConstants,
                            activationSlots) ||
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

                    // A25/A26: deep delete chain whose terminal computed delete sits past one or more
                    // computed read hops (`delete box[k1][k2]`, `delete box.a[k1][k2]`).
                    if (TryIsFirstBoundaryDeepPropertyDeleteChainCandidate(
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

                    if (IsOperationInSimpleArrayLiteralSpan(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers) ||
                        IsOperationInSimpleObjectLiteralSpan(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    // Nullish guard of a?.[k] — admitted when the program matches the optional computed read shape.
                    if (TryIsFirstBoundaryOptionalComputedPropertyReadChainCandidate(program, identifierConstants, activationSlots))
                    {
                        break;
                    }

                    // Nullish guard of delete a?.[k1][k2] — admitted when the whole program matches the
                    // optional computed-read receiver plus terminal computed-delete shape.
                    if (TryIsFirstBoundaryOptionalComputedReadThenComputedPropertyDeleteCandidate(
                            program,
                            identifierConstants,
                            activationSlots))
                    {
                        break;
                    }

                    // Nullish guard of an optional computed read used as a call argument (`fn(box?.[key])`,
                    // `fn(box?.[key]?.[key])`).
                    if (TryIsOptionalComputedReadCallArgumentOperation(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
                    {
                        break;
                    }

                    // Nullish guard of an optional named-then-computed read used as a call argument
                    // (`fn(box?.prop?.[key])`).
                    if (TryIsOptionalNamedThenComputedReadCallArgumentOperation(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
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
                    if (IsOperationInSimpleArrayLiteralSpan(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
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
                    if (IsOperationInSimpleObjectLiteralSpan(
                            program,
                            operationIndex,
                            identifierConstants,
                            activationSlots,
                            allowsDynamicIdentifiers))
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

        if (identifierReferenceSlots is { Count: > 0 })
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
            declineReason =
                "Identifier assignment references were left pending after unified bytecode expression lowering.";
            return true;
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
        var hasOptionalGuard = false;
        for (var operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
        {
            var operation = program.GetOperation(operationIndex);
            hasDelete |= operation.Kind is ExpressionOpKind.DeleteNamedProperty or ExpressionOpKind.DeleteComputedProperty;
            hasOptionalGuard |= operation.Kind is ExpressionOpKind.JumpIfNullish or ExpressionOpKind.JumpIfShortCircuited;
        }

        return hasDelete && hasOptionalGuard;
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
        if (instruction.LexicalSlotIndices.IsDefaultOrEmpty)
        {
            return true;
        }

        // Per-iteration binding environments (for (const/let x in/of ...)) are admitted when every
        // lexical slot resolves to a flat slot. Captured A44 shapes depend on the compiler carrying
        // PerIterationBindings as copy metadata so PushEnvironment copies the current value into the fresh
        // scope environment instead of applying the ordinary TDZ wipe to that slot.
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

    // Exposed to the test assembly: sync and async-kind iterator drivers must have
    // exactly one source payload. Awaited source payloads are source evaluation;
    // IteratorDriverKind.Await is the async iterator protocol boundary admitted by B41.
    internal static bool IsSupportedIteratorInit(IteratorInitInstruction instruction, out string reason)
    {
        var hasIterableProgram = instruction.IterableProgram is not null;
        var hasAwaitedProgram = instruction.AwaitedProgram is not null;
        if (hasIterableProgram == hasAwaitedProgram)
        {
            reason = "Iterator driver sources must be lowered to exactly one expression bytecode payload.";
            return false;
        }

        // Slice A (#2678): sync iterator drivers that own a TDZ head environment
        // (for example `for (const x of ...)`) are now admitted. Awaited sync-kind
        // sources in async functions are admitted through the resumable VM by
        // compiling the source expression followed by AwaitValue and IteratorInit.
        // B41: async-kind drivers are admitted for the simple resumable VM subset. The VM owns
        // next-result and yielded-value awaits before storing the current iteration value.
        reason = string.Empty;
        return true;
    }

    // Exposed to the test assembly (AC-5 negative coverage): the awaited-source arm must keep
    // declining with its explicit reason even though sync TDZ heads are admitted.
    internal static bool IsSupportedForInInit(ForInInitInstruction instruction, out string reason)
    {
        if (instruction.ObjectProgram is null && instruction.AwaitedProgram is null ||
            instruction.ObjectProgram is not null && instruction.AwaitedProgram is not null)
        {
            reason = "for-in driver sources must be lowered to exactly one expression bytecode payload.";
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

    /// <summary>
    /// Admits a PURE property-read chain whose base is a simple object/array literal
    /// (A17/A18 "non-anchored base" widening), e.g. <c>({a:{b:1}}).a.b</c>,
    /// <c>({a:1})['a']</c>, <c>[x][0].a['b']</c>. The whole program is validated with one
    /// stack-discipline walk: <c>CreateObject</c>/<c>CreateArray</c> roots the base, the literal's
    /// own construction ops balance back to a single value, and the trailing hops are plain named
    /// reads and computed reads (<c>key…, RequireObjectCoercible(Depth: 1), ResolvePropertyKey,
    /// GetComputedProperty</c>). Any call, write, optional/short-circuit read, private name, method,
    /// accessor, or name-inferred define falls outside the vocabulary and declines, so a getter in
    /// the chain is invoked exactly once per hop (matching the interpreter) and the base stays pure.
    /// </summary>
    private static bool TryIsFirstBoundaryLiteralBasePropertyReadChainCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 2)
        {
            return false;
        }

        var rootKind = program.GetOperation(0).Kind;
        if (rootKind is not (ExpressionOpKind.CreateObject or ExpressionOpKind.CreateArray))
        {
            return false;
        }

        // The chain must end on a property-read hop; otherwise the trailing ops are something
        // other than a read (write/call/define) and this is not a pure read chain.
        var lastOp = program.GetOperation(program.OperationCount - 1);
        if (lastOp.Kind is not (ExpressionOpKind.GetNamedProperty or ExpressionOpKind.GetComputedProperty))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var stackDepth = 0;
        var sawReadHop = false;
        for (var index = 0; index < program.OperationCount; index++)
        {
            var operation = program.GetOperation(index);
            switch (operation.Kind)
            {
                // ----- literal base construction -----
                case ExpressionOpKind.CreateObject:
                case ExpressionOpKind.CreateArray:
                    stackDepth++;
                    break;

                case ExpressionOpKind.DefineObjectProperty:
                    if (stackDepth < 2 ||
                        operation.AllowNameInference ||
                        operation.GetString(stringConstants).IsPrivateName())
                    {
                        return false;
                    }

                    stackDepth--;
                    break;

                case ExpressionOpKind.DefineComputedObjectProperty:
                    if (stackDepth < 3 || operation.AllowNameInference)
                    {
                        return false;
                    }

                    stackDepth -= 2;
                    break;

                case ExpressionOpKind.ArrayPush:
                case ExpressionOpKind.ArraySpread:
                case ExpressionOpKind.ObjectSpread:
                    if (stackDepth < 2)
                    {
                        return false;
                    }

                    stackDepth--;
                    break;

                case ExpressionOpKind.ArrayPushHole:
                    if (stackDepth < 1)
                    {
                        return false;
                    }

                    break;

                // ----- pure read hops -----
                case ExpressionOpKind.GetNamedProperty:
                    if (operation.IsOptional ||
                        operation.ShortCircuitOnNullishTarget ||
                        operation.GetString(stringConstants).IsPrivateName() ||
                        stackDepth < 1)
                    {
                        return false;
                    }

                    sawReadHop = true;
                    break;

                case ExpressionOpKind.GetComputedProperty:
                    if (operation.ShortCircuitOnNullishTarget || stackDepth < 2)
                    {
                        return false;
                    }

                    stackDepth--;
                    sawReadHop = true;
                    break;

                case ExpressionOpKind.RequireObjectCoercible:
                    // The receiver guard for a computed hop targets the object directly below the
                    // pending key (Depth: 1); anything else is not a plain computed read.
                    if (operation.Depth != 1 || stackDepth < 2)
                    {
                        return false;
                    }

                    break;

                case ExpressionOpKind.ResolvePropertyKey:
                    if (stackDepth < 1)
                    {
                        return false;
                    }

                    break;

                // ----- key / value sub-expressions (shared between base members and computed keys) -----
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

        return sawReadHop && stackDepth == 1;
    }

    /// <summary>
    /// A19 write-past-boundary widening: admits a PURE deep property WRITE whose base is a simple
    /// object/array literal and whose terminal store is NAMED, e.g. <c>({ a: { b: 0 } }).a.b = 1</c>,
    /// <c>({ a: {} }).a.b.c = v</c>, and the array-literal / computed-prefix analog <c>[box][0].a = v</c>.
    /// Mirrors the first-boundary read walker
    /// (<see cref="TryIsFirstBoundaryLiteralBasePropertyReadChainCandidate"/>): the WHOLE program is
    /// validated with one stack-discipline walk. <c>CreateObject</c>/<c>CreateArray</c> roots the base,
    /// the literal's own construction ops balance the stack back to a single value, the receiver-prefix
    /// is plain named/computed reads, the assigned value is a simple operand sub-expression, and the
    /// program ends on EXACTLY ONE <c>SetNamedProperty</c>.
    ///
    /// The <c>SetComputedProperty</c> terminal off a literal base (<c>({a:{}}).a['b'] = v</c>) is
    /// deliberately NOT admitted here: the compiler's computed-write lowering only recognizes an
    /// activation-resolved / named-prefix receiver and currently bails on a literal base with
    /// "Unsupported computed property key span." Admitting it would require compiler foundation work, so
    /// it stays declined rather than half-correct.
    ///
    /// Anything outside the named-terminal vocabulary — a call anywhere in the chain, a compound/logical
    /// write (<c>DuplicateTop</c>), a chained assignment (a second non-terminal <c>Set*</c>), an
    /// optional/short-circuit read, a private name, a method/accessor, or a name-inferred define — falls
    /// through to the default reject. So any setter in the chain runs exactly once (matching the
    /// interpreter) and the base stays pure.
    /// </summary>
    private static bool TryIsFirstBoundaryLiteralBasePropertyWriteChainCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 3)
        {
            return false;
        }

        var rootKind = program.GetOperation(0).Kind;
        if (rootKind is not (ExpressionOpKind.CreateObject or ExpressionOpKind.CreateArray))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var lastIndex = program.OperationCount - 1;
        var terminal = program.GetOperation(lastIndex);

        // The chain must end on a single NAMED property write; anything else means the trailing ops are
        // not a supported terminal store (read/call/define/compound, or a computed store the compiler
        // cannot lower off a literal base) and this is not a pure literal-base named write chain.
        if (terminal.Kind != ExpressionOpKind.SetNamedProperty ||
            terminal.AllowNameInference ||
            terminal.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        var stackDepth = 0;
        var sawWrite = false;
        for (var index = 0; index < program.OperationCount; index++)
        {
            var operation = program.GetOperation(index);
            switch (operation.Kind)
            {
                // ----- literal base construction -----
                case ExpressionOpKind.CreateObject:
                case ExpressionOpKind.CreateArray:
                    stackDepth++;
                    break;

                case ExpressionOpKind.DefineObjectProperty:
                    if (stackDepth < 2 ||
                        operation.AllowNameInference ||
                        operation.GetString(stringConstants).IsPrivateName())
                    {
                        return false;
                    }

                    stackDepth--;
                    break;

                case ExpressionOpKind.DefineComputedObjectProperty:
                    if (stackDepth < 3 || operation.AllowNameInference)
                    {
                        return false;
                    }

                    stackDepth -= 2;
                    break;

                case ExpressionOpKind.ArrayPush:
                case ExpressionOpKind.ArraySpread:
                case ExpressionOpKind.ObjectSpread:
                    if (stackDepth < 2)
                    {
                        return false;
                    }

                    stackDepth--;
                    break;

                case ExpressionOpKind.ArrayPushHole:
                    if (stackDepth < 1)
                    {
                        return false;
                    }

                    break;

                // ----- pure read hops (receiver prefix and value sub-expression reads) -----
                case ExpressionOpKind.GetNamedProperty:
                    if (operation.IsOptional ||
                        operation.ShortCircuitOnNullishTarget ||
                        operation.GetString(stringConstants).IsPrivateName() ||
                        stackDepth < 1)
                    {
                        return false;
                    }

                    break;

                case ExpressionOpKind.GetComputedProperty:
                    if (operation.ShortCircuitOnNullishTarget || stackDepth < 2)
                    {
                        return false;
                    }

                    stackDepth--;
                    break;

                case ExpressionOpKind.RequireObjectCoercible:
                    // The receiver guard for a computed hop targets the object directly below the
                    // pending key (Depth: 1); anything else is not a plain computed read/write.
                    if (operation.Depth != 1 || stackDepth < 2)
                    {
                        return false;
                    }

                    break;

                case ExpressionOpKind.ResolvePropertyKey:
                    if (stackDepth < 1)
                    {
                        return false;
                    }

                    break;

                // ----- key / value sub-expressions (shared between base members and the assigned value) -----
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

                // ----- terminal store (exactly one, the last op) -----
                case ExpressionOpKind.SetNamedProperty:
                    // [receiver, value] -> value. Only the terminal op may be a store; an earlier
                    // store means a chained assignment (`a.b = o.c = v`) which is not cleanly
                    // verifiable here, so decline.
                    if (index != lastIndex || stackDepth < 2)
                    {
                        return false;
                    }

                    stackDepth--;
                    sawWrite = true;
                    break;

                default:
                    return false;
            }
        }

        return sawWrite && stackDepth == 1;
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

        var computedPrefixEnd = 1;
        while (computedPrefixEnd < program.OperationCount &&
               IsPlainNamedPropertyReadOperandPrefix(
                   program.GetOperation(computedPrefixEnd),
                   program.StringConstants.AsSpan(),
                   allowPrivateNamedPrefix: true))
        {
            computedPrefixEnd++;
        }

        var computedSuffixStart = program.OperationCount;
        while (computedSuffixStart > computedPrefixEnd + 1 &&
               IsPlainNamedPropertyReadOperandPrefix(
                   program.GetOperation(computedSuffixStart - 1),
                   program.StringConstants.AsSpan(),
                   allowPrivateNamedPrefix: true))
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

    // A25/A26 widening: admits a DEEP, non-optional property delete chain off an activation-resolved
    // (or admitted plain dynamic-identifier) base where intermediate hops may be ANY mix of plain named
    // reads (`box.a.b`) and plain computed reads (`box[k1][k2]`), terminating in either a
    // DeleteNamedProperty or DeleteComputedProperty. This mirrors the property-read/write deep-chain
    // boundary widening for the delete family. The simpler single-hop and named-only shapes are still
    // matched first by their dedicated candidates; this walker covers the cases those reject (a computed
    // hop anywhere before the terminal delete, e.g. `delete box[k1][k2]`, `delete box.a[k1][k2]`,
    // `delete box[k1].b`).
    //
    // The whole program is validated with a stack-depth machine BEFORE any operand is trusted (the
    // emit-then-bail crash class). Optional/short-circuit hops and private names are rejected — those keep
    // their own dedicated optional-delete candidates and IR-runner fallbacks. Only sync-route opcodes that
    // already have production handlers are admitted; this stays entirely out of the resumable allowlist.
    private static bool TryIsFirstBoundaryDeepPropertyDeleteChainCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 3)
        {
            return false;
        }

        var lastOp = program.GetOperation(program.OperationCount - 1);
        if (lastOp.Kind is not (ExpressionOpKind.DeleteNamedProperty or ExpressionOpKind.DeleteComputedProperty))
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
        var stackDepth = 1;
        var computedHopCount = 0;
        var readHopCount = 0;
        for (var index = 1; index < program.OperationCount; index++)
        {
            var operation = program.GetOperation(index);
            var isLast = index == program.OperationCount - 1;
            switch (operation.Kind)
            {
                case ExpressionOpKind.GetNamedProperty:
                    if (operation.IsOptional ||
                        operation.ShortCircuitOnNullishTarget ||
                        operation.GetString(stringConstants).IsPrivateName() ||
                        stackDepth < 1)
                    {
                        return false;
                    }

                    readHopCount++;
                    break;

                case ExpressionOpKind.GetComputedProperty:
                    if (operation.IsOptional ||
                        operation.ShortCircuitOnNullishTarget ||
                        stackDepth < 2)
                    {
                        return false;
                    }

                    stackDepth--;
                    computedHopCount++;
                    readHopCount++;
                    break;

                case ExpressionOpKind.RequireObjectCoercible:
                    if (operation.Depth != 1 || stackDepth < 2)
                    {
                        return false;
                    }

                    break;

                case ExpressionOpKind.ResolvePropertyKey:
                    if (stackDepth < 1)
                    {
                        return false;
                    }

                    break;

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

                case ExpressionOpKind.DeleteNamedProperty:
                    if (!isLast ||
                        operation.IsOptional ||
                        operation.ShortCircuitOnNullishTarget ||
                        operation.GetString(stringConstants).IsPrivateName() ||
                        stackDepth < 1)
                    {
                        return false;
                    }

                    // delete consumes the receiver and pushes the boolean result (net 0).
                    break;

                case ExpressionOpKind.DeleteComputedProperty:
                    if (!isLast ||
                        operation.IsOptional ||
                        operation.ShortCircuitOnNullishTarget ||
                        stackDepth < 2)
                    {
                        return false;
                    }

                    // delete consumes the receiver and the key, pushing the boolean result (net -1).
                    stackDepth--;
                    break;

                default:
                    return false;
            }
        }

        // Require at least one computed read hop before the terminal delete; pure named chains
        // (`delete box.a.b.c`) are already covered by TryIsFirstBoundaryNamedPropertyDeleteCandidate,
        // and the single-computed-key shapes by TryIsFirstBoundaryComputedPropertyDeleteCandidate.
        // The terminal boolean must be the sole stack residue.
        return stackDepth == 1 && computedHopCount >= 1 && readHopCount >= 1;
    }

    // Admits delete a?.b[k]:
    // [activation-resolved base, GetNamedProperty(IsOptional:true, !SC, non-private),
    //  JumpIfShortCircuited, simple key, DeleteComputedProperty, Jump, Pop, true].
    // The guard skips key evaluation only when the named hop itself short-circuited, not when
    // the terminal computed receiver is nullish through an ordinary property value.
    private static bool TryIsFirstBoundaryOptionalNamedThenComputedPropertyDeleteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 8)
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
               program.GetOperation(2) is { Kind: ExpressionOpKind.JumpIfShortCircuited } jumpIfShortCircuited &&
               jumpIfShortCircuited.Target == program.OperationCount - 2 &&
               IsSupportedComputedPropertyKeySpan(
                   program,
                   startInclusive: 3,
                   endExclusive: program.OperationCount - 4,
                   identifierConstants,
                   activationSlots) &&
               program.GetOperation(program.OperationCount - 4).Kind == ExpressionOpKind.DeleteComputedProperty &&
               program.GetOperation(program.OperationCount - 3).Kind == ExpressionOpKind.Jump &&
               program.GetOperation(program.OperationCount - 3).Target == program.OperationCount &&
               program.GetOperation(program.OperationCount - 2).Kind == ExpressionOpKind.Pop &&
               IsTrueLiteral(program, program.OperationCount - 1);
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

    // Admits delete a?.b.c and delete a.b?.c.d:
    // [activation-resolved base, GetNamedProperty(non-optional, non-private)*,
    //  GetNamedProperty(IsOptional:true, !SC, non-private),
    //  JumpIfShortCircuited, DeleteNamedProperty, Jump, Pop, true].
    private static bool TryIsFirstBoundaryOptionalNamedThenNamedPropertyDeleteCandidate(
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
        var optionalHopIndex = 1;
        while (optionalHopIndex < program.OperationCount)
        {
            var operation = program.GetOperation(optionalHopIndex);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget ||
                operation.GetString(stringConstants).IsPrivateName())
            {
                break;
            }

            optionalHopIndex++;
        }

        if (optionalHopIndex >= program.OperationCount)
        {
            return false;
        }

        var optionalHop = program.GetOperation(optionalHopIndex);
        var jumpIndex = optionalHopIndex + 1;
        var deleteIndex = program.OperationCount - 4;
        var endJumpIndex = program.OperationCount - 3;
        var popIndex = program.OperationCount - 2;
        var trueIndex = program.OperationCount - 1;
        var deleteProperty = program.GetOperation(deleteIndex);

        return deleteIndex == jumpIndex + 1 &&
               optionalHop.Kind == ExpressionOpKind.GetNamedProperty &&
               optionalHop.IsOptional &&
               !optionalHop.ShortCircuitOnNullishTarget &&
               !optionalHop.GetString(stringConstants).IsPrivateName() &&
               program.GetOperation(jumpIndex) is { Kind: ExpressionOpKind.JumpIfShortCircuited } jumpIfShortCircuited &&
               jumpIfShortCircuited.Target == popIndex &&
               deleteProperty.Kind == ExpressionOpKind.DeleteNamedProperty &&
               !deleteProperty.GetString(stringConstants).IsPrivateName() &&
               program.GetOperation(endJumpIndex).Kind == ExpressionOpKind.Jump &&
               program.GetOperation(endJumpIndex).Target == program.OperationCount &&
               program.GetOperation(popIndex).Kind == ExpressionOpKind.Pop &&
               IsTrueLiteral(program, trueIndex);
    }

    // Admits delete a?.b?.c and delete a.b?.c?.d:
    // [activation-resolved base, GetNamedProperty(non-optional, non-private)*,
    //  GetNamedProperty(IsOptional:true, !SC, non-private),
    //  JumpIfNullish, DeleteNamedProperty, Jump, Pop, true].
    private static bool TryIsFirstBoundaryOptionalNamedThenOptionalNamedPropertyDeleteCandidate(
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
        var optionalHopIndex = 1;
        while (optionalHopIndex < program.OperationCount)
        {
            var operation = program.GetOperation(optionalHopIndex);
            if (operation.Kind != ExpressionOpKind.GetNamedProperty ||
                operation.IsOptional ||
                operation.ShortCircuitOnNullishTarget ||
                operation.GetString(stringConstants).IsPrivateName())
            {
                break;
            }

            optionalHopIndex++;
        }

        if (optionalHopIndex >= program.OperationCount)
        {
            return false;
        }

        var optionalHop = program.GetOperation(optionalHopIndex);
        var jumpIndex = optionalHopIndex + 1;
        var deleteIndex = program.OperationCount - 4;
        var endJumpIndex = program.OperationCount - 3;
        var popIndex = program.OperationCount - 2;
        var trueIndex = program.OperationCount - 1;
        var deleteProperty = program.GetOperation(deleteIndex);

        return deleteIndex == jumpIndex + 1 &&
               optionalHop.Kind == ExpressionOpKind.GetNamedProperty &&
               optionalHop.IsOptional &&
               !optionalHop.ShortCircuitOnNullishTarget &&
               !optionalHop.GetString(stringConstants).IsPrivateName() &&
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

    // Admits delete a?.[k1][k2] and delete a.b?.[k1][k2]:
    // [activation-resolved base, GetNamedProperty(non-optional, non-private)*,
    //  JumpIfNullish(RWU), first-key span, GetComputedProperty, JumpIfShortCircuited,
    //  terminal-key span, DeleteComputedProperty, Jump, Pop, true].
    private static bool TryIsFirstBoundaryOptionalComputedReadThenComputedPropertyDeleteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 9 ||
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

        var deleteIndex = program.OperationCount - 4;
        var endJumpIndex = program.OperationCount - 3;
        var popIndex = program.OperationCount - 2;
        var trueIndex = program.OperationCount - 1;
        if (program.GetOperation(jumpIndex) is not
                { Kind: ExpressionOpKind.JumpIfNullish, ReplaceWithUndefined: true } jumpIfNullish ||
            jumpIfNullish.Target <= jumpIndex + 1 ||
            jumpIfNullish.Target >= deleteIndex ||
            program.GetOperation(jumpIfNullish.Target) is not { Kind: ExpressionOpKind.JumpIfShortCircuited } jumpIfShortCircuited ||
            jumpIfShortCircuited.Target != popIndex ||
            program.GetOperation(deleteIndex).Kind != ExpressionOpKind.DeleteComputedProperty ||
            program.GetOperation(endJumpIndex).Kind != ExpressionOpKind.Jump ||
            program.GetOperation(endJumpIndex).Target != program.OperationCount ||
            program.GetOperation(popIndex).Kind != ExpressionOpKind.Pop ||
            !IsTrueLiteral(program, trueIndex))
        {
            return false;
        }

        var computedReadIndex = jumpIfNullish.Target - 1;
        if (computedReadIndex <= jumpIndex + 1 ||
            program.GetOperation(computedReadIndex).Kind != ExpressionOpKind.GetComputedProperty)
        {
            return false;
        }

        return IsSupportedComputedPropertyKeySpan(
                   program,
                   startInclusive: jumpIndex + 1,
                   endExclusive: computedReadIndex,
                   identifierConstants,
                   activationSlots) &&
               IsSupportedComputedPropertyKeySpan(
                   program,
                   startInclusive: jumpIfNullish.Target + 1,
                   endExclusive: deleteIndex,
                   identifierConstants,
                   activationSlots);
    }

    // Admits the simple a?.b shape: [activation-resolved base, GetNamedProperty(IsOptional:true, !ShortCircuitOnNullishTarget, non-private)].

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

        // A29: peel any trailing short-circuiting NAMED reads (`a?.[k].c`, `a?.[k]?.[j].c`).
        // The remaining span [jumpIndex, chainEnd) is exactly the one-or-more optional
        // computed hops. Every hop's JumpIfNullish boundary targets the same chainEnd.
        var chainEnd = program.OperationCount;
        while (chainEnd > jumpIndex + 2 &&
               IsShortCircuitNamedPropertyRead(program.GetOperation(chainEnd - 1), stringConstants))
        {
            chainEnd--;
        }

        // Walk the optional computed hops forward. Each hop is
        // [JumpIfNullish(ReplaceWithUndefined:true), key-span..., GetComputedProperty].
        // The first hop's read is the chain's first boundary (!ShortCircuitOnNullishTarget);
        // every subsequent hop's read short-circuits on a nullish receiver
        // (ShortCircuitOnNullishTarget:true).
        var hopIndex = jumpIndex;
        var hopCount = 0;
        while (hopIndex < chainEnd)
        {
            var hopJump = program.GetOperation(hopIndex);

            // The key span runs from just after the boundary jump up to this hop's
            // GetComputedProperty. Key spans never contain GetComputedProperty/JumpIfNullish,
            // so the next GetComputedProperty delimits the hop.
            var keyStart = hopIndex + 1;
            var computedIndex = keyStart;
            while (computedIndex < chainEnd &&
                   program.GetOperation(computedIndex).Kind != ExpressionOpKind.GetComputedProperty)
            {
                computedIndex++;
            }

            // Each hop's lowered boundary jump targets the operation immediately after its
            // OWN GetComputedProperty (the next hop's jump, or the chain end for the last hop);
            // short-circuit then cascades hop-to-hop through the successive jumps.
            if (hopJump.Kind != ExpressionOpKind.JumpIfNullish ||
                !hopJump.ReplaceWithUndefined ||
                computedIndex >= chainEnd ||
                computedIndex <= keyStart ||
                hopJump.Target != computedIndex + 1)
            {
                return false;
            }

            var getComputedOp = program.GetOperation(computedIndex);
            var expectedShortCircuit = hopCount > 0;
            if (getComputedOp.Kind != ExpressionOpKind.GetComputedProperty ||
                getComputedOp.ShortCircuitOnNullishTarget != expectedShortCircuit ||
                !IsSupportedComputedPropertyKeySpan(
                    program,
                    keyStart,
                    computedIndex,
                    identifierConstants,
                    activationSlots))
            {
                return false;
            }

            hopIndex = computedIndex + 1;
            hopCount++;
        }

        return hopCount >= 1 && hopIndex == chainEnd;
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

    private static bool TryIsEmbeddedSimpleLiteralPropertyReadOperandOperation(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (operationIndex <= 0 ||
            operationIndex >= program.OperationCount)
        {
            return false;
        }

        for (var index = 0; index < operationIndex; index++)
        {
            var operation = program.GetOperation(index);
            var isMeasuredLiteral = operation.Kind switch
            {
                ExpressionOpKind.CreateArray => TryMeasureSimpleArrayLiteralSpan(
                    program,
                    index,
                    identifierConstants,
                    activationSlots,
                    out var spanLength,
                    allowsDynamicIdentifiers) &&
                    operationIndex < index + spanLength,

                ExpressionOpKind.CreateObject => TryMeasureSimpleObjectLiteralSpan(
                    program,
                    index,
                    identifierConstants,
                    activationSlots,
                    out var spanLength,
                    allowsDynamicIdentifiers) &&
                    operationIndex < index + spanLength,

                _ => false
            };

            if (isMeasuredLiteral)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryIsEmbeddedSimplePropertyReadOperandOperation(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        bool allowImplicitArgumentsObjectPropertyReadOperands)
    {
        if (operationIndex <= 0 ||
            operationIndex >= program.OperationCount)
        {
            return false;
        }

        for (var startIndex = 0; startIndex < operationIndex; startIndex++)
        {
            var baseOperation = program.GetOperation(startIndex);
            if (!allowImplicitArgumentsObjectPropertyReadOperands &&
                baseOperation.Kind == ExpressionOpKind.LoadIdentifier &&
                IsImplicitArgumentsIdentifier(baseOperation, identifierConstants, activationSlots))
            {
                continue;
            }

            if (TryMeasureSimplePropertyReadOperandSpan(
                    program,
                    startIndex,
                    identifierConstants,
                    activationSlots,
                    out var spanLength,
                    allowsDynamicIdentifiers) &&
                operationIndex < startIndex + spanLength)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryIsEmbeddedSuperConstructPropertyReadArgumentOperation(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (operationIndex <= 0 ||
            operationIndex >= program.OperationCount)
        {
            return false;
        }

        var superConstructIndex = FindFirstOperation(program, ExpressionOpKind.SuperConstruct);
        if (superConstructIndex <= operationIndex)
        {
            return false;
        }

        for (var startIndex = 0; startIndex < operationIndex; startIndex++)
        {
            if (TryMeasureSimplePropertyReadOperandSpan(
                    program,
                    startIndex,
                    identifierConstants,
                    activationSlots,
                    out var spanLength,
                    allowsDynamicIdentifiers) &&
                operationIndex < startIndex + spanLength &&
                startIndex + spanLength <= superConstructIndex)
            {
                return true;
            }
        }

        return false;
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

    // Recognizes a continuation read that belongs to an optional-start named read
    // chain (`box?.child.value`, `box?.child?.value`) used as a call argument.
    // The span is measured by the same helper used by call-argument admission so
    // embedded dependency scanning and selector eligibility stay in lockstep.
    private static bool TryIsEmbeddedOptionalNamedReadChainCallArgumentContinuation(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (operationIndex < 2 || operationIndex >= program.OperationCount)
        {
            return false;
        }

        if (program.GetOperation(program.OperationCount - 1).Kind != ExpressionOpKind.Call)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var op = program.GetOperation(operationIndex);
        if (op.Kind != ExpressionOpKind.GetNamedProperty ||
            !op.ShortCircuitOnNullishTarget ||
            op.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        for (var spanStart = 0; spanStart < operationIndex; spanStart++)
        {
            if (TryMeasureSimpleOptionalNamedReadChainOperandSpan(
                    program,
                    spanStart,
                    identifierConstants,
                    activationSlots,
                    out var spanLength,
                    allowsDynamicIdentifiers) &&
                operationIndex >= spanStart + 2 &&
                operationIndex < spanStart + spanLength)
            {
                return true;
            }
        }

        return false;
    }

    // Recognizes a named continuation read (ShortCircuit:true) that belongs to an
    // optional-computed-start read chain (`box?.[key].value`, `box?.[key]?.value`)
    // used as a call argument. The enclosing program ends in a Call, and the full
    // operand span is remeasured so only already-admitted call-argument chains qualify.
    private static bool TryIsEmbeddedOptionalComputedReadChainCallArgumentContinuation(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (operationIndex < 1 || operationIndex >= program.OperationCount)
        {
            return false;
        }

        if (program.GetOperation(program.OperationCount - 1).Kind != ExpressionOpKind.Call)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var op = program.GetOperation(operationIndex);
        if (op.Kind != ExpressionOpKind.GetNamedProperty ||
            !op.ShortCircuitOnNullishTarget ||
            op.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        for (var spanStart = 0; spanStart < operationIndex; spanStart++)
        {
            if (TryMeasureSimpleOptionalComputedPropertyReadOperandSpan(
                    program,
                    spanStart,
                    identifierConstants,
                    activationSlots,
                    out var spanLength,
                    allowsDynamicIdentifiers) &&
                operationIndex >= spanStart + 2 &&
                operationIndex < spanStart + spanLength)
            {
                return true;
            }
        }

        return false;
    }

    // Recognizes a JumpIfNullish/GetComputedProperty operation that belongs to an
    // optional computed property-read chain (`box?.[key]`, `box?.[key]?.[key]`) used
    // as a call argument. Reuses the call-argument operand span scanner, scoped to a
    // program that ends in a Call.
    private static bool TryIsOptionalComputedReadCallArgumentOperation(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 2 ||
            program.GetOperation(program.OperationCount - 1).Kind != ExpressionOpKind.Call)
        {
            return false;
        }

        for (var spanStart = 0; spanStart <= operationIndex; spanStart++)
        {
            if (TryMeasureSimpleOptionalComputedPropertyReadOperandSpan(
                    program,
                    spanStart,
                    identifierConstants,
                    activationSlots,
                    out var spanLength,
                    allowsDynamicIdentifiers) &&
                operationIndex > spanStart &&
                operationIndex < spanStart + spanLength)
            {
                return true;
            }
        }

        return false;
    }

    // Recognizes operations that belong to an optional-named-then-computed read chain
    // (`box?.prop[key]`, `box?.prop?.[key]`, `box?.a.b[key]`) used as a call argument.
    // The enclosing program ends in a Call, and the full operand span is remeasured so
    // optional computed-hop jumps only qualify in this call-argument context.
    private static bool TryIsOptionalNamedThenComputedReadCallArgumentOperation(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (operationIndex < 0 || operationIndex >= program.OperationCount)
        {
            return false;
        }

        if (program.GetOperation(program.OperationCount - 1).Kind != ExpressionOpKind.Call)
        {
            return false;
        }

        for (var spanStart = 0; spanStart <= operationIndex; spanStart++)
        {
            if (TryMeasureSimpleOptionalNamedThenComputedReadOperandSpan(
                    program,
                    spanStart,
                    identifierConstants,
                    activationSlots,
                    out var spanLength,
                    allowsDynamicIdentifiers) &&
                operationIndex > spanStart &&
                operationIndex < spanStart + spanLength)
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
        bool allowsDynamicIdentifiers,
        // A30 sync-only widening: the optional-computed-START call shapes below are admitted only
        // when this flag is set (the synchronous production route). The resumable route passes false
        // so those shapes keep declining as OptionalChainDependency at the resumable plan walk.
        bool allowSyncOnlyOptionalComputedStartCalls = false)
    {
        if (program.OperationCount < 2)
        {
            return false;
        }

        if (allowSyncOnlyOptionalComputedStartCalls &&
            (TryIsFirstBoundaryOptionalComputedStartPlainCallCandidate(
                 program,
                 identifierConstants,
                 activationSlots,
                 allowsDynamicIdentifiers) ||
             TryIsFirstBoundaryOptionalChainComputedReceiverOptionalCallCandidate(
                 program,
                 identifierConstants,
                 stringConstants,
                 activationSlots,
                 allowsDynamicIdentifiers)))
        {
            return true;
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
            TryIsFirstBoundaryReceiverOptionalNamedCallCandidate(
                program,
                identifierConstants,
                stringConstants,
                activationSlots,
                allowsDynamicIdentifiers) ||
            TryIsFirstBoundaryCalleeOptionalNamedCallCandidate(
                program,
                identifierConstants,
                stringConstants,
                activationSlots,
                allowsDynamicIdentifiers) ||
            TryIsFirstBoundaryCalleeOptionalComputedCallCandidate(
                program,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers) ||
            TryIsFirstBoundaryOptionalChainPlainCallCandidate(
                program,
                identifierConstants,
                stringConstants,
                activationSlots,
                allowsDynamicIdentifiers) ||
            TryIsFirstBoundaryOptionalChainReceiverOptionalCallCandidate(
                program,
                identifierConstants,
                stringConstants,
                activationSlots,
                allowsDynamicIdentifiers) ||
            TryIsFirstBoundaryOptionalChainComputedPlainCallCandidate(
                program,
                identifierConstants,
                stringConstants,
                activationSlots,
                allowsDynamicIdentifiers))
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
            var keyStart = program.GetOperation(0).Kind == ExpressionOpKind.EnsureSuperReference ? 1 : 0;
            var keyEnd = computedSuperCallTargetIndex;
            if (program.GetOperation(keyEnd - 1).Kind == ExpressionOpKind.EnsureSuperReference)
            {
                keyEnd--;
            }

            var hasResolvedKey = keyEnd == keyStart + 2 &&
                                 program.GetOperation(keyStart + 1).Kind == ExpressionOpKind.ResolvePropertyKey;
            if (keyEnd != keyStart + 1 && !hasResolvedKey)
            {
                return false;
            }

            return IsSimpleComputedPropertyKey(
                       program.GetOperation(keyStart),
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
                       allowDeepChain: true,
                       allowsDynamicIdentifiers) &&
                   HasSimpleCallArguments(
                       program,
                       identifierConstants,
                       activationSlots,
                       namedCallTargetIndex + 1,
                       call,
                       allowsDynamicIdentifiers);
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
                       allowDeepChain: true,
                       allowsDynamicIdentifiers) &&
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
                       call,
                       allowsDynamicIdentifiers);
        }

        return false;
    }

    private static bool TryIsGeneralIdentifierCallExpressionCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 2)
        {
            return false;
        }

        var call = program.GetOperation(program.OperationCount - 1);
        if (call.Kind != ExpressionOpKind.Call ||
            !call.HasExplicitThis ||
            call.IsDirectEval ||
            call.SpreadMaskConstantIndex >= 0)
        {
            return false;
        }

        var callTarget = program.GetOperation(0);
        if (callTarget.Kind != ExpressionOpKind.LoadIdentifierCallTarget)
        {
            return false;
        }

        var identifier = callTarget.GetIdentifier(identifierConstants);
        return TryResolveActivationSlot(identifier, activationSlots) ||
               allowsDynamicIdentifiers ||
               CanUseMaterializedActivationDynamicLookup(identifier, activationSlots);
    }

    private static bool TryIsGeneralNamedMemberCallExpressionCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        // Walk the whole program with the canonical admitted-argument operand-stack model. This
        // models named AND computed member/property reads, member-call-target preparations, and
        // Call boundaries with exact deltas, so it admits not only the flat member-call shape
        // (`o.m(args)`) but also chained method/computed calls past the first invocation boundary
        // (`a.b().c()`, `o.m().n()`, `o.a()[k]()`) — A12. The trailing Call is the final invocation;
        // at least one Call must occur (so a non-call read chain does not match), and the whole
        // program must leave exactly one operand on the stack.
        var depth = 0;
        var hasCall = false;
        const int DynamicIdentifierReferenceSlot = -1;
        List<int>? identifierReferenceSlots = null;
        for (var operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
        {
            if (program.GetOperation(operationIndex).Kind == ExpressionOpKind.Call)
            {
                hasCall = true;
            }

            if (!TryApplyAdmittedArgumentOpStackDelta(
                    program,
                    operationIndex,
                    identifierConstants,
                    stringConstants,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    DynamicIdentifierReferenceSlot,
                    ref identifierReferenceSlots,
                    depth,
                    out depth))
            {
                return false;
            }
        }

        return hasCall && depth == 1 && identifierReferenceSlots is not { Count: > 0 };
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
        if (callTarget.Kind != ExpressionOpKind.LoadIdentifierCallTarget)
        {
            return false;
        }

        var identifier = callTarget.GetIdentifier(identifierConstants);
        return string.Equals(identifier.Name.Name, "eval", StringComparison.Ordinal) &&
               IsDirectEvalSingleArgumentCandidate(program, program.GetOperation(1));
    }

    private static bool IsDirectEvalSingleArgumentCandidate(ExpressionProgram program, PackedExpressionOp operation)
    {
        if (operation.Kind != ExpressionOpKind.LoadLiteral)
        {
            return false;
        }

        var literal = operation.GetLiteral(program.LiteralConstants.AsSpan());
        return !literal.IsString ||
               !ContainsEvalDeclarationKeyword(literal.AsString());
    }

    private static bool ContainsEvalDeclarationKeyword(string source)
    {
        return ContainsKeyword(source, "var") ||
               ContainsKeyword(source, "let") ||
               ContainsKeyword(source, "const") ||
               ContainsKeyword(source, "function") ||
               ContainsKeyword(source, "class");
    }

    private static bool ContainsKeyword(string source, string keyword)
    {
        var index = 0;
        while (index < source.Length)
        {
            index = source.IndexOf(keyword, index, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var before = index == 0 ? '\0' : source[index - 1];
            var afterIndex = index + keyword.Length;
            var after = afterIndex >= source.Length ? '\0' : source[afterIndex];
            if (!IsIdentifierPart(before) && !IsIdentifierPart(after))
            {
                return true;
            }

            index = afterIndex;
        }

        return false;
    }

    private static bool IsIdentifierPart(char value) =>
        value == '_' ||
        value == '$' ||
        char.IsAsciiLetterOrDigit(value);

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
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
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
                   callIndex,
                   allowsDynamicIdentifiers);
    }

    // Case 2: box.read?.() — callee-optional named call
    // Expression program: [Receiver..., LoadNamedCallTarget, JumpIfNullish, args..., Call, Jump, SwapTopTwo, Pop]
    private static bool TryIsFirstBoundaryCalleeOptionalNamedCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
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
                   callIndex,
                   allowsDynamicIdentifiers);
    }

    // Case 3: box[key]?.() — callee-optional computed call
    // Expression program: [Receiver, Key, LoadComputedCallTarget, JumpIfNullish, args..., Call, Jump, SwapTopTwo, Pop]
    private static bool TryIsFirstBoundaryCalleeOptionalComputedCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
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
                   callIndex,
                   allowsDynamicIdentifiers);
    }

    // Case 4: a?.b.c() / a.x?.b.c() — optional-start chain, plain non-optional call
    // Expression program: [base/prefix..., GetNamedProperty(IsOptional:true,b), JumpIfShortCircuited,
    //                       LoadNamedCallTarget(c), args..., Call]
    private static bool TryIsFirstBoundaryOptionalChainPlainCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
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

        return HasSimpleCallArguments(
            program,
            identifierConstants,
            activationSlots,
            namedCallTargetIndex + 1,
            call,
            allowsDynamicIdentifiers);
    }

    // Case 5: a?.b?.c() — double-optional chain, receiver-optional call
    // Expression program: [base(0), GetNamedProperty(IsOptional:true,b)(1), JumpIfShortCircuited(2),
    //                       JumpIfNullish(ReplaceWithUndefined:true)(3), LoadNamedCallTarget(c)(4), args..., Call]
    private static bool TryIsFirstBoundaryOptionalChainReceiverOptionalCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
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

        return HasSimpleCallArguments(
            program,
            identifierConstants,
            activationSlots,
            namedCallTargetIndex + 1,
            call,
            allowsDynamicIdentifiers);
    }

    // Case 6: a?.b[k]() — optional-start chain, computed plain non-optional call
    // Expression program: [base(0), GetNamedProperty(IsOptional:true,b)(1), JumpIfShortCircuited(2),
    //                       key(3), LoadComputedCallTarget(4), args..., Call]
    private static bool TryIsFirstBoundaryOptionalChainComputedPlainCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
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
               HasSimpleCallArguments(
                   program,
                   identifierConstants,
                   activationSlots,
                   computedCallTargetIndex + 1,
                   call,
                   allowsDynamicIdentifiers);
    }

    // Case 7 (A30): o?.[k]() — optional-computed-START chain, plain non-optional computed call.
    // The leading optional hop is the computed receiver itself; it lowers to a JumpIfNullish that
    // replaces the whole chain with undefined and targets the program end (the same chain-end short
    // circuit Case 6 reaches via JumpIfShortCircuited for an optional-NAMED start). This is the
    // computed-start twin of Case 6's `a?.b[k]()` and the leading-hop twin of Case 3's `box[key]?.()`.
    // Expression program: [base(0), JumpIfNullish(ReplaceWithUndefined:true,target=End)(1),
    //                       key(2), LoadComputedCallTarget(!opt,!sc)(3), args..., Call]
    private static bool TryIsFirstBoundaryOptionalComputedStartPlainCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        // Minimum: [base, JumpIfNullish, key, LoadComputedCallTarget, Call] = 5
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

        // op[3] = LoadComputedCallTarget(!opt, !sc) must be at index 3 exactly so the leading
        // optional hop is the only short-circuit before the call target.
        var computedCallTargetIndex = FindFirstOperation(program, ExpressionOpKind.LoadComputedCallTarget);
        if (computedCallTargetIndex != 3)
        {
            return false;
        }

        var computedCallTarget = program.GetOperation(computedCallTargetIndex);
        if (computedCallTarget.IsOptional || computedCallTarget.ShortCircuitOnNullishTarget)
        {
            return false;
        }

        // op[1] = JumpIfNullish(ReplaceWithUndefined) — the leading `o?.` short circuit. It must
        // jump to the program end so a nullish receiver short-circuits the WHOLE call to undefined
        // (the call is never made), matching the optional-chain semantics.
        var jumpNullish = program.GetOperation(1);
        if (jumpNullish.Kind != ExpressionOpKind.JumpIfNullish ||
            !jumpNullish.ReplaceWithUndefined ||
            jumpNullish.Target != program.OperationCount)
        {
            return false;
        }

        // op[0] = activation-resolved (or, when allowed, plain dynamic) receiver base.
        var baseOperation = program.GetOperation(0);
        if (!TryGetActivationResolvedValue(baseOperation, identifierConstants, activationSlots) &&
            !(allowsDynamicIdentifiers &&
              TryGetPlainDynamicIdentifierReadValue(baseOperation, identifierConstants, activationSlots)))
        {
            return false;
        }

        // op[2] = simple computed key.
        if (!IsSimpleComputedPropertyKey(
                program.GetOperation(2),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        return HasSimpleCallArguments(
            program,
            identifierConstants,
            activationSlots,
            computedCallTargetIndex + 1,
            call,
            allowsDynamicIdentifiers);
    }

    // Case 8 (A30): a?.b?.[k]() — double-optional chain (optional-named start, optional-computed
    // continuation), plain non-optional call. Computed-key twin of Case 5's `a?.b?.c()`: the first
    // optional hop reads `b` and provenance-short-circuits via JumpIfShortCircuited, the second
    // optional hop (`?.[k]`) short-circuits via JumpIfNullish; both target the program end so any
    // nullish hop short-circuits the WHOLE call to undefined (the call is never made).
    // Expression program: [base(0), GetNamedProperty(IsOptional:true,b)(1), JumpIfShortCircuited(2),
    //                       JumpIfNullish(ReplaceWithUndefined:true,target=End)(3), key(4),
    //                       LoadComputedCallTarget(!opt,!sc)(5), args..., Call]
    private static bool TryIsFirstBoundaryOptionalChainComputedReceiverOptionalCallCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        // Minimum: [base, GetNamedProperty, JumpIfShortCircuited, JumpIfNullish, key,
        //           LoadComputedCallTarget, Call] = 7
        if (program.OperationCount < 7)
        {
            return false;
        }

        var callIndex = program.OperationCount - 1;
        var call = program.GetOperation(callIndex);
        if (call.Kind != ExpressionOpKind.Call || !call.HasExplicitThis || call.IsDirectEval)
        {
            return false;
        }

        // LoadComputedCallTarget must be at index 5 exactly.
        var computedCallTargetIndex = FindFirstOperation(program, ExpressionOpKind.LoadComputedCallTarget);
        if (computedCallTargetIndex != 5)
        {
            return false;
        }

        var computedCallTarget = program.GetOperation(computedCallTargetIndex);
        if (computedCallTarget.IsOptional || computedCallTarget.ShortCircuitOnNullishTarget)
        {
            return false;
        }

        // op[3] = JumpIfNullish(ReplaceWithUndefined) targeting the program end.
        var jumpNullish = program.GetOperation(3);
        if (jumpNullish.Kind != ExpressionOpKind.JumpIfNullish ||
            !jumpNullish.ReplaceWithUndefined ||
            jumpNullish.Target != program.OperationCount)
        {
            return false;
        }

        // op[2] = JumpIfShortCircuited (first-hop short-circuit provenance).
        if (program.GetOperation(2).Kind != ExpressionOpKind.JumpIfShortCircuited)
        {
            return false;
        }

        // op[1] = GetNamedProperty(IsOptional:true, !SC, non-private) — the leading `a?.b` hop.
        var firstHop = program.GetOperation(1);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        // op[0] = activation-resolved base.
        if (!TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        // op[4] = simple computed key.
        if (!IsSimpleComputedPropertyKey(
                program.GetOperation(4),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        return HasSimpleCallArguments(
            program,
            identifierConstants,
            activationSlots,
            computedCallTargetIndex + 1,
            call,
            allowsDynamicIdentifiers);
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
        bool allowDeepChain,
        bool allowsDynamicIdentifiers = false)
    {
        if (endExclusive < 1 || (!allowDeepChain && endExclusive > 3))
        {
            return false;
        }

        var firstOperation = program.GetOperation(0);
        if (!TryGetActivationResolvedValue(firstOperation, identifierConstants, activationSlots) &&
            !(allowsDynamicIdentifiers &&
              TryGetPlainDynamicIdentifierReadValue(firstOperation, identifierConstants, activationSlots)))
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
            ExpressionOpKind.LoadIdentifier => TryGetActivationOrImplicitArgumentsObjectReadValue(
                operation,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers),
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
        if (startInclusive >= endExclusive)
        {
            return false;
        }

        // Control-expression computed keys (`box[cond ? a : b]`, `box[a && b]`,
        // `box[a ?? b]`) lower to JumpIfConditionalFalse/Jump/Pop control flow that the
        // stack-machine walker below cannot validate. Accept the key span when the
        // entire range is exactly one already-admitted control-expression operand span;
        // the VM executes these branches through the same general expression loop used
        // for control-expression operands elsewhere, leaving a single key value on the
        // stack. Only a whole-span match is admitted so no interleaved/partial shapes
        // slip through.
        if (TryMeasureSimpleControlExpressionOperandSpan(
                program,
                startInclusive,
                identifierConstants,
                activationSlots,
                out var controlExpressionSpanLength,
                allowsDynamicIdentifiers) &&
            startInclusive + controlExpressionSpanLength == endExclusive)
        {
            return true;
        }

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

    private static bool IsPlainNamedPropertyReadOperandPrefix(
        PackedExpressionOp operation,
        ReadOnlySpan<string> stringConstants,
        bool allowPrivateNamedPrefix)
    {
        return operation.Kind == ExpressionOpKind.GetNamedProperty &&
               !operation.IsOptional &&
               !operation.ShortCircuitOnNullishTarget &&
               (allowPrivateNamedPrefix || !operation.GetString(stringConstants).IsPrivateName());
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
                else if (TryMeasureSimplePropertyReadOperandSpan(
                             program,
                             operationIndex,
                             identifierConstants,
                             activationSlots,
                             out var propertyReadSpanLen,
                             allowsDynamicIdentifiers) &&
                         operationIndex + propertyReadSpanLen <= callIndex)
                {
                    operationIndex += propertyReadSpanLen;
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
            else if (TryMeasureSimplePropertyReadOperandSpan(
                         program,
                         operationIndex,
                         identifierConstants,
                         activationSlots,
                         out var propertyReadSpanLen,
                         allowsDynamicIdentifiers) &&
                     operationIndex + propertyReadSpanLen <= callIndex)
            {
                operationIndex += propertyReadSpanLen;
            }
            else if (TryMeasureSimpleOptionalNamedThenComputedReadOperandSpan(
                         program,
                         operationIndex,
                         identifierConstants,
                         activationSlots,
                         out var optionalNamedThenComputedSpanLen,
                         allowsDynamicIdentifiers) &&
                     operationIndex + optionalNamedThenComputedSpanLen <= callIndex)
            {
                operationIndex += optionalNamedThenComputedSpanLen;
            }
            else if (TryMeasureSimpleOptionalNamedReadChainOperandSpan(
                         program,
                         operationIndex,
                         identifierConstants,
                         activationSlots,
                         out var optionalNamedChainSpanLen,
                         allowsDynamicIdentifiers) &&
                     operationIndex + optionalNamedChainSpanLen <= callIndex)
            {
                operationIndex += optionalNamedChainSpanLen;
            }
            else if (TryMeasureSimpleOptionalComputedPropertyReadOperandSpan(
                         program,
                         operationIndex,
                         identifierConstants,
                         activationSlots,
                         out var optionalComputedSpanLen,
                         allowsDynamicIdentifiers) &&
                     operationIndex + optionalComputedSpanLen <= callIndex)
            {
                operationIndex += optionalComputedSpanLen;
            }
            else if (TryMeasureSimpleOptionalNamedPropertyReadOperandSpan(
                         program,
                         operationIndex,
                         identifierConstants,
                         activationSlots,
                         out var optionalNamedReadSpanLen,
                         allowsDynamicIdentifiers) &&
                     operationIndex + optionalNamedReadSpanLen <= callIndex)
            {
                operationIndex += optionalNamedReadSpanLen;
            }
            else if (TryMeasureSimpleUnaryOperandSpan(
                         program,
                         operationIndex,
                         identifierConstants,
                         activationSlots,
                         out var unarySpanLen,
                         allowsDynamicIdentifiers) &&
                     operationIndex + unarySpanLen <= callIndex)
            {
                operationIndex += unarySpanLen;
            }
            else if (TryMeasureSimpleTypeOfOperandSpan(
                         program,
                         operationIndex,
                         identifierConstants,
                         activationSlots,
                         out var typeOfSpanLen,
                         allowsDynamicIdentifiers) &&
                     operationIndex + typeOfSpanLen <= callIndex)
            {
                operationIndex += typeOfSpanLen;
            }
            else if (IsSimpleOperand(op, identifierConstants, activationSlots, allowsDynamicIdentifiers))
            {
                operationIndex++;
            }
            // A11: complex call arguments. The flat measurers above handle a leaf operand,
            // a binary/unary of SIMPLE operands, an admitted member-read span, and literals.
            // Anything richer — a NESTED CALL (`g(h(x))`), a binary whose operand is itself a
            // call (`g(a + h(b))`), a member call argument (`g(o.m(x))`), or any deeper
            // composition of already-admitted value-producing ops — is not splittable by the
            // greedy per-argument measurers above (a postfix operator can read a value that was
            // pushed several ops earlier). Re-validate the WHOLE argument region with the general
            // operand-stack walker, which tracks net stack depth exactly as the production VM does
            // and requires the region to leave one operand per logical argument. Walking forward
            // over the in-evaluation-order op stream preserves left-to-right argument evaluation
            // (each argument fully evaluated before the next, then the call).
            else
            {
                return TryValidateAdmittedComplexCallArgumentRegion(
                    program,
                    argsStartIndex,
                    callIndex,
                    call.ArgumentCount,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers);
            }

            argCount++;
        }

        return argCount == call.ArgumentCount;
    }

    // A11: general operand-stack walker for a SINGLE complex call-argument value span.
    //
    // Measures exactly one value-producing expression starting at <paramref name="startIndex"/>
    // and ending strictly before <paramref name="endExclusive"/> (the outer call's Call op index).
    // It simulates the operand-stack depth the production VM maintains (the same delta the VM
    // applies per op), so the span it returns is the minimal prefix that leaves exactly one net
    // value on the stack — i.e. one complete sub-expression. Because the op stream is already in
    // evaluation order, walking it forward preserves left-to-right evaluation and guarantees each
    // argument is fully evaluated before the next (and before the outer Call). Admitted ops:
    //   * leaf/multi-op value spans the flat measurers already accept (simple operand, literal,
    //     array/object/template literal, member-read span, control expression) — consumed whole
    //     and treated as a single +1,
    //   * property reads (named 0, computed -1), unary/typeof (0), binary (-1),
    //   * NESTED CALLS — identifier (LoadIdentifierCallTarget +2), named-member
    //     (LoadNamedCallTarget +1), computed-member (LoadComputedCallTarget 0), and the trailing
    //     Call (-(argc+1)) — so an argument may itself be `g(h(x))`, `o.m(x)`, `o[k](x)`, or any
    //     composition thereof.
    // Optional/short-circuit call and member shapes (JumpIfNullish-bearing) are intentionally NOT
    // walked here: their control flow is owned by the dedicated optional-chain span measurers in
    // the loop above, so an argument containing one falls back to the interpreter (the boundary
    // degrades correctly rather than over-admitting).
    internal static bool TryValidateAdmittedComplexCallArgumentRegion(
        ExpressionProgram program,
        int argsStartIndex,
        int callIndex,
        int expectedArgumentCount,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (argsStartIndex < 0 || argsStartIndex > callIndex)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var depth = 0;
        var index = argsStartIndex;
        const int DynamicIdentifierReferenceSlot = -1;
        List<int>? identifierReferenceSlots = null;
        while (index < callIndex)
        {
            // At each position first try the existing flat value-span measurers. Each one validates
            // a COMPLETE self-contained value (a leaf operand, an array/object/template literal, a
            // member-read chain, a control expression, or a member call with simple args) and nets
            // exactly +1 on the operand stack. Consuming a whole value here lets the per-op deltas
            // below model the genuinely-nested compositions (binaries/unaries/nested calls) whose
            // operands are themselves such values.
            if (TryMeasureSimpleLiteralValueOperandSpan(
                    program,
                    index,
                    identifierConstants,
                    activationSlots,
                    out var literalSpan,
                    allowsDynamicIdentifiers) &&
                index + literalSpan <= callIndex)
            {
                index += literalSpan;
                depth++;
                continue;
            }

            if (TryMeasureSimpleTypeOfOperandSpan(
                    program,
                    index,
                    identifierConstants,
                    activationSlots,
                    out var typeOfSpan,
                    allowsDynamicIdentifiers) &&
                index + typeOfSpan <= callIndex)
            {
                index += typeOfSpan;
                depth++;
                continue;
            }

            if (!TryApplyAdmittedArgumentOpStackDelta(
                    program,
                    index,
                    identifierConstants,
                    stringConstants,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    DynamicIdentifierReferenceSlot,
                    ref identifierReferenceSlots,
                    depth,
                    out depth))
            {
                return false;
            }

            index++;
        }

        // The whole argument region must leave exactly one operand per logical argument on the
        // stack (and the per-op deltas above never underflowed), matching the call's arity.
        return depth == expectedArgumentCount &&
               identifierReferenceSlots is not { Count: > 0 };
    }

    // Validates a SINGLE op at <paramref name="index"/> as an admitted value-producing argument
    // op and applies its net operand-stack delta to <paramref name="depthBefore"/>. Returns false
    // (declining the whole argument span) for any op outside the admitted vocabulary or any op
    // whose pop would underflow the current span's operand stack.
    private static bool TryApplyAdmittedArgumentOpStackDelta(
        ExpressionProgram program,
        int index,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ReadOnlySpan<string> stringConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers,
        int dynamicIdentifierReferenceSlot,
        ref List<int>? identifierReferenceSlots,
        int depthBefore,
        out int depthAfter)
    {
        depthAfter = depthBefore;
        var op = program.GetOperation(index);
        switch (op.Kind)
        {
            case ExpressionOpKind.LoadLiteral:
            case ExpressionOpKind.LoadThis:
            case ExpressionOpKind.LoadNewTarget:
            case ExpressionOpKind.LoadTemplateObject:
                depthAfter = depthBefore + 1;
                return true;

            case ExpressionOpKind.LoadIdentifier:
                if (!IsSimpleOperand(op, identifierConstants, activationSlots, allowsDynamicIdentifiers))
                {
                    return false;
                }

                depthAfter = depthBefore + 1;
                return true;

            case ExpressionOpKind.LoadFunctionLiteral:
                var descriptor = op.GetObject<FunctionLiteralDescriptor>(program.ObjectConstants.AsSpan());
                if (FunctionLiteralNeedsLexicalThisOrPrivateNameContext(descriptor.Function, out _))
                {
                    return false;
                }

                depthAfter = depthBefore + 1;
                return true;

            case ExpressionOpKind.GetNamedProperty:
                // receiver -> value: net 0. Reject private/optional/short-circuit reads.
                if (depthBefore < 1 ||
                    op.IsOptional ||
                    op.ShortCircuitOnNullishTarget ||
                    op.GetString(stringConstants).IsPrivateName())
                {
                    return false;
                }

                return true;

            case ExpressionOpKind.GetComputedProperty:
                // receiver, key -> value: net -1. Reject optional/short-circuit reads.
                if (depthBefore < 2 || op.IsOptional || op.ShortCircuitOnNullishTarget)
                {
                    return false;
                }

                depthAfter = depthBefore - 1;
                return true;

            case ExpressionOpKind.ResolvePropertyKey:
                // key -> coerced key, in place: net 0.
                return depthBefore >= 1;

            case ExpressionOpKind.EnsureSuperReference:
                // This-initialization check for computed super keys; no stack effect.
                return true;

            case ExpressionOpKind.Binary:
                if (depthBefore < 2 || !IsProductionBinaryOperator(op.Operator))
                {
                    return false;
                }

                depthAfter = depthBefore - 1;
                return true;

            case ExpressionOpKind.ResolveIdentifierReference:
                if (op.IsArguments)
                {
                    if (!allowsDynamicIdentifiers)
                    {
                        return false;
                    }

                    identifierReferenceSlots ??= [];
                    identifierReferenceSlots.Add(dynamicIdentifierReferenceSlot);
                    return true;
                }

                var referenceIdentifier = op.GetIdentifier(identifierConstants);
                if (TryResolveExplicitActivationSlot(referenceIdentifier, activationSlots, out var referenceSlotIndex))
                {
                    identifierReferenceSlots ??= [];
                    identifierReferenceSlots.Add(referenceSlotIndex);
                    return true;
                }

                if (TryResolveActivationSlot(referenceIdentifier, activationSlots))
                {
                    return false;
                }

                if (!allowsDynamicIdentifiers)
                {
                    return false;
                }

                identifierReferenceSlots ??= [];
                identifierReferenceSlots.Add(dynamicIdentifierReferenceSlot);
                return true;

            case ExpressionOpKind.LoadResolvedIdentifierValue:
                if (identifierReferenceSlots is not { Count: > 0 })
                {
                    return false;
                }

                depthAfter = depthBefore + 1;
                return identifierReferenceSlots[^1] >= 0 || allowsDynamicIdentifiers;

            case ExpressionOpKind.StoreResolvedIdentifier:
                if (depthBefore < 1)
                {
                    return false;
                }

                var storeReferenceIdentifier = op.GetIdentifier(identifierConstants);
                if (op.IsArguments)
                {
                    if (!allowsDynamicIdentifiers ||
                        identifierReferenceSlots is not { Count: > 0 } ||
                        identifierReferenceSlots[^1] != dynamicIdentifierReferenceSlot)
                    {
                        return false;
                    }

                    identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                    return true;
                }

                if (TryResolveExplicitActivationSlot(
                        storeReferenceIdentifier,
                        activationSlots,
                        out var storeReferenceSlotIndex))
                {
                    if (identifierReferenceSlots is not { Count: > 0 } ||
                        identifierReferenceSlots[^1] != storeReferenceSlotIndex)
                    {
                        return false;
                    }

                    identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                    return true;
                }

                if (TryResolveActivationSlot(storeReferenceIdentifier, activationSlots) ||
                    !allowsDynamicIdentifiers ||
                    identifierReferenceSlots is not { Count: > 0 } ||
                    identifierReferenceSlots[^1] != dynamicIdentifierReferenceSlot)
                {
                    return false;
                }

                identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                return true;

            case ExpressionOpKind.PopResolvedIdentifierReference:
                if (identifierReferenceSlots is not { Count: > 0 })
                {
                    return false;
                }

                var pendingReferenceSlot = identifierReferenceSlots[^1];
                identifierReferenceSlots.RemoveAt(identifierReferenceSlots.Count - 1);
                return pendingReferenceSlot >= 0 || allowsDynamicIdentifiers;

            case ExpressionOpKind.UnaryPlus:
            case ExpressionOpKind.UnaryMinus:
            case ExpressionOpKind.UnaryLogicalNot:
            case ExpressionOpKind.UnaryBitwiseNot:
            case ExpressionOpKind.UnaryVoid:
            case ExpressionOpKind.TypeOf:
                // operand -> result, in place: net 0.
                return depthBefore >= 1;

            case ExpressionOpKind.LoadIdentifierCallTarget:
                // Pushes <undefined this, callee>: net +2. Decline eval and unresolved
                // free identifiers unless dynamic identifier operations are admitted.

                var callIdentifier = op.GetIdentifier(identifierConstants);
                if (string.Equals(callIdentifier.Name.Name, "eval", StringComparison.Ordinal))
                {
                    return false;
                }

                if (!TryResolveActivationSlot(callIdentifier, activationSlots) &&
                    !allowsDynamicIdentifiers &&
                    !CanUseMaterializedActivationDynamicLookup(callIdentifier, activationSlots))
                {
                    return false;
                }

                depthAfter = depthBefore + 2;
                return true;

            case ExpressionOpKind.LoadNamedCallTarget:
                // receiver stays as `this`, callee pushed: net +1. Reject optional.
                if (depthBefore < 1 ||
                    op.IsOptional ||
                    op.ShortCircuitOnNullishTarget)
                {
                    return false;
                }

                depthAfter = depthBefore + 1;
                return true;

            case ExpressionOpKind.LoadComputedCallTarget:
                // pop key, keep receiver as `this`, push callee: net 0. Reject optional.
                if (depthBefore < 2 || op.IsOptional || op.ShortCircuitOnNullishTarget)
                {
                    return false;
                }

                return true;

            case ExpressionOpKind.LoadNamedSuperCallTarget:
                // Pushes <super receiver, callee>: net +2. Reject private names.
                if (op.GetString(stringConstants).IsPrivateName())
                {
                    return false;
                }

                depthAfter = depthBefore + 2;
                return true;

            case ExpressionOpKind.LoadComputedSuperCallTarget:
                // pop key, push <super receiver, callee>: net +1.
                if (depthBefore < 1)
                {
                    return false;
                }

                depthAfter = depthBefore + 1;
                return true;

            case ExpressionOpKind.Call:
                // Explicit-this calls pop <this, callee, arg0..arg(n-1)> and push the result:
                // net -(argc + 1). Bare calls pop <callee, arg0..arg(n-1)> and push the result:
                // net -argc, with undefined supplied as the receiver by the invocation boundary.
                // Reject spread/eval; require enough operands on the stack for the selected shape.
                var requiredOperands = op.ArgumentCount + (op.HasExplicitThis ? 2 : 1);
                if (op.IsDirectEval ||
                    op.SpreadMaskConstantIndex >= 0 ||
                    depthBefore < requiredOperands)
                {
                    return false;
                }

                depthAfter = depthBefore - (op.ArgumentCount + (op.HasExplicitThis ? 1 : 0));
                return true;

            default:
                return false;
        }
    }

    // Measures the op span for a simple array literal starting at startIndex.
    // Admitted shapes (CreateArray followed by N >= 0 elements, each one of):
    //   Normal:  [simple-literal-value-span, ArrayPush]
    //   Spread:  [simple-literal-value-span, ArraySpread]
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

            if (TryMeasureSimpleLiteralValueOperandSpan(
                    program,
                    i,
                    identifierConstants,
                    activationSlots,
                    out var elementSpanLength,
                    allowsDynamicIdentifiers) &&
                i + elementSpanLength < program.OperationCount &&
                program.GetOperation(i + elementSpanLength).Kind
                    is ExpressionOpKind.ArrayPush or ExpressionOpKind.ArraySpread)
            {
                // A35: the value span may now greedily match a bare-identifier-call prefix (`g()`); if
                // the terminator is NOT a push/spread (e.g. `[...g().items]`, where the call is a SPREAD
                // SOURCE base followed by a property read), fall through to the spread-source branch below
                // rather than declining the whole array literal.
                i += elementSpanLength + 1;
                continue;
            }

            // A33: spread sources accept a wider operand set than push sources —
            // a bare identifier call (`[...f()]`, `[...gen()]`) or a property read
            // off a call (`[...f().items]`). These shapes are admitted ONLY when the
            // terminating op is ArraySpread; a non-spread terminator ends the literal
            // scan so the regular-element gate decides eligibility.
            if (TryMeasureSimpleSpreadSourceOperandSpan(
                    program,
                    i,
                    identifierConstants,
                    activationSlots,
                    out var spreadSourceSpanLength,
                    allowsDynamicIdentifiers))
            {
                var spreadIndex = i + spreadSourceSpanLength;
                if (spreadIndex < program.OperationCount &&
                    program.GetOperation(spreadIndex).Kind == ExpressionOpKind.ArraySpread)
                {
                    i = spreadIndex + 1;
                    continue;
                }
            }

            // Non-simple op terminates the element scan — the array literal ends here.
            break;
        }

        spanLength = i - startIndex;
        return true;
    }

    // A33/A34: Measures the op span for a non-simple spread *source* operand.
    // This is intentionally wider than TryMeasureSimpleLiteralValueOperandSpan (which
    // gates regular array-push elements / object property values) and is consulted ONLY
    // when the terminating op is ArraySpread (A33) or ObjectSpread (A34). Admitted shapes:
    //   - bare identifier call:          `[...f()]`, `{...f()}`, `[...gen()]`
    //   - property read off a call base:  `[...f().items]`, `{...f().items}`, `{...o.m().a}`
    // The trailing spread opcode consumes whatever value the source span leaves on the
    // stack — ArraySpread runs the iterator protocol (throwing on a non-iterable);
    // ObjectSpread copies the source's own enumerable properties (a no-op for
    // null/undefined). Either way any source span built from already-admitted VM opcodes
    // (identifier/member call, plain named property read) is safe to spread. Other shapes
    // (already covered by the simple-literal-value span) and anything not verifiable here
    // are declined.
    private static bool TryMeasureSimpleSpreadSourceOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        spanLength = 0;

        // Base: a bare identifier call (`f()`) or an admitted member call (`o.m()`).
        int baseSpanLength;
        if (TryMeasureSimpleIdentifierCallOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out baseSpanLength,
                allowsDynamicIdentifiers))
        {
            // ok
        }
        else if (TryMeasureSimpleMemberCallOperandSpan(
                     program,
                     startIndex,
                     identifierConstants,
                     activationSlots,
                     out baseSpanLength,
                     allowsDynamicIdentifiers))
        {
            // A bare member call (`o.m()`) is already accepted as a simple-literal value,
            // so on its own it is handled by the push gate; we only need it here as a base
            // for trailing property reads (`o.m().items`).
        }
        else
        {
            return false;
        }

        // Optional trailing plain named property reads off the call result
        // (`f().items`, `f().a.b`). Computed/optional/private reads are NOT admitted here.
        var stringConstants = program.StringConstants.AsSpan();
        var i = startIndex + baseSpanLength;
        while (i < program.OperationCount &&
               IsPlainNamedPropertyRead(program.GetOperation(i), stringConstants))
        {
            i++;
        }

        // A bare member call with no trailing read is already covered by the simple-literal
        // value span; reporting it here would be redundant but harmless. A bare identifier
        // call, or any call followed by >= 1 named read, is the genuinely-new admission.
        spanLength = i - startIndex;
        return true;
    }

    // Measures the op span for a simple object literal starting at startIndex.
    // Admitted shapes (CreateObject followed by N >= 0 property triples/spreads):
    //   Static:   [simple-literal-value-span, DefineObjectProperty(non-private, no name inference)]
    //   Computed: [simple-key-span or simple-binary-key-expression, ResolvePropertyKey,
    //              simple-literal-value-span, DefineComputedObjectProperty(no name inference)]
    //   Spread:   [simple-spread-source-span, ObjectSpread]
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

                if (!TryMeasureSimpleLiteralValueOperandSpan(
                        program,
                        i,
                        identifierConstants,
                        activationSlots,
                        out var valueSpanLength,
                        allowsDynamicIdentifiers))
                {
                    spanLength = 0;
                    return false;
                }

                i += valueSpanLength;
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

                if (!TryMeasureSimpleLiteralValueOperandSpan(
                        program,
                        i,
                        identifierConstants,
                        activationSlots,
                        out var valueSpanLength,
                        allowsDynamicIdentifiers))
                {
                    spanLength = 0;
                    return false;
                }

                i += valueSpanLength;
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

            // A34: an object-spread whose SOURCE is non-simple — a bare identifier call
            // (`{...f()}`, `{...gen()}`), an admitted member call (`{...o.m()}`), or a plain
            // named property read off such a call (`{...f().items}`, `{...o.m().a.b}`). The
            // wider spread-source span is consulted ONLY when the terminating op is
            // ObjectSpread; a non-spread terminator ends the literal scan so the regular
            // property gate decides eligibility. ObjectSpread copies the source's own
            // enumerable properties (getters fire in source order; non-enumerables skipped;
            // null/undefined is a no-op), so any source span built from already-admitted VM
            // opcodes is safe to spread.
            if (TryMeasureSimpleSpreadSourceOperandSpan(
                    program,
                    i,
                    identifierConstants,
                    activationSlots,
                    out var spreadSourceSpanLength,
                    allowsDynamicIdentifiers) &&
                i + spreadSourceSpanLength < program.OperationCount &&
                program.GetOperation(i + spreadSourceSpanLength).Kind == ExpressionOpKind.ObjectSpread)
            {
                i += spreadSourceSpanLength + 1;
                continue;
            }

            // A35: a SHORTHAND METHOD or ACCESSOR member — `{m(){}}`, `{get a(){}}`, `{set a(v){}}`,
            // and the computed forms `{[k](){}}`, `{get [k](){}}`. These already route when terminal
            // (the per-op switch admits the Define*Method/Define*Accessor opcodes), but the literal-span
            // measurer must also recognize them so a LATER member — most importantly a trailing
            // object-spread `{m(){}, ...o}` — stays INSIDE the admitted span. The function value is a
            // LoadFunctionLiteral payload (no side effects at definition time); evaluation order across
            // members is preserved because the span is measured purely structurally, left-to-right.
            if (TryMeasureSimpleObjectLiteralMethodOrAccessorMemberSpan(
                    program,
                    i,
                    identifierConstants,
                    activationSlots,
                    out var methodMemberSpanLength,
                    allowsDynamicIdentifiers))
            {
                i += methodMemberSpanLength;
                continue;
            }

            if (!TryMeasureSimpleLiteralValueOperandSpan(
                    program,
                    i,
                    identifierConstants,
                    activationSlots,
                    out var firstSpanLength,
                    allowsDynamicIdentifiers))
            {
                // Non-simple first op terminates the property scan — the object literal ends here.
                break;
            }

            i += firstSpanLength;
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

                if (!TryMeasureSimpleLiteralValueOperandSpan(
                        program,
                        i,
                        identifierConstants,
                        activationSlots,
                        out var valueSpanLength,
                        allowsDynamicIdentifiers))
                {
                    spanLength = 0;
                    return false;
                }

                i += valueSpanLength;
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

    // A35: measures ONE object-literal shorthand-method / accessor member starting at startIndex.
    //   Static method:    LoadFunctionLiteral, DefineObjectMethod                 (2 ops)
    //   Static accessor:  LoadFunctionLiteral, DefineObjectAccessor               (2 ops)
    //   Computed method:  <key span>, ResolvePropertyKey, LoadFunctionLiteral, DefineComputedObjectMethod
    //   Computed accessor:<key span>, ResolvePropertyKey, LoadFunctionLiteral, DefineComputedObjectAccessor
    // The method/accessor function is a LoadFunctionLiteral payload (definition is side-effect-free);
    // only the computed-key subexpression can carry side effects and it is measured as an already-admitted
    // value/key span that evaluates before ResolvePropertyKey — left-to-right order is preserved.
    private static bool TryMeasureSimpleObjectLiteralMethodOrAccessorMemberSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        spanLength = 0;

        // Static form: LoadFunctionLiteral immediately followed by a static method/accessor define.
        if (program.GetOperation(startIndex).Kind == ExpressionOpKind.LoadFunctionLiteral)
        {
            if (startIndex + 1 >= program.OperationCount)
            {
                return false;
            }

            var staticDefine = program.GetOperation(startIndex + 1).Kind;
            if (staticDefine is ExpressionOpKind.DefineObjectMethod or ExpressionOpKind.DefineObjectAccessor)
            {
                spanLength = 2;
                return true;
            }

            return false;
        }

        // Computed form: <key span>, ResolvePropertyKey, LoadFunctionLiteral, DefineComputed{Method,Accessor}.
        if (!TryMeasureSimpleLiteralValueOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out var keySpanLength,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var resolveIndex = startIndex + keySpanLength;
        if (resolveIndex + 2 >= program.OperationCount ||
            program.GetOperation(resolveIndex).Kind != ExpressionOpKind.ResolvePropertyKey ||
            program.GetOperation(resolveIndex + 1).Kind != ExpressionOpKind.LoadFunctionLiteral)
        {
            return false;
        }

        var computedDefine = program.GetOperation(resolveIndex + 2).Kind;
        if (computedDefine is ExpressionOpKind.DefineComputedObjectMethod
                            or ExpressionOpKind.DefineComputedObjectAccessor)
        {
            spanLength = keySpanLength + 3;
            return true;
        }

        return false;
    }

    private static bool TryMeasureSimpleLiteralValueOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        return TryMeasureSimpleLiteralValueOperandSpanCore(
            program,
            startIndex,
            identifierConstants,
            activationSlots,
            out spanLength,
            allowsDynamicIdentifiers,
            allowControlExpressions: true);
    }

    private static bool TryMeasureSimpleLiteralValueOperandSpanCore(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers,
        bool allowControlExpressions,
        bool allowCallExpressions = true,
        bool allowTypeOfExpressions = true,
        bool allowUnaryExpressions = true,
        bool allowBinaryExpressions = true)
    {
        if (allowCallExpressions &&
            TryMeasureSimpleMemberCallOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        // A35: a bare-identifier call value (`{x: g()}`) — the member-call value form already routed.
        if (allowCallExpressions &&
            TryMeasureSimpleDirectIdentifierCallOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        var operation = program.GetOperation(startIndex);
        if (operation.Kind == ExpressionOpKind.CreateArray)
        {
            return TryMeasureSimpleArrayLiteralSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers);
        }

        if (operation.Kind == ExpressionOpKind.CreateObject)
        {
            return TryMeasureSimpleObjectLiteralSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers);
        }

        if (operation.Kind == ExpressionOpKind.LoadLiteral &&
            TryMeasureSimpleTemplateLiteralSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers) &&
            spanLength > 1)
        {
            return true;
        }

        if (allowTypeOfExpressions &&
            TryMeasureSimpleTypeOfOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        if (allowBinaryExpressions &&
            TryMeasureSimpleBinaryOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        if (allowUnaryExpressions &&
            TryMeasureSimpleUnaryOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        if (startIndex == 0 &&
            TryMeasureSimplePropertyReadOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers,
                allowPrivateNamedPrefix: true) &&
            spanLength == program.OperationCount)
        {
            return true;
        }

        if (TryMeasureSimplePropertyReadOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        if (allowControlExpressions &&
            TryMeasureSimpleControlExpressionOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        if (IsSimpleOperand(
                operation,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            spanLength = 1;
            return true;
        }

        spanLength = 0;
        return false;
    }

    private static bool TryMeasureSimpleControlExpressionOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers)
    {
        return TryMeasureSimpleLogicalControlExpressionOperandSpan(
                   program,
                   startIndex,
                   identifierConstants,
                   activationSlots,
                   out spanLength,
                   allowsDynamicIdentifiers) ||
               TryMeasureSimpleConditionalExpressionOperandSpan(
                   program,
                   startIndex,
                   identifierConstants,
                   activationSlots,
                   out spanLength,
                   allowsDynamicIdentifiers);
    }

    private static bool TryMeasureSimpleLogicalControlExpressionOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers)
    {
        spanLength = 0;
        if (!TryMeasureSimpleLiteralValueOperandSpanCore(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out var leftSpanLength,
                allowsDynamicIdentifiers,
                allowControlExpressions: false,
                allowUnaryExpressions: false,
                allowBinaryExpressions: false))
        {
            return false;
        }

        var jumpIndex = startIndex + leftSpanLength;
        var popIndex = jumpIndex + 1;
        var rhsStartIndex = jumpIndex + 2;
        if (rhsStartIndex >= program.OperationCount)
        {
            return false;
        }

        var jump = program.GetOperation(jumpIndex);
        if (jump.Kind is not (ExpressionOpKind.JumpIfFalse or ExpressionOpKind.JumpIfTrue or ExpressionOpKind.JumpIfNotNullish) ||
            program.GetOperation(popIndex).Kind != ExpressionOpKind.Pop)
        {
            return false;
        }

        if (!TryMeasureSimpleLiteralValueOperandSpanCore(
                program,
                rhsStartIndex,
                identifierConstants,
                activationSlots,
                out var rhsSpanLength,
                allowsDynamicIdentifiers,
                allowControlExpressions: true,
                allowUnaryExpressions: false,
                allowBinaryExpressions: false))
        {
            return false;
        }

        var endIndex = rhsStartIndex + rhsSpanLength;
        if (jump.Target != endIndex)
        {
            return false;
        }

        spanLength = endIndex - startIndex;
        return true;
    }

    private static bool TryMeasureSimpleConditionalExpressionOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers)
    {
        spanLength = 0;
        if (!TryMeasureSimpleLiteralValueOperandSpanCore(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out var conditionSpanLength,
                allowsDynamicIdentifiers,
                allowControlExpressions: false,
                allowUnaryExpressions: false,
                allowBinaryExpressions: false))
        {
            return false;
        }

        var branchIndex = startIndex + conditionSpanLength;
        if (branchIndex >= program.OperationCount ||
            program.GetOperation(branchIndex).Kind != ExpressionOpKind.JumpIfConditionalFalse)
        {
            return false;
        }

        var consequentStartIndex = branchIndex + 1;
        if (!TryMeasureSimpleLiteralValueOperandSpanCore(
                program,
                consequentStartIndex,
                identifierConstants,
                activationSlots,
                out var consequentSpanLength,
                allowsDynamicIdentifiers,
                allowControlExpressions: true,
                allowUnaryExpressions: false,
                allowBinaryExpressions: false))
        {
            return false;
        }

        var jumpIndex = consequentStartIndex + consequentSpanLength;
        var alternateStartIndex = jumpIndex + 1;
        if (alternateStartIndex >= program.OperationCount ||
            program.GetOperation(jumpIndex).Kind != ExpressionOpKind.Jump ||
            program.GetOperation(branchIndex).Target != alternateStartIndex)
        {
            return false;
        }

        if (!TryMeasureSimpleLiteralValueOperandSpanCore(
                program,
                alternateStartIndex,
                identifierConstants,
                activationSlots,
                out var alternateSpanLength,
                allowsDynamicIdentifiers,
                allowControlExpressions: true,
                allowUnaryExpressions: false,
                allowBinaryExpressions: false))
        {
            return false;
        }

        var endIndex = alternateStartIndex + alternateSpanLength;
        if (program.GetOperation(jumpIndex).Target != endIndex)
        {
            return false;
        }

        spanLength = endIndex - startIndex;
        return true;
    }

    private static bool TryMeasureSimplePropertyReadOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false,
        bool allowPrivateNamedPrefix = false)
    {
        if (TryMeasureSimpleComputedPropertyReadOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers,
                allowPrivateNamedPrefix))
        {
            return true;
        }

        return TryMeasureSimpleNamedPropertyReadOperandSpan(
            program,
            startIndex,
            identifierConstants,
            activationSlots,
            out spanLength,
            allowsDynamicIdentifiers,
            allowPrivateNamedPrefix);
    }

    private static bool TryMeasureSimpleNamedPropertyReadOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers,
        bool allowPrivateNamedPrefix = false)
    {
        spanLength = 0;
        if (startIndex + 1 >= program.OperationCount ||
            !IsSimpleOperand(
                program.GetOperation(startIndex),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var i = startIndex + 1;
        while (i < program.OperationCount &&
               IsPlainNamedPropertyReadOperandPrefix(
                   program.GetOperation(i),
                   program.StringConstants.AsSpan(),
                   allowPrivateNamedPrefix))
        {
            i++;
        }

        if (i == startIndex + 1)
        {
            return false;
        }

        spanLength = i - startIndex;
        return true;
    }

    // Measures a baseline optional named property-read operand span: a simple base
    // operand followed by a single optional GetNamedProperty (`box?.value`). The
    // optional GetNamedProperty yields undefined for a nullish base without a
    // short-circuit jump, so it is a self-contained operand. Chained optional reads
    // (`box?.value?.nested`) carry a ShortCircuitOnNullishTarget continuation hop and
    // are not admitted by this baseline span.
    private static bool TryMeasureSimpleOptionalNamedPropertyReadOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers)
    {
        spanLength = 0;
        if (startIndex + 1 >= program.OperationCount ||
            !IsSimpleOperand(
                program.GetOperation(startIndex),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var namedRead = program.GetOperation(startIndex + 1);
        if (namedRead.Kind != ExpressionOpKind.GetNamedProperty ||
            !namedRead.IsOptional ||
            namedRead.ShortCircuitOnNullishTarget ||
            namedRead.GetString(program.StringConstants.AsSpan()).IsPrivateName())
        {
            return false;
        }

        spanLength = 2;
        return true;
    }

    // Measures an optional-start named property-read operand span
    // (`box?.child.value`, `box?.child.nested.deep`, `box?.child?.value`): a simple
    // base operand, one optional GetNamedProperty hop (IsOptional, !ShortCircuit),
    // and at least one continuation read carrying the chain short-circuit flag. A continuation can be
    // either a plain read (`box?.child.value`) or another optional hop
    // (`box?.child?.value`); the compiler emits one boundary jump per optional hop.
    // A nullish base/intermediate short-circuits the whole chain to undefined.
    private static bool TryMeasureSimpleOptionalNamedReadChainOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers)
    {
        spanLength = 0;
        if (startIndex + 2 >= program.OperationCount ||
            !IsSimpleOperand(
                program.GetOperation(startIndex),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var firstHop = program.GetOperation(startIndex + 1);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        var index = startIndex + 2;
        while (index < program.OperationCount)
        {
            var continuation = program.GetOperation(index);
            if (continuation.Kind != ExpressionOpKind.GetNamedProperty ||
                !continuation.ShortCircuitOnNullishTarget ||
                continuation.GetString(stringConstants).IsPrivateName())
            {
                break;
            }

            index++;
        }

        if (index == startIndex + 2)
        {
            return false;
        }

        spanLength = index - startIndex;
        return true;
    }

    // Measures an optional-named-then-computed read operand span used as a call
    // argument (`box?.prop[key]`, `box?.prop?.[key]`, `box?.prop[a + b]`,
    // `box?.a.b[key]`): a simple base operand, an optional named hop
    // (GetNamedProperty(IsOptional, !SC, non-private)), zero or more plain named
    // continuations (GetNamedProperty(!IsOptional, SC, non-private)), an optional
    // computed-hop JumpIfNullish when the computed hop uses `?.[`, a supported
    // computed key span, and a chain-short-circuit GetComputedProperty. A nullish
    // base or optional computed receiver short-circuits the whole chain to undefined.
    private static bool TryMeasureSimpleOptionalNamedThenComputedReadOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers)
    {
        spanLength = 0;
        if (startIndex + 3 >= program.OperationCount ||
            !IsSimpleOperand(
                program.GetOperation(startIndex),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var firstHop = program.GetOperation(startIndex + 1);
        if (firstHop.Kind != ExpressionOpKind.GetNamedProperty ||
            !firstHop.IsOptional ||
            firstHop.ShortCircuitOnNullishTarget ||
            firstHop.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        // Plain named continuations (`box?.a.b[key]`) before the computed read.
        var keyStart = startIndex + 2;
        while (keyStart < program.OperationCount)
        {
            var continuation = program.GetOperation(keyStart);
            if (continuation.Kind != ExpressionOpKind.GetNamedProperty ||
                continuation.IsOptional ||
                !continuation.ShortCircuitOnNullishTarget ||
                continuation.GetString(stringConstants).IsPrivateName())
            {
                break;
            }

            keyStart++;
        }

        var optionalComputedJumpIndex = -1;
        if (keyStart < program.OperationCount)
        {
            var maybeJump = program.GetOperation(keyStart);
            if (maybeJump.Kind == ExpressionOpKind.JumpIfNullish &&
                maybeJump.ReplaceWithUndefined)
            {
                optionalComputedJumpIndex = keyStart;
                keyStart++;
            }
        }

        // Locate the chain-short-circuit computed read after the key span.
        for (var computedIndex = keyStart + 1; computedIndex < program.OperationCount; computedIndex++)
        {
            var computedOp = program.GetOperation(computedIndex);
            if (computedOp.Kind != ExpressionOpKind.GetComputedProperty ||
                computedOp.IsOptional ||
                !computedOp.ShortCircuitOnNullishTarget)
            {
                continue;
            }

            if (!IsSupportedComputedPropertyKeySpan(
                    program,
                    keyStart,
                    computedIndex,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers))
            {
                break;
            }

            if (optionalComputedJumpIndex >= 0 &&
                program.GetOperation(optionalComputedJumpIndex).Target != computedIndex + 1)
            {
                break;
            }

            // Trailing plain named continuations (`box?.prop[key].child`) carry the chain
            // short-circuit flag. Optional named continuations (`box?.prop?.[key]?.child`)
            // carry the same flag and are safe because the compiler emits a boundary jump
            // before every optional named hop.
            var index = computedIndex + 1;
            while (index < program.OperationCount)
            {
                var continuation = program.GetOperation(index);
                if (continuation.Kind != ExpressionOpKind.GetNamedProperty ||
                    !continuation.ShortCircuitOnNullishTarget ||
                    continuation.GetString(stringConstants).IsPrivateName())
                {
                    break;
                }

                index++;
            }

            spanLength = index - startIndex;
            return true;
        }

        return false;
    }

    // Measures an optional computed property-read operand span
    // (`box?.[key]`, `box?.[a + b]`, `box?.[key]?.[key]`, `box?.[key]?.value`):
    // a simple base operand, one or more JumpIfNullish(ReplaceWithUndefined)
    // short-circuit guards paired with computed key spans and GetComputedProperty
    // reads, followed by optional/plain named continuations. A nullish base or
    // optional continuation receiver short-circuits the whole chain to undefined.
    private static bool TryMeasureSimpleOptionalComputedPropertyReadOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers)
    {
        spanLength = 0;
        if (startIndex + 3 >= program.OperationCount ||
            !IsSimpleOperand(
                program.GetOperation(startIndex),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var index = startIndex + 1;
        var hopCount = 0;
        while (index < program.OperationCount)
        {
            var jumpOp = program.GetOperation(index);
            if (jumpOp.Kind != ExpressionOpKind.JumpIfNullish ||
                !jumpOp.ReplaceWithUndefined)
            {
                break;
            }

            var keyStart = index + 1;
            var computedIndex = keyStart;
            while (computedIndex < program.OperationCount &&
                   program.GetOperation(computedIndex).Kind != ExpressionOpKind.GetComputedProperty)
            {
                computedIndex++;
            }

            if (computedIndex <= keyStart ||
                computedIndex >= program.OperationCount ||
                jumpOp.Target != computedIndex + 1)
            {
                return false;
            }

            var computedOp = program.GetOperation(computedIndex);
            var expectedShortCircuit = hopCount > 0;
            if (computedOp.Kind != ExpressionOpKind.GetComputedProperty ||
                computedOp.IsOptional ||
                computedOp.ShortCircuitOnNullishTarget != expectedShortCircuit)
            {
                return false;
            }

            if (!IsSupportedComputedPropertyKeySpan(
                    program,
                    keyStart,
                    computedIndex,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers))
            {
                return false;
            }

            hopCount++;
            index = computedIndex + 1;
        }

        if (hopCount == 0)
        {
            return false;
        }

        // Allow named continuation reads after the optional computed read
        // (`box?.[key].value`, `box?.[key]?.value`, `box?.[key].a.b`). They carry
        // the chain short-circuit flag; optional named continuations get their own
        // emitted boundary jump so they skip the property read on a nullish receiver.
        var stringConstants = program.StringConstants.AsSpan();
        while (index < program.OperationCount)
        {
            var continuation = program.GetOperation(index);
            if (continuation.Kind != ExpressionOpKind.GetNamedProperty ||
                !continuation.ShortCircuitOnNullishTarget ||
                continuation.GetString(stringConstants).IsPrivateName())
            {
                break;
            }

            index++;
        }

        spanLength = index - startIndex;
        return true;
    }

    private static bool TryMeasureSimpleComputedPropertyReadOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers,
        bool allowPrivateNamedPrefix = false)
    {
        spanLength = 0;
        if (startIndex + 4 >= program.OperationCount ||
            !IsSimpleOperand(
                program.GetOperation(startIndex),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var keyStart = startIndex + 1;
        while (keyStart < program.OperationCount &&
               IsPlainNamedPropertyReadOperandPrefix(
                   program.GetOperation(keyStart),
                   program.StringConstants.AsSpan(),
                   allowPrivateNamedPrefix))
        {
            keyStart++;
        }

        for (var requireIndex = keyStart + 1; requireIndex + 2 < program.OperationCount; requireIndex++)
        {
            var requireObjectCoercible = program.GetOperation(requireIndex);
            if (requireObjectCoercible.Kind != ExpressionOpKind.RequireObjectCoercible ||
                requireObjectCoercible.Depth != 1)
            {
                continue;
            }

            var resolvePropertyKey = program.GetOperation(requireIndex + 1);
            var getComputedProperty = program.GetOperation(requireIndex + 2);
            if (resolvePropertyKey.Kind != ExpressionOpKind.ResolvePropertyKey ||
                getComputedProperty.Kind != ExpressionOpKind.GetComputedProperty ||
                getComputedProperty.ShortCircuitOnNullishTarget)
            {
                continue;
            }

            if (!IsSupportedComputedPropertyKeySpan(
                    program,
                    keyStart,
                    requireIndex,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers))
            {
                continue;
            }

            var endExclusive = requireIndex + 3;
            while (endExclusive < program.OperationCount &&
                   IsPlainNamedPropertyReadOperandPrefix(
                       program.GetOperation(endExclusive),
                       program.StringConstants.AsSpan(),
                       allowPrivateNamedPrefix))
            {
                endExclusive++;
            }

            spanLength = endExclusive - startIndex;
            return true;
        }

        return false;
    }

    private static bool TryMeasureSimpleTypeOfOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        var operation = program.GetOperation(startIndex);
        if (operation.Kind == ExpressionOpKind.TypeOfIdentifier)
        {
            var identifier = operation.GetIdentifier(identifierConstants);
            if (IsImplicitArgumentsIdentifier(identifier, activationSlots) ||
                TryResolveActivationSlot(identifier, activationSlots) ||
                allowsDynamicIdentifiers)
            {
                spanLength = 1;
                return true;
            }

            spanLength = 0;
            return false;
        }

        if (!TryMeasureSimpleTypeOfValueOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out var operandSpanLength,
                allowsDynamicIdentifiers) ||
            startIndex + operandSpanLength >= program.OperationCount ||
            program.GetOperation(startIndex + operandSpanLength).Kind != ExpressionOpKind.TypeOf)
        {
            spanLength = 0;
            return false;
        }

        spanLength = operandSpanLength + 1;
        return true;
    }

    private static bool TryMeasureSimpleTypeOfValueOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers)
    {
        if (TryMeasureSimpleMemberCallOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        var operation = program.GetOperation(startIndex);
        if (operation.Kind == ExpressionOpKind.CreateArray)
        {
            return TryMeasureSimpleArrayLiteralSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers);
        }

        if (operation.Kind == ExpressionOpKind.CreateObject)
        {
            return TryMeasureSimpleObjectLiteralSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers);
        }

        if (operation.Kind == ExpressionOpKind.LoadLiteral &&
            TryMeasureSimpleTemplateLiteralSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers) &&
            spanLength > 1)
        {
            return true;
        }

        if (TryMeasureFlatSimpleBinaryOperandSpan(
                program,
                startIndex,
                program.OperationCount,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        if (TryMeasureFlatSimpleUnaryOperandSpan(
                program,
                startIndex,
                program.OperationCount,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        if (TryMeasureSimplePropertyReadOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        if (IsSimpleOperand(
                operation,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            spanLength = 1;
            return true;
        }

        spanLength = 0;
        return false;
    }

    private static bool TryMeasureSimpleUnaryOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        return TryMeasureSimpleUnaryOperandSpan(
            program,
            startIndex,
            identifierConstants,
            activationSlots,
            program.OperationCount,
            out spanLength,
            allowsDynamicIdentifiers);
    }

    private static bool TryMeasureSimpleUnaryOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        int endExclusive,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        if (startIndex + 1 >= endExclusive)
        {
            spanLength = 0;
            return false;
        }

        for (var unaryIndex = endExclusive - 1; unaryIndex >= startIndex + 1; unaryIndex--)
        {
            if (!IsSimpleUnaryOperator(program.GetOperation(unaryIndex).Kind))
            {
                continue;
            }

            if (TryMeasureSimpleNestedOperandSpan(
                    program,
                    startIndex,
                    unaryIndex,
                    identifierConstants,
                    activationSlots,
                    out var operandSpanLength,
                    allowsDynamicIdentifiers) &&
                startIndex + operandSpanLength == unaryIndex)
            {
                spanLength = operandSpanLength + 1;
                return true;
            }
        }

        spanLength = 0;
        return false;
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

        for (var binaryIndex = endExclusive - 1; binaryIndex >= startIndex + 2; binaryIndex--)
        {
            var binary = program.GetOperation(binaryIndex);
            if (binary.Kind != ExpressionOpKind.Binary ||
                !IsProductionBinaryOperator(binary.Operator))
            {
                continue;
            }

            if (!TryMeasureSimpleNestedOperandSpan(
                    program,
                    startIndex,
                    binaryIndex,
                    identifierConstants,
                    activationSlots,
                    out var leftSpanLength,
                    allowsDynamicIdentifiers) ||
                startIndex + leftSpanLength >= binaryIndex)
            {
                continue;
            }

            var rightStartIndex = startIndex + leftSpanLength;
            if (!TryMeasureSimpleNestedOperandSpan(
                    program,
                    rightStartIndex,
                    binaryIndex,
                    identifierConstants,
                    activationSlots,
                    out var rightSpanLength,
                    allowsDynamicIdentifiers) ||
                rightStartIndex + rightSpanLength != binaryIndex)
            {
                continue;
            }

            spanLength = binaryIndex + 1 - startIndex;
            return true;
        }

        spanLength = 0;
        return false;
    }

    private static bool TryMeasureSimpleNestedOperandSpan(
        ExpressionProgram program,
        int startIndex,
        int endExclusive,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers)
    {
        if (TryMeasureFlatSimpleBinaryOperandSpan(
                program,
                startIndex,
                endExclusive,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers) ||
            TryMeasureFlatSimpleUnaryOperandSpan(
                program,
                startIndex,
                endExclusive,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers) ||
            TryMeasureSimplePropertyReadOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers) ||
            TryMeasureSimpleControlExpressionOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        if (TryMeasureSimpleControlExpressionOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers) &&
            startIndex + spanLength <= endExclusive)
        {
            return true;
        }

        var operation = program.GetOperation(startIndex);
        if (IsSimpleOperand(operation, identifierConstants, activationSlots, allowsDynamicIdentifiers))
        {
            spanLength = 1;
            return true;
        }

        spanLength = 0;
        return false;
    }

    private static bool TryMeasureFlatSimpleUnaryOperandSpan(
        ExpressionProgram program,
        int startIndex,
        int endExclusive,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        if (startIndex + 1 >= endExclusive)
        {
            spanLength = 0;
            return false;
        }

        var operand = program.GetOperation(startIndex);
        var unary = program.GetOperation(startIndex + 1);
        if (IsSimpleOperand(operand, identifierConstants, activationSlots, allowsDynamicIdentifiers) &&
            IsSimpleUnaryOperator(unary.Kind))
        {
            spanLength = 2;
            return true;
        }

        spanLength = 0;
        return false;
    }

    private static bool TryMeasureFlatSimpleBinaryOperandSpan(
        ExpressionProgram program,
        int startIndex,
        int endExclusive,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
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

    private static bool IsSimpleUnaryOperator(ExpressionOpKind kind)
    {
        return kind is ExpressionOpKind.UnaryPlus or
            ExpressionOpKind.UnaryMinus or
            ExpressionOpKind.UnaryLogicalNot or
            ExpressionOpKind.UnaryBitwiseNot or
            ExpressionOpKind.UnaryVoid;
    }

    private static bool TryMeasureSimpleIdentifierCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
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

    // A35: a bare-identifier call used as an object-literal VALUE — `{x: g()}`, `{a: g(arg)}`.
    // Mirrors TryMeasureSimpleDirectNamedCallOperandSpan but for a LoadIdentifierCallTarget callee
    // (the member-call value form `{a: o.m()}` already routed via the member-call span). The callee
    // identifier must resolve to an activation slot (never `arguments`/`eval`); each argument is a
    // single simple operand, so the call's arguments evaluate left-to-right after the callee — spec
    // PropertyDefinitionEvaluation order is preserved (the value subexpression runs to completion
    // before the property is defined). Spread arguments are excluded (SpreadMaskConstantIndex).
    private static bool TryMeasureSimpleDirectIdentifierCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        spanLength = 0;
        if (startIndex + 1 >= program.OperationCount)
        {
            return false;
        }

        var callTarget = program.GetOperation(startIndex);
        if (callTarget.Kind != ExpressionOpKind.LoadIdentifierCallTarget ||
            callTarget.IsArguments ||
            callTarget.IsOptional ||
            callTarget.ShortCircuitOnNullishTarget)
        {
            return false;
        }

        var identifier = callTarget.GetIdentifier(identifierConstants);
        if (identifier.Name.Name == "eval" ||
            !TryResolveActivationSlot(identifier, activationSlots))
        {
            return false;
        }

        var argCount = 0;
        var operationIndex = startIndex + 1;
        while (operationIndex < program.OperationCount &&
               IsSimpleOperand(program.GetOperation(operationIndex), identifierConstants, activationSlots, allowsDynamicIdentifiers))
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

    private static bool TryMeasureSimpleDirectMemberCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        return TryMeasureSimpleDirectNamedCallOperandSpan(
                   program,
                   startIndex,
                   identifierConstants,
                   activationSlots,
                   out spanLength,
                   allowsDynamicIdentifiers) ||
               TryMeasureSimpleDirectComputedCallOperandSpan(
                   program,
                   startIndex,
                   identifierConstants,
                   activationSlots,
                   out spanLength,
                   allowsDynamicIdentifiers);
    }

    private static bool TryMeasureSimpleMemberCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        if (TryMeasureSimpleDirectMemberCallOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        if (TryMeasureSimpleOptionalNamedCallOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        return TryMeasureSimpleOptionalComputedCallOperandSpan(
            program,
            startIndex,
            identifierConstants,
            activationSlots,
            out spanLength,
            allowsDynamicIdentifiers);
    }

    private static bool TryMeasureSimpleOptionalNamedCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        return TryMeasureSimpleReceiverOptionalNamedCallOperandSpan(
                   program,
                   startIndex,
                   identifierConstants,
                   activationSlots,
                   out spanLength,
                   allowsDynamicIdentifiers) ||
               TryMeasureSimpleCalleeOptionalNamedCallOperandSpan(
                   program,
                   startIndex,
                   identifierConstants,
                   activationSlots,
                   out spanLength,
                   allowsDynamicIdentifiers);
    }

    private static bool TryMeasureSimpleReceiverOptionalNamedCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers)
    {
        spanLength = 0;
        if (startIndex + 4 >= program.OperationCount)
        {
            return false;
        }

        if (!IsSimpleOperand(program.GetOperation(startIndex), identifierConstants, activationSlots, allowsDynamicIdentifiers))
        {
            return false;
        }

        var jump = program.GetOperation(startIndex + 1);
        if (jump.Kind != ExpressionOpKind.JumpIfNullish || !jump.ReplaceWithUndefined)
        {
            return false;
        }

        var callTarget = program.GetOperation(startIndex + 2);
        if (callTarget.Kind != ExpressionOpKind.LoadNamedCallTarget ||
            callTarget.GetString(program.StringConstants.AsSpan()).IsPrivateName())
        {
            return false;
        }

        var argCount = 0;
        var operationIndex = startIndex + 3;
        while (operationIndex < program.OperationCount &&
               IsSimpleOperand(program.GetOperation(operationIndex), identifierConstants, activationSlots, allowsDynamicIdentifiers))
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

    private static bool TryMeasureSimpleCalleeOptionalNamedCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers)
    {
        spanLength = 0;
        if (startIndex + 6 >= program.OperationCount)
        {
            return false;
        }

        if (!IsSimpleOperand(program.GetOperation(startIndex), identifierConstants, activationSlots, allowsDynamicIdentifiers))
        {
            return false;
        }

        var callTarget = program.GetOperation(startIndex + 1);
        if (callTarget.Kind != ExpressionOpKind.LoadNamedCallTarget ||
            callTarget.GetString(program.StringConstants.AsSpan()).IsPrivateName())
        {
            return false;
        }

        var jump = program.GetOperation(startIndex + 2);
        if (jump.Kind != ExpressionOpKind.JumpIfNullish || !jump.ReplaceWithUndefined)
        {
            return false;
        }

        var argCount = 0;
        var operationIndex = startIndex + 3;
        while (operationIndex < program.OperationCount &&
               IsSimpleOperand(program.GetOperation(operationIndex), identifierConstants, activationSlots, allowsDynamicIdentifiers))
        {
            argCount++;
            operationIndex++;
        }

        if (operationIndex + 3 >= program.OperationCount)
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

        if (program.GetOperation(operationIndex + 1).Kind != ExpressionOpKind.Jump ||
            program.GetOperation(operationIndex + 2).Kind != ExpressionOpKind.SwapTopTwo ||
            program.GetOperation(operationIndex + 3).Kind != ExpressionOpKind.Pop)
        {
            return false;
        }

        spanLength = operationIndex - startIndex + 4;
        return true;
    }

    private static bool TryMeasureSimpleOptionalComputedCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        return TryMeasureSimpleReceiverOptionalComputedCallOperandSpan(
                   program,
                   startIndex,
                   identifierConstants,
                   activationSlots,
                   out spanLength,
                   allowsDynamicIdentifiers) ||
               TryMeasureSimpleCalleeOptionalComputedCallOperandSpan(
                   program,
                   startIndex,
                   identifierConstants,
                   activationSlots,
                   out spanLength,
                   allowsDynamicIdentifiers);
    }

    private static bool TryMeasureSimpleReceiverOptionalComputedCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers)
    {
        spanLength = 0;
        if (startIndex + 5 >= program.OperationCount)
        {
            return false;
        }

        if (!IsSimpleOperand(program.GetOperation(startIndex), identifierConstants, activationSlots, allowsDynamicIdentifiers))
        {
            return false;
        }

        var jump = program.GetOperation(startIndex + 1);
        if (jump.Kind != ExpressionOpKind.JumpIfNullish || !jump.ReplaceWithUndefined)
        {
            return false;
        }

        var callTargetIndex = startIndex + 3;
        while (callTargetIndex < program.OperationCount &&
               program.GetOperation(callTargetIndex).Kind != ExpressionOpKind.LoadComputedCallTarget)
        {
            callTargetIndex++;
        }

        if (callTargetIndex >= program.OperationCount)
        {
            return false;
        }

        if (!IsSupportedComputedPropertyKeySpan(
                program,
                startInclusive: startIndex + 2,
                endExclusive: callTargetIndex,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var callTarget = program.GetOperation(callTargetIndex);
        if (callTarget.IsOptional || callTarget.ShortCircuitOnNullishTarget)
        {
            return false;
        }

        var argCount = 0;
        var operationIndex = callTargetIndex + 1;
        while (operationIndex < program.OperationCount &&
               IsSimpleOperand(program.GetOperation(operationIndex), identifierConstants, activationSlots, allowsDynamicIdentifiers))
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

    private static bool TryMeasureSimpleCalleeOptionalComputedCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers)
    {
        spanLength = 0;
        if (startIndex + 7 >= program.OperationCount)
        {
            return false;
        }

        if (!IsSimpleOperand(program.GetOperation(startIndex), identifierConstants, activationSlots, allowsDynamicIdentifiers))
        {
            return false;
        }

        var callTargetIndex = startIndex + 2;
        while (callTargetIndex < program.OperationCount &&
               program.GetOperation(callTargetIndex).Kind != ExpressionOpKind.LoadComputedCallTarget)
        {
            callTargetIndex++;
        }

        if (callTargetIndex >= program.OperationCount)
        {
            return false;
        }

        if (!IsSupportedComputedPropertyKeySpan(
                program,
                startInclusive: startIndex + 1,
                endExclusive: callTargetIndex,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var jump = program.GetOperation(callTargetIndex + 1);
        if (jump.Kind != ExpressionOpKind.JumpIfNullish || !jump.ReplaceWithUndefined)
        {
            return false;
        }

        var argCount = 0;
        var operationIndex = callTargetIndex + 2;
        while (operationIndex < program.OperationCount &&
               IsSimpleOperand(program.GetOperation(operationIndex), identifierConstants, activationSlots, allowsDynamicIdentifiers))
        {
            argCount++;
            operationIndex++;
        }

        if (operationIndex + 3 >= program.OperationCount)
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

        if (program.GetOperation(operationIndex + 1).Kind != ExpressionOpKind.Jump ||
            program.GetOperation(operationIndex + 2).Kind != ExpressionOpKind.SwapTopTwo ||
            program.GetOperation(operationIndex + 3).Kind != ExpressionOpKind.Pop)
        {
            return false;
        }

        spanLength = operationIndex - startIndex + 4;
        return true;
    }

    private static bool TryMeasureSimpleDirectNamedCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        spanLength = 0;
        if (startIndex + 2 >= program.OperationCount)
        {
            return false;
        }

        if (!IsSimpleOperand(program.GetOperation(startIndex), identifierConstants, activationSlots, allowsDynamicIdentifiers))
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
               IsSimpleOperand(program.GetOperation(operationIndex), identifierConstants, activationSlots, allowsDynamicIdentifiers))
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

    private static bool TryMeasureSimpleDirectComputedCallOperandSpan(
        ExpressionProgram program,
        int startIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        out int spanLength,
        bool allowsDynamicIdentifiers = false)
    {
        spanLength = 0;
        if (startIndex + 3 >= program.OperationCount)
        {
            return false;
        }

        if (!IsSimpleOperand(program.GetOperation(startIndex), identifierConstants, activationSlots, allowsDynamicIdentifiers))
        {
            return false;
        }

        var callTargetIndex = startIndex + 2;
        while (callTargetIndex < program.OperationCount &&
               program.GetOperation(callTargetIndex).Kind != ExpressionOpKind.LoadComputedCallTarget)
        {
            callTargetIndex++;
        }

        if (callTargetIndex >= program.OperationCount)
        {
            return false;
        }

        var callTarget = program.GetOperation(callTargetIndex);
        if (callTarget.IsOptional || callTarget.ShortCircuitOnNullishTarget)
        {
            return false;
        }

        if (!IsSupportedComputedPropertyKeySpan(
                program,
                startInclusive: startIndex + 1,
                endExclusive: callTargetIndex,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var argCount = 0;
        var operationIndex = callTargetIndex + 1;
        while (operationIndex < program.OperationCount &&
               IsSimpleOperand(program.GetOperation(operationIndex), identifierConstants, activationSlots, allowsDynamicIdentifiers))
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
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers = false)
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
                    out var spanLength,
                    allowsDynamicIdentifiers) &&
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
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers = false)
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
                    out var spanLength,
                    allowsDynamicIdentifiers) &&
                operationIndex < startIndex + spanLength)
            {
                return true;
            }
        }

        return false;
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

        // Multi-op RHS — first try the simple template literal span fast path.
        if (TryMeasureSimpleTemplateLiteralSpan(
                program, rhsStart, identifierConstants, activationSlots, out var spanLen) &&
            spanLen > 1 &&
            rhsStart + spanLen - 1 == rhsEnd)
        {
            return true;
        }

        // COMPOUND-WRITE complex RHS (mirrors the plain-write complex-RHS admission, 830236be0):
        // the read of the old value (DuplicateTop + GetNamedProperty) is fixed in evaluation order
        // BEFORE the RHS; the RHS region is [rhsStart, Binary). Admit ANY already-admitted
        // value-producing expression (binary, nested call, member/optional read span, composition
        // thereof) by validating the whole RHS region with the general operand-stack walker,
        // requiring it to net exactly ONE operand. The op stream is already in evaluation order
        // (base, old-value read, RHS, Binary, store), so the read-old / evaluate-RHS / apply-op /
        // store spec sequence is preserved exactly — nothing is reordered.
        var binaryIndex = program.OperationCount - 2;
        return TryValidateAdmittedComplexCallArgumentRegion(
            program,
            argsStartIndex: rhsStart,
            callIndex: binaryIndex,
            expectedArgumentCount: 1,
            identifierConstants,
            activationSlots,
            allowsDynamicIdentifiers);
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

        // Walk an optional named receiver-prefix chain (e.g. box.child[key] += value).
        // The collapsed receiver stays a single stack value so RequireObjectCoercible.Depth
        // remains 1; the computed key span starts after the prefix.
        // The fixed tail is [..., GetComputedProperty (old-value read), RHS..., Binary,
        // SetComputedProperty]. Binary and SetComputedProperty are the last two ops; the 4-op
        // read prefix [RequireObjectCoercible, ResolvePropertyKey, DuplicateTopTwo,
        // GetComputedProperty] is at [readStart, readStart+4). For a single-op RHS, readStart =
        // OperationCount - 7 (the old shape); for a complex multi-op RHS the read prefix sits
        // earlier and the RHS region [readStart+4, Binary) is variable-length. Evaluation order
        // (object, key, read old value, RHS, apply op, store) is fixed by the op stream — we only
        // locate where the RHS region begins, never reorder.
        var stringConstants = program.StringConstants.AsSpan();
        var binaryIndex = program.OperationCount - 2;
        var binary = program.GetOperation(binaryIndex);
        var propertyWrite = program.GetOperation(program.OperationCount - 1);
        if (binary.Kind != ExpressionOpKind.Binary ||
            !IsProductionBinaryOperator(binary.Operator) ||
            propertyWrite.Kind != ExpressionOpKind.SetComputedProperty ||
            propertyWrite.AllowNameInference)
        {
            return false;
        }

        // readStart is the index of RequireObjectCoercible; readStart+3 = GetComputedProperty.
        // The RHS region begins at readStart+4 and the smallest legal RHS is one op, so readStart
        // ranges over [1, binaryIndex-4]. Prefer the simple single-op RHS (old fast path) and fall
        // back to a complex RHS region only if that does not validate.
        for (var readStart = program.OperationCount - 7; readStart >= 1; readStart--)
        {
            if (readStart + 4 > binaryIndex)
            {
                continue;
            }

            var requireObjectCoercible = program.GetOperation(readStart);
            var resolvePropertyKey = program.GetOperation(readStart + 1);
            var duplicateTargetAndKey = program.GetOperation(readStart + 2);
            var propertyRead = program.GetOperation(readStart + 3);
            if (requireObjectCoercible.Kind != ExpressionOpKind.RequireObjectCoercible ||
                requireObjectCoercible.Depth != 1 ||
                resolvePropertyKey.Kind != ExpressionOpKind.ResolvePropertyKey ||
                duplicateTargetAndKey.Kind != ExpressionOpKind.DuplicateTopTwo ||
                propertyRead.Kind != ExpressionOpKind.GetComputedProperty ||
                propertyRead.ShortCircuitOnNullishTarget)
            {
                continue;
            }

            // Walk an optional named receiver-prefix chain (e.g. box.child[key] += value).
            // The collapsed receiver stays a single stack value so RequireObjectCoercible.Depth
            // remains 1; the computed key span starts after the prefix and ends at readStart.
            var keyStart = 1;
            var receiverChainOk = true;
            while (keyStart < readStart)
            {
                var receiverRead = program.GetOperation(keyStart);
                if (receiverRead.Kind != ExpressionOpKind.GetNamedProperty)
                {
                    break;
                }

                if (receiverRead.GetString(stringConstants).IsPrivateName() ||
                    receiverRead.IsOptional ||
                    receiverRead.ShortCircuitOnNullishTarget)
                {
                    receiverChainOk = false;
                    break;
                }

                keyStart++;
            }

            if (receiverChainOk &&
                keyStart == 1 &&
                TryMeasureSimpleComputedPropertyReadOperandSpan(
                    program,
                    0,
                    identifierConstants,
                    activationSlots,
                    out var receiverSpanLength,
                    allowsDynamicIdentifiers) &&
                receiverSpanLength > 1 &&
                receiverSpanLength < readStart)
            {
                keyStart = receiverSpanLength;
            }

            if (!receiverChainOk ||
                keyStart >= readStart ||
                !IsSupportedComputedPropertyKeySpan(
                    program,
                    startInclusive: keyStart,
                    endExclusive: readStart,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers))
            {
                continue;
            }

            var rhsStart = readStart + 4;

            // Simple single-op RHS fast path.
            if (rhsStart == binaryIndex - 1 &&
                IsSimpleOperand(
                    program.GetOperation(rhsStart),
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers))
            {
                return true;
            }

            // Complex RHS region: admit ANY already-admitted value-producing expression (binary,
            // nested call, member/optional read span, composition thereof) by validating the whole
            // RHS region [rhsStart, Binary) with the general operand-stack walker, requiring it to
            // net exactly ONE operand.
            if (rhsStart < binaryIndex &&
                TryValidateAdmittedComplexCallArgumentRegion(
                    program,
                    argsStartIndex: rhsStart,
                    callIndex: binaryIndex,
                    expectedArgumentCount: 1,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers))
            {
                return true;
            }
        }

        return false;
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

        // Walk an optional named receiver-prefix chain (e.g. box.child[key] &&= value).
        // The collapsed receiver stays a single stack value so RequireObjectCoercible.Depth
        // remains 1; the computed key span starts after the prefix.
        var stringConstants = program.StringConstants.AsSpan();
        var keyStart = 1;
        var propertyWriteIndex = program.OperationCount - 6;
        while (keyStart < propertyWriteIndex)
        {
            var receiverRead = program.GetOperation(keyStart);
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

            keyStart++;
        }

        if (keyStart == 1 &&
            TryMeasureSimpleComputedPropertyReadOperandSpan(
                program,
                0,
                identifierConstants,
                activationSlots,
                out var receiverSpanLength,
                allowsDynamicIdentifiers) &&
            receiverSpanLength > 1 &&
            receiverSpanLength < propertyWriteIndex)
        {
            keyStart = receiverSpanLength;
        }

        for (var rhsLength = 1; rhsLength <= 3; rhsLength += 2)
        {
            var suffixStart = propertyWriteIndex - 6 - rhsLength;
            if (suffixStart <= keyStart ||
                !IsSupportedComputedPropertyKeySpan(
                    program,
                    startInclusive: keyStart,
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

        _ = program.StringConstants.AsSpan();
        var lastOp = program.GetOperation(program.OperationCount - 1);

        // Named property write: [base, rhs..., SetNamedProperty]
        if (lastOp.Kind == ExpressionOpKind.SetNamedProperty &&
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
                return IsSimpleOperandOrSafeFunctionLiteral(
                    program,
                    rhsStart,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers);
            }

            // Multi-op RHS — first try the simple template literal span fast path.
            if (TryMeasureSimpleTemplateLiteralSpan(
                    program, rhsStart, identifierConstants, activationSlots, out var spanLen) &&
                spanLen > 1 &&
                rhsStart + spanLen - 1 == rhsEnd)
            {
                return true;
            }

            // PROPERTY-WRITE complex RHS (mirrors A11 call-arg admission): the base is op 0
            // (already an activation/dynamic identifier); the RHS region is everything between
            // it and the SetNamedProperty store. Admit ANY already-admitted value-producing
            // expression (binary, nested call, member/optional read span, composition thereof)
            // by validating the whole RHS region with the general operand-stack walker,
            // requiring it to net exactly ONE operand. Because the op stream is already in
            // evaluation order and the base precedes the RHS, the store observes
            // base-then-RHS order exactly as the interpreter.
            var setNamedIndex = program.OperationCount - 1;
            return TryValidateAdmittedComplexCallArgumentRegion(
                program,
                argsStartIndex: rhsStart,
                callIndex: setNamedIndex,
                expectedArgumentCount: 1,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers);
        }

        // Computed property write: [base, key..., value..., SetComputedProperty]
        if (lastOp.Kind == ExpressionOpKind.SetComputedProperty &&
            !lastOp.AllowNameInference &&
            TryGetActivationOrPlainDynamicIdentifierReadValue(
                program.GetOperation(0),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            var setComputedIndex = program.OperationCount - 1;

            // Simple-value fast path: [base, key..., value, SetComputedProperty].
            var simpleValueIndex = setComputedIndex - 1;
            if (IsSupportedComputedPropertyKeySpan(
                    program,
                    startInclusive: 1,
                    endExclusive: simpleValueIndex,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers) &&
                IsSimpleOperand(
                    program.GetOperation(simpleValueIndex),
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers))
            {
                return true;
            }

            // Complex-value: find the split where [1, valueStart) is a valid key span (object
            // then key, evaluated FIRST) and [valueStart, set) is a single-operand value region
            // (the RHS, evaluated AFTER the reference). Evaluation order is preserved because
            // the op stream is already base-then-key-then-value; we never reorder.
            for (var valueStart = 2; valueStart < setComputedIndex; valueStart++)
            {
                if (IsSupportedComputedPropertyKeySpan(
                        program,
                        startInclusive: 1,
                        endExclusive: valueStart,
                        identifierConstants,
                        activationSlots,
                        allowsDynamicIdentifiers) &&
                    TryValidateAdmittedComplexCallArgumentRegion(
                        program,
                        argsStartIndex: valueStart,
                        callIndex: setComputedIndex,
                        expectedArgumentCount: 1,
                        identifierConstants,
                        activationSlots,
                        allowsDynamicIdentifiers))
                {
                    return true;
                }
            }

            return false;
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

    // Nested named receiver prefix followed by a computed property write
    // (`box.child[key] = value`):
    // [activation-resolved base, GetNamedProperty(prefix)+, key-span, value, SetComputedProperty].
    private static bool TryIsFirstBoundaryNestedNamedComputedPropertyWriteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 5)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var lastOp = program.GetOperation(program.OperationCount - 1);
        if (lastOp.Kind != ExpressionOpKind.SetComputedProperty ||
            lastOp.AllowNameInference ||
            !TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        // Walk the named receiver-prefix chain (at least one plain named hop).
        var keyStart = 1;
        while (keyStart < program.OperationCount - 1)
        {
            var receiverRead = program.GetOperation(keyStart);
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

            keyStart++;
        }

        // Require at least one named prefix hop; the prefix-free shape is the simple
        // computed write handled by TryIsFirstBoundaryPropertyWriteCandidate.
        if (keyStart < 2)
        {
            return false;
        }

        var valueIndex = program.OperationCount - 2;
        if (keyStart >= valueIndex)
        {
            return false;
        }

        return IsSupportedComputedPropertyKeySpan(
                   program,
                   startInclusive: keyStart,
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

    // Computed property-read receiver prefix followed by a named property write
    // (`box[key].child = value`):
    // [activation-resolved base, computed-read span (`box[key]`), value, SetNamedProperty].
    private static bool TryIsFirstBoundaryComputedPrefixNamedPropertyWriteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 6)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var lastOp = program.GetOperation(program.OperationCount - 1);
        if (lastOp.Kind != ExpressionOpKind.SetNamedProperty ||
            lastOp.AllowNameInference ||
            lastOp.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        // The receiver is a simple computed property read (`box[key]`, optionally with
        // trailing plain named reads) ending exactly before the written value.
        if (!TryMeasureSimpleComputedPropertyReadOperandSpan(
                program,
                0,
                identifierConstants,
                activationSlots,
                out var receiverSpanLength,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        var valueIndex = program.OperationCount - 2;
        if (receiverSpanLength != valueIndex)
        {
            return false;
        }

        return IsSimpleOperand(
            program.GetOperation(valueIndex),
            identifierConstants,
            activationSlots,
            allowsDynamicIdentifiers);
    }

    // Computed property-read receiver prefix followed by a computed property write
    // (`box[k1].child[k2] = value`):
    // [computed-read receiver-prefix span, terminal computed key span, value, SetComputedProperty].
    private static bool TryIsFirstBoundaryComputedPrefixComputedPropertyWriteCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 8)
        {
            return false;
        }

        var setComputedIndex = program.OperationCount - 1;
        var lastOp = program.GetOperation(setComputedIndex);
        if (lastOp.Kind != ExpressionOpKind.SetComputedProperty ||
            lastOp.AllowNameInference)
        {
            return false;
        }

        if (!TryMeasureSimpleComputedPropertyReadOperandSpan(
                program,
                0,
                identifierConstants,
                activationSlots,
                out var receiverSpanLength,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        if (receiverSpanLength <= 0 || receiverSpanLength >= setComputedIndex)
        {
            return false;
        }

        var simpleValueIndex = setComputedIndex - 1;
        if (receiverSpanLength < simpleValueIndex &&
            IsSupportedComputedPropertyKeySpan(
                program,
                startInclusive: receiverSpanLength,
                endExclusive: simpleValueIndex,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers) &&
            IsSimpleOperand(
                program.GetOperation(simpleValueIndex),
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        for (var valueStart = receiverSpanLength + 1; valueStart < setComputedIndex; valueStart++)
        {
            if (IsSupportedComputedPropertyKeySpan(
                    program,
                    startInclusive: receiverSpanLength,
                    endExclusive: valueStart,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers) &&
                TryValidateAdmittedComplexCallArgumentRegion(
                    program,
                    argsStartIndex: valueStart,
                    callIndex: setComputedIndex,
                    expectedArgumentCount: 1,
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers))
            {
                return true;
            }
        }

        return false;
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

    // Nested named receiver-prefix computed property update
    // (`box.child[key]++`, `++box.child[key]`, `box.child[key]--`, `--box.child[key]`):
    // [activation-resolved base, GetNamedProperty+ (>=1 plain named hop), computed key span,
    // UpdateComputedProperty]. Mirrors TryIsFirstBoundaryNestedNamedComputedPropertyWriteCandidate
    // but ends in UpdateComputedProperty (no separate value/Set sequence). A computed receiver
    // prefix (`box[k1].child[k2]++`) keeps keyStart at 1 and declines below.
    private static bool TryIsFirstBoundaryNestedNamedComputedPropertyUpdateCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 4)
        {
            return false;
        }

        var lastOp = program.GetOperation(program.OperationCount - 1);
        if (lastOp.Kind != ExpressionOpKind.UpdateComputedProperty ||
            !TryGetActivationResolvedValue(program.GetOperation(0), identifierConstants, activationSlots))
        {
            return false;
        }

        // Walk the named receiver-prefix chain (at least one plain named hop).
        var stringConstants = program.StringConstants.AsSpan();
        var keyStart = 1;
        while (keyStart < program.OperationCount - 1)
        {
            var receiverRead = program.GetOperation(keyStart);
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

            keyStart++;
        }

        // Require at least one named prefix hop; the prefix-free shape is the simple
        // computed update handled by TryIsFirstBoundaryPropertyUpdateCandidate.
        if (keyStart < 2)
        {
            return false;
        }

        var updateIndex = program.OperationCount - 1;
        if (keyStart >= updateIndex)
        {
            return false;
        }

        return IsSupportedComputedPropertyKeySpan(
            program,
            startInclusive: keyStart,
            endExclusive: updateIndex,
            identifierConstants,
            activationSlots,
            allowsDynamicIdentifiers);
    }

    // A23: computed receiver-prefix property update
    // (`box[k1].child[k2]++`, `--box[k1].child[k2]`, `box[k1].child++`, `box[k1][k2]--`):
    // [computed-read receiver-prefix span (`box[k1]`, optionally trailing plain named reads),
    //  optional computed key span, UpdateNamedProperty | UpdateComputedProperty].
    // Mirrors TryIsFirstBoundaryComputedPrefixNamedPropertyWriteCandidate but ends in an
    // Update opcode (no separate value/Set sequence). The receiver prefix is resolved once via
    // the shared computed-read span helper. Compound (`box[k1].child[k2] += v`) lowers to a
    // GetComputedPropertyForCompoundSet/SetComputedProperty write pair and is NOT an update
    // terminal, so it stays declined by the write-family boundary. A call inside the prefix is
    // rejected because TryMeasureSimpleComputedPropertyReadOperandSpan only admits simple read
    // hops.
    private static bool TryIsFirstBoundaryComputedPrefixPropertyUpdateCandidate(
        ExpressionProgram program,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers)
    {
        if (program.OperationCount < 6)
        {
            return false;
        }

        var stringConstants = program.StringConstants.AsSpan();
        var updateIndex = program.OperationCount - 1;
        var lastOp = program.GetOperation(updateIndex);

        // The receiver is a simple computed property read (`box[k1]`, optionally with trailing
        // plain named reads). For a named-update terminal the prefix is the full receiver; for a
        // computed-update terminal the prefix is everything before the trailing computed key span.
        if (!TryMeasureSimpleComputedPropertyReadOperandSpan(
                program,
                0,
                identifierConstants,
                activationSlots,
                out var receiverSpanLength,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        if (lastOp.Kind == ExpressionOpKind.UpdateNamedProperty)
        {
            // `box[k1].child++` / `box[k1]['a'].b++`: the computed-read span is the whole
            // receiver and the named property is the update target.
            return !lastOp.GetString(stringConstants).IsPrivateName() &&
                   receiverSpanLength == updateIndex;
        }

        if (lastOp.Kind != ExpressionOpKind.UpdateComputedProperty)
        {
            return false;
        }

        // `box[k1].child[k2]++`: the computed-read span resolves the receiver prefix, then a
        // trailing computed key span feeds the computed update.
        if (receiverSpanLength >= updateIndex)
        {
            return false;
        }

        return IsSupportedComputedPropertyKeySpan(
            program,
            startInclusive: receiverSpanLength,
            endExclusive: updateIndex,
            identifierConstants,
            activationSlots,
            allowsDynamicIdentifiers);
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
            ExpressionOpKind.LoadIdentifier => TryGetActivationOrImplicitArgumentsObjectReadValue(
                operation,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers),
            _ => false
        };
    }

    private static bool IsSimpleOperandOrSafeFunctionLiteral(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots,
        bool allowsDynamicIdentifiers = false)
    {
        var operation = program.GetOperation(operationIndex);
        if (operation.Kind == ExpressionOpKind.LoadFunctionLiteral)
        {
            var descriptor = operation.GetObject<FunctionLiteralDescriptor>(program.ObjectConstants.AsSpan());
            return !FunctionLiteralNeedsLexicalThisOrPrivateNameContext(descriptor.Function, out _);
        }

        return IsSimpleOperand(operation, identifierConstants, activationSlots, allowsDynamicIdentifiers);
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
               TryResolveActivationSlotByUniqueName(identifier.Name, activationSlots, out _) ||
               IsYieldStarSyntheticResult(identifier.Name);
    }

    private static bool TryResolveExplicitActivationSlot(
        IdentifierOperand identifier,
        ActivationSlotShape activationSlots,
        out int slotIndex)
    {
        if (identifier.FlatSlotId >= 0)
        {
            slotIndex = identifier.FlatSlotId;
            return true;
        }

        if (identifier.ScopeId == activationSlots.ScopeId && identifier.SlotIndex >= 0)
        {
            slotIndex = identifier.SlotIndex;
            return true;
        }

        slotIndex = -1;
        return false;
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

        return activationSlots.SlotMap.ContainsKey(symbol) ||
               TryResolveActivationSlotByUniqueName(symbol, activationSlots, out _);
    }

    private static bool TryResolveActivationSlotByUniqueName(
        Symbol symbol,
        ActivationSlotShape activationSlots,
        out int slotIndex)
    {
        slotIndex = -1;
        var name = symbol.Name;
        if (string.IsNullOrEmpty(name) || activationSlots.SlotNames.IsDefaultOrEmpty)
        {
            return false;
        }

        var found = false;
        foreach (var (candidate, candidateSlotIndex) in activationSlots.SlotNames)
        {
            if (!string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (found)
            {
                slotIndex = -1;
                return false;
            }

            found = true;
            slotIndex = candidateSlotIndex;
        }

        return found;
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
                case UnifiedBytecodeOpCode.RegisterDisposable:
                case UnifiedBytecodeOpCode.DeclareDynamicVar:
                case UnifiedBytecodeOpCode.DeclareDynamicLexical:
                case UnifiedBytecodeOpCode.InitializeDynamicLexical:
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
