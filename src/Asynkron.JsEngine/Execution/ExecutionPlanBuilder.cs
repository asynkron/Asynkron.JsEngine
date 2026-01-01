#region

using System.Collections.Immutable;
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
        if (!_isScriptLevel)
        {
            AssignSlotsToUserVariables(entryIndex);
        }

        plan = new ExecutionPlan(
            [.._instructions],
            entryIndex,
            _slotSymbols.Count,
            [.._slotSymbols]);
        return true;
    }

    /// <summary>
    /// Collects variable declarations and scope metadata from instructions,
    /// assigns function-scope slots, and stamps identifier nodes with scope info.
    /// </summary>
    private void AssignSlotsToUserVariables(int entryIndex)
    {
        var instructionScopes = new int[_instructions.Count];
        Array.Fill(instructionScopes, int.MinValue);

        var scopeParents = new Dictionary<int, int>();
        var scopeDeclarations = new Dictionary<int, Dictionary<Symbol, int>>();
        var scopeInstructionIndices = new Dictionary<int, List<int>>();

        // Function scope (ScopeId = 0) hosts execution plan slots.
        var functionDeclarations = new Dictionary<Symbol, int>(ReferenceEqualityComparer<Symbol>.Instance);
        for (var i = 0; i < _slotSymbols.Count; i++)
        {
            functionDeclarations[_slotSymbols[i]] = i;
        }
        scopeDeclarations[0] = functionDeclarations;

        var scopeStacksByInstruction = new Dictionary<int, ImmutableArray<int>>();
        var worklist = new Stack<(int index, ImmutableArray<int> scopeStack)>();
        worklist.Push((entryIndex, ImmutableArray.Create(0)));

        while (worklist.Count > 0)
        {
            var (index, scopeStack) = worklist.Pop();
            if (index < 0 || index >= _instructions.Count)
            {
                continue;
            }

            if (scopeStacksByInstruction.TryGetValue(index, out var existingStack))
            {
                // If the same instruction is reached with the same scope stack, we can skip it.
                // Conflicting stacks indicate unexpected control-flow shapes; keep the first seen.
                if (ScopeStacksEqual(existingStack, scopeStack))
                {
                    continue;
                }
                continue;
            }

            scopeStacksByInstruction[index] = scopeStack;

            var currentScopeId = scopeStack[^1];
            if (instructionScopes[index] == int.MinValue)
            {
                instructionScopes[index] = currentScopeId;
            }

            var instruction = _instructions[index];

            if (instruction is SimpleVariableDeclarationInstruction varDecl)
            {
                // Var declarations always bind in function scope; let/const bind in the current scope.
                var targetScopeId = varDecl.VarKind == VariableKind.Var ? 0 : currentScopeId;
                RegisterVariableDeclaration(scopeDeclarations, targetScopeId, varDecl.TargetSymbol);
            }

            switch (instruction)
            {
                case PushEnvironmentInstruction pushEnv:
                {
                    TrackScopeInstruction(scopeInstructionIndices, pushEnv.ScopeId, index);
                    RegisterScope(scopeDeclarations, scopeParents, pushEnv.ScopeId, currentScopeId, pushEnv.SlotMap,
                        pushEnv.PerIterationBindings);
                    worklist.Push((pushEnv.Next, scopeStack.Add(pushEnv.ScopeId)));
                    continue;
                }
                case EnterCatchInstruction enterCatch:
                {
                    TrackScopeInstruction(scopeInstructionIndices, enterCatch.ScopeId, index);
                    RegisterScope(scopeDeclarations, scopeParents, enterCatch.ScopeId, currentScopeId, enterCatch.SlotMap,
                        ImmutableArray<Symbol>.Empty);
                    worklist.Push((enterCatch.Next, scopeStack.Add(enterCatch.ScopeId)));
                    continue;
                }
                case EnterCatchWithDestructuringInstruction enterCatchDestructuring:
                {
                    TrackScopeInstruction(scopeInstructionIndices, enterCatchDestructuring.ScopeId, index);
                    RegisterScope(scopeDeclarations, scopeParents, enterCatchDestructuring.ScopeId, currentScopeId,
                        enterCatchDestructuring.SlotMap, ImmutableArray<Symbol>.Empty);
                    worklist.Push((enterCatchDestructuring.Next, scopeStack.Add(enterCatchDestructuring.ScopeId)));
                    continue;
                }
                case PopEnvironmentInstruction popEnv:
                {
                    var nextStack = scopeStack;
                    if (scopeStack.Length > 1 && scopeStack[^1] == popEnv.ScopeId)
                    {
                        nextStack = scopeStack.RemoveAt(scopeStack.Length - 1);
                    }

                    worklist.Push((popEnv.Next, nextStack));
                    continue;
                }
            }

            foreach (var successor in GetSuccessors(instruction))
            {
                worklist.Push((successor, scopeStack));
            }
        }

        var resolver = new ScopeAwareSlotResolver(scopeDeclarations, scopeParents);
        var rewriter = new ScopeAwareSlotRewriter(resolver);

        UpdateScopeInstructions(scopeDeclarations, scopeInstructionIndices);

        for (var i = 0; i < _instructions.Count; i++)
        {
            var currentScopeId = instructionScopes[i];
            if (currentScopeId == int.MinValue)
            {
                currentScopeId = 0;
            }

            _instructions[i] = rewriter.RewriteInstruction(_instructions[i], currentScopeId);
        }

        return;

        void RegisterScope(
            Dictionary<int, Dictionary<Symbol, int>> declarations,
            Dictionary<int, int> parents,
            int scopeId,
            int parentScopeId,
            ImmutableDictionary<Symbol, int> slotMap,
            ImmutableArray<Symbol> perIterationBindings)
        {
            if (!parents.TryGetValue(scopeId, out var existingParent))
            {
                parents[scopeId] = parentScopeId;
            }
            else if (existingParent != parentScopeId)
            {
                // Keep the first parent mapping to avoid conflicting scope graphs.
            }

            if (!declarations.TryGetValue(scopeId, out var scopeMap))
            {
                scopeMap = new Dictionary<Symbol, int>(ReferenceEqualityComparer<Symbol>.Instance);
                declarations[scopeId] = scopeMap;
            }

            foreach (var (symbol, slotIndex) in slotMap)
            {
                if (!scopeMap.ContainsKey(symbol))
                {
                    scopeMap[symbol] = slotIndex;
                }
            }

            if (!perIterationBindings.IsDefaultOrEmpty &&
                declarations.TryGetValue(parentScopeId, out var parentScopeMap))
            {
                foreach (var binding in perIterationBindings)
                {
                    if (scopeMap.ContainsKey(binding))
                    {
                        continue;
                    }

                    if (parentScopeMap.TryGetValue(binding, out var parentSlotIndex))
                    {
                        scopeMap[binding] = parentSlotIndex;
                    }
                    else
                    {
                        scopeMap[binding] = GetNextSlotIndex(scopeMap);
                    }
                }
            }
        }

        void RegisterVariableDeclaration(
            Dictionary<int, Dictionary<Symbol, int>> declarations,
            int scopeId,
            Symbol symbol)
        {
            if (!declarations.TryGetValue(scopeId, out var scopeMap))
            {
                scopeMap = new Dictionary<Symbol, int>(ReferenceEqualityComparer<Symbol>.Instance);
                declarations[scopeId] = scopeMap;
            }

            if (scopeMap.ContainsKey(symbol))
            {
                return;
            }

            if (scopeId == 0)
            {
                // Function-scope declarations become execution-plan slots.
                var slotIndex = AllocateSlot(symbol);
                scopeMap[symbol] = slotIndex;
                return;
            }

            // Non-function scopes get local slots so identifiers can resolve to their environment.
            scopeMap[symbol] = GetNextSlotIndex(scopeMap);
        }

        void TrackScopeInstruction(
            Dictionary<int, List<int>> scopeInstructions,
            int scopeId,
            int instructionIndex)
        {
            if (!scopeInstructions.TryGetValue(scopeId, out var indices))
            {
                indices = [];
                scopeInstructions[scopeId] = indices;
            }

            indices.Add(instructionIndex);
        }

        void UpdateScopeInstructions(
            Dictionary<int, Dictionary<Symbol, int>> declarations,
            Dictionary<int, List<int>> scopeInstructions)
        {
            foreach (var (scopeId, instructionIndices) in scopeInstructions)
            {
                if (!declarations.TryGetValue(scopeId, out var scopeMap))
                {
                    continue;
                }

                var slotCount = 0;
                var slotMapBuilder = ImmutableDictionary.CreateBuilder<Symbol, int>(
                    ReferenceEqualityComparer<Symbol>.Instance);
                foreach (var (symbol, slotIndex) in scopeMap)
                {
                    slotMapBuilder[symbol] = slotIndex;
                    if (slotIndex >= 0)
                    {
                        slotCount = Math.Max(slotCount, slotIndex + 1);
                    }
                }

                var updatedSlotMap = slotMapBuilder.ToImmutable();
                foreach (var instructionIndex in instructionIndices)
                {
                    switch (_instructions[instructionIndex])
                    {
                        case PushEnvironmentInstruction pushEnv:
                            _instructions[instructionIndex] = pushEnv with
                            {
                                SlotCount = slotCount,
                                SlotMap = updatedSlotMap
                            };
                            break;
                        case EnterCatchInstruction enterCatch:
                            _instructions[instructionIndex] = enterCatch with
                            {
                                SlotCount = slotCount,
                                SlotMap = updatedSlotMap
                            };
                            break;
                        case EnterCatchWithDestructuringInstruction enterCatchDestructuring:
                            _instructions[instructionIndex] = enterCatchDestructuring with
                            {
                                SlotCount = slotCount,
                                SlotMap = updatedSlotMap
                            };
                            break;
                    }
                }
            }
        }

        static int GetNextSlotIndex(Dictionary<Symbol, int> scopeMap)
        {
            var nextIndex = 0;
            foreach (var slotIndex in scopeMap.Values)
            {
                if (slotIndex >= nextIndex)
                {
                    nextIndex = slotIndex + 1;
                }
            }

            return nextIndex;
        }

        static IEnumerable<int> GetSuccessors(ExecutionInstruction instruction)
        {
            switch (instruction)
            {
                case BranchInstruction branch:
                    if (branch.ConsequentIndex >= 0)
                    {
                        yield return branch.ConsequentIndex;
                    }
                    if (branch.AlternateIndex >= 0)
                    {
                        yield return branch.AlternateIndex;
                    }
                    yield break;

                case JumpInstruction jump:
                    if (jump.TargetIndex >= 0)
                    {
                        yield return jump.TargetIndex;
                    }
                    yield break;

                case EnterTryInstruction enterTry:
                    if (enterTry.Next >= 0)
                    {
                        yield return enterTry.Next;
                    }
                    if (enterTry.HandlerIndex >= 0)
                    {
                        yield return enterTry.HandlerIndex;
                    }
                    if (enterTry.FinallyIndex >= 0)
                    {
                        yield return enterTry.FinallyIndex;
                    }
                    if (enterTry.EndFinallyIndex >= 0)
                    {
                        yield return enterTry.EndFinallyIndex;
                    }
                    yield break;

                case IteratorMoveNextInstruction moveNext:
                    if (moveNext.Next >= 0)
                    {
                        yield return moveNext.Next;
                    }
                    if (moveNext.BreakIndex >= 0)
                    {
                        yield return moveNext.BreakIndex;
                    }
                    yield break;

                case BreakInstruction breakInstruction:
                    if (breakInstruction.TargetIndex >= 0)
                    {
                        yield return breakInstruction.TargetIndex;
                    }
                    yield break;

                case ContinueInstruction continueInstruction:
                    if (continueInstruction.TargetIndex >= 0)
                    {
                        yield return continueInstruction.TargetIndex;
                    }
                    yield break;

                default:
                    if (instruction.Next >= 0)
                    {
                        yield return instruction.Next;
                    }
                    yield break;
            }
        }

        static bool ScopeStacksEqual(ImmutableArray<int> left, ImmutableArray<int> right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
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
