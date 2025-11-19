using System.Collections.Generic;
using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Builds generator IR for a subset of JavaScript constructs. The builder currently supports linear statement lists,
/// blocks, expression statements, variable declarations, simple returns, and top-level <c>yield</c> expressions.
/// More complex control flow (if/loops/try/yield inside expressions) is detected and reported as unsupported so the
/// engine can fall back to the legacy replay runner.
/// </summary>
internal sealed class GeneratorIrBuilder
{
    private readonly List<GeneratorInstruction> _instructions = [];
    private readonly Stack<LoopScope> _loopScopes = new();
    private int _resumeSlotCounter;
    private int _catchSlotCounter;
    private int _yieldStarStateCounter;
    private const string ResumeSlotPrefix = "\u0001_resume";
    private const string CatchSlotPrefix = "\u0001_catch";
    private const string YieldStarStatePrefix = "\u0001_yieldstar";
    private static readonly LiteralExpression TrueLiteralExpression = new(null, true);
    private string? _failureReason;

    private readonly record struct LoopScope(Symbol? Label, int ContinueTarget, int BreakTarget);

    private GeneratorIrBuilder()
    {
    }

    private bool TryBuildIfWithYieldCondition(IfStatement statement, YieldExpression yieldExpression, int nextIndex,
        out int entryIndex, Symbol? activeLabel)
    {
        // Only handle non-delegated `yield` used directly as the condition,
        // and reject nested `yield` inside the yielded expression for now.
        if (yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression))
        {
            entryIndex = -1;
            return false;
        }

        var instructionStart = _instructions.Count;

        var resumeSymbol = CreateResumeSlotSymbol();
        var conditionIdentifier = new IdentifierExpression(yieldExpression.Source, resumeSymbol);
        var rewrittenIf = new IfStatement(statement.Source, conditionIdentifier, statement.Then, statement.Else);

        // When control reaches the rewritten if, we need the pending resume
        // value in the slot that backs the condition.
        var ifEntryNext = nextIndex;
        var ifBuilt = TryBuildIfStatement(rewrittenIf, ifEntryNext, out var ifEntryIndex, activeLabel);
        if (!ifBuilt)
        {
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        // Prefix the if with a yield sequence that:
        //   - yields the original expression (if any), and
        //   - stores the resume payload into the resume slot.
        entryIndex = AppendYieldSequence(yieldExpression.Expression, ifEntryIndex, resumeSymbol);
        return true;
    }

    private bool TryBuildReturnWithYield(ReturnStatement statement, YieldExpression yieldExpression, int nextIndex,
        out int entryIndex)
    {
        // Only handle non-delegated yield used directly as the return expression,
        // and reject nested yield inside the yielded expression for now.
        if (yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression))
        {
            entryIndex = -1;
            return false;
        }

        var resumeSymbol = CreateResumeSlotSymbol();
        var returnExpression = new IdentifierExpression(yieldExpression.Source, resumeSymbol);

        // Build a return that uses the resume slot value, then prefix it with
        // a yield sequence that captures the resume payload into that slot.
        var returnIndex = Append(new ReturnInstruction(returnExpression));
        entryIndex = AppendYieldSequence(yieldExpression.Expression, returnIndex, resumeSymbol);
        return true;
    }

    public static bool TryBuild(FunctionExpression function, out GeneratorPlan plan)
    {
        var builder = new GeneratorIrBuilder();
        var succeeded = builder.TryBuildInternal(function, out plan);
        GeneratorIrDiagnostics.ReportResult(function, succeeded, builder._failureReason);
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

    private bool TryBuildStatement(StatementNode statement, int nextIndex, out int entryIndex, Symbol? activeLabel = null)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    return TryBuildStatementList(block.Statements, nextIndex, out entryIndex);

                case IfStatement ifStatement:
                    // Handle simple `if (yield <expr>)` by rewriting the condition
                    // to use a resume slot fed by a dedicated yield sequence.
                    if (ifStatement.Condition is YieldExpression yieldCondition)
                    {
                        if (TryBuildIfWithYieldCondition(ifStatement, yieldCondition, nextIndex, out entryIndex, activeLabel))
                        {
                            return true;
                        }
                    }

                    return TryBuildIfStatement(ifStatement, nextIndex, out entryIndex, activeLabel);

                case EmptyStatement:
                    entryIndex = nextIndex;
                    return true;

                case ExpressionStatement { Expression: YieldExpression yieldExpression }:
                    if (yieldExpression.IsDelegated)
                    {
                        if (ContainsYield(yieldExpression.Expression))
                        {
                            entryIndex = -1;
                            return false;
                        }

                        entryIndex = AppendYieldStarSequence(yieldExpression, nextIndex, resultSlot: null);
                        return true;
                    }

                    if (ContainsYield(yieldExpression.Expression))
                    {
                        entryIndex = -1;
                        return false;
                    }

                    entryIndex = AppendYieldSequence(yieldExpression.Expression, nextIndex, resumeSlot: null);
                    return true;

                case ExpressionStatement expressionStatement:
                    if (TryLowerYieldingAssignment(expressionStatement, nextIndex, out entryIndex))
                    {
                        return true;
                    }

                    if (ContainsYield(expressionStatement.Expression))
                    {
                        entryIndex = -1;
                        return false;
                    }

                    entryIndex = Append(new StatementInstruction(nextIndex, expressionStatement));
                    return true;

                case VariableDeclaration declaration:
                    if (TryLowerYieldingDeclaration(declaration, nextIndex, out entryIndex))
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
                    return TryBuildWhileStatement(whileStatement, nextIndex, out entryIndex, activeLabel);

                case DoWhileStatement doWhileStatement:
                    return TryBuildDoWhileStatement(doWhileStatement, nextIndex, out entryIndex, activeLabel);

                case ForStatement forStatement:
                    return TryBuildForStatement(forStatement, nextIndex, out entryIndex, activeLabel);

                case TryStatement tryStatement:
                    return TryBuildTryStatement(tryStatement, nextIndex, out entryIndex, activeLabel);

                case ForEachStatement forEachStatement when (forEachStatement.Kind == ForEachKind.Of || forEachStatement.Kind == ForEachKind.AwaitOf) && IsSimpleForOfBinding(forEachStatement):
                    // For-of with block-scoped declarations (`let`/`const`) and closures
                    // requires per-iteration environments. The replay engine already
                    // models this correctly, so we currently fall back instead of
                    // hosting these loops on the IR path.
                    if (forEachStatement.DeclarationKind is VariableKind.Let or VariableKind.Const)
                    {
                        entryIndex = -1;
                        _failureReason ??= "for...of with block-scoped bindings falls back to replay.";
                        return false;
                    }

                    return forEachStatement.Kind == ForEachKind.AwaitOf
                        ? TryBuildForAwaitStatement(forEachStatement, nextIndex, out entryIndex, activeLabel)
                        : TryBuildForOfStatement(forEachStatement, nextIndex, out entryIndex, activeLabel);

                case ReturnStatement returnStatement:
                    if (returnStatement.Expression is YieldExpression yieldReturn &&
                        TryBuildReturnWithYield(returnStatement, yieldReturn, nextIndex, out entryIndex))
                    {
                        return true;
                    }

                    if (returnStatement.Expression is not null && ContainsYield(returnStatement.Expression))
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

                case LabeledStatement labeled:
                    statement = labeled.Statement;
                    activeLabel = labeled.Label;
                    continue;

                default:
                    entryIndex = -1;
                    _failureReason ??= $"Unsupported statement '{statement.GetType().Name}'.";
                    return false;
            }

            break;
        }
    }

    private bool TryBuildIfStatement(IfStatement statement, int nextIndex, out int entryIndex, Symbol? activeLabel)
    {
        if (ContainsYield(statement.Condition))
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

    private bool TryBuildWhileStatement(WhileStatement statement, int nextIndex, out int entryIndex, Symbol? label)
    {
        if (ContainsYield(statement.Condition))
        {
            entryIndex = -1;
            _failureReason ??= "While condition contains unsupported yield shape.";
            return false;
        }

        var instructionStart = _instructions.Count;
        var jumpIndex = Append(new JumpInstruction(-1));

        var continueTarget = jumpIndex;
        var breakTarget = nextIndex;
        var scope = new LoopScope(label, continueTarget, breakTarget);
        _loopScopes.Push(scope);

        var bodyBuilt = TryBuildStatement(statement.Body, jumpIndex, out var bodyEntry);
        _loopScopes.Pop();

        if (!bodyBuilt)
        {
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        var branchIndex = Append(new BranchInstruction(statement.Condition, bodyEntry, nextIndex));
        _instructions[jumpIndex] = new JumpInstruction(branchIndex);
        entryIndex = branchIndex;
        return true;
    }

    private bool TryBuildDoWhileStatement(DoWhileStatement statement, int nextIndex, out int entryIndex, Symbol? label)
    {
        if (ContainsYield(statement.Condition))
        {
            entryIndex = -1;
            _failureReason ??= "Do/while condition contains unsupported yield shape.";
            return false;
        }

        var instructionStart = _instructions.Count;
        var conditionJumpIndex = Append(new JumpInstruction(-1));

        var continueTarget = conditionJumpIndex;
        var breakTarget = nextIndex;
        var scope = new LoopScope(label, continueTarget, breakTarget);
        _loopScopes.Push(scope);
        var bodyBuilt = TryBuildStatement(statement.Body, conditionJumpIndex, out var bodyEntry, label);
        _loopScopes.Pop();

        if (!bodyBuilt)
        {
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        var branchIndex = Append(new BranchInstruction(statement.Condition, bodyEntry, nextIndex));
        _instructions[conditionJumpIndex] = new JumpInstruction(branchIndex);

        entryIndex = bodyEntry;
        return true;
    }

    private bool TryBuildForStatement(ForStatement statement, int nextIndex, out int entryIndex, Symbol? label)
    {
        var instructionStart = _instructions.Count;

        var conditionExpression = statement.Condition ?? TrueLiteralExpression;
        if (ContainsYield(conditionExpression))
        {
            entryIndex = -1;
            _failureReason ??= "For condition contains unsupported yield shape.";
            return false;
        }

        if (statement.Increment is not null && ContainsYield(statement.Increment))
        {
            entryIndex = -1;
            _failureReason ??= "For increment contains unsupported yield shape.";
            return false;
        }

        var conditionJumpIndex = Append(new JumpInstruction(-1));
        var continueTarget = conditionJumpIndex;
        var breakTarget = nextIndex;

        if (statement.Increment is not null)
        {
            var incrementStatement = new ExpressionStatement(statement.Increment.Source, statement.Increment);
            continueTarget = Append(new StatementInstruction(conditionJumpIndex, incrementStatement));
        }

        var scope = new LoopScope(label, continueTarget, breakTarget);
        _loopScopes.Push(scope);
        var bodyBuilt = TryBuildStatement(statement.Body, continueTarget, out var bodyEntry, label);
        _loopScopes.Pop();

        if (!bodyBuilt)
        {
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        var branchIndex = Append(new BranchInstruction(conditionExpression, bodyEntry, nextIndex));
        _instructions[conditionJumpIndex] = new JumpInstruction(branchIndex);

        var loopEntry = bodyEntry;
        if (statement.Initializer is not null)
        {
            if (!TryBuildStatement(statement.Initializer, loopEntry, out loopEntry))
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

        int catchEntry = -1;
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

    private static bool ContainsTryStatement(StatementNode statement)
    {
        while (true)
        {
            switch (statement)
            {
                case TryStatement:
                    return true;
                case BlockStatement block:
                    foreach (var s in block.Statements)
                    {
                        if (ContainsTryStatement(s))
                        {
                            return true;
                        }
                    }

                    break;
                case IfStatement ifStatement:
                    if (ContainsTryStatement(ifStatement.Then))
                    {
                        return true;
                    }

                    if (ifStatement.Else is not null && ContainsTryStatement(ifStatement.Else))
                    {
                        return true;
                    }

                    break;
                case WhileStatement whileStatement:
                    statement = whileStatement.Body;
                    continue;
                case DoWhileStatement doWhileStatement:
                    statement = doWhileStatement.Body;
                    continue;
                case ForStatement forStatement:
                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    statement = forEachStatement.Body;
                    continue;
                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var c in switchStatement.Cases)
                    {
                        if (ContainsTryStatement(c.Body))
                        {
                            return true;
                        }
                    }

                    break;
            }

            return false;
            break;
        }
    }

    private bool TryBuildForOfStatement(ForEachStatement statement, int nextIndex, out int entryIndex, Symbol? label)
    {
        if (ContainsYield(statement.Iterable))
        {
            entryIndex = -1;
            return false;
        }

        if (!IsSimpleForOfBinding(statement))
        {
            entryIndex = -1;
            return false;
        }

        var instructionStart = _instructions.Count;
        var iteratorSymbol = Symbol.Intern($"__forOf_iter_{instructionStart}");
        var valueSymbol = Symbol.Intern($"__forOf_value_{instructionStart}");

        var moveNextIndex = Append(new ForOfMoveNextInstruction(iteratorSymbol, valueSymbol, nextIndex, -1));
        var perIterationBlock = CreateForOfIterationBlock(statement, valueSymbol);

        var scope = new LoopScope(label, moveNextIndex, nextIndex);
        _loopScopes.Push(scope);
        var bodyBuilt = TryBuildStatement(perIterationBlock, moveNextIndex, out var iterationEntry, label);
        _loopScopes.Pop();

        if (!bodyBuilt)
        {
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        _instructions[moveNextIndex] = ((ForOfMoveNextInstruction)_instructions[moveNextIndex]) with { Next = iterationEntry };

        var initIndex = Append(new ForOfInitInstruction(statement.Iterable, iteratorSymbol, moveNextIndex));
        entryIndex = initIndex;
        return true;
    }

    private bool TryBuildForAwaitStatement(ForEachStatement statement, int nextIndex, out int entryIndex,
        Symbol? label)
    {
        if (ContainsYield(statement.Iterable))
        {
            entryIndex = -1;
            return false;
        }

        var instructionStart = _instructions.Count;
        var iteratorSymbol = Symbol.Intern($"__forAwait_iter_{instructionStart}");
        var valueSymbol = Symbol.Intern($"__forAwait_value_{instructionStart}");

        var moveNextIndex = Append(new ForAwaitMoveNextInstruction(iteratorSymbol, valueSymbol, nextIndex, -1));
        var perIterationBlock = CreateForOfIterationBlock(statement, valueSymbol);

        var scope = new LoopScope(label, moveNextIndex, nextIndex);
        _loopScopes.Push(scope);
        var bodyBuilt = TryBuildStatement(perIterationBlock, moveNextIndex, out var iterationEntry, label);
        _loopScopes.Pop();

        if (!bodyBuilt)
        {
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        _instructions[moveNextIndex] = ((ForAwaitMoveNextInstruction)_instructions[moveNextIndex]) with { Next = iterationEntry };

        var initIndex = Append(new ForAwaitInitInstruction(statement.Iterable, iteratorSymbol, moveNextIndex));
        entryIndex = initIndex;
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

    private bool TryLowerYieldingDeclaration(VariableDeclaration declaration, int nextIndex, out int entryIndex)
    {
        if (declaration.Declarators.Length != 1)
        {
            entryIndex = -1;
            return false;
        }

        var declarator = declaration.Declarators[0];
        if (declarator.Initializer is not YieldExpression yieldExpression)
        {
            entryIndex = -1;
            return false;
        }

        if (ContainsYield(yieldExpression.Expression))
        {
            entryIndex = -1;
            return false;
        }

        if (declarator.Target is not IdentifierBinding)
        {
            entryIndex = -1;
            return false;
        }

        var resumeSymbol = CreateResumeSlotSymbol();
        var rewrittenDeclarator = declarator with
        {
            Initializer = new IdentifierExpression(yieldExpression.Source, resumeSymbol)
        };
        var rewrittenDeclaration = declaration with
        {
            Declarators = [rewrittenDeclarator]
        };

        var declarationIndex = Append(new StatementInstruction(nextIndex, rewrittenDeclaration));
        entryIndex = yieldExpression.IsDelegated
            ? AppendYieldStarSequence(yieldExpression, declarationIndex, resumeSymbol)
            : AppendYieldSequence(yieldExpression.Expression, declarationIndex, resumeSymbol);
        return true;
    }

    private bool TryLowerYieldingAssignment(ExpressionStatement statement, int nextIndex, out int entryIndex)
    {
        if (statement.Expression is not AssignmentExpression assignment ||
            assignment.Value is not YieldExpression yieldExpression)
        {
            entryIndex = -1;
            return false;
        }

        if (ContainsYield(yieldExpression.Expression))
        {
            entryIndex = -1;
            return false;
        }

        var resumeSymbol = CreateResumeSlotSymbol();
        var rewrittenAssignment = assignment with
        {
            Value = new IdentifierExpression(yieldExpression.Source, resumeSymbol)
        };
        var rewrittenStatement = statement with
        {
            Expression = rewrittenAssignment
        };

        var assignmentIndex = Append(new StatementInstruction(nextIndex, rewrittenStatement));
        entryIndex = yieldExpression.IsDelegated
            ? AppendYieldStarSequence(yieldExpression, assignmentIndex, resumeSymbol)
            : AppendYieldSequence(yieldExpression.Expression, assignmentIndex, resumeSymbol);
        return true;
    }

    private static bool DeclarationContainsYield(VariableDeclaration declaration)
    {
        foreach (var declarator in declaration.Declarators)
        {
            if (declarator.Initializer is not null && ContainsYield(declarator.Initializer))
            {
                return true;
            }
        }

        return false;
    }

    private Symbol CreateResumeSlotSymbol()
    {
        var symbolName = $"{ResumeSlotPrefix}{_resumeSlotCounter++}";
        return Symbol.Intern(symbolName);
    }

    private static bool IsSimpleForOfBinding(ForEachStatement statement)
    {
        // We now allow identifier or destructuring targets for all declaration kinds.
        return statement.Target is not null;
    }

    private static StatementNode CreateForOfIterationBlock(ForEachStatement statement, Symbol valueSymbol)
    {
        var valueExpression = new IdentifierExpression(statement.Source, valueSymbol);
        StatementNode bindingStatement;

        if (statement.DeclarationKind is null)
        {
            bindingStatement = new ExpressionStatement(statement.Source,
                CreateAssignmentExpression(statement.Target, valueExpression));
        }
        else
        {
            var declarator = new VariableDeclarator(statement.Source, statement.Target, valueExpression);
            bindingStatement = new VariableDeclaration(statement.Source, statement.DeclarationKind.Value,
                [declarator]);
        }

        ImmutableArray<StatementNode> bodyStatements;
        var isStrict = false;
        if (statement.Body is BlockStatement block)
        {
            var builder = ImmutableArray.CreateBuilder<StatementNode>(block.Statements.Length + 1);
            builder.Add(bindingStatement);
            builder.AddRange(block.Statements);
            bodyStatements = builder.ToImmutable();
            isStrict = block.IsStrict;
        }
        else
        {
            bodyStatements = [bindingStatement, statement.Body];
        }

        return new BlockStatement(statement.Source, bodyStatements, isStrict);
    }

    private static ExpressionNode CreateAssignmentExpression(BindingTarget target, ExpressionNode valueExpression)
    {
        return target switch
        {
            IdentifierBinding identifier => new AssignmentExpression(target.Source, identifier.Name, valueExpression),
            ArrayBinding or ObjectBinding => new DestructuringAssignmentExpression(target.Source, target, valueExpression),
            _ => throw new NotSupportedException($"Unsupported for-of binding target '{target.GetType().Name}'.")
        };
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
            new IdentifierBinding(clause.Source, clause.Binding),
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

    private static bool ContainsYield(ExpressionNode? expression)
    {
        while (true)
        {
            switch (expression)
            {
                case null:
                    return false;
                case YieldExpression:
                    return true;
                case BinaryExpression binary:
                    return ContainsYield(binary.Left) || ContainsYield(binary.Right);
                case ConditionalExpression conditional:
                    return ContainsYield(conditional.Test) || ContainsYield(conditional.Consequent) || ContainsYield(conditional.Alternate);
                case CallExpression call:
                    if (ContainsYield(call.Callee))
                    {
                        return true;
                    }

                    foreach (var argument in call.Arguments)
                    {
                        if (ContainsYield(argument.Expression))
                        {
                            return true;
                        }
                    }

                    return false;
                case NewExpression @new:
                    if (ContainsYield(@new.Constructor))
                    {
                        return true;
                    }

                    foreach (var argument in @new.Arguments)
                    {
                        if (ContainsYield(argument))
                        {
                            return true;
                        }
                    }

                    return false;
                case MemberExpression member:
                    return ContainsYield(member.Target) || ContainsYield(member.Property);
                case AssignmentExpression assignment:
                    expression = assignment.Value;
                    continue;
                case PropertyAssignmentExpression propertyAssignment:
                    return ContainsYield(propertyAssignment.Target) || ContainsYield(propertyAssignment.Property) || ContainsYield(propertyAssignment.Value);
                case IndexAssignmentExpression indexAssignment:
                    return ContainsYield(indexAssignment.Target) || ContainsYield(indexAssignment.Index) || ContainsYield(indexAssignment.Value);
                case SequenceExpression sequence:
                    return ContainsYield(sequence.Left) || ContainsYield(sequence.Right);
                case UnaryExpression unary:
                    expression = unary.Operand;
                    continue;
                case ArrayExpression array:
                    foreach (var element in array.Elements)
                    {
                        if (element.Expression is not null && ContainsYield(element.Expression))
                        {
                            return true;
                        }
                    }

                    return false;
                case ObjectExpression obj:
                    foreach (var member in obj.Members)
                    {
                        if (member.Value is not null && ContainsYield(member.Value))
                        {
                            return true;
                        }
                    }

                    return false;
                case FunctionExpression:
                case ClassExpression:
                    // Nested functions/classes can capture yield expressions that should be handled within their own scope.
                    return false;
                default:
                    return false;
            }

            break;
        }
    }

    private int Append(GeneratorInstruction instruction)
    {
        var index = _instructions.Count;
        _instructions.Add(instruction);
        return index;
    }
}
