namespace Asynkron.JsEngine.Lisp;

/// <summary>
/// Transforms S-expressions from direct style to Continuation-Passing Style (CPS).
/// This enables support for generators (function*, yield) and async/await by making
/// control flow explicit through continuations.
///
/// The transformer operates after parsing but before evaluation, converting only
/// functions that require CPS (async functions, generators) while leaving synchronous
/// code unchanged for optimal performance.
/// </summary>
public sealed class CpsTransformer
{
    /// <summary>
    /// Determines if the given S-expression program needs CPS transformation.
    /// Returns true if the program contains async functions, generators, await expressions,
    /// or yield expressions.
    /// </summary>
    /// <param name="program">The S-expression program to analyze</param>
    /// <returns>True if CPS transformation is needed, false otherwise</returns>
    public static bool NeedsTransformation(Cons program)
    {
        if (program == null)
        {
            return false;
        }

        return ContainsAsyncOrGenerator(program);
    }

    /// <summary>
    /// Transforms an S-expression program from direct style to CPS style.
    /// Converts async functions and await expressions to work with promises.
    /// </summary>
    /// <param name="program">The S-expression program to transform</param>
    /// <returns>The transformed S-expression program in CPS style</returns>
    public Cons Transform(Cons program)
    {
        if (program == null || program.IsEmpty)
        {
            return program;
        }

        // If the program doesn't need transformation, return it unchanged
        if (!NeedsTransformation(program))
        {
            return program;
        }

        // Transform each statement in the program
        var transformedStatements = new List<object?> { JsSymbols.Program };

        var current = program.Rest; // Skip the 'program' symbol
        while (current is Cons { IsEmpty: false } cons)
        {
            transformedStatements.Add(TransformExpression(cons.Head));
            current = cons.Rest;
        }

        return Cons.FromEnumerable(transformedStatements);
    }

    /// <summary>
    /// Transforms a single expression from direct style to CPS style.
    /// </summary>
    private object? TransformExpression(object? expr)
    {
        if (expr == null)
        {
            return null;
        }

        // If not a Cons, return as-is (literals, symbols, etc.)
        if (expr is not Cons cons || cons.IsEmpty)
        {
            return expr;
        }

        // Check the head to determine what kind of expression this is
        if (cons.Head is not Symbol symbol)
        {
            return TransformCons(cons);
        }

        // Transform async function declarations
        if (symbol == JsSymbols.Async)
        {
            return TransformAsyncFunction(cons);
        }

        // Transform async function expressions
        if (symbol == JsSymbols.AsyncExpr)
        {
            return TransformAsyncFunctionExpression(cons);
        }

        // Transform blocks that might contain await
        if (symbol == JsSymbols.Block)
        {
            return TransformBlock(cons);
        }

        // Transform await expressions
        if (symbol == JsSymbols.Await)
        {
            return TransformAwait(cons);
        }

        // Transform other statement types
        if (symbol == JsSymbols.Let || symbol == JsSymbols.Var || symbol == JsSymbols.Const)
        {
            return TransformVariableDeclaration(cons);
        }

        if (symbol == JsSymbols.Return)
        {
            return TransformReturn(cons);
        }

        if (symbol == JsSymbols.If)
        {
            return TransformIf(cons);
        }

        if (symbol == JsSymbols.ExpressionStatement)
        {
            return TransformExpressionStatement(cons);
        }

        if (symbol == JsSymbols.Call)
        {
            return TransformCall(cons);
        }

        if (symbol == JsSymbols.Assign)
        {
            return TransformAssign(cons);
        }

        if (symbol == JsSymbols.Try)
        {
            return TransformTry(cons);
        }

        // For other expressions, recursively transform children
        return TransformCons(cons);
    }

    /// <summary>
    /// Transforms an async function to return a Promise.
    /// (async name (params) body) => (function name (params) (return (new Promise ...)))
    /// </summary>
    private Cons TransformAsyncFunction(Cons cons)
    {
        // cons is (async name params body)
        var parts = ConsList(cons);
        if (parts.Count < 4)
        {
            return cons;
        }

        var asyncSymbol = parts[0]; // 'async'
        var name = parts[1]; // function name or null
        var parameters = parts[2]; // parameter list
        var body = parts[3]; // function body

        // DON'T transform the body here - it will be transformed inside CreateAsyncPromiseWrapper
        // which understands the async context (return => resolve, await => .then())

        // Wrap the body in a Promise constructor
        // The async function will return a new Promise that resolves with the function's result
        var promiseBody = CreateAsyncPromiseWrapper(body);

        // For declarations, use Function symbol so they can be properly bound
        // Async declarations always have a name
        return MakeTransformedCons([
            JsSymbols.Function,
            name,
            parameters,
            promiseBody
        ], cons);
    }

    /// <summary>
    /// Transforms an async function expression to return a Promise.
    /// (async-expr name (params) body) => (lambda name (params) (return (new Promise ...)))
    /// </summary>
    private Cons TransformAsyncFunctionExpression(Cons cons)
    {
        // cons is (async-expr name params body)
        var parts = ConsList(cons);
        if (parts.Count < 4)
        {
            return cons;
        }

        var asyncExprSymbol = parts[0]; // 'async-expr'
        var name = parts[1]; // function name or null
        var parameters = parts[2]; // parameter list
        var body = parts[3]; // function body

        // DON'T transform the body here - it will be transformed inside CreateAsyncPromiseWrapper
        // which understands the async context (return => resolve, await => .then())

        // Wrap the body in a Promise constructor
        // The async function will return a new Promise that resolves with the function's result
        var promiseBody = CreateAsyncPromiseWrapper(body);

        // For expressions, always use Lambda regardless of whether there's a name
        return MakeTransformedCons([
            JsSymbols.Lambda,
            name,
            parameters,
            promiseBody
        ], cons);
    }

    /// <summary>
    /// Creates a Promise wrapper for an async function body.
    /// Wraps the body in: (block (return (new Promise (lambda (resolve reject) ...))))
    /// </summary>
    private Cons CreateAsyncPromiseWrapper(object? body)
    {
        // Create the executor function: (lambda (resolve reject) body-with-awaits)
        var resolveParam = Symbol.Intern("__resolve");
        var rejectParam = Symbol.Intern("__reject");
        var executorParams = Cons.FromEnumerable([resolveParam, rejectParam]);

        // The executor body needs to handle the transformed body
        var executorBody = CreateAsyncExecutorBody(body, resolveParam, rejectParam);

        var executor = Cons.FromEnumerable([
            JsSymbols.Lambda,
            null,
            executorParams,
            executorBody
        ]);

        // Create: (new Promise executor)
        var promiseCall = Cons.FromEnumerable([
            JsSymbols.New,
            Symbol.Intern("Promise"),
            executor
        ]);

        // Wrap in return statement
        var returnStatement = Cons.FromEnumerable([
            JsSymbols.Return,
            promiseCall
        ]);

        // Wrap in block
        return Cons.FromEnumerable([
            JsSymbols.Block,
            returnStatement
        ]);
    }

    /// <summary>
    /// Creates the body of the Promise executor for an async function.
    /// This transforms the body to handle await expressions by chaining promises.
    /// </summary>
    private Cons CreateAsyncExecutorBody(object? body, Symbol resolveParam, Symbol rejectParam)
    {
        // Transform the body to handle await expressions
        var transformedBody = TransformAsyncBody(body, resolveParam, rejectParam);

        // Wrap in a try-catch that calls resolve/reject
        var catchParam = Symbol.Intern("__error");
        var rejectCall = Cons.FromEnumerable([
            JsSymbols.Call,
            rejectParam,
            catchParam
        ]);

        var catchBlock = Cons.FromEnumerable([
            JsSymbols.Block,
            Cons.FromEnumerable([JsSymbols.ExpressionStatement, rejectCall])
        ]);

        // Create the catch clause: (catch catchParam catchBlock)
        var catchClause = Cons.FromEnumerable([
            JsSymbols.Catch,
            catchParam,
            catchBlock
        ]);

        // Create try statement with catch and no finally (null)
        var tryStatement = Cons.FromEnumerable([
            JsSymbols.Try,
            transformedBody,
            catchClause,
            null // No finally clause
        ]);

        return Cons.FromEnumerable([JsSymbols.Block, tryStatement]);
    }

    /// <summary>
    /// Transforms the body of an async function to chain await expressions.
    /// Converts: let x = await p; return x;
    /// Into: p.then(function(x) { return x; }).then(resolve);
    /// </summary>
    private object? TransformAsyncBody(object? body, Symbol resolveParam, Symbol rejectParam)
    {
        if (body is not Cons cons || cons.IsEmpty)
            // No await, just call resolve with the result
        {
            return CreateResolveCall(body, resolveParam);
        }

        // Check if this is a block
        if (cons.Head is Symbol symbol && ReferenceEquals(symbol, JsSymbols.Block))
            // Transform block statements, chaining awaits
        {
            return TransformBlockWithAwaits(cons, resolveParam, rejectParam);
        }

        // For other expressions, just resolve with the result
        return CreateResolveCall(body, resolveParam);
    }

    /// <summary>
    /// Transforms a block containing await expressions into a chain of .then() calls.
    /// </summary>
    private object? TransformBlockWithAwaits(Cons blockCons, Symbol resolveParam, Symbol rejectParam)
    {
        var statements = new List<object?>();
        var current = blockCons.Rest; // Skip 'block' symbol

        while (current is { IsEmpty: false } c)
        {
            statements.Add(c.Head);
            current = c.Rest;
        }

        if (statements.Count == 0)
            // Empty block, just resolve with null
        {
            return CreateResolveCall(null, resolveParam);
        }

        // Build the chain of statements, handling await specially
        return ChainStatementsWithAwaits(statements, 0, resolveParam, rejectParam, null, null);
    }

    /// <summary>
    /// Recursively chains statements, handling await expressions by creating promise chains.
    /// </summary>
    private object? ChainStatementsWithAwaits(List<object?> statements, int index, Symbol resolveParam,
        Symbol rejectParam, Symbol? loopContinueTarget = null, object? loopBreakTarget = null,
        bool addFinalContinuation = true)
    {
        if (index >= statements.Count)
        {
            // No more statements
            if (!addFinalContinuation)
                // Don't add a continuation - this is for if/else branches that should fall through
            {
                return Cons.FromEnumerable([JsSymbols.Block]);
            }

            // Add resolve call
            // In loop context, return the promise from resolve to chain iterations
            var inLoopContext = loopContinueTarget != null || loopBreakTarget != null;
            return CreateResolveCall(null, resolveParam, inLoopContext);
        }

        var statement = statements[index];

        // Check if this is a break statement
        if (statement is Cons { IsEmpty: false, Head: Symbol breakSymbol } && ReferenceEquals(breakSymbol, JsSymbols.Break))
            // In loop context, break should jump to after-loop continuation
        {
            if (loopBreakTarget != null)
            {
                switch (loopBreakTarget)
                {
                    // loopBreakTarget can be:
                    // 1. A Symbol (function name to call)
                    // 2. A Cons representing (call __resolve) or similar call
                    // 3. A Cons representing (block (expr-stmt (call __resolve)))
                    // 4. A Cons representing (block (return (call __resolve)))
                    case Symbol breakFunc:
                        // It's a function symbol, call it with return
                        return Cons.FromEnumerable([
                            JsSymbols.Return,
                            Cons.FromEnumerable([
                                JsSymbols.Call,
                                breakFunc
                            ])
                        ]);
                    case Cons { IsEmpty: false } breakCons2:
                    {
                        var head = breakCons2.Head;

                        switch (head)
                        {
                            // Check if it's a block
                            case Symbol blockSym when ReferenceEquals(blockSym, JsSymbols.Block):
                            {
                                switch (breakCons2.Rest)
                                {
                                    // It's a block, check the first statement
                                    case { IsEmpty: false, Head: Cons { IsEmpty: false } firstStmt }:
                                    {
                                        var firstStmtHead = firstStmt.Head;

                                        switch (firstStmtHead)
                                        {
                                            // If it's already a return statement, use the block as-is
                                            case Symbol returnSym when ReferenceEquals(returnSym, JsSymbols.Return):
                                                return breakCons2;
                                            // If it's an expression statement, convert to return
                                            // Get the expression from (expr-stmt expression)
                                            case Symbol exprSym when
                                                ReferenceEquals(exprSym, JsSymbols.ExpressionStatement) && firstStmt.Rest is { IsEmpty: false } && firstStmt.Rest is { IsEmpty: false } exprRest:
                                            {
                                                var expression = exprRest.Head;
                                                // Return the expression directly
                                                return Cons.FromEnumerable([
                                                    JsSymbols.Return,
                                                    expression
                                                ]);
                                            }
                                        }

                                        break;
                                    }
                                }

                                // Block with other content, wrap in return (though this may not work correctly)
                                return Cons.FromEnumerable([
                                    JsSymbols.Return,
                                    breakCons2
                                ]);
                            }
                            // Check if it's a call expression (call __resolve)
                            case Symbol callSym when ReferenceEquals(callSym, JsSymbols.Call):
                                // It's a call expression, wrap in return
                                return Cons.FromEnumerable([
                                    JsSymbols.Return,
                                    breakCons2
                                ]);
                            default:
                                // Some other expression, wrap in return
                                return Cons.FromEnumerable([
                                    JsSymbols.Return,
                                    breakCons2
                                ]);
                        }
                    }
                    default:
                        // Unknown type, return as-is
                        return loopBreakTarget;
                }
            }
        }

        // Outside loop context, just include it (will be handled at runtime)
        // Check if this is a continue statement
        if (statement is Cons { IsEmpty: false, Head: Symbol continueSymbol } && ReferenceEquals(continueSymbol, JsSymbols.Continue))
            // In loop context, continue should call the loop check function (next iteration)
        {
            if (loopContinueTarget != null)
            {
                return Cons.FromEnumerable([
                    JsSymbols.Block,
                    Cons.FromEnumerable([
                        JsSymbols.Return,
                        Cons.FromEnumerable([
                            JsSymbols.Call,
                            loopContinueTarget
                        ])
                    ])
                ]);
            }
        }

        switch (statement)
        {
            // Outside loop context, just include it (will be handled at runtime)
            // Check if this is a return statement
            // Always transform return statements to call resolve
            case Cons { IsEmpty: false, Head: Symbol symbol } cons when ReferenceEquals(symbol, JsSymbols.Return):
                return TransformReturnStatement(cons, resolveParam, statements, index);
            // Check if this is a try-catch statement
            // Transform try-catch with special handling for async context
            case Cons { IsEmpty: false, Head: Symbol trySymbol } tryCons when ReferenceEquals(trySymbol, JsSymbols.Try):
                return TransformTryInAsyncContext(tryCons, statements, index, resolveParam, rejectParam, loopContinueTarget,
                    loopBreakTarget, addFinalContinuation);
            // Check if this is a for-of statement - needs transformation if body contains await
            case Cons { IsEmpty: false, Head: Symbol forOfSymbol } forOfCons when ReferenceEquals(forOfSymbol, JsSymbols.ForOf):
            {
                // Check if body contains await
                var parts = ConsList(forOfCons);
                if (parts.Count >= 4 && ContainsAwait(parts[3]))
                    // Transform for-of with await in body
                {
                    return TransformForOfWithAwaitInBody(forOfCons, statements, index, resolveParam, rejectParam);
                }

                break;
            }
        }

        switch (statement)
        {
            // Check if this is a for-await-of statement - always transform in async context
            // We wrap iterator.next() in Promise.resolve() to handle both sync and async iterators
            // Always transform for-await-of in async functions
            // Promise.resolve() ensures both sync and async iterators work the same way
            case Cons { IsEmpty: false, Head: Symbol forAwaitSymbol } forAwaitCons when ReferenceEquals(forAwaitSymbol, JsSymbols.ForAwaitOf):
                return TransformForOfWithAwaitInBody(forAwaitCons, statements, index, resolveParam, rejectParam);
            // Check if this is a while statement - needs transformation if body contains await
            case Cons { IsEmpty: false, Head: Symbol whileSymbol } whileCons when ReferenceEquals(whileSymbol, JsSymbols.While):
            {
                // Check if body contains await
                var parts = ConsList(whileCons);
                if (parts.Count >= 3 && ContainsAwait(parts[2]))
                    // Transform while with await in body
                {
                    return TransformWhileWithAwaitInBody(whileCons, statements, index, resolveParam, rejectParam);
                }

                break;
            }
        }

        // Check if this is an if statement - recursively transform branches when in loop context or contains await
        if (statement is Cons { IsEmpty: false, Head: Symbol ifSymbol } ifCons && ReferenceEquals(ifSymbol, JsSymbols.If))
        {
            var ifParts = ConsList(ifCons);
            if (ifParts.Count >= 3)
            {
                var condition = ifParts[1];
                var thenBranch = ifParts[2];
                var elseBranch = ifParts.Count > 3 ? ifParts[3] : null;

                // Check if we need to transform branches (in loop context or contains await)
                var needsTransform = loopContinueTarget != null || loopBreakTarget != null ||
                                     ContainsAwait(thenBranch) ||
                                     (elseBranch != null && ContainsAwait(elseBranch));

                if (needsTransform)
                {
                    // Transform branches recursively - handles break/continue/await/return
                    // Use TransformIfBranchInLoopContext when in loop context to avoid adding continuation at end
                    var inLoopContext = loopContinueTarget != null || loopBreakTarget != null;

                    object? transformedThen, transformedElse = null;
                    if (inLoopContext)
                    {
                        transformedThen = TransformIfBranchInLoopContext(thenBranch, resolveParam, rejectParam,
                            loopContinueTarget, loopBreakTarget);
                        transformedElse = elseBranch != null
                            ? TransformIfBranchInLoopContext(elseBranch, resolveParam, rejectParam, loopContinueTarget,
                                loopBreakTarget)
                            : null;
                    }
                    else
                    {
                        transformedThen = TransformBlockInAsyncContext(thenBranch, resolveParam, rejectParam,
                            loopContinueTarget, loopBreakTarget);
                        transformedElse = elseBranch != null
                            ? TransformBlockInAsyncContext(elseBranch, resolveParam, rejectParam, loopContinueTarget,
                                loopBreakTarget)
                            : null;
                    }

                    // Check if both branches (or the single then-branch with no else) return early
                    // If so, we shouldn't add the continuation after the if statement
                    var allBranchesReturnEarly = false;
                    var thenReturnsEarly = BlockAlwaysReturnsEarly(transformedThen);

                    if (transformedElse != null)
                        // Both branches exist - check if both return early
                    {
                        allBranchesReturnEarly = thenReturnsEarly &&
                                                 BlockAlwaysReturnsEarly(transformedElse);
                    }
                    // If there's no else branch, it can't guarantee early return since execution falls through when condition is false

                    // Special case: In loop context, if the then branch returns early but there's no else,
                    // we need to add the continuation as an else branch instead of after the if statement.
                    // This prevents the continuation from being executed unconditionally.
                    if (inLoopContext && thenReturnsEarly && transformedElse == null)
                    {
                        // Get the continuation for the rest of the statements
                        var elseContinuation = ChainStatementsWithAwaits(statements, index + 1, resolveParam, rejectParam,
                            loopContinueTarget, loopBreakTarget, addFinalContinuation);

                        // Add the continuation as the else branch
                        var ifWithElse = Cons.FromEnumerable([JsSymbols.If, condition, transformedThen, elseContinuation]);

                        // Both branches now return, so no continuation needed after the if
                        return Cons.FromEnumerable([JsSymbols.Block, ifWithElse]);
                    }

                    var transformedIf = transformedElse != null
                        ? Cons.FromEnumerable([JsSymbols.If, condition, transformedThen, transformedElse])
                        : Cons.FromEnumerable([JsSymbols.If, condition, transformedThen, null]);

                    // Only add continuation if not all branches return early
                    if (allBranchesReturnEarly)
                    {
                        return Cons.FromEnumerable([JsSymbols.Block, transformedIf]);
                    }

                    var ifRest = ChainStatementsWithAwaits(statements, index + 1, resolveParam, rejectParam,
                        loopContinueTarget, loopBreakTarget, addFinalContinuation);

                    if (ifRest is not Cons { IsEmpty: false, Head: Symbol ifRestSymbol } ifRestCons ||
                        !ReferenceEquals(ifRestSymbol, JsSymbols.Block))
                    {
                        return Cons.FromEnumerable([JsSymbols.Block, transformedIf, ifRest]);
                    }

                    var flattenedStatements = new List<object?> { JsSymbols.Block, transformedIf };
                    var current = ifRestCons.Rest;
                    while (current is { IsEmpty: false } c)
                    {
                        flattenedStatements.Add(c.Head);
                        current = c.Rest;
                    }

                    return Cons.FromEnumerable(flattenedStatements);

                    // All branches return early, just return the if statement without continuation
                }
            }
        }

        // Check if this statement contains await
        if (ContainsAwait(statement))
            // Transform this statement with await into a promise chain
        {
            return TransformStatementWithAwait(statement, statements, index, resolveParam, rejectParam,
                loopContinueTarget, loopBreakTarget, addFinalContinuation);
        }

        // No await and not a special case, just include it and continue
        var rest = ChainStatementsWithAwaits(statements, index + 1, resolveParam, rejectParam, loopContinueTarget,
            loopBreakTarget, addFinalContinuation);

        // If rest is a block, flatten it to avoid nested blocks
        if (rest is Cons { IsEmpty: false, Head: Symbol restSymbol } restCons && ReferenceEquals(restSymbol, JsSymbols.Block))
        {
            // rest is (block stmts...), so we want to create (block statement stmts...)
            var flattenedStatements = new List<object?> { JsSymbols.Block, statement };
            var current = restCons.Rest;
            while (current is Cons { IsEmpty: false } c)
            {
                flattenedStatements.Add(c.Head);
                current = c.Rest;
            }

            return Cons.FromEnumerable(flattenedStatements);
        }

        return Cons.FromEnumerable([
            JsSymbols.Block,
            statement,
            rest
        ]);
    }

    /// <summary>
    /// Transforms a return statement to call resolve with the return value.
    /// </summary>
    private static Cons TransformReturnStatement(Cons returnCons, Symbol resolveParam, List<object?> statements, int index)
    {
        var parts = ConsList(returnCons);
        var returnValue = parts.Count >= 2 ? parts[1] : null;

        // Check if return value is an await expression
        if (returnValue is not Cons { IsEmpty: false, Head: Symbol awaitSym } valueCons ||
            !ReferenceEquals(awaitSym, JsSymbols.Await))
        {
            return CreateResolveCall(returnValue, resolveParam);
        }

        // Extract the promise expression
        var promiseExpr = valueCons.Rest.Head;

        // Create a temporary parameter name for the .then() callback
        var valueParam = Symbol.Intern("__value");

        // Create the .then() callback: function(__value) { resolve(__value); }
        var resolveCall = Cons.FromEnumerable([
            JsSymbols.Call,
            resolveParam,
            valueParam
        ]);

        var thenCallback = Cons.FromEnumerable([
            JsSymbols.Lambda,
            null,
            Cons.FromEnumerable([valueParam]),
            Cons.FromEnumerable([
                JsSymbols.Block,
                Cons.FromEnumerable([JsSymbols.ExpressionStatement, resolveCall])
            ])
        ]);

        // Use __awaitHelper to wrap promiseExpr in Promise if needed
        var awaitHelperCall = Cons.FromEnumerable([
            JsSymbols.Call,
            Symbol.Intern("__awaitHelper"),
            promiseExpr
        ]);

        // Create: __awaitHelper(promiseExpr).then(callback)
        var thenCall = Cons.FromEnumerable([
            JsSymbols.Call,
            Cons.FromEnumerable([
                JsSymbols.GetProperty,
                awaitHelperCall,
                "then"
            ]),
            thenCallback
        ]);

        return Cons.FromEnumerable([
            JsSymbols.Block,
            Cons.FromEnumerable([JsSymbols.ExpressionStatement, thenCall])
        ]);

        // Regular return, call resolve with the value
    }

    /// <summary>
    /// Checks if an expression contains an await.
    /// </summary>
    private static bool ContainsAwait(object? expr)
    {
        if (expr is not Cons { IsEmpty: false } cons)
        {
            return false;
        }

        if (cons.Head is not Symbol symbol)
        {
            return ContainsAwait(cons.Head) || ContainsAwait(cons.Rest);
        }

        // Check for await expression
        if (ReferenceEquals(symbol, JsSymbols.Await))
        {
            return true;
        }

        // Check for for-await-of loop
        if (ReferenceEquals(symbol, JsSymbols.ForAwaitOf))
        {
            return true;
        }

        // Check for while loop with await in body
        if (!ReferenceEquals(symbol, JsSymbols.While))
        {
            return ContainsAwait(cons.Head) || ContainsAwait(cons.Rest);
        }

        var parts = ConsList(cons);
        if (parts.Count >= 3 && ContainsAwait(parts[2]))
        {
            return true;
        }

        return ContainsAwait(cons.Head) || ContainsAwait(cons.Rest);
    }

    /// <summary>
    /// Checks if a transformed block always returns early (doesn't fall through).
    /// A block returns early if it ends with a return statement or if all branches
    /// of a final if statement return early.
    /// </summary>
    private static bool BlockAlwaysReturnsEarly(object? block)
    {
        if (block == null)
        {
            return false;
        }

        // If it's a return statement, it returns early
        if (block is Cons { IsEmpty: false, Head: Symbol returnSym } && ReferenceEquals(returnSym, JsSymbols.Return))
        {
            return true;
        }

        // If it's a block, check the last statement
        if (block is not Cons { IsEmpty: false, Head: Symbol blockSym } blockCons ||
            !ReferenceEquals(blockSym, JsSymbols.Block))
        {
            return false;
        }

        // Find the last statement in the block
        object? lastStmt = null;
        var current = blockCons.Rest;
        while (current is Cons { IsEmpty: false } c)
        {
            lastStmt = c.Head;
            current = c.Rest;
        }

        if (lastStmt == null)
        {
            return false;
        }

        // Check if last statement is a return
        if (lastStmt is Cons { IsEmpty: false, Head: Symbol lastSym } && ReferenceEquals(lastSym, JsSymbols.Return))
        {
            return true;
        }

        // Check if last statement is an if where all branches return early
        if (lastStmt is not Cons { IsEmpty: false, Head: Symbol ifSym } ifCons || !ReferenceEquals(ifSym, JsSymbols.If))
        {
            return false;
        }

        var ifParts = ConsList(ifCons);
        if (ifParts.Count < 3)
        {
            return false;
        }

        var thenBranch = ifParts[2];
        var elseBranch = ifParts.Count > 3 ? ifParts[3] : null;

        // If there's an else branch, both must return early
        if (elseBranch != null)
        {
            return BlockAlwaysReturnsEarly(thenBranch) && BlockAlwaysReturnsEarly(elseBranch);
        }

        // If there's no else branch, the if doesn't guarantee early return
        // because execution can fall through when condition is false
        return false;

    }

    /// <summary>
    /// Transforms a statement containing await into a promise chain.
    /// Example: let x = await p; [rest]
    /// Becomes: p.then(function(x) { [rest] })
    /// </summary>
    private object? TransformStatementWithAwait(object? statement, List<object?> statements, int index,
        Symbol resolveParam, Symbol rejectParam, Symbol? loopContinueTarget = null, object? loopBreakTarget = null,
        bool addFinalContinuation = true)
    {
        // Handle different statement types
        if (statement is Cons { IsEmpty: false, Head: Symbol symbol } cons)
        {
            // Handle: let/var/const x = await expr;
            if (ReferenceEquals(symbol, JsSymbols.Let) ||
                ReferenceEquals(symbol, JsSymbols.Var) ||
                ReferenceEquals(symbol, JsSymbols.Const))
            {
                var parts = ConsList(cons);
                if (parts.Count >= 3)
                {
                    var varKeyword = parts[0]; // let/var/const
                    var varName = parts[1]; // variable name
                    var value = parts[2]; // value expression

                    // Check if value is a simple await expression
                    if (value is Cons { IsEmpty: false, Head: Symbol awaitSym } valueCons && ReferenceEquals(awaitSym, JsSymbols.Await))
                    {
                        // Extract the promise expression from (await promise-expr)
                        var promiseExpr = valueCons.Rest.Head;

                        // Create continuation for remaining statements
                        var restStatements = statements.Skip(index + 1).ToList();
                        var continuation = ChainStatementsWithAwaits(restStatements, 0, resolveParam, rejectParam,
                            loopContinueTarget, loopBreakTarget);

                        // Create the .then() callback: function(varName) { [continuation] }
                        var thenCallback = Cons.FromEnumerable([
                            JsSymbols.Lambda,
                            null,
                            Cons.FromEnumerable([varName]),
                            continuation
                        ]);

                        // Use __awaitHelper to wrap promiseExpr in Promise if needed
                        // This checks if the value is already thenable before wrapping
                        var awaitHelperCall = Cons.FromEnumerable([
                            JsSymbols.Call,
                            Symbol.Intern("__awaitHelper"),
                            promiseExpr
                        ]);

                        // Create the .then() call: __awaitHelper(promiseExpr).then(thenCallback)
                        var thenCall = Cons.FromEnumerable([
                            JsSymbols.Call,
                            Cons.FromEnumerable([
                                JsSymbols.GetProperty,
                                awaitHelperCall,
                                "then"
                            ]),
                            thenCallback
                        ]);

                        // Wrap in expression statement and block
                        return Cons.FromEnumerable([
                            JsSymbols.Block,
                            Cons.FromEnumerable([JsSymbols.ExpressionStatement, thenCall])
                        ]);
                    }
                    // Check if value is a complex expression containing await

                    if (ContainsAwait(value))
                    {
                        // Extract awaits from the complex expression and chain them
                        return ExtractAndChainAwaits(varKeyword, varName, value, statements, index, resolveParam,
                            rejectParam, loopContinueTarget, loopBreakTarget, addFinalContinuation);
                    }
                }
            }

            // Handle: return await expr;
            if (ReferenceEquals(symbol, JsSymbols.Return))
            {
                var parts = ConsList(cons);
                if (parts is [_, Cons { IsEmpty: false } retValueCons, ..])
                {
                    if (retValueCons.Head is Symbol awaitSym && ReferenceEquals(awaitSym, JsSymbols.Await))
                    {
                        // Extract the promise expression
                        var promiseExpr = retValueCons.Rest.Head;

                        // Use __awaitHelper to wrap promiseExpr in Promise if needed
                        var awaitHelperCall = Cons.FromEnumerable([
                            JsSymbols.Call,
                            Symbol.Intern("__awaitHelper"),
                            promiseExpr
                        ]);

                        // Create: __awaitHelper(promiseExpr).then(resolve)
                        var thenCall = Cons.FromEnumerable([
                            JsSymbols.Call,
                            Cons.FromEnumerable([
                                JsSymbols.GetProperty,
                                awaitHelperCall,
                                "then"
                            ]),
                            resolveParam
                        ]);

                        return Cons.FromEnumerable([
                            JsSymbols.Block,
                            Cons.FromEnumerable([JsSymbols.ExpressionStatement, thenCall])
                        ]);
                    }
                }

                // Regular return without await, just call resolve
                var returnValue = parts.Count >= 2 ? parts[1] : null;
                return CreateResolveCall(returnValue, resolveParam);
            }

            // Handle: (expr-stmt (assign x (await expr)))
            // This handles plain assignments like: x = await expr;
            if (ReferenceEquals(symbol, JsSymbols.ExpressionStatement))
            {
                var parts = ConsList(cons);
                if (parts is [_, Cons { IsEmpty: false, Head: Symbol exprSymbol } exprCons, ..] && ReferenceEquals(exprSymbol, JsSymbols.Assign))
                {
                    // It's an assignment expression, check if it contains await
                    var assignParts = ConsList(exprCons);
                    if (assignParts.Count >= 3)
                    {
                        var varName = assignParts[1]; // variable name
                        var value = assignParts[2]; // value expression

                        // Check if value is a simple await expression
                        if (value is Cons { IsEmpty: false, Head: Symbol awaitSym } valueCons && ReferenceEquals(awaitSym, JsSymbols.Await))
                        {
                            // Extract the promise expression from (await promise-expr)
                            var promiseExpr = valueCons.Rest.Head;

                            // Create continuation for remaining statements
                            var restStatements = statements.Skip(index + 1).ToList();
                            var continuation = ChainStatementsWithAwaits(restStatements, 0, resolveParam, rejectParam,
                                loopContinueTarget, loopBreakTarget);

                            // Create the .then() callback: function(tempVar) { varName = tempVar; [continuation] }
                            // We need a temporary variable for the .then() callback parameter
                            var tempVar = Symbol.Intern("__awaitResult");

                            // Create the assignment statement in the callback: varName = tempVar
                            var assignStatement = Cons.FromEnumerable([
                                JsSymbols.ExpressionStatement,
                                Cons.FromEnumerable([
                                    JsSymbols.Assign,
                                    varName,
                                    tempVar
                                ])
                            ]);

                            // Combine assignment with continuation
                            object? callbackBody;
                            if (continuation is Cons { IsEmpty: false, Head: Symbol blockSym } contCons && ReferenceEquals(blockSym, JsSymbols.Block))
                            {
                                // Continuation is a block, flatten it
                                var bodyItems = new List<object?> { JsSymbols.Block, assignStatement };
                                bodyItems.AddRange(ConsList(contCons).Skip(1));
                                callbackBody = Cons.FromEnumerable(bodyItems);
                            }
                            else
                            {
                                // Continuation is not a block, wrap together
                                callbackBody = Cons.FromEnumerable([
                                    JsSymbols.Block,
                                    assignStatement,
                                    continuation
                                ]);
                            }

                            var thenCallback = Cons.FromEnumerable([
                                JsSymbols.Lambda,
                                null,
                                Cons.FromEnumerable([tempVar]),
                                callbackBody
                            ]);

                            // Use __awaitHelper to wrap promiseExpr in Promise if needed
                            var awaitHelperCall = Cons.FromEnumerable([
                                JsSymbols.Call,
                                Symbol.Intern("__awaitHelper"),
                                promiseExpr
                            ]);

                            // Create the .then() call: __awaitHelper(promiseExpr).then(thenCallback)
                            var thenCall = Cons.FromEnumerable([
                                JsSymbols.Call,
                                Cons.FromEnumerable([
                                    JsSymbols.GetProperty,
                                    awaitHelperCall,
                                    "then"
                                ]),
                                thenCallback
                            ]);

                            // Wrap in expression statement and block
                            return Cons.FromEnumerable([
                                JsSymbols.Block,
                                Cons.FromEnumerable([JsSymbols.ExpressionStatement, thenCall])
                            ]);
                        }
                    }
                }
            }
        }

        // Default: include the statement as-is and continue
        var rest = ChainStatementsWithAwaits(statements, index + 1, resolveParam, rejectParam, loopContinueTarget,
            loopBreakTarget, addFinalContinuation);
        return Cons.FromEnumerable([
            JsSymbols.Block,
            statement,
            rest
        ]);
    }

    /// <summary>
    /// Transforms a try-catch statement within an async function context.
    /// Handles return statements in try and catch blocks by calling resolve.
    /// </summary>
    private object? TransformTryInAsyncContext(Cons tryCons, List<object?> statements, int index, Symbol resolveParam,
        Symbol rejectParam, Symbol? loopContinueTarget = null, object? loopBreakTarget = null,
        bool addFinalContinuation = true)
    {
        // tryCons is (try tryBlock catchClause? finallyBlock?)
        var parts = ConsList(tryCons);
        if (parts.Count < 2)
        {
            return tryCons;
        }

        var tryBlock = parts[1];
        var catchClause = parts.Count > 2 ? parts[2] : null;
        var finallyBlock = parts.Count > 3 ? parts[3] : null;

        // Check if try or catch blocks contain await
        // If catch contains await, we need full promise-based transformation
        // to ensure code after try-catch waits for async operations in catch
        // Note: Finally blocks with await are not fully supported in promise-based transformation
        var catchContainsAwait = false;

        if (catchClause is Cons { IsEmpty: false } catchCons)
        {
            var catchParts = ConsList(catchCons);
            if (catchParts.Count >= 3)
            {
                var catchBlock = catchParts[2];
                catchContainsAwait = ContainsAwait(catchBlock);
            }
        }

        // Only keep synchronous try-catch structure if try and catch are both synchronous
        // Finally blocks are transformed in place regardless
        if (!ContainsAwait(tryBlock) && !catchContainsAwait)
        {
            // No await in try or catch blocks - transform blocks but keep try-catch structure
            var transformedTryBlock = TransformBlockInAsyncContext(tryBlock, resolveParam, rejectParam,
                loopContinueTarget, loopBreakTarget);

            object? transformedCatchClause = null;
            if (catchClause is Cons { IsEmpty: false } syncCatchCons)
            {
                var catchParts = ConsList(syncCatchCons);
                if (catchParts.Count >= 3)
                {
                    var catchSymbol = catchParts[0];
                    var catchParam = catchParts[1];
                    var catchBlock = catchParts[2];
                    var transformedCatchBlock = TransformBlockInAsyncContext(catchBlock, resolveParam, rejectParam,
                        loopContinueTarget, loopBreakTarget);

                    transformedCatchClause = Cons.FromEnumerable([
                        catchSymbol,
                        catchParam,
                        transformedCatchBlock
                    ]);
                }
            }

            object? transformedFinallyBlock = null;
            if (finallyBlock != null)
            {
                transformedFinallyBlock = TransformBlockInAsyncContext(finallyBlock, resolveParam, rejectParam,
                    loopContinueTarget, loopBreakTarget);
            }

            // Keep try-catch structure
            var transformedTry = Cons.FromEnumerable([
                JsSymbols.Try,
                transformedTryBlock,
                transformedCatchClause,
                transformedFinallyBlock
            ]);

            // Continue with remaining statements
            var rest = ChainStatementsWithAwaits(statements, index + 1, resolveParam, rejectParam, loopContinueTarget,
                loopBreakTarget, addFinalContinuation);

            return Cons.FromEnumerable([
                JsSymbols.Block,
                transformedTry,
                rest
            ]);
        }

        // Try block contains await - need to transform into promise-based error handling
        // Continue with remaining statements after the try-catch
        var restAfterTry = ChainStatementsWithAwaits(statements, index + 1, resolveParam, rejectParam,
            loopContinueTarget, loopBreakTarget, addFinalContinuation);

        // Check if we have a catch clause
        Symbol? asyncCatchParam = null;
        object? asyncCatchBlock = null;
        if (catchClause is Cons { IsEmpty: false } asyncCatchCons)
        {
            var asyncCatchParts = ConsList(asyncCatchCons);
            if (asyncCatchParts.Count >= 3)
            {
                asyncCatchParam = asyncCatchParts[1] as Symbol;
                asyncCatchBlock = asyncCatchParts[2];
            }
        }

        // Create a catch handler function that will be used as the reject parameter for the try block
        // This function will execute the catch block and then continue with rest
        Symbol tryCatchRejectHandler;
        object? tryCatchRejectHandlerDecl = null;

        if (asyncCatchBlock != null && asyncCatchParam != null)
        {
            tryCatchRejectHandler = Symbol.Intern(string.Concat("__tryCatchReject", Guid.NewGuid().ToString("N").AsSpan(0, 8)));

            // Transform the catch block
            // The catch block's continuation is the rest of the statements
            var transformedCatchBlock = TransformBlockInAsyncContext(asyncCatchBlock, resolveParam, rejectParam,
                loopContinueTarget, loopBreakTarget);

            // Combine catch block with rest
            var catchWithRest = Cons.FromEnumerable([
                JsSymbols.Block,
                transformedCatchBlock,
                restAfterTry
            ]);

            // Create function: function __tryCatchReject(asyncCatchParam) { catchBlock; rest; }
            tryCatchRejectHandlerDecl = Cons.FromEnumerable([
                JsSymbols.Function,
                tryCatchRejectHandler,
                Cons.FromEnumerable([asyncCatchParam]),
                catchWithRest
            ]);
        }
        else
        {
            // No catch clause, use the outer reject handler
            tryCatchRejectHandler = rejectParam;
        }

        // Transform the try block, using the catch handler as the reject parameter
        var transformedTryBlock2 = TransformBlockInAsyncContext(tryBlock, resolveParam, tryCatchRejectHandler,
            loopContinueTarget, loopBreakTarget);

        // If the try block doesn't contain await but we're using promise-based transformation
        // (because catch has await), wrap in try-catch to convert sync throws to reject calls
        object? wrappedTryBlock;
        if (!ContainsAwait(tryBlock))
        {
            // Synchronous try block - wrap in try-catch to catch sync errors
            var errorParam = Symbol.Intern(string.Concat("__syncError", Guid.NewGuid().ToString("N").AsSpan(0, 8)));

            // Create catch block: { tryCatchRejectHandler(errorParam); }
            var syncCatchBlock = Cons.FromEnumerable([
                JsSymbols.Block,
                Cons.FromEnumerable([
                    JsSymbols.ExpressionStatement,
                    Cons.FromEnumerable([
                        JsSymbols.Call,
                        tryCatchRejectHandler,
                        errorParam
                    ])
                ])
            ]);

            // Create catch clause: (catch errorParam syncCatchBlock)
            var syncCatchClause = Cons.FromEnumerable([
                JsSymbols.Catch,
                errorParam,
                syncCatchBlock
            ]);

            // Wrap: try { transformedTryBlock2 } catch (errorParam) { tryCatchRejectHandler(errorParam); }
            wrappedTryBlock = Cons.FromEnumerable([
                JsSymbols.Try,
                transformedTryBlock2,
                syncCatchClause,
                null // no finally
            ]);
        }
        else
        {
            // Async try block - errors are already promise rejections
            wrappedTryBlock = transformedTryBlock2;
        }

        // If try block completes successfully, execute rest
        // Note: If an error occurs in sync try and catch handles it, rest will execute in the catch handler
        var tryWithRest = Cons.FromEnumerable([
            JsSymbols.Block,
            wrappedTryBlock,
            restAfterTry
        ]);

        // If we have a catch handler function, define it first
        if (tryCatchRejectHandlerDecl != null)
        {
            return Cons.FromEnumerable([
                JsSymbols.Block,
                tryCatchRejectHandlerDecl,
                tryWithRest
            ]);
        }

        return tryWithRest;
    }

    /// <summary>
    /// Transforms a block within an async function context, handling return statements.
    /// </summary>
    private object? TransformBlockInAsyncContext(object? block, Symbol resolveParam, Symbol rejectParam,
        Symbol? loopContinueTarget = null, object? loopBreakTarget = null)
    {
        if (block is not Cons blockCons || blockCons.IsEmpty)
        {
            return block;
        }

        // Check if this is a block
        if (blockCons.Head is not Symbol blockSymbol || !ReferenceEquals(blockSymbol, JsSymbols.Block))
        {
            return block;
        }

        var statements = new List<object?>();
        var current = blockCons.Rest;

        while (current is { IsEmpty: false } c)
        {
            statements.Add(c.Head);
            current = c.Rest;
        }

        // Chain statements with async context
        return ChainStatementsWithAwaits(statements, 0, resolveParam, rejectParam, loopContinueTarget,
            loopBreakTarget);

    }

    /// <summary>
    /// Transforms an if branch (then or else) within a loop context.
    /// Unlike TransformBlockInAsyncContext, this does NOT add a continuation at the end
    /// because if branches should fall through to the next statement after the if.
    /// </summary>
    private object? TransformIfBranchInLoopContext(object? branch, Symbol resolveParam, Symbol rejectParam,
        Symbol? loopContinueTarget, object? loopBreakTarget)
    {
        if (branch is not Cons branchCons || branchCons.IsEmpty)
        {
            return branch;
        }

        // Check if this is a block
        if (branchCons.Head is not Symbol blockSymbol || !ReferenceEquals(blockSymbol, JsSymbols.Block))
        {
            return branch;
        }

        var statements = new List<object?>();
        var current = branchCons.Rest;

        while (current is Cons { IsEmpty: false } c)
        {
            statements.Add(c.Head);
            current = c.Rest;
        }

        // Chain statements WITHOUT adding final continuation (addFinalContinuation = false)
        // This allows the if branch to fall through to subsequent statements
        return ChainStatementsWithAwaits(statements, 0, resolveParam, rejectParam, loopContinueTarget,
            loopBreakTarget, false);

    }

    /// <summary>
    /// Extracts await expressions from a complex expression and chains them.
    /// Example: let x = (await p1) + (await p2);
    /// Becomes: p1.then(function(t1) { p2.then(function(t2) { let x = t1 + t2; [rest] }) })
    /// </summary>
    private object? ExtractAndChainAwaits(object? varKeyword, object? varName, object? expr, List<object?> statements,
        int index, Symbol resolveParam, Symbol rejectParam, Symbol? loopContinueTarget = null,
        object? loopBreakTarget = null, bool addFinalContinuation = true)
    {
        // Collect all await expressions in the expression
        var awaits = new List<(object? promiseExpr, Symbol tempVar)>();
        var transformedExpr = ExtractAwaitsFromExpression(expr, awaits);

        if (awaits.Count == 0)
        {
            // No awaits found, shouldn't happen but handle gracefully
            var rest = ChainStatementsWithAwaits(statements, index + 1, resolveParam, rejectParam, loopContinueTarget,
                loopBreakTarget, addFinalContinuation);
            return Cons.FromEnumerable([
                JsSymbols.Block,
                Cons.FromEnumerable([varKeyword, varName, expr]),
                rest
            ]);
        }

        // Build the chain from innermost to outermost
        // Start with the variable declaration and remaining statements
        var restStatements = statements.Skip(index + 1).ToList();
        var varDecl = Cons.FromEnumerable([varKeyword, varName, transformedExpr]);
        var innerStatements = new List<object?> { varDecl };
        innerStatements.AddRange(restStatements);
        var innerBody = ChainStatementsWithAwaits(innerStatements, 0, resolveParam, rejectParam, loopContinueTarget,
            loopBreakTarget);

        // Chain the awaits from right to left (innermost first)
        for (var i = awaits.Count - 1; i >= 0; i--)
        {
            var (promiseExpr, tempVar) = awaits[i];

            // Create the .then() callback: function(tempVar) { innerBody }
            var thenCallback = Cons.FromEnumerable([
                JsSymbols.Lambda,
                null,
                Cons.FromEnumerable([tempVar]),
                innerBody
            ]);

            // Create the .then() call: promiseExpr.then(thenCallback)
            var thenCall = Cons.FromEnumerable([
                JsSymbols.Call,
                Cons.FromEnumerable([
                    JsSymbols.GetProperty,
                    promiseExpr,
                    "then"
                ]),
                thenCallback
            ]);

            // Wrap in block for next iteration
            innerBody = Cons.FromEnumerable([
                JsSymbols.Block,
                Cons.FromEnumerable([JsSymbols.ExpressionStatement, thenCall])
            ]);
        }

        return innerBody;
    }

    /// <summary>
    /// Extracts await expressions from a complex expression, replacing them with temporary variables.
    /// Returns the transformed expression and populates the awaits list.
    /// </summary>
    private static object? ExtractAwaitsFromExpression(object? expr, List<(object? promiseExpr, Symbol tempVar)> awaits)
    {
        if (expr == null)
        {
            return null;
        }

        // If not a Cons, return as-is
        if (expr is not Cons cons || cons.IsEmpty)
        {
            return expr;
        }

        // Check if this is an await expression
        if (cons.Head is Symbol symbol && ReferenceEquals(symbol, JsSymbols.Await))
        {
            // Extract the promise expression
            var promiseExpr = cons.Rest.Head;

            // Create a temporary variable
            var tempVar = Symbol.Intern($"__await{awaits.Count}");

            // Add to awaits list
            awaits.Add((promiseExpr, tempVar));

            // Return the temporary variable
            return tempVar;
        }

        // Recursively transform all children
        var items = new List<object?>();
        var current = cons;

        while (current is { IsEmpty: false } c)
        {
            items.Add(ExtractAwaitsFromExpression(c.Head, awaits));
            current = c.Rest;
        }

        return Cons.FromEnumerable(items);
    }

    /// <summary>
    /// Creates a call to resolve with the given value.
    /// </summary>
    private static Cons CreateResolveCall(object? value, Symbol resolveParam, bool shouldReturn = false)
    {
        // Create: (call resolve value)
        var args = new List<object?> { JsSymbols.Call, resolveParam };
        if (value != null)
        {
            args.Add(value);
        }

        var resolveCall = Cons.FromEnumerable(args);

        // In loop context, we need to return the promise from resolve call to chain iterations
        if (shouldReturn)
        {
            return Cons.FromEnumerable([
                JsSymbols.Block,
                Cons.FromEnumerable([JsSymbols.Return, resolveCall])
            ]);
        }

        return Cons.FromEnumerable([
            JsSymbols.Block,
            Cons.FromEnumerable([JsSymbols.ExpressionStatement, resolveCall])
        ]);
    }

    /// <summary>
    /// Transforms a block by recursively transforming all statements within it.
    /// </summary>
    private Cons TransformBlock(Cons cons)
    {
        var statements = new List<object?> { JsSymbols.Block };

        var current = cons.Rest; // Skip the 'block' symbol
        while (current is Cons { IsEmpty: false } c)
        {
            statements.Add(TransformExpression(c.Head));
            current = c.Rest;
        }

        return Cons.FromEnumerable(statements);
    }

    /// <summary>
    /// Transforms an await expression.
    /// (await expr) => expr (the Promise infrastructure handles the suspension)
    /// </summary>
    private Cons TransformAwait(Cons cons)
    {
        // cons is (await expression)
        var parts = ConsList(cons);
        if (parts.Count < 2)
        {
            return cons;
        }

        var expression = parts[1];
        var transformedExpr = TransformExpression(expression);

        // For now, keep the await construct - the evaluator will handle it
        return Cons.FromEnumerable([JsSymbols.Await, transformedExpr]);
    }

    /// <summary>
    /// Transforms a variable declaration.
    /// </summary>
    private Cons TransformVariableDeclaration(Cons cons)
    {
        var parts = ConsList(cons);
        if (parts.Count < 3)
        {
            return cons;
        }

        var keyword = parts[0]; // let/var/const
        var name = parts[1];
        var value = parts[2];

        return Cons.FromEnumerable([
            keyword,
            name,
            TransformExpression(value)
        ]);
    }

    /// <summary>
    /// Transforms a return statement.
    /// In async context, this should resolve the promise.
    /// </summary>
    private Cons TransformReturn(Cons cons)
    {
        var parts = ConsList(cons);
        if (parts.Count < 2)
        {
            return cons;
        }

        var value = parts[1];

        // Transform the return value
        var transformedValue = TransformExpression(value);

        // In an async function context, we need to call resolve
        // For now, we'll transform the return and let the evaluator handle it
        return Cons.FromEnumerable([
            JsSymbols.Return,
            transformedValue
        ]);
    }

    /// <summary>
    /// Transforms an if statement.
    /// </summary>
    private Cons TransformIf(Cons cons)
    {
        var parts = ConsList(cons);
        if (parts.Count < 3)
        {
            return cons;
        }

        var condition = TransformExpression(parts[1]);
        var thenBranch = TransformExpression(parts[2]);
        var elseBranch = parts.Count > 3 ? TransformExpression(parts[3]) : null;

        if (elseBranch != null)
        {
            return Cons.FromEnumerable([
                JsSymbols.If,
                condition,
                thenBranch,
                elseBranch
            ]);
        }

        return Cons.FromEnumerable([
            JsSymbols.If,
            condition,
            thenBranch
        ]);
    }

    /// <summary>
    /// Transforms an expression statement.
    /// </summary>
    private Cons TransformExpressionStatement(Cons cons)
    {
        var parts = ConsList(cons);
        if (parts.Count < 2)
        {
            return cons;
        }

        return Cons.FromEnumerable([
            JsSymbols.ExpressionStatement,
            TransformExpression(parts[1])
        ]);
    }

    /// <summary>
    /// Transforms a function call.
    /// </summary>
    private Cons TransformCall(Cons cons)
    {
        var transformed = new List<object?> { JsSymbols.Call };

        var current = cons.Rest; // Skip the 'call' symbol
        while (current is Cons { IsEmpty: false } c)
        {
            transformed.Add(TransformExpression(c.Head));
            current = c.Rest;
        }

        return Cons.FromEnumerable(transformed);
    }

    /// <summary>
    /// Transforms an assignment.
    /// </summary>
    private Cons TransformAssign(Cons cons)
    {
        var parts = ConsList(cons);
        if (parts.Count < 3)
        {
            return cons;
        }

        return Cons.FromEnumerable([
            JsSymbols.Assign,
            parts[1],
            TransformExpression(parts[2])
        ]);
    }

    /// <summary>
    /// Transforms a try-catch-finally statement.
    /// Recursively transforms the try block, catch block, and finally block.
    /// </summary>
    private Cons TransformTry(Cons cons)
    {
        // cons is (try tryBlock catchClause? finallyBlock?)
        var parts = ConsList(cons);
        if (parts.Count < 2)
        {
            return cons;
        }

        var tryBlock = TransformExpression(parts[1]);

        object? catchClause = null;
        if (parts.Count > 2 && parts[2] != null)
            // catchClause is (catch param block)
        {
            if (parts[2] is Cons { IsEmpty: false } catchCons)
            {
                var catchParts = ConsList(catchCons);
                if (catchParts.Count >= 3)
                {
                    var catchSymbol = catchParts[0]; // 'catch'
                    var catchParam = catchParts[1];
                    var catchBlock = TransformExpression(catchParts[2]);

                    catchClause = Cons.FromEnumerable([
                        catchSymbol,
                        catchParam,
                        catchBlock
                    ]);
                }
            }
        }

        object? finallyBlock = null;
        if (parts.Count > 3 && parts[3] != null)
        {
            finallyBlock = TransformExpression(parts[3]);
        }

        return Cons.FromEnumerable([
            JsSymbols.Try,
            tryBlock,
            catchClause,
            finallyBlock
        ]);
    }

    /// <summary>
    /// Transforms a Cons by recursively transforming its head and rest.
    /// </summary>
    private Cons TransformCons(Cons cons)
    {
        var items = new List<object?>();
        var current = cons;

        while (current is Cons { IsEmpty: false } c)
        {
            items.Add(TransformExpression(c.Head));
            current = c.Rest;
        }

        return Cons.FromEnumerable(items);
    }

    /// <summary>
    /// Transforms a for-of or for-await-of loop that contains await expressions in the body.
    /// Following @rogeralsing's CPS insight: the body's last fragment returns the loop head
    /// as continuation, creating a natural loop via the event queue.
    /// </summary>
    private Cons TransformForOfWithAwaitInBody(Cons forOfCons, List<object?> statements, int index,
        Symbol resolveParam, Symbol rejectParam)
    {
        // Parse: (for-of/for-await-of (let/var/const variable) iterable body)
        var parts = ConsList(forOfCons);
        if (parts.Count < 4)
        {
            // Malformed, continue with rest
            var restStatements = statements.Skip(index + 1).ToList();
            var continuation = ChainStatementsWithAwaits(restStatements, 0, resolveParam, rejectParam);
            return Cons.FromEnumerable([JsSymbols.Block, forOfCons, continuation]);
        }

        var loopType = parts[0]; // for-of or for-await-of
        var variableDecl = parts[1]; // (let/var/const variable)
        var iterableExpr = parts[2]; // iterable expression
        var loopBody = parts[3]; // loop body

        // Extract variable info
        Symbol? variableName = null;
        Symbol? varKeyword = null;
        if (variableDecl is Cons { IsEmpty: false } varDeclCons)
        {
            var varDeclParts = ConsList(varDeclCons);
            if (varDeclParts.Count >= 2)
            {
                varKeyword = varDeclParts[0] as Symbol;
                variableName = varDeclParts[1] as Symbol;
            }
        }

        if (variableName == null || varKeyword == null)
        {
            // Can't transform, pass through
            var restStatements = statements.Skip(index + 1).ToList();
            var continuation = ChainStatementsWithAwaits(restStatements, 0, resolveParam, rejectParam);
            return Cons.FromEnumerable([JsSymbols.Block, forOfCons, continuation]);
        }

        // Transform using CPS approach:
        // 1. Get iterator once
        // 2. Create loop check that gets next value
        // 3. If done, continue after loop
        // 4. If not done, extract value, execute body, and body's continuation is back to step 2

        var iteratorVar = Symbol.Intern(string.Concat("__iterator", Guid.NewGuid().ToString("N").AsSpan(0, 8)));
        var resultVar = Symbol.Intern("__result");
        var loopCheckFunc = Symbol.Intern(string.Concat("__loopCheck", Guid.NewGuid().ToString("N").AsSpan(0, 8)));

        // Get continuation after loop completes
        var afterLoopStatements = statements.Skip(index + 1).ToList();
        var afterLoopContinuation = ChainStatementsWithAwaits(afterLoopStatements, 0, resolveParam, rejectParam);

        // Check if this is for-await-of
        var isForAwaitOf = loopType is Symbol loopSymbol && ReferenceEquals(loopSymbol, JsSymbols.ForAwaitOf);

        // Get iterator: let __iterator = __getAsyncIterator(iterable) for for-await-of
        // or let __iterator = iterable[Symbol.iterator]() for regular for-of
        var getIteratorStmt = BuildGetIteratorForTransform(iterableExpr, iteratorVar, isForAwaitOf);

        // Build the loop check function that:
        // - Gets next value
        // - Checks if done
        // - If done: calls after-loop continuation
        // - If not done: executes body with continuation back to loop check
        var loopCheckFuncBody = BuildCpsLoopCheck(
            loopCheckFunc,
            iteratorVar,
            resultVar,
            variableName,
            varKeyword,
            loopBody,
            afterLoopContinuation,
            resolveParam,
            rejectParam
        );

        // function __loopCheck() { ... }
        var loopCheckFuncDecl = Cons.FromEnumerable([
            JsSymbols.Function,
            loopCheckFunc,
            Cons.FromEnumerable([]), // no params
            loopCheckFuncBody
        ]);

        // Initial call: __loopCheck() as expression statement
        // Not a return - just call it to start the loop
        var initialCall = Cons.FromEnumerable([
            JsSymbols.ExpressionStatement,
            Cons.FromEnumerable([
                JsSymbols.Call,
                loopCheckFunc
            ])
        ]);

        // Combine: get iterator, define function, call it
        return Cons.FromEnumerable([
            JsSymbols.Block,
            getIteratorStmt,
            loopCheckFuncDecl,
            initialCall
        ]);
    }

    /// <summary>
    /// Builds the CPS loop check function.
    /// Key insight: body's continuation calls loopCheckFunc again, creating natural loop.
    /// </summary>
    private Cons BuildCpsLoopCheck(
        Symbol loopCheckFunc,
        Symbol iteratorVar,
        Symbol resultVar,
        Symbol variableName,
        Symbol varKeyword,
        object? loopBody,
        object? afterLoopContinuation,
        Symbol resolveParam,
        Symbol rejectParam)
    {
        // Call __iteratorNext(iterator) - this C# helper wraps result in Promise if needed
        // This handles both sync and async iterators uniformly
        var callIteratorNext = Cons.FromEnumerable([
            JsSymbols.Call,
            Symbol.Intern("__iteratorNext"),
            iteratorVar
        ]);

        // if (__result.done) { afterLoopContinuation } else { body + call loopCheckFunc }
        var doneCheck = Cons.FromEnumerable([
            JsSymbols.GetProperty,
            resultVar,
            "done"
        ]);

        // Extract value: let variable = __result.value;
        var extractValue = Cons.FromEnumerable([
            varKeyword,
            variableName,
            Cons.FromEnumerable([
                JsSymbols.GetProperty,
                resultVar,
                "value"
            ])
        ]);

        // Extract body statements and add call to loopCheckFunc at end
        var bodyStatements = new List<object?> { extractValue };
        if (loopBody is Cons { IsEmpty: false } bodyCons)
        {
            if (bodyCons.Head is Symbol sym && ReferenceEquals(sym, JsSymbols.Block))
            {
                var current = bodyCons.Rest;
                while (current is Cons { IsEmpty: false } c)
                {
                    bodyStatements.Add(c.Head);
                    current = c.Rest;
                }
            }
            else
            {
                bodyStatements.Add(loopBody);
            }
        }

        // Instead of adding the loop check call to body statements, we'll create a special
        // "loop resolve" function that calls the loop check instead of resolving
        // This way, ChainStatementsWithAwaits will call this function at the end instead of __resolve
        var loopResolve = Symbol.Intern(string.Concat("__loopResolve", Guid.NewGuid().ToString("N").AsSpan(0, 8)));

        // Chain the body statements with await handling
        // Use loopResolve instead of resolveParam so the body calls loopCheck at the end
        // Pass loopCheckFunc for continue and afterLoopContinuation for break
        var transformedBody = ChainStatementsWithAwaits(bodyStatements, 0, loopResolve, rejectParam, loopCheckFunc,
            afterLoopContinuation);

        // Now wrap the transformed body with the loop resolve function definition
        // function __loopResolve() { return __loopCheck(); }
        var loopResolveFuncBody = Cons.FromEnumerable([
            JsSymbols.Block,
            Cons.FromEnumerable([
                JsSymbols.Return,
                Cons.FromEnumerable([
                    JsSymbols.Call,
                    loopCheckFunc
                ])
            ])
        ]);

        var loopResolveFuncDecl = Cons.FromEnumerable([
            JsSymbols.Function,
            loopResolve,
            Cons.FromEnumerable([]), // no params
            loopResolveFuncBody
        ]);

        // The transformed body with the loop resolve function prepended
        transformedBody = Cons.FromEnumerable([
            JsSymbols.Block,
            loopResolveFuncDecl,
            transformedBody
        ]);

        // if (__result.done) { afterLoop } else { transformedBody }
        var ifStatement = Cons.FromEnumerable([
            JsSymbols.If,
            doneCheck,
            afterLoopContinuation,
            transformedBody
        ]);

        // Create the .then() callback: function(__result) { if (__result.done) {...} else {...} }
        var thenCallback = Cons.FromEnumerable([
            JsSymbols.Lambda,
            null,
            Cons.FromEnumerable([resultVar]),
            Cons.FromEnumerable([
                JsSymbols.Block,
                ifStatement
            ])
        ]);

        // __iteratorNext(iterator).then(callback)
        var thenCall = Cons.FromEnumerable([
            JsSymbols.Call,
            Cons.FromEnumerable([
                JsSymbols.GetProperty,
                callIteratorNext,
                "then"
            ]),
            thenCallback
        ]);

        // Add .catch() handler to propagate errors: .catch(__reject)
        var catchCallback = Cons.FromEnumerable([
            JsSymbols.Lambda,
            null,
            Cons.FromEnumerable([Symbol.Intern("__error")]),
            Cons.FromEnumerable([
                JsSymbols.Block,
                Cons.FromEnumerable([
                    JsSymbols.Return,
                    Cons.FromEnumerable([
                        JsSymbols.Call,
                        rejectParam,
                        Symbol.Intern("__error")
                    ])
                ])
            ])
        ]);

        var catchCall = Cons.FromEnumerable([
            JsSymbols.Call,
            Cons.FromEnumerable([
                JsSymbols.GetProperty,
                thenCall,
                "catch"
            ]),
            catchCallback
        ]);

        // Return the promise chain with error handling
        return Cons.FromEnumerable([
            JsSymbols.Block,
            Cons.FromEnumerable([
                JsSymbols.Return,
                catchCall
            ])
        ]);
    }

    /// <summary>
    /// Builds code to get an iterator from an iterable.
    /// For for-await-of: let __iterator = __getAsyncIterator(iterable);
    /// For regular for-of: let __iterator = iterable[Symbol.iterator]();
    /// </summary>
    private static Cons BuildGetIteratorForTransform(object? iterableExpr, Symbol iteratorVar, bool isForAwaitOf)
    {
        if (isForAwaitOf)
        {
            // Use __getAsyncIterator helper which tries Symbol.asyncIterator first,
            // then falls back to Symbol.iterator
            var callGetAsyncIterator = Cons.FromEnumerable([
                JsSymbols.Call,
                Symbol.Intern("__getAsyncIterator"),
                iterableExpr
            ]);

            return Cons.FromEnumerable([
                JsSymbols.Let,
                iteratorVar,
                callGetAsyncIterator
            ]);
        }

        // Regular for-of: use Symbol.iterator
        // Symbol.iterator
        var iteratorSymbol = Cons.FromEnumerable([
            JsSymbols.GetProperty,
            Symbol.Intern("Symbol"),
            "iterator"
        ]);

        // iterable[Symbol.iterator]
        var getIteratorMethod = Cons.FromEnumerable([
            JsSymbols.GetIndex,
            iterableExpr,
            iteratorSymbol
        ]);

        // iterable[Symbol.iterator]()
        var callIteratorMethod = Cons.FromEnumerable([
            JsSymbols.Call,
            getIteratorMethod
        ]);

        // let __iterator = iterable[Symbol.iterator]();
        return Cons.FromEnumerable([
            JsSymbols.Let,
            iteratorVar,
            callIteratorMethod
        ]);
    }

    /// <summary>
    /// Transforms a while loop with await in the body into a recursive CPS function.
    /// while (condition) { body with await } becomes:
    /// function __whileCheck() {
    ///     if (condition) {
    ///         body-transformed (with continuation back to __whileCheck)
    ///     } else {
    ///         continuation (after loop)
    ///     }
    /// }
    /// __whileCheck();
    /// </summary>
    private Cons TransformWhileWithAwaitInBody(Cons whileCons, List<object?> statements, int index,
        Symbol resolveParam, Symbol rejectParam)
    {
        // whileCons is (while condition body)
        var parts = ConsList(whileCons);
        if (parts.Count < 3)
        {
            return whileCons;
        }

        var condition = parts[1];
        var body = parts[2];

        // Create unique function name for the while check
        var whileCheckFunc = Symbol.Intern(string.Concat("__whileCheck", Guid.NewGuid().ToString("N").AsSpan(0, 8)));

        // Get continuation after loop completes (when condition is false)
        var afterLoopStatements = statements.Skip(index + 1).ToList();
        var afterLoopContinuation = ChainStatementsWithAwaits(afterLoopStatements, 0, resolveParam, rejectParam);

        // Extract body statements
        var bodyStatements = new List<object?>();
        if (body is Cons { IsEmpty: false } bodyCons)
        {
            if (bodyCons.Head is Symbol sym && ReferenceEquals(sym, JsSymbols.Block))
            {
                var current = bodyCons.Rest;
                while (current is Cons { IsEmpty: false } c)
                {
                    bodyStatements.Add(c.Head);
                    current = c.Rest;
                }
            }
            else
            {
                bodyStatements.Add(body);
            }
        }

        // Chain the body statements with await handling
        // Important: Pass whileCheckFunc as both resolve param (for normal completion)
        // and as loopContinueTarget (for continue statements)
        // Pass afterLoopContinuation as loopBreakTarget (for break statements)
        // The body will call whileCheckFunc at the end to continue the loop
        var transformedBodyBlock = ChainStatementsWithAwaits(bodyStatements, 0, whileCheckFunc, rejectParam,
            whileCheckFunc, afterLoopContinuation);

        // Create if statement: if (condition) { transformedBodyBlock } else { afterLoopContinuation }
        var ifStatement = Cons.FromEnumerable([
            JsSymbols.If,
            condition,
            transformedBodyBlock,
            afterLoopContinuation
        ]);

        // Create the while check function: function __whileCheck() { if (condition) {...} else {...} }
        var whileCheckFuncBody = Cons.FromEnumerable([
            JsSymbols.Block,
            ifStatement
        ]);

        var whileCheckFuncDecl = Cons.FromEnumerable([
            JsSymbols.Function,
            whileCheckFunc,
            Cons.FromEnumerable([]), // no params
            whileCheckFuncBody
        ]);

        // Initial call: __whileCheck()
        var initialCall = Cons.FromEnumerable([
            JsSymbols.ExpressionStatement,
            Cons.FromEnumerable([
                JsSymbols.Call,
                whileCheckFunc
            ])
        ]);

        // Combine: define function, call it
        return Cons.FromEnumerable([
            JsSymbols.Block,
            whileCheckFuncDecl,
            initialCall
        ]);
    }

    /// <summary>
    /// Converts a Cons to a List for easier manipulation.
    /// </summary>
    private static List<object?> ConsList(Cons cons)
    {
        var result = new List<object?>();
        var current = cons;

        while (current is { IsEmpty: false } c)
        {
            result.Add(c.Head);
            current = c.Rest;
        }

        return result;
    }

    /// <summary>
    /// Recursively searches an S-expression tree for async/generator constructs.
    /// </summary>
    /// <param name="expr">The expression to search</param>
    /// <returns>True if async/generator constructs are found</returns>
    private static bool ContainsAsyncOrGenerator(object? expr)
    {
        if (expr == null)
        {
            return false;
        }

        // Check if this is a Cons (S-expression list)
        if (expr is Cons cons)
        {
            // Check if the list is empty
            if (cons.IsEmpty)
            {
                return false;
            }

            // Check the head of the list for async/generator symbols
            if (cons.Head is Symbol symbol)
            {
                if (symbol == JsSymbols.Async ||
                    symbol == JsSymbols.AsyncExpr ||
                    symbol == JsSymbols.Await ||
                    symbol == JsSymbols.Generator ||
                    symbol == JsSymbols.Yield ||
                    symbol == JsSymbols.YieldStar)
                {
                    return true;
                }
            }

            // Recursively check head and rest
            if (ContainsAsyncOrGenerator(cons.Head))
            {
                return true;
            }

            if (ContainsAsyncOrGenerator(cons.Rest))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Helper method to create a transformed Cons from an enumerable with an origin reference.
    /// </summary>
    private static Cons MakeTransformedCons(IEnumerable<object?> items, Cons? origin)
    {
        var cons = Cons.FromEnumerable(items);
        if (origin != null)
        {
            cons.WithOrigin(origin);
        }

        return cons;
    }
}
