#region

using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// <para>
///     Builds execution plans (IR) for all function types, executed by different invokers:
///     - Synchronous functions (SyncFunctionInvoker) when JsEngineConstants.SyncIrLoops = true
///     - Synchronous generators (SyncGeneratorInvoker)
///     - Async functions (AsyncFunctionInvoker)
///     - Async generators (AsyncGeneratorInvoker, AsyncGeneratorFunctionInvoker)
/// </para>
/// <para>
///     The builder supports linear statement lists, blocks, expression statements, variable declarations,
///     returns, yield/yield* expressions, and control flow (if/loops/try-catch).
///     More complex constructs are detected and reported as unsupported so the engine can fall back to
///     the legacy AST-walking evaluator.
/// </para>
/// </summary>
internal sealed partial class ExecutionPlanBuilder
{
    private const string ResumeSlotPrefix = "\u0001_resume";
    private const string CatchSlotPrefix = "\u0001_catch";
    private const string YieldStarStatePrefix = "\u0001_yieldstar";
    private const string WithScopeSlotPrefix = "\u0001_with";
    private readonly Stack<LoopScope> _loopScopes = new();
    private readonly List<Symbol> _slotSymbols = [];
    private int _catchSlotCounter;
    private ExecutionPlanFailureCode? _failureCode;
    private string? _failureReason;
    private int _analysisRootScopeId;
    private Dictionary<int, ImmutableHashSet<Symbol>> _lexicalBindings = new();
    private int _resumeSlotCounter;
    private int _scopeIdCounter = 1; // Start at 1; root scope id is handled separately and remapped later
    private int _rootScopeId;
    private int _withScopeSlotCounter;
    private int _yieldStarStateCounter;
    private readonly Dictionary<ForEachStatement, IteratorDriverPlan> _iteratorPlanOverrides =
        new(ReferenceEqualityComparer<ForEachStatement>.Instance);

    private ExecutionPlanBuilder()
    {
    }

    /// <summary>
    /// Whether this plan is being built for a top-level script (not a function body).
    /// Script-level var declarations must update the global object.
    /// </summary>
    internal bool IsScriptLevel { get; private set; }

    /// <summary>
    /// Allocates a new slot index for a generator-internal symbol.
    /// </summary>
    internal int AllocateSlot(Symbol symbol)
    {
        var index = _slotSymbols.Count;
        _slotSymbols.Add(symbol);
        return index;
    }

    /// <summary>
    /// Allocates a new scope ID for dynamic scopes (catch blocks, etc.).
    /// </summary>
    internal int AllocateScopeId()
    {
        return _scopeIdCounter++;
    }

    /// <summary>
    /// Builds an execution plan for a function expression.
    /// </summary>
    /// <param name="function">The function to build a plan for.</param>
    /// <param name="plan">The resulting execution plan.</param>
    /// <param name="failureReason">If building fails, the reason why.</param>
    /// <param name="reportDiagnostics">Whether to report diagnostics for test tracking.</param>
    /// <param name="isScriptLevel">
    ///     When true, indicates this is a top-level script (not a function body).
    ///     Script-level var declarations must update the global object.
    /// </param>
    public static ExecutionPlanBuildResult Build(FunctionExpression function, bool reportDiagnostics = true,
        bool isScriptLevel = false)
    {
        // First run the yield-lowering pre-pass so that ExecutionPlanBuilder
        // can assume a simplified, pauseable-function-friendly AST. The lowerer currently acts
        // as a no-op scaffold; yield normalization logic will be migrated here
        // incrementally.
        if (!GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var lowerFailure))
        {
            var failure = ExecutionPlanBuildResult.FailureResult(
                ExecutionPlanFailureCode.YieldLoweringFailed,
                lowerFailure ?? "Failed to lower function to generator-friendly IR.");

            if (reportDiagnostics)
            {
                ExecutionPlanDiagnostics.ReportResult(function, failure);
            }

            return failure;
        }

        var builder = new ExecutionPlanBuilder { IsScriptLevel = isScriptLevel };
        var succeeded = builder.TryBuildInternal(lowered, out var plan);
        var result = succeeded
            ? ExecutionPlanBuildResult.Success(plan)
            : ExecutionPlanBuildResult.FailureResult(
                builder._failureCode ?? ExecutionPlanFailureCode.UnsupportedConstruct,
                builder._failureReason ?? lowerFailure ?? "Function contains unsupported construct for execution plan.");

        if (reportDiagnostics)
        {
            ExecutionPlanDiagnostics.ReportResult(function, result);
        }

        return result;
    }

    public static bool TryBuild(FunctionExpression function, out ExecutionPlan plan, out string? failureReason,
        bool reportDiagnostics = true, bool isScriptLevel = false)
    {
        var result = Build(function, reportDiagnostics, isScriptLevel);
        plan = result.Plan!;
        failureReason = result.FailureReason;
        return result.Succeeded;
    }

    private bool TryBuildInternal(FunctionExpression function, out ExecutionPlan plan)
    {
        // Ensure the root scope id is a stable, positive value. Scope analysis should
        // stamp functions with a non-negative ScopeId, but if it's missing or 0, allocate
        // a synthetic id so downstream slot layout and logging remain consistent.
        // Prefer the function's stamped ScopeId; otherwise allocate a unique synthetic id so
        // each plan has a distinct root scope identity for logging and slot reuse.
        _rootScopeId = function.ScopeId > 0 ? function.ScopeId : SyntheticScopeIdAllocator.NextFunctionRoot();
        // Scope analysis uses the function's declared ScopeId when available, otherwise 0 for legacy stamping.
        var analysisRootScopeId = function.ScopeId >= 0 ? function.ScopeId : 0;
        _analysisRootScopeId = analysisRootScopeId;
        // Always append an implicit "return undefined" instruction. Statement lists fall through to this index.
        var implicitReturnIndex = Append(new ReturnInstruction(-1, null));
        if (!TryBuildStatementList(function.Body.Statements, implicitReturnIndex, out var entryIndex))
        {
            plan = default!;
            _failureReason ??= "Statement list contains unsupported construct.";
            return false;
        }

        // After building all instructions, assign slots to user variables and update AST nodes.
        //
        // NOTE: For scripts (IsScriptLevel=true), we do NOT assign slots to user variables because:
        // 1. Script hoisting already created dictionary-based bindings for var/let/const declarations
        // 2. Scripts may contain 'with' statements that require dynamic identifier resolution
        // 3. Slot-based lookup would bypass the with-scope, breaking 'with' semantics
        //
        // NOTE: For functions that contain dynamic scope features (with/direct eval), we also skip slot assignment
        // because:
        // 1. Direct eval can introduce new bindings at runtime (invalidating fixed slot layouts)
        // 2. Slot/flat-slot reads bypass object-environment resolution needed for with semantics
        // These functions run via dictionary lookups (AllowIdentifierCache=false).
        ScopeSlotAnalysis? analysis = null;
        SlotAssignmentRewriter? rewriter = null;
        if (!IsScriptLevel && TypedAstEvaluator.AllowsIdentifierCaching(function))
        {
            analysis = AssignSlotsToUserVariables(entryIndex, function, _rootScopeId, analysisRootScopeId,
                out rewriter);
        }

        LowerExpressionPayloads();

        var rootSlotCount = analysis is not null && analysis.Scopes.TryGetValue(analysisRootScopeId, out var rootInfo)
            ? rootInfo.SlotCount
            : 0;
        var rootSlotMap = analysis is not null && analysis.ImmutableSlotMaps.TryGetValue(analysisRootScopeId, out var rootMap)
            ? rootMap
            : ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);
        var mappedRootScopeId = rewriter?.MapScopeId(analysisRootScopeId) ?? _rootScopeId;
        var rootLexicalBindings = analysis is not null && _lexicalBindings.TryGetValue(mappedRootScopeId, out var rootLex)
            ? rootLex
            : ImmutableHashSet<Symbol>.Empty.WithComparer(ReferenceEqualityComparer<Symbol>.Instance);
        var slotSymbols = _slotSymbols.ToImmutableArray();
        var layoutId = ComputeLayoutId(rootSlotCount, rootSlotMap, slotSymbols);
        var flatSlotCount = rewriter?.FlatSlotCount ?? 0;
        var flatSlotMappings = rewriter?.BuildFlatSlotMappings();

        // Post-process: stamp FlatSlotMappings on PushEnvironmentInstructions for O(1) access at runtime
        // Also clear stale mappings when flat slots are not used (instructions can be reused across builds).
        for (var i = 0; i < Instructions.Count; i++)
        {
            if (Instructions[i] is not PushEnvironmentInstruction push)
            {
                continue;
            }

            var updatedPush = push;
            var changed = false;
            if (flatSlotMappings is { Count: > 0 } &&
                flatSlotMappings.TryGetValue(updatedPush.ScopeId, out var scopeMappings))
            {
                updatedPush = updatedPush with { FlatSlotMappings = scopeMappings };
                changed = true;
            }
            else if (!updatedPush.FlatSlotMappings.IsDefaultOrEmpty)
            {
                updatedPush = updatedPush with { FlatSlotMappings = default };
                changed = true;
            }

            if (updatedPush.SourceBlock is not null)
            {
                updatedPush = updatedPush with { SourceBlock = null };
                changed = true;
            }

            if (changed)
            {
                Instructions[i] = updatedPush;
            }
        }

        plan = new ExecutionPlan(
            [.. Instructions],
            entryIndex,
            _slotSymbols.Count,
            slotSymbols,
            rootSlotCount,
            rootSlotMap,
            rootLexicalBindings,
            _lexicalBindings.ToImmutableDictionary(kv => kv.Key, kv => kv.Value,
                EqualityComparer<int>.Default),
            RootScopeId: mappedRootScopeId,
            layoutId,
            flatSlotCount,
            flatSlotMappings);
        return true;
    }

    private void LowerExpressionPayloads()
    {
        for (var i = 0; i < Instructions.Count; i++)
        {
            switch (Instructions[i])
            {
                case EvaluateAndDiscardInstruction { Expression: not null, ExpressionOps: null } evaluateInstruction:
                    if (!TryCompileExpressionProgram(evaluateInstruction.Expression, out var evaluateProgram))
                    {
                        break;
                    }

                    Instructions[i] = evaluateInstruction with
                    {
                        Expression = null,
                        ExpressionOps = evaluateProgram.Operations
                    };
                    break;

                case AssignmentSlotInstruction { ValueExpression: not null, ValueProgram: null } assignmentInstruction:
                    if (!TryCompileExpressionProgram(assignmentInstruction.ValueExpression, out var assignmentProgram))
                    {
                        break;
                    }

                    Instructions[i] = assignmentInstruction with
                    {
                        ValueExpression = null,
                        ValueProgram = assignmentProgram
                    };
                    break;

                case LogicalCompoundAssignmentSlotInstruction { RhsExpression: not null, RhsProgram: null } logicalInstruction:
                    if (!TryCompileExpressionProgram(logicalInstruction.RhsExpression, out var logicalProgram))
                    {
                        break;
                    }

                    Instructions[i] = logicalInstruction with
                    {
                        RhsExpression = null,
                        RhsProgram = logicalProgram
                    };
                    break;

                case CompoundAssignmentSlotInstruction { RhsExpression: not null, RhsExpressionOps: null } compoundInstruction:
                    if (!TryCompileExpressionProgram(compoundInstruction.RhsExpression, out var compoundProgram))
                    {
                        break;
                    }

                    Instructions[i] = compoundInstruction with
                    {
                        RhsExpression = null,
                        RhsExpressionOps = compoundProgram.Operations
                    };
                    break;

                case ThrowInstruction { Expression: not null, ThrowProgram: null } throwInstruction:
                    if (!TryCompileExpressionProgram(throwInstruction.Expression, out var throwProgram))
                    {
                        break;
                    }

                    Instructions[i] = throwInstruction with
                    {
                        Expression = null,
                        ThrowProgram = throwProgram
                    };
                    break;

                case ReturnInstruction { ReturnExpression: not null, ReturnProgram: null } returnInstruction:
                    if (!TryCompileExpressionProgram(returnInstruction.ReturnExpression, out var returnProgram))
                    {
                        break;
                    }

                    Instructions[i] = returnInstruction with
                    {
                        ReturnExpression = null,
                        ReturnProgram = returnProgram
                    };
                    break;

                case BranchInstruction { Condition: not null, ConditionProgram: null } branchInstruction:
                    if (!TryCompileExpressionProgram(branchInstruction.Condition, out var conditionProgram))
                    {
                        break;
                    }

                    Instructions[i] = branchInstruction with
                    {
                        Condition = null,
                        ConditionProgram = conditionProgram
                    };
                    break;

                case SimpleVariableDeclarationInstruction { Initializer: not null, InitializerProgram: null } variableInstruction:
                    if (!TryCompileExpressionProgram(variableInstruction.Initializer, out var initializerProgram))
                    {
                        break;
                    }

                    Instructions[i] = variableInstruction with
                    {
                        Initializer = null,
                        InitializerProgram = initializerProgram
                    };
                    break;

                case YieldInstruction { YieldExpression: not null, YieldProgram: null } yieldInstruction:
                    if (!TryCompileExpressionProgram(yieldInstruction.YieldExpression, out var yieldProgram))
                    {
                        break;
                    }

                    Instructions[i] = yieldInstruction with
                    {
                        YieldExpression = null,
                        YieldProgram = yieldProgram
                    };
                    break;

                case YieldStarInstruction { IterableExpression: not null, IterableProgram: null } yieldStarInstruction:
                    if (!TryCompileExpressionProgram(yieldStarInstruction.IterableExpression, out var iterableProgram))
                    {
                        break;
                    }

                    Instructions[i] = yieldStarInstruction with
                    {
                        IterableExpression = null,
                        IterableProgram = iterableProgram
                    };
                    break;

                case IteratorInitInstruction { IterableExpression: not null, IterableExpressionOps: null } iteratorInitInstruction:
                    if (!TryCompileExpressionProgram(iteratorInitInstruction.IterableExpression, out var iteratorProgram))
                    {
                        break;
                    }

                    Instructions[i] = iteratorInitInstruction with
                    {
                        IterableExpression = null,
                        IterableExpressionOps = iteratorProgram.Operations,
                        IterableSource = iteratorInitInstruction.IterableSource ?? iteratorInitInstruction.IterableExpression.Source
                    };
                    break;

                case ForInInitInstruction { ObjectExpression: not null, ObjectProgram: null } forInInitInstruction:
                    if (!TryCompileExpressionProgram(forInInitInstruction.ObjectExpression, out var objectProgram))
                    {
                        break;
                    }

                    Instructions[i] = forInInitInstruction with
                    {
                        ObjectExpression = null,
                        ObjectProgram = objectProgram,
                        ObjectSource = forInInitInstruction.ObjectSource ?? forInInitInstruction.ObjectExpression.Source
                    };
                    break;

                case EnterWithInstruction { ObjectExpression: not null, ObjectProgram: null } enterWithInstruction:
                    if (!TryCompileExpressionProgram(enterWithInstruction.ObjectExpression, out var withObjectProgram))
                    {
                        break;
                    }

                    Instructions[i] = enterWithInstruction with
                    {
                        ObjectExpression = null,
                        ObjectProgram = withObjectProgram,
                        ObjectSource = enterWithInstruction.ObjectSource ?? enterWithInstruction.ObjectExpression.Source
                    };
                    break;

                case ArrayDestructuringInitInstruction { SourceExpression: not null, SourceProgram: null } arrayDestructuringInitInstruction:
                    if (!TryCompileExpressionProgram(arrayDestructuringInitInstruction.SourceExpression, out var destructuringSourceProgram))
                    {
                        break;
                    }

                    Instructions[i] = arrayDestructuringInitInstruction with
                    {
                        SourceExpression = null,
                        SourceProgram = destructuringSourceProgram
                    };
                    break;

                case BindingVariableDeclarationInstruction bindingInstruction:
                {
                    var updatedBindingInstruction = bindingInstruction;
                    var changed = false;

                    if (updatedBindingInstruction.Initializer is not null &&
                        updatedBindingInstruction.InitializerProgram is null &&
                        TryCompileExpressionProgram(updatedBindingInstruction.Initializer, out var bindingInitializerProgram))
                    {
                        updatedBindingInstruction = updatedBindingInstruction with
                        {
                            Initializer = null,
                            InitializerProgram = bindingInitializerProgram
                        };
                        changed = true;
                    }

                    if (updatedBindingInstruction.Target is not null &&
                        updatedBindingInstruction.TargetProgram is null &&
                        BindingTargetProgramCompiler.TryCompile(updatedBindingInstruction.Target, out var targetProgram, out _))
                    {
                        updatedBindingInstruction = updatedBindingInstruction with
                        {
                            Target = null,
                            TargetProgram = targetProgram
                        };
                        changed = true;
                    }

                    if (changed)
                    {
                        Instructions[i] = updatedBindingInstruction;
                    }

                    break;
                }
            }
        }

        bool TryCompileExpressionProgram(ExpressionNode expression, out ExpressionProgram program)
        {
            return ExpressionProgramCompiler.TryCompile(expression, out program, out _);
        }
    }

    /// <summary>
    /// Collects all user variable identifiers from instructions, assigns them slots,
    /// and updates the AST nodes with scope-aware slot metadata.
    /// </summary>
    private ScopeSlotAnalysis AssignSlotsToUserVariables(
        int entryIndex,
        FunctionExpression function,
        int targetRootScopeId,
        int analysisRootScopeId,
        out SlotAssignmentRewriter rewriter)
    {
        var parameterNames = new List<Symbol>();
        function.CollectParameterNamesFromFunction(parameterNames);
        var hoistedFunctions = CollectHoistedFunctionSymbols(function.Body);

        // Per Annex B.3.3.1/B.3.3.2: filter out block-scoped function names that conflict
        // with parameter names (BoundNames of argumentsList), body-level lexical names,
        // or non-simple catch parameters (B.3.5). These should NOT get var-scoped slots.
        if (!function.Body.IsStrict && hoistedFunctions.Count > 0)
        {
            var hoistPlan = ((IAstCacheable<HoistPlan>)function.Body).GetOrCreateCache();
            var bodyLexNames = hoistPlan.LexicalNames;
            var simpleCatchNames = hoistPlan.SimpleCatchParameterNames;
            var catchNames = hoistPlan.CatchParameterNames;
            var blocked = new HashSet<Symbol>(bodyLexNames, ReferenceEqualityComparer<Symbol>.Instance);
            blocked.ExceptWith(simpleCatchNames);
            foreach (var pn in parameterNames)
            {
                blocked.Add(pn);
            }

            // B.3.5: non-simple catch parameters (destructured) block var hoisting
            foreach (var cn in catchNames)
            {
                if (!simpleCatchNames.Contains(cn))
                {
                    blocked.Add(cn);
                }
            }

            // Per spec FunctionDeclarationInstantiation step 22.f: when argumentsObjectNeeded
            // is true, "arguments" is appended to parameterNames and blocks AnnexB hoisting.
            if (!function.IsArrow)
            {
                var argumentsIsParam = parameterNames.Contains(Symbol.Arguments);
                var argumentsInBodyLex = bodyLexNames.Contains(Symbol.Arguments) &&
                                         !simpleCatchNames.Contains(Symbol.Arguments);
                var hasParamExpressions = function.Parameters.Any(p =>
                    p.DefaultValue is not null || p.Pattern is not null);
                var canSkipForBodyDecl = !hasParamExpressions && argumentsInBodyLex;
                var argumentsObjectNeeded = !argumentsIsParam && !canSkipForBodyDecl;
                if (argumentsObjectNeeded)
                {
                    blocked.Add(Symbol.Arguments);
                }
            }

            if (blocked.Count > 0)
            {
                hoistedFunctions.RemoveAll(name => blocked.Contains(name));
            }
        }

        var seedSlots = new List<Symbol>(_slotSymbols.Count + hoistedFunctions.Count + parameterNames.Count);
        var seen = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);

        AppendIfMissing(_slotSymbols);
        AppendIfMissing(hoistedFunctions);
        AppendIfMissing(parameterNames);

        // Keep backing slot list aligned with the ordered seeds so future allocations
        // get non-conflicting indices.
        _slotSymbols.Clear();
        _slotSymbols.AddRange(seedSlots);

        var collector = new ScopeSlotCollector(Instructions, seedSlots, AllocateSlot, entryIndex, function);
        var analysis = collector.Collect();

        rewriter = new SlotAssignmentRewriter(analysis, targetRootScopeId, analysisRootScopeId);
        var slotMapper = new Func<int, int>(rewriter.MapScopeId);
        _lexicalBindings = analysis.LexicalBindings.ToDictionary(
            kv => slotMapper(kv.Key),
            kv => kv.Value,
            EqualityComparer<int>.Default);
        rewriter.RewriteInstructions(Instructions, entryIndex);

        // Stamp iterator driver bodies (executed via AST) with slot metadata so identifiers resolve to slots.
        StampIteratorBodies(function, rewriter);

        if (EngineFeatureFlags.EnableNestedSlotStamping)
        {
            StampNestedFunctionBodies(function, rewriter, analysis);
        }

        Debug.Assert(analysis is not null);
        return analysis;

        void AppendIfMissing(IEnumerable<Symbol> symbols)
        {
            foreach (var symbol in symbols)
            {
                if (seen.Add(symbol))
                {
                    seedSlots.Add(symbol);
                }
            }
        }
    }

    private void StampIteratorBodies(FunctionExpression function, SlotAssignmentRewriter rewriter)
    {
        var collector = new ForEachCollector();
        collector.Visit(function.Body);

        foreach (var forEach in collector.Results)
        {
            var plan = ((IAstCacheable<IteratorDriverPlan>)forEach).GetOrCreateCache();
            var mappedScopeId = rewriter.MapScopeId(plan.IterationScopeId);
            var perIterationSlotIndices = plan.PerIterationBindings.IsDefaultOrEmpty
                ? plan.PerIterationSlotIndices
                : [
                    ..plan.PerIterationBindings
                        .Select(binding => rewriter.TryResolveSlot(binding, mappedScopeId, out var idx) ? idx : -1)
                ];
            var hasResolvedSlot = perIterationSlotIndices.Any(idx => idx >= 0);
            var planHasResolvedSlot = !plan.PerIterationSlotIndices.IsDefaultOrEmpty &&
                                      plan.PerIterationSlotIndices.Any(idx => idx >= 0);
            if (!hasResolvedSlot && planHasResolvedSlot)
            {
                _iteratorPlanOverrides[forEach] = plan;
                continue;
            }

            var stampedBody = rewriter.StampNodeInScope(plan.Body, mappedScopeId);
            var mappedSlotCount = rewriter.GetSlotCountForScope(mappedScopeId);
            var updatedPlan = plan with
            {
                Body = stampedBody,
                IterationScopeId = mappedScopeId,
                IterationSlotCount = mappedSlotCount >= 0 ? mappedSlotCount : plan.IterationSlotCount,
                PerIterationSlotIndices = perIterationSlotIndices.IsDefaultOrEmpty
                    ? plan.PerIterationSlotIndices
                    : perIterationSlotIndices
            };
            _iteratorPlanOverrides[forEach] = updatedPlan;
            UpdateCachedIteratorPlan(forEach, updatedPlan);
        }
    }

    /// <summary>
    /// Stamps nested function execution plans with slot metadata so closures can reference outer scope variables.
    /// This walks the function body, finds all nested FunctionExpression/FunctionDeclaration nodes,
    /// builds their execution plans (if possible), and stamps those plans with the parent's slot analysis.
    /// </summary>
    //TODO: This is the key method for the future fix, the issue is that we need to fix other tasks first.
    // ReSharper disable once UnusedMember.Local
    private static void StampNestedFunctionBodies(FunctionExpression function, SlotAssignmentRewriter rewriter,
        ScopeSlotAnalysis analysis)
    {
        var collector = new NestedFunctionCollector(analysis.BlockScopeIds);
        collector.Visit(function.Body);

        Debug.WriteLine($"[StampNestedFunctionBodies] Found {collector.Results.Count} nested functions");
        Debug.WriteLine($"[StampNestedFunctionBodies] BlockScopeIds count: {analysis.BlockScopeIds.Count}");

        foreach (var (funcExpr, scopeId) in collector.Results)
        {
            Debug.WriteLine($"[StampNestedFunctionBodies] Processing nested function, enclosingScopeId={scopeId}");

            // Trigger building the nested function's execution plan
            var nestedCache = ((IAstCacheable<ExecutionPlanCache>)funcExpr).GetOrCreateCache();
            if (!nestedCache.Succeeded || nestedCache.Plan is null)
            {
                Debug.WriteLine("[StampNestedFunctionBodies] Nested plan failed, stamping body AST");
                // If we can't build an execution plan, stamp the body AST for AST-based evaluation
                var mappedScopeId = rewriter.MapScopeId(scopeId);
                var stampedBody = rewriter.StampNodeInScope(funcExpr.Body, mappedScopeId);
                if (!ReferenceEquals(stampedBody, funcExpr.Body))
                {
                    UpdateFunctionBody(funcExpr, stampedBody);
                }

                continue;
            }

            Debug.WriteLine(
                $"[StampNestedFunctionBodies] Nested plan has {nestedCache.Plan.Instructions.Length} instructions");

            // Stamp the nested function's execution plan instructions with outer scope slot info
            var mappedScope = rewriter.MapScopeId(scopeId);
            Debug.WriteLine($"[StampNestedFunctionBodies] mappedScope={mappedScope}");
            StampNestedExecutionPlan(funcExpr, nestedCache.Plan, rewriter, mappedScope);
        }
    }

    private static void StampNestedExecutionPlan(
        FunctionExpression funcExpr,
        ExecutionPlan plan,
        SlotAssignmentRewriter rewriter,
        int enclosingScopeId)
    {
        // Create a copy of the instructions list so we can stamp them
        var instructions = plan.Instructions.ToList();

        // Stamp each instruction in the nested plan with outer scope slot info
        for (var i = 0; i < instructions.Count; i++)
        {
            instructions[i] = rewriter.StampInstructionInScope(instructions[i], enclosingScopeId);
        }

        // Create an updated plan with stamped instructions
        var stampedPlan = new ExecutionPlan(
            [.. instructions],
            plan.EntryPoint,
            plan.SlotCount,
            plan.SlotSymbols,
            plan.RootSlotCount,
            plan.RootSlotMap,
            plan.RootLexicalBindings,
            plan.ScopeLexicalBindings,
            plan.RootScopeId,
            plan.LayoutId,
            plan.FlatSlotCount,
            plan.FlatSlotMappings);

        // Update the cached plan on the FunctionExpression
        UpdateCachedExecutionPlan(funcExpr, stampedPlan);
    }

    private static void UpdateCachedExecutionPlan(FunctionExpression funcExpr, ExecutionPlan stampedPlan)
    {
        Debug.WriteLine(
            $"[UpdateCachedExecutionPlan] funcExpr.Hash={funcExpr.GetHashCode()} stampedPlan.Hash={stampedPlan.GetHashCode()}");

        var cacheField = typeof(FunctionExpression)
            .GetField("_cachedExecutionPlan", BindingFlags.Instance | BindingFlags.NonPublic);
        if (cacheField is not null)
        {
            // Create a new ExecutionPlanCache with the stamped plan
            var cacheType = typeof(ExecutionPlanCache);
            var ctor = cacheType.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                [typeof(ExecutionPlan), typeof(string)],
                null);
            if (ctor is not null)
            {
                var newCache = ctor.Invoke([stampedPlan, null]);
                cacheField.SetValue(funcExpr, newCache);
                Debug.WriteLine("[UpdateCachedExecutionPlan] Successfully updated cache");
            }
            else
            {
                Debug.WriteLine("[UpdateCachedExecutionPlan] ERROR: Constructor not found");
            }
        }
        else
        {
            Debug.WriteLine("[UpdateCachedExecutionPlan] ERROR: Field not found");
        }
    }

    private static void UpdateCachedIteratorPlan(ForEachStatement statement, IteratorDriverPlan stampedPlan)
    {
        var cacheField = typeof(ForEachStatement)
            .GetField("_cachedPlan", BindingFlags.Instance | BindingFlags.NonPublic);
        if (cacheField is not null)
        {
            var slotMapCacheField = typeof(IteratorDriverPlan)
                .GetField("_slotMapCache", BindingFlags.Instance | BindingFlags.NonPublic);
            slotMapCacheField?.SetValue(stampedPlan, null);
            cacheField.SetValue(statement, stampedPlan);
        }
    }

    private static int ComputeLayoutId(
        int rootSlotCount,
        ImmutableDictionary<Symbol, int> rootSlotMap,
        ImmutableArray<Symbol> slotSymbols)
    {
        var hash = new HashCode();
        hash.Add(rootSlotCount);

        if (!rootSlotMap.IsEmpty)
        {
            foreach (var kv in rootSlotMap.OrderBy(kv => kv.Value))
            {
                hash.Add(kv.Value);
                hash.Add(kv.Key.GetHashCode());
            }
        }
        else
        {
            hash.Add(slotSymbols.Length);
            for (var i = 0; i < slotSymbols.Length; i++)
            {
                hash.Add(i);
                hash.Add(slotSymbols[i].GetHashCode());
            }
        }

        return hash.ToHashCode();
    }

    private static void UpdateFunctionBody(FunctionExpression funcExpr, BlockStatement stampedBody)
    {
        // Use reflection to update the cached Body property
        // FunctionExpression is a record, so we need to update the backing field
        var backingField = typeof(FunctionExpression)
            .GetField("<Body>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        backingField?.SetValue(funcExpr, stampedBody);
    }

    private static List<Symbol> CollectHoistedFunctionSymbols(BlockStatement body)
    {
        var result = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        var isStrict = body.IsStrict;

        foreach (var statement in body.Statements)
        {
            CollectFromStatement(statement, result, isStrict, inBlockScope: false);
        }

        return result.ToList();

        // Per ES2024 Annex B.3.2 (Block-Level Function Declarations Web Legacy Compatibility Semantics):
        // In strict mode, function declarations inside blocks are block-scoped only and do NOT get
        // hoisted to the enclosing function scope. The AnnexB hoisting only applies to sloppy mode.
        static void CollectFromStatement(StatementNode statement, HashSet<Symbol> sink, bool isStrict, bool inBlockScope)
        {
            while (true)
            {
                switch (statement)
                {
                    case FunctionDeclaration funcDecl:
                        // In strict mode, function declarations inside blocks are NOT hoisted
                        // to the function scope. Only collect if we're at function body level
                        // (not inside a nested block) OR if we're in sloppy mode.
                        if (!isStrict || !inBlockScope)
                        {
                            sink.Add(funcDecl.Name);
                        }
                        return;
                    case BlockStatement block:
                        // When entering a nested block, function declarations inside become block-scoped
                        // in strict mode (not hoisted to function scope)
                        foreach (var inner in block.Statements)
                        {
                            CollectFromStatement(inner, sink, isStrict, inBlockScope: true);
                        }

                        return;
                    case IfStatement ifStatement:
                        // if/else bodies create implicit block scope for function declarations in strict mode
                        CollectFromStatement(ifStatement.Then, sink, isStrict, inBlockScope: true);
                        if (ifStatement.Else is { } elseBranch)
                        {
                            statement = elseBranch;
                            continue;
                        }

                        return;
                    case WhileStatement whileStatement:
                        statement = whileStatement.Body;
                        continue;
                    case DoWhileStatement doWhileStatement:
                        statement = doWhileStatement.Body;
                        continue;
                    case ForStatement forStatement:
                        if (forStatement.Initializer is { } initStmt)
                        {
                            CollectFromStatement(initStmt, sink, isStrict, inBlockScope);
                        }

                        statement = forStatement.Body;
                        continue;
                    case ForEachStatement forEachStatement:
                        statement = forEachStatement.Body;
                        continue;
                    case SwitchStatement switchStatement:
                        // Switch case/default bodies are block-scoped for function declarations
                        foreach (var switchCase in switchStatement.Cases)
                        {
                            CollectFromStatement(switchCase.Body, sink, isStrict, inBlockScope: true);
                        }

                        return;
                    case TryStatement tryStatement:
                        CollectFromStatement(tryStatement.TryBlock, sink, isStrict, inBlockScope);
                        if (tryStatement.Catch is { Body: { } catchBody })
                        {
                            CollectFromStatement(catchBody, sink, isStrict, inBlockScope);
                        }

                        if (tryStatement.Finally is { } finallyBody)
                        {
                            CollectFromStatement(finallyBody, sink, isStrict, inBlockScope);
                        }

                        return;
                    case LabeledStatement labeledStatement:
                        statement = labeledStatement.Statement;
                        continue;
                    case WithStatement withStatement:
                        statement = withStatement.Body;
                        continue;
                    default:
                        return;
                }
            }
        }
    }

    internal bool TryBuildStatementList(ImmutableArray<StatementNode> statements, int nextIndex, out int entryIndex)
    {
        var ctx = GetEmitContext();
        var currentNext = nextIndex;
        for (var i = statements.Length - 1; i >= 0; i--)
        {
            if (!ctx.TryBuildStatement(statements[i], currentNext, out currentNext))
            {
                entryIndex = -1;
                _failureReason ??= $"Unsupported statement '{statements[i].GetType().Name}'.";
                return false;
            }
        }

        entryIndex = currentNext;
        return true;
    }

    internal Symbol CreateResumeSlotSymbol()
    {
        var symbolName = $"{ResumeSlotPrefix}{_resumeSlotCounter++}";
        return Symbol.Intern(symbolName);
    }

    internal static StatementNode CreateIteratorBindingStatement(IteratorDriverPlan plan, Symbol valueSymbol,
        int valueSlotIndex, int rootScopeId)
    {
        // Stamp the identifier expression with slot info for O(1) access
        // Use the actual root scope ID from the execution context
        var valueExpression = new IdentifierExpression(plan.Body.Source, valueSymbol) { SlotIndex = valueSlotIndex, ScopeId = rootScopeId, ScopeDepth = 0 };
        StatementNode bindingStatement;

        if (plan.DeclarationKind is null)
        {
            // Per ES spec 13.6.4.13 (ForIn/OfBodyEvaluation), the iterator binding assignment
            // should NOT affect the loop's completion value. Only the loop body contributes.
            bindingStatement = new ExpressionStatement(plan.Body.Source,
                CreateAssignmentExpression(plan.Target, valueExpression),
                true);
        }
        else
        {
            var declarator = new VariableDeclarator(plan.Body.Source, plan.Target, valueExpression);
            bindingStatement = new VariableDeclaration(plan.Body.Source, plan.DeclarationKind.Value,
                [declarator]);
        }

        return bindingStatement;
    }

    private static ExpressionNode CreateAssignmentExpression(BindingTarget target, ExpressionNode valueExpression)
    {
        return target switch
        {
            IdentifierBinding identifier => new AssignmentExpression(target.Source, identifier.Name, valueExpression),
            ArrayBinding or ObjectBinding => new DestructuringAssignmentExpression(target.Source, target,
                valueExpression),
            AssignmentTargetBinding atb => CreateAssignmentExpressionFromLhs(atb.Expression, valueExpression),
            _ => throw new NotSupportedException($"Unsupported for-of binding target '{target.GetType().Name}'.")
        };
    }

    private static ExpressionNode CreateAssignmentExpressionFromLhs(ExpressionNode lhs, ExpressionNode value)
    {
        switch (lhs)
        {
            case IdentifierExpression id:
                return new AssignmentExpression(lhs.Source, id.Name, value);
            case MemberExpression member:
                return new PropertyAssignmentExpression(lhs.Source, member.Target, member.Property, value,
                    member.IsComputed);
            default:
                throw new NotSupportedException($"Unsupported for-of assignment target '{lhs.GetType().Name}'.");
        }
    }

    internal Symbol CreateCatchSlotSymbol()
    {
        var symbolName = $"{CatchSlotPrefix}{_catchSlotCounter++}";
        return Symbol.Intern(symbolName);
    }

    private Symbol CreateYieldStarStateSymbol()
    {
        var symbolName = $"{YieldStarStatePrefix}{_yieldStarStateCounter++}";
        return Symbol.Intern(symbolName);
    }

    internal Symbol CreateWithScopeSlotSymbol()
    {
        var symbolName = $"{WithScopeSlotPrefix}{_withScopeSlotCounter++}";
        return Symbol.Intern(symbolName);
    }

    internal static bool IsStrictBlock(StatementNode statement)
    {
        return statement is BlockStatement { IsStrict: true };
    }

    internal int AppendYieldSequence(ExpressionNode? expression, int continuationIndex, Symbol? resumeSlot)
    {
        var storeIndex = Append(new StoreResumeValueInstruction(continuationIndex, resumeSlot));
        return Append(new YieldInstruction(storeIndex, expression));
    }

    internal int AppendYieldStarSequence(YieldExpression expression, int continuationIndex, Symbol? resultSlot)
    {
        if (expression.Expression is null)
        {
            throw new InvalidOperationException("yield* requires an expression.");
        }

        var stateSymbol = CreateYieldStarStateSymbol();
        return Append(new YieldStarInstruction(
            continuationIndex,
            IterableExpression: expression.Expression,
            StateSlotSymbol: stateSymbol,
            ResultSlotSymbol: resultSlot));
    }

    internal static BlockStatement BuildCatchBlock(CatchClause clause, Symbol catchSlotSymbol)
    {
        var builder = ImmutableArray.CreateBuilder<StatementNode>();

        // ES2019: Optional catch binding - only add variable declaration if binding exists
        if (clause.Binding is not null)
        {
            var declarator = new VariableDeclarator(
                clause.Source,
                clause.Binding,
                new IdentifierExpression(clause.Source, catchSlotSymbol));
            var declaration = new VariableDeclaration(
                clause.Source,
                VariableKind.Let,
                [declarator]);
            builder.Add(declaration);
        }

        builder.AddRange(clause.Body.Statements);

        // IMPORTANT: Create a NEW BlockStatement instead of using `with`.
        // Using `with` would copy the cached HoistPlan from the original catch body,
        // which doesn't include the synthetic `let` declaration. This would cause
        // the catch block to execute without its own environment, causing the catch
        // parameter to overwrite any same-named var in the enclosing function scope.
        return new BlockStatement(clause.Source, builder.ToImmutableArray(), clause.Body.IsStrict);
    }

    private int Append(ExecutionInstruction instruction)
    {
        var index = Instructions.Count;
        Instructions.Add(instruction);
        return index;
    }

    /// <summary>
    /// Checks if a statement contains a try-finally where the finally block has
    /// unlabeled break/continue that would target the enclosing switch/loop.
    /// This is used to reject such cases in switch statements since the lowered
    /// code transforms switch into if statements, making the break target invalid.
    /// </summary>
    internal static bool ContainsUnlabeledAbruptInFinally(StatementNode statement)
    {
        return ContainsUnlabeledAbruptInFinallyImpl(statement, false);
    }

    private static bool ContainsUnlabeledAbruptInFinallyImpl(StatementNode statement, bool inFinally)
    {
        while (true)
        {
            switch (statement)
            {
                case TryStatement tryStmt:
                    // Check the try block (not in finally yet)
                    if (ContainsUnlabeledAbruptInFinallyImpl(tryStmt.TryBlock, inFinally))
                    {
                        return true;
                    }

                    // Check the catch block if present
                    if (tryStmt.Catch is not null &&
                        ContainsUnlabeledAbruptInFinallyImpl(tryStmt.Catch.Body, inFinally))
                    {
                        return true;
                    }

                    // Check the finally block - now we're in a finally context
                    if (tryStmt.Finally is not null && ContainsUnlabeledAbruptInFinallyImpl(tryStmt.Finally, true))
                    {
                        return true;
                    }

                    return false;

                case BreakStatement { Label: null }:
                case ContinueStatement { Label: null }:
                    // Unlabeled break/continue inside a finally targeting outer switch
                    return inFinally;

                case BlockStatement block:
                    foreach (var stmt in block.Statements)
                    {
                        if (ContainsUnlabeledAbruptInFinallyImpl(stmt, inFinally))
                        {
                            return true;
                        }
                    }

                    return false;

                case IfStatement ifStmt:
                    if (ContainsUnlabeledAbruptInFinallyImpl(ifStmt.Then, inFinally))
                    {
                        return true;
                    }

                    if (ifStmt.Else is not null && ContainsUnlabeledAbruptInFinallyImpl(ifStmt.Else, inFinally))
                    {
                        return true;
                    }

                    return false;

                case WhileStatement whileStmt:
                    // Break/continue inside a loop targets the loop, not outer switch
                    // So we reset inFinally context for the loop body
                    statement = whileStmt.Body;
                    inFinally = false;
                    continue;

                case DoWhileStatement doWhileStmt:
                    statement = doWhileStmt.Body;
                    inFinally = false;
                    continue;

                case ForStatement forStmt:
                    statement = forStmt.Body;
                    inFinally = false;
                    continue;

                case ForEachStatement forEachStmt:
                    statement = forEachStmt.Body;
                    inFinally = false;
                    continue;

                case SwitchStatement switchStmt:
                    // Break inside a nested switch targets that switch, not outer one
                    // But we still need to check for try-finally patterns
                    foreach (var switchCase in switchStmt.Cases)
                    {
                        if (ContainsUnlabeledAbruptInFinallyImpl(switchCase.Body, false))
                        {
                            return true;
                        }
                    }

                    return false;

                case LabeledStatement labeledStmt:
                    statement = labeledStmt.Statement;
                    continue;

                case WithStatement withStmt:
                    statement = withStmt.Body;
                    continue;

                default:
                    // Other statements (return, throw, expression, var, etc.) don't contain nested abrupt
                    return false;
            }
        }
    }

    private sealed class ForEachCollector : AstVisitor
    {
        public List<ForEachStatement> Results { get; } = [];

        protected override void VisitStatement(StatementNode statement)
        {
            if (statement is ForEachStatement forEach)
            {
                Results.Add(forEach);
            }

            base.VisitStatement(statement);
        }
    }

    private sealed class NestedFunctionCollector : AstVisitor
    {
        private readonly Dictionary<BlockStatement, int> _analysisBlockScopes;
        private readonly Stack<int> _scopeStack = new();

        public NestedFunctionCollector(Dictionary<BlockStatement, int> blockScopeIds)
        {
            _analysisBlockScopes = blockScopeIds;
            _scopeStack.Push(0); // Root scope
        }

        /// <summary>
        /// Collected functions with their enclosing scope ID.
        /// </summary>
        public List<(FunctionExpression Function, int EnclosingScopeId)> Results { get; } = [];

        protected override void VisitBlockStatement(BlockStatement node)
        {
            // Use the scope ID from the analysis if this block was assigned one
            if (_analysisBlockScopes.TryGetValue(node, out var scopeId))
            {
                _scopeStack.Push(scopeId);
                base.VisitBlockStatement(node);
                _scopeStack.Pop();
            }
            else
            {
                base.VisitBlockStatement(node);
            }
        }

        protected override void VisitStatement(StatementNode statement)
        {
            if (statement is FunctionDeclaration funcDecl)
            {
                // Capture the nested function with its enclosing scope
                Results.Add((funcDecl.Function, _scopeStack.Peek()));
                // Don't traverse into the function body here - it will be stamped separately
                return;
            }

            base.VisitStatement(statement);
        }

        protected override void VisitExpression(ExpressionNode expression)
        {
            if (expression is FunctionExpression funcExpr)
            {
                // Capture the nested function with its enclosing scope
                Results.Add((funcExpr, _scopeStack.Peek()));
                // Don't traverse into the function body here - it will be stamped separately
                return;
            }

            base.VisitExpression(expression);
        }
    }
}
