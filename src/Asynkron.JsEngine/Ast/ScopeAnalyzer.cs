using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Analyzes variable scopes in an AST and assigns slot indices for fast variable access.
/// This enables O(1) array-based variable lookup instead of dictionary-based lookup.
/// </summary>
public sealed class ScopeAnalyzer
{
    /// <summary>
    /// Information about a scope collected during analysis.
    /// </summary>
    public sealed class ScopeInfo(ScopeInfo? parent, bool isFunctionScope, bool isBlockScope, int scopeId)
    {
        public ScopeInfo? Parent { get; } = parent;
        private int Depth { get; } = parent is null ? 0 : parent.Depth + 1;
        public bool IsFunctionScope { get; } = isFunctionScope;
        public bool IsBlockScope { get; } = isBlockScope;
        public bool HasEval { get; set; }
        public bool HasWith { get; set; }
        public bool IsDynamic => HasEval || HasWith || (Parent?.IsDynamic ?? false);

        /// <summary>
        /// True if any inner functions capture variables from this scope.
        /// When true, environment reuse optimization is disabled for this function.
        /// </summary>
        public bool HasClosures { get; set; }

        /// <summary>
        /// Unique ID for this scope, used to match variables to their declaring scope at runtime.
        /// </summary>
        public int ScopeId { get; } = scopeId;

        private readonly Dictionary<Symbol, int> _variables = new(ReferenceEqualityComparer<Symbol>.Instance);
        private HashSet<Symbol>? _immutableBindings;
        private int _nextSlot;

        /// <summary>
        /// The number of slots allocated in this scope.
        /// </summary>
        public int SlotCount => _nextSlot;

        /// <summary>
        /// Declares a variable in this scope and assigns it a slot index.
        /// </summary>
        public int DeclareVariable(Symbol name)
        {
            if (_variables.TryGetValue(name, out var existingSlot))
            {
                return existingSlot; // Already declared (e.g., var hoisting)
            }

            var slot = _nextSlot++;
            _variables[name] = slot;
            return slot;
        }

        /// <summary>
        /// Declares an immutable variable in this scope (e.g., named function expression name).
        /// </summary>
        public int DeclareImmutableVariable(Symbol name)
        {
            var slot = DeclareVariable(name);
            _immutableBindings ??= new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
            _immutableBindings.Add(name);
            return slot;
        }

        /// <summary>
        /// Checks if a variable is an immutable binding.
        /// </summary>
        public bool IsImmutable(Symbol name) => _immutableBindings?.Contains(name) ?? false;

        /// <summary>
        /// Gets the slot index for a variable declared in this scope.
        /// Returns -1 if not found in this scope.
        /// </summary>
        public int GetSlot(Symbol name)
        {
            return _variables.TryGetValue(name, out var slot) ? slot : -1;
        }

        /// <summary>
        /// Checks if a variable is declared in this scope.
        /// </summary>
        public bool HasVariable(Symbol name) => _variables.ContainsKey(name);

        /// <summary>
        /// Gets all variable names declared in this scope.
        /// </summary>
        public IEnumerable<Symbol> GetVariables() => _variables.Keys;
    }

    private ScopeInfo? _currentScope;
    private ScopeInfo? _currentFunctionScope;
    private int _nextScopeId;

    /// <summary>
    /// Analyzes a program and returns a new program with resolved slot indices.
    /// </summary>
    public ProgramNode Analyze(ProgramNode program)
    {
        // Start with global/module scope (ScopeId 0 is reserved for global)
        _currentScope = new ScopeInfo(null, isFunctionScope: true, isBlockScope: false, scopeId: _nextScopeId++);

        // First pass: collect all variable declarations
        CollectDeclarations(program);

        // Second pass: resolve identifier references
        var resolvedStatements = ResolveStatements(program.Body);

        // Capture slot count and scope ID from the program scope
        var slotCount = _currentScope!.IsDynamic ? -1 : _currentScope.SlotCount;
        var scopeId = _currentScope!.IsDynamic ? -1 : _currentScope.ScopeId;

        return program with { Body = resolvedStatements, ScopeId = scopeId, SlotCount = slotCount };
    }

    /// <summary>
    /// Analyzes a function expression and returns a new one with resolved slot indices.
    /// </summary>
    /// <param name="function">The function to analyze.</param>
    /// <param name="isDeclaration">True if this is a FunctionDeclaration (name bound in outer scope),
    /// false if it's a named FunctionExpression (name bound inside for self-reference).</param>
    public FunctionExpression AnalyzeFunction(FunctionExpression function, bool isDeclaration = false)
    {
        var originalParentScope = _currentScope;
        var parentFunctionScope = _currentFunctionScope;

        // For named function expressions, create an intermediate scope for the function name
        // Per ECMAScript spec, the function name is bound in a scope between the outer scope
        // and the parameter scope, so it's visible inside but not outside
        // The binding is immutable (per ES spec 9.2.10 SetFunctionName): in strict mode,
        // assignment throws TypeError; in non-strict mode, assignment is silently ignored.
        ScopeInfo? functionNameScope = null;
        var functionNameScopeId = -1;
        var scopeParent = originalParentScope;
        if (function.Name is not null && !isDeclaration)
        {
            functionNameScope = new ScopeInfo(originalParentScope, isFunctionScope: false, isBlockScope: false, scopeId: _nextScopeId++);
            functionNameScope.DeclareImmutableVariable(function.Name);
            functionNameScopeId = functionNameScope.ScopeId;
            scopeParent = functionNameScope; // Parameter scope's parent is the function name scope
        }

        // Create the parameter/function body scope
        _currentScope = new ScopeInfo(scopeParent, isFunctionScope: true, isBlockScope: false, scopeId: _nextScopeId++);
        _currentFunctionScope = _currentScope;

        // Declare parameters
        foreach (var param in function.Parameters)
        {
            if (param.Name is not null)
            {
                _currentScope.DeclareVariable(param.Name);
            }
            // Handle destructuring patterns
            if (param.Pattern is not null)
            {
                CollectBindingDeclarations(param.Pattern);
            }
        }

        // Resolve parameter default expressions - these may contain function expressions
        // that need to capture the function name binding. Per ECMAScript spec, parameter
        // defaults are evaluated in a scope where the function name is visible.
        var resolvedParameters = ResolveParameters(function.Parameters);

        // Collect var declarations (hoisted)
        CollectVarDeclarations(function.Body);

        // Resolve the body
        var resolvedBody = ResolveBlockStatement(function.Body);

        // Capture slot count, scope ID, and closure info before leaving scope
        var slotCount = _currentScope!.IsDynamic ? -1 : _currentScope.SlotCount;
        var scopeId = _currentScope!.IsDynamic ? -1 : _currentScope.ScopeId;
        var hasClosures = _currentScope.HasClosures || _currentScope.IsDynamic;
        var slotMap = CreateSlotMap(_currentScope);

        _currentScope = originalParentScope;
        _currentFunctionScope = parentFunctionScope;

        return function with
        {
            Parameters = resolvedParameters,
            Body = resolvedBody,
            SlotCount = slotCount,
            ScopeId = scopeId,
            HasClosures = hasClosures,
            FunctionNameScopeId = functionNameScopeId,
            SlotMap = slotMap
        };
    }

    private ImmutableArray<FunctionParameter> ResolveParameters(ImmutableArray<FunctionParameter> parameters)
    {
        var hasChanges = false;
        var builder = ImmutableArray.CreateBuilder<FunctionParameter>(parameters.Length);

        foreach (var param in parameters)
        {
            ExpressionNode? resolvedDefault = null;
            if (param.DefaultValue is not null)
            {
                resolvedDefault = ResolveExpression(param.DefaultValue);
                hasChanges = true;
            }

            // TODO: Resolve destructuring patterns if they contain expressions

            if (resolvedDefault is not null)
            {
                builder.Add(param with { DefaultValue = resolvedDefault });
            }
            else
            {
                builder.Add(param);
            }
        }

        return hasChanges ? builder.ToImmutable() : parameters;
    }

    #region Declaration Collection

    private void CollectDeclarations(ProgramNode program)
    {
        // Collect var declarations (hoisted to function scope)
        foreach (var statement in program.Body)
        {
            CollectVarDeclarations(statement);
        }

        // Collect let/const declarations
        foreach (var statement in program.Body)
        {
            CollectLexicalDeclarations(statement);
        }
    }

    private void CollectVarDeclarations(StatementNode statement)
    {
        while (true)
        {
            switch (statement)
            {
                case VariableDeclaration { Kind: VariableKind.Var } varDecl:
                    foreach (var declarator in varDecl.Declarators)
                    {
                        CollectBindingDeclarations(declarator.Target);
                    }

                    break;

                case FunctionDeclaration funcDecl:
                    _currentScope!.DeclareVariable(funcDecl.Name);
                    break;

                case BlockStatement block:
                    foreach (var stmt in block.Statements)
                    {
                        CollectVarDeclarations(stmt);
                    }

                    break;

                case IfStatement ifStmt:
                    CollectVarDeclarations(ifStmt.Then);
                    if (ifStmt.Else is not null)
                    {
                        statement = ifStmt.Else;
                        continue;
                    }

                    break;

                case WhileStatement whileStmt:
                    statement = whileStmt.Body;
                    continue;

                case DoWhileStatement doWhileStmt:
                    statement = doWhileStmt.Body;
                    continue;

                case ForStatement forStmt:
                    if (forStmt.Initializer is VariableDeclaration { Kind: VariableKind.Var } initVar)
                    {
                        foreach (var declarator in initVar.Declarators)
                        {
                            CollectBindingDeclarations(declarator.Target);
                        }
                    }

                    statement = forStmt.Body;
                    continue;

                case ForEachStatement forEachStmt:
                    if (forEachStmt.DeclarationKind == VariableKind.Var)
                    {
                        CollectBindingDeclarations(forEachStmt.Target);
                    }

                    statement = forEachStmt.Body;
                    continue;

                case TryStatement tryStmt:
                    CollectVarDeclarations(tryStmt.TryBlock);
                    if (tryStmt.Catch is not null)
                    {
                        CollectVarDeclarations(tryStmt.Catch.Body);
                    }

                    if (tryStmt.Finally is not null)
                    {
                        statement = tryStmt.Finally;
                        continue;
                    }

                    break;

                case SwitchStatement switchStmt:
                    foreach (var switchCase in switchStmt.Cases)
                    {
                        CollectVarDeclarations(switchCase.Body);
                    }

                    break;

                case LabeledStatement labeledStmt:
                    statement = labeledStmt.Statement;
                    continue;

                case WithStatement withStmt:
                    _currentScope!.HasWith = true;
                    statement = withStmt.Body;
                    continue;
            }

            break;
        }
    }

    private void CollectLexicalDeclarations(StatementNode statement)
    {
        switch (statement)
        {
            case VariableDeclaration { Kind: VariableKind.Let or VariableKind.Const } lexDecl:
                foreach (var declarator in lexDecl.Declarators)
                {
                    CollectBindingDeclarations(declarator.Target);
                }
                break;

            case ClassDeclaration classDecl:
                _currentScope!.DeclareVariable(classDecl.Name);
                break;
        }
    }

    private void CollectBindingDeclarations(BindingTarget target)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding identifier:
                    _currentScope!.DeclareVariable(identifier.Name);
                    break;

                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null)
                        {
                            CollectBindingDeclarations(element.Target);
                        }
                    }

                    if (arrayBinding.RestElement is not null)
                    {
                        target = arrayBinding.RestElement;
                        continue;
                    }

                    break;

                case ObjectBinding objectBinding:
                    foreach (var prop in objectBinding.Properties)
                    {
                        if (prop.Target is not null)
                        {
                            CollectBindingDeclarations(prop.Target);
                        }
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        target = objectBinding.RestElement;
                        continue;
                    }

                    break;
            }

            break;
        }
    }

    private static void CollectBindingSlotIndices(BindingTarget target, ScopeInfo scope, List<int> indices)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding identifier:
                    indices.Add(scope.GetSlot(identifier.Name));
                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null)
                        {
                            CollectBindingSlotIndices(element.Target, scope, indices);
                        }
                    }
                    if (arrayBinding.RestElement is not null)
                    {
                        target = arrayBinding.RestElement;
                        continue;
                    }
                    break;
                case ObjectBinding objectBinding:
                    foreach (var prop in objectBinding.Properties)
                    {
                        if (prop.Target is not null)
                        {
                            CollectBindingSlotIndices(prop.Target, scope, indices);
                        }
                    }
                    if (objectBinding.RestElement is not null)
                    {
                        target = objectBinding.RestElement;
                        continue;
                    }
                    break;
            }

            break;
        }
    }

    private static void CollectBindingNamesInOrder(BindingTarget target, List<Symbol> names)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding identifier:
                    names.Add(identifier.Name);
                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null)
                        {
                            CollectBindingNamesInOrder(element.Target, names);
                        }
                    }
                    if (arrayBinding.RestElement is not null)
                    {
                        target = arrayBinding.RestElement;
                        continue;
                    }
                    break;
                case ObjectBinding objectBinding:
                    foreach (var prop in objectBinding.Properties)
                    {
                        if (prop.Target is not null)
                        {
                            CollectBindingNamesInOrder(prop.Target, names);
                        }
                    }
                    if (objectBinding.RestElement is not null)
                    {
                        target = objectBinding.RestElement;
                        continue;
                    }
                    break;
            }

            break;
        }
    }

    #endregion

    #region Identifier Resolution

    private (int depth, int slot, int scopeId, bool isImmutable) ResolveIdentifier(Symbol name)
    {
        var scope = _currentScope;
        var depth = 0;

        while (scope is not null)
        {
            // If we hit a dynamic scope, we can't resolve statically.
            // But we still need to mark enclosing function scopes as potentially
            // having closures, since inner functions with dynamic scope may capture
            // variables from outer scopes at runtime.
            if (scope.IsDynamic)
            {
                // Mark all enclosing function scopes as having closures
                // since we can't statically determine which variables are captured
                var parentScope = scope.Parent;
                while (parentScope is not null)
                {
                    if (parentScope.IsFunctionScope)
                    {
                        parentScope.HasClosures = true;
                    }
                    parentScope = parentScope.Parent;
                }
                return (-1, -1, -1, false);
            }

            var slot = scope.GetSlot(name);
            if (slot >= 0)
            {
                // If we crossed function boundaries to find this variable,
                // mark the declaring scope as having closures (an inner function
                // captures variables from this scope).
                if (depth > 0 && scope.IsFunctionScope)
                {
                    scope.HasClosures = true;
                }

                var isImmutable = scope.IsImmutable(name);
                return (depth, slot, scope.ScopeId, isImmutable);
            }

            // Only increment depth when crossing function boundaries
            if (scope.IsFunctionScope && scope.Parent is not null)
            {
                depth++;
            }

            scope = scope.Parent;
        }

        // Not found - could be a global or undeclared
        return (-1, -1, -1, false);
    }

    private static ImmutableDictionary<Symbol, int> CreateSlotMap(ScopeInfo scope)
    {
        if (scope.IsDynamic || scope.SlotCount <= 0)
        {
            return ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);
        }

        var builder = ImmutableDictionary.CreateBuilder<Symbol, int>(ReferenceEqualityComparer<Symbol>.Instance);
        foreach (var variable in scope.GetVariables())
        {
            var slotIndex = scope.GetSlot(variable);
            if (slotIndex >= 0)
            {
                builder[variable] = slotIndex;
            }
        }

        return builder.Count == 0
            ? ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance)
            : builder.ToImmutable();
    }

    private ImmutableArray<StatementNode> ResolveStatements(ImmutableArray<StatementNode> statements)
    {
        var builder = ImmutableArray.CreateBuilder<StatementNode>(statements.Length);
        foreach (var statement in statements)
        {
            builder.Add(ResolveStatement(statement));
        }
        return builder.MoveToImmutable();
    }

    private StatementNode ResolveStatement(StatementNode statement)
    {
        return statement switch
        {
            ExpressionStatement exprStmt => exprStmt with
            {
                Expression = ResolveExpression(exprStmt.Expression)
            },

            VariableDeclaration varDecl => ResolveVariableDeclaration(varDecl),

            BlockStatement block => ResolveBlockStatement(block),

            IfStatement ifStmt => ifStmt with
            {
                Condition = ResolveExpression(ifStmt.Condition),
                Then = ResolveStatement(ifStmt.Then),
                Else = ifStmt.Else is not null ? ResolveStatement(ifStmt.Else) : null
            },

            WhileStatement whileStmt => whileStmt with
            {
                Condition = ResolveExpression(whileStmt.Condition),
                Body = ResolveStatement(whileStmt.Body)
            },

            DoWhileStatement doWhileStmt => doWhileStmt with
            {
                Body = ResolveStatement(doWhileStmt.Body),
                Condition = ResolveExpression(doWhileStmt.Condition)
            },

            ForStatement forStmt => ResolveForStatement(forStmt),

            ForEachStatement forEachStmt => ResolveForEachStatement(forEachStmt),

            ReturnStatement returnStmt => ResolveReturnStatement(returnStmt),

            ThrowStatement throwStmt => throwStmt with
            {
                Expression = ResolveExpression(throwStmt.Expression)
            },

            TryStatement tryStmt => ResolveTryStatement(tryStmt),

            SwitchStatement switchStmt => ResolveSwitchStatement(switchStmt),

            FunctionDeclaration funcDecl => funcDecl with
            {
                Function = AnalyzeFunction(funcDecl.Function, isDeclaration: true)
            },

            ClassDeclaration classDecl => ResolveClassDeclaration(classDecl),

            LabeledStatement labeledStmt => labeledStmt with
            {
                Statement = ResolveStatement(labeledStmt.Statement)
            },

            WithStatement withStmt => withStmt with
            {
                Object = ResolveExpression(withStmt.Object),
                Body = ResolveStatement(withStmt.Body)
            },

            // Statements that don't contain expressions to resolve
            BreakStatement or ContinueStatement or EmptyStatement => statement,

            _ => statement // Default: return as-is
        };
    }

    private BlockStatement ResolveBlockStatement(BlockStatement block)
    {
        // Create block scope for let/const
        var parentScope = _currentScope;
        _currentScope = new ScopeInfo(parentScope, isFunctionScope: false, isBlockScope: true, scopeId: _nextScopeId++);

        // Collect block-scoped declarations
        foreach (var stmt in block.Statements)
        {
            CollectLexicalDeclarations(stmt);
        }

        var resolvedStatements = ResolveStatements(block.Statements);

        var slotCount = _currentScope!.IsDynamic ? -1 : _currentScope.SlotCount;
        var scopeId = _currentScope!.IsDynamic ? -1 : _currentScope.ScopeId;
        var slotMap = CreateSlotMap(_currentScope);

        _currentScope = parentScope;
        return block with
        {
            Statements = resolvedStatements,
            ScopeId = scopeId,
            SlotCount = slotCount,
            SlotMap = slotMap
        };
    }

    private ReturnStatement ResolveReturnStatement(ReturnStatement returnStmt)
    {
        if (returnStmt.Expression is null)
        {
            return returnStmt;
        }

        // Check if environment reuse is possible for this function
        var canOptimize = _currentFunctionScope?.IsDynamic == false &&
                          !_currentFunctionScope.HasClosures;

        if (!canOptimize)
        {
            return returnStmt with { Expression = ResolveExpression(returnStmt.Expression) };
        }

        // Check if we have a binary expression with exactly 2 direct call expressions
        // In this case, we can use argument pre-evaluation to enable environment reuse for ALL calls
        if (returnStmt.Expression is BinaryExpression binary &&
            binary.Operator is not (BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr or BinaryOperator.NullishCoalescing) &&
            binary.Left is CallExpression leftCall &&
            binary.Right is CallExpression rightCall &&
            leftCall.Arguments.Length == 1 && !leftCall.Arguments[0].IsSpread &&
            rightCall.Arguments.Length == 1 && !rightCall.Arguments[0].IsSpread)
        {
            // Mark both calls for environment reuse and set the pre-evaluation flag
            var resolvedLeftCall = ResolveCallExpression(leftCall) with { CanReuseCallerEnvironment = true };
            var resolvedRightCall = ResolveCallExpression(rightCall) with { CanReuseCallerEnvironment = true };
            var resolvedBinary = binary with { Left = resolvedLeftCall, Right = resolvedRightCall };
            return returnStmt with { Expression = resolvedBinary, UseArgumentPreEvaluation = true };
        }

        // Check if we have call * identifier pattern (e.g., return __func(arg-1) * arg)
        // Pre-evaluate the identifier before the call to preserve its value
        if (returnStmt.Expression is BinaryExpression binaryCallId &&
            binaryCallId.Operator is not (BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr or BinaryOperator.NullishCoalescing) &&
            binaryCallId.Left is CallExpression callLeft &&
            binaryCallId.Right is IdentifierExpression idRight &&
            callLeft.Arguments.Length == 1 && !callLeft.Arguments[0].IsSpread)
        {
            var resolvedCall = ResolveCallExpression(callLeft) with { CanReuseCallerEnvironment = true };
            var resolvedId = ResolveIdentifierExpression(idRight);
            var resolvedBinary = binaryCallId with { Left = resolvedCall, Right = resolvedId };
            return returnStmt with { Expression = resolvedBinary, UseArgumentPreEvaluation = true };
        }

        // Standard optimization path (marks only rightmost call for simple cases)
        var resolvedExpr = ResolveReturnExpressionWithReuse(returnStmt.Expression);
        return returnStmt with { Expression = resolvedExpr };
    }

    /// <summary>
    /// Resolves a return expression and marks eligible call expressions for environment reuse.
    /// A call is eligible if no scope variables are referenced after its arguments are evaluated.
    /// </summary>
    private ExpressionNode ResolveReturnExpressionWithReuse(ExpressionNode expression)
    {
        // For a direct call expression (tail call position), we can definitely reuse
        // since there's nothing after the call returns.
        if (expression is CallExpression call)
        {
            var resolvedCall = ResolveCallExpression(call);
            // Mark as eligible for environment reuse
            return resolvedCall with { CanReuseCallerEnvironment = true };
        }

        // For conditional expressions, both branches could potentially reuse
        if (expression is ConditionalExpression cond)
        {
            var resolvedTest = ResolveExpression(cond.Test);
            var resolvedConsequent = ResolveReturnExpressionWithReuse(cond.Consequent);
            var resolvedAlternate = ResolveReturnExpressionWithReuse(cond.Alternate);
            return cond with
            {
                Test = resolvedTest,
                Consequent = resolvedConsequent,
                Alternate = resolvedAlternate
            };
        }

        // For logical expressions (&&, ||, ??), the right side could be a tail call
        if (expression is BinaryExpression { Operator: BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr or BinaryOperator.NullishCoalescing } logical)
        {
            var resolvedLeft = ResolveExpression(logical.Left);
            var resolvedRight = ResolveReturnExpressionWithReuse(logical.Right);
            return logical with { Left = resolvedLeft, Right = resolvedRight };
        }

        // For binary expressions like fib(n-1) + fib(n-2), analyze variable liveness
        if (expression is BinaryExpression binary)
        {
            return ResolveBinaryExpressionWithReuse(binary);
        }

        // For sequence expressions, only the last expression matters for return
        if (expression is SequenceExpression seq)
        {
            var resolvedLeft = ResolveExpression(seq.Left);
            var resolvedRight = ResolveReturnExpressionWithReuse(seq.Right);
            return seq with { Left = resolvedLeft, Right = resolvedRight };
        }

        // Default: just resolve normally
        return ResolveExpression(expression);
    }

    /// <summary>
    /// Resolves a binary expression in a return context, analyzing which calls can reuse the environment.
    /// Currently only the RIGHTMOST call can safely reuse the environment. To enable ALL calls to reuse,
    /// we would need to pre-evaluate all arguments before making any calls - this requires a specialized
    /// return expression evaluator.
    /// </summary>
    private ExpressionNode ResolveBinaryExpressionWithReuse(BinaryExpression binary)
    {
        // Count how many calls are in this expression
        var callCount = CountCallExpressions(binary);

        // If there's only one call, we can mark it for reuse regardless of position
        if (callCount == 1)
        {
            var resolvedLeft = MarkCallsForReuse(ResolveExpression(binary.Left));
            var resolvedRight = MarkCallsForReuse(ResolveExpression(binary.Right));
            return binary with { Left = resolvedLeft, Right = resolvedRight };
        }

        // For multiple calls, only mark the rightmost for reuse (safe because nothing after it needs the environment).
        // NOTE: To enable ALL calls to reuse, we need argument pre-evaluation. See ReturnStatementExtensions.
        var resolvedLeftNormal = ResolveExpression(binary.Left);

        ExpressionNode resolvedRightWithReuse;
        if (binary.Right is CallExpression rightCall)
        {
            var resolvedRightCall = ResolveCallExpression(rightCall);
            resolvedRightWithReuse = resolvedRightCall with { CanReuseCallerEnvironment = true };
        }
        else if (binary.Right is BinaryExpression rightBinary)
        {
            resolvedRightWithReuse = ResolveBinaryExpressionWithReuse(rightBinary);
        }
        else
        {
            resolvedRightWithReuse = ResolveExpression(binary.Right);
        }

        return binary with { Left = resolvedLeftNormal, Right = resolvedRightWithReuse };
    }

    /// <summary>
    /// Counts the number of CallExpression nodes in an expression tree.
    /// </summary>
    private static int CountCallExpressions(ExpressionNode expression)
    {
        return expression switch
        {
            CallExpression => 1,
            BinaryExpression binary => CountCallExpressions(binary.Left) + CountCallExpressions(binary.Right),
            UnaryExpression unary => CountCallExpressions(unary.Operand),
            ConditionalExpression cond => CountCallExpressions(cond.Test) +
                                          CountCallExpressions(cond.Consequent) +
                                          CountCallExpressions(cond.Alternate),
            _ => 0
        };
    }

    /// <summary>
    /// Recursively marks all CallExpression nodes in the expression tree for environment reuse.
    /// Only safe when arguments have been pre-evaluated or there's only one call.
    /// </summary>
    private static ExpressionNode MarkCallsForReuse(ExpressionNode expression)
    {
        return expression switch
        {
            CallExpression call => call with { CanReuseCallerEnvironment = true },

            BinaryExpression binary => binary with
            {
                Left = MarkCallsForReuse(binary.Left),
                Right = MarkCallsForReuse(binary.Right)
            },

            UnaryExpression unary => unary with
            {
                Operand = MarkCallsForReuse(unary.Operand)
            },

            ConditionalExpression cond => cond with
            {
                Test = MarkCallsForReuse(cond.Test),
                Consequent = MarkCallsForReuse(cond.Consequent),
                Alternate = MarkCallsForReuse(cond.Alternate)
            },

            // For other expression types, return as-is (they don't contain calls we can optimize)
            _ => expression
        };
    }

    private static void CollectVariablesRecursive(ExpressionNode expression, HashSet<Symbol> variables)
    {
        while (true)
        {
            switch (expression)
            {
                case IdentifierExpression id:
                    variables.Add(id.Name);
                    break;

                case BinaryExpression binary:
                    CollectVariablesRecursive(binary.Left, variables);
                    expression = binary.Right;
                    continue;

                case UnaryExpression unary:
                    expression = unary.Operand;
                    continue;

                case CallExpression call:
                    CollectVariablesRecursive(call.Callee, variables);
                    foreach (var arg in call.Arguments)
                    {
                        CollectVariablesRecursive(arg.Expression, variables);
                    }

                    break;

                case MemberExpression member:
                    CollectVariablesRecursive(member.Target, variables);
                    if (member.IsComputed)
                    {
                        expression = member.Property;
                        continue;
                    }

                    break;

                case ConditionalExpression cond:
                    CollectVariablesRecursive(cond.Test, variables);
                    CollectVariablesRecursive(cond.Consequent, variables);
                    expression = cond.Alternate;
                    continue;

                case ArrayExpression array:
                    foreach (var element in array.Elements)
                    {
                        if (element.Expression is not null)
                        {
                            CollectVariablesRecursive(element.Expression, variables);
                        }
                    }

                    break;

                case ObjectExpression obj:
                    foreach (var member in obj.Members)
                    {
                        if (member.Value is not null)
                        {
                            CollectVariablesRecursive(member.Value, variables);
                        }
                    }

                    break;

                case SequenceExpression seq:
                    CollectVariablesRecursive(seq.Left, variables);
                    expression = seq.Right;
                    continue;

                case AssignmentExpression assign:
                    variables.Add(assign.Target);
                    expression = assign.Value;
                    continue;

                case AwaitExpression await:
                    expression = await.Expression;
                    continue;

                case YieldExpression yield when yield.Expression is not null:
                    expression = yield.Expression;
                    continue;

                // Literals and other expressions that don't reference variables
                case LiteralExpression or ThisExpression or SuperExpression or NewTargetExpression or ImportMetaExpression or RegexLiteralExpression or FunctionExpression:
                    // These don't reference scope variables directly
                    break;
            }

            break;
        }
    }

    private VariableDeclaration ResolveVariableDeclaration(VariableDeclaration varDecl)
    {
        var resolvedDeclarators = ImmutableArray.CreateBuilder<VariableDeclarator>(varDecl.Declarators.Length);

        foreach (var declarator in varDecl.Declarators)
        {
            var resolvedInitializer = declarator.Initializer is not null
                ? ResolveExpression(declarator.Initializer)
                : null;

            resolvedDeclarators.Add(declarator with { Initializer = resolvedInitializer });
        }

        return varDecl with { Declarators = resolvedDeclarators.MoveToImmutable() };
    }

    private ForStatement ResolveForStatement(ForStatement forStmt)
    {
        // For statements with let/const create a new scope
        var needsScope = forStmt.Initializer is VariableDeclaration { Kind: VariableKind.Let or VariableKind.Const };

        var perIterationScopeId = -1;
        var perIterationSlotCount = -1;
        List<int>? perIterationSlotIndices = null;

        var parentScope = _currentScope;
        if (needsScope)
        {
            _currentScope = new ScopeInfo(parentScope, isFunctionScope: false, isBlockScope: true, scopeId: _nextScopeId++);

            // Collect the loop variable declaration
            if (forStmt.Initializer is VariableDeclaration initDecl)
            {
                foreach (var declarator in initDecl.Declarators)
                {
                    CollectBindingDeclarations(declarator.Target);
                }
            }

            perIterationScopeId = _currentScope.ScopeId;
            perIterationSlotCount = _currentScope.SlotCount;

            // Capture slot indices for each declared binding (in declaration order)
            perIterationSlotIndices = new List<int>();
            if (forStmt.Initializer is VariableDeclaration initDecl2)
            {
                foreach (var declarator in initDecl2.Declarators)
                {
                    CollectBindingSlotIndices(declarator.Target, _currentScope, perIterationSlotIndices);
                }
            }
        }

        var resolvedInit = forStmt.Initializer switch
        {
            VariableDeclaration varDecl => ResolveVariableDeclaration(varDecl),
            ExpressionStatement exprStmt => exprStmt with { Expression = ResolveExpression(exprStmt.Expression) },
            null => null,
            _ => forStmt.Initializer
        };

        var resolved = forStmt with
        {
            Initializer = resolvedInit,
            Condition = forStmt.Condition is not null ? ResolveExpression(forStmt.Condition) : null,
            Increment = forStmt.Increment is not null ? ResolveExpression(forStmt.Increment) : null,
            Body = ResolveStatement(forStmt.Body)
        };

        if (needsScope)
        {
            _currentScope = parentScope;
        }

        return resolved with
        {
            PerIterationScopeId = perIterationScopeId,
            PerIterationSlotCount = perIterationSlotCount,
            PerIterationSlotIndices = perIterationSlotIndices?.ToImmutableArray() ?? default
        };
    }

    private ForEachStatement ResolveForEachStatement(ForEachStatement forEachStmt)
    {
        var needsScope = forEachStmt.DeclarationKind is VariableKind.Let or VariableKind.Const
            or VariableKind.Using or VariableKind.AwaitUsing;

        var perIterationScopeId = -1;
        var perIterationSlotCount = -1;
        List<int>? perIterationSlotIndices = null;
        List<Symbol>? perIterationBindings = null;

        var parentScope = _currentScope;
        if (needsScope)
        {
            _currentScope = new ScopeInfo(parentScope, isFunctionScope: false, isBlockScope: true, scopeId: _nextScopeId++);
            CollectBindingDeclarations(forEachStmt.Target);

            perIterationScopeId = _currentScope.ScopeId;
            perIterationSlotCount = _currentScope.SlotCount;
            perIterationSlotIndices = new List<int>();
            CollectBindingSlotIndices(forEachStmt.Target, _currentScope, perIterationSlotIndices);

            perIterationBindings = new List<Symbol>();
            CollectBindingNamesInOrder(forEachStmt.Target, perIterationBindings);
        }

        var resolved = forEachStmt with
        {
            Iterable = ResolveExpression(forEachStmt.Iterable),
            Body = ResolveStatement(forEachStmt.Body)
        };

        if (needsScope)
        {
            _currentScope = parentScope;
        }

        return resolved with
        {
            PerIterationScopeId = perIterationScopeId,
            PerIterationSlotCount = perIterationSlotCount,
            PerIterationSlotIndices = perIterationSlotIndices?.ToImmutableArray() ?? default,
            PerIterationBindings = perIterationBindings?.ToImmutableArray() ?? default
        };
    }

    private TryStatement ResolveTryStatement(TryStatement tryStmt)
    {
        var resolvedTry = ResolveBlockStatement(tryStmt.TryBlock);

        CatchClause? resolvedCatch = null;
        if (tryStmt.Catch is not null)
        {
            var parentScope = _currentScope;
            _currentScope = new ScopeInfo(parentScope, isFunctionScope: false, isBlockScope: true, scopeId: _nextScopeId++);

            if (tryStmt.Catch.Binding is not null)
            {
                CollectBindingDeclarations(tryStmt.Catch.Binding);
            }

            var resolvedCatchBody = ResolveBlockStatement(tryStmt.Catch.Body);
            resolvedCatch = tryStmt.Catch with { Body = resolvedCatchBody };

            _currentScope = parentScope;
        }

        var resolvedFinally = tryStmt.Finally is not null ? ResolveBlockStatement(tryStmt.Finally) : null;

        return tryStmt with
        {
            TryBlock = resolvedTry,
            Catch = resolvedCatch,
            Finally = resolvedFinally
        };
    }

    private SwitchStatement ResolveSwitchStatement(SwitchStatement switchStmt)
    {
        var parentScope = _currentScope;
        _currentScope = new ScopeInfo(parentScope, isFunctionScope: false, isBlockScope: true, scopeId: _nextScopeId++);

        // Collect declarations from all cases
        foreach (var switchCase in switchStmt.Cases)
        {
            foreach (var stmt in switchCase.Body.Statements)
            {
                CollectLexicalDeclarations(stmt);
            }
        }

        var resolvedCases = ImmutableArray.CreateBuilder<SwitchCase>(switchStmt.Cases.Length);
        foreach (var switchCase in switchStmt.Cases)
        {
            var resolvedTest = switchCase.Test is not null ? ResolveExpression(switchCase.Test) : null;
            var resolvedBody = switchCase.Body with { Statements = ResolveStatements(switchCase.Body.Statements) };
            resolvedCases.Add(switchCase with { Test = resolvedTest, Body = resolvedBody });
        }

        _currentScope = parentScope;

        return switchStmt with
        {
            Discriminant = ResolveExpression(switchStmt.Discriminant),
            Cases = resolvedCases.MoveToImmutable()
        };
    }

    private ClassDeclaration ResolveClassDeclaration(ClassDeclaration classDecl)
    {
        // Resolve superclass expression
        var resolvedDefinition = classDecl.Definition with
        {
            Extends = classDecl.Definition.Extends is not null
                ? ResolveExpression(classDecl.Definition.Extends)
                : null
        };

        return classDecl with { Definition = resolvedDefinition };
    }

    #endregion

    #region Expression Resolution

    private ExpressionNode ResolveExpression(ExpressionNode expression)
    {
        return expression switch
        {
            IdentifierExpression identifier => ResolveIdentifierExpression(identifier),

            AssignmentExpression assignment => ResolveAssignmentExpression(assignment),

            BinaryExpression binary => binary with
            {
                Left = ResolveExpression(binary.Left),
                Right = ResolveExpression(binary.Right)
            },

            UnaryExpression unary => unary with
            {
                Operand = ResolveExpression(unary.Operand)
            },

            CallExpression call => ResolveCallExpression(call),

            MemberExpression member => member with
            {
                Target = ResolveExpression(member.Target),
                Property = member.IsComputed ? ResolveExpression(member.Property) : member.Property
            },

            NewExpression newExpr => newExpr with
            {
                Constructor = ResolveExpression(newExpr.Constructor),
                Arguments = ResolveCallArguments(newExpr.Arguments)
            },

            ConditionalExpression cond => cond with
            {
                Test = ResolveExpression(cond.Test),
                Consequent = ResolveExpression(cond.Consequent),
                Alternate = ResolveExpression(cond.Alternate)
            },

            ArrayExpression array => array with
            {
                Elements = ResolveArrayElements(array.Elements)
            },

            ObjectExpression obj => ResolveObjectExpression(obj),

            FunctionExpression func => AnalyzeFunction(func),

            ClassExpression classExpr => classExpr, // TODO: resolve class body

            SequenceExpression seq => seq with
            {
                Left = ResolveExpression(seq.Left),
                Right = ResolveExpression(seq.Right)
            },

            TemplateLiteralExpression template => ResolveTemplateLiteral(template),

            TaggedTemplateExpression tagged => ResolveTaggedTemplate(tagged),

            AwaitExpression awaitExpr => awaitExpr with
            {
                Expression = ResolveExpression(awaitExpr.Expression)
            },

            YieldExpression yieldExpr => yieldExpr with
            {
                Expression = yieldExpr.Expression is not null ? ResolveExpression(yieldExpr.Expression) : null
            },

            PropertyAssignmentExpression propAssign => propAssign with
            {
                Target = ResolveExpression(propAssign.Target),
                Property = propAssign.IsComputed ? ResolveExpression(propAssign.Property) : propAssign.Property,
                Value = ResolveExpression(propAssign.Value)
            },

            IndexAssignmentExpression indexAssign => indexAssign with
            {
                Target = ResolveExpression(indexAssign.Target),
                Index = ResolveExpression(indexAssign.Index),
                Value = ResolveExpression(indexAssign.Value)
            },

            DestructuringAssignmentExpression destruct => destruct with
            {
                Value = ResolveExpression(destruct.Value)
            },

            // Literals and other expressions that don't need resolution
            LiteralExpression or ThisExpression or SuperExpression
                or NewTargetExpression or ImportMetaExpression
                or RegexLiteralExpression or PrivateIdentifierExpression => expression,

            _ => expression // Default: return as-is
        };
    }

    private IdentifierExpression ResolveIdentifierExpression(IdentifierExpression identifier)
    {
        var (depth, slot, scopeId, _) = ResolveIdentifier(identifier.Name);
        return identifier with { ScopeDepth = depth, SlotIndex = slot, ScopeId = scopeId };
    }

    private AssignmentExpression ResolveAssignmentExpression(AssignmentExpression assignment)
    {
        // IMPORTANT: Resolve the Value expression FIRST.
        // This ensures that if the RHS contains an eval() call, HasEval is set
        // BEFORE we resolve the Target identifier. Per ES spec 13.15.2, the LHS
        // reference is resolved before evaluating RHS at runtime, but at static
        // analysis time we need to know about eval to correctly mark the scope
        // as dynamic (which disables slot-based optimization).
        var resolvedValue = ResolveExpression(assignment.Value);

        // Now resolve the target identifier - HasEval will be set correctly
        var (depth, slot, scopeId, isImmutable) = ResolveIdentifier(assignment.Target);
        return assignment with
        {
            Value = resolvedValue,
            ScopeDepth = depth,
            SlotIndex = slot,
            ScopeId = scopeId,
            IsImmutableTarget = isImmutable,
            TargetIdentifier = new IdentifierExpression(assignment.Source, assignment.Target, depth, slot, scopeId)
        };
    }

    private CallExpression ResolveCallExpression(CallExpression call)
    {
        // Check for eval call which makes the scope dynamic
        if (call.Callee is IdentifierExpression { Name.Name: "eval" })
        {
            _currentScope!.HasEval = true;
        }

        return call with
        {
            Callee = ResolveExpression(call.Callee),
            Arguments = ResolveCallArguments(call.Arguments)
        };
    }

    private ImmutableArray<CallArgument> ResolveCallArguments(ImmutableArray<CallArgument> arguments)
    {
        var builder = ImmutableArray.CreateBuilder<CallArgument>(arguments.Length);
        foreach (var arg in arguments)
        {
            builder.Add(arg with { Expression = ResolveExpression(arg.Expression) });
        }
        return builder.MoveToImmutable();
    }

    private ImmutableArray<ArrayElement> ResolveArrayElements(ImmutableArray<ArrayElement> elements)
    {
        var builder = ImmutableArray.CreateBuilder<ArrayElement>(elements.Length);
        foreach (var element in elements)
        {
            builder.Add(element with
            {
                Expression = element.Expression is not null ? ResolveExpression(element.Expression) : null
            });
        }
        return builder.MoveToImmutable();
    }

    private ObjectExpression ResolveObjectExpression(ObjectExpression obj)
    {
        var builder = ImmutableArray.CreateBuilder<ObjectMember>(obj.Members.Length);
        foreach (var member in obj.Members)
        {
            var resolvedMember = member with
            {
                Value = member.Value is not null ? ResolveExpression(member.Value) : null,
                Function = member.Function is not null ? AnalyzeFunction(member.Function) : null
            };
            builder.Add(resolvedMember);
        }
        return obj with { Members = builder.MoveToImmutable() };
    }

    private TaggedTemplateExpression ResolveTaggedTemplate(TaggedTemplateExpression tagged)
    {
        var resolvedTag = ResolveExpression(tagged.Tag);

        // Resolve interpolated expressions (e.g., `b` in tag`head${b}tail`)
        if (tagged.Expressions.Length == 0)
        {
            return tagged with { Tag = resolvedTag };
        }

        var builder = ImmutableArray.CreateBuilder<ExpressionNode>(tagged.Expressions.Length);
        foreach (var expr in tagged.Expressions)
        {
            builder.Add(ResolveExpression(expr));
        }
        return tagged with { Tag = resolvedTag, Expressions = builder.MoveToImmutable() };
    }

    private TemplateLiteralExpression ResolveTemplateLiteral(TemplateLiteralExpression template)
    {
        var builder = ImmutableArray.CreateBuilder<TemplatePart>(template.Parts.Length);
        foreach (var part in template.Parts)
        {
            builder.Add(part with
            {
                Expression = part.Expression is not null ? ResolveExpression(part.Expression) : null
            });
        }
        return template with { Parts = builder.MoveToImmutable() };
    }

    #endregion
}
