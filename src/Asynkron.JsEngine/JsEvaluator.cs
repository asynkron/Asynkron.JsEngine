using System.Globalization;

namespace Asynkron.JsEngine;

public static class JsEvaluator
{
    public static object? EvaluateProgram(Cons program, JsEnvironment environment)
    {
        return EvaluateProgram(program, environment, new EvaluationContext());
    }

    private static object? EvaluateProgram(Cons program, JsEnvironment environment, EvaluationContext context)
    {
        context.SourceReference = program.SourceReference;

        if (program.IsEmpty || program.Head is not Symbol { } tag || !ReferenceEquals(tag, JsSymbols.Program))
        {
            throw new InvalidOperationException($"Program S-expression must start with the 'program' symbol.{GetSourceInfo(context)}");
        }

        // Check if program has "use strict" directive
        var hasUseStrict = false;
        var statements = program.Rest;
        if (statements is { IsEmpty: false, Head: Cons { Head: Symbol useStrictSymbol } } &&
            ReferenceEquals(useStrictSymbol, JsSymbols.UseStrict))
        {
            hasUseStrict = true;
            statements = statements.Rest; // Skip the use strict directive
        }

        // For global programs with strict mode, we need a wrapper environment
        // to enable strict mode checking without modifying the global environment
        var evalEnv = hasUseStrict ? new JsEnvironment(environment, true, true) : environment;

        object? result = null;
        foreach (var statement in statements)
        {
            result = EvaluateStatement(statement, evalEnv, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        // If there's an unhandled throw, convert it to an exception
        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return result;
    }

    public static object? EvaluateBlock(Cons block, JsEnvironment environment)
    {
        return EvaluateBlock(block, environment, new EvaluationContext());
    }

    internal static object? EvaluateBlock(Cons block, JsEnvironment environment, EvaluationContext context)
    {
        context.SourceReference = block.SourceReference;

        if (block.IsEmpty || block.Head is not Symbol { } tag || !ReferenceEquals(tag, JsSymbols.Block))
        {
            throw new InvalidOperationException($"Block S-expression must start with the 'block' symbol.{GetSourceInfo(context)}");
        }

        // Check if block has "use strict" directive
        var isStrict = false;
        var statements = block.Rest;
        if (statements is { IsEmpty: false, Head: Cons { Head: Symbol useStrictSymbol } } &&
            ReferenceEquals(useStrictSymbol, JsSymbols.UseStrict))
        {
            isStrict = true;
            statements = statements.Rest; // Skip the use strict directive
        }

        var scope = new JsEnvironment(environment, false, isStrict);
        object? result = null;
        foreach (var statement in statements)
        {
            result = EvaluateStatement(statement, scope, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        return result;
    }

    private static object? EvaluateStatement(object? statement, JsEnvironment environment, EvaluationContext context)
    {
        if (statement is not Cons cons)
        {
            return statement;
        }

        context.SourceReference = cons.SourceReference;

        if (cons.Head is not Symbol symbol)
        {
            throw new InvalidOperationException($"Statement must start with a symbol.{GetSourceInfo(context)}");
        }

        if (ReferenceEquals(symbol, JsSymbols.Let))
        {
            return EvaluateLet(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.Var))
        {
            return EvaluateVar(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.Const))
        {
            return EvaluateConst(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.Function))
        {
            return EvaluateFunctionDeclaration(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.Generator))
        {
            return EvaluateGeneratorDeclaration(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.Class))
        {
            return EvaluateClass(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.If))
        {
            return EvaluateIf(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.For))
        {
            return EvaluateFor(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.ForIn))
        {
            return EvaluateForIn(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.ForOf))
        {
            return EvaluateForOf(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.ForAwaitOf))
        {
            return EvaluateForAwaitOf(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.Switch))
        {
            return EvaluateSwitch(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.Try))
        {
            return EvaluateTry(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.While))
        {
            return EvaluateWhile(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.DoWhile))
        {
            return EvaluateDoWhile(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.Break))
        {
            context.SetBreak();
            return null;
        }

        if (ReferenceEquals(symbol, JsSymbols.Continue))
        {
            context.SetContinue();
            return null;
        }

        if (ReferenceEquals(symbol, JsSymbols.EmptyStatement))
        {
            // Empty statement does nothing, just return null
            return null;
        }

        if (ReferenceEquals(symbol, JsSymbols.Return))
        {
            return EvaluateReturn(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.Throw))
        {
            return EvaluateThrow(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.ExpressionStatement))
        {
            var expression = cons.Rest.Head;
            return EvaluateExpression(expression, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.Block))
        {
            return EvaluateBlock(cons, environment, context);
        }

        return EvaluateExpression(cons, environment, context);
    }

    private static object? EvaluateIf(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var conditionExpression = cons.Rest.Head;
        var thenBranch = cons.Rest.Rest.Head;
        var elseBranchCons = cons.Rest.Rest.Rest;
        var elseBranch = elseBranchCons.IsEmpty ? null : elseBranchCons.Head;

        var condition = EvaluateExpression(conditionExpression, environment, context);
        if (IsTruthy(condition))
        {
            return EvaluateStatement(thenBranch, environment, context);
        }

        if (elseBranch is not null)
        {
            return EvaluateStatement(elseBranch, environment, context);
        }

        return null;
    }

    private static object? EvaluateWhile(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var conditionExpression = cons.Rest.Head;
        var body = cons.Rest.Rest.Head;

        object? lastResult = null;
        while (IsTruthy(EvaluateExpression(conditionExpression, environment, context)))
        {
            if (context.ShouldStopEvaluation)
            {
                break;
            }

            lastResult = EvaluateStatement(body, environment, context);

            // Demonstrate pattern matching with typed signals
            // Note: This shows the new signal-based approach
            if (context.CurrentSignal is ContinueSignal)
            {
                context.ClearContinue();
                continue;
            }

            if (context.CurrentSignal is BreakSignal)
            {
                context.ClearBreak();
                break;
            }

            if (context.CurrentSignal is ReturnSignal or ThrowFlowSignal)
                // Propagate return/throw signals up the call stack
            {
                break;
            }
        }

        return lastResult;
    }

    private static object? EvaluateDoWhile(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var conditionExpression = cons.Rest.Head;
        var body = cons.Rest.Rest.Head;

        object? lastResult = null;
        while (true)
        {
            lastResult = EvaluateStatement(body, environment, context);

            if (context.IsContinue)
            {
                context.ClearContinue();
                // fall through to condition check for the next iteration
            }
            else if (context.IsBreak)
            {
                context.ClearBreak();
                break;
            }
            else if (context.IsReturn || context.IsThrow)
            {
                break; // Propagate return/throw
            }

            if (!IsTruthy(EvaluateExpression(conditionExpression, environment, context)))
            {
                break;
            }
        }

        return lastResult;
    }

    private static object? EvaluateFor(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var initializer = cons.Rest.Head;
        var conditionExpression = cons.Rest.Rest.Head;
        var incrementExpression = cons.Rest.Rest.Rest.Head;
        var body = cons.Rest.Rest.Rest.Rest.Head;

        // Create environment for the loop, passing the for loop S-expression
        var loopJsEnvironment = new JsEnvironment(environment, creatingExpression: cons, description: "for loop");

        if (initializer is not null)
        {
            EvaluateStatement(initializer, loopJsEnvironment, context);
        }

        object? lastResult = null;
        while (conditionExpression is null ||
               IsTruthy(EvaluateExpression(conditionExpression, loopJsEnvironment, context)))
        {
            if (context.ShouldStopEvaluation)
            {
                break;
            }

            lastResult = EvaluateStatement(body, loopJsEnvironment, context);

            if (context.IsContinue)
            {
                context.ClearContinue();
                if (incrementExpression is not null)
                {
                    EvaluateExpression(incrementExpression, loopJsEnvironment, context);
                }

                continue;
            }

            if (context.IsBreak)
            {
                context.ClearBreak();
                break;
            }

            if (context.IsReturn || context.IsThrow)
            {
                break; // Propagate return/throw
            }

            if (incrementExpression is not null)
            {
                EvaluateExpression(incrementExpression, loopJsEnvironment, context);
            }
        }

        return lastResult;
    }

    private static object? EvaluateForIn(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        // (for-in (let/var/const variable) iterable body) OR (for-in identifier iterable body)
        var firstArg = cons.Rest.Head;
        Symbol variableName;

        // Check if first argument is a variable declaration or just an identifier
        if (firstArg is Cons variableDecl)
        {
            // Extract variable name from declaration
            variableName = ExpectSymbol(variableDecl.Rest.Head, "Expected variable name in for...in loop.", context);
        }
        else if (firstArg is Symbol identifier)
        {
            // Using existing variable
            variableName = identifier;
        }
        else
        {
            throw new InvalidOperationException($"Expected variable declaration or identifier in for...in loop.{GetSourceInfo(context)}");
        }

        var iterableExpression = cons.Rest.Rest.Head;
        var body = cons.Rest.Rest.Rest.Head;

        // Evaluate the iterable
        var iterable = EvaluateExpression(iterableExpression, environment, context);

        var loopJsEnvironment = new JsEnvironment(environment, creatingExpression: cons, description: "for-in loop");
        object? lastResult = null;

        // Get keys to iterate over
        List<string> keys = [];
        switch (iterable)
        {
            case JsObject jsObject:
            {
                keys.AddRange(jsObject.GetOwnPropertyNames());

                break;
            }
            case JsArray jsArray:
            {
                for (var i = 0; i < jsArray.Items.Count; i++)
                    keys.Add(i.ToString(CultureInfo.InvariantCulture));
                break;
            }
            case string str:
            {
                for (var i = 0; i < str.Length; i++)
                    keys.Add(i.ToString(CultureInfo.InvariantCulture));
                break;
            }
        }

        foreach (var key in keys)
        {
            if (context.ShouldStopEvaluation)
            {
                break;
            }

            // Set loop variable
            // If using existing variable, update in parent scope, otherwise define in loop scope
            if (firstArg is Symbol)
            {
                environment.Assign(variableName, key);
            }
            else
            {
                loopJsEnvironment.Define(variableName, key);
            }

            lastResult = EvaluateStatement(body, loopJsEnvironment, context);

            if (context.IsContinue)
            {
                context.ClearContinue();
                continue;
            }

            if (context.IsBreak)
            {
                context.ClearBreak();
                break;
            }

            if (context.IsReturn || context.IsThrow)
            {
                break; // Propagate return/throw
            }
        }

        return lastResult;
    }

    private static object? EvaluateForOf(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        // (for-of (let/var/const variable) iterable body) OR (for-of identifier iterable body)
        var firstArg = cons.Rest.Head;

        var variableName = firstArg switch
        {
            // Check if first argument is a variable declaration or just an identifier
            Cons variableDecl => ExpectSymbol(variableDecl.Rest.Head, "Expected variable name in for...of loop.",
                context),
            Symbol identifier => identifier,
            _ => throw new InvalidOperationException(
                $"Expected variable declaration or identifier in for...of loop.{GetSourceInfo(context)}")
        };

        var iterableExpression = cons.Rest.Rest.Head;
        var body = cons.Rest.Rest.Rest.Head;

        // Evaluate the iterable
        var iterable = EvaluateExpression(iterableExpression, environment, context);

        var loopJsEnvironment = new JsEnvironment(environment, creatingExpression: cons, description: "for-of loop");
        object? lastResult = null;

        // Get values to iterate over
        List<object?> values = [];
        switch (iterable)
        {
            case JsArray jsArray:
            {
                for (var i = 0; i < jsArray.Items.Count; i++)
                {
                    values.Add(jsArray.GetElement(i));
                }

                break;
            }
            case string str:
            {
                foreach (var c in str)
                {
                    values.Add(c.ToString());
                }

                break;
            }
            default:
                throw new InvalidOperationException(FormatErrorMessage($"Cannot iterate over non-iterable value '{iterable}'", cons) + ".");
        }

        foreach (var value in values.TakeWhile(_ => !context.ShouldStopEvaluation))
        {
            // Set loop variable
            // If using existing variable, update in parent scope, otherwise define in loop scope
            if (firstArg is Symbol)
            {
                environment.Assign(variableName, value);
            }
            else
            {
                loopJsEnvironment.Define(variableName, value);
            }

            lastResult = EvaluateStatement(body, loopJsEnvironment, context);

            if (context.IsContinue)
            {
                context.ClearContinue();
                continue;
            }

            if (context.IsBreak)
            {
                context.ClearBreak();
                break;
            }

            if (context.IsReturn || context.IsThrow)
            {
                break; // Propagate return/throw
            }
        }

        return lastResult;
    }

    private static object? EvaluateForAwaitOf(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        // (for-await-of (let/var/const variable) iterable body) OR (for-await-of identifier iterable body)
        // This implements async iteration with support for:
        // 1. Symbol.asyncIterator protocol
        // 2. Fallback to Symbol.iterator
        // 3. Built-in iterables (arrays, strings, generators)
        var firstArg = cons.Rest.Head;

        var variableName = firstArg switch
        {
            // Check if first argument is a variable declaration or just an identifier
            Cons variableDecl => ExpectSymbol(variableDecl.Rest.Head, "Expected variable name in for await...of loop.",
                context),
            Symbol identifier => identifier,
            _ => throw new InvalidOperationException(
                $"Expected variable declaration or identifier in for await...of loop.{GetSourceInfo(context)}")
        };

        var iterableExpression = cons.Rest.Rest.Head;
        var body = cons.Rest.Rest.Rest.Head;

        // Evaluate the iterable
        var iterable = EvaluateExpression(iterableExpression, environment, context);

        var loopJsEnvironment = new JsEnvironment(environment, creatingExpression: cons, description: "for-await-of loop");
        object? lastResult = null;

        // Try to get an iterator using the async iterator protocol
        object? iterator = null;

        // Check for Symbol.asyncIterator first
        if (iterable is JsObject jsObj)
        {
            // Get Symbol.asyncIterator
            var asyncIteratorSymbol = JsSymbol.For("Symbol.asyncIterator");
            var asyncIteratorKey = $"@@symbol:{asyncIteratorSymbol.GetHashCode()}";
            if (jsObj.TryGetProperty(asyncIteratorKey, out var asyncIteratorMethod) &&
                asyncIteratorMethod is IJsCallable asyncIteratorCallable)
            {
                // Call the async iterator method to get the async iterator
                iterator = asyncIteratorCallable.Invoke([], jsObj);
            }
            // Fallback to Symbol.iterator if Symbol.asyncIterator is not present
            else
            {
                var iteratorSymbol = JsSymbol.For("Symbol.iterator");
                var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";
                if (jsObj.TryGetProperty(iteratorKey, out var iteratorMethod) &&
                    iteratorMethod is IJsCallable iteratorCallable)
                    // Call the iterator method to get the iterator
                {
                    iterator = iteratorCallable.Invoke([], jsObj);
                }
            }
        }

        // If we have an iterator from the protocol, use it
        if (iterator is JsObject iteratorObj)
        {
            // Iterate using the iterator protocol
            while (true)
            {
                if (context.ShouldStopEvaluation)
                {
                    break;
                }

                // Call next() on the iterator
                if (!iteratorObj.TryGetProperty("next", out var nextMethod) ||
                    nextMethod is not IJsCallable nextCallable)
                {
                    throw new InvalidOperationException($"Iterator must have a 'next' method.{GetSourceInfo(context)}");
                }

                var nextResult = nextCallable.Invoke([], iteratorObj);

                // Handle promise results (for async iterators)
                if (nextResult is JsObject resultObj)
                {
                    // Check if the result is a promise
                    if (resultObj.TryGetProperty("then", out var thenMethod) && thenMethod is IJsCallable)
                        // For now, we can't truly await promises in synchronous evaluation
                        // The CPS transformation should handle this for async functions
                        // For testing purposes, if it's a resolved promise, we try to extract the value
                        // This is a limitation - proper async iteration requires CPS transformation
                    {
                        throw new InvalidOperationException(
                            $"Async iteration with promises requires async function context. Use for await...of inside an async function.{GetSourceInfo(context)}");
                    }

                    // Check if iteration is done
                    var done = resultObj.TryGetProperty("done", out var doneValue) && doneValue is bool and true;
                    if (done)
                    {
                        break;
                    }

                    // Get the value
                    if (!resultObj.TryGetProperty("value", out var value))
                    {
                        continue;
                    }

                    // Set loop variable
                    // If using existing variable, update in parent scope, otherwise define in loop scope
                    if (firstArg is Symbol)
                    {
                        environment.Assign(variableName, value);
                    }
                    else
                    {
                        loopJsEnvironment.Define(variableName, value);
                    }

                    lastResult = EvaluateStatement(body, loopJsEnvironment, context);

                    if (context.IsContinue)
                    {
                        context.ClearContinue();
                        continue;
                    }

                    if (context.IsBreak)
                    {
                        context.ClearBreak();
                        break;
                    }

                    if (context.IsReturn || context.IsThrow)
                    {
                        break; // Propagate return/throw
                    }
                }
                else
                {
                    break;
                }
            }

            return lastResult;
        }

        // Fallback to built-in iterable handling
        List<object?> values = [];
        switch (iterable)
        {
            // Regular array - collect values
            case JsArray jsArray:
            {
                for (var i = 0; i < jsArray.Items.Count; i++)
                    values.Add(jsArray.GetElement(i));
                break;
            }
            // Generator - iterate through its values
            case JsGenerator generator:
            {
                while (true)
                {
                    var nextResult = generator.Next(null);
                    if (nextResult is JsObject resultObj)
                    {
                        var done = resultObj.TryGetProperty("done", out var doneValue) && doneValue is bool and true;
                        if (done)
                        {
                            break;
                        }

                        if (resultObj.TryGetProperty("value", out var value))
                        {
                            values.Add(value);
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                break;
            }
            case string str:
            {
                foreach (var c in str)
                {
                    values.Add(c.ToString());
                }

                break;
            }
            default:
                throw new InvalidOperationException(FormatErrorMessage($"Cannot iterate over non-iterable value '{iterable}'", cons) + ".");
        }

        // Iterate over collected values
        foreach (var value in values.TakeWhile(_ => !context.ShouldStopEvaluation))
        {
            // Set loop variable
            // If using existing variable, update in parent scope, otherwise define in loop scope
            if (firstArg is Symbol)
            {
                environment.Assign(variableName, value);
            }
            else
            {
                loopJsEnvironment.Define(variableName, value);
            }

            lastResult = EvaluateStatement(body, loopJsEnvironment, context);

            if (context.IsContinue)
            {
                context.ClearContinue();
                continue;
            }

            if (context.IsBreak)
            {
                context.ClearBreak();
                break;
            }

            if (context.IsReturn || context.IsThrow)
            {
                break; // Propagate return/throw
            }
        }

        return lastResult;
    }

    private static object? EvaluateSwitch(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var discriminantExpression = cons.Rest.Head;
        var clauses = ExpectCons(cons.Rest.Rest.Head, "Expected switch clause list.", context);
        var discriminant = EvaluateExpression(discriminantExpression, environment, context);
        var hasMatched = false; // Once a clause matches, we keep executing subsequent clauses to model fallthrough.
        object? result = null;

        foreach (var clauseEntry in clauses)
        {
            var clause = ExpectCons(clauseEntry, "Expected switch clause.", context);
            var tag = ExpectSymbol(clause.Head, "Expected switch clause tag.", context);

            if (ReferenceEquals(tag, JsSymbols.Case))
            {
                var testExpression = clause.Rest.Head;
                var body = ExpectCons(clause.Rest.Rest.Head, "Expected case body block.", context);

                if (!hasMatched)
                {
                    var testValue = EvaluateExpression(testExpression, environment, context);
                    hasMatched = Equals(discriminant, testValue);
                }

                if (hasMatched)
                {
                    result = ExecuteSwitchBody(body, environment, result, context);
                    if (context.IsBreak)
                    {
                        context.ClearBreak();
                        return result;
                    }

                    if (context.IsReturn || context.IsThrow)
                    {
                        return result; // Propagate
                    }
                }

                continue;
            }

            if (ReferenceEquals(tag, JsSymbols.Default))
            {
                var body = ExpectCons(clause.Rest.Head, "Expected default body block.", context);

                if (!hasMatched)
                {
                    hasMatched = true;
                }

                result = ExecuteSwitchBody(body, environment, result, context);
                if (context.IsBreak)
                {
                    context.ClearBreak();
                    return result;
                }

                if (context.IsReturn || context.IsThrow)
                {
                    return result; // Propagate
                }

                continue;
            }

            throw new InvalidOperationException($"Unknown switch clause.{GetSourceInfo(context)}");
        }

        return result;
    }

    private static object? ExecuteSwitchBody(Cons body, JsEnvironment environment, object? currentResult,
        EvaluationContext context)
    {
        var result = currentResult;
        foreach (var statement in body.Rest)
        {
            result = EvaluateStatement(statement, environment, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        return result;
    }

    private static object? EvaluateTry(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var tryStatement = cons.Rest.Head;
        var catchClause = cons.Rest.Rest.Head;
        var finallyClause = cons.Rest.Rest.Rest.Head;

        object? result = null;
        object? thrownValue = null;
        var hasThrow = false;

        // Execute try block
        result = EvaluateStatement(tryStatement, environment, context);

        // Check if a throw occurred
        if (context.IsThrow)
        {
            thrownValue = context.FlowValue;
            hasThrow = true;

            if (catchClause is Cons catchCons && ReferenceEquals(catchCons.Head, JsSymbols.Catch))
            {
                // Clear the throw state before executing catch block
                context.Clear();
                result = ExecuteCatchClause(catchCons, thrownValue, environment, context);
                // If catch handled it successfully, don't re-throw
                if (!context.IsThrow)
                {
                    hasThrow = false;
                }
            }
            // If no catch clause or catch didn't handle it, keep the throw state
        }

        // Execute finally block regardless
        if (finallyClause is Cons finallyCons)
        {
            // Save current signal in case finally changes it
            var savedSignal = context.CurrentSignal;

            EvaluateStatement(finallyCons, environment, context);

            // If finally didn't set a new signal, restore the previous one
            if (context.CurrentSignal is null && hasThrow)
            {
                context.SetThrow(thrownValue);
            }
        }

        return result;
    }

    private static object? EvaluateLet(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var target = cons.Rest.Head;

        // Check if this is a destructuring pattern
        if (target is Cons { Head: Symbol patternSymbol } patternCons &&
            (ReferenceEquals(patternSymbol, JsSymbols.ArrayPattern) ||
             ReferenceEquals(patternSymbol, JsSymbols.ObjectPattern)))
        {
            var valueExpression = cons.Rest.Rest.Head;
            var value = EvaluateExpression(valueExpression, environment, context);
            DestructureAndDefine(patternCons, value, environment, false, context);
            return value;
        }

        // Simple identifier case
        var name = ExpectSymbol(target, "Expected identifier in let declaration.", context);
        var initializer = cons.Rest.Rest.Head;
        var hasInitializer = !ReferenceEquals(initializer, JsSymbols.Uninitialized);
        var simpleValue = hasInitializer ? EvaluateExpression(initializer, environment, context) : JsSymbols.Undefined;
        environment.Define(name, simpleValue);
        return simpleValue;
    }

    private static object? EvaluateVar(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var target = cons.Rest.Head;

        // Check if this is a destructuring pattern
        if (target is Cons { Head: Symbol patternSymbol } patternCons &&
            (ReferenceEquals(patternSymbol, JsSymbols.ArrayPattern) ||
             ReferenceEquals(patternSymbol, JsSymbols.ObjectPattern)))
        {
            var valueExpression = cons.Rest.Rest.Head;
            var value = EvaluateExpression(valueExpression, environment, context);
            DestructureAndDefineFunctionScoped(patternCons, value, environment, context);
            return value;
        }

        // Simple identifier case
        var name = ExpectSymbol(target, "Expected identifier in var declaration.", context);
        var initializer = cons.Rest.Rest.Head;
        var hasInitializer = !ReferenceEquals(initializer, JsSymbols.Uninitialized);
        var varValue = hasInitializer ? EvaluateExpression(initializer, environment, context) : JsSymbols.Undefined;
        environment.DefineFunctionScoped(name, varValue, hasInitializer);
        return environment.Get(name);
    }

    private static object? EvaluateConst(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var target = cons.Rest.Head;

        // Check if this is a destructuring pattern
        if (target is Cons { Head: Symbol patternSymbol } patternCons &&
            (ReferenceEquals(patternSymbol, JsSymbols.ArrayPattern) ||
             ReferenceEquals(patternSymbol, JsSymbols.ObjectPattern)))
        {
            var valueExpression = cons.Rest.Rest.Head;
            var value = EvaluateExpression(valueExpression, environment, context);
            DestructureAndDefine(patternCons, value, environment, true, context);
            return value;
        }

        // Simple identifier case
        var name = ExpectSymbol(target, "Expected identifier in const declaration.", context);
        var constValueExpression = cons.Rest.Rest.Head;
        var constValue = EvaluateExpression(constValueExpression, environment, context);
        environment.Define(name, constValue, true);
        return constValue;
    }

    private static object? EvaluateFunctionDeclaration(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var name = ExpectSymbol(cons.Rest.Head, "Expected function name.", context);
        var parameters = ExpectCons(cons.Rest.Rest.Head, "Expected parameter list for function.", context);
        var body = ExpectCons(cons.Rest.Rest.Rest.Head, "Expected function body block.", context);
        var (regularParams, restParam) = ParseParameterList(parameters, context);
        var function = new JsFunction(name, regularParams, restParam, body, environment);
        environment.Define(name, function);
        return function;
    }

    private static object? EvaluateGeneratorDeclaration(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var name = ExpectSymbol(cons.Rest.Head, "Expected generator function name.", context);
        var parameters = ExpectCons(cons.Rest.Rest.Head, "Expected parameter list for generator function.", context);
        var body = ExpectCons(cons.Rest.Rest.Rest.Head, "Expected generator function body block.", context);

        // Create a generator factory function that returns a new generator instance when called
        var generatorFactory = new GeneratorFactory(name, parameters, body, environment);
        environment.Define(name, generatorFactory);
        return generatorFactory;
    }

    private static object? EvaluateClass(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var name = ExpectSymbol(cons.Rest.Head, "Expected class name symbol.", context);
        var extendsEntry = cons.Rest.Rest.Head;
        var constructorExpression = cons.Rest.Rest.Rest.Head;
        var methodsList = ExpectCons(cons.Rest.Rest.Rest.Rest.Head, "Expected class body list.", context);
        var privateFieldsList = cons.Rest.Rest.Rest.Rest.Rest?.Head as Cons;

        var (superConstructor, superPrototype) = ResolveSuperclass(extendsEntry, environment, context);

        var constructorValue = EvaluateExpression(constructorExpression, environment, context);
        if (constructorValue is not JsFunction constructor)
        {
            throw new InvalidOperationException($"Class constructor must be a function.{GetSourceInfo(context)}");
        }

        // Store private field definitions on the constructor for later initialization
        if (privateFieldsList is not null)
        {
            constructor.SetProperty("__privateFields__", privateFieldsList);
        }

        environment.Define(name, constructor);

        if (!constructor.TryGetProperty("prototype", out var prototypeValue) ||
            prototypeValue is not JsObject prototype)
        {
            prototype = new JsObject();
            constructor.SetProperty("prototype", prototype);
        }

        if (superPrototype is not null)
        {
            prototype.SetPrototype(superPrototype);
        }

        if (superConstructor is not null || superPrototype is not null)
        {
            constructor.SetSuperBinding(superConstructor, superPrototype);
            if (superConstructor is not null)
            {
                constructor.SetProperty("__proto__", superConstructor);
            }
        }

        prototype.SetProperty("constructor", constructor);

        foreach (var methodExpression in methodsList)
        {
            var methodCons = ExpectCons(methodExpression, "Expected method definition.", context);
            var tag = ExpectSymbol(methodCons.Head, "Expected method tag.", context);

            if (ReferenceEquals(tag, JsSymbols.Method))
            {
                var methodName = methodCons.Rest.Head as string
                                 ?? throw new InvalidOperationException($"Expected method name.{GetSourceInfo(context)}");
                var functionExpression = methodCons.Rest.Rest.Head;
                var methodValue = EvaluateExpression(functionExpression, environment, context);

                if (methodValue is not IJsCallable)
                {
                    throw new InvalidOperationException($"Class method '{methodName}' must be callable.{GetSourceInfo(context)}");
                }

                if (methodValue is JsFunction methodFunction)
                {
                    methodFunction.SetSuperBinding(superConstructor, superPrototype);
                }

                prototype.SetProperty(methodName, methodValue);
            }
            else if (ReferenceEquals(tag, JsSymbols.StaticMethod))
            {
                // Static method - add to constructor, not prototype
                var methodName = methodCons.Rest.Head as string
                                 ?? throw new InvalidOperationException($"Expected static method name.{GetSourceInfo(context)}");
                var functionExpression = methodCons.Rest.Rest.Head;
                var methodValue = EvaluateExpression(functionExpression, environment, context);

                if (methodValue is not IJsCallable)
                {
                    throw new InvalidOperationException($"Static method '{methodName}' must be callable.{GetSourceInfo(context)}");
                }

                constructor.SetProperty(methodName, methodValue);
            }
            else if (ReferenceEquals(tag, JsSymbols.Getter))
            {
                // (getter "name" (block ...))
                var methodName = methodCons.Rest.Head as string
                                 ?? throw new InvalidOperationException($"Expected getter name.{GetSourceInfo(context)}");
                var body = ExpectCons(methodCons.Rest.Rest.Head, "Expected getter body.", context);
                var getter = new JsFunction(null, [], null, body, environment);

                if (superConstructor is not null || superPrototype is not null)
                {
                    getter.SetSuperBinding(superConstructor, superPrototype);
                }

                prototype.SetGetter(methodName, getter);
            }
            else if (ReferenceEquals(tag, JsSymbols.StaticGetter))
            {
                // Static getter - add to constructor's properties
                var methodName = methodCons.Rest.Head as string
                                 ?? throw new InvalidOperationException($"Expected static getter name.{GetSourceInfo(context)}");
                var body = ExpectCons(methodCons.Rest.Rest.Head, "Expected static getter body.", context);
                var getter = new JsFunction(null, [], null, body, environment);

                if (constructor.TryGetProperty("__properties__", out var propsValue) && propsValue is JsObject props)
                {
                    props.SetGetter(methodName, getter);
                }
                else
                    // Fall back to setting as a regular property
                {
                    constructor.SetProperty(methodName, getter);
                }
            }
            else if (ReferenceEquals(tag, JsSymbols.Setter))
            {
                // (setter "name" param (block ...))
                var methodName = methodCons.Rest.Head as string
                                 ?? throw new InvalidOperationException($"Expected setter name.{GetSourceInfo(context)}");
                var param = ExpectSymbol(methodCons.Rest.Rest.Head, "Expected setter parameter.", context);
                var body = ExpectCons(methodCons.Rest.Rest.Rest.Head, "Expected setter body.", context);
                var paramList = new[] { param };
                var setter = new JsFunction(null, paramList, null, body, environment);

                if (superConstructor is not null || superPrototype is not null)
                {
                    setter.SetSuperBinding(superConstructor, superPrototype);
                }

                prototype.SetSetter(methodName, setter);
            }
            else if (ReferenceEquals(tag, JsSymbols.StaticSetter))
            {
                // Static setter - add to constructor's properties
                var methodName = methodCons.Rest.Head as string
                                 ?? throw new InvalidOperationException($"Expected static setter name.{GetSourceInfo(context)}");
                var param = ExpectSymbol(methodCons.Rest.Rest.Head, "Expected static setter parameter.", context);
                var body = ExpectCons(methodCons.Rest.Rest.Rest.Head, "Expected static setter body.", context);
                var paramList = new[] { param };
                var setter = new JsFunction(null, paramList, null, body, environment);

                if (constructor.TryGetProperty("__properties__", out var propsValue) && propsValue is JsObject props)
                {
                    props.SetSetter(methodName, setter);
                }
                else
                    // Fall back to setting as a regular property
                {
                    constructor.SetProperty(methodName, setter);
                }
            }
            else
            {
                throw new InvalidOperationException($"Invalid entry in class body.{GetSourceInfo(context)}");
            }
        }

        // Handle static fields from private fields list
        if (privateFieldsList is not null)
        {
            foreach (var fieldExpression in privateFieldsList)
            {
                var fieldCons = fieldExpression as Cons;
                if (fieldCons is null)
                {
                    continue;
                }

                var fieldTag = fieldCons.Head as Symbol;
                if (fieldTag is null)
                {
                    continue;
                }

                if (ReferenceEquals(fieldTag, JsSymbols.StaticField))
                {
                    // (static-field "name" initializer)
                    var fieldName = fieldCons.Rest.Head as string
                                    ?? throw new InvalidOperationException($"Expected static field name.{GetSourceInfo(context)}");
                    var initializer = fieldCons.Rest.Rest.Head;

                    var initialValue = initializer is not null
                        ? EvaluateExpression(initializer, environment, context)
                        : null;

                    constructor.SetProperty(fieldName, initialValue);
                }
            }
        }

        return constructor;
    }

    private static (JsFunction? Constructor, JsObject? Prototype) ResolveSuperclass(object? extendsEntry,
        JsEnvironment environment, EvaluationContext context)
    {
        if (extendsEntry is null)
        {
            return (null, null);
        }

        var extendsCons = ExpectCons(extendsEntry, "Expected extends clause structure.", context);
        var tag = ExpectSymbol(extendsCons.Head, "Expected extends tag.", context);
        if (!ReferenceEquals(tag, JsSymbols.Extends))
        {
            throw new InvalidOperationException($"Malformed extends clause.{GetSourceInfo(context)}");
        }

        var baseExpression = extendsCons.Rest.Head;
        var baseValue = EvaluateExpression(baseExpression, environment, context);

        if (baseValue is null)
        {
            return (null, null);
        }

        if (baseValue is not JsFunction baseConstructor)
        {
            throw new InvalidOperationException($"Classes can only extend other constructors (or null).{GetSourceInfo(context)}");
        }

        if (!baseConstructor.TryGetProperty("prototype", out var prototypeValue) ||
            prototypeValue is not JsObject basePrototype)
        {
            basePrototype = new JsObject();
            baseConstructor.SetProperty("prototype", basePrototype);
        }

        return (baseConstructor, basePrototype);
    }

    private static object? EvaluateReturn(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        if (cons.Rest.IsEmpty)
        {
            context.SetReturn(null);
            return null;
        }

        var value = EvaluateExpression(cons.Rest.Head, environment, context);
        context.SetReturn(value);
        return value;
    }

    private static object? EvaluateThrow(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var valueExpression = cons.Rest.Head;
        var value = EvaluateExpression(valueExpression, environment, context);
        context.SetThrow(value);
        return value;
    }

    private static object? ExecuteCatchClause(Cons catchClause, object? thrownValue, JsEnvironment environment,
        EvaluationContext context)
    {
        var tag = ExpectSymbol(catchClause.Head, "Expected catch clause tag.", context);
        if (!ReferenceEquals(tag, JsSymbols.Catch))
        {
            throw new InvalidOperationException($"Malformed catch clause.{GetSourceInfo(context)}");
        }

        var binding = ExpectSymbol(catchClause.Rest.Head, "Expected catch binding symbol.", context);
        var body = ExpectCons(catchClause.Rest.Rest.Head, "Expected catch block.", context);

        var catchJsEnvironment = new JsEnvironment(environment);
        catchJsEnvironment.Define(binding, thrownValue);
        return EvaluateStatement(body, catchJsEnvironment, context);
    }

    private static object? EvaluateExpression(object? expression, JsEnvironment environment, EvaluationContext context)
    {
        switch (expression)
        {
            case null:
                return null;
            case bool b:
                return b;
            case string s:
                return s;
            case double d:
                return d;
            case Symbol symbol:
                // Special case: undefined is a reserved symbol that evaluates to itself
                if (ReferenceEquals(symbol, JsSymbols.Undefined))
                {
                    return symbol;
                }

                return environment.Get(symbol);
            case Cons cons:
                return EvaluateCompositeExpression(cons, environment, context);
            default:
                return expression;
        }
    }

    private static object? EvaluateCompositeExpression(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        // Set source reference for error reporting
        context.SourceReference = cons.SourceReference;
        // Debug: Console.WriteLine($"Setting SourceReference: {context.SourceReference} for {cons.Head}");

        if (cons.Head is not Symbol symbol)
        {
            throw new InvalidOperationException($"Composite expression must begin with a symbol.{GetSourceInfo(context)}");
        }

        if (ReferenceEquals(symbol, JsSymbols.Assign))
        {
            var target = ExpectSymbol(cons.Rest.Head, "Expected assignment target.", context);
            var valueExpression = cons.Rest.Rest.Head;
            var value = EvaluateExpression(valueExpression, environment, context);
            environment.Assign(target, value);
            return value;
        }

        if (ReferenceEquals(symbol, JsSymbols.DestructuringAssignment))
        {
            var pattern = ExpectCons(cons.Rest.Head, "Expected destructuring pattern.", context);
            var valueExpression = cons.Rest.Rest.Head;
            var value = EvaluateExpression(valueExpression, environment, context);
            DestructureAssignment(pattern, value, environment, context);
            return value;
        }

        if (ReferenceEquals(symbol, JsSymbols.Call))
        {
            return EvaluateCall(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.OptionalCall))
        {
            return EvaluateOptionalCall(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.ArrayLiteral))
        {
            return EvaluateArrayLiteral(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.TemplateLiteral))
        {
            return EvaluateTemplateLiteral(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.TaggedTemplate))
        {
            return EvaluateTaggedTemplate(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.ObjectLiteral))
        {
            return EvaluateObjectLiteral(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.GetIndex))
        {
            return EvaluateGetIndex(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.OptionalGetIndex))
        {
            return EvaluateOptionalGetIndex(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.SetIndex))
        {
            return EvaluateSetIndex(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.GetProperty))
        {
            return EvaluateGetProperty(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.OptionalGetProperty))
        {
            return EvaluateOptionalGetProperty(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.SetProperty))
        {
            return EvaluateSetProperty(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.New))
        {
            return EvaluateNew(cons, environment, context);
        }

        if (ReferenceEquals(symbol, JsSymbols.Negate))
        {
            var operand = EvaluateExpression(cons.Rest.Head, environment, context);
            // Handle BigInt negation
            if (operand is JsBigInt bigInt)
            {
                return -bigInt;
            }

            return -ToNumber(operand);
        }

        if (ReferenceEquals(symbol, JsSymbols.Not))
        {
            var operand = EvaluateExpression(cons.Rest.Head, environment, context);
            return !IsTruthy(operand);
        }

        if (ReferenceEquals(symbol, JsSymbols.Typeof))
        {
            // Special case: typeof can be used on undeclared variables without throwing
            // Check if the operand is a simple identifier (Symbol) that doesn't exist
            var operandExpression = cons.Rest.Head;
            if (operandExpression is Symbol operandSymbol &&
                !ReferenceEquals(operandSymbol, JsSymbols.Undefined))
            {
                // Try to get the value without throwing
                return !environment.TryGet(operandSymbol, out var value) ?
                    // Symbol doesn't exist, return "undefined" for typeof
                    "undefined" :
                    // Symbol exists, return its typeof
                    GetTypeofString(value);
            }

            // For non-symbol operands (e.g., typeof (x + y)), evaluate normally
            var operand = EvaluateExpression(operandExpression, environment, context);
            return GetTypeofString(operand);
        }

        if (ReferenceEquals(symbol, JsSymbols.Void))
        {
            // The void operator evaluates its operand and returns undefined
            var operandExpression = cons.Rest.Head;
            EvaluateExpression(operandExpression, environment, context);
            return JsSymbols.Undefined;
        }

        if (ReferenceEquals(symbol, JsSymbols.Delete))
        {
            // The delete operator deletes a property from an object
            var operandExpression = cons.Rest.Head;

            // Check if operand is a property access or index access
            if (operandExpression is Cons { Head: Symbol operandSymbol } operandCons)
            {
                // delete obj.prop or delete obj[key]
                if (ReferenceEquals(operandSymbol, JsSymbols.GetProperty))
                {
                    // delete obj.prop
                    var target = EvaluateExpression(operandCons.Rest.Head, environment, context);
                    var propertyNameObj = operandCons.Rest.Rest.Head;
                    if (target is not JsObject jsObj)
                    {
                        return true;
                    }

                    // Property name can be a string or Symbol
                    var propertyName = propertyNameObj is Symbol sym ? sym.Name : propertyNameObj?.ToString() ?? "";
                    jsObj.Remove(propertyName);
                    return true;
                }

                if (ReferenceEquals(operandSymbol, JsSymbols.GetIndex))
                {
                    // delete obj[key]
                    var target = EvaluateExpression(operandCons.Rest.Head, environment, context);
                    var key = EvaluateExpression(operandCons.Rest.Rest.Head, environment, context);

                    switch (target)
                    {
                        // Handle array deletion - set element to undefined to create a hole
                        case JsArray jsArray when TryConvertToIndex(key, out var arrayIndex):
                            jsArray.SetElement(arrayIndex, JsSymbols.Undefined);
                            return true;
                        case JsObject jsObj:
                        {
                            var keyStr = ToString(key);
                            jsObj.Remove(keyStr);
                            return true;
                        }
                        default:
                            return true;
                    }
                }
            }

            // For other cases (like delete of a variable or non-property access), evaluate and return true
            // In non-strict mode, delete always returns true for non-property-references
            EvaluateExpression(operandExpression, environment, context);
            return true;
        }

        if (ReferenceEquals(symbol, JsSymbols.Lambda))
        {
            var maybeName = cons.Rest.Head as Symbol;
            var parameters = ExpectCons(cons.Rest.Rest.Head, "Expected lambda parameters list.", context);
            var body = ExpectCons(cons.Rest.Rest.Rest.Head, "Expected lambda body block.", context);
        var (regularParams, restParam) = ParseParameterList(parameters, context);
            return new JsFunction(maybeName, regularParams, restParam, body, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Generator))
        {
            // Handle generator expressions like: function*() { yield 1; }
            var maybeName = cons.Rest.Head as Symbol;
            var parameters = ExpectCons(cons.Rest.Rest.Head, "Expected generator parameters list.", context);
            var body = ExpectCons(cons.Rest.Rest.Rest.Head, "Expected generator body block.", context);
            return new GeneratorFactory(maybeName, parameters, body, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Yield))
        {
            // Evaluate the value to yield
            var value = EvaluateExpression(cons.Rest.Head, environment, context);

            // Check if we have a yield tracker (only present in generator context)
            try
            {
                var trackerObj = environment.Get(Symbol.Intern("__yieldTracker__"));
                if (trackerObj is not YieldTracker tracker || !tracker.ShouldYield())
                {
                    return null;
                }

                // This is the yield we should stop at
                context.SetYield(value);
                return value;

                // Otherwise, this yield was already processed - skip it and return null
                // (the value is not meaningful when skipping)
            }
            catch (InvalidOperationException)
            {
                // No tracker found - yield is outside a generator (shouldn't happen)
                throw new InvalidOperationException(FormatErrorMessage("yield can only be used inside a generator function", cons) + ".");
            }
        }

        if (!ReferenceEquals(symbol, JsSymbols.Ternary))
        {
            return EvaluateBinary(cons, environment, symbol, context);
        }

        var condition = EvaluateExpression(cons.Rest.Head, environment, context);
        var thenBranch = cons.Rest.Rest.Head;
        var elseBranch = cons.Rest.Rest.Rest.Head;
        return IsTruthy(condition)
            ? EvaluateExpression(thenBranch, environment, context)
            : EvaluateExpression(elseBranch, environment, context);

    }

    private static object? EvaluateCall(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var calleeExpression = cons.Rest.Head;
        var (callee, thisValue) = ResolveCallee(calleeExpression, environment, context);
        if (callee is not IJsCallable callable)
        {
            var calleeType = callee?.GetType().Name ?? "null";
            var calleeValue = callee?.ToString() ?? "null";
            var errorMessage = FormatErrorMessage($"Attempted to call a non-callable value of type {calleeType}: {calleeValue}", cons);
            throw new InvalidOperationException(errorMessage + ".");
        }

        var arguments = new List<object?>();
        foreach (var argumentExpression in cons.Rest.Rest)
            // Check if this is a spread argument
        {
            if (argumentExpression is Cons { Head: Symbol head } spreadCons && ReferenceEquals(head, JsSymbols.Spread))
            {
                var spreadValue = EvaluateExpression(spreadCons.Rest.Head, environment, context);
                // Spread arrays
                if (spreadValue is JsArray array)
                {
                    foreach (var element in array.Items)
                    {
                        arguments.Add(element);
                    }
                }
                else
                {
                    throw new InvalidOperationException(FormatErrorMessage("Spread operator can only be applied to arrays", spreadCons) + ".");
                }
            }
            else
            {
                arguments.Add(EvaluateExpression(argumentExpression, environment, context));
            }
        }

        try
        {
            // If this is an environment-aware callable, set the calling environment
            if (callable is IJsEnvironmentAwareCallable envAware)
            {
                envAware.CallingJsEnvironment = environment;
            }

            // If this is a debug-aware function, set the environment and context
            if (callable is not DebugAwareHostFunction debugFunc)
            {
                return callable.Invoke(arguments, thisValue);
            }

            debugFunc.CurrentJsEnvironment = environment;
            debugFunc.CurrentContext = context;

            return callable.Invoke(arguments, thisValue);
        }
        catch (ThrowSignal signal)
        {
            // Propagate the throw to the calling context
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
    }

    private static (object? Callee, object? ThisValue) ResolveCallee(object? calleeExpression, JsEnvironment environment,
        EvaluationContext context)
    {
        switch (calleeExpression)
        {
            case Symbol superSymbol when ReferenceEquals(superSymbol, JsSymbols.Super):
            {
                var binding = ExpectSuperBinding(environment, context);
                if (binding.Constructor is null)
                {
                    throw new InvalidOperationException(FormatErrorMessage("Super constructor is not available in this context",
                        calleeExpression as Cons) + ".");
                }

                return (binding.Constructor, binding.ThisValue);
            }
            case Cons { Head: Symbol head } propertyCons when ReferenceEquals(head, JsSymbols.GetProperty):
            {
                var targetExpression = propertyCons.Rest.Head;
                var propertyName = propertyCons.Rest.Rest.Head as string
                                   ?? throw new InvalidOperationException(
                                       $"Property access requires a string name.{GetSourceInfo(context)}");

                if (targetExpression is Symbol targetSymbol && ReferenceEquals(targetSymbol, JsSymbols.Super))
                {
                    var binding = ExpectSuperBinding(environment, context);
                    return binding.TryGetProperty(propertyName, out var superValue)
                        ? (superValue, binding.ThisValue)
                        : (null, binding.ThisValue);
                }

                var target = EvaluateExpression(targetExpression, environment, context);
                return TryGetPropertyValue(target, propertyName, out var value)
                    ? (value, target)
                    : (null, target);
            }
            case Cons { Head: Symbol { } indexHead } indexCons when
                ReferenceEquals(indexHead, JsSymbols.GetIndex):
            {
                var targetExpression = indexCons.Rest.Head;
                var indexExpression = indexCons.Rest.Rest.Head;

                if (targetExpression is Symbol indexTargetSymbol && ReferenceEquals(indexTargetSymbol, JsSymbols.Super))
                {
                    var binding = ExpectSuperBinding(environment, context);
                    var superIndex = EvaluateExpression(indexExpression, environment, context);
                    var superPropertyName = ToPropertyName(superIndex)
                                            ?? throw new InvalidOperationException(
                                                $"Unsupported index value '{superIndex}'.{GetSourceInfo(context)}");

                    return binding.TryGetProperty(superPropertyName, out var superValue)
                        ? (superValue, binding.ThisValue)
                        : (null, binding.ThisValue);
                }

                var target = EvaluateExpression(targetExpression, environment, context);
                var index = EvaluateExpression(indexExpression, environment, context);

                if (target is JsArray jsArray && TryConvertToIndex(index, out var arrayIndex))
                {
                    return (jsArray.GetElement(arrayIndex), target);
                }

                var propertyName = ToPropertyName(index);
                if (propertyName is not null && TryGetPropertyValue(target, propertyName, out var value))
                {
                    return (value, target);
                }

                return (null, target);
            }
            default:
                return (EvaluateExpression(calleeExpression, environment, context), null);
        }
    }

    private static object EvaluateArrayLiteral(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var array = new JsArray();
        foreach (var elementExpression in cons.Rest)
            // Check if this is a spread element
        {
            if (elementExpression is Cons { Head: Symbol head } spreadCons && ReferenceEquals(head, JsSymbols.Spread))
            {
                var spreadValue = EvaluateExpression(spreadCons.Rest.Head, environment, context);
                // Spread arrays
                if (spreadValue is JsArray spreadArray)
                {
                    foreach (var arrayElement in spreadArray.Items)
                    {
                        array.Push(arrayElement);
                    }
                }
                else
                {
                    throw new InvalidOperationException(FormatErrorMessage("Spread operator can only be applied to arrays", spreadCons) + ".");
                }
            }
            else
            {
                array.Push(EvaluateExpression(elementExpression, environment, context));
            }
        }

        // Add standard array methods
        StandardLibrary.AddArrayMethods(array);

        return array;
    }

    private static object EvaluateTemplateLiteral(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var result = new System.Text.StringBuilder();

        foreach (var part in cons.Rest)
        {
            if (part is string str)
            {
                result.Append(str);
            }
            else
            {
                // Evaluate the expression and convert to string
                var value = EvaluateExpression(part, environment, context);
                result.Append(ConvertToString(value));
            }
        }

        return result.ToString();
    }

    private static string ConvertToString(object? value)
    {
        return value switch
        {
            //TODO: [object, Object] ??
            null => "null",
            string s => s,
            bool b => b ? "true" : "false",
            double d => d.ToString(CultureInfo.InvariantCulture),
            IJsCallable => "function() { [native code] }",
            _ => value.ToString() ?? ""
        };
    }

    private static object? EvaluateTaggedTemplate(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        // Format: (taggedTemplate tag stringsArray rawStringsArray expr1 expr2 ...)
        var rest = cons.Rest;

        // Get the tag function
        var tagExpr = rest.Head;
        var tagFunction = EvaluateExpression(tagExpr, environment, context);

        if (tagFunction is not IJsCallable callable)
        {
            throw new InvalidOperationException(FormatErrorMessage("Tag in tagged template must be a function", cons) + ".");
        }

        rest = rest.Rest;

        // Get the strings array expression
        var stringsArrayExpr = rest.Head;
        var stringsArray = EvaluateExpression(stringsArrayExpr, environment, context) as JsArray;
        if (stringsArray == null)
        {
            throw new InvalidOperationException(FormatErrorMessage("Tagged template strings array is invalid", cons) + ".");
        }

        rest = rest.Rest;

        // Get the raw strings array expression
        var rawStringsArrayExpr = rest.Head;
        var rawStringsArray = EvaluateExpression(rawStringsArrayExpr, environment, context) as JsArray;
        if (rawStringsArray == null)
        {
            throw new InvalidOperationException(FormatErrorMessage("Tagged template raw strings array is invalid", cons) + ".");
        }

        rest = rest.Rest;

        // Create a template object with a 'raw' property
        var templateObj = new JsObject();

        // Copy strings array properties
        for (var i = 0; i < stringsArray.Items.Count; i++) templateObj[i.ToString(CultureInfo.InvariantCulture)] = stringsArray.Items[i];
        templateObj["length"] = (double)stringsArray.Items.Count;

        // Add raw property
        templateObj["raw"] = rawStringsArray;

        // Evaluate the substitution expressions
        var substitutions = new List<object?> { templateObj };
        foreach (var exprNode in rest)
        {
            var value = EvaluateExpression(exprNode, environment, context);
            substitutions.Add(value);
        }

        // Call the tag function with the template object and substitutions
        try
        {
            return callable.Invoke(substitutions, null);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
    }

    private static object EvaluateObjectLiteral(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var result = new JsObject();
        foreach (var propertyExpression in cons.Rest)
        {
            var propertyCons = ExpectCons(propertyExpression, "Expected property description in object literal.", context);
            var propertyTag = propertyCons.Head as Symbol
                              ?? throw new InvalidOperationException(
                                  $"Object literal entries must start with a symbol.{GetSourceInfo(context)}");

            // Handle spread operator (future feature for object rest/spread)
            if (ReferenceEquals(propertyTag, JsSymbols.Spread))
            {
                var spreadValue = EvaluateExpression(propertyCons.Rest.Head, environment, context);
                if (spreadValue is JsObject spreadObj)
                {
                    foreach (var kvp in spreadObj)
                    {
                        result.SetProperty(kvp.Key, kvp.Value);
                    }
                }

                continue;
            }

            // Property name can be a string literal or an expression (for computed properties)
            var propertyNameOrExpression = propertyCons.Rest.Head;
            string propertyName;

            if (propertyNameOrExpression is string str)
            {
                propertyName = str;
            }
            else
            {
                // Computed property name - evaluate the expression
                var propertyNameValue = EvaluateExpression(propertyNameOrExpression, environment, context);
                propertyName = ToPropertyName(propertyNameValue)
                               ?? throw new InvalidOperationException(
                                   $"Cannot convert '{propertyNameValue}' to property name.{GetSourceInfo(context)}");
            }

            if (ReferenceEquals(propertyTag, JsSymbols.Property))
            {
                var valueExpression = propertyCons.Rest.Rest.Head;
                var value = EvaluateExpression(valueExpression, environment, context);
                result.SetProperty(propertyName, value);
            }
            else if (ReferenceEquals(propertyTag, JsSymbols.Getter))
            {
                // (getter "name" (block ...))
                var body = ExpectCons(propertyCons.Rest.Rest.Head, "Expected getter body.", context);
                var getter = new JsFunction(null, [], null, body, environment);
                result.SetGetter(propertyName, getter);
            }
            else if (ReferenceEquals(propertyTag, JsSymbols.Setter))
            {
                // (setter "name" param (block ...))
                var param = ExpectSymbol(propertyCons.Rest.Rest.Head, "Expected setter parameter.", context);
                var body = ExpectCons(propertyCons.Rest.Rest.Rest.Head, "Expected setter body.", context);
                var paramList = new[] { param };
                var setter = new JsFunction(null, paramList, null, body, environment);
                result.SetSetter(propertyName, setter);
            }
            else
            {
                throw new InvalidOperationException($"Unknown property type: {propertyTag}{GetSourceInfo(context)}");
            }
        }

        return result;
    }

    private static object? EvaluateGetProperty(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var targetExpression = cons.Rest.Head;
        var propertyName = cons.Rest.Rest.Head as string
                           ?? throw new InvalidOperationException($"Property access requires a string name.{GetSourceInfo(context)}");

        if (targetExpression is Symbol { } superSymbol && ReferenceEquals(superSymbol, JsSymbols.Super))
        {
            var binding = ExpectSuperBinding(environment, context);
            if (binding.TryGetProperty(propertyName, out var superValue))
            {
                return superValue;
            }

            throw new InvalidOperationException($"Cannot read property '{propertyName}' from super prototype.{GetSourceInfo(context)}");
        }

        var target = EvaluateExpression(targetExpression, environment, context);
        if (TryGetPropertyValue(target, propertyName, out var value))
        {
            return value;
        }

        // Return undefined for non-existent properties (JavaScript behavior)
        return JsSymbols.Undefined;
    }

    private static object? EvaluateSetProperty(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var targetExpression = cons.Rest.Head;
        var propertyName = cons.Rest.Rest.Head as string
                           ?? throw new InvalidOperationException($"Property assignment requires a string name.{GetSourceInfo(context)}");

         if (targetExpression is Symbol { } superSymbol && ReferenceEquals(superSymbol, JsSymbols.Super))
         {
             throw new InvalidOperationException($"Assigning through super is not supported in this interpreter.{GetSourceInfo(context)}");
         }

         var valueExpression = cons.Rest.Rest.Rest.Head;
        var target = EvaluateExpression(targetExpression, environment, context);
        var value = EvaluateExpression(valueExpression, environment, context);
        AssignPropertyValue(target, propertyName, value);
        return value;
    }

    private static object? EvaluateOptionalGetProperty(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var targetExpression = cons.Rest.Head;
        var propertyName = cons.Rest.Rest.Head as string
                           ?? throw new InvalidOperationException($"Property access requires a string name.{GetSourceInfo(context)}");

        var target = EvaluateExpression(targetExpression, environment, context);

        // If target is null or undefined, return undefined
        if (IsNullish(target))
        {
            return JsSymbols.Undefined;
        }

        if (TryGetPropertyValue(target, propertyName, out var value))
        {
            return value;
        }

        return JsSymbols.Undefined;
    }

    private static object? EvaluateOptionalGetIndex(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var targetExpression = cons.Rest.Head;
        var indexExpression = cons.Rest.Rest.Head;

        var target = EvaluateExpression(targetExpression, environment, context);

        // If target is null or undefined, return undefined
        if (IsNullish(target))
        {
            return JsSymbols.Undefined;
        }

        var indexValue = EvaluateExpression(indexExpression, environment, context);

        if (target is JsArray jsArray && TryConvertToIndex(indexValue, out var arrayIndex))
        {
            return jsArray.GetElement(arrayIndex);
        }

        if (target is TypedArrayBase typedArray && TryConvertToIndex(indexValue, out var typedIndex))
        {
            return typedArray.GetElement(typedIndex);
        }

        var propertyName = ToPropertyName(indexValue);
        if (propertyName is not null && TryGetPropertyValue(target, propertyName, out var value))
        {
            return value;
        }

        return JsSymbols.Undefined;
    }

    private static object? EvaluateOptionalCall(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var calleeExpression = cons.Rest.Head;

        var callee = EvaluateExpression(calleeExpression, environment, context);

        // If callee is null or undefined, return undefined
        if (IsNullish(callee))
        {
            return JsSymbols.Undefined;
        }

        // Evaluate arguments
        var arguments = new List<object?>();
        foreach (var argumentExpression in cons.Rest.Rest)
        {
            if (argumentExpression is Cons { Head: Symbol sym } spreadCons && ReferenceEquals(sym, JsSymbols.Spread))
            {
                var spreadValue = EvaluateExpression(spreadCons.Rest.Head, environment, context);
                if (spreadValue is JsArray array)
                {
                    foreach (var element in array.Items)
                    {
                        arguments.Add(element);
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Spread operator can only be applied to arrays.{GetSourceInfo(context)}");
                }
            }
            else
            {
                arguments.Add(EvaluateExpression(argumentExpression, environment, context));
            }
        }

        if (callee is not IJsCallable callable)
        {
            return JsSymbols.Undefined;
        }

        try
        {
            return callable.Invoke(arguments, null);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
    }

    private static bool IsNullish(object? value)
    {
        return value is null || (value is Symbol sym && ReferenceEquals(sym, JsSymbols.Undefined));
    }

    private static object? EvaluateGetIndex(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var targetExpression = cons.Rest.Head;
        var indexExpression = cons.Rest.Rest.Head;

        if (targetExpression is Symbol superSymbol && ReferenceEquals(superSymbol, JsSymbols.Super))
        {
            var binding = ExpectSuperBinding(environment, context);
            var superIndexValue = EvaluateExpression(indexExpression, environment, context);
            var superPropertyName = ToPropertyName(superIndexValue)
                                    ?? throw new InvalidOperationException(
                                        $"Unsupported index value '{superIndexValue}'.{GetSourceInfo(context)}");

            if (binding.TryGetProperty(superPropertyName, out var superPropertyValue))
            {
                return superPropertyValue;
            }

            throw new InvalidOperationException($"Cannot read property '{superPropertyName}' from super prototype.{GetSourceInfo(context)}");
        }

        var target = EvaluateExpression(targetExpression, environment, context);
        var indexValue = EvaluateExpression(indexExpression, environment, context);

        if (target is JsArray jsArray && TryConvertToIndex(indexValue, out var arrayIndex))
        {
            return jsArray.GetElement(arrayIndex);
        }

        if (target is TypedArrayBase typedArray && TryConvertToIndex(indexValue, out var typedIndex))
        {
            return typedArray.GetElement(typedIndex);
        }

        var propertyName = ToPropertyName(indexValue)
                           ?? throw new InvalidOperationException($"Unsupported index value '{indexValue}'.{GetSourceInfo(context)}");

        if (TryGetPropertyValue(target, propertyName, out var propertyValue))
        {
            return propertyValue;
        }

        // Return undefined for non-existent properties (JavaScript behavior)
        return JsSymbols.Undefined;
    }

    private static object? EvaluateSetIndex(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var targetExpression = cons.Rest.Head;
        var indexExpression = cons.Rest.Rest.Head;
        var valueExpression = cons.Rest.Rest.Rest.Head;

        if (targetExpression is Symbol { } superSymbol && ReferenceEquals(superSymbol, JsSymbols.Super))
        {
            throw new InvalidOperationException($"Assigning through super is not supported in this interpreter.{GetSourceInfo(context)}");
        }

        var target = EvaluateExpression(targetExpression, environment, context);
        var indexValue = EvaluateExpression(indexExpression, environment, context);
        var value = EvaluateExpression(valueExpression, environment, context);

        if (target is JsArray jsArray && TryConvertToIndex(indexValue, out var arrayIndex))
        {
            jsArray.SetElement(arrayIndex, value);
            return value;
        }

        if (target is TypedArrayBase typedArray && TryConvertToIndex(indexValue, out var typedIndex))
        {
            var numValue = value switch
            {
                double d => d,
                int i => (double)i,
                _ => 0.0
            };
            typedArray.SetElement(typedIndex, numValue);
            return value;
        }

        var propertyName = ToPropertyName(indexValue)
                           ?? throw new InvalidOperationException($"Unsupported index value '{indexValue}'.{GetSourceInfo(context)}");

        AssignPropertyValue(target, propertyName, value);
        return value;
    }

    private static object? EvaluateNew(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        var constructorExpression = cons.Rest.Head;
        var constructor = EvaluateExpression(constructorExpression, environment, context);
        if (constructor is not IJsCallable callable)
        {
            var constructorType = constructor?.GetType().Name ?? "null";
            var constructorValue = constructor?.ToString() ?? "null";
            var expressionStr = constructorExpression?.ToString() ?? "null";
            throw new InvalidOperationException(
                FormatErrorMessage($"Attempted to construct with a non-callable value (expression: {expressionStr}, type: {constructorType}, value: {constructorValue})", cons) + ".");
        }

        var instance = new JsObject();
        if (TryGetPropertyValue(constructor, "prototype", out var prototype) && prototype is JsObject prototypeObject)
        {
            instance.SetPrototype(prototypeObject);
        }

        // Initialize private fields from this class and all parent classes
        InitializePrivateFields(constructor, instance, environment, context);

        var arguments = new List<object?>();
        foreach (var argumentExpression in cons.Rest.Rest)
        {
            arguments.Add(EvaluateExpression(argumentExpression, environment, context));
        }

        try
        {
            var result = callable.Invoke(arguments, instance);
            return result switch
            {
                JsArray jsArray => jsArray,
                JsObject jsObject => jsObject,
                JsMap jsMap => jsMap,
                JsSet jsSet => jsSet,
                JsWeakMap jsWeakMap => jsWeakMap,
                JsWeakSet jsWeakSet => jsWeakSet,
                JsArrayBuffer buffer => buffer,
                JsDataView dataView => dataView,
                TypedArrayBase typedArray => typedArray,
                IDictionary<string, object?> dictionary => dictionary,
                _ => instance
            };
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
    }

    private static void InitializePrivateFields(object? constructor, JsObject instance, JsEnvironment environment,
        EvaluationContext context)
    {
        // First, initialize parent class private and public fields (if any)
        if (constructor is JsFunction jsFunc && TryGetPropertyValue(constructor, "__proto__", out var parent) &&
            parent is not null)
        {
            InitializePrivateFields(parent, instance, environment, context);
        }

        // Then initialize this class's private and public fields
        if (!TryGetPropertyValue(constructor, "__privateFields__", out var privateFieldsValue) ||
            privateFieldsValue is not Cons privateFieldsList)
        {
            return;
        }

        foreach (var fieldExpression in privateFieldsList)
        {
            var fieldCons = ExpectCons(fieldExpression, "Expected field definition.", context);
            var tag = ExpectSymbol(fieldCons.Head, "Expected field tag.", context);

            if (!ReferenceEquals(tag, JsSymbols.PrivateField) && !ReferenceEquals(tag, JsSymbols.PublicField))
            {
                continue;
            }

            var fieldName = fieldCons.Rest.Head as string
                            ?? throw new InvalidOperationException($"Expected field name.{GetSourceInfo(context)}");
            var initializer = fieldCons.Rest.Rest.Head;

            object? initialValue = null;
            if (initializer is not null)
            {
                // Create a temporary environment with 'this' bound to the instance
                var initEnv = new JsEnvironment(environment);
                initEnv.Define(JsSymbols.This, instance);
                initialValue = EvaluateExpression(initializer, initEnv, context);
            }

            instance.SetProperty(fieldName, initialValue);
        }
    }

    private static object? EvaluateBinary(Cons cons, JsEnvironment environment, Symbol operatorSymbol,
        EvaluationContext context)
    {
        var leftExpression = cons.Rest.Head;
        var operatorName = operatorSymbol.Name;

        // Handle unary operators (only have left operand)
        switch (operatorName)
        {
            case "~":
            {
                var operand = EvaluateExpression(leftExpression, environment, context);
                try
                {
                    return BitwiseNot(operand);
                }
                catch (InvalidOperationException ex)
                {
                    throw new InvalidOperationException(ex.Message + GetSourceInfo(context), ex);
                }
            }
            case "++prefix":
                return IncrementPrefix(leftExpression, environment, context);
            case "--prefix":
                return DecrementPrefix(leftExpression, environment, context);
            case "++postfix":
                return IncrementPostfix(leftExpression, environment, context);
            case "--postfix":
                return DecrementPostfix(leftExpression, environment, context);
        }

        // Binary operators have both left and right
        var rightExpression = cons.Rest.Rest.Head;
        var left = EvaluateExpression(leftExpression, environment, context);

        switch (operatorName)
        {
            case "&&":
                return IsTruthy(left) ? EvaluateExpression(rightExpression, environment, context) : left;
            case "||":
                return IsTruthy(left) ? left : EvaluateExpression(rightExpression, environment, context);
            case "??":
            {
                var leftIsNullish = left is null || (left is Symbol sym && ReferenceEquals(sym, JsSymbols.Undefined));
                return leftIsNullish ? EvaluateExpression(rightExpression, environment, context) : left;
            }
            case ",":
                // Comma operator: evaluate left (discard result), then evaluate and return right
                return EvaluateExpression(rightExpression, environment, context);
            case "===":
            {
                var rightStrict = EvaluateExpression(rightExpression, environment, context);
                return StrictEquals(left, rightStrict);
            }
            case "!==":
            {
                var rightStrict = EvaluateExpression(rightExpression, environment, context);
                return !StrictEquals(left, rightStrict);
            }
        }

        var right = EvaluateExpression(rightExpression, environment, context);

        try
        {
            return operatorName switch
            {
                "+" => Add(left, right),
                "-" => Subtract(left, right),
                "*" => Multiply(left, right),
                "**" => Power(left, right),
                "/" => Divide(left, right),
                "%" => Modulo(left, right),
                "&" => BitwiseAnd(left, right),
                "|" => BitwiseOr(left, right),
                "^" => BitwiseXor(left, right),
                "<<" => LeftShift(left, right),
                ">>" => RightShift(left, right),
                ">>>" => UnsignedRightShift(left, right),
                "==" => LooseEquals(left, right),
                "!=" => !LooseEquals(left, right),
                ">" => GreaterThan(left, right),
                ">=" => GreaterThanOrEqual(left, right),
                "<" => LessThan(left, right),
                "<=" => LessThanOrEqual(left, right),
                "in" => InOperator(left, right),
                "instanceof" => InstanceofOperator(left, right),
                _ => throw new InvalidOperationException($"Unsupported operator '{operatorName}'.{GetSourceInfo(context)}")
            };
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(ex.Message + GetSourceInfo(context), ex);
        }
    }

    private static (IReadOnlyList<object> regularParams, Symbol? restParam) ParseParameterList(Cons list, EvaluationContext context)
    {
        var regularParams = new List<object>();
        Symbol? restParam = null;

        foreach (var item in list)
        {
            // Check if this is a rest parameter (rest symbol paramName)
            if (item is Cons { Head: Symbol head } restCons && ReferenceEquals(head, JsSymbols.Rest))
            {
                restParam = ExpectSymbol(restCons.Rest.Head, "Expected rest parameter name.", context);
                break; // Rest parameter must be last
            }

            // Check if this is a destructuring pattern (array or object pattern)
            if (item is Cons { Head: Symbol patternType } pattern &&
                (ReferenceEquals(patternType, JsSymbols.ArrayPattern) ||
                 ReferenceEquals(patternType, JsSymbols.ObjectPattern)))
            {
                regularParams.Add(pattern);
            }
            else
            {
                regularParams.Add(ExpectSymbol(item, "Expected symbol or pattern in parameter list.", context));
            }
        }

        return (regularParams, restParam);
    }

    private static Symbol ExpectSymbol(object? value, string message, EvaluationContext context)
    {
        return value as Symbol ?? throw new InvalidOperationException($"{message}{GetSourceInfo(context)}");
    }

    private static Cons ExpectCons(object? value, string message, EvaluationContext context)
    {
        return value as Cons ?? throw new InvalidOperationException($"{message}{GetSourceInfo(context)}");
    }

    private static SuperBinding ExpectSuperBinding(JsEnvironment environment, EvaluationContext context)
    {
        object? value;
        try
        {
            value = environment.Get(JsSymbols.Super);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"Super is not available in this context.{GetSourceInfo(context)}", ex);
        }

        return value as SuperBinding ?? throw new InvalidOperationException($"Super is not available in this context.{GetSourceInfo(context)}");
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            Symbol sym when ReferenceEquals(sym, JsSymbols.Undefined) => false,
            bool b => b,
            double d => !double.IsNaN(d) && Math.Abs(d) > double.Epsilon,
            string s => s.Length > 0,
            _ => true
        };
    }

    internal static double ToNumber(this object? value)
    {
        return value switch
        {
            null => 0,
            Symbol sym when ReferenceEquals(sym, JsSymbols.Undefined) => double.NaN,
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            uint ui => ui,
            long l => l,
            ulong ul => ul,
            short s => s,
            ushort us => us,
            byte b => b,
            sbyte sb => sb,
            bool flag => flag ? 1 : 0,
            string str => StringToNumber(str),
            JsArray arr => ArrayToNumber(arr),
            JsObject => double.NaN, // Objects convert to NaN
            _ => throw new InvalidOperationException($"Cannot convert value '{value}' to a number.")
        };
    }

    private static double StringToNumber(string str)
    {
        // Empty string converts to 0
        if (string.IsNullOrEmpty(str))
        {
            return 0;
        }

        // Trim whitespace
        var trimmed = str.Trim();

        // Whitespace-only string converts to 0
        if (string.IsNullOrEmpty(trimmed))
        {
            return 0;
        }

        // Try to parse the trimmed string
        if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        // Invalid number format converts to NaN
        return double.NaN;
    }

    private static double ArrayToNumber(JsArray arr)
    {
        return arr.Items.Count switch
        {
            // Empty array converts to 0
            0 => 0,
            // Single element array converts to the number representation of that element
            1 => ToNumber(arr.Items[0]),
            _ => double.NaN
        };

        // Multi-element array converts to NaN
    }

    // Helper method for converting values to strings in array context (join/toString)
    // where null and undefined become empty strings
    internal static string ToStringForArray(object? value)
    {
        // null and undefined convert to empty string in array toString/join
        if (value is null || (value is Symbol sym && ReferenceEquals(sym, JsSymbols.Undefined)))
        {
            return "";
        }

        return ToString(value);
    }

    private static string ToString(object? value)
    {
        return value switch
        {
            null => "null",
            Symbol sym when ReferenceEquals(sym, JsSymbols.Undefined) => "undefined",
            bool b => b ? "true" : "false",
            JsBigInt bigInt => bigInt.ToString(),
            JsArray arr => ArrayToString(arr),
            JsObject => "[object Object]",
            IJsCallable => "function() { [native code] }",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string ArrayToString(JsArray arr)
    {
        // Convert each element to string and join with comma
        // Per ECMAScript spec: null and undefined are converted to empty strings
        var elements = arr.Items.Select(ToStringForArray).ToList();
        return string.Join(",", elements);
    }

    private static string GetTypeofString(object? value)
    {
        // JavaScript oddity: typeof null === "object" (historical bug)
        if (value is null)
        {
            return "object";
        }

        // Check for undefined symbol
        if (value is Symbol sym && ReferenceEquals(sym, JsSymbols.Undefined))
        {
            return "undefined";
        }

        // Check for JavaScript Symbol (primitive type)
        if (value is JsSymbol)
        {
            return "symbol";
        }

        // Check for BigInt
        if (value is JsBigInt)
        {
            return "bigint";
        }

        return value switch
        {
            bool => "boolean",
            double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte => "number",
            string => "string",
            JsFunction or HostFunction => "function",
            _ => "object"
        };
    }

    // Helper method to handle common BigInt binary operation pattern
    private static object PerformBigIntOrNumericOperation(
        object? left, 
        object? right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<double, double, object> numericOp)
    {
        // Handle BigInt op BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return bigIntOp(leftBigInt, rightBigInt);
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        return numericOp(ToNumber(left), ToNumber(right));
    }

    private static object Add(object? left, object? right)
    {
        // If either operand is a string, perform string concatenation
        if (left is string || right is string)
        {
            return ToString(left) + ToString(right);
        }

        // If either operand is an object or array, convert to string (ToPrimitive preference is string for +)
        if (left is JsObject || left is JsArray || right is JsObject || right is JsArray)
        {
            return ToString(left) + ToString(right);
        }

        // Handle BigInt + BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt + rightBigInt;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        // Otherwise, perform numeric addition
        return ToNumber(left) + ToNumber(right);
    }

    private static object Subtract(object? left, object? right)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l - r,
            (l, r) => l - r);
    }

    private static object Multiply(object? left, object? right)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l * r,
            (l, r) => l * r);
    }

    private static object Power(object? left, object? right)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => JsBigInt.Pow(l, r),
            (l, r) => Math.Pow(l, r));
    }

    private static object Divide(object? left, object? right)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l / r,
            (l, r) => l / r);
    }

    private static object Modulo(object? left, object? right)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l % r,
            (l, r) => l % r);
    }

    // Helper for comparison operations with BigInt/Number mixed comparisons
    private static bool PerformComparisonOperation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, bool> bigIntOp,
        Func<System.Numerics.BigInteger, System.Numerics.BigInteger, bool> mixedOp,
        Func<double, double, bool> numericOp)
    {
        switch (left)
        {
            // Handle BigInt comparisons
            case JsBigInt leftBigInt when right is JsBigInt rightBigInt:
                return bigIntOp(leftBigInt, rightBigInt);
            // BigInt can be compared with Number in relational operators
            case JsBigInt lbi:
            {
                var rightNum = ToNumber(right);
                if (double.IsNaN(rightNum))
                {
                    return false;
                }

                return mixedOp(lbi.Value, new System.Numerics.BigInteger(rightNum));
            }
        }

        switch (right)
        {
            case JsBigInt rbi:
            {
                var leftNum = ToNumber(left);
                if (double.IsNaN(leftNum))
                {
                    return false;
                }

                return mixedOp(new System.Numerics.BigInteger(leftNum), rbi.Value);
            }
            default:
                return numericOp(ToNumber(left), ToNumber(right));
        }
    }

    private static bool GreaterThan(object? left, object? right)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l > r,
            (l, r) => l > r,
            (l, r) => l > r);
    }

    private static bool GreaterThanOrEqual(object? left, object? right)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l >= r,
            (l, r) => l >= r,
            (l, r) => l >= r);
    }

    private static bool LessThan(object? left, object? right)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l < r,
            (l, r) => l < r,
            (l, r) => l < r);
    }

    private static bool LessThanOrEqual(object? left, object? right)
    {
        return PerformComparisonOperation(left, right,
            (l, r) => l <= r,
            (l, r) => l <= r,
            (l, r) => l <= r);
    }

    private static bool StrictEquals(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return left is not Double.NaN;
            // mirror JavaScript's NaN behaviour
        }

        if (left is null || right is null)
        {
            return false;
        }

        // BigInt strict equality
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt == rightBigInt;
        }

        // BigInt and Number are never strictly equal
        if ((left is JsBigInt && IsNumeric(right)) || (IsNumeric(left) && right is JsBigInt))
        {
            return false;
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            var leftNumber = ToNumber(left);
            var rightNumber = ToNumber(right);
            if (double.IsNaN(leftNumber) || double.IsNaN(rightNumber))
            {
                return false;
            }

            return leftNumber.Equals(rightNumber);
        }

        return left.GetType() == right.GetType() && Equals(left, right);
    }

    private static bool LooseEquals(object? left, object? right)
    {
        while (true)
        {
            // JavaScript oddity: null == undefined (but null !== undefined)
            var leftIsNullish = left is null || (left is Symbol symL && ReferenceEquals(symL, JsSymbols.Undefined));
            var rightIsNullish = right is null || (right is Symbol symR && ReferenceEquals(symR, JsSymbols.Undefined));

            if (leftIsNullish && rightIsNullish)
            {
                return true;
            }

            if (leftIsNullish || rightIsNullish)
            {
                return false;
            }

            // If types are the same, use strict equality
            if (left?.GetType() == right?.GetType())
            {
                return StrictEquals(left, right);
            }

            // BigInt == Number: compare numerically (allowed in loose equality)
            if (left is JsBigInt leftBigInt && IsNumeric(right))
            {
                var rightNum = ToNumber(right);
                if (double.IsNaN(rightNum) || double.IsInfinity(rightNum))
                {
                    return false;
                }

                //TODO: Check for fractional part, how does this work in JS?
                // Check if right is an integer and compare
                if (rightNum == Math.Floor(rightNum))
                {
                    return leftBigInt.Value == new System.Numerics.BigInteger(rightNum);
                }

                return false;
            }

            if (IsNumeric(left) && right is JsBigInt rightBigInt)
            {
                var leftNum = ToNumber(left);
                if (double.IsNaN(leftNum) || double.IsInfinity(leftNum))
                {
                    return false;
                }

                // Check if left is an integer and compare
                //TODO: Check for fractional part, how does this work in JS?
                if (leftNum == Math.Floor(leftNum))
                {
                    return new System.Numerics.BigInteger(leftNum) == rightBigInt.Value;
                }

                return false;
            }

            // BigInt == String: convert string to BigInt if possible
            if (left is JsBigInt lbi && right is string str)
            {
                try
                {
                    var rightBigInt2 = new JsBigInt(str.Trim());
                    return lbi == rightBigInt2;
                }
                catch
                {
                    return false;
                }
            }

            if (left is string str2 && right is JsBigInt rbi)
            {
                try
                {
                    var leftBigInt2 = new JsBigInt(str2.Trim());
                    return leftBigInt2 == rbi;
                }
                catch
                {
                    return false;
                }
            }

            // Type coercion for loose equality
            // Number == String: convert string to number
            if (IsNumeric(left) && right is string)
            {
                return ToNumber(left).Equals(ToNumber(right));
            }

            switch (left)
            {
                case string when IsNumeric(right):
                    return ToNumber(left).Equals(ToNumber(right));
                // Boolean == anything: convert boolean to number
                case bool:
                    left = ToNumber(left);
                    continue;
            }

            if (right is bool)
            {
                right = ToNumber(right);
                continue;
            }

            // Object/Array == Primitive: convert object/array to primitive
            if (left is JsObject or JsArray && (IsNumeric(right) || right is string))
            {
                // Try converting to primitive (via toString then toNumber if comparing to number)
                return IsNumeric(right)
                    ? ToNumber(left).Equals(ToNumber(right))
                    : ToString(left).Equals(right);
            }

            if (right is JsObject or JsArray && (IsNumeric(left) || left is string))
            {
                // Try converting to primitive
                return IsNumeric(left) ? ToNumber(left).Equals(ToNumber(right)) : left.Equals(ToString(right));
            }

            // For other cases, use strict equality
            return StrictEquals(left, right);
            break;
        }
    }

    private static bool IsNumeric(object? value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }

    private static bool TryGetPropertyValue(object? target, string propertyName, out object? value)
    {
        // First, try the common interface for types with TryGetProperty
        if (target is IJsPropertyAccessor propertyAccessor)
        {
            // For JsObject, check for getter first before delegating
            if (target is JsObject jsObject)
            {
                var getter = jsObject.GetGetter(propertyName);
                if (getter != null)
                {
                    value = getter.Invoke([], jsObject);
                    return true;
                }
            }
            
            return propertyAccessor.TryGetProperty(propertyName, out value);
        }

        // Handle types that don't implement IJsPropertyAccessor
        switch (target)
        {
            case TypedArrayBase typedArray:
                // Handle typed array properties
                switch (propertyName)
                {
                    case "length":
                        value = (double)typedArray.Length;
                        return true;
                    case "byteLength":
                        value = (double)typedArray.ByteLength;
                        return true;
                    case "byteOffset":
                        value = (double)typedArray.ByteOffset;
                        return true;
                    case "buffer":
                        value = typedArray.Buffer;
                        return true;
                    case "BYTES_PER_ELEMENT":
                        value = (double)typedArray.BytesPerElement;
                        return true;
                    case "subarray":
                        value = new HostFunction(args =>
                        {
                            var begin = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                            var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : typedArray.Length;
                            return typedArray.Subarray(begin, end);
                        });
                        return true;
                    case "set":
                        value = new HostFunction(args =>
                        {
                            if (args.Count == 0)
                            {
                                return JsSymbols.Undefined;
                            }

                            var offset = args.Count > 1 && args[1] is double d ? (int)d : 0;

                            switch (args[0])
                            {
                                case TypedArrayBase sourceTypedArray:
                                    typedArray.Set(sourceTypedArray, offset);
                                    break;
                                case JsArray sourceArray:
                                    typedArray.Set(sourceArray, offset);
                                    break;
                            }

                            return JsSymbols.Undefined;
                        });
                        return true;
                    case "slice":
                        value = CreateTypedArraySliceMethod(typedArray);
                        return true;
                }

                break;
            case JsArrayBuffer buffer:
                switch (propertyName)
                {
                    // Handle ArrayBuffer properties
                    case "byteLength":
                        value = (double)buffer.ByteLength;
                        return true;
                    case "slice":
                        value = new HostFunction(args =>
                        {
                            var begin = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                            var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : buffer.ByteLength;
                            return buffer.Slice(begin, end);
                        });
                        return true;
                    default:
                        value = null;
                        return false;
                }

            case JsDataView dataView:
                // Handle DataView properties
                switch (propertyName)
                {
                    case "buffer":
                        value = dataView.Buffer;
                        return true;
                    case "byteLength":
                        value = (double)dataView.ByteLength;
                        return true;
                    case "byteOffset":
                        value = (double)dataView.ByteOffset;
                        return true;
                    case "getInt8":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                            return (double)dataView.GetInt8(offset);
                        });
                        return true;
                    case "setInt8":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                            var val = args.Count > 1 && args[1] is double d2 ? (sbyte)(int)d2 : (sbyte)0;
                            dataView.SetInt8(offset, val);
                            return JsSymbols.Undefined;
                        });
                        return true;
                    case "getUint8":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                            return (double)dataView.GetUint8(offset);
                        });
                        return true;
                    case "setUint8":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                            var val = args.Count > 1 && args[1] is double d2 ? (byte)(int)d2 : (byte)0;
                            dataView.SetUint8(offset, val);
                            return JsSymbols.Undefined;
                        });
                        return true;
                    case "getInt16":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                            var littleEndian = args.Count > 1 && args[1] is bool and true;
                            return (double)dataView.GetInt16(offset, littleEndian);
                        });
                        return true;
                    case "setInt16":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                            var val = args.Count > 1 && args[1] is double d2 ? (short)(int)d2 : (short)0;
                            var littleEndian = args.Count > 2 && args[2] is bool and true;
                            dataView.SetInt16(offset, val, littleEndian);
                            return JsSymbols.Undefined;
                        });
                        return true;
                    case "getUint16":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                            var littleEndian = args.Count > 1 && args[1] is bool and true;
                            return (double)dataView.GetUint16(offset, littleEndian);
                        });
                        return true;
                    case "setUint16":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                            var val = args.Count > 1 && args[1] is double d2 ? (ushort)(int)d2 : (ushort)0;
                            var littleEndian = args.Count > 2 && args[2] is bool and true;
                            dataView.SetUint16(offset, val, littleEndian);
                            return JsSymbols.Undefined;
                        });
                        return true;
                    case "getInt32":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                            var littleEndian = args.Count > 1 && args[1] is bool and true;
                            return (double)dataView.GetInt32(offset, littleEndian);
                        });
                        return true;
                    case "setInt32":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                            var val = args.Count > 1 && args[1] is double d2 ? (int)d2 : 0;
                            var littleEndian = args.Count > 2 && args[2] is bool and true;
                            dataView.SetInt32(offset, val, littleEndian);
                            return JsSymbols.Undefined;
                        });
                        return true;
                    case "getUint32":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                            var littleEndian = args.Count > 1 && args[1] is bool and true;
                            return (double)dataView.GetUint32(offset, littleEndian);
                        });
                        return true;
                    case "setUint32":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                            var val = args.Count > 1 && args[1] is double d2 ? (uint)d2 : 0;
                            var littleEndian = args.Count > 2 && args[2] is bool and true;
                            dataView.SetUint32(offset, val, littleEndian);
                            return JsSymbols.Undefined;
                        });
                        return true;
                    case "getFloat32":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                            var littleEndian = args.Count > 1 && args[1] is bool and true;
                            return (double)dataView.GetFloat32(offset, littleEndian);
                        });
                        return true;
                    case "setFloat32":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                            var val = args.Count > 1 && args[1] is double d2 ? (float)d2 : 0f;
                            var littleEndian = args.Count > 2 && args[2] is bool and true;
                            dataView.SetFloat32(offset, val, littleEndian);
                            return JsSymbols.Undefined;
                        });
                        return true;
                    case "getFloat64":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d ? (int)d : 0;
                            var littleEndian = args.Count > 1 && args[1] is bool and true;
                            return dataView.GetFloat64(offset, littleEndian);
                        });
                        return true;
                    case "setFloat64":
                        value = new HostFunction(args =>
                        {
                            var offset = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                            var val = args.Count > 1 && args[1] is double d2 ? d2 : 0.0;
                            var littleEndian = args.Count > 2 && args[2] is bool and true;
                            dataView.SetFloat64(offset, val, littleEndian);
                            return JsSymbols.Undefined;
                        });
                        return true;
                }

                break;
            case IDictionary<string, object?> dictionary when dictionary.TryGetValue(propertyName, out value):
                return true;
            case double num:
                // Handle number properties (Number.prototype methods)
                var numberWrapper = StandardLibrary.CreateNumberWrapper(num);
                if (numberWrapper.TryGetProperty(propertyName, out value))
                {
                    return true;
                }

                break;
            case string str:
                // Handle string properties
                if (propertyName == "length")
                {
                    value = (double)str.Length;
                    return true;
                }

                // Handle numeric indices (bracket notation: str[0], str[1], etc.)
                if (int.TryParse(propertyName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                    index >= 0 && index < str.Length)
                {
                    value = str[index].ToString();
                    return true;
                }

                // For string methods, create a wrapper object with methods
                var stringWrapper = StandardLibrary.CreateStringWrapper(str);
                if (stringWrapper.TryGetProperty(propertyName, out value))
                {
                    return true;
                }

                break;
        }

        value = null;
        return false;
    }

    private static void AssignPropertyValue(object? target, string propertyName, object? value)
    {
        switch (target)
        {
            case JsArray jsArray:
                jsArray.SetProperty(propertyName, value);
                break;
            case JsObject jsObject:
                // Check for setter first
                var setter = jsObject.GetSetter(propertyName);
                if (setter != null)
                {
                    setter.Invoke([value], jsObject);
                }
                else
                {
                    jsObject.SetProperty(propertyName, value);
                }

                break;
            case JsFunction function:
                function.SetProperty(propertyName, value);
                break;
            case HostFunction hostFunction:
                hostFunction.SetProperty(propertyName, value);
                break;
            case IDictionary<string, object?> dictionary:
                dictionary[propertyName] = value;
                break;
            default:
                throw new InvalidOperationException($"Cannot assign property '{propertyName}' on value '{target}'.");
        }
    }

    private static bool TryConvertToIndex(object? value, out int index)
    {
        switch (value)
        {
            case int i and >= 0:
                index = i;
                return true;
            case long l and >= 0 and <= int.MaxValue:
                index = (int)l;
                return true;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                var truncated = Math.Truncate(d);
                if (Math.Abs(d - truncated) < double.Epsilon && truncated is >= 0 and <= int.MaxValue)
                {
                    index = (int)truncated;
                    return true;
                }

                break;
            case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                               parsed >= 0:
                index = parsed;
                return true;
        }

        index = 0;
        return false;
    }

    private static string? ToPropertyName(object? value)
    {
        return value switch
        {
            null => "null",
            string s => s,
            Symbol symbol => symbol.Name,
            JsSymbol jsSymbol => $"@@symbol:{jsSymbol.GetHashCode()}", // Special prefix for Symbol keys
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d when !double.IsNaN(d) && !double.IsInfinity(d) => d.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static void DestructureAndDefine(Cons pattern, object? value, JsEnvironment environment, bool isConst,
        EvaluationContext context)
    {
        if (pattern.Head is not Symbol patternType)
        {
            throw new InvalidOperationException($"Pattern must start with a symbol.{GetSourceInfo(context)}");
        }

        if (ReferenceEquals(patternType, JsSymbols.ArrayPattern))
        {
            DestructureArray(pattern, value, environment, isConst, context);
        }
        else if (ReferenceEquals(patternType, JsSymbols.ObjectPattern))
        {
            DestructureObject(pattern, value, environment, isConst, context);
        }
        else
        {
            throw new InvalidOperationException($"Unknown pattern type: {patternType}{GetSourceInfo(context)}");
        }
    }

    private static void DestructureAndDefineFunctionScoped(Cons pattern, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        if (pattern.Head is not Symbol patternType)
        {
            throw new InvalidOperationException($"Pattern must start with a symbol.{GetSourceInfo(context)}");
        }

        if (ReferenceEquals(patternType, JsSymbols.ArrayPattern))
        {
            DestructureArrayFunctionScoped(pattern, value, environment, context);
        }
        else if (ReferenceEquals(patternType, JsSymbols.ObjectPattern))
        {
            DestructureObjectFunctionScoped(pattern, value, environment, context);
        }
        else
        {
            throw new InvalidOperationException($"Unknown pattern type: {patternType}{GetSourceInfo(context)}");
        }
    }

    private static void DestructureArray(Cons pattern, object? value, JsEnvironment environment, bool isConst,
        EvaluationContext context)
    {
        if (value is not JsArray array)
        {
            throw new InvalidOperationException($"Cannot destructure non-array value in array pattern.{GetSourceInfo(context)}");
        }

        var index = 0;
        foreach (var element in pattern.Rest)
        {
            // Skip holes (null elements)
            if (element is null)
            {
                index++;
                continue;
            }

            if (element is not Cons elementCons)
            {
                throw new InvalidOperationException($"Expected pattern element to be a cons.{GetSourceInfo(context)}");
            }

            if (elementCons.Head is not Symbol elementType)
            {
                throw new InvalidOperationException($"Pattern element must start with a symbol.{GetSourceInfo(context)}");
            }

            // Handle rest element
            if (ReferenceEquals(elementType, JsSymbols.PatternRest))
            {
                var restName = ExpectSymbol(elementCons.Rest.Head, "Expected identifier for rest element.", context);
                var restArray = new JsArray();
                for (var i = index; i < array.Items.Count; i++) restArray.Push(array.Items[i]);
                environment.Define(restName, restArray, isConst);
                break;
            }

            // Handle pattern element
            if (!ReferenceEquals(elementType, JsSymbols.PatternElement))
            {
                continue;
            }

            var target = elementCons.Rest.Head;
            var defaultValue = elementCons.Rest.Rest.Head;
            var elementValue = index < array.Items.Count ? array.Items[index] : null;

            // Apply default value if element is undefined
            if (elementValue is null && defaultValue is not null)
            {
                elementValue = EvaluateExpression(defaultValue, environment, context);
            }

            switch (target)
            {
                // Check if target is a nested pattern
                case Cons { Head: Symbol nestedType } nestedPattern when ReferenceEquals(nestedType, JsSymbols.ArrayPattern) ||
                                                                         ReferenceEquals(nestedType, JsSymbols.ObjectPattern):
                    DestructureAndDefine(nestedPattern, elementValue, environment, isConst, context);
                    break;
                case Symbol identifier:
                    environment.Define(identifier, elementValue, isConst);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Expected identifier or nested pattern in array pattern element.{GetSourceInfo(context)}");
            }

            index++;
        }
    }

    private static void DestructureArrayFunctionScoped(Cons pattern, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        if (value is not JsArray array)
        {
            throw new InvalidOperationException($"Cannot destructure non-array value in array pattern.{GetSourceInfo(context)}");
        }

        var index = 0;
        foreach (var element in pattern.Rest)
        {
            // Skip holes (null elements)
            if (element is null)
            {
                index++;
                continue;
            }

            if (element is not Cons elementCons)
            {
                throw new InvalidOperationException($"Expected pattern element to be a cons.{GetSourceInfo(context)}");
            }

            if (elementCons.Head is not Symbol elementType)
            {
                throw new InvalidOperationException($"Pattern element must start with a symbol.{GetSourceInfo(context)}");
            }

            // Handle rest element
            if (ReferenceEquals(elementType, JsSymbols.PatternRest))
            {
                var restName = ExpectSymbol(elementCons.Rest.Head, "Expected identifier for rest element.", context);
                var restArray = new JsArray();
                for (var i = index; i < array.Items.Count; i++) restArray.Push(array.Items[i]);
                environment.DefineFunctionScoped(restName, restArray, true);
                break;
            }

            // Handle pattern element
            if (!ReferenceEquals(elementType, JsSymbols.PatternElement))
            {
                continue;
            }

            var target = elementCons.Rest.Head;
            var defaultValue = elementCons.Rest.Rest.Head;
            var elementValue = index < array.Items.Count ? array.Items[index] : null;

            // Apply default value if element is undefined
            if (elementValue is null && defaultValue is not null)
            {
                elementValue = EvaluateExpression(defaultValue, environment, context);
            }

            switch (target)
            {
                // Check if target is a nested pattern
                case Cons { Head: Symbol nestedType } nestedPattern when ReferenceEquals(nestedType, JsSymbols.ArrayPattern) ||
                                                                         ReferenceEquals(nestedType, JsSymbols.ObjectPattern):
                    DestructureAndDefineFunctionScoped(nestedPattern, elementValue, environment, context);
                    break;
                case Symbol identifier:
                    environment.DefineFunctionScoped(identifier, elementValue, true);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Expected identifier or nested pattern in array pattern element.{GetSourceInfo(context)}");
            }

            index++;
        }
    }

    private static void DestructureObject(Cons pattern, object? value, JsEnvironment environment, bool isConst,
        EvaluationContext context)
    {
        if (value is not JsObject obj)
        {
            throw new InvalidOperationException($"Cannot destructure non-object value in object pattern.{GetSourceInfo(context)}");
        }

        var usedKeys = new HashSet<string>();

        foreach (var property in pattern.Rest)
        {
            if (property is not Cons propertyCons)
            {
                throw new InvalidOperationException($"Expected pattern property to be a cons.{GetSourceInfo(context)}");
            }

            if (propertyCons.Head is not Symbol propertyType)
            {
                throw new InvalidOperationException($"Pattern property must start with a symbol.{GetSourceInfo(context)}");
            }

            // Handle rest property
            if (ReferenceEquals(propertyType, JsSymbols.PatternRest))
            {
                var restName = ExpectSymbol(propertyCons.Rest.Head, "Expected identifier for rest property.", context);
                var restObject = new JsObject();
                foreach (var kvp in obj)
                {
                    if (!usedKeys.Contains(kvp.Key))
                    {
                        restObject[kvp.Key] = kvp.Value;
                    }
                }

                environment.Define(restName, restObject, isConst);
                break;
            }

            // Handle pattern property
            if (!ReferenceEquals(propertyType, JsSymbols.PatternProperty))
            {
                continue;
            }

            var sourceName = propertyCons.Rest.Head as string ??
                             throw new InvalidOperationException($"Expected property name in object pattern.{GetSourceInfo(context)}");
            var target = propertyCons.Rest.Rest.Head;
            var defaultValue = propertyCons.Rest.Rest.Rest.Head;

            usedKeys.Add(sourceName);

            var propertyValue = obj.TryGetProperty(sourceName, out var val) ? val : null;

            // Apply default value if property is undefined
            if (propertyValue is null && defaultValue is not null)
            {
                propertyValue = EvaluateExpression(defaultValue, environment, context);
            }

            switch (target)
            {
                // Check if target is a nested pattern
                case Cons { Head: Symbol nestedType } nestedPattern when ReferenceEquals(nestedType, JsSymbols.ArrayPattern) ||
                                                                         ReferenceEquals(nestedType, JsSymbols.ObjectPattern):
                    DestructureAndDefine(nestedPattern, propertyValue, environment, isConst, context);
                    break;
                case Symbol identifier:
                    environment.Define(identifier, propertyValue, isConst);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Expected identifier or nested pattern in object pattern property.{GetSourceInfo(context)}");
            }
        }
    }

    private static void DestructureObjectFunctionScoped(Cons pattern, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        if (value is not JsObject obj)
        {
            throw new InvalidOperationException($"Cannot destructure non-object value in object pattern.{GetSourceInfo(context)}");
        }

        var usedKeys = new HashSet<string>();

        foreach (var property in pattern.Rest)
        {
            if (property is not Cons propertyCons)
            {
                throw new InvalidOperationException($"Expected pattern property to be a cons.{GetSourceInfo(context)}");
            }

            if (propertyCons.Head is not Symbol propertyType)
            {
                throw new InvalidOperationException($"Pattern property must start with a symbol.{GetSourceInfo(context)}");
            }

            // Handle rest property
            if (ReferenceEquals(propertyType, JsSymbols.PatternRest))
            {
                var restName = ExpectSymbol(propertyCons.Rest.Head, "Expected identifier for rest property.", context);
                var restObject = new JsObject();
                foreach (var kvp in obj)
                {
                    if (!usedKeys.Contains(kvp.Key))
                    {
                        restObject[kvp.Key] = kvp.Value;
                    }
                }

                environment.DefineFunctionScoped(restName, restObject, true);
                break;
            }

            // Handle pattern property
            if (!ReferenceEquals(propertyType, JsSymbols.PatternProperty))
            {
                continue;
            }

            var sourceName = propertyCons.Rest.Head as string ??
                             throw new InvalidOperationException($"Expected property name in object pattern.{GetSourceInfo(context)}");
            var target = propertyCons.Rest.Rest.Head;
            var defaultValue = propertyCons.Rest.Rest.Rest.Head;

            usedKeys.Add(sourceName);

            var propertyValue = obj.TryGetProperty(sourceName, out var val) ? val : null;

            // Apply default value if property is undefined
            if (propertyValue is null && defaultValue is not null)
            {
                propertyValue = EvaluateExpression(defaultValue, environment, context);
            }

            switch (target)
            {
                // Check if target is a nested pattern
                case Cons { Head: Symbol nestedType } nestedPattern when ReferenceEquals(nestedType, JsSymbols.ArrayPattern) ||
                                                                         ReferenceEquals(nestedType, JsSymbols.ObjectPattern):
                    DestructureAndDefineFunctionScoped(nestedPattern, propertyValue, environment, context);
                    break;
                case Symbol identifier:
                    environment.DefineFunctionScoped(identifier, propertyValue, true);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Expected identifier or nested pattern in object pattern property.{GetSourceInfo(context)}");
            }
        }
    }

    // Public method to destructure function parameters (called from JsFunction)
    public static void DestructureParameter(Cons pattern, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        if (pattern.Head is not Symbol patternType)
        {
            throw new InvalidOperationException($"Pattern must start with a symbol.{GetSourceInfo(context)}");
        }

        if (ReferenceEquals(patternType, JsSymbols.ArrayPattern))
        {
            DestructureArrayFunctionScoped(pattern, value, environment, context);
        }
        else if (ReferenceEquals(patternType, JsSymbols.ObjectPattern))
        {
            DestructureObjectFunctionScoped(pattern, value, environment, context);
        }
        else
        {
            throw new InvalidOperationException($"Unknown pattern type: {patternType}{GetSourceInfo(context)}");
        }
    }

    private static void DestructureAssignment(Cons pattern, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        if (pattern.Head is not Symbol patternType)
        {
            throw new InvalidOperationException($"Pattern must start with a symbol.{GetSourceInfo(context)}");
        }

        if (ReferenceEquals(patternType, JsSymbols.ArrayPattern))
        {
            DestructureArrayFunctionScoped(pattern, value, environment, context);
        }
        else if (ReferenceEquals(patternType, JsSymbols.ObjectPattern))
        {
            DestructureObjectFunctionScoped(pattern, value, environment, context);
        }
        else
        {
            throw new InvalidOperationException($"Unknown pattern type: {patternType}{GetSourceInfo(context)}");
        }
    }

    // Helper for exceptions with source info
    private static string GetSourceInfo(EvaluationContext context)
    {
        if (context.SourceReference is null)
        {
            return " (no source reference)";
        }

        var src = context.SourceReference;
        var snippet = src.GetText();
        if (snippet.Length > 50)
        {
            snippet = snippet[..47] + "...";
        }

        return $" at {src} (snippet: '{snippet}') Source: '{src.Source}' Start: {src.StartPosition} End: {src.EndPosition}";
    }

    // Bitwise operations
    // Helper for bitwise operations that work on int32
    private static object PerformBigIntOrInt32Operation(
        object? left, 
        object? right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<int, int, int> int32Op)
    {
        // Handle BigInt op BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return bigIntOp(leftBigInt, rightBigInt);
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ToInt32(left);
        var rightInt = ToInt32(right);
        return (double)int32Op(leftInt, rightInt);
    }

    private static object BitwiseAnd(object? left, object? right)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l & r,
            (l, r) => l & r);
    }

    private static object BitwiseOr(object? left, object? right)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l | r,
            (l, r) => l | r);
    }

    private static object BitwiseXor(object? left, object? right)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l ^ r,
            (l, r) => l ^ r);
    }

    private static object BitwiseNot(object? operand)
    {
        // Handle ~BigInt
        if (operand is JsBigInt bigInt)
        {
            return ~bigInt;
        }

        var operandInt = ToInt32(operand);
        return (double)~operandInt;
    }

    private static object LeftShift(object? left, object? right)
    {
        // Handle BigInt << BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            // BigInt shift requires int, so check range
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw new InvalidOperationException("BigInt shift amount is too large");
            }

            return leftBigInt << (int)rightBigInt.Value;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ToInt32(left);
        var rightInt = ToInt32(right) & 0x1F; // Only use the bottom 5 bits
        return (double)(leftInt << rightInt);
    }

    private static object RightShift(object? left, object? right)
    {
        // Handle BigInt >> BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            // BigInt shift requires int, so check range
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw new InvalidOperationException("BigInt shift amount is too large");
            }

            return leftBigInt >> (int)rightBigInt.Value;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ToInt32(left);
        var rightInt = ToInt32(right) & 0x1F; // Only use the bottom 5 bits
        return (double)(leftInt >> rightInt);
    }

    private static object UnsignedRightShift(object? left, object? right)
    {
        // BigInt does not support >>> operator (unsigned right shift)
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("BigInts have no unsigned right shift, use >> instead");
        }

        var leftUInt = ToUInt32(left);
        var rightInt = ToInt32(right) & 0x1F; // Only use the bottom 5 bits
        return (double)(leftUInt >> rightInt);
    }

    private static int ToInt32(object? value)
    {
        var num = ToNumber(value);
        return JsNumericConversions.ToInt32(num);
    }

    private static uint ToUInt32(object? value)
    {
        var num = ToNumber(value);
        return JsNumericConversions.ToUInt32(num);
    }

    // Increment/Decrement operations
    private static object IncrementPrefix(object? operandExpression, JsEnvironment environment, EvaluationContext context)
    {
        // Get current value
        var currentValue = EvaluateExpression(operandExpression, environment, context);

        // Handle BigInt increment
        if (currentValue is JsBigInt bigInt)
        {
            var newValue = bigInt + JsBigInt.One;
            UpdateVariable(operandExpression, newValue, environment, context);
            return newValue;
        }

        var numValue = ToNumber(currentValue) + 1;
        UpdateVariable(operandExpression, numValue, environment, context);
        return numValue;
    }

    private static object DecrementPrefix(object? operandExpression, JsEnvironment environment, EvaluationContext context)
    {
        // Get current value
        var currentValue = EvaluateExpression(operandExpression, environment, context);

        // Handle BigInt decrement
        if (currentValue is JsBigInt bigInt)
        {
            var newValue = bigInt - JsBigInt.One;
            UpdateVariable(operandExpression, newValue, environment, context);
            return newValue;
        }

        var numValue = ToNumber(currentValue) - 1;
        UpdateVariable(operandExpression, numValue, environment, context);
        return numValue;
    }

    private static object IncrementPostfix(object? operandExpression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Get current value
        var currentValue = EvaluateExpression(operandExpression, environment, context);

        // Handle BigInt increment
        if (currentValue is JsBigInt bigInt)
        {
            var newValue = bigInt + JsBigInt.One;
            UpdateVariable(operandExpression, newValue, environment, context);
            return bigInt; // Return the old value
        }

        var oldValue = ToNumber(currentValue);
        var newValue2 = oldValue + 1;
        UpdateVariable(operandExpression, newValue2, environment, context);
        return oldValue; // Return the old value
    }

    private static object DecrementPostfix(object? operandExpression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Get current value
        var currentValue = EvaluateExpression(operandExpression, environment, context);

        // Handle BigInt decrement
        if (currentValue is JsBigInt bigInt)
        {
            var newValue = bigInt - JsBigInt.One;
            UpdateVariable(operandExpression, newValue, environment, context);
            return bigInt; // Return the old value
        }

        var oldValue = ToNumber(currentValue);
        var newValue2 = oldValue - 1;
        UpdateVariable(operandExpression, newValue2, environment, context);
        return oldValue; // Return the old value
    }

    private static void UpdateVariable(object? operandExpression, object? newValue, JsEnvironment environment,
        EvaluationContext context)
    {
        if (operandExpression is Symbol symbol)
        {
            environment.Assign(symbol, newValue);
        }
        else if (operandExpression is Cons { Head: Symbol head } cons)
        {
            if (ReferenceEquals(head, JsSymbols.GetProperty))
            {
                var target = EvaluateExpression(cons.Rest.Head, environment, context);
        var propertyName = cons.Rest.Rest.Head as string
                           ?? throw new InvalidOperationException($"Property access requires a string name.{GetSourceInfo(context)}");
                AssignPropertyValue(target, propertyName, newValue);
            }
            else if (ReferenceEquals(head, JsSymbols.GetIndex))
            {
                var target = EvaluateExpression(cons.Rest.Head, environment, context);
                var index = EvaluateExpression(cons.Rest.Rest.Head, environment, context);

                if (target is JsArray jsArray && TryConvertToIndex(index, out var arrayIndex))
                {
                    jsArray.SetElement(arrayIndex, newValue);
                }
                else if (target is TypedArrayBase typedArray && TryConvertToIndex(index, out var typedIndex))
                {
                    var numericValue = ToNumber(newValue);
                    typedArray.SetElement(typedIndex, numericValue);
                }
                else if (target is JsObject jsObject)
                {
                    var propertyName = ToPropertyName(index)
                                       ?? throw new InvalidOperationException($"Invalid property name: {index}{GetSourceInfo(context)}");
                    jsObject.SetProperty(propertyName, newValue);
                }
            }
        }
        else
        {
            throw new InvalidOperationException($"Invalid operand for increment/decrement operator.{GetSourceInfo(context)}");
        }
    }

    private static HostFunction CreateTypedArraySliceMethod(TypedArrayBase typedArray)
    {
        return new HostFunction(args =>
        {
            var begin = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
            var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : typedArray.Length;

            return typedArray switch
            {
                JsInt8Array arr => arr.Slice(begin, end),
                JsUint8Array arr => arr.Slice(begin, end),
                JsUint8ClampedArray arr => arr.Slice(begin, end),
                JsInt16Array arr => arr.Slice(begin, end),
                JsUint16Array arr => arr.Slice(begin, end),
                JsInt32Array arr => arr.Slice(begin, end),
                JsUint32Array arr => arr.Slice(begin, end),
                JsFloat32Array arr => arr.Slice(begin, end),
                JsFloat64Array arr => arr.Slice(begin, end),
                _ => throw new InvalidOperationException($"Unknown typed array type: {typedArray.GetType()}")
            };
        });
    }

    /// <summary>
    /// Gets the source reference from a cons cell or its origin chain.
    /// </summary>
    private static SourceReference? GetSourceReference(Cons? cons)
    {
        if (cons == null)
        {
            return null;
        }

        // Check the current cons for a source reference
        if (cons.SourceReference != null)
        {
            return cons.SourceReference;
        }

        // Walk the origin chain to find a source reference
        var current = cons.Origin;
        while (current != null)
        {
            if (current.SourceReference != null)
            {
                return current.SourceReference;
            }

            current = current.Origin;
        }

        return null;
    }

    /// <summary>
    /// Formats an error message with source reference information if available.
    /// </summary>
    private static string FormatErrorMessage(string message, Cons? cons)
    {
        var sourceRef = GetSourceReference(cons);
        if (sourceRef == null)
        {
            return message;
        }

        message += $" at {sourceRef}";
        var sourceText = sourceRef.GetText();
        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            message += $": {sourceText}";
        }

        return message;
    }

    /// <summary>
    /// Implements the 'in' operator, which checks if a property exists in an object.
    /// </summary>
    private static bool InOperator(object? left, object? right)
    {
        // Convert left operand to string (property name)
        var propertyName = left?.ToString() ?? "";

        // Right operand must be an object
        if (right is JsObject jsObj)
        {
            return jsObj.ContainsKey(propertyName);
        }

        // For non-objects, 'in' returns false
        return false;
    }

    /// <summary>
    /// Implements the instanceof operator. Checks if an object has a constructor's prototype in its prototype chain.
    /// </summary>
    private static bool InstanceofOperator(object? left, object? right)
    {
        // Left operand must be an object
        if (left is not JsObject leftObj)
        {
            return false;
        }

        // Right operand must be a constructor function
        if (right is not IJsCallable)
        {
            return false;
        }

        // Get the prototype property from the constructor
        object? constructorPrototype = null;
        if (right is JsFunction jsFunc)
        {
            TryGetPropertyValue(jsFunc, "prototype", out constructorPrototype);
        }
        else if (right is JsObject rightObj)
        {
            TryGetPropertyValue(rightObj, "prototype", out constructorPrototype);
        }

        if (constructorPrototype is not JsObject prototypeObj)
        {
            return false;
        }

        // Walk up the prototype chain of the left operand
        var current = leftObj.Prototype;
        while (current != null)
        {
            if (ReferenceEquals(current, prototypeObj))
            {
                return true;
            }
            current = current.Prototype;
        }

        return false;
    }
}
