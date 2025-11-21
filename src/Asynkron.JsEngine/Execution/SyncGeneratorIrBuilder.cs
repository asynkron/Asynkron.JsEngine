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
/// <summary>
/// Builds generator IR plans for synchronous generator functions. Async generators are not
/// implemented yet; async <c>function*</c> bodies always fall back to the replay engine.
/// </summary>
internal sealed class SyncGeneratorIrBuilder
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

    private SyncGeneratorIrBuilder()
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

        var resumeSymbol = CreateResumeSlotSymbol();
        var conditionIdentifier = new IdentifierExpression(yieldExpression.Source, resumeSymbol);
        var rewrittenIf = new IfStatement(statement.Source, conditionIdentifier, statement.Then, statement.Else);

        return TryBuildIfWithRewrittenCondition(statement, yieldExpression, rewrittenIf, resumeSymbol, nextIndex,
            out entryIndex, activeLabel);
    }

    private bool TryBuildIfWithConditionYield(IfStatement statement, int nextIndex,
        out int entryIndex, Symbol? activeLabel)
    {
        // Fast-path: direct `if (yield ...)`.
        if (statement.Condition is YieldExpression directYield)
        {
            return TryBuildIfWithYieldCondition(statement, directYield, nextIndex, out entryIndex, activeLabel);
        }

        if (statement.Condition is null || !ContainsYield(statement.Condition))
        {
            entryIndex = -1;
            return false;
        }

        var resumeSymbol = CreateResumeSlotSymbol();
        if (!TryRewriteConditionWithSingleYield(statement.Condition, resumeSymbol,
                out var yieldExpression, out var rewrittenCondition))
        {
            entryIndex = -1;
            return false;
        }

        var rewrittenIf = new IfStatement(statement.Source, rewrittenCondition, statement.Then, statement.Else);
        return TryBuildIfWithRewrittenCondition(statement, yieldExpression, rewrittenIf, resumeSymbol, nextIndex,
            out entryIndex, activeLabel);
    }

    private bool TryBuildIfWithRewrittenCondition(IfStatement original, YieldExpression yieldExpression,
        IfStatement rewrittenIf, Symbol resumeSymbol, int nextIndex, out int entryIndex, Symbol? activeLabel)
    {
        // Only handle non-delegated `yield` whose operand does not contain
        // nested `yield` expressions. Complex nested shapes still fall back
        // to the replay path.
        if (yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression))
        {
            entryIndex = -1;
            return false;
        }

        var instructionStart = _instructions.Count;

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
        // Only handle simple `yield` / `yield*` used directly as the return
        // expression, and reject nested `yield` inside the yielded expression
        // for now.
        if (ContainsYield(yieldExpression.Expression))
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

    private bool TryBuildStatement(StatementNode statement, int nextIndex, out int entryIndex, Symbol? activeLabel = null)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    return TryBuildStatementList(block.Statements, nextIndex, out entryIndex);

                case IfStatement ifStatement:
                    // Handle conditions that contain a single non-delegated `yield`
                    // by rewriting the condition to use a resume slot fed by a
                    // dedicated yield sequence. Purely yield-free conditions go
                    // through the regular if lowering.
                    if (ifStatement.Condition is not null && ContainsYield(ifStatement.Condition))
                    {
                        if (TryBuildIfWithConditionYield(ifStatement, nextIndex, out entryIndex, activeLabel))
                        {
                            return true;
                        }

                        entryIndex = -1;
                        _failureReason ??= "If condition contains unsupported yield shape.";
                        return false;
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

                case ForEachStatement forEachStatement
                    when forEachStatement.Kind == ForEachKind.Of && IsSimpleForOfBinding(forEachStatement):
                    // For-of with block-scoped bindings and closures requires per-iteration lexical environments.
                    // When the loop body and iterable are yield-free, we can safely delegate to the typed evaluator
                    // (no generator yields inside the loop), preserving correct closure capture semantics.
                    if (forEachStatement.DeclarationKind is VariableKind.Let or VariableKind.Const &&
                        !StatementContainsYield(forEachStatement.Body) &&
                        !ContainsYield(forEachStatement.Iterable))
                    {
                        entryIndex = Append(new StatementInstruction(nextIndex, forEachStatement));
                        return true;
                    }

                    return TryBuildForOfStatement(forEachStatement, nextIndex, out entryIndex, activeLabel);

                case ForEachStatement forEachStatement
                    when forEachStatement.Kind == ForEachKind.AwaitOf && IsSimpleForOfBinding(forEachStatement):
                    // Async `for await...of` loops inside generator bodies now
                    // use the dedicated IR instructions so async generators can
                    // share the same non-blocking await pipeline as the rest of
                    // the generator IR executor.
                    return TryBuildForAwaitStatement(forEachStatement, nextIndex, out entryIndex, activeLabel);

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
        }
    }

    private bool TryBuildIfStatement(IfStatement statement, int nextIndex, out int entryIndex, Symbol? activeLabel)
    {
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

    private bool TryBuildWhileWithYieldCondition(WhileStatement statement, YieldExpression yieldExpression,
        int nextIndex, out int entryIndex, Symbol? label)
    {
        if (yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression))
        {
            entryIndex = -1;
            return false;
        }

        var resumeSymbol = CreateResumeSlotSymbol();
        var conditionIdentifier = new IdentifierExpression(yieldExpression.Source, resumeSymbol);
        var rewrittenWhile = new WhileStatement(statement.Source, conditionIdentifier, statement.Body);

        return TryBuildWhileWithRewrittenCondition(rewrittenWhile, yieldExpression, resumeSymbol, nextIndex,
            out entryIndex, label);
    }

    private bool TryBuildWhileWithConditionYield(WhileStatement statement, int nextIndex,
        out int entryIndex, Symbol? label)
    {
        // Fast-path: direct `while (yield ...)`.
        if (statement.Condition is YieldExpression directYield)
        {
            return TryBuildWhileWithYieldCondition(statement, directYield, nextIndex, out entryIndex, label);
        }

        if (statement.Condition is null || !ContainsYield(statement.Condition))
        {
            entryIndex = -1;
            return false;
        }

        var resumeSymbol = CreateResumeSlotSymbol();
        if (!TryRewriteConditionWithSingleYield(statement.Condition, resumeSymbol,
                out var yieldExpression, out var rewrittenCondition))
        {
            entryIndex = -1;
            return false;
        }

        var rewrittenWhile = new WhileStatement(statement.Source, rewrittenCondition, statement.Body);
        return TryBuildWhileWithRewrittenCondition(rewrittenWhile, yieldExpression, resumeSymbol, nextIndex,
            out entryIndex, label);
    }

    private bool TryBuildWhileWithRewrittenCondition(WhileStatement rewrittenWhile, YieldExpression yieldExpression,
        Symbol resumeSymbol, int nextIndex, out int entryIndex, Symbol? label)
    {
        // Loop shape:
        //   entry -> yield -> store resume into slot -> branch(condition, body, exit)
        //   body  -> jump back to yield
        //
        // This ensures the condition's `yield` is evaluated once per
        // iteration, matching the source semantics.
        if (yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression))
        {
            entryIndex = -1;
            return false;
        }

        var instructionStart = _instructions.Count;

        var jumpIndex = Append(new JumpInstruction(-1));

        var continueTarget = jumpIndex;
        var breakTarget = nextIndex;
        var scope = new LoopScope(label, continueTarget, breakTarget);
        _loopScopes.Push(scope);
        var bodyBuilt = TryBuildStatement(rewrittenWhile.Body, jumpIndex, out var bodyEntry, label);
        _loopScopes.Pop();

        if (!bodyBuilt)
        {
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        var branchIndex = Append(new BranchInstruction(rewrittenWhile.Condition, bodyEntry, nextIndex));
        var yieldEntryIndex = AppendYieldSequence(yieldExpression.Expression, branchIndex, resumeSymbol);

        _instructions[jumpIndex] = new JumpInstruction(yieldEntryIndex);
        entryIndex = yieldEntryIndex;
        return true;
    }

    private bool TryBuildWhileStatement(WhileStatement statement, int nextIndex, out int entryIndex, Symbol? label)
    {
        if (statement.Condition is not null && ContainsYield(statement.Condition))
        {
            if (TryBuildWhileWithConditionYield(statement, nextIndex, out entryIndex, label))
            {
                return true;
            }

            entryIndex = -1;
            _failureReason ??= "While condition contains unsupported yield shape.";
            return false;
        }

        return TryBuildWhileLoop(statement, nextIndex, out entryIndex, label);
    }

    private bool TryBuildWhileLoop(WhileStatement statement, int nextIndex, out int entryIndex, Symbol? label)
    {
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
        if (statement.Condition is not null && ContainsYield(statement.Condition))
        {
            if (TryBuildDoWhileWithConditionYield(statement, nextIndex, out entryIndex, label))
            {
                return true;
            }

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

    private bool TryBuildDoWhileWithConditionYield(DoWhileStatement statement, int nextIndex,
        out int entryIndex, Symbol? label)
    {
        // Fast-path: direct `do { ... } while (yield ...)`.
        if (statement.Condition is YieldExpression directYield)
        {
            var resumeSymbolSimple = CreateResumeSlotSymbol();
            if (directYield.IsDelegated || ContainsYield(directYield.Expression))
            {
                entryIndex = -1;
                return false;
            }

            var conditionIdentifier = new IdentifierExpression(directYield.Source, resumeSymbolSimple);
            var rewrittenSimple = new DoWhileStatement(statement.Source, statement.Body, conditionIdentifier);
            return TryBuildDoWhileWithRewrittenCondition(rewrittenSimple, directYield, resumeSymbolSimple, nextIndex,
                out entryIndex, label);
        }

        if (statement.Condition is null || !ContainsYield(statement.Condition))
        {
            entryIndex = -1;
            return false;
        }

        var resumeSymbol = CreateResumeSlotSymbol();
        if (!TryRewriteConditionWithSingleYield(statement.Condition, resumeSymbol,
                out var yieldExpression, out var rewrittenCondition))
        {
            entryIndex = -1;
            return false;
        }

        var rewritten = new DoWhileStatement(statement.Source, statement.Body, rewrittenCondition);
        return TryBuildDoWhileWithRewrittenCondition(rewritten, yieldExpression, resumeSymbol, nextIndex,
            out entryIndex, label);
    }

    private bool TryBuildDoWhileWithRewrittenCondition(DoWhileStatement rewritten, YieldExpression yieldExpression,
        Symbol resumeSymbol, int nextIndex, out int entryIndex, Symbol? label)
    {
        // Loop shape:
        //   entry -> body -> jump -> yield -> store resume into slot -> branch(condition, body, exit)
        //
        // This ensures `yield` in the condition runs once per iteration,
        // after the body has executed (do/while semantics).
        if (yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression))
        {
            entryIndex = -1;
            return false;
        }

        var instructionStart = _instructions.Count;
        var conditionJumpIndex = Append(new JumpInstruction(-1));

        var continueTarget = conditionJumpIndex;
        var breakTarget = nextIndex;
        var scope = new LoopScope(label, continueTarget, breakTarget);
        _loopScopes.Push(scope);
        var bodyBuilt = TryBuildStatement(rewritten.Body, conditionJumpIndex, out var bodyEntry, label);
        _loopScopes.Pop();

        if (!bodyBuilt)
        {
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        var branchIndex = Append(new BranchInstruction(rewritten.Condition, bodyEntry, nextIndex));
        var yieldEntryIndex = AppendYieldSequence(yieldExpression.Expression, branchIndex, resumeSymbol);

        _instructions[conditionJumpIndex] = new JumpInstruction(yieldEntryIndex);

        entryIndex = bodyEntry;
        return true;
    }

    private bool TryBuildForStatement(ForStatement statement, int nextIndex, out int entryIndex, Symbol? label)
    {
        var instructionStart = _instructions.Count;

        ExpressionNode conditionExpression;
        YieldExpression? yieldCondition = null;
        Symbol? conditionResumeSlot = null;

        if (statement.Condition is null)
        {
            conditionExpression = TrueLiteralExpression;
        }
        else if (ContainsYield(statement.Condition))
        {
            conditionResumeSlot = CreateResumeSlotSymbol();
            if (!TryRewriteConditionWithSingleYield(statement.Condition, conditionResumeSlot,
                    out var yieldExpr, out var rewrittenCondition))
            {
                entryIndex = -1;
                _failureReason ??= "For condition contains unsupported yield shape.";
                return false;
            }

            yieldCondition = yieldExpr;
            conditionExpression = rewrittenCondition;
        }
        else
        {
            conditionExpression = statement.Condition;
        }

        var conditionJumpIndex = Append(new JumpInstruction(-1));
        var continueTarget = conditionJumpIndex;
        var breakTarget = nextIndex;

        if (statement.Increment is not null)
        {
            if (ContainsYield(statement.Increment))
            {
                var resumeSlot = CreateResumeSlotSymbol();
                if (!TryRewriteConditionWithSingleYield(statement.Increment, resumeSlot,
                        out var yieldExpression, out var rewrittenIncrement))
                {
                    _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
                    entryIndex = -1;
                    _failureReason ??= "For increment contains unsupported yield shape.";
                    return false;
                }

                var incrementStatement = new ExpressionStatement(statement.Increment.Source, rewrittenIncrement);
                var incrementIndex = Append(new StatementInstruction(conditionJumpIndex, incrementStatement));
                var yieldEntryIndex =
                    AppendYieldSequence(yieldExpression.Expression, incrementIndex, resumeSlot);
                continueTarget = yieldEntryIndex;
            }
            else
            {
                var incrementStatement = new ExpressionStatement(statement.Increment.Source, statement.Increment);
                continueTarget = Append(new StatementInstruction(conditionJumpIndex, incrementStatement));
            }
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

        if (yieldCondition is not null)
        {
            var yieldEntryIndex =
                AppendYieldSequence(yieldCondition.Expression, branchIndex, conditionResumeSlot);
            _instructions[conditionJumpIndex] = new JumpInstruction(yieldEntryIndex);
        }
        else
        {
            _instructions[conditionJumpIndex] = new JumpInstruction(branchIndex);
        }

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

    private bool TryBuildSwitchStatement(SwitchStatement statement, int nextIndex, out int entryIndex,
        Symbol? activeLabel)
    {
        // For now we support switch statements whose discriminant and case
        // tests are yield-free, and whose case bodies only contain at most a
        // single trailing unlabeled `break;` at top level. More complex break
        // shapes (including non-trailing `break` and default clauses that
        // are not last) continue to be rejected.
        if (ContainsYield(statement.Discriminant))
        {
            entryIndex = -1;
            return false;
        }

        foreach (var switchCase in statement.Cases)
        {
            if (switchCase.Test is not null && ContainsYield(switchCase.Test))
            {
                entryIndex = -1;
                return false;
            }
        }

        // Enforce at most a single default clause, and only in the final
        // position. JavaScript evaluates switch by first selecting the
        // matching case clause (preferring explicit case tests and only
        // using default if no case matches) and then executing the case
        // body with fallthrough. Our lowering models this behaviour only
        // when default is in the canonical tail position.
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

        if (defaultIndex != -1 && defaultIndex != statement.Cases.Length - 1)
        {
            entryIndex = -1;
            _failureReason ??= "Switch statement default clause must be last.";
            return false;
        }

        var instructionStart = _instructions.Count;
        var discriminantSymbol = Symbol.Intern($"__switch_disc_{instructionStart}");
        var matchedSymbol = Symbol.Intern($"__switch_matched_{instructionStart}");
        var doneSymbol = Symbol.Intern($"__switch_done_{instructionStart}");

        var statements = ImmutableArray.CreateBuilder<StatementNode>();

        // const __discN = <discriminant>;
        var discBinding = new IdentifierBinding(statement.Source, discriminantSymbol);
        var discDeclarator = new VariableDeclarator(statement.Source, discBinding, statement.Discriminant);
        var discDeclaration = new VariableDeclaration(statement.Source, VariableKind.Const, [discDeclarator]);
        statements.Add(discDeclaration);

        // let __matchedN = false;
        var matchedBinding = new IdentifierBinding(statement.Source, matchedSymbol);
        var matchedInitializer = new LiteralExpression(statement.Source, false);
        var matchedDeclarator = new VariableDeclarator(statement.Source, matchedBinding, matchedInitializer);
        var matchedDeclaration = new VariableDeclaration(statement.Source, VariableKind.Let, [matchedDeclarator]);
        statements.Add(matchedDeclaration);

        // let __doneN = false;
        var doneBinding = new IdentifierBinding(statement.Source, doneSymbol);
        var doneInitializer = new LiteralExpression(statement.Source, false);
        var doneDeclarator = new VariableDeclarator(statement.Source, doneBinding, doneInitializer);
        var doneDeclaration = new VariableDeclaration(statement.Source, VariableKind.Let, [doneDeclarator]);
        statements.Add(doneDeclaration);

        foreach (var switchCase in statement.Cases)
        {
            var body = switchCase.Body;
            var bodyStatements = body.Statements;

            // Only support a single trailing unlabeled break at top level.
            var hasTrailingBreak = false;
            if (bodyStatements.Length > 0 &&
                bodyStatements[^1] is BreakStatement trailingBreak &&
                trailingBreak.Label is null)
            {
                hasTrailingBreak = true;
                for (var i = 0; i < bodyStatements.Length - 1; i++)
                {
                    if (bodyStatements[i] is BreakStatement)
                    {
                        _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
                        entryIndex = -1;
                        return false;
                    }
                }
            }

            // Build condition: !__done && !__matched && (disc === test) or
            //                 !__done && !__matched for default case.
            var notDone = new UnaryExpression(statement.Source, "!",
                new IdentifierExpression(statement.Source, doneSymbol), true);
            var notMatched = new UnaryExpression(statement.Source, "!",
                new IdentifierExpression(statement.Source, matchedSymbol), true);
            ExpressionNode matchCondition = new BinaryExpression(statement.Source, "&&", notDone, notMatched);

            if (switchCase.Test is not null)
            {
                var discIdentifier = new IdentifierExpression(statement.Source, discriminantSymbol);
                var equalTest = new BinaryExpression(statement.Source, "===",
                    discIdentifier, switchCase.Test);
                matchCondition = new BinaryExpression(statement.Source, "&&", matchCondition, equalTest);
            }

            // if (matchCondition) { __matchedN = true; }
            var setMatchedAssignment = new AssignmentExpression(statement.Source, matchedSymbol,
                new LiteralExpression(statement.Source, true));
            var setMatchedStatement = new ExpressionStatement(statement.Source, setMatchedAssignment);
            var setMatchedBlock = new BlockStatement(statement.Source, [setMatchedStatement], body.IsStrict);
            statements.Add(new IfStatement(statement.Source, matchCondition, setMatchedBlock, null));

            // Execution guard: if (!__done && __matched) { ...case body... }
            var notDoneExec = new UnaryExpression(statement.Source, "!",
                new IdentifierExpression(statement.Source, doneSymbol), true);
            var matchedIdentifier = new IdentifierExpression(statement.Source, matchedSymbol);
            var execCondition = new BinaryExpression(statement.Source, "&&", notDoneExec, matchedIdentifier);

            var execBuilder = ImmutableArray.CreateBuilder<StatementNode>();
            var copyCount = hasTrailingBreak ? bodyStatements.Length - 1 : bodyStatements.Length;
            for (var i = 0; i < copyCount; i++)
            {
                execBuilder.Add(bodyStatements[i]);
            }

            if (hasTrailingBreak)
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
        }
    }

    private static bool StatementContainsYield(StatementNode statement)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var s in block.Statements)
                    {
                        if (StatementContainsYield(s))
                        {
                            return true;
                        }
                    }

                    return false;

                case ExpressionStatement expressionStatement:
                    return ContainsYield(expressionStatement.Expression);

                case VariableDeclaration declaration:
                    return DeclarationContainsYield(declaration);

                case IfStatement ifStatement:
                    if (ContainsYield(ifStatement.Condition))
                    {
                        return true;
                    }

                    if (StatementContainsYield(ifStatement.Then))
                    {
                        return true;
                    }

                    if (ifStatement.Else is not null && StatementContainsYield(ifStatement.Else))
                    {
                        return true;
                    }

                    return false;

                case WhileStatement whileStatement:
                    if (ContainsYield(whileStatement.Condition))
                    {
                        return true;
                    }

                    statement = whileStatement.Body;
                    continue;

                case DoWhileStatement doWhileStatement:
                    if (ContainsYield(doWhileStatement.Condition))
                    {
                        return true;
                    }

                    statement = doWhileStatement.Body;
                    continue;

                case ForStatement forStatement:
                    if (forStatement.Initializer is ExpressionStatement initExpr &&
                        ContainsYield(initExpr.Expression))
                    {
                        return true;
                    }

                    if (forStatement.Initializer is VariableDeclaration initDecl &&
                        DeclarationContainsYield(initDecl))
                    {
                        return true;
                    }

                    if (forStatement.Condition is not null && ContainsYield(forStatement.Condition))
                    {
                        return true;
                    }

                    if (forStatement.Increment is not null && ContainsYield(forStatement.Increment))
                    {
                        return true;
                    }

                    statement = forStatement.Body;
                    continue;

                case ForEachStatement forEachStatement:
                    if (ContainsYield(forEachStatement.Iterable))
                    {
                        return true;
                    }

                    statement = forEachStatement.Body;
                    continue;

                case ReturnStatement returnStatement:
                    return returnStatement.Expression is not null &&
                           ContainsYield(returnStatement.Expression);

                case SwitchStatement switchStatement:
                    if (ContainsYield(switchStatement.Discriminant))
                    {
                        return true;
                    }

                    foreach (var c in switchStatement.Cases)
                    {
                        if (c.Test is not null && ContainsYield(c.Test))
                        {
                            return true;
                        }

                        if (StatementContainsYield(c.Body))
                        {
                            return true;
                        }
                    }

                    return false;

                case TryStatement tryStatement:
                    if (StatementContainsYield(tryStatement.TryBlock))
                    {
                        return true;
                    }

                    if (tryStatement.Catch is not null && StatementContainsYield(tryStatement.Catch.Body))
                    {
                        return true;
                    }

                    if (tryStatement.Finally is not null && StatementContainsYield(tryStatement.Finally))
                    {
                        return true;
                    }

                    return false;

                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;

                // Break/continue/throw etc. cannot contain yield directly.
                default:
                    return false;
            }
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
        if (declarator.Target is not IdentifierBinding)
        {
            entryIndex = -1;
            return false;
        }

        // Simple case: single `yield` / `yield*` used directly as the initializer.
        if (declarator.Initializer is YieldExpression yieldExpression)
        {
            if (ContainsYield(yieldExpression.Expression))
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

        // Multi-yield initializers should be normalized by GeneratorYieldLowerer before reaching
        // the IR builder. If they appear here, treat them as unsupported so lowering remains the
        // single place that handles this shape.
        if (declarator.Initializer is BinaryExpression { Left: YieldExpression, Right: YieldExpression })
        {
            entryIndex = -1;
            _failureReason ??= "Variable declaration contains unsupported yield shape.";
            return false;
        }

        entryIndex = -1;
        return false;
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
        }
    }

    private bool TryRewriteConditionWithSingleYield(ExpressionNode expression, Symbol resumeSlot,
        out YieldExpression yieldExpression, out ExpressionNode rewrittenCondition)
    {
        YieldExpression? singleYield = null;
        var found = false;

        bool Rewrite(ExpressionNode? expr, out ExpressionNode rewritten)
        {
            switch (expr)
            {
                case null:
                    rewritten = null!;
                    return true;
                case YieldExpression y:
                    if (found)
                    {
                        rewritten = null!;
                        return false;
                    }

                    if (y.IsDelegated || ContainsYield(y.Expression))
                    {
                        rewritten = null!;
                        return false;
                    }

                    found = true;
                    singleYield = y;
                    rewritten = new IdentifierExpression(y.Source, resumeSlot);
                    return true;

                case BinaryExpression binary:
                    if (!Rewrite(binary.Left, out var left) || !Rewrite(binary.Right, out var right))
                    {
                        rewritten = null!;
                        return false;
                    }

                    rewritten = ReferenceEquals(left, binary.Left) && ReferenceEquals(right, binary.Right)
                        ? binary
                        : binary with { Left = left, Right = right };
                    return true;

                case ConditionalExpression conditional:
                    if (!Rewrite(conditional.Test, out var test) ||
                        !Rewrite(conditional.Consequent, out var cons) ||
                        !Rewrite(conditional.Alternate, out var alt))
                    {
                        rewritten = null!;
                        return false;
                    }

                    rewritten = ReferenceEquals(test, conditional.Test) &&
                                ReferenceEquals(cons, conditional.Consequent) &&
                                ReferenceEquals(alt, conditional.Alternate)
                        ? conditional
                        : conditional with { Test = test, Consequent = cons, Alternate = alt };
                    return true;

                case CallExpression call:
                    if (!Rewrite(call.Callee, out var callee))
                    {
                        rewritten = null!;
                        return false;
                    }

                    var callArgs = call.Arguments;
                    if (call.Arguments.Length > 0)
                    {
                        var argBuilder = ImmutableArray.CreateBuilder<CallArgument>(call.Arguments.Length);
                        var changed = false;
                        foreach (var arg in call.Arguments)
                        {
                            if (!Rewrite(arg.Expression, out var argExpr))
                            {
                                rewritten = null!;
                                return false;
                            }

                            if (ReferenceEquals(argExpr, arg.Expression))
                            {
                                argBuilder.Add(arg);
                            }
                            else
                            {
                                argBuilder.Add(arg with { Expression = argExpr });
                                changed = true;
                            }
                        }

                        if (changed || !ReferenceEquals(callee, call.Callee))
                        {
                            callArgs = argBuilder.ToImmutable();
                        }
                    }

                    rewritten = ReferenceEquals(callee, call.Callee) && ReferenceEquals(callArgs, call.Arguments)
                        ? call
                        : call with { Callee = callee, Arguments = callArgs };
                    return true;

                case NewExpression @new:
                    if (!Rewrite(@new.Constructor, out var ctor))
                    {
                        rewritten = null!;
                        return false;
                    }

                    var newArgs = @new.Arguments;
                    if (@new.Arguments.Length > 0)
                    {
                        var argBuilder = ImmutableArray.CreateBuilder<ExpressionNode>(@new.Arguments.Length);
                        var changed = false;
                        foreach (var arg in @new.Arguments)
                        {
                            if (!Rewrite(arg, out var argExpr))
                            {
                                rewritten = null!;
                                return false;
                            }

                            if (ReferenceEquals(argExpr, arg))
                            {
                                argBuilder.Add(arg);
                            }
                            else
                            {
                                argBuilder.Add(argExpr);
                                changed = true;
                            }
                        }

                        if (changed || !ReferenceEquals(ctor, @new.Constructor))
                        {
                            newArgs = argBuilder.ToImmutable();
                        }
                    }

                    rewritten = ReferenceEquals(ctor, @new.Constructor) && ReferenceEquals(newArgs, @new.Arguments)
                        ? @new
                        : @new with { Constructor = ctor, Arguments = newArgs };
                    return true;

                case MemberExpression member:
                    if (!Rewrite(member.Target, out var target) ||
                        !Rewrite(member.Property, out var prop))
                    {
                        rewritten = null!;
                        return false;
                    }

                    rewritten = ReferenceEquals(target, member.Target) && ReferenceEquals(prop, member.Property)
                        ? member
                        : member with { Target = target, Property = prop };
                    return true;

                case AssignmentExpression assignment:
                    if (!Rewrite(assignment.Value, out var valueExpr))
                    {
                        rewritten = null!;
                        return false;
                    }

                    rewritten = ReferenceEquals(valueExpr, assignment.Value)
                        ? assignment
                        : assignment with { Value = valueExpr };
                    return true;

                case PropertyAssignmentExpression propertyAssignment:
                    if (!Rewrite(propertyAssignment.Target, out var patTarget) ||
                        !Rewrite(propertyAssignment.Property, out var patProp) ||
                        !Rewrite(propertyAssignment.Value, out var patValue))
                    {
                        rewritten = null!;
                        return false;
                    }

                    rewritten = ReferenceEquals(patTarget, propertyAssignment.Target) &&
                                ReferenceEquals(patProp, propertyAssignment.Property) &&
                                ReferenceEquals(patValue, propertyAssignment.Value)
                        ? propertyAssignment
                        : propertyAssignment with
                        {
                            Target = patTarget, Property = patProp, Value = patValue
                        };
                    return true;

                case IndexAssignmentExpression indexAssignment:
                    if (!Rewrite(indexAssignment.Target, out var idxTarget) ||
                        !Rewrite(indexAssignment.Index, out var idxIndex) ||
                        !Rewrite(indexAssignment.Value, out var idxValue))
                    {
                        rewritten = null!;
                        return false;
                    }

                    rewritten = ReferenceEquals(idxTarget, indexAssignment.Target) &&
                                ReferenceEquals(idxIndex, indexAssignment.Index) &&
                                ReferenceEquals(idxValue, indexAssignment.Value)
                        ? indexAssignment
                        : indexAssignment with
                        {
                            Target = idxTarget, Index = idxIndex, Value = idxValue
                        };
                    return true;

                case SequenceExpression sequence:
                    if (!Rewrite(sequence.Left, out var leftSeq) ||
                        !Rewrite(sequence.Right, out var rightSeq))
                    {
                        rewritten = null!;
                        return false;
                    }

                    rewritten = ReferenceEquals(leftSeq, sequence.Left) && ReferenceEquals(rightSeq, sequence.Right)
                        ? sequence
                        : sequence with { Left = leftSeq, Right = rightSeq };
                    return true;

                case UnaryExpression unary:
                    if (!Rewrite(unary.Operand, out var operand))
                    {
                        rewritten = null!;
                        return false;
                    }

                    rewritten = ReferenceEquals(operand, unary.Operand)
                        ? unary
                        : unary with { Operand = operand };
                    return true;

                case ArrayExpression array:
                    if (array.Elements.IsDefaultOrEmpty)
                    {
                        rewritten = array;
                        return true;
                    }

                    {
                        var builder = ImmutableArray.CreateBuilder<ArrayElement>(array.Elements.Length);
                        var changed = false;
                        foreach (var element in array.Elements)
                        {
                            if (element.Expression is null)
                            {
                                builder.Add(element);
                                continue;
                            }

                            if (!Rewrite(element.Expression, out var elemExpr))
                            {
                                rewritten = null!;
                                return false;
                            }

                            if (ReferenceEquals(elemExpr, element.Expression))
                            {
                                builder.Add(element);
                            }
                            else
                            {
                                builder.Add(element with { Expression = elemExpr });
                                changed = true;
                            }
                        }

                        rewritten = changed
                            ? array with { Elements = builder.ToImmutable() }
                            : array;
                        return true;
                    }

                case ObjectExpression obj:
                    if (obj.Members.IsDefaultOrEmpty)
                    {
                        rewritten = obj;
                        return true;
                    }

                    {
                        var builder = ImmutableArray.CreateBuilder<ObjectMember>(obj.Members.Length);
                        var changed = false;
                        foreach (var member in obj.Members)
                        {
                            ExpressionNode? memberValue = member.Value;
                            if (member.Value is not null)
                            {
                                if (!Rewrite(member.Value, out var memberValueExpr))
                                {
                                    rewritten = null!;
                                    return false;
                                }

                                memberValue = memberValueExpr;
                            }

                            if (ReferenceEquals(memberValue, member.Value))
                            {
                                builder.Add(member);
                            }
                            else
                            {
                                builder.Add(member with { Value = memberValue });
                                changed = true;
                            }
                        }

                        rewritten = changed
                            ? obj with { Members = builder.ToImmutable() }
                            : obj;
                        return true;
                    }

                case FunctionExpression:
                case ClassExpression:
                    // Nested functions/classes have their own yield scopes.
                    rewritten = expr;
                    return true;

                default:
                    if (ContainsYield(expr))
                    {
                        rewritten = null!;
                        return false;
                    }

                    rewritten = expr;
                    return true;
            }
        }

        if (!Rewrite(expression, out var rewritten))
        {
            yieldExpression = null!;
            rewrittenCondition = null!;
            return false;
        }

        if (!found)
        {
            yieldExpression = null!;
            rewrittenCondition = expression;
            return false;
        }

        yieldExpression = singleYield!;
        rewrittenCondition = rewritten;
        return true;
    }

    private int Append(GeneratorInstruction instruction)
    {
        var index = _instructions.Count;
        _instructions.Add(instruction);
        return index;
    }
}
