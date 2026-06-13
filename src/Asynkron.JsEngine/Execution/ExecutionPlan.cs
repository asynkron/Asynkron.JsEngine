using System.Collections.Immutable;
using System.Threading;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Intermediate representation for pauseable functions (generators, async functions, async generators).
///     The plan contains a flat list of instructions that model sequential execution, branching, and yield/await points.
///     The interpreter maintains a program counter and executes the instructions synchronously, allowing
///     .next/.throw/.return to resume exactly where execution paused.
/// </summary>
/// <param name="Instructions">The instruction sequence.</param>
/// <param name="EntryPoint">Index of the first instruction to execute.</param>
/// <param name="SlotCount">Number of slots to allocate for internal variables (iterator states, values, etc.).</param>
/// <param name="SlotSymbols">Symbols mapped to slot indices for O(1) variable access.</param>
/// <param name="RootSlotCount">Slot count required for the root (function) scope user bindings.</param>
/// <param name="RootSlotMap">Slot map for the root (function) scope user bindings.</param>
/// <param name="RootLexicalBindings">Lexical bindings in the root scope (for TDZ).</param>
/// <param name="ScopeLexicalBindings">Lexical bindings per scope id.</param>
/// <param name="RootScopeId">Explicit root scope id for this plan (default 0 for compatibility).</param>
/// <param name="LayoutId">Stable identity for the expected slot layout, used to validate pooled environments.</param>
/// <param name="FlatSlotCount">Total number of flat slots needed for O(1) variable access across all scopes.</param>
/// <param name="FlatSlotMappings">Maps scopeId to array of (slotIndex, flatSlotId) for eager flat slot initialization.</param>
/// <param name="ActivationSlots">Precomputed slot-shape metadata for function activation setup.</param>
internal sealed record ExecutionPlan(
    ImmutableArray<ExecutionInstruction> Instructions,
    int EntryPoint,
    int SlotCount = 0,
    ImmutableArray<Symbol> SlotSymbols = default,
    int RootSlotCount = 0,
    ImmutableDictionary<Symbol, int>? RootSlotMap = null,
    ImmutableHashSet<Symbol>? RootLexicalBindings = null,
    ImmutableDictionary<int, ImmutableHashSet<Symbol>>? ScopeLexicalBindings = null,
    int RootScopeId = 0,
    int LayoutId = 0,
    int FlatSlotCount = 0,
    ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>>? FlatSlotMappings = null,
    ActivationSlotShape? ActivationSlots = null,
    CompactStatementStorageBoundary? CompactStatementStorageBoundary = null,
    ImmutableHashSet<Symbol>? RootConstBindings = null,
    ImmutableDictionary<int, ImmutableHashSet<Symbol>>? ScopeConstBindings = null)
{
    public ImmutableDictionary<Symbol, int> SafeRootSlotMap =>
        RootSlotMap ?? ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);

    public ImmutableHashSet<Symbol> SafeRootLexicalBindings =>
        RootLexicalBindings ?? ImmutableHashSet<Symbol>.Empty.WithComparer(ReferenceEqualityComparer<Symbol>.Instance);

    public ImmutableDictionary<int, ImmutableHashSet<Symbol>> SafeScopeLexicalBindings =>
        ScopeLexicalBindings ?? ImmutableDictionary<int, ImmutableHashSet<Symbol>>.Empty;

    public ImmutableHashSet<Symbol> SafeRootConstBindings =>
        RootConstBindings ?? ImmutableHashSet<Symbol>.Empty.WithComparer(ReferenceEqualityComparer<Symbol>.Instance);

    public ImmutableDictionary<int, ImmutableHashSet<Symbol>> SafeScopeConstBindings =>
        ScopeConstBindings ?? ImmutableDictionary<int, ImmutableHashSet<Symbol>>.Empty;

    public bool HasOnlyRootFlatSlotMappings { get; } =
        ComputeHasOnlyRootFlatSlotMappings(RootScopeId, FlatSlotMappings);

    /// <summary>
    ///     True when no captured/dynamic identifier name (a free name the production VM resolves through the
    ///     live environment chain) collides with a lexical binding declared in a CATCH scope or a
    ///     per-iteration LOOP scope of this function.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     Option B (Stage 5) narrowing. The captured-name miscompile (a captured enclosing name stamped to
    ///     a SHADOWING nested-scope flat slot by SlotAssignmentRewriter's unscoped fallback, design §1.3) is
    ///     now fixed AT THE SOURCE for plain lexical { } BLOCK scopes: the rewriter never resolves a captured
    ///     read to an off-stack block scope (it lowers to a dynamic-identifier op walking the env chain). So
    ///     block-shadow collisions route AND compute correctly and no longer need a guard.
    /// </para>
    /// <para>
    ///     The rewriter fix is UNSOUND for two non-block env-bearing scope kinds that never enter the active
    ///     scope stack at their lexical position during rewriting: CATCH bindings and per-iteration LOOP
    ///     bindings (which are legitimately resolved via the same unscoped fallback for their own local
    ///     reads). For those, a captured enclosing read sharing the binding's name is indistinguishable from
    ///     the legitimate local read at the rewriter level (it would require knowledge of the ENCLOSING
    ///     function's scopes, which the inner rewriter does not have). This guard therefore still DECLINES a
    ///     captured-name collision with a catch / per-iteration-loop binding, keeping those shapes on the IR
    ///     runner where they compute correctly.
    /// </para>
    /// <para>
    ///     nonBlockNestedBoundNames := names bound by an <see cref="EnterCatchInstruction" /> (catch scope)
    ///     or carried as <see cref="PushEnvironmentInstruction.PerIterationBindings" /> (per-iteration loop
    ///     scope). Plain block-scope let/const names are deliberately EXCLUDED — the rewriter handles them.
    /// </para>
    /// <para>
    ///     A read of a nonBlockNestedBoundName collides (declines) if EITHER detector fires (union;
    ///     over-decline is safe, under-decline is the only unsound direction):
    ///     - (1) the identifier OPERATION resolves to NEITHER a flat slot (FlatSlotId &gt;= 0) NOR a slot in
    ///       one of this plan's own scopes — i.e. it points at an ENCLOSING scope (or is unresolved) and
    ///       lowers to a dynamic op walking the env chain.
    ///     - (2) the read is STAMPED to one of this plan's own nested scopes S, but a forward MUST-active
    ///       scope dataflow (intersected at CFG joins) shows S is NOT guaranteed active where the read
    ///       occurs — the mis-stamp symptom (design §1.3).
    /// </para>
    /// </remarks>
    public bool HasNoCapturedNameShadowedByNonBlockNestedScope { get; } =
        ComputeHasNoCapturedNameShadowedByNonBlockNestedScope(
            RootScopeId,
            Instructions,
            FlatSlotMappings);

    public bool CanUseRawSyncReturn { get; } = ComputeCanUseRawSyncReturn(Instructions);

    public ExpressionProgram? SimpleReturnProgram { get; } = ComputeSimpleReturnProgram(Instructions, EntryPoint);

    public IrCallShape IrCallShape { get; } = ComputeIrCallShape(Instructions, EntryPoint);

    public SimpleReturnParameterBinaryExpression? SimpleReturnParameterBinary { get; } =
        ComputeSimpleReturnParameterBinary(Instructions, EntryPoint, ActivationSlots);

    public SimpleReturnParameterBinaryChainExpression? SimpleReturnParameterBinaryChain { get; } =
        ComputeSimpleReturnParameterBinaryChain(Instructions, EntryPoint, ActivationSlots);

    public SimpleReturnLiteralExpression? SimpleReturnLiteral { get; } =
        ComputeSimpleReturnLiteral(Instructions, EntryPoint);

    public SimpleReturnParameterExpression? SimpleReturnParameter { get; } =
        ComputeSimpleReturnParameter(Instructions, EntryPoint, ActivationSlots);

    // Mutable plan-level caches — set once after the first eligibility evaluation; never reverted.
    // Thread-safe: writes use volatile semantics; races are benign only for plan-pure facts.

    /// <summary>
    /// Set to true once the plan-level production unified-bytecode eligibility check has found a
    /// structural decline.  Structural declines are purely plan-dependent (not closure- or
    /// descriptor-dependent) and never change after first evaluation, so any subsequent
    /// SyncFunctionInvoker instance for the same FunctionExpression can skip the re-evaluation.
    /// </summary>
    private volatile bool _productionEligibilityPermanentDecline;
    private int _containsOrdinaryDynamicIdentifierDependency;
    private int _containsOnlyImplicitArgumentsObjectDynamicIdentifierDependency;

    internal bool IsProductionEligibilityPermanentDecline => _productionEligibilityPermanentDecline;

    internal void MarkProductionEligibilityPermanentDecline()
    {
        _productionEligibilityPermanentDecline = true;
    }

    internal bool TryGetContainsOrdinaryDynamicIdentifierDependency(out bool value) =>
        TryReadCachedBoolean(ref _containsOrdinaryDynamicIdentifierDependency, out value);

    internal void SetContainsOrdinaryDynamicIdentifierDependency(bool value)
    {
        Volatile.Write(
            ref _containsOrdinaryDynamicIdentifierDependency,
            ToCachedBoolean(value));
    }

    internal bool TryGetContainsOnlyImplicitArgumentsObjectDynamicIdentifierDependency(out bool value) =>
        TryReadCachedBoolean(ref _containsOnlyImplicitArgumentsObjectDynamicIdentifierDependency, out value);

    internal void SetContainsOnlyImplicitArgumentsObjectDynamicIdentifierDependency(bool value)
    {
        Volatile.Write(
            ref _containsOnlyImplicitArgumentsObjectDynamicIdentifierDependency,
            ToCachedBoolean(value));
    }

    private static bool TryReadCachedBoolean(ref int cached, out bool value)
    {
        var state = Volatile.Read(ref cached);
        switch (state)
        {
            case 1:
                value = false;
                return true;

            case 2:
                value = true;
                return true;

            default:
                value = false;
                return false;
        }
    }

    private static int ToCachedBoolean(bool value) => value ? 2 : 1;

    public CompactStatementStorageBoundary CreateCompactStatementStorageBoundary() =>
        CompactStatementStorageBoundary ?? CompactStatementStorage.CreateBoundary(
            Instructions,
            CompactStatementBoundaryMode.PureControlFlow);

    public CompactStatementStorageBoundary CreateDiagnosticCompactStatementStorageBoundary() =>
        CompactStatementStorage.CreateBoundary(Instructions, CompactStatementBoundaryMode.DiagnosticCoverage);

    private static bool ComputeHasOnlyRootFlatSlotMappings(
        int rootScopeId,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>>? flatSlotMappings)
    {
        if (flatSlotMappings is null || flatSlotMappings.Count == 0)
        {
            return true;
        }

        foreach (var mapping in flatSlotMappings)
        {
            if (mapping.Key != rootScopeId)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ComputeHasNoCapturedNameShadowedByNonBlockNestedScope(
        int rootScopeId,
        ImmutableArray<ExecutionInstruction> instructions,
        ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>>? flatSlotMappings)
    {
        if (instructions.IsDefaultOrEmpty)
        {
            return true;
        }

        // nonBlockNestedBoundNames: names bound by a CATCH scope (EnterCatchInstruction.SlotMap) or a
        // per-iteration LOOP scope (PushEnvironmentInstruction carrying PerIterationBindings). Plain { }
        // block-scope let/const names are DELIBERATELY EXCLUDED — SlotAssignmentRewriter now resolves a
        // captured read past such a block correctly (Option B), so block shadows need no guard. Catch /
        // per-iteration scopes are the residual cases the rewriter cannot distinguish from a legitimate
        // local read, so a captured-name collision with one of them must still decline.
        HashSet<string>? nonBlockNestedBoundNames = null;
        var ownScopeIds = new HashSet<int> { rootScopeId };

        foreach (var instruction in instructions)
        {
            switch (instruction)
            {
                case EnterCatchInstruction enterCatch:
                    if (enterCatch.ScopeId >= 0)
                    {
                        ownScopeIds.Add(enterCatch.ScopeId);
                    }

                    foreach (var symbol in enterCatch.SlotMap.Keys)
                    {
                        (nonBlockNestedBoundNames ??= new HashSet<string>(StringComparer.Ordinal))
                            .Add(symbol.Name);
                    }

                    break;

                case PushEnvironmentInstruction push:
                    if (push.ScopeId >= 0)
                    {
                        ownScopeIds.Add(push.ScopeId);
                    }

                    if (!push.PerIterationBindings.IsDefaultOrEmpty)
                    {
                        foreach (var symbol in push.PerIterationBindings)
                        {
                            (nonBlockNestedBoundNames ??= new HashSet<string>(StringComparer.Ordinal))
                                .Add(symbol.Name);
                        }
                    }

                    break;
            }
        }

        if (nonBlockNestedBoundNames is null || nonBlockNestedBoundNames.Count == 0)
        {
            // No catch / per-iteration-loop binding ⇒ no residual shadow site ⇒ no hazard.
            return true;
        }

        if (flatSlotMappings is not null)
        {
            foreach (var scopeId in flatSlotMappings.Keys)
            {
                ownScopeIds.Add(scopeId);
            }
        }

        var nestedBoundNames = nonBlockNestedBoundNames;

        // Per-instruction MUST-active nested scope set: the nested scope ids guaranteed to be on the
        // active scope chain on EVERY control-flow path that reaches the instruction, plus the universe of
        // all nested scope ids that ever push an environment. Used to detect the mis-stamp hazard below.
        var (nestedScopeUniverse, mustActiveScopes) =
            ComputeMustActiveNestedScopes(instructions, rootScopeId);

        // Two collision detectors, unioned (decline if EITHER fires; over-decline is safe):
        //
        // (1) Captured-via-enclosing read of a nested-bound name. An identifier OPERATION resolving to an
        //     enclosing scope (or unresolved) whose name is also a nested binding is the classic dynamic
        //     shadow — the unscoped TryResolve fallback can mis-stamp it.
        //
        // (2) Mis-stamped captured read: a read STAMPED to a nested scope S (the very symptom from
        //     SlotAssignmentRewriter's unscoped fallback — design §1.3) that occurs at an instruction
        //     where S is NOT guaranteed active. A genuine nested local is only read while its scope is
        //     active; a captured enclosing read that got the nested slot is read outside it. The
        //     regression shapes (NestedFunctionScopeRegressionTests) collapse BOTH reads of the shadowed
        //     name to the nested flat slot, so detector (1) alone misses them — detector (2) catches the
        //     outer read because its scope is not active there.
        for (var i = 0; i < instructions.Length; i++)
        {
            var instruction = instructions[i];
            var activeHere = new NestedScopeActivity(nestedScopeUniverse, mustActiveScopes[i]);

            if (InstructionHasShadowedCapture(
                    instruction,
                    nestedBoundNames,
                    ownScopeIds,
                    activeHere))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Bundles the universe of all tracked nested scope ids (every scope id that pushes an environment)
    ///     with the subset that is guaranteed active at a particular instruction.
    /// </summary>
    private readonly struct NestedScopeActivity(ImmutableHashSet<int> universe, ImmutableHashSet<int> active)
    {
        public bool IsTrackedNestedScope(int scopeId) => universe.Contains(scopeId);

        public bool IsActive(int scopeId) => active.Contains(scopeId);
    }

    /// <summary>
    ///     Forward MUST dataflow: returns, per instruction index, the set of nested scope ids that are on
    ///     the active scope chain on EVERY path reaching that instruction, plus the universe of all nested
    ///     scope ids that push an environment. A scope is pushed by a
    ///     <see cref="PushEnvironmentInstruction" /> / <see cref="EnterCatchInstruction" /> and popped by
    ///     the matching <see cref="PopEnvironmentInstruction" /> (break/continue pop to a target scope).
    ///     Intersecting at control-flow joins makes it sound for the "read stamped to a scope that is not
    ///     guaranteed in scope" hazard check.
    /// </summary>
    private static (ImmutableHashSet<int> Universe, ImmutableHashSet<int>[] ActiveIn) ComputeMustActiveNestedScopes(
        ImmutableArray<ExecutionInstruction> instructions,
        int rootScopeId)
    {
        var length = instructions.Length;
        var activeIn = new ImmutableHashSet<int>?[length];
        var empty = ImmutableHashSet<int>.Empty;

        var universeBuilder = ImmutableHashSet.CreateBuilder<int>();
        foreach (var instruction in instructions)
        {
            switch (instruction)
            {
                case PushEnvironmentInstruction push when push.ScopeId != rootScopeId && push.ScopeId >= 0:
                    universeBuilder.Add(push.ScopeId);
                    break;
                case EnterCatchInstruction enterCatch when enterCatch.ScopeId != rootScopeId && enterCatch.ScopeId >= 0:
                    universeBuilder.Add(enterCatch.ScopeId);
                    break;
            }
        }

        var universe = universeBuilder.ToImmutable();

        var worklist = new Queue<int>();
        var queued = new bool[length];

        // Seed every instruction with no predecessors as a CFG entry (active-in = empty). Practically the
        // plan entry plus any unreferenced handler heads; seeding all roots keeps the analysis total.
        var hasPredecessor = new bool[length];
        for (var i = 0; i < length; i++)
        {
            foreach (var successor in instructions[i].GetSuccessors())
            {
                if ((uint)successor < (uint)length)
                {
                    hasPredecessor[successor] = true;
                }
            }
        }

        for (var i = 0; i < length; i++)
        {
            if (!hasPredecessor[i])
            {
                activeIn[i] = empty;
                worklist.Enqueue(i);
                queued[i] = true;
            }
        }

        while (worklist.Count > 0)
        {
            var index = worklist.Dequeue();
            queued[index] = false;

            var inSet = activeIn[index] ?? empty;
            var outSet = ApplyScopeEffect(instructions[index], inSet, rootScopeId);

            foreach (var successor in instructions[index].GetSuccessors())
            {
                if ((uint)successor >= (uint)length)
                {
                    continue;
                }

                var existing = activeIn[successor];
                var merged = existing is null ? outSet : existing.Intersect(outSet);
                if (existing is not null && merged.Count == existing.Count && merged.SetEquals(existing))
                {
                    continue;
                }

                activeIn[successor] = merged;
                if (!queued[successor])
                {
                    worklist.Enqueue(successor);
                    queued[successor] = true;
                }
            }
        }

        var result = new ImmutableHashSet<int>[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = activeIn[i] ?? empty;
        }

        return (universe, result);
    }

    private static ImmutableHashSet<int> ApplyScopeEffect(
        ExecutionInstruction instruction,
        ImmutableHashSet<int> active,
        int rootScopeId)
    {
        switch (instruction)
        {
            case PushEnvironmentInstruction push when push.ScopeId != rootScopeId && push.ScopeId >= 0:
                return active.Add(push.ScopeId);

            case EnterCatchInstruction enterCatch when enterCatch.ScopeId != rootScopeId && enterCatch.ScopeId >= 0:
                return active.Add(enterCatch.ScopeId);

            case PopEnvironmentInstruction pop when pop.ScopeId >= 0:
                return active.Remove(pop.ScopeId);

            case BreakInstruction { TargetScopeId: >= 0 } breakInstruction:
                return PopToTargetScope(active, breakInstruction.TargetScopeId);

            case ContinueInstruction { TargetScopeId: >= 0 } continueInstruction:
                return PopToTargetScope(active, continueInstruction.TargetScopeId);

            default:
                return active;
        }
    }

    private static ImmutableHashSet<int> PopToTargetScope(ImmutableHashSet<int> active, int targetScopeId)
    {
        // break/continue unwind to the target scope: drop any active scope that is not the target. The
        // target itself stays if present (the loop/block being continued). This is a conservative
        // approximation sufficient for the must-active membership test.
        if (active.IsEmpty)
        {
            return active;
        }

        var result = active;
        foreach (var scopeId in active)
        {
            if (scopeId != targetScopeId)
            {
                result = result.Remove(scopeId);
            }
        }

        return result;
    }

    private static bool InstructionHasShadowedCapture(
        ExecutionInstruction instruction,
        HashSet<string> nestedBoundNames,
        HashSet<int> ownScopeIds,
        NestedScopeActivity activeNestedScopes)
    {
        switch (instruction)
        {
            case ThrowInstruction throwInstruction:
                return Program(throwInstruction.ThrowProgram) || Program(throwInstruction.AwaitedProgram);
            case EvaluateAndDiscardInstruction evaluate:
                return Program(evaluate.ExpressionProgram);
            case AwaitAndDiscardInstruction awaitDiscard:
                return Program(awaitDiscard.AwaitedProgram);
            case AssignmentSlotInstruction assign:
                return Program(assign.ValueProgram) || Program(assign.AwaitedProgram);
            case LogicalCompoundAssignmentSlotInstruction logical:
                return Program(logical.RhsProgram) || Program(logical.AwaitedProgram);
            case CompoundAssignmentSlotInstruction compound:
                return Program(compound.RhsProgram) || Program(compound.AwaitedProgram);
            case SimpleVariableDeclarationInstruction simpleDecl:
                return Program(simpleDecl.InitializerProgram) || Program(simpleDecl.AwaitedProgram);
            case BindingVariableDeclarationInstruction bindingDecl:
                return Binding(bindingDecl.TargetProgram) ||
                       Program(bindingDecl.InitializerProgram) ||
                       Program(bindingDecl.AwaitedProgram);
            case YieldInstruction yield:
                return Program(yield.YieldProgram) || Program(yield.AwaitedProgram);
            case YieldStarInstruction yieldStar:
                return Program(yieldStar.IterableProgram) || Program(yieldStar.AwaitedProgram);
            case EnterCatchInstruction { CatchBindingProgram: { } catchBinding }:
                return Binding(catchBinding);
            case IteratorInitInstruction iteratorInit:
                return Program(iteratorInit.IterableProgram) || Program(iteratorInit.AwaitedProgram);
            case ForInInitInstruction forInInit:
                return Program(forInInit.ObjectProgram) || Program(forInInit.AwaitedProgram);
            case BranchInstruction branch:
                return Program(branch.ConditionProgram);
            case ReturnInstruction ret:
                return Program(ret.ReturnProgram) || Program(ret.AwaitedProgram);
            case EnterWithInstruction enterWith:
                return Program(enterWith.ObjectProgram) || Program(enterWith.AwaitedProgram);
            case ArrayDestructuringInitInstruction arrayDestructuringInit:
                return Program(arrayDestructuringInit.SourceProgram);
            case ObjectDestructuringInitInstruction objectDestructuringInit:
                return Program(objectDestructuringInit.SourceProgram);
            default:
                return false;
        }

        bool Program(ExpressionProgram? program) =>
            program is { } value &&
            ExpressionProgramHasShadowedCapture(value, nestedBoundNames, ownScopeIds, activeNestedScopes);

        bool Binding(BindingTargetProgram program) =>
            BindingTargetProgramHasShadowedCapture(program, nestedBoundNames, ownScopeIds, activeNestedScopes);
    }

    private static bool ExpressionProgramHasShadowedCapture(
        ExpressionProgram program,
        HashSet<string> nestedBoundNames,
        HashSet<int> ownScopeIds,
        NestedScopeActivity activeNestedScopes)
    {
        if (program.IsEmpty)
        {
            return false;
        }

        var identifierConstants = program.IdentifierConstants.AsSpan();
        for (var i = 0; i < program.OperationCount; i++)
        {
            var operation = program.GetOperation(i);
            if (!IsIdentifierBearingOp(operation.Kind))
            {
                continue;
            }

            var identifier = operation.GetIdentifier(identifierConstants);
            if (!nestedBoundNames.Contains(identifier.Name.Name))
            {
                continue;
            }

            // Detector (1): a captured (enclosing / unresolved) read whose name shadows a nested binding.
            if (IsCapturedIdentifier(identifier, ownScopeIds))
            {
                return true;
            }

            // Detector (2): a read STAMPED to a nested scope S that is NOT guaranteed active here. A
            // genuine nested local is only read while its scope is active; a captured read mis-stamped to
            // the nested slot (SlotAssignmentRewriter's unscoped fallback) is read outside that scope.
            if (IsNestedSlotReadOutsideScope(identifier, activeNestedScopes))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     An identifier OPERATION reads through the live environment chain (is captured/dynamic) iff it
    ///     resolves to NEITHER a flat slot (FlatSlotId &gt;= 0) NOR a slot in one of THIS plan's own
    ///     scopes (root or any nested scope it owns). An operand pointing at an enclosing scope id (or
    ///     left fully unresolved) is the captured/free case the shadow hazard miscompiles.
    /// </summary>
    private static bool IsCapturedIdentifier(IdentifierOperand identifier, HashSet<int> ownScopeIds)
    {
        if (identifier.FlatSlotId >= 0)
        {
            return false;
        }

        if (identifier.ScopeId >= 0 &&
            identifier.SlotIndex >= 0 &&
            ownScopeIds.Contains(identifier.ScopeId))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    ///     True when <paramref name="identifier" /> is stamped to one of this plan's OWN nested scopes
    ///     (any scope tracked in <paramref name="activeNestedScopes" />'s universe, i.e. a non-root scope
    ///     that pushes an environment) but that scope is NOT guaranteed active where the read occurs — the
    ///     symptom of a captured enclosing read mis-stamped to a shadowing nested local's slot. Root-scope
    ///     and enclosing reads are excluded: they are never tracked as nested active scopes, and a nested
    ///     scope id is recognised only because it appears as a push target in the active-set universe.
    /// </summary>
    private static bool IsNestedSlotReadOutsideScope(
        IdentifierOperand identifier,
        NestedScopeActivity activeNestedScopes)
    {
        var scopeId = identifier.ScopeId;
        if (scopeId < 0 || !activeNestedScopes.IsTrackedNestedScope(scopeId))
        {
            // Root, enclosing, or unresolved: handled by detector (1); not a tracked nested-slot read.
            return false;
        }

        // A read stamped to a nested OWN scope is safe only while that scope is guaranteed active.
        return !activeNestedScopes.IsActive(scopeId);
    }

    private static bool IsIdentifierBearingOp(ExpressionOpKind kind) =>
        kind is
            ExpressionOpKind.LoadIdentifier or
            ExpressionOpKind.LoadIdentifierCallTarget or
            ExpressionOpKind.ResolveIdentifierReference or
            ExpressionOpKind.StoreResolvedIdentifier or
            ExpressionOpKind.StoreIdentifier or
            ExpressionOpKind.UpdateIdentifier or
            ExpressionOpKind.TypeOfIdentifier or
            ExpressionOpKind.DeleteIdentifier;

    private static bool BindingTargetProgramHasShadowedCapture(
        BindingTargetProgram program,
        HashSet<string> nestedBoundNames,
        HashSet<int> ownScopeIds,
        NestedScopeActivity activeNestedScopes)
    {
        switch (program)
        {
            case ArrayBindingTargetProgram arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    if (element.Target is not null &&
                        BindingTargetProgramHasShadowedCapture(element.Target, nestedBoundNames, ownScopeIds, activeNestedScopes))
                    {
                        return true;
                    }

                    if (element.DefaultProgram is { } defaultProgram &&
                        ExpressionProgramHasShadowedCapture(defaultProgram, nestedBoundNames, ownScopeIds, activeNestedScopes))
                    {
                        return true;
                    }
                }

                return arrayBinding.RestElement is { } arrayRest &&
                       BindingTargetProgramHasShadowedCapture(arrayRest, nestedBoundNames, ownScopeIds, activeNestedScopes);

            case ObjectBindingTargetProgram objectBinding:
                foreach (var property in objectBinding.Properties)
                {
                    if (BindingTargetProgramHasShadowedCapture(property.Target, nestedBoundNames, ownScopeIds, activeNestedScopes))
                    {
                        return true;
                    }

                    if (property.DefaultProgram is { } objDefault &&
                        ExpressionProgramHasShadowedCapture(objDefault, nestedBoundNames, ownScopeIds, activeNestedScopes))
                    {
                        return true;
                    }

                    if (property.NameProgram is { } nameProgram &&
                        ExpressionProgramHasShadowedCapture(nameProgram, nestedBoundNames, ownScopeIds, activeNestedScopes))
                    {
                        return true;
                    }
                }

                return objectBinding.RestElement is { } objectRest &&
                       BindingTargetProgramHasShadowedCapture(objectRest, nestedBoundNames, ownScopeIds, activeNestedScopes);

            case NamedPropertyAssignmentBindingTargetProgram namedAssignment:
                return ExpressionProgramHasShadowedCapture(namedAssignment.TargetProgram, nestedBoundNames, ownScopeIds, activeNestedScopes);

            case ComputedPropertyAssignmentBindingTargetProgram computedAssignment:
                return ExpressionProgramHasShadowedCapture(computedAssignment.TargetProgram, nestedBoundNames, ownScopeIds, activeNestedScopes) ||
                       ExpressionProgramHasShadowedCapture(computedAssignment.PropertyProgram, nestedBoundNames, ownScopeIds, activeNestedScopes);

            case ComputedSuperPropertyAssignmentBindingTargetProgram computedSuperAssignment:
                return ExpressionProgramHasShadowedCapture(computedSuperAssignment.PropertyProgram, nestedBoundNames, ownScopeIds, activeNestedScopes);

            default:
                // IdentifierBindingTargetProgram / NamedSuperPropertyAssignmentBindingTargetProgram:
                // no embedded expression program that could carry a captured read.
                return false;
        }
    }

    private static bool ComputeCanUseRawSyncReturn(ImmutableArray<ExecutionInstruction> instructions)
    {
        if (instructions.IsDefaultOrEmpty)
        {
            return true;
        }

        foreach (var instruction in instructions)
        {
            if (BlocksRawSyncReturn(instruction))
            {
                return false;
            }
        }

        return true;
    }

    private static ExpressionProgram? ComputeSimpleReturnProgram(
        ImmutableArray<ExecutionInstruction> instructions,
        int entryPoint)
    {
        if ((uint)entryPoint >= (uint)instructions.Length ||
            !ComputeCanUseRawSyncReturn(instructions))
        {
            return null;
        }

        return instructions[entryPoint] is ReturnInstruction
        {
            ReturnProgram: { } returnProgram,
            AwaitedProgram: null
        }
            ? returnProgram
            : null;
    }

    private static IrCallShape ComputeIrCallShape(
        ImmutableArray<ExecutionInstruction> instructions,
        int entryPoint) =>
        ComputeSimpleReturnProgram(instructions, entryPoint) is not null
            ? IrCallShape.SimpleReturnExpression
            : IrCallShape.None;

    private static SimpleReturnParameterBinaryExpression? ComputeSimpleReturnParameterBinary(
        ImmutableArray<ExecutionInstruction> instructions,
        int entryPoint,
        ActivationSlotShape? activationSlots)
    {
        if (activationSlots is null ||
            ComputeSimpleReturnProgram(instructions, entryPoint) is not { } program ||
            program.OperationCount != 3)
        {
            return null;
        }

        var leftLoad = program.GetOperation(0);
        var rightLoad = program.GetOperation(1);
        var binary = program.GetOperation(2);
        if (leftLoad.Kind != ExpressionOpKind.LoadIdentifier ||
            rightLoad.Kind != ExpressionOpKind.LoadIdentifier ||
            leftLoad.IsArguments ||
            rightLoad.IsArguments ||
            binary.Kind != ExpressionOpKind.Binary ||
            !IsSupportedSimpleParameterBinaryOperator(binary.Operator))
        {
            return null;
        }

        var identifiers = program.IdentifierConstants.AsSpan();
        var leftParameterIndex = ResolveParameterSlotIndex(
            leftLoad.GetIdentifier(identifiers),
            activationSlots);
        var rightParameterIndex = ResolveParameterSlotIndex(
            rightLoad.GetIdentifier(identifiers),
            activationSlots);
        if (leftParameterIndex < 0 || rightParameterIndex < 0)
        {
            return null;
        }

        return new SimpleReturnParameterBinaryExpression(
            binary.Operator,
            leftParameterIndex,
            rightParameterIndex);
    }

    private static SimpleReturnParameterBinaryChainExpression? ComputeSimpleReturnParameterBinaryChain(
        ImmutableArray<ExecutionInstruction> instructions,
        int entryPoint,
        ActivationSlotShape? activationSlots)
    {
        if (activationSlots is null ||
            ComputeSimpleReturnProgram(instructions, entryPoint) is not { } program ||
            program.OperationCount != 5)
        {
            return null;
        }

        var leftLoad = program.GetOperation(0);
        var rightLoad = program.GetOperation(1);
        var firstBinary = program.GetOperation(2);
        var thirdLoad = program.GetOperation(3);
        var secondBinary = program.GetOperation(4);
        if (firstBinary.Kind != ExpressionOpKind.Binary ||
            secondBinary.Kind != ExpressionOpKind.Binary ||
            !IsSupportedSimpleParameterBinaryChainOperator(firstBinary.Operator) ||
            !IsSupportedSimpleParameterBinaryChainOperator(secondBinary.Operator) ||
            !TryResolveParameterLoad(leftLoad, program, activationSlots, out var leftParameterIndex) ||
            !TryResolveParameterLoad(rightLoad, program, activationSlots, out var rightParameterIndex) ||
            !TryResolveParameterLoad(thirdLoad, program, activationSlots, out var thirdParameterIndex))
        {
            return null;
        }

        return new SimpleReturnParameterBinaryChainExpression(
            firstBinary.Operator,
            leftParameterIndex,
            rightParameterIndex,
            secondBinary.Operator,
            thirdParameterIndex);
    }

    private static SimpleReturnLiteralExpression? ComputeSimpleReturnLiteral(
        ImmutableArray<ExecutionInstruction> instructions,
        int entryPoint)
    {
        if (ComputeSimpleReturnProgram(instructions, entryPoint) is not { OperationCount: 1 } program)
        {
            return null;
        }

        var operation = program.GetOperation(0);
        return operation.Kind == ExpressionOpKind.LoadLiteral
            ? new SimpleReturnLiteralExpression(operation.GetLiteral(program.LiteralConstants.AsSpan()))
            : null;
    }

    private static SimpleReturnParameterExpression? ComputeSimpleReturnParameter(
        ImmutableArray<ExecutionInstruction> instructions,
        int entryPoint,
        ActivationSlotShape? activationSlots)
    {
        if (activationSlots is null ||
            ComputeSimpleReturnProgram(instructions, entryPoint) is not { OperationCount: 1 } program)
        {
            return null;
        }

        var operation = program.GetOperation(0);
        if (operation.Kind != ExpressionOpKind.LoadIdentifier || operation.IsArguments)
        {
            return null;
        }

        var parameterIndex = ResolveParameterSlotIndex(
            operation.GetIdentifier(program.IdentifierConstants.AsSpan()),
            activationSlots);
        return parameterIndex >= 0
            ? new SimpleReturnParameterExpression(parameterIndex)
            : null;
    }

    private static bool IsSupportedSimpleParameterBinaryOperator(BinaryOperator op) =>
        op is BinaryOperator.Add or
            BinaryOperator.Subtract or
            BinaryOperator.Multiply or
            BinaryOperator.Divide;

    private static bool IsSupportedSimpleParameterBinaryChainOperator(BinaryOperator op) =>
        op is BinaryOperator.Add or
            BinaryOperator.Subtract or
            BinaryOperator.Multiply or
            BinaryOperator.Divide or
            BinaryOperator.BitwiseXor;

    private static bool TryResolveParameterLoad(
        PackedExpressionOp operation,
        ExpressionProgram program,
        ActivationSlotShape activationSlots,
        out int parameterIndex)
    {
        parameterIndex = -1;
        if (operation.Kind != ExpressionOpKind.LoadIdentifier || operation.IsArguments)
        {
            return false;
        }

        parameterIndex = ResolveParameterSlotIndex(
            operation.GetIdentifier(program.IdentifierConstants.AsSpan()),
            activationSlots);
        return parameterIndex >= 0;
    }

    private static int ResolveParameterSlotIndex(
        IdentifierOperand identifier,
        ActivationSlotShape activationSlots)
    {
        if (identifier.ScopeId != activationSlots.ScopeId ||
            identifier.SlotIndex < 0 ||
            activationSlots.ParameterSlotIndices.IsDefault)
        {
            return -1;
        }

        var parameterIndex = -1;
        var parameterSlotIndices = activationSlots.ParameterSlotIndices;
        for (var i = 0; i < parameterSlotIndices.Length; i++)
        {
            if (parameterSlotIndices[i] == identifier.SlotIndex)
            {
                parameterIndex = i;
            }
        }

        return parameterIndex;
    }

    private static bool BlocksRawSyncReturn(ExecutionInstruction instruction) =>
        instruction.Kind is
            InstructionKind.AwaitAndDiscard or
            InstructionKind.Yield or
            InstructionKind.YieldStar or
            InstructionKind.EnterTry or
            InstructionKind.EnterCatch or
            InstructionKind.LeaveTry or
            InstructionKind.EndFinally or
            InstructionKind.IteratorInit or
            InstructionKind.IteratorMoveNext or
            InstructionKind.IteratorClose or
            InstructionKind.ForInInit or
            InstructionKind.ForInMoveNext or
            InstructionKind.ArrayDestructuringInit or
            InstructionKind.ArrayDestructuringElement or
            InstructionKind.ArrayDestructuringRest or
            InstructionKind.ArrayDestructuringClose ||
        MayCreateActiveIteratorState(instruction);

    private static bool MayCreateActiveIteratorState(ExecutionInstruction instruction)
    {
        if (instruction.Kind is
            InstructionKind.IteratorInit or
            InstructionKind.IteratorMoveNext or
            InstructionKind.IteratorClose or
            InstructionKind.ForInInit or
            InstructionKind.ForInMoveNext or
            InstructionKind.ArrayDestructuringInit or
            InstructionKind.ArrayDestructuringElement or
            InstructionKind.ArrayDestructuringRest or
            InstructionKind.ArrayDestructuringClose or
            InstructionKind.BindingVariableDeclaration)
        {
            return true;
        }

        return instruction switch
        {
            ThrowInstruction { ThrowProgram: { } program } => ContainsBindingTarget(program),
            EvaluateAndDiscardInstruction { ExpressionProgram: var program } => ContainsBindingTarget(program),
            AwaitAndDiscardInstruction { AwaitedProgram: var program } => ContainsBindingTarget(program),
            AssignmentSlotInstruction { ValueProgram: { } program } => ContainsBindingTarget(program),
            AssignmentSlotInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            LogicalCompoundAssignmentSlotInstruction { RhsProgram: { } program } => ContainsBindingTarget(program),
            LogicalCompoundAssignmentSlotInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            CompoundAssignmentSlotInstruction { RhsProgram: { } program } => ContainsBindingTarget(program),
            CompoundAssignmentSlotInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            SimpleVariableDeclarationInstruction { InitializerProgram: { } program } => ContainsBindingTarget(program),
            SimpleVariableDeclarationInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            BranchInstruction { ConditionProgram: var program } => ContainsBindingTarget(program),
            ReturnInstruction { ReturnProgram: { } program } => ContainsBindingTarget(program),
            ReturnInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            EnterWithInstruction { ObjectProgram: { } program } => ContainsBindingTarget(program),
            EnterWithInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            ForInInitInstruction { ObjectProgram: { } program } => ContainsBindingTarget(program),
            ForInInitInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            IteratorInitInstruction { IterableProgram: { } program } => ContainsBindingTarget(program),
            IteratorInitInstruction { AwaitedProgram: { } program } => ContainsBindingTarget(program),
            _ => false
        };
    }

    private static bool ContainsBindingTarget(ExpressionProgram program)
    {
        foreach (var operation in program.EnumerateOperations())
        {
            if (operation.Kind == ExpressionOpKind.ApplyBindingTarget)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record ActivationSlotShape(
    int ScopeId,
    int SlotCount,
    int LayoutId,
    ImmutableDictionary<Symbol, int> SlotMap,
    ImmutableArray<(Symbol Name, int SlotIndex)> SlotNames,
    ImmutableArray<int> ParameterSlotIndices,
    ImmutableArray<int> LexicalSlotIndices,
    ImmutableHashSet<Symbol> MaterializedBindingNames,
    ImmutableArray<int> ConstLexicalSlotIndices = default);

internal readonly record struct SimpleReturnParameterBinaryExpression(
    BinaryOperator Operator,
    int LeftParameterIndex,
    int RightParameterIndex);

internal readonly record struct SimpleReturnParameterBinaryChainExpression(
    BinaryOperator FirstOperator,
    int LeftParameterIndex,
    int RightParameterIndex,
    BinaryOperator SecondOperator,
    int ThirdParameterIndex);

internal readonly record struct SimpleReturnLiteralExpression(JsValue Value);

internal readonly record struct SimpleReturnParameterExpression(int ParameterIndex);

internal enum IrCallShape
{
    None,
    SimpleReturnExpression
}
