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
        var currentNext = nextIndex;
        for (var i = statements.Length - 1; i >= 0; i--)
        {
            if (!TryBuildStatement(statements[i], currentNext, out currentNext))
            {
                entryIndex = -1;
                _failureReason ??= $"Unsupported statement '{statements[i].GetType().Name}'.";
                return false;
            }
        }

        entryIndex = currentNext;
        return true;
    }

    private bool TryBuildStatement(StatementNode statement, int nextIndex, out int entryIndex,
        Symbol? activeLabel = null)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    return Emitters.BlockEmitter.TryEmitBlock(GetEmitContext(), block, nextIndex, out entryIndex);

                case FunctionDeclaration:
                    // Function declarations are hoisted - this is a no-op at runtime
                    entryIndex = Append(new FunctionDeclarationInstruction(nextIndex));
                    return true;

                case IfStatement ifStatement:
                    return Emitters.ControlFlowEmitter.TryEmitIf(
                        GetEmitContext(), ifStatement, nextIndex, activeLabel, out entryIndex);

                case EmptyStatement:
                    entryIndex = nextIndex;
                    return true;

                case ExpressionStatement { Expression: YieldExpression yieldExpression }:
                    return Emitters.YieldEmitter.TryEmitYieldExpressionStatement(
                        GetEmitContext(), yieldExpression, nextIndex, out entryIndex);

                case ExpressionStatement expressionStatement:
                    return Emitters.ExpressionStatementEmitter.TryEmitExpressionStatement(
                        GetEmitContext(), expressionStatement, nextIndex, out entryIndex);

                case VariableDeclaration declaration:
                    return Emitters.DeclarationEmitter.TryEmitVariableDeclaration(
                        GetEmitContext(), declaration, nextIndex, out entryIndex);

                case WhileStatement whileStatement:
                    if (AstShapeAnalyzer.ContainsYield(whileStatement.Condition))
                    {
                        entryIndex = -1;
                        _failureReason ??= "While condition contains unsupported yield shape.";
                        return false;
                    }

                    var whileStrict = IsStrictBlock(whileStatement.Body);
                    if (!LoopNormalizer.TryNormalize(whileStatement, whileStrict, out var whilePlan,
                            out var whileFailure))
                    {
                        entryIndex = -1;
                        _failureReason ??= whileFailure ?? "Failed to normalize while loop.";
                        return false;
                    }

                    return Emitters.LoopEmitter.TryEmitLoopPlan(
                        GetEmitContext(), whilePlan, nextIndex, activeLabel, out entryIndex);

                case DoWhileStatement doWhileStatement:
                    if (AstShapeAnalyzer.ContainsYield(doWhileStatement.Condition))
                    {
                        entryIndex = -1;
                        _failureReason ??= "Do/while condition contains unsupported yield shape.";
                        return false;
                    }

                    var doStrict = IsStrictBlock(doWhileStatement.Body);
                    if (!LoopNormalizer.TryNormalize(doWhileStatement, doStrict,
                            out var doWhilePlan, out var doFailure))
                    {
                        entryIndex = -1;
                        _failureReason ??= doFailure ?? "Failed to normalize do/while loop.";
                        return false;
                    }

                    return Emitters.LoopEmitter.TryEmitLoopPlan(
                        GetEmitContext(), doWhilePlan, nextIndex, activeLabel, out entryIndex);

                case ForStatement forStatement:
                    if (forStatement.Condition is not null && AstShapeAnalyzer.ContainsYield(forStatement.Condition))
                    {
                        entryIndex = -1;
                        _failureReason ??= "For condition contains unsupported yield shape.";
                        return false;
                    }

                    if (forStatement.Increment is not null && AstShapeAnalyzer.ContainsYield(forStatement.Increment))
                    {
                        entryIndex = -1;
                        _failureReason ??= "For increment contains unsupported yield shape.";
                        return false;
                    }

                    var forStrict = IsStrictBlock(forStatement.Body);
                    if (!LoopNormalizer.TryNormalize(forStatement, forStrict, out var forPlan,
                            out var forFailure))
                    {
                        entryIndex = -1;
                        _failureReason ??= forFailure ?? "Failed to normalize for loop.";
                        return false;
                    }

                    return Emitters.LoopEmitter.TryEmitLoopPlan(
                        GetEmitContext(), forPlan, nextIndex, activeLabel, out entryIndex);

                case SwitchStatement switchStatement:
                    if (Emitters.SwitchEmitter.TryEmitSwitch(
                        GetEmitContext(), switchStatement, nextIndex, activeLabel, out entryIndex))
                    {
                        return true;
                    }

                    entryIndex = -1;
                    _failureReason ??= "Unsupported statement 'SwitchStatement'.";
                    return false;

                case TryStatement tryStatement:
                    return Emitters.TryEmitter.TryEmitTry(
                        GetEmitContext(), tryStatement, nextIndex, activeLabel, out entryIndex);

                case ForEachStatement { Kind: ForEachKind.Of } forEachStatement
                    when IsSimpleForOfBinding(forEachStatement):
                    // If binding target has yields anywhere (defaults or assignment target expressions),
                    // wrap as StatementInstruction. The AST evaluator handles yield state-saving correctly.
                    // This handles patterns like: for ([ {}[ yield ] ] of iterable) { }
                    if (BindingTargetContainsYieldAnywhere(forEachStatement.Target) &&
                        !AstShapeAnalyzer.StatementContainsYield(forEachStatement.Body) &&
                        !AstShapeAnalyzer.ContainsYield(forEachStatement.Iterable))
                    {
                        entryIndex = Append(new StatementInstruction(nextIndex, forEachStatement));
                        return true;
                    }

                    return Emitters.ForOfEmitter.TryEmitForOf(
                        GetEmitContext(), forEachStatement, nextIndex, activeLabel, out entryIndex);

                case ForEachStatement { Kind: ForEachKind.AwaitOf } forEachStatement
                    when IsSimpleForOfBinding(forEachStatement):
                    // If binding target has yields anywhere (defaults or assignment target expressions),
                    // wrap as StatementInstruction. Same reasoning as for regular for-of loops above.
                    if (BindingTargetContainsYieldAnywhere(forEachStatement.Target) &&
                        !AstShapeAnalyzer.StatementContainsYield(forEachStatement.Body) &&
                        !AstShapeAnalyzer.ContainsYield(forEachStatement.Iterable))
                    {
                        entryIndex = Append(new StatementInstruction(nextIndex, forEachStatement));
                        return true;
                    }

                    return Emitters.ForOfEmitter.TryEmitForAwaitOf(
                        GetEmitContext(), forEachStatement, nextIndex, activeLabel, out entryIndex);

                case ReturnStatement returnStatement:
                    // First check for yield return
                    if (returnStatement.Expression is YieldExpression yieldReturn &&
                        Emitters.YieldEmitter.TryEmitReturnWithYield(GetEmitContext(), yieldReturn, out entryIndex))
                    {
                        return true;
                    }
                    return Emitters.DeclarationEmitter.TryEmitReturn(
                        GetEmitContext(), returnStatement, nextIndex, out entryIndex);

                case BreakStatement breakStatement:
                    return Emitters.ControlFlowEmitter.TryEmitBreak(
                        GetEmitContext(), breakStatement, out entryIndex);

                case ContinueStatement continueStatement:
                    return Emitters.ControlFlowEmitter.TryEmitContinue(
                        GetEmitContext(), continueStatement, out entryIndex);

                case WithStatement withStatement:
                    // Yield is not allowed in the object expression
                    if (AstShapeAnalyzer.ContainsYield(withStatement.Object))
                    {
                        entryIndex = -1;
                        _failureReason ??= "With statement object expression contains unsupported yield shape.";
                        return false;
                    }

                    // If the body contains yield, we need to use EnterWith/LeaveWith instructions
                    if (AstShapeAnalyzer.StatementContainsYield(withStatement.Body))
                    {
                        return Emitters.WithEmitter.TryEmitWith(
                            GetEmitContext(), withStatement, nextIndex, activeLabel, out entryIndex);
                    }

                    // If no yield in body, emit as a simple statement instruction
                    entryIndex = Append(new StatementInstruction(nextIndex, withStatement));
                    return true;

                case ClassDeclaration classDeclaration:
                    return Emitters.DeclarationEmitter.TryEmitClassDeclaration(
                        GetEmitContext(), classDeclaration, nextIndex, out entryIndex);

                case ThrowStatement throwStatement:
                    return Emitters.DeclarationEmitter.TryEmitThrow(
                        GetEmitContext(), throwStatement, out entryIndex);

                case LabeledStatement labeled:
                    // For loop-like statements, pass the label through - they handle it internally
                    if (labeled.Statement is WhileStatement or DoWhileStatement or ForStatement
                        or ForEachStatement or SwitchStatement)
                    {
                        statement = labeled.Statement;
                        activeLabel = labeled.Label;
                        continue;
                    }

                    // For non-loop statements (like blocks), wrap with LoopEnter/LoopExit
                    // to provide break targets for labeled break statements
                    return Emitters.ControlFlowEmitter.TryEmitLabeledNonLoop(
                        GetEmitContext(), labeled, nextIndex, out entryIndex);

                default:
                    entryIndex = -1;
                    _failureReason ??= $"Unsupported statement '{statement.GetType().Name}'.";
                    return false;
            }
        }
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

    private static bool IsSimpleForOfBinding(ForEachStatement statement)
    {
        // We now allow identifier or destructuring targets for all declaration kinds.
        return statement.Target is not null;
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

    /// <summary>
    /// Checks if a binding target contains yields anywhere - either in default values
    /// or in assignment target expressions (like [ {}[ yield ] ]).
    /// This is used to determine when to wrap declarations in StatementInstruction.
    /// </summary>
    private static bool BindingTargetContainsYieldAnywhere(BindingTarget target)
    {
        switch (target)
        {
            case ArrayBinding arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    // Check for yields in default values
                    if (element.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(element.DefaultValue))
                    {
                        return true;
                    }

                    // Recursively check nested bindings
                    if (element.Target is not null && BindingTargetContainsYieldAnywhere(element.Target))
                    {
                        return true;
                    }
                }

                if (arrayBinding.RestElement is not null &&
                    BindingTargetContainsYieldAnywhere(arrayBinding.RestElement))
                {
                    return true;
                }

                return false;

            case ObjectBinding objectBinding:
                foreach (var prop in objectBinding.Properties)
                {
                    // Check for yields in default values
                    if (prop.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(prop.DefaultValue))
                    {
                        return true;
                    }

                    // Check for yields in computed property names
                    if (prop.NameExpression is not null && AstShapeAnalyzer.ContainsYield(prop.NameExpression))
                    {
                        return true;
                    }

                    // Recursively check nested bindings
                    if (BindingTargetContainsYieldAnywhere(prop.Target))
                    {
                        return true;
                    }
                }

                if (objectBinding.RestElement is not null &&
                    BindingTargetContainsYieldAnywhere(objectBinding.RestElement))
                {
                    return true;
                }

                return false;

            case AssignmentTargetBinding assignmentTarget:
                // Check if the assignment target expression contains a yield
                return AstShapeAnalyzer.ContainsYield(assignmentTarget.Expression);

            default:
                // IdentifierBinding doesn't have expressions or default values
                return false;
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

        return clause.Body with { Statements = builder.ToImmutableArray() };
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
        switch (statement)
        {
            case TryStatement tryStmt:
                // Check the try block (not in finally yet)
                if (ContainsUnlabeledAbruptInFinallyImpl(tryStmt.TryBlock, inFinally))
                    return true;

                // Check the catch block if present
                if (tryStmt.Catch is not null &&
                    ContainsUnlabeledAbruptInFinallyImpl(tryStmt.Catch.Body, inFinally))
                    return true;

                // Check the finally block - now we're in a finally context
                if (tryStmt.Finally is not null &&
                    ContainsUnlabeledAbruptInFinallyImpl(tryStmt.Finally, true))
                    return true;

                return false;

            case BreakStatement { Label: null }:
            case ContinueStatement { Label: null }:
                // Unlabeled break/continue inside a finally targeting outer switch
                return inFinally;

            case BlockStatement block:
                foreach (var stmt in block.Statements)
                {
                    if (ContainsUnlabeledAbruptInFinallyImpl(stmt, inFinally))
                        return true;
                }
                return false;

            case IfStatement ifStmt:
                if (ContainsUnlabeledAbruptInFinallyImpl(ifStmt.Then, inFinally))
                    return true;
                if (ifStmt.Else is not null &&
                    ContainsUnlabeledAbruptInFinallyImpl(ifStmt.Else, inFinally))
                    return true;
                return false;

            case WhileStatement whileStmt:
                // Break/continue inside a loop targets the loop, not outer switch
                // So we reset inFinally context for the loop body
                return ContainsUnlabeledAbruptInFinallyImpl(whileStmt.Body, false);

            case DoWhileStatement doWhileStmt:
                return ContainsUnlabeledAbruptInFinallyImpl(doWhileStmt.Body, false);

            case ForStatement forStmt:
                return ContainsUnlabeledAbruptInFinallyImpl(forStmt.Body, false);

            case ForEachStatement forEachStmt:
                return ContainsUnlabeledAbruptInFinallyImpl(forEachStmt.Body, false);

            case SwitchStatement switchStmt:
                // Break inside a nested switch targets that switch, not outer one
                // But we still need to check for try-finally patterns
                foreach (var switchCase in switchStmt.Cases)
                {
                    if (ContainsUnlabeledAbruptInFinallyImpl(switchCase.Body, false))
                        return true;
                }
                return false;

            case LabeledStatement labeledStmt:
                return ContainsUnlabeledAbruptInFinallyImpl(labeledStmt.Statement, inFinally);

            case WithStatement withStmt:
                return ContainsUnlabeledAbruptInFinallyImpl(withStmt.Body, inFinally);

            default:
                // Other statements (return, throw, expression, var, etc.) don't contain nested abrupt
                return false;
        }
    }
}
