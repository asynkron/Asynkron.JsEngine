#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
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
    private int _catchSlotCounter;
    private string? _failureReason;
    private int _resumeSlotCounter;
    private int _withScopeSlotCounter;
    private int _yieldStarStateCounter;

    /// <summary>
    /// Allocates a new slot index for a generator-internal symbol.
    /// </summary>
    private int AllocateSlot(Symbol symbol)
    {
        var index = _slotSymbols.Count;
        _slotSymbols.Add(symbol);
        return index;
    }

    private ExecutionPlanBuilder()
    {
    }

    public static bool TryBuild(FunctionExpression function, out ExecutionPlan plan, out string? failureReason,
        bool reportDiagnostics = true)
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

        var builder = new ExecutionPlanBuilder();
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
        AssignSlotsToUserVariables();

        plan = new ExecutionPlan(
            [.._instructions],
            entryIndex,
            _slotSymbols.Count,
            [.._slotSymbols]);
        return true;
    }

    /// <summary>
    /// Collects all user variable identifiers from instructions, assigns them slots,
    /// and updates the AST nodes with ScopeId=0 and the assigned slot indices.
    /// </summary>
    private void AssignSlotsToUserVariables()
    {
        // Step 1: Collect all unique user variable symbols from instructions using the visitor
        var collector = new IdentifierCollector();
        foreach (var instruction in _instructions)
        {
            collector.VisitInstruction(instruction);
        }

        // Step 2: Build a map from symbol to (scopeId, slotIndex)
        var symbolToScope = new Dictionary<Symbol, (int scopeId, int slotIndex)>(
            collector.Identifiers.Count,
            ReferenceEqualityComparer<Symbol>.Instance);
        foreach (var symbol in collector.Identifiers)
        {
            // Allocate a slot for this user variable
            var slotIndex = AllocateSlot(symbol);
            symbolToScope[symbol] = (0, slotIndex); // ScopeId=0 for execution plan environment
        }

        // Step 3: Update all instructions to use the new slot information using the rewriter
        if (symbolToScope.Count > 0)
        {
            var rewriter = new SlotAssignmentRewriter(symbolToScope);
            for (var i = 0; i < _instructions.Count; i++)
            {
                _instructions[i] = rewriter.RewriteInstruction(_instructions[i]);
            }
        }
    }

    private bool TryBuildStatementList(ImmutableArray<StatementNode> statements, int nextIndex, out int entryIndex)
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

    private Symbol CreateResumeSlotSymbol()
    {
        var symbolName = $"{ResumeSlotPrefix}{_resumeSlotCounter++}";
        return Symbol.Intern(symbolName);
    }

    private static StatementNode CreateIteratorBindingStatement(IteratorDriverPlan plan, Symbol valueSymbol,
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
            bindingStatement = new ExpressionStatement(plan.Body.Source,
                CreateAssignmentExpression(plan.Target, valueExpression));
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

    private Symbol CreateCatchSlotSymbol()
    {
        var symbolName = $"{CatchSlotPrefix}{_catchSlotCounter++}";
        return Symbol.Intern(symbolName);
    }

    private Symbol CreateYieldStarStateSymbol()
    {
        var symbolName = $"{YieldStarStatePrefix}{_yieldStarStateCounter++}";
        return Symbol.Intern(symbolName);
    }

    private Symbol CreateWithScopeSlotSymbol()
    {
        var symbolName = $"{WithScopeSlotPrefix}{_withScopeSlotCounter++}";
        return Symbol.Intern(symbolName);
    }

    private static bool IsStrictBlock(StatementNode statement)
    {
        return statement is BlockStatement { IsStrict: true };
    }

    private int AppendYieldSequence(ExpressionNode? expression, int continuationIndex, Symbol? resumeSlot)
    {
        var storeIndex = Append(new StoreResumeValueInstruction(continuationIndex, resumeSlot));
        return Append(new YieldInstruction(storeIndex, expression));
    }

    private int AppendYieldStarSequence(YieldExpression expression, int continuationIndex, Symbol? resultSlot)
    {
        if (expression.Expression is null)
        {
            throw new InvalidOperationException("yield* requires an expression.");
        }

        var stateSymbol = CreateYieldStarStateSymbol();
        return Append(new YieldStarInstruction(continuationIndex, expression.Expression, stateSymbol, resultSlot));
    }

    private static BlockStatement BuildCatchBlock(CatchClause clause, Symbol catchSlotSymbol)
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
    private static bool ContainsUnlabeledAbruptInFinally(StatementNode statement)
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
