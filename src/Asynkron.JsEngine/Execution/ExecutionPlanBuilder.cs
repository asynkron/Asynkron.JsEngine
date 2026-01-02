#region

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using static Asynkron.JsEngine.Ast.TypedAstEvaluator;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Builds execution plans (IR) for all function types, executed by different invokers:
///     - Synchronous functions (SyncFunctionInvoker) when JsEngineConstants.SyncIrLoops = true
///     - Synchronous generators (SyncGeneratorInvoker)
///     - Async functions (AsyncFunctionInvoker)
///     - Async generators (AsyncGeneratorInvoker, AsyncGeneratorFunctionInvoker)
///
///     The builder supports linear statement lists, blocks, expression statements, variable declarations,
///     returns, yield/yield* expressions, and control flow (if/loops/try-catch).
///     More complex constructs are detected and reported as unsupported so the engine can fall back to
///     the legacy AST-walking evaluator.
/// </summary>
internal sealed partial class ExecutionPlanBuilder
{
    private const string ResumeSlotPrefix = "\u0001_resume";
    private const string CatchSlotPrefix = "\u0001_catch";
    private const string YieldStarStatePrefix = "\u0001_yieldstar";
    private const string WithScopeSlotPrefix = "\u0001_with";
    private readonly List<ExecutionInstruction> _instructions = [];
    private readonly Stack<LoopScope> _loopScopes = new();
    private readonly List<Symbol> _slotSymbols = [];
    private Dictionary<int, ImmutableHashSet<Symbol>> _lexicalBindings = new();
    private int _catchSlotCounter;
    private string? _failureReason;
    private bool _isScriptLevel;
    private int _resumeSlotCounter;
    private int _scopeIdCounter = 1; // Start at 1 because 0 is reserved for function-level scope
    private int _withScopeSlotCounter;
    private int _yieldStarStateCounter;

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
    /// Whether this plan is being built for a top-level script (not a function body).
    /// Script-level var declarations must update the global object.
    /// </summary>
    internal bool IsScriptLevel => _isScriptLevel;

    private ExecutionPlanBuilder()
    {
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
    public static bool TryBuild(FunctionExpression function, out ExecutionPlan plan, out string? failureReason,
        bool reportDiagnostics = true, bool isScriptLevel = false)
    {
        // First run the yield-lowering pre-pass so that ExecutionPlanBuilder
        // can assume a simplified, pauseable-function-friendly AST. The lowerer currently acts
        // as a no-op scaffold; yield normalization logic will be migrated here
        // incrementally.
        if (!GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var lowerFailure))
        {
            plan = null!;
            failureReason = lowerFailure;

            if (reportDiagnostics)
            {
                ExecutionPlanDiagnostics.ReportResult(function, false, failureReason);
            }
            return false;
        }

        var builder = new ExecutionPlanBuilder { _isScriptLevel = isScriptLevel };
        var succeeded = builder.TryBuildInternal(lowered, out plan);
        failureReason = builder._failureReason ?? lowerFailure;

        if (reportDiagnostics)
        {
            ExecutionPlanDiagnostics.ReportResult(function, succeeded, failureReason);
        }
        return succeeded;
    }

    private bool TryBuildInternal(FunctionExpression function, out ExecutionPlan plan)
    {
        // Always append an implicit "return undefined" instruction. Statement lists fall through to this index.
        var implicitReturnIndex = Append(new ReturnInstruction(-1, null));
        if (!TryBuildStatementList(function.Body.Statements, implicitReturnIndex, out var entryIndex))
        {
            plan = default!;
            _failureReason ??= "Statement list contains unsupported construct.";
            return false;
        }

        // After building all instructions, assign slots to user variables and update AST nodes.
        // Scripts are only executed via IR when dynamic scope is excluded (no with/eval),
        // so slot assignment is safe and needed for consistent slot indices.
        var analysis = AssignSlotsToUserVariables(entryIndex, function);

        var rootSlotCount = analysis is not null && analysis.Scopes.TryGetValue(0, out var rootInfo)
            ? rootInfo.SlotCount
            : 0;
        var rootSlotMap = analysis is not null && analysis.ImmutableSlotMaps.TryGetValue(0, out var rootMap)
            ? rootMap
            : ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);
        var rootLexicalBindings = analysis is not null && analysis.LexicalBindings.TryGetValue(0, out var rootLex)
            ? rootLex
            : ImmutableHashSet<Symbol>.Empty.WithComparer(ReferenceEqualityComparer<Symbol>.Instance);

        plan = new ExecutionPlan(
            [.._instructions],
            entryIndex,
            _slotSymbols.Count,
            [.._slotSymbols],
            rootSlotCount,
            rootSlotMap,
            rootLexicalBindings,
            _lexicalBindings.ToImmutableDictionary(kv => kv.Key, kv => kv.Value,
                EqualityComparer<int>.Default));
        return true;
    }

    /// <summary>
    /// Collects all user variable identifiers from instructions, assigns them slots,
    /// and updates the AST nodes with scope-aware slot metadata.
    /// </summary>
    private ScopeSlotAnalysis AssignSlotsToUserVariables(int entryIndex, FunctionExpression function)
    {
        var parameterNames = new List<Symbol>();
        function.CollectParameterNamesFromFunction(parameterNames);
        var hoistedFunctions = CollectHoistedFunctionSymbols(function.Body);
        var seedSlots = new List<Symbol>(_slotSymbols.Count + hoistedFunctions.Count + parameterNames.Count);
        var seen = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);

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

        AppendIfMissing(_slotSymbols);
        AppendIfMissing(hoistedFunctions);
        AppendIfMissing(parameterNames);

        // Keep backing slot list aligned with the ordered seeds so future allocations
        // get non-conflicting indices.
        _slotSymbols.Clear();
        _slotSymbols.AddRange(seedSlots);

        var collector = new ScopeSlotCollector(_instructions, seedSlots, AllocateSlot, entryIndex, function);
        var analysis = collector.Collect();
        _lexicalBindings = analysis.LexicalBindings;
        var rewriter = new SlotAssignmentRewriter(analysis);
        rewriter.RewriteInstructions(_instructions, entryIndex);

        // Stamp iterator driver bodies (executed via AST) with slot metadata so identifiers resolve to slots.
        StampIteratorBodies(function, rewriter);

        // Stamp nested function bodies so that closures can reference outer scope variables.
        // This is critical for closures accessing block-scoped variables.
        StampNestedFunctionBodies(function, rewriter, analysis);

        return analysis;
    }

    private static void StampIteratorBodies(FunctionExpression function, SlotAssignmentRewriter rewriter)
    {
        var collector = new ForEachCollector();
        collector.Visit(function.Body);

        foreach (var forEach in collector.Results)
        {
            var plan = ((IAstCacheable<IteratorDriverPlan>)forEach).GetOrCreateCache();
            var mappedScopeId = rewriter.MapScopeId(plan.IterationScopeId);
            var stampedBody = (BlockStatement)rewriter.StampNodeInScope(plan.Body, mappedScopeId);
            var mappedSlotCount = rewriter.GetSlotCountForScope(mappedScopeId);
            var perIterationSlotIndices = plan.PerIterationBindings.IsDefaultOrEmpty
                ? plan.PerIterationSlotIndices
                : plan.PerIterationBindings
                    .Select(binding => rewriter.TryResolveSlot(binding, mappedScopeId, out var idx) ? idx : -1)
                    .ToImmutableArray();
            if (!ReferenceEquals(stampedBody, plan.Body))
            {
                UpdateCachedIteratorPlan(forEach, plan, stampedBody, mappedScopeId, mappedSlotCount,
                    perIterationSlotIndices);
            }
            else if (plan.IterationScopeId != mappedScopeId ||
                     plan.IterationSlotCount != mappedSlotCount ||
                     !perIterationSlotIndices.IsDefaultOrEmpty && perIterationSlotIndices != plan.PerIterationSlotIndices)
            {
                UpdateCachedIteratorPlan(forEach, plan, plan.Body, mappedScopeId, mappedSlotCount,
                    perIterationSlotIndices);
            }
        }
    }

    private static void UpdateCachedIteratorPlan(
        ForEachStatement forEach,
        IteratorDriverPlan existingPlan,
        BlockStatement stampedBody,
        int mappedScopeId,
        int mappedSlotCount,
        ImmutableArray<int> mappedSlotIndices)
    {
        var updatedPlan = existingPlan with
        {
            Body = stampedBody,
            IterationScopeId = mappedScopeId,
            IterationSlotCount = mappedSlotCount >= 0 ? mappedSlotCount : existingPlan.IterationSlotCount,
            PerIterationSlotIndices = mappedSlotIndices.IsDefaultOrEmpty
                ? existingPlan.PerIterationSlotIndices
                : mappedSlotIndices
        };
        var cacheField = typeof(ForEachStatement)
            .GetField("_cachedPlan", BindingFlags.Instance | BindingFlags.NonPublic);
        cacheField?.SetValue(forEach, updatedPlan);
    }

    private sealed class ForEachCollector : AstVisitor
    {
        public List<ForEachStatement> Results { get; } = new();

        protected override void VisitStatement(StatementNode statement)
        {
            if (statement is ForEachStatement forEach)
            {
                Results.Add(forEach);
            }

            base.VisitStatement(statement);
        }
    }

    /// <summary>
    /// Stamps nested function execution plans with slot metadata so closures can reference outer scope variables.
    /// This walks the function body, finds all nested FunctionExpression/FunctionDeclaration nodes,
    /// builds their execution plans (if possible), and stamps those plans with the parent's slot analysis.
    /// </summary>
    private static void StampNestedFunctionBodies(FunctionExpression function, SlotAssignmentRewriter rewriter, ScopeSlotAnalysis analysis)
    {
        var collector = new NestedFunctionCollector(analysis.BlockScopeIds);
        collector.Visit(function.Body);

        System.Diagnostics.Debug.WriteLine($"[StampNestedFunctionBodies] Found {collector.Results.Count} nested functions");
        System.Diagnostics.Debug.WriteLine($"[StampNestedFunctionBodies] BlockScopeIds count: {analysis.BlockScopeIds.Count}");

        foreach (var (funcExpr, scopeId) in collector.Results)
        {
            System.Diagnostics.Debug.WriteLine($"[StampNestedFunctionBodies] Processing nested function, enclosingScopeId={scopeId}");

            // Trigger building the nested function's execution plan
            var nestedCache = ((IAstCacheable<ExecutionPlanCache>)funcExpr).GetOrCreateCache();
            if (!nestedCache.Succeeded || nestedCache.Plan is null)
            {
                System.Diagnostics.Debug.WriteLine($"[StampNestedFunctionBodies] Nested plan failed, stamping body AST");
                // If we can't build an execution plan, stamp the body AST for AST-based evaluation
                var mappedScopeId = rewriter.MapScopeId(scopeId);
                var stampedBody = (BlockStatement)rewriter.StampNodeInScope(funcExpr.Body, mappedScopeId);
                if (!ReferenceEquals(stampedBody, funcExpr.Body))
                {
                    UpdateFunctionBody(funcExpr, stampedBody);
                }
                continue;
            }

            System.Diagnostics.Debug.WriteLine($"[StampNestedFunctionBodies] Nested plan has {nestedCache.Plan.Instructions.Length} instructions");

            // Stamp the nested function's execution plan instructions with outer scope slot info
            var mappedScope = rewriter.MapScopeId(scopeId);
            System.Diagnostics.Debug.WriteLine($"[StampNestedFunctionBodies] mappedScope={mappedScope}");
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
            [..instructions],
            plan.EntryPoint,
            plan.SlotCount,
            plan.SlotSymbols,
            plan.RootSlotCount,
            plan.RootSlotMap,
            plan.RootLexicalBindings,
            plan.ScopeLexicalBindings);

        // Update the cached plan on the FunctionExpression
        UpdateCachedExecutionPlan(funcExpr, stampedPlan);
    }

    private static void UpdateCachedExecutionPlan(FunctionExpression funcExpr, ExecutionPlan stampedPlan)
    {
        System.Diagnostics.Debug.WriteLine($"[UpdateCachedExecutionPlan] funcExpr.Hash={funcExpr.GetHashCode()} stampedPlan.Hash={stampedPlan.GetHashCode()}");

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
                System.Diagnostics.Debug.WriteLine($"[UpdateCachedExecutionPlan] Successfully updated cache");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateCachedExecutionPlan] ERROR: Constructor not found");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateCachedExecutionPlan] ERROR: Field not found");
        }
    }

    private static void UpdateFunctionBody(FunctionExpression funcExpr, BlockStatement stampedBody)
    {
        // Use reflection to update the cached Body property
        // FunctionExpression is a record, so we need to update the backing field
        var backingField = typeof(FunctionExpression)
            .GetField("<Body>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        backingField?.SetValue(funcExpr, stampedBody);
    }

    private sealed class NestedFunctionCollector : AstVisitor
    {
        private readonly Stack<int> _scopeStack = new();
        private readonly Dictionary<BlockStatement, int> _analysisBlockScopes;

        /// <summary>
        /// Collected functions with their enclosing scope ID.
        /// </summary>
        public List<(FunctionExpression Function, int EnclosingScopeId)> Results { get; } = new();

        public NestedFunctionCollector(Dictionary<BlockStatement, int> blockScopeIds)
        {
            _analysisBlockScopes = blockScopeIds;
            _scopeStack.Push(0); // Root scope
        }

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

    private static List<Symbol> CollectHoistedFunctionSymbols(BlockStatement body)
    {
        var result = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);

        foreach (var statement in body.Statements)
        {
            CollectFromStatement(statement, inBlockScope: false, result);
        }

        return result.ToList();

        static void CollectFromStatement(StatementNode statement, bool inBlockScope, HashSet<Symbol> sink)
        {
            while (true)
            {
                switch (statement)
                {
                    case FunctionDeclaration funcDecl:
                        sink.Add(funcDecl.Name);
                        return;
                    case BlockStatement block:
                        foreach (var inner in block.Statements)
                        {
                            CollectFromStatement(inner, true, sink);
                        }
                        return;
                    case IfStatement ifStatement:
                        CollectFromStatement(ifStatement.Then, true, sink);
                        if (ifStatement.Else is { } elseBranch)
                        {
                            statement = elseBranch;
                            inBlockScope = true;
                            continue;
                        }
                        return;
                    case WhileStatement whileStatement:
                        statement = whileStatement.Body;
                        inBlockScope = true;
                        continue;
                    case DoWhileStatement doWhileStatement:
                        statement = doWhileStatement.Body;
                        inBlockScope = true;
                        continue;
                    case ForStatement forStatement:
                        if (forStatement.Initializer is StatementNode initStmt)
                        {
                            CollectFromStatement(initStmt, true, sink);
                        }
                        statement = forStatement.Body;
                        inBlockScope = true;
                        continue;
                    case ForEachStatement forEachStatement:
                        statement = forEachStatement.Body;
                        inBlockScope = true;
                        continue;
                    case SwitchStatement switchStatement:
                        foreach (var switchCase in switchStatement.Cases)
                        {
                            CollectFromStatement(switchCase.Body, true, sink);
                        }
                        return;
                    case TryStatement tryStatement:
                        CollectFromStatement(tryStatement.TryBlock, true, sink);
                        if (tryStatement.Catch is { Body: { } catchBody })
                        {
                            CollectFromStatement(catchBody, true, sink);
                        }
                        if (tryStatement.Finally is { } finallyBody)
                        {
                            CollectFromStatement(finallyBody, true, sink);
                        }
                        return;
                    case LabeledStatement labeledStatement:
                        statement = labeledStatement.Statement;
                        inBlockScope = true;
                        continue;
                    case WithStatement withStatement:
                        statement = withStatement.Body;
                        inBlockScope = true;
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
        int valueSlotIndex)
    {
        // Stamp the identifier expression with slot info for O(1) access
        // ScopeId = 0 means the function's primary scope where execution plan slots live
        var valueExpression = new IdentifierExpression(plan.Body.Source, valueSymbol) with
        {
            SlotIndex = valueSlotIndex,
            ScopeId = 0,
            ScopeDepth = 0
        };
        StatementNode bindingStatement;

        if (plan.DeclarationKind is null)
        {
            // Per ES spec 13.6.4.13 (ForIn/OfBodyEvaluation), the iterator binding assignment
            // should NOT affect the loop's completion value. Only the loop body contributes.
            bindingStatement = new ExpressionStatement(plan.Body.Source,
                CreateAssignmentExpression(plan.Target, valueExpression),
                SuppressCompletionValue: true);
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
        return Append(new YieldStarInstruction(continuationIndex, expression.Expression, stateSymbol, resultSlot));
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
        var index = _instructions.Count;
        _instructions.Add(instruction);
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
                    if (ContainsUnlabeledAbruptInFinallyImpl(tryStmt.TryBlock, inFinally)) return true;

                    // Check the catch block if present
                    if (tryStmt.Catch is not null && ContainsUnlabeledAbruptInFinallyImpl(tryStmt.Catch.Body, inFinally)) return true;

                    // Check the finally block - now we're in a finally context
                    if (tryStmt.Finally is not null && ContainsUnlabeledAbruptInFinallyImpl(tryStmt.Finally, true)) return true;

                    return false;

                case BreakStatement { Label: null }:
                case ContinueStatement { Label: null }:
                    // Unlabeled break/continue inside a finally targeting outer switch
                    return inFinally;

                case BlockStatement block:
                    foreach (var stmt in block.Statements)
                    {
                        if (ContainsUnlabeledAbruptInFinallyImpl(stmt, inFinally)) return true;
                    }

                    return false;

                case IfStatement ifStmt:
                    if (ContainsUnlabeledAbruptInFinallyImpl(ifStmt.Then, inFinally)) return true;
                    if (ifStmt.Else is not null && ContainsUnlabeledAbruptInFinallyImpl(ifStmt.Else, inFinally)) return true;
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
                        if (ContainsUnlabeledAbruptInFinallyImpl(switchCase.Body, false)) return true;
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
}
