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
                    ExpressionOpKind.StoreIdentifier)
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
                    ExpressionOpKind.UpdateIdentifier or
                    ExpressionOpKind.DeleteIdentifier))
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
                (HasOrdinaryDynamicExpressionDependency(program, activationSlots) ||
                 HasOrdinaryDynamicCallTargetDependency(program, activationSlots)))
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

        // The production VM does not implement same-function tail-call optimization. A call expression
        // returned from inside a finally block is a tail position per spec (the finally completion
        // overrides the protected block), so deep self-recursion through such a return must run on the
        // TCO-capable IR runner (ExecutionPlanRunner) instead of the VM; otherwise the native call stack
        // grows unbounded and overflows. Decline so these functions route to the IR runner.
        if (ContainsCallReturnReachableFromFinally(plan))
        {
            declineCode = UnifiedBytecodeProductionDeclineCode.CallDependency;
            declineReason =
                "A call returned from within a finally block is a tail position and requires the tail-call-optimizing IR runner; not eligible for production unified bytecode routing.";
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

            if (instruction is BindingVariableDeclarationInstruction
                {
                    VarKind: VariableKind.Using or VariableKind.AwaitUsing
                })
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                declineReason = "using declarations require scope-exit disposal and are not eligible for production unified bytecode routing.";
                return true;
            }

            // A43 (burn-down) — DECLINED, kept as the explicit gate. The DeclareFunction opcode and VM
            // handler (UnifiedBytecodeVirtualMachine.DeclareFunction) already port the IR runner's
            // sloppy-mode Annex B.3.3 dual-hoist, so this instruction gate is not the true blocker: a
            // descriptor-backed block function ALSO declines at IsSupportedPushEnvironment because the
            // block's lexical slot for the function name carries NO flat-slot mapping (the value is bound
            // by Symbol via DeclareFunction and read through the Annex B var/dynamic path, never via the
            // block flat slot). Relaxing IsSupportedPushEnvironment to tolerate unmapped lexical slots
            // does admit the sloppy shapes correctly (return g() => 1, conditional, redeclaration), but it
            // BREAKS strict-mode block scoping: the strict IIFE in StrictModeBlockFunctionScopingTests
            // .StrictMode_BlockFunctionDeclaration_ShouldNotHoistToFunctionScope then leaks the block `f`
            // into the function scope (typeof f === "function"/"undefined" outside the block instead of a
            // ReferenceError), because the VM's flat-slot env model resolves the dropped block binding at
            // the activation scope. Admitting A43 cleanly requires giving the block function binding a
            // real flat-slot mapping AND strict-mode-correct VM env resolution — a block-environment
            // change with blast radius across all lexical block bindings, well beyond an Annex-B-scoped
            // edit. Correct decline > fragile admission: the IR runner keeps these shapes correct today.
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
        // inside the CATCH or FINALLY block cannot be driven by the resumable VM's cleanup path —
        // `.return()`/`.throw()` must run a *suspending* finally, which it does not support — so decline
        // it. A suspension in the TRY BODY stays admitted: for-of lowers its yielding loop body into the
        // try body with a NON-suspending iterator-close finally, which the resumable VM handles correctly.

        var finallyBoundary = new HashSet<int> { enterTryIndex };
        if (enterTry.EndFinallyIndex >= 0)
        {
            finallyBoundary.Add(enterTry.EndFinallyIndex);
        }

        if (enterTry.LeaveTryIndex >= 0)
        {
            finallyBoundary.Add(enterTry.LeaveTryIndex);
        }

        var catchBoundary = new HashSet<int>(finallyBoundary);
        if (enterTry.FinallyIndex >= 0)
        {
            catchBoundary.Add(enterTry.FinallyIndex);
        }

        if (CleanupBlockHasSuspension(instructions, enterTry.FinallyIndex, finallyBoundary, out instructionName)
            || CleanupBlockHasSuspension(instructions, enterTry.HandlerIndex, catchBoundary, out instructionName))
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

        // SCOPED close-finally guard for the NEW captured/free dynamic-UPDATE admission only. The resumable
        // VM's early-close path (`.return()`/`.throw()` while the generator is suspended at a yield protected
        // by this try) does not re-drive a user finally body — a PRE-EXISTING limitation shared by every
        // non-empty finally (property-write finallies included; those keep routing exactly as on main, so the
        // B32 normal-completion pin stays green). This guard does NOT widen that limitation. It only declines
        // the shapes THIS change newly admitted: a finally that performs a free/captured dynamic UPDATE
        // (`n++` where `n` escapes the activation's slots, lowering to UpdateDynamicIdentifier). Before this
        // change such a finally declined at the increment gate and ran on the IR runner (where early-close
        // finallies execute correctly — see IteratorCloseGeneratorTests `finally { finallyCount++; }`);
        // admitting UpdateDynamicIdentifier would otherwise newly route those generators and silently drop the
        // finally on early close. Declining them here restores their IR-runner route. (A free/captured READ in
        // the finally is unaffected — only the new dynamic UPDATE is gated.)
        if (enterTry.FinallyIndex >= 0 &&
            FinallyRegionContainsFreeOrCapturedUpdate(instructions, enterTry, activationSlots))
        {
            instructionName = "finally body performs a captured/free dynamic update whose early-close execution the resumable VM does not drive";
            return true;
        }

        return false;
    }

    private static bool FinallyRegionContainsFreeOrCapturedUpdate(
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

        // Walk only the finally region (from its first instruction up to EndFinally). Detect an
        // IncrementSlotInstruction whose target does NOT resolve to one of this activation's slots — that is
        // the free/captured update (`n++`) that lowers to UpdateDynamicIdentifier and is the sole shape this
        // change newly admitted into finally-on-close territory.
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
            if (instruction is IncrementSlotInstruction
                {
                    TargetSymbol: { } targetSymbol, FlatSlotId: var flatSlotId
                } &&
                !TryResolveActivationSymbolSlot(targetSymbol, flatSlotId, activationSlots))
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

        for (var instructionIndex = 0; instructionIndex < plan.Instructions.Length; instructionIndex++)
        {
            if (activeWithDepths[instructionIndex] < 0)
            {
                continue;
            }

            var instruction = plan.Instructions[instructionIndex];
            if (isAsyncGenerator && instruction is YieldStarInstruction)
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                declineReason =
                    "Async-generator yield* delegation is not eligible for resumable unified bytecode routing; it stays on the async-generator IR runner until the VM owns delegated async iterator settlement.";
                return true;
            }

            // Ordinary free/dynamic identifier resolution (a free variable READ or a free function
            // CALL target that escapes this activation's slots, e.g. `yield outerVar` /
            // `yield helper(x)`) is admitted into the resumable route. Resolution runs against the
            // live closure environment threaded onto UnifiedBytecodeResumeState.CallingEnvironment
            // (#3108), which is captured at construction and stable across yield/await suspension, so a
            // resumed step observes the CURRENT value of a captured/outer binding (closure capture and
            // outer mutation between yields both resolve correctly) and an uninitialized free binding
            // still throws ReferenceError. Free dynamic *writes* (StoreDynamicIdentifier /
            // ResolveDynamicIdentifierReference) and other dynamic-environment opcodes remain declined:
            // the resumable opcode allowlist (TryFindUnsupportedResumableOpcode) only admits the dynamic
            // read / call-target opcodes, so any write shape still routes to the interpreter.
            const bool allowsDynamicIdentifiers = true;
            if (!IsSupportedResumableInstruction(instruction, out declineReason))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                return true;
            }

            // Slot-update const-safety guard. A `x++` / `x--` whose target resolves to a lexical
            // (`let`/`const`) slot is declined: the resumable VM has no const-slot metadata
            // (UnifiedBytecodeResumeState carries neither a const-slot bitmap nor slot environments), so it
            // cannot reproduce the `TypeError: Assignment to constant variable` the sync VM raises for a
            // `const` update. Because the lowered plan does not distinguish `let` from `const` (const-ness
            // is a runtime environment property), the only statically provable non-const targets are
            // parameters and `var`-declared slots — neither of which appears in
            // ActivationSlotShape.LexicalSlotIndices. Declining every lexical-slot update therefore keeps
            // exactly the const-unsafe shapes on the interpreter while admitting the provably-safe
            // parameter/`var` updates. (This mirrors the const gap the already-admitted StoreSlot path has,
            // but stays on the safe side of it rather than widening it.)
            if (instruction is IncrementSlotInstruction
                {
                    TargetSymbol: { } updateTargetSymbol, FlatSlotId: var updateFlatSlotId, SlotIndex: var updateSlotIndex
                })
            {
                if (!TryResolveActivationSymbolSlot(updateTargetSymbol, updateFlatSlotId, activationSlots))
                {
                    // Captured / free update target (`n++` where `n` is an enclosing-function local or a
                    // module/script-level binding that escapes this activation's slots). The instruction
                    // lowers to UpdateDynamicIdentifier, which resolves the name against the live closure
                    // environment threaded onto UnifiedBytecodeResumeState.CallingEnvironment (#3108) —
                    // captured at construction and stable across yield/await, so the update aliases the SAME
                    // enclosing heap slot before and after each suspension. const-safety is enforced by the
                    // environment itself (ResolveIdentifierAssignmentReference -> reference.SetValue throws the
                    // `TypeError: Assignment to constant variable` for a captured `const`), so unlike the
                    // resolved-slot path below this needs no const-slot metadata. The resumable opcode allowlist
                    // (TryFindUnsupportedResumableOpcode) admits UpdateDynamicIdentifier, and the
                    // ExecuteResumable switch carries its handler; both stay 1:1. Do NOT decline here — the
                    // captured-write tier is admitted (mirrors the sync captured-closure route, commit
                    // 1c0b30675). The one exception is a captured/free update sited INSIDE a finally that
                    // protects a yield/await: TryFindTryFinallyRegionResumableSuspension declines that so the
                    // generator keeps its IR-runner early-close finally semantics.
                }
                else if (IsLexicalSlotUpdateTarget(updateSlotIndex, updateFlatSlotId, activationSlots))
                {
                    declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                    declineReason =
                        $"Update target '{updateTargetSymbol.Name}' is a lexical (let/const) slot; the resumable VM cannot enforce const-assignment semantics and the update is not eligible for resumable unified bytecode routing.";
                    return true;
                }
            }

            // Plain slot assignment (`x = v`) to a lexical (`let`/`const`) slot keeps its interpreter route
            // for the SAME reason as the slot-update guard above: the resumable VM
            // (UnifiedBytecodeResumeState) carries no const-slot metadata, so it cannot raise the
            // `TypeError: Assignment to constant variable` the sync VM enforces for a `const` reassignment.
            // The already-admitted resumable StoreSlot opcode does not distinguish `const`, so without this
            // guard `const x = 1; x = 2` inside a generator/async body silently succeeds (yields `1|2`).
            // Declining every resolved lexical-slot assignment keeps the const-unsafe shapes on the
            // interpreter while still admitting provably-non-const parameter/`var` assignments. (Unresolved
            // free/dynamic stores already decline at the opcode level and are not affected here.)
            if (instruction is AssignmentSlotInstruction
                {
                    TargetSymbol: { } assignTargetSymbol, FlatSlotId: var assignFlatSlotId, SlotIndex: var assignSlotIndex
                }
                && TryResolveActivationSymbolSlot(assignTargetSymbol, assignFlatSlotId, activationSlots)
                && IsLexicalSlotUpdateTarget(assignSlotIndex, assignFlatSlotId, activationSlots))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape;
                declineReason =
                    $"Assignment target '{assignTargetSymbol.Name}' is a lexical (let/const) slot; the resumable VM cannot enforce const-assignment semantics and the assignment is not eligible for resumable unified bytecode routing.";
                return true;
            }

            // `yield* <iterable>` keeps its prior verified routing when the iterable resolves through a
            // free/dynamic identifier (`yield* spyIterable`). The resumable YieldStar delegation protocol
            // was only validated against slot-resolved iterables; admitting a dynamic-identifier iterable
            // here would newly route those `yield*` bodies through the resumable VM and regress the
            // delegation semantics (next/return/throw forwarding, iterator-result shape). Declining keeps
            // them on the IR runner. Free reads/calls in non-`yield*` positions remain admitted.
            if (instruction is YieldStarInstruction { IterableProgram: { } yieldStarIterable } &&
                YieldStarIterableHasFreeIdentifierDependency(yieldStarIterable, activationSlots))
            {
                declineCode = UnifiedBytecodeProductionDeclineCode.DynamicLookupDependency;
                declineReason =
                    "yield* over a free/dynamic-identifier iterable keeps its IR-runner routing and is not eligible for resumable unified bytecode routing.";
                return true;
            }

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

            if (TryGetResumableExpressionProgram(instruction, out var program) &&
                TryFindExpressionDecline(
                    program,
                    activationSlots,
                    allowsDynamicIdentifiers,
                    allowImplicitArgumentsObjectPropertyReadOperands: false,
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
            // Slot increment / decrement (`x++`, `x--`, `++x`, `--x`) over a parameter or `var`-declared
            // slot. The instruction carries no AwaitedProgram (a prefix/postfix update on a slot cannot
            // itself suspend — its operand is `slots[index]`, not a sub-expression that yields/awaits), so
            // it always runs to completion inside one resumable step and never needs operand-stack
            // restoration across a suspension. It is admitted here only structurally; the lexical-slot
            // const-safety guard in TryFindResumablePlanDecline declines any update whose target is a
            // lexical (`let`/`const`) slot, because the resumable VM carries no const-slot metadata and
            // therefore cannot enforce the `TypeError` on a `const` reassignment the sync VM raises.
            case IncrementSlotInstruction:
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
            case EnterTryInstruction:
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
            case BreakableEnterInstruction { ConstructKind: BreakableKind.ResetsCompletionValue }:
            case BreakableExitInstruction:
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
                // Slot increment / decrement (`x++`, `x--`, `++x`, `--x`). Reaches this allowlist only for
                // the parameter / `var` targets the instruction-level lexical-slot const-safety guard
                // (TryFindResumablePlanDecline) admits — every lexical (`let`/`const`) target is declined
                // before compilation because the resumable VM carries no const-slot metadata. The opcode
                // reads `slots[index]`, computes the numeric ++/-- in place, and pushes the old or new
                // value; it never touches the operand stack across a suspension (an update cannot itself
                // yield/await), so no resume-state restoration is involved.
                UnifiedBytecodeOpCode.UpdateSlot or
                UnifiedBytecodeOpCode.InitializeSlot or
                UnifiedBytecodeOpCode.Binary or
                UnifiedBytecodeOpCode.GetNamedProperty or
                UnifiedBytecodeOpCode.GetComputedProperty or
                // Property WRITES (`o.x = v`, `o[k] = v`, `this.x = v`) inside a resumable body. The
                // assignment value can suspend (`o.x = yield 1`); the base (and, for the computed form,
                // the key) sit on the operand stack across the suspension and are restored on resume
                // because UnifiedBytecodeResumeState.OperandStack is the stable backing store — the same
                // mechanism the admitted property READS already rely on. The resumable handlers reuse the
                // sync VM's SetPropertyValue helper (which ORs context.CurrentScope.IsStrict for strict
                // semantics) and translate a thrown set (e.g. a strict write to a read-only property) into
                // the resumable Throw step. Super-property writes stay omitted: those opcodes have no
                // resumable handler yet, so leaving them off this allowlist declines them back to the
                // interpreter.
                UnifiedBytecodeOpCode.SetNamedProperty or
                UnifiedBytecodeOpCode.SetComputedProperty or
                // Property UPDATES (`o.x++`, `o[k]--`) and DELETES (`delete o.x`, `delete o[k]`) inside a
                // resumable body. Like the property writes above, these opcodes operate purely on the
                // operand stack — the base (and, for the computed form, the key) sit on
                // UnifiedBytecodeResumeState.OperandStack across any suspension in a sibling sub-expression
                // and are restored on resume. The opcodes themselves cannot suspend (no AwaitedProgram), so
                // they always run to completion inside one resumable step. The resumable handlers reuse the
                // sync VM's UpdatePropertyValue / DeleteNamedProperty / DeleteComputedProperty helpers,
                // threading the body's own strictness (state.IsStrict) so a strict update/delete of a
                // read-only / non-configurable property throws and translates to the resumable Throw step.
                // Super-property updates/deletes stay omitted (no resumable super handler yet).
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
                UnifiedBytecodeOpCode.JumpIfFalse or
                UnifiedBytecodeOpCode.JumpIfShortCircuitFalse or
                UnifiedBytecodeOpCode.JumpIfShortCircuitTrue or
                UnifiedBytecodeOpCode.JumpIfShortCircuitNotNullish or
                UnifiedBytecodeOpCode.EnterTry or
                UnifiedBytecodeOpCode.LeaveTry or
                UnifiedBytecodeOpCode.EndFinally or
                UnifiedBytecodeOpCode.Return or
                UnifiedBytecodeOpCode.ReturnUndefined or
                UnifiedBytecodeOpCode.Throw or
                // Synchronous call dispatch (non-optional `f()`, `o.m()`, `o[k]()`). The optional
                // call-target opcodes and super/construct boundaries remain unsupported below because
                // they require short-circuit-flag persistence / dynamic-environment plumbing the
                // resumable state does not carry.
                UnifiedBytecodeOpCode.PrepareIdentifierCallTarget or
                UnifiedBytecodeOpCode.PrepareNamedCallTarget or
                UnifiedBytecodeOpCode.PrepareComputedCallTarget or
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
                // `TypeError: Assignment to constant variable` for a captured `const`), not by absent
                // const-slot metadata, so admitting it is sound where the resolved-lexical-SLOT update stays
                // declined. The ExecuteResumable switch carries the UpdateDynamicIdentifier handler (kept 1:1
                // with this allowlist).
                //
                // The captured/free plain and compound STORE (`n = v`, `n += v`) is NOT admitted: it lowers
                // to the three-opcode ResolveDynamicIdentifierReference -> <RHS> ->
                // StoreDynamicIdentifierReference sequence whose resolved AssignmentReference lives in a
                // transient VM-local array, NOT on UnifiedBytecodeResumeState. The RHS can suspend
                // (`n = yield`), and the resume state does not thread the pending reference across the
                // suspension, so admitting it would corrupt the store target on resume. It stays declined on
                // the IR runner (ResolveDynamicIdentifierReference is absent from this allowlist). The
                // remaining dynamic reference / single-shot store / delete opcodes likewise stay omitted.
                UnifiedBytecodeOpCode.UpdateDynamicIdentifier or
                UnifiedBytecodeOpCode.CallInvocationBoundary or
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
                        isComplexRhsPropertyWriteCandidate)
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
                        isComplexRhsPropertyWriteCandidate)
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
                        isComplexRhsPropertyWriteCandidate)
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

                        // Plain continuation read of an optional-computed-start read chain used as
                        // a call argument (`fn(box?.[key].value)`).
                        if (TryIsEmbeddedOptionalComputedReadChainCallArgumentContinuation(
                                program,
                                operationIndex,
                                identifierConstants,
                                activationSlots))
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

                    // Read op of an optional computed read used as a call argument (`fn(box?.[key])`).
                    if (TryIsOptionalComputedReadCallArgumentOperation(program, operationIndex, identifierConstants, activationSlots))
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
                        if (TryIsOptionalNamedThenComputedReadCallArgumentOperation(program, operationIndex, identifierConstants, activationSlots))
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

                    // Nullish guard of an optional computed read used as a call argument (`fn(box?.[key])`).
                    if (TryIsOptionalComputedReadCallArgumentOperation(program, operationIndex, identifierConstants, activationSlots))
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

    // Detects a free/dynamic identifier used anywhere inside a yield* iterable program, including a free
    // CALL target (`yield* makeIterator()`). Broader than HasOrdinaryDynamicExpressionDependency, which
    // intentionally omits LoadIdentifierCallTarget; here a free callee also disqualifies resumable
    // routing so the yield* delegation keeps its verified IR-runner path.
    private static bool YieldStarIterableHasFreeIdentifierDependency(
        ExpressionProgram program,
        ActivationSlotShape activationSlots)
    {
        if (HasOrdinaryDynamicExpressionDependency(program, activationSlots))
        {
            return true;
        }

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
        if (instruction.LexicalSlotIndices.IsDefaultOrEmpty)
        {
            return true;
        }

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

        if (instruction.IterableProgram is null && instruction.AwaitedProgram is null ||
            instruction.IterableProgram is not null && instruction.AwaitedProgram is not null)
        {
            reason = "Iterator driver sources must be lowered to exactly one expression bytecode payload.";
            return false;
        }

        // Slice A (#2678): sync iterator drivers that own a TDZ head environment
        // (for example `for (const x of ...)`) are now admitted. Awaited sync-kind
        // sources in async functions are admitted through the resumable VM by
        // compiling the source expression followed by AwaitValue and IteratorInit.
        // Async-kind drivers remain declined pending later slices.
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

    // Recognizes a plain continuation read (IsOptional:false, ShortCircuit:true) that
    // belongs to an optional-start-then-plain named read chain (`box?.child.value`)
    // used as a call argument. The span is [simple base, GetNamedProperty(IsOptional,
    // !SC), GetNamedProperty(!IsOptional, SC)+] and the enclosing program ends in a
    // Call. A continuation hop that is itself optional (`box?.child?.value`) is not
    // matched, so that shape still declines as OptionalChainDependency.
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
            op.IsOptional ||
            !op.ShortCircuitOnNullishTarget ||
            op.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        // Walk back over preceding plain continuation reads to the optional hop.
        var hopIndex = operationIndex - 1;
        while (hopIndex >= 0)
        {
            var prev = program.GetOperation(hopIndex);
            if (prev.Kind == ExpressionOpKind.GetNamedProperty &&
                !prev.IsOptional &&
                prev.ShortCircuitOnNullishTarget &&
                !prev.GetString(stringConstants).IsPrivateName())
            {
                hopIndex--;
                continue;
            }

            break;
        }

        if (hopIndex < 1)
        {
            return false;
        }

        var hop = program.GetOperation(hopIndex);
        if (hop.Kind != ExpressionOpKind.GetNamedProperty ||
            !hop.IsOptional ||
            hop.ShortCircuitOnNullishTarget ||
            hop.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        return IsSimpleOperand(
            program.GetOperation(hopIndex - 1),
            identifierConstants,
            activationSlots,
            allowsDynamicIdentifiers);
    }

    // Recognizes a plain continuation read (IsOptional:false, ShortCircuit:true) that
    // belongs to an optional-computed-start-then-plain named read chain
    // (`box?.[key].value`) used as a call argument. The span is
    // [simple base, JumpIfNullish(RWU), key-span, GetComputedProperty(!opt,!SC),
    // GetNamedProperty(!opt, SC)+] and the enclosing program ends in a Call. An optional
    // continuation hop (`box?.[key]?.value`) is not matched, so it still declines.
    private static bool TryIsEmbeddedOptionalComputedReadChainCallArgumentContinuation(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
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
            op.IsOptional ||
            !op.ShortCircuitOnNullishTarget ||
            op.GetString(stringConstants).IsPrivateName())
        {
            return false;
        }

        // Walk back over preceding plain continuation reads to the computed read.
        var readIndex = operationIndex - 1;
        while (readIndex >= 0)
        {
            var prev = program.GetOperation(readIndex);
            if (prev.Kind == ExpressionOpKind.GetNamedProperty &&
                !prev.IsOptional &&
                prev.ShortCircuitOnNullishTarget &&
                !prev.GetString(stringConstants).IsPrivateName())
            {
                readIndex--;
                continue;
            }

            break;
        }

        if (readIndex < 0)
        {
            return false;
        }

        var computedRead = program.GetOperation(readIndex);
        if (computedRead.Kind != ExpressionOpKind.GetComputedProperty ||
            computedRead.IsOptional ||
            computedRead.ShortCircuitOnNullishTarget)
        {
            return false;
        }

        // Verify the optional-computed prefix [base, JumpIfNullish(RWU), key.., GetComputedProperty].
        return TryIsEmbeddedOptionalComputedReadSpanOperation(
            program,
            readIndex,
            identifierConstants,
            activationSlots);
    }

    // Recognizes a JumpIfNullish or GetComputedProperty operation that belongs to a
    // baseline optional computed property-read (`box?.[key]`) used as a call argument.
    // Reuses the embedded optional-computed span scanner, scoped to a program that
    // ends in a Call so it only admits the call-argument context.
    private static bool TryIsOptionalComputedReadCallArgumentOperation(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (program.OperationCount < 2 ||
            program.GetOperation(program.OperationCount - 1).Kind != ExpressionOpKind.Call)
        {
            return false;
        }

        return TryIsEmbeddedOptionalComputedReadSpanOperation(
            program,
            operationIndex,
            identifierConstants,
            activationSlots);
    }

    // Recognizes the chain-short-circuit GetComputedProperty op that belongs to an
    // optional-named-then-plain-computed read chain (`box?.prop[key]`, `box?.a.b[key]`)
    // used as a call argument. The span is [simple base, GetNamedProperty(IsOptional,
    // !SC), GetNamedProperty(!IsOptional, SC)*, key.., GetComputedProperty(!IsOptional,
    // SC)] and the enclosing program ends in a Call. A second optional hop
    // (`box?.prop?.[key]`) emits a JumpIfNullish instead and is not matched here, so it
    // still declines as OptionalChainDependency.
    private static bool TryIsOptionalNamedThenComputedReadCallArgumentOperation(
        ExpressionProgram program,
        int operationIndex,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ActivationSlotShape activationSlots)
    {
        if (operationIndex < 0 || operationIndex >= program.OperationCount)
        {
            return false;
        }

        if (program.GetOperation(program.OperationCount - 1).Kind != ExpressionOpKind.Call)
        {
            return false;
        }

        var computedOp = program.GetOperation(operationIndex);
        if (computedOp.Kind != ExpressionOpKind.GetComputedProperty ||
            computedOp.IsOptional ||
            !computedOp.ShortCircuitOnNullishTarget)
        {
            return false;
        }

        // Walk back over the supported computed key span and the named continuation/hop
        // prefix to confirm the optional-named-then-computed shape rooted at a simple base.
        var stringConstants = program.StringConstants.AsSpan();
        var hopIndex = -1;
        for (var spanStart = 1; spanStart < operationIndex; spanStart++)
        {
            var hop = program.GetOperation(spanStart);
            if (hop.Kind != ExpressionOpKind.GetNamedProperty ||
                !hop.IsOptional ||
                hop.ShortCircuitOnNullishTarget ||
                hop.GetString(stringConstants).IsPrivateName())
            {
                continue;
            }

            if (!IsSimpleOperand(
                    program.GetOperation(spanStart - 1),
                    identifierConstants,
                    activationSlots,
                    allowsDynamicIdentifiers: true))
            {
                continue;
            }

            hopIndex = spanStart;
            break;
        }

        if (hopIndex < 1)
        {
            return false;
        }

        // Plain named continuations between the optional hop and the computed key span.
        var keyStart = hopIndex + 1;
        while (keyStart < operationIndex)
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

        return IsSupportedComputedPropertyKeySpan(
            program,
            keyStart,
            operationIndex,
            identifierConstants,
            activationSlots,
            allowsDynamicIdentifiers: true);
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
        if (callTarget.Kind != ExpressionOpKind.LoadIdentifierCallTarget ||
            callTarget.IsArguments)
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
        var stackDepth = 0;
        var hasNamedMemberCall = false;
        for (var operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
        {
            var operation = program.GetOperation(operationIndex);
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadLiteral:
                case ExpressionOpKind.LoadThis:
                case ExpressionOpKind.LoadNewTarget:
                    stackDepth++;
                    break;

                case ExpressionOpKind.LoadIdentifier:
                    if (!IsSimpleOperand(operation, identifierConstants, activationSlots, allowsDynamicIdentifiers))
                    {
                        return false;
                    }

                    stackDepth++;
                    break;

                case ExpressionOpKind.GetNamedProperty:
                    if (stackDepth < 1 ||
                        operation.IsOptional ||
                        operation.ShortCircuitOnNullishTarget ||
                        operation.GetString(stringConstants).IsPrivateName())
                    {
                        return false;
                    }

                    break;

                case ExpressionOpKind.LoadNamedCallTarget:
                    if (stackDepth < 1 ||
                        operation.IsOptional ||
                        operation.ShortCircuitOnNullishTarget ||
                        operation.GetString(stringConstants).IsPrivateName())
                    {
                        return false;
                    }

                    stackDepth++;
                    break;

                case ExpressionOpKind.Call:
                    if (!operation.HasExplicitThis ||
                        operation.IsDirectEval ||
                        operation.SpreadMaskConstantIndex >= 0 ||
                        stackDepth < operation.ArgumentCount + 2)
                    {
                        return false;
                    }

                    stackDepth -= operation.ArgumentCount + 1;
                    hasNamedMemberCall = true;
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

        return hasNamedMemberCall && stackDepth == 1;
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
                    depth,
                    out depth))
            {
                return false;
            }

            index++;
        }

        // The whole argument region must leave exactly one operand per logical argument on the
        // stack (and the per-op deltas above never underflowed), matching the call's arity.
        return depth == expectedArgumentCount;
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
                depthAfter = depthBefore + 1;
                return true;

            case ExpressionOpKind.LoadIdentifier:
                if (!IsSimpleOperand(op, identifierConstants, activationSlots, allowsDynamicIdentifiers))
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

            case ExpressionOpKind.Binary:
                if (depthBefore < 2 || !IsProductionBinaryOperator(op.Operator))
                {
                    return false;
                }

                depthAfter = depthBefore - 1;
                return true;

            case ExpressionOpKind.UnaryPlus:
            case ExpressionOpKind.UnaryMinus:
            case ExpressionOpKind.UnaryLogicalNot:
            case ExpressionOpKind.UnaryBitwiseNot:
            case ExpressionOpKind.UnaryVoid:
            case ExpressionOpKind.TypeOf:
                // operand -> result, in place: net 0.
                return depthBefore >= 1;

            case ExpressionOpKind.LoadIdentifierCallTarget:
                // Pushes <undefined this, callee>: net +2. Decline arguments/eval and unresolved
                // free identifiers (those have no admitted lowering here).
                if (op.IsArguments)
                {
                    return false;
                }

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
                // receiver stays as `this`, callee pushed: net +1. Reject optional/private.
                if (depthBefore < 1 ||
                    op.IsOptional ||
                    op.ShortCircuitOnNullishTarget ||
                    op.GetString(stringConstants).IsPrivateName())
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

            case ExpressionOpKind.Call:
                // pops <this, callee, arg0..arg(n-1)> and pushes the result: net -(argc + 1).
                // Reject spread/eval/non-explicit-this; require enough operands on the stack.
                if (!op.HasExplicitThis ||
                    op.IsDirectEval ||
                    op.SpreadMaskConstantIndex >= 0 ||
                    depthBefore < op.ArgumentCount + 2)
                {
                    return false;
                }

                depthAfter = depthBefore - (op.ArgumentCount + 1);
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
                    allowsDynamicIdentifiers))
            {
                var pushIndex = i + elementSpanLength;
                if (pushIndex >= program.OperationCount)
                {
                    spanLength = 0;
                    return false;
                }

                var pushOp = program.GetOperation(pushIndex);
                if (pushOp.Kind is not (ExpressionOpKind.ArrayPush or ExpressionOpKind.ArraySpread))
                {
                    spanLength = 0;
                    return false;
                }

                i = pushIndex + 1;
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
        bool allowControlExpressions)
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

        if (TryMeasureSimpleTypeOfOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        if (TryMeasureSimpleBinaryOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        if (TryMeasureSimpleUnaryOperandSpan(
                program,
                startIndex,
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
                allowControlExpressions: false))
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
                allowControlExpressions: true))
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
                allowControlExpressions: false))
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
                allowControlExpressions: true))
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
                allowControlExpressions: true))
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
        bool allowsDynamicIdentifiers = false)
    {
        if (TryMeasureSimpleComputedPropertyReadOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        return TryMeasureSimpleNamedPropertyReadOperandSpan(
            program,
            startIndex,
            identifierConstants,
            activationSlots,
            out spanLength,
            allowsDynamicIdentifiers);
    }

    private static bool TryMeasureSimpleNamedPropertyReadOperandSpan(
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

        var stringConstants = program.StringConstants.AsSpan();
        var i = startIndex + 1;
        while (i < program.OperationCount &&
               IsPlainNamedPropertyRead(program.GetOperation(i), stringConstants))
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

    // Measures an optional-start-then-plain named property-read operand span
    // (`box?.child.value`, `box?.child.nested.deep`): a simple base operand, one
    // optional GetNamedProperty hop (IsOptional, !ShortCircuit), and at least one
    // plain continuation read carrying the chain short-circuit flag
    // (!IsOptional, ShortCircuitOnNullishTarget). A nullish base short-circuits the
    // whole chain to undefined. A continuation hop that is itself optional
    // (`box?.child?.value`) is NOT part of this span and leaves the call to decline.
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
                continuation.IsOptional ||
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

    // Measures an optional-named-then-plain-computed read operand span used as a call
    // argument (`box?.prop[key]`, `box?.prop[a + b]`, `box?.a.b[key]`): a simple base
    // operand, an optional named hop (GetNamedProperty(IsOptional, !SC, non-private)),
    // zero or more plain named continuations (GetNamedProperty(!IsOptional, SC,
    // non-private)), a supported computed key span, and a chain-short-circuit
    // GetComputedProperty (!IsOptional, SC). A nullish base short-circuits the whole
    // chain to undefined. A second optional hop (`box?.prop?.[key]`) carries its own
    // JumpIfNullish and is not consumed here, so it still declines.
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

            // Trailing plain named continuations (`box?.prop[key].child`) carry the chain
            // short-circuit flag; an optional continuation hop is not consumed here.
            var index = computedIndex + 1;
            while (index < program.OperationCount)
            {
                var continuation = program.GetOperation(index);
                if (continuation.Kind != ExpressionOpKind.GetNamedProperty ||
                    continuation.IsOptional ||
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

    // Measures a baseline optional computed property-read operand span
    // (`box?.[key]`, `box?.[a + b]`): a simple base operand, a
    // JumpIfNullish(ReplaceWithUndefined) short-circuit guard, a supported computed
    // key span, and a non-optional/non-short-circuit GetComputedProperty whose read
    // is the jump target. A nullish base short-circuits the read to undefined.
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

        var jumpOp = program.GetOperation(startIndex + 1);
        if (jumpOp.Kind != ExpressionOpKind.JumpIfNullish ||
            !jumpOp.ReplaceWithUndefined)
        {
            return false;
        }

        // The optional guard jumps to the instruction after the computed read.
        var computedIndex = jumpOp.Target - 1;
        if (computedIndex <= startIndex + 1 ||
            computedIndex >= program.OperationCount)
        {
            return false;
        }

        var computedOp = program.GetOperation(computedIndex);
        if (computedOp.Kind != ExpressionOpKind.GetComputedProperty ||
            computedOp.IsOptional ||
            computedOp.ShortCircuitOnNullishTarget)
        {
            return false;
        }

        if (!IsSupportedComputedPropertyKeySpan(
                program,
                startIndex + 2,
                computedIndex,
                identifierConstants,
                activationSlots,
                allowsDynamicIdentifiers))
        {
            return false;
        }

        // Allow plain named continuation reads after the optional computed read
        // (`box?.[key].value`, `box?.[key].a.b`). They carry the chain short-circuit
        // flag (!IsOptional, ShortCircuit) so a nullish base short-circuits the whole
        // chain to undefined; an optional continuation hop (`box?.[key]?.value`) is not
        // consumed here and leaves the call to decline.
        var stringConstants = program.StringConstants.AsSpan();
        var index = computedIndex + 1;
        while (index < program.OperationCount)
        {
            var continuation = program.GetOperation(index);
            if (continuation.Kind != ExpressionOpKind.GetNamedProperty ||
                continuation.IsOptional ||
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
        bool allowsDynamicIdentifiers)
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

        var stringConstants = program.StringConstants.AsSpan();
        var keyStart = startIndex + 1;
        while (keyStart < program.OperationCount &&
               IsPlainNamedPropertyRead(program.GetOperation(keyStart), stringConstants))
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
                   IsPlainNamedPropertyRead(program.GetOperation(endExclusive), stringConstants))
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

        if (TryMeasureSimpleBinaryOperandSpan(
                program,
                startIndex,
                identifierConstants,
                activationSlots,
                out spanLength,
                allowsDynamicIdentifiers))
        {
            return true;
        }

        if (TryMeasureSimpleUnaryOperandSpan(
                program,
                startIndex,
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
        if (startIndex + 1 >= program.OperationCount)
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

        // Walk an optional named receiver-prefix chain (e.g. box.child[key] += value).
        // The collapsed receiver stays a single stack value so RequireObjectCoercible.Depth
        // remains 1; the computed key span starts after the prefix.
        var stringConstants = program.StringConstants.AsSpan();
        var keyStart = 1;
        var suffixStart = program.OperationCount - 7;
        while (keyStart < suffixStart)
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

        if (keyStart >= suffixStart ||
            !IsSupportedComputedPropertyKeySpan(
                program,
                startInclusive: keyStart,
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

    /// <summary>
    ///     Conservatively reports whether a slot-update target (<c>x++</c> / <c>x--</c>) addresses a
    ///     lexical (<c>let</c>/<c>const</c>) slot. Const-ness is a runtime environment property that the
    ///     lowered plan does not preserve, so the only statically provable non-const slots are parameters
    ///     and <c>var</c> bindings — neither of which is recorded in
    ///     <see cref="ActivationSlotShape.LexicalSlotIndices" />. The update instruction may carry either a
    ///     resolved flat slot id (<paramref name="flatSlotId" /> &gt;= 0) or a scope-relative
    ///     <paramref name="slotIndex" />; because the two index spaces are not interchangeable here, this
    ///     treats the target as lexical when <em>either</em> index appears in the lexical set. Returning
    ///     <c>true</c> only ever causes an extra decline (the update keeps its interpreter route), so an
    ///     over-broad match is safe; an under-broad one would not be, hence the union.
    /// </summary>
    private static bool IsLexicalSlotUpdateTarget(int slotIndex, int flatSlotId, ActivationSlotShape activationSlots)
    {
        var lexicalSlotIndices = activationSlots.LexicalSlotIndices;
        if (lexicalSlotIndices.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var lexicalSlotIndex in lexicalSlotIndices)
        {
            if (lexicalSlotIndex == slotIndex || (flatSlotId >= 0 && lexicalSlotIndex == flatSlotId))
            {
                return true;
            }
        }

        return false;
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
                case UnifiedBytecodeOpCode.DeclareDynamicLexical:
                case UnifiedBytecodeOpCode.InitializeDynamicLexical:
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
