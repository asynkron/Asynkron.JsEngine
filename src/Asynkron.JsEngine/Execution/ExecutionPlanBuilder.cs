#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Builds execution plans (IR) for pauseable functions: generators, async functions, and async generators.
///     The builder supports linear statement lists, blocks, expression statements, variable declarations,
///     returns, yield/yield* expressions, and control flow (if/loops/try-catch).
///     More complex constructs are detected and reported as unsupported so the engine can fall back to
///     the legacy replay runner.
/// </summary>
internal sealed class ExecutionPlanBuilder
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

    private bool TryBuildReturnWithYield(YieldExpression yieldExpression,
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
        var implicitReturnIndex = Append(new ReturnInstruction(null));
        if (!TryBuildStatementList(function.Body.Statements, implicitReturnIndex, out var entryIndex))
        {
            plan = default!;
            _failureReason ??= "Statement list contains unsupported construct.";
            return false;
        }

        plan = new ExecutionPlan(
            [.._instructions],
            entryIndex,
            _slotSymbols.Count,
            [.._slotSymbols]);
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

                case FunctionDeclaration:
                    // Function declarations are hoisted - this is a no-op at runtime
                    entryIndex = Append(new FunctionDeclarationInstruction(nextIndex));
                    return true;

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

                    // Check for destructuring assignment expressions with yields that cannot be safely
                    // extracted. This includes:
                    // - Yields in default values (only evaluated when element is undefined)
                    // - Yields in assignment target expressions (e.g., [ {}[ yield ] ] = x)
                    // Wrap them as StatementInstruction to use AST evaluation's state-saving.
                    if (ExpressionContainsDestructuringWithYieldAnywhere(expressionStatement.Expression))
                    {
                        entryIndex = Append(new StatementInstruction(nextIndex, expressionStatement));
                        return true;
                    }

                    var expressionShape = AstShapeAnalyzer.AnalyzeExpression(expressionStatement.Expression);
                    if (expressionShape.DelegatedYieldCount > 0 ||
                        expressionShape.YieldOperandContainsYield)
                    {
                        entryIndex = -1;
                        _failureReason ??= "Expression statement contains unsupported yield shape.";
                        return false;
                    }

                    // After lowering, no yields should remain in expression statements.
                    // If we still have yields here, the lowerer missed a pattern.
                    if (expressionShape.YieldCount > 0)
                    {
                        entryIndex = -1;
                        _failureReason ??= "Expression statement contains unlowered yield - this should have been handled by GeneratorYieldLowerer.";
                        return false;
                    }

                    // For async generators, await expressions are lowered to yield points.
                    // Don't use native instruction if there are awaits - fall back to StatementInstruction.
                    if (AstShapeAnalyzer.ContainsAwait(expressionStatement.Expression))
                    {
                        entryIndex = Append(new StatementInstruction(nextIndex, expressionStatement));
                        return true;
                    }

                    // Use native EvaluateAndDiscardInstruction - evaluates expression and discards result
                    entryIndex = Append(new EvaluateAndDiscardInstruction(nextIndex, expressionStatement.Expression));
                    return true;

                case VariableDeclaration declaration:
                    if (TryBuildVariableDeclaration(declaration, nextIndex, out entryIndex))
                    {
                        return true;
                    }

                    // Check for variable declarations with yields in binding target default values.
                    // These cannot be safely lowered because defaults are only evaluated when
                    // the value is undefined. Wrap them as StatementInstruction.
                    if (DeclarationContainsYieldInBindingTargetDefaults(declaration))
                    {
                        entryIndex = Append(new StatementInstruction(nextIndex, declaration));
                        return true;
                    }

                    if (DeclarationContainsYield(declaration))
                    {
                        entryIndex = -1;
                        _failureReason ??= "Variable declaration contains unsupported yield shape.";
                        return false;
                    }

                    // Try to use native SimpleVariableDeclarationInstruction for simple cases
                    if (TryBuildSimpleVariableDeclaration(declaration, nextIndex, out entryIndex))
                    {
                        return true;
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

                    return TryBuildForOfStatement(forEachStatement, nextIndex, out entryIndex, activeLabel);

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

                    return TryBuildForAwaitStatement(forEachStatement, nextIndex, out entryIndex, activeLabel);

                case ReturnStatement returnStatement:
                    if (returnStatement.Expression is YieldExpression yieldReturn &&
                        TryBuildReturnWithYield(yieldReturn, out entryIndex))
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
                        return TryBuildWithStatement(withStatement, nextIndex, out entryIndex, activeLabel);
                    }

                    // If no yield in body, emit as a simple statement instruction
                    entryIndex = Append(new StatementInstruction(nextIndex, withStatement));
                    return true;

                case ClassDeclaration classDeclaration:
                    // Check for yields in places that are evaluated in the generator context:
                    // - extends expression
                    // - computed property names of members and fields
                    if (ClassDefinitionContainsYield(classDeclaration.Definition))
                    {
                        entryIndex = -1;
                        _failureReason ??=
                            "Class declaration contains yield in computed property names or extends clause.";
                        return false;
                    }

                    // For async generators, awaits need proper handling via StatementInstruction
                    if (ClassDefinitionContainsAwait(classDeclaration.Definition))
                    {
                        entryIndex = Append(new StatementInstruction(nextIndex, classDeclaration));
                        return true;
                    }

                    // Use native ClassDeclarationInstruction for clean cases
                    entryIndex = Append(new ClassDeclarationInstruction(nextIndex, classDeclaration));
                    return true;

                case ThrowStatement throwStatement:
                    if (throwStatement.Expression is not null &&
                        AstShapeAnalyzer.ContainsYield(throwStatement.Expression))
                    {
                        entryIndex = -1;
                        _failureReason ??= "Throw expression contains unsupported yield shape.";
                        return false;
                    }

                    // Use native ThrowInstruction - it evaluates the expression and throws
                    entryIndex = Append(new ThrowInstruction(throwStatement.Expression!));
                    return true;

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

        if (!IsLowererTemp(targetSymbol) ||
            AstShapeAnalyzer.ContainsYield(yieldInitializer.Expression))
        {
            return false;
        }

        entryIndex = yieldInitializer.IsDelegated
            ? AppendYieldStarSequence(yieldInitializer, nextIndex, targetSymbol)
            : AppendYieldSequence(yieldInitializer.Expression, nextIndex, targetSymbol);
        return true;
    }

    /// <summary>
    ///     Attempts to build native SimpleVariableDeclarationInstructions for simple declarations.
    ///     Handles single or multiple declarators with identifier bindings (no destructuring).
    ///     For multiple declarators like <c>let a = 1, b = 2;</c>, creates a chain of instructions.
    /// </summary>
    private bool TryBuildSimpleVariableDeclaration(VariableDeclaration declaration, int nextIndex, out int entryIndex)
    {
        // Don't handle using/await using for now - they have complex disposal semantics
        if (declaration.Kind is VariableKind.Using or VariableKind.AwaitUsing)
        {
            entryIndex = -1;
            return false;
        }

        // First, verify ALL declarators are simple (identifier binding, no yields/awaits)
        foreach (var declarator in declaration.Declarators)
        {
            // Only handle simple identifier binding (no destructuring)
            if (declarator.Target is not IdentifierBinding)
            {
                entryIndex = -1;
                return false;
            }

            // Ensure no yields or awaits in initializer
            if (declarator.Initializer is not null &&
                (AstShapeAnalyzer.ContainsYield(declarator.Initializer) ||
                 AstShapeAnalyzer.ContainsAwait(declarator.Initializer)))
            {
                entryIndex = -1;
                return false;
            }
        }

        // All declarators are simple - build a chain of instructions
        // Work backwards from the last declarator to properly chain next pointers
        var currentNext = nextIndex;
        entryIndex = -1;

        for (var i = declaration.Declarators.Length - 1; i >= 0; i--)
        {
            var declarator = declaration.Declarators[i];
            var targetSymbol = ((IdentifierBinding)declarator.Target).Name;

            var instructionIndex = Append(new SimpleVariableDeclarationInstruction(
                currentNext,
                declaration.Kind,
                targetSymbol!,
                declarator.Initializer));

            currentNext = instructionIndex;
            if (i == 0)
            {
                entryIndex = instructionIndex;
            }
        }

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

        // For lexical declarations (let/const), emit CreateIterationEnvironmentInstruction
        // AFTER the body but BEFORE the increment (PostIteration).
        // This matches the ECMAScript spec (ForBodyEvaluation):
        //   1. Evaluate condition
        //   2. Evaluate body (closures capture current environment)
        //   3. CreatePerIterationEnvironment (create new env, copy values)
        //   4. Evaluate increment (modifies new env, not the captured one)
        //
        // This ensures closures capture the pre-increment value of loop variables.
        var continueTarget = postIterationEntry;
        int createEnvIndex = -1;
        if (!plan.PerIterationBindings.IsDefaultOrEmpty && plan.Kind == LoopKind.For)
        {
            // Build slot map from per-iteration bindings and slot indices
            var slotMapBuilder = ImmutableDictionary.CreateBuilder<Symbol, int>();
            var bindings = plan.PerIterationBindings;
            var slotIndices = plan.PerIterationSlotIndices;
            var count = Math.Min(bindings.Length, slotIndices.IsDefaultOrEmpty ? 0 : slotIndices.Length);
            for (var i = 0; i < count; i++)
            {
                if (slotIndices[i] >= 0)
                {
                    slotMapBuilder[bindings[i]] = slotIndices[i];
                }
            }

            // CreateEnv flows to PostIteration (increment)
            createEnvIndex = Append(new CreateIterationEnvironmentInstruction(
                postIterationEntry,
                plan.PerIterationBindings,
                plan.IterationScopeId,
                plan.IterationSlotCount,
                slotMapBuilder.ToImmutable(),
                plan.IterationParentScopeId,
                plan.AllowIterationEnvironmentPooling));
            // Continue should go through CreateEnv before increment
            continueTarget = createEnvIndex;
        }

        var scope = new LoopScope(label, continueTarget, nextIndex);
        _loopScopes.Push(scope);

        // Body flows to CreateEnv (if present) or PostIteration (increment)
        var bodyNextTarget = createEnvIndex >= 0 ? createEnvIndex : postIterationEntry;
        if (!TryBuildStatement(plan.Body, bodyNextTarget, out var bodyEntry, label))
        {
            _loopScopes.Pop();
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        _loopScopes.Pop();

        // For non-For loops (while, do-while), create environment before body (original behavior)
        var iterationBodyEntry = bodyEntry;
        if (!plan.PerIterationBindings.IsDefaultOrEmpty && plan.Kind != LoopKind.For)
        {
            // Build slot map from per-iteration bindings and slot indices
            var slotMapBuilder = ImmutableDictionary.CreateBuilder<Symbol, int>();
            var bindings = plan.PerIterationBindings;
            var slotIndices = plan.PerIterationSlotIndices;
            var count = Math.Min(bindings.Length, slotIndices.IsDefaultOrEmpty ? 0 : slotIndices.Length);
            for (var i = 0; i < count; i++)
            {
                if (slotIndices[i] >= 0)
                {
                    slotMapBuilder[bindings[i]] = slotIndices[i];
                }
            }

            var createEnvBeforeBody = Append(new CreateIterationEnvironmentInstruction(
                bodyEntry,
                plan.PerIterationBindings,
                plan.IterationScopeId,
                plan.IterationSlotCount,
                slotMapBuilder.ToImmutable(),
                plan.IterationParentScopeId,
                plan.AllowIterationEnvironmentPooling));
            iterationBodyEntry = createEnvBeforeBody;
        }

        // Branch flows to body directly (for For loops) or CreateEnv (for others)
        var branchIndex = Append(new BranchInstruction(plan.Condition, iterationBodyEntry, nextIndex));

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

        var loopEntry = plan.ConditionAfterBody ? iterationBodyEntry : conditionJumpIndex;

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

        var finallyEntry = -1;
        if (hasFinally && statement.Finally is not null)
        {
            var endFinallyIndex = Append(new EndFinallyInstruction(nextIndex));
            if (!TryBuildStatement(statement.Finally, endFinallyIndex, out finallyEntry, activeLabel))
            {
                _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
                entryIndex = -1;
                return false;
            }
        }

        var leaveTryIndex = Append(new LeaveTryInstruction(nextIndex));

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

            var matchUnset = new BinaryExpression(statement.Source, BinaryOperator.StrictEqual,
                new IdentifierExpression(statement.Source, matchIndexSymbol),
                new LiteralExpression(statement.Source, -1));
            var discIdentifier = new IdentifierExpression(statement.Source, discriminantSymbol);
            var equalTest = new BinaryExpression(statement.Source, BinaryOperator.StrictEqual,
                discIdentifier, switchCase.Test);
            var combinedTest = new BinaryExpression(statement.Source, BinaryOperator.LogicalAnd, matchUnset, equalTest);

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
            var stillUnmatched = new BinaryExpression(statement.Source, BinaryOperator.StrictEqual,
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
            var notDoneExec = new UnaryExpression(statement.Source, UnaryOperator.LogicalNot,
                new IdentifierExpression(statement.Source, doneSymbol), true);
            var matchSet = new BinaryExpression(statement.Source, BinaryOperator.StrictNotEqual,
                new IdentifierExpression(statement.Source, matchIndexSymbol),
                new LiteralExpression(statement.Source, -1));
            var matchReached = new BinaryExpression(statement.Source, BinaryOperator.LessThanOrEqual,
                new IdentifierExpression(statement.Source, matchIndexSymbol),
                new LiteralExpression(statement.Source, caseIndex));
            var matchGuard = new BinaryExpression(statement.Source, BinaryOperator.LogicalAnd, matchSet, matchReached);
            var execCondition =
                new BinaryExpression(statement.Source, BinaryOperator.LogicalAnd, notDoneExec, matchGuard);

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

        var instructionStart = _instructions.Count;

        // Build the structure bottom-up:
        // 1. EndFinally -> nextIndex
        // 2. IteratorClose -> EndFinally
        // 3. LeaveTry -> nextIndex
        // 4. Body -> MoveNext
        // 5. MoveNext -> Body, BreakIndex -> LeaveTry
        // 6. EnterTry -> MoveNext, finally -> IteratorClose
        // 7. IteratorInit -> EnterTry

        // Pre-create symbols and allocate slots for O(1) access
        var iteratorSymbol = Symbol.Intern(iteratorPlan.Kind == IteratorDriverKind.Await
            ? $"__forAwait_iter_{instructionStart}"
            : $"__forOf_iter_{instructionStart}");
        var valueSymbol = Symbol.Intern(iteratorPlan.Kind == IteratorDriverKind.Await
            ? $"__forAwait_value_{instructionStart}"
            : $"__forOf_value_{instructionStart}");

        var iteratorSlotIndex = AllocateSlot(iteratorSymbol);
        var valueSlotIndex = AllocateSlot(valueSymbol);

        // First, create the iterator instructions with pre-allocated slots
        var iteratorInstructions =
            IteratorInstructionTemplate.AppendInstructions(_instructions, iteratorPlan,
                -1, // breakIndex will be LeaveTry
                iteratorSymbol,
                valueSymbol,
                iteratorSlotIndex,
                valueSlotIndex);

        // EndFinally - this is the end of the finally block
        var endFinallyIndex = Append(new EndFinallyInstruction(nextIndex));

        // IteratorClose - the finally block content
        var iteratorCloseIndex =
            Append(new IteratorCloseInstruction(iteratorInstructions.IteratorSlot, endFinallyIndex));

        // LeaveTry - normal exit from the loop
        var leaveTryIndex = Append(new LeaveTryInstruction(nextIndex));

        // Now update the MoveNext break target to point to LeaveTry
        _instructions[iteratorInstructions.MoveNextIndex] =
            (IteratorMoveNextInstruction)_instructions[iteratorInstructions.MoveNextIndex] with
            {
                BreakIndex = leaveTryIndex
            };

        // Build the loop body
        var perIterationBlock = CreateIteratorIterationBlock(iteratorPlan, iteratorInstructions.ValueSlot);
        var scope = new LoopScope(label, iteratorInstructions.MoveNextIndex, leaveTryIndex);
        _loopScopes.Push(scope);
        var bodyBuilt = TryBuildStatement(perIterationBlock, iteratorInstructions.MoveNextIndex, out var iterationEntry,
            label);
        _loopScopes.Pop();

        if (!bodyBuilt)
        {
            _instructions.RemoveRange(instructionStart,
                _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        // For lexical declarations (let/const), emit CreateIterationEnvironmentInstruction
        // to create fresh per-iteration bindings. This ensures closures capture separate values.
        var loopEntry = iterationEntry;
        if (iteratorPlan.DeclarationKind is VariableKind.Let or VariableKind.Const &&
            !iteratorPlan.PerIterationBindings.IsDefaultOrEmpty)
        {
            // Build slot map from per-iteration bindings and slot indices
            var slotMapBuilder = ImmutableDictionary.CreateBuilder<Symbol, int>();
            var bindings = iteratorPlan.PerIterationBindings;
            var slotIndices = iteratorPlan.PerIterationSlotIndices;
            var count = Math.Min(bindings.Length, slotIndices.IsDefaultOrEmpty ? 0 : slotIndices.Length);
            for (var i = 0; i < count; i++)
            {
                if (slotIndices[i] >= 0)
                {
                    slotMapBuilder[bindings[i]] = slotIndices[i];
                }
            }

            var createEnvIndex = Append(new CreateIterationEnvironmentInstruction(
                iterationEntry,
                iteratorPlan.PerIterationBindings,
                iteratorPlan.IterationScopeId,
                iteratorPlan.IterationSlotCount,
                slotMapBuilder.ToImmutable(),
                iteratorPlan.IterationParentScopeId,
                iteratorPlan.CanReuseIterationEnvironment));
            loopEntry = createEnvIndex;
        }

        // Wire up the MoveNext to point to the loop entry (env instruction or body)
        IteratorInstructionTemplate.Wire(iteratorInstructions, loopEntry, _instructions);

        // EnterTry - wraps the loop in a try/finally
        var enterTryIndex =
            Append(new EnterTryInstruction(iteratorInstructions.MoveNextIndex, -1, null, iteratorCloseIndex));

        // Wire IteratorInit to point to EnterTry
        _instructions[iteratorInstructions.InitIndex] =
            (IteratorInitInstruction)_instructions[iteratorInstructions.InitIndex] with { Next = enterTryIndex };

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
        return declaration.Declarators.Any(static d =>
            d.Initializer is not null &&
            AstShapeAnalyzer.ContainsYield(d.Initializer) &&
            !IsLowererTemp(d.Target));
    }

    /// <summary>
    /// Checks if a variable declaration contains yields in binding targets that cannot be
    /// safely extracted. This includes:
    /// - Yields in default values (only evaluated when value is undefined)
    /// - Yields in assignment target expressions (e.g., [ {}[ yield ] ] = x)
    /// </summary>
    private static bool DeclarationContainsYieldInBindingTargetDefaults(VariableDeclaration declaration)
    {
        return declaration.Declarators.Any(static d =>
            BindingTargetContainsYieldInDefaultValue(d.Target) ||
            (d.Initializer is not null && ExpressionContainsDestructuringWithYieldAnywhere(d.Initializer)));
    }

    /// <summary>
    ///     Checks if a class definition contains yield expressions in places that are
    ///     evaluated in the generator context (extends clause, computed property names).
    ///     Method bodies and field initializers are NOT checked because they create their
    ///     own scope and aren't evaluated during class definition.
    /// </summary>
    private static bool ClassDefinitionContainsYield(ClassDefinition definition)
    {
        // Check extends clause
        if (definition.Extends is not null && AstShapeAnalyzer.ContainsYield(definition.Extends))
        {
            return true;
        }

        // Check computed property names in members (methods, getters, setters)
        foreach (var member in definition.Members)
        {
            if (member is { IsComputed: true, ComputedName: not null } &&
                AstShapeAnalyzer.ContainsYield(member.ComputedName))
            {
                return true;
            }
        }

        // Check computed property names in fields
        foreach (var field in definition.Fields)
        {
            if (field is { IsComputed: true, ComputedName: not null } &&
                AstShapeAnalyzer.ContainsYield(field.ComputedName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Checks if a class definition contains await expressions in places that are
    ///     evaluated in the generator context (extends clause, computed property names).
    ///     Used to fall back to StatementInstruction for async generators.
    /// </summary>
    private static bool ClassDefinitionContainsAwait(ClassDefinition definition)
    {
        // Check extends clause
        if (definition.Extends is not null && AstShapeAnalyzer.ContainsAwait(definition.Extends))
        {
            return true;
        }

        // Check computed property names in members (methods, getters, setters)
        foreach (var member in definition.Members)
        {
            if (member is { IsComputed: true, ComputedName: not null } &&
                AstShapeAnalyzer.ContainsAwait(member.ComputedName))
            {
                return true;
            }
        }

        // Check computed property names in fields
        foreach (var field in definition.Fields)
        {
            if (field is { IsComputed: true, ComputedName: not null } &&
                AstShapeAnalyzer.ContainsAwait(field.ComputedName))
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
        if (plan.Body is { } block)
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

    private static bool IsLowererTemp(BindingTarget target)
    {
        return target is IdentifierBinding { Name.Name: not null } identifier &&
               identifier.Name.Name.StartsWith("__yield_lower_", StringComparison.Ordinal);
    }

    private static bool IsLowererTemp(Symbol symbol)
    {
        return symbol.Name?.StartsWith("__yield_lower_", StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// Checks if the binding target contains yields specifically in default value expressions.
    /// Yields in default values cannot be safely extracted because defaults are only evaluated
    /// when the element is undefined - extracting them would change evaluation order.
    /// </summary>
    private static bool BindingTargetContainsYieldInDefaultValue(BindingTarget target)
    {
        switch (target)
        {
            case ArrayBinding arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    // Check for yields specifically in default values
                    if (element.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(element.DefaultValue))
                    {
                        return true;
                    }

                    // Recursively check nested bindings for yields in their defaults
                    if (element.Target is not null && BindingTargetContainsYieldInDefaultValue(element.Target))
                    {
                        return true;
                    }
                }

                if (arrayBinding.RestElement is not null &&
                    BindingTargetContainsYieldInDefaultValue(arrayBinding.RestElement))
                {
                    return true;
                }

                return false;

            case ObjectBinding objectBinding:
                foreach (var prop in objectBinding.Properties)
                {
                    // Check for yields specifically in default values
                    if (prop.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(prop.DefaultValue))
                    {
                        return true;
                    }

                    // Note: yields in computed property names (NameExpression) CAN be safely extracted
                    // because they're always evaluated, so we don't check them here

                    // Recursively check nested bindings for yields in their defaults
                    if (BindingTargetContainsYieldInDefaultValue(prop.Target))
                    {
                        return true;
                    }
                }

                if (objectBinding.RestElement is not null &&
                    BindingTargetContainsYieldInDefaultValue(objectBinding.RestElement))
                {
                    return true;
                }

                return false;

            case AssignmentTargetBinding assignmentTarget:
                // Check if the assignment target expression contains a yield
                // e.g., [ {}[ yield ] ] has a yield in the MemberExpression
                return AstShapeAnalyzer.ContainsYield(assignmentTarget.Expression);

            default:
                // IdentifierBinding doesn't have expressions or default values
                return false;
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

    /// <summary>
    /// Checks if an expression contains a destructuring assignment with yields anywhere in
    /// the binding target - either in default values or in assignment target expressions.
    /// This handles patterns like: result = [ {}[ yield ] ] = vals;
    /// </summary>
    private static bool ExpressionContainsDestructuringWithYieldAnywhere(ExpressionNode expression)
    {
        while (true)
        {
            switch (expression)
            {
                case DestructuringAssignmentExpression destructuringExpr:
                    // Direct destructuring assignment
                    if (BindingTargetContainsYieldAnywhere(destructuringExpr.Target))
                    {
                        return true;
                    }

                    // Also check the value expression for nested destructurings
                    expression = destructuringExpr.Value;
                    continue;

                case AssignmentExpression assignmentExpr:
                    // Check the value side of the assignment for destructuring
                    expression = assignmentExpr.Value;
                    continue;

                case PropertyAssignmentExpression propAssignExpr:
                    expression = propAssignExpr.Value;
                    continue;

                case IndexAssignmentExpression indexAssignExpr:
                    expression = indexAssignExpr.Value;
                    continue;

                case ConditionalExpression conditionalExpr:
                    return ExpressionContainsDestructuringWithYieldAnywhere(conditionalExpr.Consequent) || ExpressionContainsDestructuringWithYieldAnywhere(conditionalExpr.Alternate);

                case SequenceExpression seqExpr:
                    return ExpressionContainsDestructuringWithYieldAnywhere(seqExpr.Left) || ExpressionContainsDestructuringWithYieldAnywhere(seqExpr.Right);

                default:
                    return false;
            }
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

    private bool TryBuildWithStatement(WithStatement statement, int nextIndex, out int entryIndex, Symbol? activeLabel)
    {
        var instructionStart = _instructions.Count;
        var withScopeSlot = CreateWithScopeSlotSymbol();

        // Build the leave with instruction first (it comes after the body)
        var leaveWithIndex = Append(new LeaveWithInstruction(withScopeSlot, nextIndex));

        // Build the body (jumps to the leave with instruction when done)
        if (!TryBuildStatement(statement.Body, leaveWithIndex, out var bodyEntry, activeLabel))
        {
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        // Build the enter with instruction (comes before the body)
        entryIndex = Append(new EnterWithInstruction(statement.Object, withScopeSlot, bodyEntry));
        return true;
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

    private int Append(ExecutionInstruction instruction)
    {
        var index = _instructions.Count;
        _instructions.Add(instruction);
        return index;
    }

    private readonly record struct LoopScope(Symbol? Label, int ContinueTarget, int BreakTarget);
}
