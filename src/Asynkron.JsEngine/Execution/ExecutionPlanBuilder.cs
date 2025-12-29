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

    private bool TryBuildReturnWithYield(YieldExpression yieldExpression,
        out int entryIndex)
    {
        return Emitters.YieldEmitter.TryEmitReturnWithYield(GetEmitContext(), yieldExpression, out entryIndex);
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
                    // If the block needs its own scope (has let/const declarations),
                    // we need to create an environment for it.
                    var hoistPlan = ((IAstCacheable<HoistPlan>)block).GetOrCreateCache();
                    if (hoistPlan.NeedsEnvironment)
                    {
                        // If the block contains yield or await, we must NOT use StatementInstruction
                        // because that causes duplicate execution on resume. Instead, emit
                        // PushEnvironment + individual statements + PopEnvironment.
                        if (AstShapeAnalyzer.StatementContainsYield(block) ||
                            AstShapeAnalyzer.StatementContainsAwait(block))
                        {
                            return TryBuildBlockWithEnvironment(block, hoistPlan, nextIndex, out entryIndex);
                        }

                        // For blocks without yield/await, StatementInstruction is fine
                        entryIndex = Append(new StatementInstruction(nextIndex, block));
                        return true;
                    }

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
                    return Emitters.YieldEmitter.TryEmitYieldExpressionStatement(
                        GetEmitContext(), yieldExpression, nextIndex, out entryIndex);

                case ExpressionStatement expressionStatement:
                    if (expressionStatement.Expression is AssignmentExpression
                        {
                            Target: { } targetSymbol, Value: YieldExpression yieldAssignment
                        } &&
                        IsLowererTemp(targetSymbol))
                    {
                        if (Emitters.YieldEmitter.TryEmitYieldAssignment(
                            GetEmitContext(), targetSymbol, yieldAssignment, nextIndex, out entryIndex))
                        {
                            return true;
                        }
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

                    // Fast path: simple increment/decrement on identifiers (e.g., i++, --j)
                    if (expressionStatement.Expression is UnaryExpression
                        {
                            Operator: UnaryOperator.Increment or UnaryOperator.Decrement,
                            Operand: IdentifierExpression identTarget
                        } unaryExpr)
                    {
                        var isIncrement = unaryExpr.Operator == UnaryOperator.Increment;
                        entryIndex = Append(new IncrementSlotInstruction(
                            nextIndex,
                            identTarget.Name,
                            isIncrement,
                            unaryExpr.IsPrefix));
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

                    // Pass nextIndex so that if return is inside try/finally, we can
                    // continue to EndFinallyInstruction after updating pending completion.
                    entryIndex = Append(new ReturnInstruction(nextIndex, returnStatement.Expression));
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
                    return TryBuildLabeledNonLoopStatement(labeled, nextIndex, out entryIndex);

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

        if (!IsLowererTemp(targetSymbol))
        {
            return false;
        }

        return Emitters.YieldEmitter.TryEmitVariableWithYieldInitializer(
            GetEmitContext(), targetSymbol, yieldInitializer, nextIndex, out entryIndex);
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

    /// <summary>
    /// Builds a block that needs its own environment AND contains yield/await.
    /// Instead of using StatementInstruction (which causes duplicate execution on resume),
    /// we emit PushEnvironment + individual statements + PopEnvironment.
    /// </summary>
    private bool TryBuildBlockWithEnvironment(BlockStatement block, HoistPlan hoistPlan, int nextIndex, out int entryIndex)
    {
        var instructionStart = _instructions.Count;

        // Check if we can pool the environment (no closures or dynamic scope)
        var allowPooling = !ContainsWithOrDirectEval(block) && !ContainsInnerFunctionExpression(block);

        // Get scope info from the block (stamped by scope analysis)
        var scopeId = block.ScopeId >= 0 ? block.ScopeId : -1;
        var slotCount = block.SlotCount >= 0 ? block.SlotCount : 0;
        var slotMap = block.SlotMap.IsEmpty
            ? ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance)
            : block.SlotMap;

        // Build instructions bottom-up (reverse order):
        // 1. PopEnvironmentInstruction pointing to nextIndex
        // 2. Body statements pointing to PopEnvironment
        // 3. PushEnvironmentInstruction pointing to body entry

        // 1. Pop environment (exit the block scope)
        var popEnvIndex = Append(new PopEnvironmentInstruction(scopeId, allowPooling, nextIndex));

        // 2. Build the body statements, they flow to PopEnvironment
        if (!TryBuildStatementList(block.Statements, popEnvIndex, out var bodyEntry))
        {
            _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
            entryIndex = -1;
            return false;
        }

        // 3. Push environment (enter the block scope)
        // For blocks, PerIterationBindings is empty (no loop iteration semantics)
        entryIndex = Append(new PushEnvironmentInstruction(
            bodyEntry,
            hoistPlan.LexicalTemplate,
            scopeId,
            slotCount,
            slotMap,
            allowPooling));

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
        return Emitters.LoopEmitter.TryEmitLoopPlan(GetEmitContext(), plan, nextIndex, label, out entryIndex);
    }

    private bool TryBuildTryStatement(TryStatement statement, int nextIndex, out int entryIndex, Symbol? activeLabel)
    {
        return Emitters.TryEmitter.TryEmitTry(GetEmitContext(), statement, nextIndex, activeLabel, out entryIndex);
    }

    /// <summary>
    ///     Builds a labeled non-loop statement by wrapping it with LoopEnter/LoopExit instructions.
    ///     This enables labeled break statements within the statement body (e.g., <c>label: { break label; }</c>).
    /// </summary>
    private bool TryBuildLabeledNonLoopStatement(LabeledStatement labeled, int nextIndex, out int entryIndex)
    {
        return Emitters.ControlFlowEmitter.TryEmitLabeledNonLoop(GetEmitContext(), labeled, nextIndex, out entryIndex);
    }

    private bool TryBuildSwitchStatement(SwitchStatement statement, int nextIndex, out int entryIndex,
        Symbol? activeLabel)
    {
        return Emitters.SwitchEmitter.TryEmitSwitch(GetEmitContext(), statement, nextIndex, activeLabel, out entryIndex);
    }

    private bool TryBuildForOfStatement(ForEachStatement statement, int nextIndex, out int entryIndex, Symbol? label)
    {
        return Emitters.ForOfEmitter.TryEmitForOf(GetEmitContext(), statement, nextIndex, label, out entryIndex);
    }

    private bool TryBuildForAwaitStatement(ForEachStatement statement, int nextIndex, out int entryIndex,
        Symbol? label)
    {
        return Emitters.ForOfEmitter.TryEmitForAwaitOf(GetEmitContext(), statement, nextIndex, label, out entryIndex);
    }

    private bool TryBuildBreak(BreakStatement statement, out int entryIndex)
    {
        return Emitters.ControlFlowEmitter.TryEmitBreak(GetEmitContext(), statement, out entryIndex);
    }

    private bool TryBuildContinue(ContinueStatement statement, out int entryIndex)
    {
        return Emitters.ControlFlowEmitter.TryEmitContinue(GetEmitContext(), statement, out entryIndex);
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
        return Emitters.WithEmitter.TryEmitWith(GetEmitContext(), statement, nextIndex, activeLabel, out entryIndex);
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
