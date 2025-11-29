using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Builds generator IR for a subset of JavaScript constructs. The builder currently supports linear statement lists,
///     blocks, expression statements, variable declarations, simple returns, and top-level <c>yield</c> expressions.
///     More complex control flow (if/loops/try/yield inside expressions) is detected and reported as unsupported so the
///     engine can fall back to the legacy replay runner.
/// </summary>
/// <summary>
///     Builds generator IR plans for synchronous generator functions. Async generators are not
///     implemented yet; async <c>function*</c> bodies always fall back to the replay engine.
/// </summary>
internal sealed class SyncGeneratorIrBuilder
{
    private const string ResumeSlotPrefix = "\u0001_resume";
    private const string CatchSlotPrefix = "\u0001_catch";
    private const string YieldStarStatePrefix = "\u0001_yieldstar";
    private readonly List<GeneratorInstruction> _instructions = [];
    private readonly Stack<LoopScope> _loopScopes = new();
    private int _catchSlotCounter;
    private string? _failureReason;
    private int _resumeSlotCounter;
    private int _yieldStarStateCounter;

    private SyncGeneratorIrBuilder()
    {
    }

    private bool TryBuildReturnWithYield(ReturnStatement statement, YieldExpression yieldExpression, int nextIndex,
        out int entryIndex)
    {
        // Only handle simple `yield` / `yield*` used directly as the return
        // expression, and reject nested `yield` inside the yielded expression
        // for now.
        if (AstShapeAnalyzer.ContainsYield(yieldExpression.Expression))
        {
            entryIndex = -1;
            return false;
        }

        var resumeSymbol = CreateResumeSlotSymbol();
        var returnExpression = new IdentifierExpression(yieldExpression.Source, resumeSymbol);

        // Build a return that uses the resume slot value, then prefix it with
        // either:
        //   - a simple yield sequence that captures the resume payload into
        //     that slot (`return yield <expr>;`), or
        //   - a delegated yield* sequence that stores the delegate's final
        //     completion value into the slot (`return yield* <expr>;`).
        var returnIndex = Append(new ReturnInstruction(returnExpression));
        entryIndex = yieldExpression.IsDelegated
            ? AppendYieldStarSequence(yieldExpression, returnIndex, resumeSymbol)
            : AppendYieldSequence(yieldExpression.Expression, returnIndex, resumeSymbol);
        return true;
    }

    public static bool TryBuild(FunctionExpression function, out GeneratorPlan plan, out string? failureReason)
    {
        // First run the generator yield-lowering pre-pass so that SyncGeneratorIrBuilder
        // can assume a simplified, generator-friendly AST. The lowerer currently acts
        // as a no-op scaffold; yield normalization logic will be migrated here
        // incrementally.
        if (!GeneratorYieldLowerer.TryLowerToGeneratorFriendlyAst(function, out var lowered, out var lowerFailure))
        {
            plan = default!;
            failureReason = lowerFailure;
            return false;
        }

        var builder = new SyncGeneratorIrBuilder();
        var succeeded = builder.TryBuildInternal(lowered, out plan);
        failureReason = builder._failureReason ?? lowerFailure;
        return succeeded;
    }

    private bool TryBuildInternal(FunctionExpression function, out GeneratorPlan plan)
    {
        // Always append an implicit "return undefined" instruction. Statement lists fall through to this index.
        var implicitReturnIndex = Append(new ReturnInstruction(null));
        if (!TryBuildStatementList(function.Body.Statements, implicitReturnIndex, out var entryIndex))
        {
            plan = default!;
            _failureReason ??= "Statement list contains unsupported construct.";
            return false;
        }

        plan = new GeneratorPlan([.._instructions], entryIndex);
        return true;
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
                    return TryBuildStatementList(block.Statements, nextIndex, out entryIndex);

                case IfStatement ifStatement:
                    return TryBuildIfStatement(ifStatement, nextIndex, out entryIndex, activeLabel);

                case EmptyStatement:
                    entryIndex = nextIndex;
                    return true;

                case ExpressionStatement { Expression: YieldExpression yieldExpression }:
                    if (yieldExpression.IsDelegated)
                    {
                        if (AstShapeAnalyzer.ContainsYield(yieldExpression.Expression))
                        {
                            entryIndex = -1;
                            return false;
                        }

                        entryIndex = AppendYieldStarSequence(yieldExpression, nextIndex, null);
                        return true;
                    }

                    if (AstShapeAnalyzer.ContainsYield(yieldExpression.Expression))
                    {
                        entryIndex = -1;
                        return false;
                    }

                    entryIndex = AppendYieldSequence(yieldExpression.Expression, nextIndex, null);
                    return true;

                case ExpressionStatement expressionStatement:
                    if (expressionStatement.Expression is AssignmentExpression
                        {
                            Target: { } targetSymbol, Value: YieldExpression yieldAssignment
                        } &&
                        IsLowererTemp(targetSymbol) &&
                        !AstShapeAnalyzer.ContainsYield(yieldAssignment.Expression))
                    {
                        entryIndex = yieldAssignment.IsDelegated
                            ? AppendYieldStarSequence(yieldAssignment, nextIndex, targetSymbol)
                            : AppendYieldSequence(yieldAssignment.Expression, nextIndex, targetSymbol);
                        return true;
                    }

                    if (AstShapeAnalyzer.ContainsYield(expressionStatement.Expression))
                    {
                        entryIndex = -1;
                        return false;
                    }

                    entryIndex = Append(new StatementInstruction(nextIndex, expressionStatement));
                    return true;

                case VariableDeclaration declaration:
                    if (TryBuildVariableDeclaration(declaration, nextIndex, out entryIndex))
                    {
                        return true;
                    }

                    if (DeclarationContainsYield(declaration))
                    {
                        entryIndex = -1;
                        _failureReason ??= "Variable declaration contains unsupported yield shape.";
                        return false;
                    }

                    entryIndex = Append(new StatementInstruction(nextIndex, declaration));
                    return true;

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

                    return TryBuildLoopPlan(whilePlan, nextIndex, out entryIndex, activeLabel);

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

                    return TryBuildLoopPlan(doWhilePlan, nextIndex, out entryIndex, activeLabel);

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

                    return TryBuildLoopPlan(forPlan, nextIndex, out entryIndex, activeLabel);

                case SwitchStatement switchStatement:
                    if (TryBuildSwitchStatement(switchStatement, nextIndex, out entryIndex, activeLabel))
                    {
                        return true;
                    }

                    entryIndex = -1;
                    _failureReason ??= "Unsupported statement 'SwitchStatement'.";
                    return false;

                case TryStatement tryStatement:
                    return TryBuildTryStatement(tryStatement, nextIndex, out entryIndex, activeLabel);

                case ForEachStatement { Kind: ForEachKind.Of } forEachStatement
                    when IsSimpleForOfBinding(forEachStatement):
                    if (forEachStatement.DeclarationKind is VariableKind.Let or VariableKind.Const &&
                        !AstShapeAnalyzer.StatementContainsYield(forEachStatement.Body) &&
                        !AstShapeAnalyzer.ContainsYield(forEachStatement.Iterable))
                    {
                        entryIndex = Append(new StatementInstruction(nextIndex, forEachStatement));
                        return true;
                    }

                    return TryBuildForOfStatement(forEachStatement, nextIndex, out entryIndex, activeLabel);

                case ForEachStatement { Kind: ForEachKind.AwaitOf } forEachStatement
                    when IsSimpleForOfBinding(forEachStatement):
                    return TryBuildForAwaitStatement(forEachStatement, nextIndex, out entryIndex, activeLabel);

                case ReturnStatement returnStatement:
                    if (returnStatement.Expression is YieldExpression yieldReturn &&
                        TryBuildReturnWithYield(returnStatement, yieldReturn, nextIndex, out entryIndex))
                    {
                        return true;
                    }

                    if (returnStatement.Expression is not null &&
                        AstShapeAnalyzer.ContainsYield(returnStatement.Expression))
                    {
                        entryIndex = -1;
                        _failureReason ??= "Return expression contains unsupported yield shape.";
                        return false;
                    }

                    entryIndex = Append(new ReturnInstruction(returnStatement.Expression));
                    return true;

                case BreakStatement breakStatement:
                    return TryBuildBreak(breakStatement, out entryIndex);

                case ContinueStatement continueStatement:
                    return TryBuildContinue(continueStatement, out entryIndex);

                case ThrowStatement throwStatement:
                    if (throwStatement.Expression is not null &&
                        AstShapeAnalyzer.ContainsYield(throwStatement.Expression))
                    {
                        entryIndex = -1;
                        _failureReason ??= "Throw expression contains unsupported yield shape.";
                        return false;
                    }

                    entryIndex = Append(new StatementInstruction(nextIndex, throwStatement));
                    return true;

                case LabeledStatement labeled:
                    statement = labeled.Statement;
                    activeLabel = labeled.Label;
                    continue;

                default:
                    if (statement is ThrowStatement throwFallback)
                    {
                        entryIndex = Append(new StatementInstruction(nextIndex, throwFallback));
                        return true;
                    }

                    entryIndex = -1;
                    _failureReason ??= $"Unsupported statement '{statement.GetType().Name}'.";
                    return false;
            }
        }
    }

    private bool TryBuildVariableDeclaration(VariableDeclaration declaration, int nextIndex, out int entryIndex)
    {
        entryIndex = -1;

        if (declaration.Declarators.Length != 1 ||
            declaration.Declarators[0] is not { } declarator ||
            declarator.Target is not IdentifierBinding { Name: { } targetSymbol } ||
            declarator.Initializer is not YieldExpression yieldInitializer)
        {
            return false;
        }

        if (!IsLowererTemp(targetSymbol) || yieldInitializer.IsDelegated ||
            AstShapeAnalyzer.ContainsYield(yieldInitializer.Expression))
        {
            return false;
        }

        entryIndex = AppendYieldSequence(yieldInitializer.Expression, nextIndex, targetSymbol);
        return true;
    }

    private bool TryBuildIfStatement(IfStatement statement, int nextIndex, out int entryIndex, Symbol? activeLabel)
    {
        if (AstShapeAnalyzer.ContainsYield(statement.Condition))
        {
            entryIndex = -1;
            _failureReason ??= "If condition contains unsupported yield shape.";
            return false;
        }

        var instructionStart = _instructions.Count;

        var elseEntry = nextIndex;
        if (statement.Else is not null)
        {
            if (!TryBuildStatement(statement.Else, nextIndex, out elseEntry, activeLabel))
            {
                _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        if (!TryBuildStatement(statement.Then, nextIndex, out var thenEntry, activeLabel))
        {
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        var branchIndex = Append(new BranchInstruction(statement.Condition, thenEntry, elseEntry));
        entryIndex = branchIndex;
        return true;
    }

    private bool TryBuildLoopPlan(LoopPlan plan, int nextIndex, out int entryIndex, Symbol? label)
    {
        var instructionStart = _instructions.Count;

        var conditionJumpIndex = Append(new JumpInstruction(-1));
        var breakTarget = nextIndex;

        var postIterationEntry = conditionJumpIndex;
        if (!plan.PostIteration.IsDefaultOrEmpty)
        {
            if (!TryBuildStatementList(plan.PostIteration, conditionJumpIndex, out postIterationEntry))
            {
                _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        var continueTarget = postIterationEntry;
        var scope = new LoopScope(label, continueTarget, breakTarget);
        _loopScopes.Push(scope);

        if (!TryBuildStatement(plan.Body, continueTarget, out var bodyEntry, label))
        {
            _loopScopes.Pop();
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        _loopScopes.Pop();

        var branchIndex = Append(new BranchInstruction(plan.Condition, bodyEntry, nextIndex));

        var conditionEntry = branchIndex;
        if (!plan.ConditionPrologue.IsDefaultOrEmpty)
        {
            if (!TryBuildStatementList(plan.ConditionPrologue, branchIndex, out conditionEntry))
            {
                _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        _instructions[conditionJumpIndex] = new JumpInstruction(conditionEntry);

        var loopEntry = plan.ConditionAfterBody ? bodyEntry : conditionJumpIndex;

        if (!plan.LeadingStatements.IsDefaultOrEmpty)
        {
            if (!TryBuildStatementList(plan.LeadingStatements, loopEntry, out loopEntry))
            {
                _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        entryIndex = loopEntry;
        return true;
    }

    private bool TryBuildTryStatement(TryStatement statement, int nextIndex, out int entryIndex, Symbol? activeLabel)
    {
        var hasCatch = statement.Catch is not null;
        var hasFinally = statement.Finally is not null;
        if (!hasCatch && !hasFinally)
        {
            entryIndex = -1;
            return false;
        }

        var instructionStart = _instructions.Count;
        var exitIndex = nextIndex;

        var finallyEntry = -1;
        if (hasFinally && statement.Finally is not null)
        {
            var endFinallyIndex = Append(new EndFinallyInstruction(exitIndex));
            if (!TryBuildStatement(statement.Finally, endFinallyIndex, out finallyEntry, activeLabel))
            {
                _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        var leaveNext = exitIndex;
        var leaveTryIndex = Append(new LeaveTryInstruction(leaveNext));

        var catchEntry = -1;
        Symbol? catchSlotSymbol = null;
        if (hasCatch && statement.Catch is not null)
        {
            catchSlotSymbol = CreateCatchSlotSymbol();
            var catchBlock = BuildCatchBlock(statement.Catch, catchSlotSymbol);
            if (!TryBuildStatement(catchBlock, leaveTryIndex, out catchEntry, activeLabel))
            {
                _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        if (!TryBuildStatement(statement.TryBlock, leaveTryIndex, out var tryEntry, activeLabel))
        {
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        var enterTryIndex = Append(new EnterTryInstruction(tryEntry, catchEntry, catchSlotSymbol, finallyEntry));
        entryIndex = enterTryIndex;
        return true;
    }

    private bool TryBuildSwitchStatement(SwitchStatement statement, int nextIndex, out int entryIndex,
        Symbol? activeLabel)
    {
        // For now we support switch statements whose discriminant and case
        // tests are yield-free, and whose case bodies only contain at most a
        // single trailing unlabeled `break;` at top level. More complex break
        // shapes (including non-trailing `break`) continue to be rejected.
        if (AstShapeAnalyzer.ContainsYield(statement.Discriminant))
        {
            entryIndex = -1;
            return false;
        }

        foreach (var switchCase in statement.Cases)
        {
            if (switchCase.Test is not null && AstShapeAnalyzer.ContainsYield(switchCase.Test))
            {
                entryIndex = -1;
                return false;
            }
        }

        // Enforce at most a single default clause. JavaScript evaluates switch
        // by first selecting the matching case clause (preferring explicit case
        // tests and only using default if no case matches) and then executing
        // the case body with fallthrough. The default clause can appear in any
        // position; execution begins at the selected clause and falls through
        // to later clauses until a break is hit or the switch ends.
        var defaultIndex = -1;
        for (var i = 0; i < statement.Cases.Length; i++)
        {
            if (statement.Cases[i].Test is null)
            {
                if (defaultIndex != -1)
                {
                    entryIndex = -1;
                    _failureReason ??= "Switch statement contains multiple default clauses.";
                    return false;
                }

                defaultIndex = i;
            }
        }

        var instructionStart = _instructions.Count;
        var discriminantSymbol = Symbol.Intern($"__switch_disc_{instructionStart}");
        var matchIndexSymbol = Symbol.Intern($"__switch_match_{instructionStart}");
        var doneSymbol = Symbol.Intern($"__switch_done_{instructionStart}");

        var statements = ImmutableArray.CreateBuilder<StatementNode>();

        // const __discN = <discriminant>;
        var discBinding = new IdentifierBinding(statement.Source, discriminantSymbol);
        var discDeclarator = new VariableDeclarator(statement.Source, discBinding, statement.Discriminant);
        var discDeclaration = new VariableDeclaration(statement.Source, VariableKind.Const, [discDeclarator]);
        statements.Add(discDeclaration);

        // let __matchN = -1;
        var matchBinding = new IdentifierBinding(statement.Source, matchIndexSymbol);
        var matchInitializer = new LiteralExpression(statement.Source, -1);
        var matchDeclarator = new VariableDeclarator(statement.Source, matchBinding, matchInitializer);
        var matchDeclaration = new VariableDeclaration(statement.Source, VariableKind.Let, [matchDeclarator]);
        statements.Add(matchDeclaration);

        // let __doneN = false;
        var doneBinding = new IdentifierBinding(statement.Source, doneSymbol);
        var doneInitializer = new LiteralExpression(statement.Source, false);
        var doneDeclarator = new VariableDeclarator(statement.Source, doneBinding, doneInitializer);
        var doneDeclaration = new VariableDeclaration(statement.Source, VariableKind.Let, [doneDeclarator]);
        statements.Add(doneDeclaration);

        // Matching phase: set __matchN to the first matching case index.
        for (var i = 0; i < statement.Cases.Length; i++)
        {
            var switchCase = statement.Cases[i];
            if (switchCase.Test is null)
            {
                continue;
            }

            var matchUnset = new BinaryExpression(statement.Source, "===",
                new IdentifierExpression(statement.Source, matchIndexSymbol),
                new LiteralExpression(statement.Source, -1));
            var discIdentifier = new IdentifierExpression(statement.Source, discriminantSymbol);
            var equalTest = new BinaryExpression(statement.Source, "===",
                discIdentifier, switchCase.Test);
            var combinedTest = new BinaryExpression(statement.Source, "&&", matchUnset, equalTest);

            var setMatch = new AssignmentExpression(statement.Source, matchIndexSymbol,
                new LiteralExpression(statement.Source, i));
            var setMatchStatement = new ExpressionStatement(statement.Source, setMatch);
            statements.Add(new IfStatement(statement.Source, combinedTest,
                new BlockStatement(statement.Source, [setMatchStatement], statement.Cases[0].Body.IsStrict),
                null));
        }

        // If still unmatched, fall back to default (if any).
        if (defaultIndex != -1)
        {
            var stillUnmatched = new BinaryExpression(statement.Source, "===",
                new IdentifierExpression(statement.Source, matchIndexSymbol),
                new LiteralExpression(statement.Source, -1));
            var setDefaultMatch = new AssignmentExpression(statement.Source, matchIndexSymbol,
                new LiteralExpression(statement.Source, defaultIndex));
            var setDefaultStatement = new ExpressionStatement(statement.Source, setDefaultMatch);
            statements.Add(new IfStatement(statement.Source, stillUnmatched,
                new BlockStatement(statement.Source, [setDefaultStatement], statement.Cases[0].Body.IsStrict),
                null));
        }

        for (var caseIndex = 0; caseIndex < statement.Cases.Length; caseIndex++)
        {
            var switchCase = statement.Cases[caseIndex];
            var body = switchCase.Body;
            var bodyStatements = body.Statements;

            var breakIndex = -1;
            for (var i = 0; i < bodyStatements.Length; i++)
            {
                if (bodyStatements[i] is BreakStatement breakStatement)
                {
                    if (breakStatement.Label is not null &&
                        (activeLabel is null || !ReferenceEquals(activeLabel, breakStatement.Label)))
                    {
                        _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
                        entryIndex = -1;
                        return false;
                    }

                    breakIndex = breakIndex == -1 ? i : breakIndex;
                }
            }

            // Execution guard: if (!__done && __matchN != -1 && __matchN <= caseIndex) { ...body... }
            var notDoneExec = new UnaryExpression(statement.Source, "!",
                new IdentifierExpression(statement.Source, doneSymbol), true);
            var matchSet = new BinaryExpression(statement.Source, "!==",
                new IdentifierExpression(statement.Source, matchIndexSymbol),
                new LiteralExpression(statement.Source, -1));
            var matchReached = new BinaryExpression(statement.Source, "<=",
                new IdentifierExpression(statement.Source, matchIndexSymbol),
                new LiteralExpression(statement.Source, caseIndex));
            var matchGuard = new BinaryExpression(statement.Source, "&&", matchSet, matchReached);
            var execCondition = new BinaryExpression(statement.Source, "&&", notDoneExec, matchGuard);

            var execBuilder = ImmutableArray.CreateBuilder<StatementNode>();
            var copyCount = breakIndex == -1 ? bodyStatements.Length : breakIndex;
            for (var i = 0; i < copyCount; i++)
            {
                execBuilder.Add(bodyStatements[i]);
            }

            if (breakIndex != -1)
            {
                var setDoneAssignment = new AssignmentExpression(statement.Source, doneSymbol,
                    new LiteralExpression(statement.Source, true));
                execBuilder.Add(new ExpressionStatement(statement.Source, setDoneAssignment));
            }

            var execBlock = new BlockStatement(body.Source, execBuilder.ToImmutable(), body.IsStrict);
            statements.Add(new IfStatement(statement.Source, execCondition, execBlock, null));
        }

        var isStrict = statement.Cases.Length > 0 && statement.Cases[0].Body.IsStrict;
        var lowered = new BlockStatement(statement.Source, statements.ToImmutable(), isStrict);

        if (!TryBuildStatement(lowered, nextIndex, out entryIndex, activeLabel))
        {
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        return true;
    }

    private bool TryBuildForOfStatement(ForEachStatement statement, int nextIndex, out int entryIndex, Symbol? label)
    {
        return TryBuildIteratorPlan(statement, nextIndex, out entryIndex, label);
    }

    private bool TryBuildForAwaitStatement(ForEachStatement statement, int nextIndex, out int entryIndex,
        Symbol? label)
    {
        return TryBuildIteratorPlan(statement, nextIndex, out entryIndex, label);
    }

    private bool TryBuildIteratorPlan(ForEachStatement statement, int nextIndex, out int entryIndex,
        Symbol? label)
    {
        if (AstShapeAnalyzer.ContainsYield(statement.Iterable))
        {
            entryIndex = -1;
            return false;
        }

        var planBody = statement.Body is BlockStatement blockBody
            ? blockBody
            : new BlockStatement(statement.Source, [statement.Body], IsStrictBlock(statement.Body));
        var iteratorPlan = IteratorDriverFactory.CreatePlan(statement, planBody);

        var iteratorInstructions =
            IteratorInstructionTemplate.AppendInstructions(_instructions, iteratorPlan, nextIndex);

        var perIterationBlock = CreateIteratorIterationBlock(iteratorPlan, iteratorInstructions.ValueSlot);
        var scope = new LoopScope(label, iteratorInstructions.MoveNextIndex, nextIndex);
        _loopScopes.Push(scope);
        var bodyBuilt = TryBuildStatement(perIterationBlock, iteratorInstructions.MoveNextIndex, out var iterationEntry,
            label);
        _loopScopes.Pop();

        if (!bodyBuilt)
        {
            _instructions.RemoveRange(iteratorInstructions.InitIndex,
                _instructions.Count - iteratorInstructions.InitIndex);
            entryIndex = -1;
            return false;
        }

        IteratorInstructionTemplate.Wire(iteratorInstructions, iterationEntry, _instructions);

        entryIndex = iteratorInstructions.InitIndex;
        return true;
    }

    private bool TryBuildBreak(BreakStatement statement, out int entryIndex)
    {
        if (!TryResolveBreakTarget(statement.Label, out var target))
        {
            entryIndex = -1;
            return false;
        }

        entryIndex = Append(new BreakInstruction(target));
        return true;
    }

    private bool TryBuildContinue(ContinueStatement statement, out int entryIndex)
    {
        if (!TryResolveContinueTarget(statement.Label, out var target))
        {
            entryIndex = -1;
            return false;
        }

        entryIndex = Append(new ContinueInstruction(target));
        return true;
    }

    private static bool DeclarationContainsYield(VariableDeclaration declaration)
    {
        return declaration.Declarators.Any(d =>
            d.Initializer is not null &&
            AstShapeAnalyzer.ContainsYield(d.Initializer) &&
            !IsLowererTemp(d.Target));
    }

    private Symbol CreateResumeSlotSymbol()
    {
        var symbolName = $"{ResumeSlotPrefix}{_resumeSlotCounter++}";
        return Symbol.Intern(symbolName);
    }

    private static StatementNode CreateIteratorIterationBlock(IteratorDriverPlan plan, Symbol valueSymbol)
    {
        var valueExpression = new IdentifierExpression(plan.Body.Source, valueSymbol);
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

        ImmutableArray<StatementNode> bodyStatements;
        var isStrict = false;
        if (plan.Body is BlockStatement block)
        {
            var builder = ImmutableArray.CreateBuilder<StatementNode>(block.Statements.Length + 1);
            builder.Add(bindingStatement);
            builder.AddRange(block.Statements);
            bodyStatements = builder.ToImmutable();
            isStrict = block.IsStrict;
        }
        else
        {
            bodyStatements = [bindingStatement, plan.Body];
        }

        return new BlockStatement(plan.Body.Source, bodyStatements, isStrict);
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
            _ => throw new NotSupportedException($"Unsupported for-of binding target '{target.GetType().Name}'.")
        };
    }

    private static bool IsLowererTemp(BindingTarget target)
    {
        return target is IdentifierBinding { Name.Name: not null } identifier &&
               identifier.Name.Name.StartsWith("__yield_lower_", StringComparison.Ordinal);
    }

    private static bool IsLowererTemp(Symbol symbol)
    {
        return symbol.Name?.StartsWith("__yield_lower_", StringComparison.Ordinal) == true;
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
        var declarator = new VariableDeclarator(
            clause.Source,
            clause.Binding,
            new IdentifierExpression(clause.Source, catchSlotSymbol));
        var declaration = new VariableDeclaration(
            clause.Source,
            VariableKind.Let,
            [declarator]);

        var builder = ImmutableArray.CreateBuilder<StatementNode>();
        builder.Add(declaration);
        builder.AddRange(clause.Body.Statements);

        return clause.Body with { Statements = builder.ToImmutableArray() };
    }

    private bool TryResolveBreakTarget(Symbol? label, out int target)
    {
        if (_loopScopes.Count == 0)
        {
            target = -1;
            return false;
        }

        if (label is null)
        {
            target = _loopScopes.Peek().BreakTarget;
            return true;
        }

        foreach (var scope in _loopScopes)
        {
            if (scope.Label is not null && ReferenceEquals(scope.Label, label))
            {
                target = scope.BreakTarget;
                return true;
            }
        }

        target = -1;
        return false;
    }

    private bool TryResolveContinueTarget(Symbol? label, out int target)
    {
        if (_loopScopes.Count == 0)
        {
            target = -1;
            return false;
        }

        if (label is null)
        {
            target = _loopScopes.Peek().ContinueTarget;
            return true;
        }

        foreach (var scope in _loopScopes)
        {
            if (scope.Label is not null && ReferenceEquals(scope.Label, label))
            {
                target = scope.ContinueTarget;
                return true;
            }
        }

        target = -1;
        return false;
    }

    private int Append(GeneratorInstruction instruction)
    {
        var index = _instructions.Count;
        _instructions.Add(instruction);
        return index;
    }

    private readonly record struct LoopScope(Symbol? Label, int ContinueTarget, int BreakTarget);
}
