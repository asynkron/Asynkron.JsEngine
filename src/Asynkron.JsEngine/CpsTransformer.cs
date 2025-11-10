namespace Asynkron.JsEngine;

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
    public bool NeedsTransformation(Cons program)
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
        while (current is Cons cons && !cons.IsEmpty)
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
        if (cons.Head is Symbol symbol)
        {
            // Transform async function declarations
            if (symbol == JsSymbols.Async)
            {
                return TransformAsyncFunction(cons);
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
        }

        // For other expressions, recursively transform children
        return TransformCons(cons);
    }

    /// <summary>
    /// Transforms an async function to return a Promise.
    /// (async name (params) body) => (function name (params) (return (new Promise ...)))
    /// </summary>
    private object? TransformAsyncFunction(Cons cons)
    {
        // cons is (async name params body)
        var parts = ConsList(cons);
        if (parts.Count < 4)
        {
            return cons;
        }

        var asyncSymbol = parts[0]; // 'async'
        var name = parts[1];        // function name or null
        var parameters = parts[2];   // parameter list
        var body = parts[3];        // function body

        // DON'T transform the body here - it will be transformed inside CreateAsyncPromiseWrapper
        // which understands the async context (return => resolve, await => .then())
        
        // Wrap the body in a Promise constructor
        // The async function will return a new Promise that resolves with the function's result
        var promiseBody = CreateAsyncPromiseWrapper(body);

        // Return a regular function (declaration) or lambda (expression) that returns a promise
        // Use Lambda for anonymous functions (function expressions), Function for named functions (function declarations)
        var functionType = name == null ? JsSymbols.Lambda : JsSymbols.Function;
        
        return Cons.FromEnumerable([
            functionType, 
            name, 
            parameters, 
            promiseBody
        ]);
    }

    /// <summary>
    /// Creates a Promise wrapper for an async function body.
    /// Wraps the body in: (block (return (new Promise (lambda (resolve reject) ...))))
    /// </summary>
    private object? CreateAsyncPromiseWrapper(object? body)
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
    private object? CreateAsyncExecutorBody(object? body, Symbol resolveParam, Symbol rejectParam)
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
            null  // No finally clause
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
        {
            // No await, just call resolve with the result
            return CreateResolveCall(body, resolveParam);
        }

        // Check if this is a block
        if (cons.Head is Symbol symbol && ReferenceEquals(symbol, JsSymbols.Block))
        {
            // Transform block statements, chaining awaits
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
        
        while (current is Cons c && !c.IsEmpty)
        {
            statements.Add(c.Head);
            current = c.Rest;
        }

        if (statements.Count == 0)
        {
            // Empty block, just resolve with null
            return CreateResolveCall(null, resolveParam);
        }

        // Build the chain of statements, handling await specially
        return ChainStatementsWithAwaits(statements, 0, resolveParam, rejectParam, null, null);
    }

    /// <summary>
    /// Recursively chains statements, handling await expressions by creating promise chains.
    /// </summary>
    private object? ChainStatementsWithAwaits(List<object?> statements, int index, Symbol resolveParam, Symbol rejectParam, Symbol? loopContinueTarget = null, object? loopBreakTarget = null)
    {
        if (index >= statements.Count)
        {
            // No more statements, resolve with null
            // In loop context, return the promise from resolve to chain iterations
            bool inLoopContext = loopContinueTarget != null || loopBreakTarget != null;
            return CreateResolveCall(null, resolveParam, inLoopContext);
        }

        var statement = statements[index];
        
        // Check if this is a break statement
        if (statement is Cons breakCons && !breakCons.IsEmpty && 
            breakCons.Head is Symbol breakSymbol && ReferenceEquals(breakSymbol, JsSymbols.Break))
        {
            // In loop context, break should call the after-loop continuation
            if (loopBreakTarget != null)
            {
                return loopBreakTarget;
            }
            // Outside loop context, just include it (will be handled at runtime)
        }
        
        // Check if this is a continue statement
        if (statement is Cons continueCons && !continueCons.IsEmpty && 
            continueCons.Head is Symbol continueSymbol && ReferenceEquals(continueSymbol, JsSymbols.Continue))
        {
            // In loop context, continue should call the loop check function (next iteration)
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
            // Outside loop context, just include it (will be handled at runtime)
        }
        
        // Check if this is a return statement
        if (statement is Cons cons && !cons.IsEmpty && 
            cons.Head is Symbol symbol && ReferenceEquals(symbol, JsSymbols.Return))
        {
            // Always transform return statements to call resolve
            return TransformReturnStatement(cons, resolveParam, statements, index);
        }
        
        // Check if this is a try-catch statement
        if (statement is Cons tryCons && !tryCons.IsEmpty && 
            tryCons.Head is Symbol trySymbol && ReferenceEquals(trySymbol, JsSymbols.Try))
        {
            // Transform try-catch with special handling for async context
            return TransformTryInAsyncContext(tryCons, statements, index, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);
        }
        
        // Check if this is a for-of statement - needs transformation if body contains await
        if (statement is Cons forOfCons && !forOfCons.IsEmpty && 
            forOfCons.Head is Symbol forOfSymbol && ReferenceEquals(forOfSymbol, JsSymbols.ForOf))
        {
            // Check if body contains await
            var parts = ConsList(forOfCons);
            if (parts.Count >= 4 && ContainsAwait(parts[3]))
            {
                // Transform for-of with await in body
                return TransformForOfWithAwaitInBody(forOfCons, statements, index, resolveParam, rejectParam);
            }
        }

        // Check if this is a for-await-of statement - always transform in async context
        // We wrap iterator.next() in Promise.resolve() to handle both sync and async iterators
        if (statement is Cons forAwaitCons && !forAwaitCons.IsEmpty && 
            forAwaitCons.Head is Symbol forAwaitSymbol && ReferenceEquals(forAwaitSymbol, JsSymbols.ForAwaitOf))
        {
            // Always transform for-await-of in async functions
            // Promise.resolve() ensures both sync and async iterators work the same way
            return TransformForOfWithAwaitInBody(forAwaitCons, statements, index, resolveParam, rejectParam);
        }
        
        // Check if this is an if statement - recursively transform branches when in loop context or contains await
        if (statement is Cons ifCons && !ifCons.IsEmpty && 
            ifCons.Head is Symbol ifSymbol && ReferenceEquals(ifSymbol, JsSymbols.If))
        {
            var ifParts = ConsList(ifCons);
            if (ifParts.Count >= 3)
            {
                var condition = ifParts[1];
                var thenBranch = ifParts[2];
                var elseBranch = ifParts.Count > 3 ? ifParts[3] : null;
                
                // Check if we need to transform branches (in loop context or contains await)
                bool needsTransform = (loopContinueTarget != null || loopBreakTarget != null) || 
                                     ContainsAwait(thenBranch) || 
                                     (elseBranch != null && ContainsAwait(elseBranch));
                
                if (needsTransform)
                {
                    // Transform branches recursively - handles break/continue/await/return
                    var transformedThen = TransformBlockInAsyncContext(thenBranch, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);
                    var transformedElse = elseBranch != null ? TransformBlockInAsyncContext(elseBranch, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget) : null;
                    
                    var transformedIf = transformedElse != null
                        ? Cons.FromEnumerable([JsSymbols.If, condition, transformedThen, transformedElse])
                        : Cons.FromEnumerable([JsSymbols.If, condition, transformedThen]);
                    
                    var ifRest = ChainStatementsWithAwaits(statements, index + 1, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);
                    
                    if (ifRest is Cons ifRestCons && !ifRestCons.IsEmpty && 
                        ifRestCons.Head is Symbol ifRestSymbol && ReferenceEquals(ifRestSymbol, JsSymbols.Block))
                    {
                        var flattenedStatements = new List<object?> { JsSymbols.Block, transformedIf };
                        var current = ifRestCons.Rest;
                        while (current is Cons c && !c.IsEmpty)
                        {
                            flattenedStatements.Add(c.Head);
                            current = c.Rest;
                        }
                        return Cons.FromEnumerable(flattenedStatements);
                    }
                    
                    return Cons.FromEnumerable([JsSymbols.Block, transformedIf, ifRest]);
                }
            }
        }
        
        // Check if this statement contains await
        if (ContainsAwait(statement))
        {
            // Transform this statement with await into a promise chain
            return TransformStatementWithAwait(statement, statements, index, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);
        }

        // No await and not a special case, just include it and continue
        var rest = ChainStatementsWithAwaits(statements, index + 1, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);
        
        // If rest is a block, flatten it to avoid nested blocks
        if (rest is Cons restCons && !restCons.IsEmpty && 
            restCons.Head is Symbol restSymbol && ReferenceEquals(restSymbol, JsSymbols.Block))
        {
            // rest is (block stmts...), so we want to create (block statement stmts...)
            var flattenedStatements = new List<object?> { JsSymbols.Block, statement };
            var current = restCons.Rest;
            while (current is Cons c && !c.IsEmpty)
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
    private object? TransformReturnStatement(Cons returnCons, Symbol resolveParam, List<object?> statements, int index)
    {
        var parts = ConsList(returnCons);
        var returnValue = parts.Count >= 2 ? parts[1] : null;
        
        // Check if return value is an await expression
        if (returnValue is Cons valueCons && !valueCons.IsEmpty &&
            valueCons.Head is Symbol awaitSym && ReferenceEquals(awaitSym, JsSymbols.Await))
        {
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
            
            // Create: promiseExpr.then(callback)
            var thenCall = Cons.FromEnumerable([
                JsSymbols.Call, 
                Cons.FromEnumerable([
                    JsSymbols.GetProperty, 
                    promiseExpr, 
                    "then"
                ]), 
                thenCallback
            ]);
            
            return Cons.FromEnumerable([
                JsSymbols.Block, 
                Cons.FromEnumerable([JsSymbols.ExpressionStatement, thenCall])
            ]);
        }
        
        // Regular return, call resolve with the value
        return CreateResolveCall(returnValue, resolveParam);
    }

    /// <summary>
    /// Checks if an expression contains an await.
    /// </summary>
    private bool ContainsAwait(object? expr)
    {
        if (expr == null)
        {
            return false;
        }

        if (expr is Cons cons && !cons.IsEmpty)
        {
            if (cons.Head is Symbol symbol && ReferenceEquals(symbol, JsSymbols.Await))
            {
                return true;
            }

            if (ContainsAwait(cons.Head) || ContainsAwait(cons.Rest))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Transforms a statement containing await into a promise chain.
    /// Example: let x = await p; [rest]
    /// Becomes: p.then(function(x) { [rest] })
    /// </summary>
    private object? TransformStatementWithAwait(object? statement, List<object?> statements, int index, Symbol resolveParam, Symbol rejectParam, Symbol? loopContinueTarget = null, object? loopBreakTarget = null)
    {
        // Handle different statement types
        if (statement is Cons cons && !cons.IsEmpty && cons.Head is Symbol symbol)
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
                    var varName = parts[1];    // variable name
                    var value = parts[2];      // value expression
                    
                    // Check if value is a simple await expression
                    if (value is Cons valueCons && !valueCons.IsEmpty && 
                        valueCons.Head is Symbol awaitSym && ReferenceEquals(awaitSym, JsSymbols.Await))
                    {
                        // Extract the promise expression from (await promise-expr)
                        var promiseExpr = valueCons.Rest.Head;
                        
                        // Create continuation for remaining statements
                        var restStatements = statements.Skip(index + 1).ToList();
                        var continuation = ChainStatementsWithAwaits(restStatements, 0, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);
                        
                        // Create the .then() callback: function(varName) { [continuation] }
                        var thenCallback = Cons.FromEnumerable([
                            JsSymbols.Lambda, 
                            null, 
                            Cons.FromEnumerable([varName]), 
                            continuation
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
                        
                        // Wrap in expression statement and block
                        return Cons.FromEnumerable([
                            JsSymbols.Block, 
                            Cons.FromEnumerable([JsSymbols.ExpressionStatement, thenCall])
                        ]);
                    }
                    // Check if value is a complex expression containing await
                    else if (ContainsAwait(value))
                    {
                        // Extract awaits from the complex expression and chain them
                        return ExtractAndChainAwaits(varKeyword, varName, value, statements, index, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);
                    }
                }
            }
            
            // Handle: return await expr;
            if (ReferenceEquals(symbol, JsSymbols.Return))
            {
                var parts = ConsList(cons);
                if (parts.Count >= 2 && parts[1] is Cons retValueCons && !retValueCons.IsEmpty)
                {
                    if (retValueCons.Head is Symbol awaitSym && ReferenceEquals(awaitSym, JsSymbols.Await))
                    {
                        // Extract the promise expression
                        var promiseExpr = retValueCons.Rest.Head;
                        
                        // Create: promiseExpr.then(resolve)
                        var thenCall = Cons.FromEnumerable([
                            JsSymbols.Call, 
                            Cons.FromEnumerable([
                                JsSymbols.GetProperty, 
                                promiseExpr, 
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
        }

        // Default: include the statement as-is and continue
        var rest = ChainStatementsWithAwaits(statements, index + 1, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);
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
    private object? TransformTryInAsyncContext(Cons tryCons, List<object?> statements, int index, Symbol resolveParam, Symbol rejectParam, Symbol? loopContinueTarget = null, object? loopBreakTarget = null)
    {
        // tryCons is (try tryBlock catchClause? finallyBlock?)
        var parts = ConsList(tryCons);
        if (parts.Count < 2)
        {
            return tryCons;
        }

        // Transform the try block with async context
        var tryBlock = parts[1];
        var transformedTryBlock = TransformBlockInAsyncContext(tryBlock, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);

        object? transformedCatchClause = null;
        if (parts.Count > 2 && parts[2] != null)
        {
            // catchClause is (catch param block)
            if (parts[2] is Cons catchCons && !catchCons.IsEmpty)
            {
                var catchParts = ConsList(catchCons);
                if (catchParts.Count >= 3)
                {
                    var catchSymbol = catchParts[0]; // 'catch'
                    var catchParam = catchParts[1];
                    var catchBlock = catchParts[2];
                    var transformedCatchBlock = TransformBlockInAsyncContext(catchBlock, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);
                    
                    transformedCatchClause = Cons.FromEnumerable([
                        catchSymbol, 
                        catchParam, 
                        transformedCatchBlock
                    ]);
                }
            }
        }

        object? transformedFinallyBlock = null;
        if (parts.Count > 3 && parts[3] != null)
        {
            transformedFinallyBlock = TransformBlockInAsyncContext(parts[3], resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);
        }

        // Continue with remaining statements
        var rest = ChainStatementsWithAwaits(statements, index + 1, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);

        // Build the transformed try statement
        var transformedTry = Cons.FromEnumerable([
            JsSymbols.Try, 
            transformedTryBlock, 
            transformedCatchClause, 
            transformedFinallyBlock
        ]);

        // If rest is just resolving null, return just the try statement
        if (IsSimpleResolveCall(rest))
        {
            return Cons.FromEnumerable([JsSymbols.Block, transformedTry]);
        }

        // Combine with rest
        return Cons.FromEnumerable([
            JsSymbols.Block, 
            transformedTry, 
            rest
        ]);
    }

    /// <summary>
    /// Checks if an expression is a simple resolve call with null.
    /// </summary>
    private bool IsSimpleResolveCall(object? expr)
    {
        if (expr is not Cons cons || cons.IsEmpty)
            return false;

        if (cons.Head is not Symbol blockSym || !ReferenceEquals(blockSym, JsSymbols.Block))
            return false;

        var blockContents = cons.Rest;
        if (blockContents is not Cons blockCons || blockCons.IsEmpty)
            return false;

        var firstStmt = blockCons.Head;
        if (firstStmt is not Cons stmtCons || stmtCons.IsEmpty)
            return false;

        // Check if it's an expression statement with a call to resolve with no args or null
        if (stmtCons.Head is Symbol exprSym && ReferenceEquals(exprSym, JsSymbols.ExpressionStatement))
        {
            var exprContent = stmtCons.Rest;
            if (exprContent is Cons exprCons && !exprCons.IsEmpty &&
                exprCons.Head is Cons callCons && !callCons.IsEmpty &&
                callCons.Head is Symbol callSym && ReferenceEquals(callSym, JsSymbols.Call))
            {
                // It's a call - check if it's calling resolve with null or no args
                var callArgs = ConsList(callCons);
                return callArgs.Count <= 2; // call resolve [null]
            }
        }

        return false;
    }

    /// <summary>
    /// Transforms a block within an async function context, handling return statements.
    /// </summary>
    private object? TransformBlockInAsyncContext(object? block, Symbol resolveParam, Symbol rejectParam, Symbol? loopContinueTarget = null, object? loopBreakTarget = null)
    {
        if (block is not Cons blockCons || blockCons.IsEmpty)
        {
            return block;
        }

        // Check if this is a block
        if (blockCons.Head is Symbol blockSymbol && ReferenceEquals(blockSymbol, JsSymbols.Block))
        {
            var statements = new List<object?>();
            var current = blockCons.Rest;
            
            while (current is Cons c && !c.IsEmpty)
            {
                statements.Add(c.Head);
                current = c.Rest;
            }

            // Chain statements with async context
            return ChainStatementsWithAwaits(statements, 0, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);
        }

        return block;
    }

    /// <summary>
    /// Extracts await expressions from a complex expression and chains them.
    /// Example: let x = (await p1) + (await p2);
    /// Becomes: p1.then(function(t1) { p2.then(function(t2) { let x = t1 + t2; [rest] }) })
    /// </summary>
    private object? ExtractAndChainAwaits(object? varKeyword, object? varName, object? expr, List<object?> statements, int index, Symbol resolveParam, Symbol rejectParam, Symbol? loopContinueTarget = null, object? loopBreakTarget = null)
    {
        // Collect all await expressions in the expression
        var awaits = new List<(object? promiseExpr, Symbol tempVar)>();
        var transformedExpr = ExtractAwaitsFromExpression(expr, awaits);

        if (awaits.Count == 0)
        {
            // No awaits found, shouldn't happen but handle gracefully
            var rest = ChainStatementsWithAwaits(statements, index + 1, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);
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
        var innerBody = ChainStatementsWithAwaits(innerStatements, 0, resolveParam, rejectParam, loopContinueTarget, loopBreakTarget);

        // Chain the awaits from right to left (innermost first)
        for (int i = awaits.Count - 1; i >= 0; i--)
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
    private object? ExtractAwaitsFromExpression(object? expr, List<(object? promiseExpr, Symbol tempVar)> awaits)
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
        
        while (current is Cons c && !c.IsEmpty)
        {
            items.Add(ExtractAwaitsFromExpression(c.Head, awaits));
            current = c.Rest;
        }

        return Cons.FromEnumerable(items);
    }

    /// <summary>
    /// Creates a call to resolve with the given value.
    /// </summary>
    private object? CreateResolveCall(object? value, Symbol resolveParam, bool shouldReturn = false)
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
    private object? TransformBlock(Cons cons)
    {
        var statements = new List<object?> { JsSymbols.Block };
        
        var current = cons.Rest; // Skip the 'block' symbol
        while (current is Cons c && !c.IsEmpty)
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
    private object? TransformAwait(Cons cons)
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
    private object? TransformVariableDeclaration(Cons cons)
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
    private object? TransformReturn(Cons cons)
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
    private object? TransformIf(Cons cons)
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
    private object? TransformExpressionStatement(Cons cons)
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
    private object? TransformCall(Cons cons)
    {
        var transformed = new List<object?> { JsSymbols.Call };
        
        var current = cons.Rest; // Skip the 'call' symbol
        while (current is Cons c && !c.IsEmpty)
        {
            transformed.Add(TransformExpression(c.Head));
            current = c.Rest;
        }

        return Cons.FromEnumerable(transformed);
    }

    /// <summary>
    /// Transforms an assignment.
    /// </summary>
    private object? TransformAssign(Cons cons)
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
    private object? TransformTry(Cons cons)
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
        {
            // catchClause is (catch param block)
            if (parts[2] is Cons catchCons && !catchCons.IsEmpty)
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
    private object? TransformCons(Cons cons)
    {
        var items = new List<object?>();
        var current = cons;
        
        while (current is Cons c && !c.IsEmpty)
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
    private object? TransformForOfWithAwaitInBody(Cons forOfCons, List<object?> statements, int index, Symbol resolveParam, Symbol rejectParam)
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

        var loopType = parts[0];        // for-of or for-await-of
        var variableDecl = parts[1];    // (let/var/const variable)
        var iterableExpr = parts[2];    // iterable expression  
        var loopBody = parts[3];        // loop body

        // Extract variable info
        Symbol? variableName = null;
        Symbol? varKeyword = null;
        if (variableDecl is Cons varDeclCons && !varDeclCons.IsEmpty)
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
        
        var iteratorVar = Symbol.Intern("__iterator" + Guid.NewGuid().ToString("N").Substring(0, 8));
        var resultVar = Symbol.Intern("__result");
        var loopCheckFunc = Symbol.Intern("__loopCheck" + Guid.NewGuid().ToString("N").Substring(0, 8));
        
        // Get continuation after loop completes
        var afterLoopStatements = statements.Skip(index + 1).ToList();
        var afterLoopContinuation = ChainStatementsWithAwaits(afterLoopStatements, 0, resolveParam, rejectParam);

        // Get iterator: let __iterator = iterable[Symbol.iterator]();
        var getIteratorStmt = BuildGetIteratorForTransform(iterableExpr, iteratorVar);

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
            Cons.FromEnumerable([]),  // no params
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
    private object? BuildCpsLoopCheck(
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
        if (loopBody is Cons bodyCons && !bodyCons.IsEmpty)
        {
            if (bodyCons.Head is Symbol sym && ReferenceEquals(sym, JsSymbols.Block))
            {
                var current = bodyCons.Rest;
                while (current is Cons c && !c.IsEmpty)
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
        var loopResolve = Symbol.Intern("__loopResolve" + Guid.NewGuid().ToString("N").Substring(0, 8));
        
        // Chain the body statements with await handling
        // Use loopResolve instead of resolveParam so the body calls loopCheck at the end
        // Pass loopCheckFunc for continue and afterLoopContinuation for break
        var transformedBody = ChainStatementsWithAwaits(bodyStatements, 0, loopResolve, rejectParam, loopCheckFunc, afterLoopContinuation);
        
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
            Cons.FromEnumerable([]),  // no params
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

        // Return the promise chain
        return Cons.FromEnumerable([
            JsSymbols.Block,
            Cons.FromEnumerable([
                JsSymbols.Return,
                thenCall
            ])
        ]);
    }

    /// <summary>
    /// Builds code to get an iterator from an iterable.
    /// Returns: let __iterator = iterable[Symbol.iterator]();
    /// </summary>
    private object? BuildGetIteratorForTransform(object? iterableExpr, Symbol iteratorVar)
    {
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
    /// Converts a Cons to a List for easier manipulation.
    /// </summary>
    private List<object?> ConsList(Cons cons)
    {
        var result = new List<object?>();
        var current = cons;
        
        while (current is Cons c && !c.IsEmpty)
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
    private bool ContainsAsyncOrGenerator(object? expr)
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
}
