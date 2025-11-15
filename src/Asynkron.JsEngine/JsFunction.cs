namespace Asynkron.JsEngine;

public sealed class JsFunction : IJsEnvironmentAwareCallable, IJsPropertyAccessor
{
    private readonly Symbol? _name;
    private readonly IReadOnlyList<object> _parameters; // Can be Symbol or Cons (for destructuring patterns)
    private readonly Symbol? _restParameter;
    private readonly Cons _body;
    private readonly JsEnvironment _closure;
    private readonly JsObject _properties = new();
    private JsFunction? _superConstructor;
    private JsObject? _superPrototype;

    /// <summary>
    /// The environment that is calling this function. Used for building call stacks.
    /// </summary>
    public JsEnvironment? CallingJsEnvironment { get; set; }

    public JsFunction(Symbol? name, IReadOnlyList<object> parameters, Symbol? restParameter, Cons body,
        JsEnvironment closure)
    {
        _name = name;
        _parameters = parameters;
        _restParameter = restParameter;
        _body = body.CloneDeep();
        _closure = closure;

        // Every function in JavaScript exposes a prototype object so instances created via `new` can inherit from it.
        _properties.SetProperty("prototype", new JsObject());
    }

    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        // JavaScript allows both more and fewer arguments than parameters
        // Missing arguments become undefined (null in our implementation)
        // Extra arguments are accessible via the arguments object

        var context = new EvaluationContext();
        var functionDescription = _name != null ? $"function {_name.Name}" : "anonymous function";
        var environment = new JsEnvironment(_closure, true, creatingExpression: _body, description: functionDescription);

        // Bind regular parameters (could be symbols or destructuring patterns)
        for (var i = 0; i < _parameters.Count; i++)
        {
            var parameter = _parameters[i];
            var argument = i < arguments.Count ? arguments[i] : null;

            switch (parameter)
            {
                // Simple parameter
                case Symbol symbol:
                    environment.Define(symbol, argument);
                    break;
                // Destructuring parameter
                case Cons pattern:
                    JsEvaluator.DestructureParameter(pattern, argument, environment, context);
                    break;
                default:
                    throw new InvalidOperationException($"Invalid parameter type: {parameter?.GetType().Name ?? "null"}");
            }
        }

        // Bind rest parameter if present
        if (_restParameter is not null)
        {
            var restArgs = new List<object?>();
            for (var i = _parameters.Count; i < arguments.Count; i++) restArgs.Add(arguments[i]);
            var restArray = new JsArray();
            foreach (var arg in restArgs)
            {
                restArray.Push(arg);
            }

            environment.Define(_restParameter, restArray);
        }

        // In non-strict mode JavaScript, when this is null or undefined, it should reference the global object.
        // For simplicity, we create a new empty object when this is null.
        // This handles cases where constructor functions are called without 'new'.
        var effectiveThis = thisValue ?? new JsObject();
        environment.Define(JsSymbols.This, effectiveThis);

        // Only define the function name if it's not already defined as a parameter
        // Parameters should shadow the function name
        if (_name is not null && !environment.TryGet(_name, out _))
        {
            environment.Define(_name, this);
        }

        if (_superConstructor is not null || _superPrototype is not null)
        {
            var binding = new SuperBinding(_superConstructor, _superPrototype, thisValue);
            environment.Define(JsSymbols.Super, binding);
        }

        // Hoist all var declarations before executing the function body
        HoistVariableDeclarations(_body, environment);

        var result = JsEvaluator.EvaluateBlock(_body, environment, context);

        if (context.IsReturn)
        {
            return context.FlowValue;
        }

        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // In JavaScript, functions without an explicit return statement return undefined
        return JsSymbols.Undefined;
    }

    /// <summary>
    /// Scans the function body for all var declarations and hoists them to function scope.
    /// This implements JavaScript's variable hoisting behavior where var declarations
    /// are moved to the top of the function scope (initialized to undefined).
    /// </summary>
    private static void HoistVariableDeclarations(Cons body, JsEnvironment environment)
    {
        // body is a Block: (Block statement1 statement2 ...)
        if (body.Head is not Symbol blockSymbol || !ReferenceEquals(blockSymbol, JsSymbols.Block))
        {
            return;
        }

        var statements = body.Rest;
        HoistFromStatementList(statements, environment);
    }

    private static void HoistFromStatementList(object? statements, JsEnvironment environment)
    {
        while (statements is Cons { IsEmpty: false } cons)
        {
            HoistFromStatement(cons.Head, environment);
            statements = cons.Rest;
        }
    }

    private static void HoistFromStatement(object? statement, JsEnvironment environment)
    {
        while (true)
        {
            if (statement is not Cons cons)
            {
                return;
            }

            if (cons.Head is not Symbol symbol)
            {
                return;
            }

            if (ReferenceEquals(symbol, JsSymbols.Var))
            {
                // Found a var declaration: (Var name initializer)
                var target = cons.Rest.Head;

                // Handle simple identifier case
                if (target is Symbol varName)
                {
                    // Pre-declare the variable with undefined if it doesn't exist
                    if (!environment.TryGet(varName, out _))
                    {
                        environment.Define(varName, JsSymbols.Undefined);
                    }
                }
                // Handle destructuring patterns
                else if (target is Cons { Head: Symbol patternSymbol } patternCons && (ReferenceEquals(patternSymbol, JsSymbols.ArrayPattern) || ReferenceEquals(patternSymbol, JsSymbols.ObjectPattern)))
                {
                    // Hoist identifiers from destructuring pattern
                    HoistFromDestructuringPattern(patternCons, environment);
                }
            }
            else if (ReferenceEquals(symbol, JsSymbols.Block))
            {
                // Recursively hoist from nested blocks
                HoistFromStatementList(cons.Rest, environment);
            }
            else if (ReferenceEquals(symbol, JsSymbols.If))
            {
                // Hoist from if branches: (If condition thenBranch elseBranch)
                var thenBranch = cons.Rest.Rest.Head;
                HoistFromStatement(thenBranch, environment);

                // Check if we have an else branch
                if (cons.Rest.Rest.Rest is { IsEmpty: false } elseCons)
                {
                    var elseBranch = elseCons.Head;
                    if (elseBranch != null)
                    {
                        statement = elseBranch;
                        continue;
                    }
                }
            }
            else if (ReferenceEquals(symbol, JsSymbols.For))
            {
                // Hoist from for loop body: (For init condition update body)
                var body = cons.Rest.Rest.Rest.Rest.Head;
                statement = body;
                continue;
            }
            else if (ReferenceEquals(symbol, JsSymbols.ForIn) || ReferenceEquals(symbol, JsSymbols.ForOf))
            {
                // Hoist from for-in/for-of loop body: (ForIn variable iterable body)
                var body = cons.Rest.Rest.Rest.Head;
                statement = body;
                continue;
            }
            else if (ReferenceEquals(symbol, JsSymbols.While) || ReferenceEquals(symbol, JsSymbols.DoWhile))
            {
                // Hoist from while loop body: (While condition body)
                var body = cons.Rest.Rest.Head;
                statement = body;
                continue;
            }
            else if (ReferenceEquals(symbol, JsSymbols.Switch))
            {
                // Hoist from switch cases: (Switch expr (Case value body)* (Default body)?)
                var cases = cons.Rest.Rest;
                HoistFromStatementList(cases, environment);
            }
            else if (ReferenceEquals(symbol, JsSymbols.Case) || ReferenceEquals(symbol, JsSymbols.Default))
            {
                // Hoist from case body
                var caseBody = cons.Rest.Rest.Head;
                statement = caseBody;
                continue;
            }
            else if (ReferenceEquals(symbol, JsSymbols.Try))
            {
                // Hoist from try/catch/finally blocks: (Try tryBlock catchClause finallyBlock)
                var tryBlock = cons.Rest.Head;
                HoistFromStatement(tryBlock, environment);

                var catchBlock = cons.Rest.Rest.Head;
                if (catchBlock is Cons { Head: Symbol catchSymbol } catchCons && ReferenceEquals(catchSymbol, JsSymbols.Catch))
                {
                    var catchBody = catchCons.Rest.Rest.Head;
                    HoistFromStatement(catchBody, environment);
                }

                var finallyBlock = cons.Rest.Rest.Rest.Head;
                if (finallyBlock is Cons)
                {
                    statement = finallyBlock;
                    continue;
                }
            }

            break;
        }
    }

    private static void HoistFromDestructuringPattern(Cons pattern, JsEnvironment environment)
    {
        // Extract identifiers from destructuring patterns and pre-declare them
        if (pattern.Head is not Symbol patternSymbol)
        {
            return;
        }

        if (ReferenceEquals(patternSymbol, JsSymbols.ArrayPattern))
        {
            // (ArrayPattern element1 element2 ...)
            var elements = pattern.Rest;
            while (elements is { } elementCons)
            {
                var element = elementCons.Head;
                switch (element)
                {
                    case Symbol identifier:
                    {
                        if (!environment.TryGet(identifier, out _))
                        {
                            environment.Define(identifier, JsSymbols.Undefined);
                        }

                        break;
                    }
                    case Cons nestedPattern:
                        HoistFromDestructuringPattern(nestedPattern, environment);
                        break;
                }
                elements = elementCons.Rest;
            }
        }
        else if (ReferenceEquals(patternSymbol, JsSymbols.ObjectPattern))
        {
            // (ObjectPattern (key value) ...)
            var properties = pattern.Rest;
            while (properties is Cons propertyCons)
            {
                if (propertyCons.Head is Cons property)
                {
                    var value = property.Rest.Head;
                    switch (value)
                    {
                        case Symbol identifier:
                        {
                            if (!environment.TryGet(identifier, out _))
                            {
                                environment.Define(identifier, JsSymbols.Undefined);
                            }

                            break;
                        }
                        case Cons nestedPattern:
                            HoistFromDestructuringPattern(nestedPattern, environment);
                            break;
                    }
                }
                properties = propertyCons.Rest;
            }
        }
    }

    public bool TryGetProperty(string name, out object? value)
    {
        return _properties.TryGetProperty(name, out value);
    }

    public void SetProperty(string name, object? value)
    {
        _properties.SetProperty(name, value);
    }

    public void SetSuperBinding(JsFunction? superConstructor, JsObject? superPrototype)
    {
        _superConstructor = superConstructor;
        _superPrototype = superPrototype;
    }
}
