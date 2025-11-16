using System.Globalization;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Lisp;
using static Asynkron.JsEngine.Evaluation.DestructuringEvaluator;
using static Asynkron.JsEngine.Evaluation.EvaluationGuards;
using static Asynkron.JsEngine.Evaluation.ExpressionEvaluator;
using static Asynkron.JsEngine.Evaluation.ProgramEvaluator;

namespace Asynkron.JsEngine.Evaluation;

internal static class StatementEvaluator
{
    internal static object? EvaluateStatement(object? statement, JsEnvironment environment, EvaluationContext context)
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
            // Check if there's a label: (break) or (break labelName)
            Symbol? label = null;
            if (!ReferenceEquals(cons.Rest, Cons.Empty) && cons.Rest is Cons restCons)
            {
                label = restCons.Head as Symbol;
            }
            context.SetBreak(label);
            return null;
        }

        if (ReferenceEquals(symbol, JsSymbols.Continue))
        {
            // Check if there's a label: (continue) or (continue labelName)
            Symbol? label = null;
            if (!ReferenceEquals(cons.Rest, Cons.Empty) && cons.Rest is Cons restCons)
            {
                label = restCons.Head as Symbol;
            }
            context.SetContinue(label);
            return null;
        }

        if (ReferenceEquals(symbol, JsSymbols.EmptyStatement))
        {
            // Empty statement does nothing, just return null
            return null;
        }

        if (ReferenceEquals(symbol, JsSymbols.Label))
        {
            return EvaluateLabel(cons, environment, context);
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
        if (!cons.TryAsIfStatement(out var conditionExpression, out var thenBranch, out var elseBranch))
        {
            throw new InvalidOperationException($"If expression is malformed.{GetSourceInfo(context)}");
        }

        var condition = EvaluateExpression(conditionExpression, environment, context);
        if (JsTruthyConversion.IsTruthy(condition))
        {
            return EvaluateStatement(thenBranch, environment, context);
        }

        if (elseBranch is not null)
        {
            return EvaluateStatement(elseBranch, environment, context);
        }

        return null;
    }

    private static object? EvaluateWhile(Cons cons, JsEnvironment environment, EvaluationContext context, Symbol? loopLabel = null)
    {
        if (!cons.TryAsWhileStatement(out var conditionExpression, out var body))
        {
            throw new InvalidOperationException($"While loop must have a condition and body.{GetSourceInfo(context)}");
        }

        object? lastResult = null;
        while (JsTruthyConversion.IsTruthy(EvaluateExpression(conditionExpression, environment, context)))
        {
            if (context.ShouldStopEvaluation)
            {
                break;
            }

            lastResult = EvaluateStatement(body, environment, context);

            // Demonstrate pattern matching with typed signals
            // Note: This shows the new signal-based approach
            if (context.CurrentSignal is ContinueSignal continueSignal)
            {
                // Clear if this loop owns the continue or if it's unlabeled (innermost loop)
                if (continueSignal.Label is null ||
                    (loopLabel is not null && ReferenceEquals(continueSignal.Label, loopLabel)))
                {
                    context.ClearContinue();
                    continue;
                }

                // Has a different label, let it propagate up to an outer loop
                break;
            }

            if (context.CurrentSignal is BreakSignal breakSignal)
            {
                // Clear if this loop owns the break or if it's unlabeled (innermost loop)
                if (breakSignal.Label is null ||
                    (loopLabel is not null && ReferenceEquals(breakSignal.Label, loopLabel)))
                {
                    context.ClearBreak();
                    break;
                }

                // Has a different label, let it propagate up
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

    private static object? EvaluateDoWhile(Cons cons, JsEnvironment environment, EvaluationContext context, Symbol? loopLabel = null)
    {
        if (!cons.TryAsDoWhileStatement(out var conditionExpression, out var body))
        {
            throw new InvalidOperationException($"Do/while loop must have a condition and body.{GetSourceInfo(context)}");
        }

        object? lastResult = null;
        while (true)
        {
            lastResult = EvaluateStatement(body, environment, context);

            if (context.CurrentSignal is ContinueSignal continueSignal)
            {
                // Clear if this loop owns the continue or if it's unlabeled (innermost loop)
                if (continueSignal.Label is null ||
                    (loopLabel is not null && ReferenceEquals(continueSignal.Label, loopLabel)))
                {
                    context.ClearContinue();
                    // fall through to condition check for the next iteration
                }
                else
                {
                    // Has a different label, let it propagate up
                    break;
                }
            }
            else if (context.CurrentSignal is BreakSignal breakSignal)
            {
                // Clear if this loop owns the break or if it's unlabeled (innermost loop)
                if (breakSignal.Label is null ||
                    (loopLabel is not null && ReferenceEquals(breakSignal.Label, loopLabel)))
                {
                    context.ClearBreak();
                    break;
                }

                // Has a different label, let it propagate up
                break;
            }
            else if (context.IsReturn || context.IsThrow)
            {
                break; // Propagate return/throw
            }

            if (!JsTruthyConversion.IsTruthy(EvaluateExpression(conditionExpression, environment, context)))
            {
                break;
            }
        }

        return lastResult;
    }

    private static object? EvaluateLabel(Cons cons, JsEnvironment environment, EvaluationContext context)
    {
        if (!cons.TryAsLabelStatement(out var labelName, out var statement))
        {
            throw new InvalidOperationException($"Label statement must include a symbol name and a statement.{GetSourceInfo(context)}");
        }

        // Push the label onto the context stack
        context.PushLabel(labelName);
        try
        {
            object? result;
            var handledAsLoop = false;

            if (statement is Cons statementCons && statementCons.Head is Symbol statementType)
            {
                if (ReferenceEquals(statementType, JsSymbols.For))
                {
                    handledAsLoop = true;
                    result = EvaluateFor(statementCons, environment, context, labelName);
                }
                else if (ReferenceEquals(statementType, JsSymbols.ForIn))
                {
                    handledAsLoop = true;
                    result = EvaluateForIn(statementCons, environment, context, labelName);
                }
                else if (ReferenceEquals(statementType, JsSymbols.ForOf))
                {
                    handledAsLoop = true;
                    result = EvaluateForOf(statementCons, environment, context, labelName);
                }
                else if (ReferenceEquals(statementType, JsSymbols.ForAwaitOf))
                {
                    handledAsLoop = true;
                    result = EvaluateForAwaitOf(statementCons, environment, context, labelName);
                }
                else if (ReferenceEquals(statementType, JsSymbols.While))
                {
                    handledAsLoop = true;
                    result = EvaluateWhile(statementCons, environment, context, labelName);
                }
                else if (ReferenceEquals(statementType, JsSymbols.DoWhile))
                {
                    handledAsLoop = true;
                    result = EvaluateDoWhile(statementCons, environment, context, labelName);
                }
                else
                {
                    result = EvaluateStatement(statement, environment, context);
                }
            }
            else
            {
                result = EvaluateStatement(statement, environment, context);
            }

            if (context.CurrentSignal is BreakSignal)
            {
                context.TryClearBreak(labelName);
            }

            if (handledAsLoop && context.CurrentSignal is ContinueSignal)
            {
                context.TryClearContinue(labelName);
            }

            return result;
        }
        finally
        {
            // Always pop the label when exiting
            context.PopLabel();
        }
    }

    private static object? EvaluateFor(Cons cons, JsEnvironment environment, EvaluationContext context, Symbol? loopLabel = null)
    {
        if (!cons.TryAsForStatement(out var initializer, out var conditionExpression, out var incrementExpression,
                out var body))
        {
            throw new InvalidOperationException($"For loop must have initializer, condition, increment, and body.{GetSourceInfo(context)}");
        }

        // Create environment for the loop, passing the for loop S-expression
        var loopJsEnvironment = new JsEnvironment(environment, creatingExpression: cons, description: "for loop");

        if (initializer is not null)
        {
            EvaluateStatement(initializer, loopJsEnvironment, context);
        }

        object? lastResult = null;
        while (conditionExpression is null || JsTruthyConversion.IsTruthy(EvaluateExpression(conditionExpression, loopJsEnvironment, context)))
        {
            if (context.ShouldStopEvaluation)
            {
                break;
            }

            lastResult = EvaluateStatement(body, loopJsEnvironment, context);

            if (context.CurrentSignal is ContinueSignal continueSignal)
            {
                // Clear if this loop owns the continue or if it's unlabeled (innermost loop)
                if (continueSignal.Label is null ||
                    (loopLabel is not null && ReferenceEquals(continueSignal.Label, loopLabel)))
                {
                    context.ClearContinue();
                    if (incrementExpression is not null)
                    {
                        EvaluateExpression(incrementExpression, loopJsEnvironment, context);
                    }
                    continue;
                }

                // Has a different label, let it propagate up
                break;
            }

            if (context.CurrentSignal is BreakSignal breakSignal)
            {
                // Clear if this loop owns the break or if it's unlabeled (innermost loop)
                if (breakSignal.Label is null ||
                    (loopLabel is not null && ReferenceEquals(breakSignal.Label, loopLabel)))
                {
                    context.ClearBreak();
                    break;
                }

                // Has a different label, let it propagate up
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

    private static object? EvaluateForIn(Cons cons, JsEnvironment environment, EvaluationContext context, Symbol? loopLabel = null)
    {
        // (for-in (let/var/const variable) iterable body) OR (for-in identifier iterable body)
        if (!cons.TryAsForInStatement(out var firstArg, out var iterableExpression, out var body))
        {
            throw new InvalidOperationException($"for...in loop must include a binding, iterable expression, and body.{GetSourceInfo(context)}");
        }

        var variableName = firstArg switch
        {
            // Check if first argument is a variable declaration or just an identifier
            Cons variableDecl => ExpectSymbol(variableDecl.Rest.Head, "Expected variable name in for...in loop.",
                context),
            Symbol identifier => identifier,
            _ => throw new InvalidOperationException(
                $"Expected variable declaration or identifier in for...in loop.{GetSourceInfo(context)}")
        };

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

        foreach (var key in keys.TakeWhile(key => !context.ShouldStopEvaluation))
        {
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

            if (context.CurrentSignal is ContinueSignal continueSignal)
            {
                // Clear if this loop owns the continue or if it's unlabeled (innermost loop)
                if (continueSignal.Label is null ||
                    (loopLabel is not null && ReferenceEquals(continueSignal.Label, loopLabel)))
                {
                    context.ClearContinue();
                    continue;
                }

                // Has a different label, let it propagate up
                break;
            }

            if (context.CurrentSignal is BreakSignal breakSignal)
            {
                // Clear if this loop owns the break or if it's unlabeled (innermost loop)
                if (breakSignal.Label is null ||
                    (loopLabel is not null && ReferenceEquals(breakSignal.Label, loopLabel)))
                {
                    context.ClearBreak();
                    break;
                }

                // Has a different label, let it propagate up
                break;
            }

            if (context.IsReturn || context.IsThrow)
            {
                break; // Propagate return/throw
            }
        }

        return lastResult;
    }

    private static object? EvaluateForOf(Cons cons, JsEnvironment environment, EvaluationContext context, Symbol? loopLabel = null)
    {
        // (for-of (let/var/const variable) iterable body) OR (for-of identifier iterable body)
        if (!cons.TryAsForOfStatement(out var firstArg, out var iterableExpression, out var body))
        {
            throw new InvalidOperationException($"for...of loop must include a binding, iterable expression, and body.{GetSourceInfo(context)}");
        }

        var variableName = firstArg switch
        {
            // Check if first argument is a variable declaration or just an identifier
            Cons variableDecl => ExpectSymbol(variableDecl.Rest.Head, "Expected variable name in for...of loop.",
                context),
            Symbol identifier => identifier,
            _ => throw new InvalidOperationException(
                $"Expected variable declaration or identifier in for...of loop.{GetSourceInfo(context)}")
        };

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

            if (context.CurrentSignal is ContinueSignal continueSignal)
            {
                // Clear if this loop owns the continue or if it's unlabeled (innermost loop)
                if (continueSignal.Label is null ||
                    (loopLabel is not null && ReferenceEquals(continueSignal.Label, loopLabel)))
                {
                    context.ClearContinue();
                    continue;
                }

                // Has a different label, let it propagate up
                break;
            }

            if (context.CurrentSignal is BreakSignal breakSignal)
            {
                // Clear if this loop owns the break or if it's unlabeled (innermost loop)
                if (breakSignal.Label is null ||
                    (loopLabel is not null && ReferenceEquals(breakSignal.Label, loopLabel)))
                {
                    context.ClearBreak();
                    break;
                }

                // Has a different label, let it propagate up
                break;
            }

            if (context.IsReturn || context.IsThrow)
            {
                break; // Propagate return/throw
            }
        }

        return lastResult;
    }

    private static object? EvaluateForAwaitOf(Cons cons, JsEnvironment environment, EvaluationContext context, Symbol? loopLabel = null)
    {
        // (for-await-of (let/var/const variable) iterable body) OR (for-await-of identifier iterable body)
        // This implements async iteration with support for:
        // 1. Symbol.asyncIterator protocol
        // 2. Fallback to Symbol.iterator
        // 3. Built-in iterables (arrays, strings, generators)
        if (!cons.TryAsForAwaitOfStatement(out var firstArg, out var iterableExpression, out var body))
        {
            throw new InvalidOperationException($"for await...of loop must include a binding, iterable expression, and body.{GetSourceInfo(context)}");
        }

        var variableName = firstArg switch
        {
            // Check if first argument is a variable declaration or just an identifier
            Cons variableDecl => ExpectSymbol(variableDecl.Rest.Head, "Expected variable name in for await...of loop.",
                context),
            Symbol identifier => identifier,
            _ => throw new InvalidOperationException(
                $"Expected variable declaration or identifier in for await...of loop.{GetSourceInfo(context)}")
        };

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
            var asyncIteratorSymbol = TypedAstSymbol.For("Symbol.asyncIterator");
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
                var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
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

                    if (context.CurrentSignal is ContinueSignal continueSignal)
                    {
                        // Clear if this loop owns the continue or if it's unlabeled (innermost loop)
                        if (continueSignal.Label is null ||
                            (loopLabel is not null && ReferenceEquals(continueSignal.Label, loopLabel)))
                        {
                            context.ClearContinue();
                            continue;
                        }

                        // Has a different label, let it propagate up
                        break;
                    }

                    if (context.CurrentSignal is BreakSignal breakSignal)
                    {
                        // Clear if this loop owns the break or if it's unlabeled (innermost loop)
                        if (breakSignal.Label is null ||
                            (loopLabel is not null && ReferenceEquals(breakSignal.Label, loopLabel)))
                        {
                            context.ClearBreak();
                            break;
                        }

                        // Has a different label, let it propagate up
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

            if (context.CurrentSignal is ContinueSignal continueSignal)
            {
                // Clear if this loop owns the continue or if it's unlabeled (innermost loop)
                if (continueSignal.Label is null ||
                    (loopLabel is not null && ReferenceEquals(continueSignal.Label, loopLabel)))
                {
                    context.ClearContinue();
                    continue;
                }

                // Has a different label, let it propagate up
                break;
            }

            if (context.CurrentSignal is BreakSignal breakSignal)
            {
                // Clear if this loop owns the break or if it's unlabeled (innermost loop)
                if (breakSignal.Label is null ||
                    (loopLabel is not null && ReferenceEquals(breakSignal.Label, loopLabel)))
                {
                    context.ClearBreak();
                    break;
                }

                // Has a different label, let it propagate up
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
                    if (context.CurrentSignal is BreakSignal breakSignal)
                    {
                        // Only clear if the break has no label
                        if (breakSignal.Label is null)
                        {
                            context.ClearBreak();
                            return result;
                        }
                        // Has a label, let it propagate up
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
                if (context.CurrentSignal is BreakSignal breakSignal)
                {
                    // Only clear if the break has no label
                    if (breakSignal.Label is null)
                    {
                        context.ClearBreak();
                        return result;
                    }
                    // Has a label, let it propagate up
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
                if (fieldExpression is not Cons fieldCons)
                {
                    continue;
                }

                if (fieldCons.Head is not Symbol fieldTag)
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


}
