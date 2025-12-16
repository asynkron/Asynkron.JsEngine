using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;

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
    public sealed class ScopeInfo
    {
        public ScopeInfo? Parent { get; }
        public int Depth { get; }
        public bool IsFunctionScope { get; }
        public bool IsBlockScope { get; }
        public bool HasEval { get; set; }
        public bool HasWith { get; set; }
        public bool IsDynamic => HasEval || HasWith || (Parent?.IsDynamic ?? false);

        /// <summary>
        /// Unique ID for this scope, used to match variables to their declaring scope at runtime.
        /// </summary>
        public int ScopeId { get; }

        private readonly Dictionary<Symbol, int> _variables = new(ReferenceEqualityComparer<Symbol>.Instance);
        private int _nextSlot;

        /// <summary>
        /// The number of slots allocated in this scope.
        /// </summary>
        public int SlotCount => _nextSlot;

        public ScopeInfo(ScopeInfo? parent, bool isFunctionScope, bool isBlockScope, int scopeId)
        {
            Parent = parent;
            Depth = parent is null ? 0 : parent.Depth + 1;
            IsFunctionScope = isFunctionScope;
            IsBlockScope = isBlockScope;
            ScopeId = scopeId;
        }

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
    }

    private ScopeInfo? _currentScope;
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

        return program with { Body = resolvedStatements };
    }

    /// <summary>
    /// Analyzes a function expression and returns a new one with resolved slot indices.
    /// </summary>
    /// <param name="function">The function to analyze.</param>
    /// <param name="isDeclaration">True if this is a FunctionDeclaration (name bound in outer scope),
    /// false if it's a named FunctionExpression (name bound inside for self-reference).</param>
    public FunctionExpression AnalyzeFunction(FunctionExpression function, bool isDeclaration = false)
    {
        var parentScope = _currentScope;
        _currentScope = new ScopeInfo(parentScope, isFunctionScope: true, isBlockScope: false, scopeId: _nextScopeId++);

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

        // Declare function name for named function expressions (internal binding)
        // For FunctionDeclarations, the name is bound in the outer scope, not inside
        if (function.Name is not null && !isDeclaration)
        {
            _currentScope.DeclareVariable(function.Name);
        }

        // Collect var declarations (hoisted)
        CollectVarDeclarations(function.Body);

        // Resolve the body
        var resolvedBody = ResolveBlockStatement(function.Body);

        // Capture slot count and scope ID before leaving scope
        var slotCount = _currentScope!.IsDynamic ? -1 : _currentScope.SlotCount;
        var scopeId = _currentScope!.IsDynamic ? -1 : _currentScope.ScopeId;

        _currentScope = parentScope;

        return function with { Body = resolvedBody, SlotCount = slotCount, ScopeId = scopeId };
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

    #endregion

    #region Identifier Resolution

    private (int depth, int slot, int scopeId) ResolveIdentifier(Symbol name)
    {
        var scope = _currentScope;
        var depth = 0;

        while (scope is not null)
        {
            // If we hit a dynamic scope, we can't resolve statically
            if (scope.IsDynamic)
            {
                return (-1, -1, -1);
            }

            var slot = scope.GetSlot(name);
            if (slot >= 0)
            {
                return (depth, slot, scope.ScopeId);
            }

            // Only increment depth when crossing function boundaries
            if (scope.IsFunctionScope && scope.Parent is not null)
            {
                depth++;
            }

            scope = scope.Parent;
        }

        // Not found - could be a global or undeclared
        return (-1, -1, -1);
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

            ReturnStatement returnStmt => returnStmt with
            {
                Expression = returnStmt.Expression is not null ? ResolveExpression(returnStmt.Expression) : null
            },

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

        var resolved = block with { Statements = ResolveStatements(block.Statements) };

        _currentScope = parentScope;
        return resolved;
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

        return resolved;
    }

    private ForEachStatement ResolveForEachStatement(ForEachStatement forEachStmt)
    {
        var needsScope = forEachStmt.DeclarationKind is VariableKind.Let or VariableKind.Const;

        var parentScope = _currentScope;
        if (needsScope)
        {
            _currentScope = new ScopeInfo(parentScope, isFunctionScope: false, isBlockScope: true, scopeId: _nextScopeId++);
            CollectBindingDeclarations(forEachStmt.Target);
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

        return resolved;
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

            TaggedTemplateExpression tagged => tagged with
            {
                Tag = ResolveExpression(tagged.Tag)
            },

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
        var (depth, slot, scopeId) = ResolveIdentifier(identifier.Name);
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
        var (depth, slot, scopeId) = ResolveIdentifier(assignment.Target);
        return assignment with
        {
            Value = resolvedValue,
            ScopeDepth = depth,
            SlotIndex = slot,
            ScopeId = scopeId
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
