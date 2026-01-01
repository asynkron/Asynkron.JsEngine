#region

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
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

        // After building all instructions, assign slots to user variables and update AST nodes
        // NOTE: For scripts (IsScriptLevel=true), we do NOT assign slots to user variables because:
        // 1. Script hoisting already created dictionary-based bindings for var/let/const declarations
        // 2. Scripts may contain 'with' statements that require dynamic identifier resolution
        // 3. Slot-based lookup would bypass the with-scope, breaking 'with' semantics
        // For functions, slot assignment is fine because scope analysis happens at parse time.
        ScopeSlotAnalysis? analysis = null;
        if (!_isScriptLevel)
        {
            analysis = AssignSlotsToUserVariables(entryIndex);
        }

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
    private ScopeSlotAnalysis AssignSlotsToUserVariables(int entryIndex)
    {
        var collector = new ScopeSlotCollector(_instructions, _slotSymbols, AllocateSlot);
        var analysis = collector.Collect();
        _lexicalBindings = analysis.LexicalBindings;
        var rewriter = new SlotAssignmentRewriter(analysis);
        rewriter.RewriteInstructions(_instructions, entryIndex);

        if (Environment.GetEnvironmentVariable("DEBUG_SLOT") == "1")
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Slot Assignment Debug ===");
            foreach (var scope in analysis.Scopes.OrderBy(kv => kv.Key))
            {
                var info = scope.Value;
                sb.AppendLine($"Scope {scope.Key} count={info.SlotCount} hint={info.SlotCountHint}");
                foreach (var kv in info.Slots.OrderBy(kv => kv.Value))
                {
                    sb.AppendLine($"  {kv.Key.Name} -> {kv.Value}");
                }
            }

            foreach (var instruction in _instructions)
            {
                switch (instruction)
                {
                    case Instructions.PushEnvironmentInstruction push:
                        sb.AppendLine(
                            $"PushEnv scope={push.ScopeId} slotCount={push.SlotCount} slots=[{string.Join(",", push.SlotMap.Select(kv => $"{kv.Key.Name}:{kv.Value}"))}]");
                        break;
                    case Instructions.BranchInstruction branch when branch.Condition is Ast.BinaryExpression { Left: Ast.IdentifierExpression leftId }:
                        sb.AppendLine($"Branch condition left name={leftId.Name.Name} scope={leftId.ScopeId} slot={leftId.SlotIndex}");
                        break;
                }
            }

            var identifiers = new List<Ast.IdentifierExpression>();

            void CollectIdentifiers(Ast.ExpressionNode expr)
            {
                switch (expr)
                {
                    case Ast.IdentifierExpression id:
                        identifiers.Add(id);
                        break;
                    case Ast.BinaryExpression bin:
                        CollectIdentifiers(bin.Left);
                        CollectIdentifiers(bin.Right);
                        break;
                    case Ast.UnaryExpression unary:
                        CollectIdentifiers(unary.Operand);
                        break;
                }
            }

            foreach (var instruction in _instructions)
            {
                switch (instruction)
                {
                    case Instructions.BranchInstruction branch:
                        CollectIdentifiers(branch.Condition);
                        break;
                    case Instructions.CompoundAssignmentSlotInstruction compound:
                        CollectIdentifiers(compound.RhsExpression);
                        break;
                    case Instructions.ReturnInstruction { ReturnExpression: { } retExpr }:
                        CollectIdentifiers(retExpr);
                        break;
                    case Instructions.SimpleVariableDeclarationInstruction { Initializer: { } init }:
                        CollectIdentifiers(init);
                        break;
                }
            }

            sb.AppendLine("Identifiers:");
            foreach (var id in identifiers)
            {
                sb.AppendLine($"  {id.Name.Name} scope={id.ScopeId} slot={id.SlotIndex}");
            }

            Console.WriteLine(sb.ToString());
            File.AppendAllText("/tmp/slotdebug.txt", sb.ToString());
        }

        return analysis;
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
