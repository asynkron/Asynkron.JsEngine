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

        // Transform the body
        var transformedBody = TransformExpression(body);

        // Wrap the body in a Promise constructor
        // The async function will return a new Promise that resolves with the function's result
        var promiseBody = CreateAsyncPromiseWrapper(transformedBody);

        // Return a regular function that returns a promise
        return Cons.FromEnumerable(new object?[] 
        { 
            JsSymbols.Function, 
            name, 
            parameters, 
            promiseBody 
        });
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
        var executorParams = Cons.FromEnumerable(new object?[] { resolveParam, rejectParam });
        
        // The executor body needs to handle the transformed body
        var executorBody = CreateAsyncExecutorBody(body, resolveParam, rejectParam);
        
        var executor = Cons.FromEnumerable(new object?[] 
        { 
            JsSymbols.Lambda, 
            null, 
            executorParams, 
            executorBody 
        });

        // Create: (new Promise executor)
        var promiseCall = Cons.FromEnumerable(new object?[] 
        { 
            JsSymbols.New, 
            Symbol.Intern("Promise"), 
            executor 
        });

        // Wrap in return statement
        var returnStatement = Cons.FromEnumerable(new object?[] 
        { 
            JsSymbols.Return, 
            promiseCall 
        });

        // Wrap in block
        return Cons.FromEnumerable(new object?[] 
        { 
            JsSymbols.Block, 
            returnStatement 
        });
    }

    /// <summary>
    /// Creates the body of the Promise executor for an async function.
    /// </summary>
    private object? CreateAsyncExecutorBody(object? body, Symbol resolveParam, Symbol rejectParam)
    {
        // Wrap the body in a try-catch that calls resolve/reject
        // (block
        //   (try
        //     body
        //     (catch e (call reject e))))
        
        var catchParam = Symbol.Intern("__error");
        var rejectCall = Cons.FromEnumerable(new object?[] 
        { 
            JsSymbols.Call, 
            rejectParam, 
            catchParam 
        });

        var catchBlock = Cons.FromEnumerable(new object?[] 
        { 
            JsSymbols.Block, 
            Cons.FromEnumerable(new object?[] { JsSymbols.ExpressionStatement, rejectCall })
        });

        var tryStatement = Cons.FromEnumerable(new object?[] 
        { 
            JsSymbols.Try, 
            body, 
            catchParam, 
            catchBlock 
        });

        return Cons.FromEnumerable(new object?[] { JsSymbols.Block, tryStatement });
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
        return Cons.FromEnumerable(new object?[] { JsSymbols.Await, transformedExpr });
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

        return Cons.FromEnumerable(new object?[] 
        { 
            keyword, 
            name, 
            TransformExpression(value) 
        });
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
        return Cons.FromEnumerable(new object?[] 
        { 
            JsSymbols.Return, 
            transformedValue 
        });
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
            return Cons.FromEnumerable(new object?[] 
            { 
                JsSymbols.If, 
                condition, 
                thenBranch, 
                elseBranch 
            });
        }

        return Cons.FromEnumerable(new object?[] 
        { 
            JsSymbols.If, 
            condition, 
            thenBranch 
        });
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

        return Cons.FromEnumerable(new object?[] 
        { 
            JsSymbols.ExpressionStatement, 
            TransformExpression(parts[1]) 
        });
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

        return Cons.FromEnumerable(new object?[] 
        { 
            JsSymbols.Assign, 
            parts[1], 
            TransformExpression(parts[2]) 
        });
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
