using System.Globalization;

namespace Asynkron.JsEngine;

internal static class Evaluator
{
    public static object? EvaluateProgram(Cons program, Environment environment)
    {
        if (program.IsEmpty || program.Head is not Symbol { } tag || !ReferenceEquals(tag, JsSymbols.Program))
        {
            throw new InvalidOperationException("Program S-expression must start with the 'program' symbol.");
        }

        object? result = null;
        foreach (var statement in program.Rest)
        {
            result = EvaluateStatement(statement, environment);
        }

        return result;
    }

    public static object? EvaluateBlock(Cons block, Environment environment)
    {
        if (block.IsEmpty || block.Head is not Symbol { } tag || !ReferenceEquals(tag, JsSymbols.Block))
        {
            throw new InvalidOperationException("Block S-expression must start with the 'block' symbol.");
        }

        var scope = new Environment(environment);
        object? result = null;
        foreach (var statement in block.Rest)
        {
            result = EvaluateStatement(statement, scope);
        }

        return result;
    }

    private static object? EvaluateStatement(object? statement, Environment environment)
    {
        if (statement is not Cons cons)
        {
            return statement;
        }

        if (cons.Head is not Symbol symbol)
        {
            throw new InvalidOperationException("Statement must start with a symbol.");
        }

        if (ReferenceEquals(symbol, JsSymbols.Let))
        {
            return EvaluateLet(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Var))
        {
            return EvaluateVar(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Const))
        {
            return EvaluateConst(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Function))
        {
            return EvaluateFunctionDeclaration(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Class))
        {
            return EvaluateClass(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.If))
        {
            return EvaluateIf(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.For))
        {
            return EvaluateFor(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Switch))
        {
            return EvaluateSwitch(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Try))
        {
            return EvaluateTry(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.While))
        {
            return EvaluateWhile(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.DoWhile))
        {
            return EvaluateDoWhile(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Break))
        {
            throw new BreakSignal();
        }

        if (ReferenceEquals(symbol, JsSymbols.Continue))
        {
            throw new ContinueSignal();
        }

        if (ReferenceEquals(symbol, JsSymbols.Return))
        {
            return EvaluateReturn(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Throw))
        {
            return EvaluateThrow(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.ExpressionStatement))
        {
            var expression = cons.Rest.Head;
            return EvaluateExpression(expression, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Block))
        {
            return EvaluateBlock(cons, environment);
        }

        return EvaluateExpression(cons, environment);
    }

    private static object? EvaluateIf(Cons cons, Environment environment)
    {
        var conditionExpression = cons.Rest.Head;
        var thenBranch = cons.Rest.Rest.Head;
        var elseBranch = cons.Rest.Rest.Rest.Head;

        var condition = EvaluateExpression(conditionExpression, environment);
        if (IsTruthy(condition))
        {
            return EvaluateStatement(thenBranch, environment);
        }

        if (elseBranch is not null)
        {
            return EvaluateStatement(elseBranch, environment);
        }

        return null;
    }

    private static object? EvaluateWhile(Cons cons, Environment environment)
    {
        var conditionExpression = cons.Rest.Head;
        var body = cons.Rest.Rest.Head;

        object? lastResult = null;
        while (IsTruthy(EvaluateExpression(conditionExpression, environment)))
        {
            try
            {
                lastResult = EvaluateStatement(body, environment);
            }
            catch (ContinueSignal)
            {
                continue;
            }
            catch (BreakSignal)
            {
                break;
            }
        }

        return lastResult;
    }

    private static object? EvaluateDoWhile(Cons cons, Environment environment)
    {
        var conditionExpression = cons.Rest.Head;
        var body = cons.Rest.Rest.Head;

        object? lastResult = null;
        while (true)
        {
            try
            {
                lastResult = EvaluateStatement(body, environment);
            }
            catch (ContinueSignal)
            {
                // fall through to condition check for the next iteration
            }
            catch (BreakSignal)
            {
                break;
            }

            if (!IsTruthy(EvaluateExpression(conditionExpression, environment)))
            {
                break;
            }
        }

        return lastResult;
    }

    private static object? EvaluateFor(Cons cons, Environment environment)
    {
        var initializer = cons.Rest.Head;
        var conditionExpression = cons.Rest.Rest.Head;
        var incrementExpression = cons.Rest.Rest.Rest.Head;
        var body = cons.Rest.Rest.Rest.Rest.Head;

        var loopEnvironment = new Environment(environment);

        if (initializer is not null)
        {
            EvaluateStatement(initializer, loopEnvironment);
        }

        object? lastResult = null;
        while (conditionExpression is null || IsTruthy(EvaluateExpression(conditionExpression, loopEnvironment)))
        {
            try
            {
                lastResult = EvaluateStatement(body, loopEnvironment);
            }
            catch (ContinueSignal)
            {
                if (incrementExpression is not null)
                {
                    EvaluateExpression(incrementExpression, loopEnvironment);
                }

                continue;
            }
            catch (BreakSignal)
            {
                break;
            }

            if (incrementExpression is not null)
            {
                EvaluateExpression(incrementExpression, loopEnvironment);
            }
        }

        return lastResult;
    }

    private static object? EvaluateSwitch(Cons cons, Environment environment)
    {
        var discriminantExpression = cons.Rest.Head;
        var clauses = ExpectCons(cons.Rest.Rest.Head, "Expected switch clause list.");
        var discriminant = EvaluateExpression(discriminantExpression, environment);
        var hasMatched = false; // Once a clause matches, we keep executing subsequent clauses to model fallthrough.
        object? result = null;

        foreach (var clauseEntry in clauses)
        {
            var clause = ExpectCons(clauseEntry, "Expected switch clause.");
            var tag = ExpectSymbol(clause.Head, "Expected switch clause tag.");

            if (ReferenceEquals(tag, JsSymbols.Case))
            {
                var testExpression = clause.Rest.Head;
                var body = ExpectCons(clause.Rest.Rest.Head, "Expected case body block.");

                if (!hasMatched)
                {
                    var testValue = EvaluateExpression(testExpression, environment);
                    hasMatched = Equals(discriminant, testValue);
                }

                if (hasMatched)
                {
                    try
                    {
                        result = ExecuteSwitchBody(body, environment, result);
                    }
                    catch (BreakSignal)
                    {
                        return result;
                    }
                }

                continue;
            }

            if (ReferenceEquals(tag, JsSymbols.Default))
            {
                var body = ExpectCons(clause.Rest.Head, "Expected default body block.");

                if (!hasMatched)
                {
                    hasMatched = true;
                }

                try
                {
                    result = ExecuteSwitchBody(body, environment, result);
                }
                catch (BreakSignal)
                {
                    return result;
                }

                continue;
            }

            throw new InvalidOperationException("Unknown switch clause.");
        }

        return result;
    }

    private static object? ExecuteSwitchBody(Cons body, Environment environment, object? currentResult)
    {
        var result = currentResult;
        foreach (var statement in body.Rest)
        {
            result = EvaluateStatement(statement, environment);
        }

        return result;
    }

    private static object? EvaluateTry(Cons cons, Environment environment)
    {
        var tryStatement = cons.Rest.Head;
        var catchClause = cons.Rest.Rest.Head;
        var finallyClause = cons.Rest.Rest.Rest.Head;

        ThrowSignal? pendingThrow = null;
        object? result = null;

        try
        {
            result = EvaluateStatement(tryStatement, environment);
        }
        catch (ThrowSignal signal)
        {
            if (catchClause is Cons catchCons && ReferenceEquals(catchCons.Head, JsSymbols.Catch))
            {
                result = ExecuteCatchClause(catchCons, signal.Value, environment);
            }
            else
            {
                pendingThrow = signal;
            }
        }
        finally
        {
            if (finallyClause is Cons finallyCons)
            {
                EvaluateStatement(finallyCons, environment);
            }
        }

        if (pendingThrow is not null)
        {
            throw pendingThrow;
        }

        return result;
    }

    private static object? EvaluateLet(Cons cons, Environment environment)
    {
        var name = ExpectSymbol(cons.Rest.Head, "Expected identifier in let declaration.");
        var valueExpression = cons.Rest.Rest.Head;
        var value = EvaluateExpression(valueExpression, environment);
        environment.Define(name, value);
        return value;
    }

    private static object? EvaluateVar(Cons cons, Environment environment)
    {
        var name = ExpectSymbol(cons.Rest.Head, "Expected identifier in var declaration.");
        var initializer = cons.Rest.Rest.Head;
        var hasInitializer = !ReferenceEquals(initializer, JsSymbols.Uninitialized);
        var value = hasInitializer ? EvaluateExpression(initializer, environment) : null;
        environment.DefineFunctionScoped(name, value, hasInitializer);
        return environment.Get(name);
    }

    private static object? EvaluateConst(Cons cons, Environment environment)
    {
        var name = ExpectSymbol(cons.Rest.Head, "Expected identifier in const declaration.");
        var valueExpression = cons.Rest.Rest.Head;
        var value = EvaluateExpression(valueExpression, environment);
        environment.Define(name, value, isConst: true);
        return value;
    }

    private static object? EvaluateFunctionDeclaration(Cons cons, Environment environment)
    {
        var name = ExpectSymbol(cons.Rest.Head, "Expected function name.");
        var parameters = ExpectCons(cons.Rest.Rest.Head, "Expected parameter list for function.");
        var body = ExpectCons(cons.Rest.Rest.Rest.Head, "Expected function body block.");
        var function = new JsFunction(name, ToSymbolList(parameters), body, environment);
        environment.Define(name, function);
        return function;
    }

    private static object? EvaluateClass(Cons cons, Environment environment)
    {
        var name = ExpectSymbol(cons.Rest.Head, "Expected class name symbol.");
        var extendsEntry = cons.Rest.Rest.Head;
        var constructorExpression = cons.Rest.Rest.Rest.Head;
        var methodsList = ExpectCons(cons.Rest.Rest.Rest.Rest.Head, "Expected class body list.");

        var (superConstructor, superPrototype) = ResolveSuperclass(extendsEntry, environment);

        var constructorValue = EvaluateExpression(constructorExpression, environment);
        if (constructorValue is not JsFunction constructor)
        {
            throw new InvalidOperationException("Class constructor must be a function.");
        }

        environment.Define(name, constructor);

        if (!constructor.TryGetProperty("prototype", out var prototypeValue) || prototypeValue is not JsObject prototype)
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
            var methodCons = ExpectCons(methodExpression, "Expected method definition.");
            var tag = ExpectSymbol(methodCons.Head, "Expected method tag.");
            if (!ReferenceEquals(tag, JsSymbols.Method))
            {
                throw new InvalidOperationException("Invalid entry in class body.");
            }

            var methodName = methodCons.Rest.Head as string
                ?? throw new InvalidOperationException("Expected method name.");
            var functionExpression = methodCons.Rest.Rest.Head;
            var methodValue = EvaluateExpression(functionExpression, environment);

            if (methodValue is not IJsCallable)
            {
                throw new InvalidOperationException($"Class method '{methodName}' must be callable.");
            }

            if (methodValue is JsFunction methodFunction)
            {
                methodFunction.SetSuperBinding(superConstructor, superPrototype);
            }

            prototype.SetProperty(methodName, methodValue);
        }

        return constructor;
    }

    private static (JsFunction? Constructor, JsObject? Prototype) ResolveSuperclass(object? extendsEntry, Environment environment)
    {
        if (extendsEntry is null)
        {
            return (null, null);
        }

        var extendsCons = ExpectCons(extendsEntry, "Expected extends clause structure.");
        var tag = ExpectSymbol(extendsCons.Head, "Expected extends tag.");
        if (!ReferenceEquals(tag, JsSymbols.Extends))
        {
            throw new InvalidOperationException("Malformed extends clause.");
        }

        var baseExpression = extendsCons.Rest.Head;
        var baseValue = EvaluateExpression(baseExpression, environment);

        if (baseValue is null)
        {
            return (null, null);
        }

        if (baseValue is not JsFunction baseConstructor)
        {
            throw new InvalidOperationException("Classes can only extend other constructors (or null).");
        }

        if (!baseConstructor.TryGetProperty("prototype", out var prototypeValue) || prototypeValue is not JsObject basePrototype)
        {
            basePrototype = new JsObject();
            baseConstructor.SetProperty("prototype", basePrototype);
        }

        return (baseConstructor, basePrototype);
    }

    private static object? EvaluateReturn(Cons cons, Environment environment)
    {
        if (cons.Rest.IsEmpty)
        {
            throw new ReturnSignal(null);
        }

        var value = EvaluateExpression(cons.Rest.Head, environment);
        throw new ReturnSignal(value);
    }

    private static object? EvaluateThrow(Cons cons, Environment environment)
    {
        var valueExpression = cons.Rest.Head;
        var value = EvaluateExpression(valueExpression, environment);
        throw new ThrowSignal(value);
    }

    private static object? ExecuteCatchClause(Cons catchClause, object? thrownValue, Environment environment)
    {
        var tag = ExpectSymbol(catchClause.Head, "Expected catch clause tag.");
        if (!ReferenceEquals(tag, JsSymbols.Catch))
        {
            throw new InvalidOperationException("Malformed catch clause.");
        }

        var binding = ExpectSymbol(catchClause.Rest.Head, "Expected catch binding symbol.");
        var body = ExpectCons(catchClause.Rest.Rest.Head, "Expected catch block.");

        var catchEnvironment = new Environment(environment);
        catchEnvironment.Define(binding, thrownValue);
        return EvaluateStatement(body, catchEnvironment);
    }

    private static object? EvaluateExpression(object? expression, Environment environment)
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
                return environment.Get(symbol);
            case Cons cons:
                return EvaluateCompositeExpression(cons, environment);
            default:
                return expression;
        }
    }

    private static object? EvaluateCompositeExpression(Cons cons, Environment environment)
    {
        if (cons.Head is not Symbol symbol)
        {
            throw new InvalidOperationException("Composite expression must begin with a symbol.");
        }

        if (ReferenceEquals(symbol, JsSymbols.Assign))
        {
            var target = ExpectSymbol(cons.Rest.Head, "Expected assignment target.");
            var valueExpression = cons.Rest.Rest.Head;
            var value = EvaluateExpression(valueExpression, environment);
            environment.Assign(target, value);
            return value;
        }

        if (ReferenceEquals(symbol, JsSymbols.Call))
        {
            return EvaluateCall(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.ArrayLiteral))
        {
            return EvaluateArrayLiteral(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.ObjectLiteral))
        {
            return EvaluateObjectLiteral(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.GetIndex))
        {
            return EvaluateGetIndex(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.SetIndex))
        {
            return EvaluateSetIndex(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.GetProperty))
        {
            return EvaluateGetProperty(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.SetProperty))
        {
            return EvaluateSetProperty(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.New))
        {
            return EvaluateNew(cons, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Negate))
        {
            var operand = EvaluateExpression(cons.Rest.Head, environment);
            return -ToNumber(operand);
        }

        if (ReferenceEquals(symbol, JsSymbols.Not))
        {
            var operand = EvaluateExpression(cons.Rest.Head, environment);
            return !IsTruthy(operand);
        }

        if (ReferenceEquals(symbol, JsSymbols.Lambda))
        {
            var maybeName = cons.Rest.Head as Symbol;
            var parameters = ExpectCons(cons.Rest.Rest.Head, "Expected lambda parameters list.");
            var body = ExpectCons(cons.Rest.Rest.Rest.Head, "Expected lambda body block.");
            return new JsFunction(maybeName, ToSymbolList(parameters), body, environment);
        }

        if (ReferenceEquals(symbol, JsSymbols.Ternary))
        {
            var condition = EvaluateExpression(cons.Rest.Head, environment);
            var thenBranch = cons.Rest.Rest.Head;
            var elseBranch = cons.Rest.Rest.Rest.Head;
            return IsTruthy(condition)
                ? EvaluateExpression(thenBranch, environment)
                : EvaluateExpression(elseBranch, environment);
        }

        return EvaluateBinary(cons, environment, symbol);
    }

    private static object? EvaluateCall(Cons cons, Environment environment)
    {
        var calleeExpression = cons.Rest.Head;
        var (callee, thisValue) = ResolveCallee(calleeExpression, environment);
        if (callee is not IJsCallable callable)
        {
            throw new InvalidOperationException("Attempted to call a non-callable value.");
        }

        var arguments = new List<object?>();
        foreach (var argumentExpression in cons.Rest.Rest)
        {
            arguments.Add(EvaluateExpression(argumentExpression, environment));
        }

        return callable.Invoke(arguments, thisValue);
    }

    private static (object? Callee, object? ThisValue) ResolveCallee(object? calleeExpression, Environment environment)
    {
        if (calleeExpression is Symbol { } superSymbol && ReferenceEquals(superSymbol, JsSymbols.Super))
        {
            var binding = ExpectSuperBinding(environment);
            if (binding.Constructor is null)
            {
                throw new InvalidOperationException("Super constructor is not available in this context.");
            }

            return (binding.Constructor, binding.ThisValue);
        }

        if (calleeExpression is Cons { Head: Symbol { } head } propertyCons && ReferenceEquals(head, JsSymbols.GetProperty))
        {
            var targetExpression = propertyCons.Rest.Head;
            var propertyName = propertyCons.Rest.Rest.Head as string
                ?? throw new InvalidOperationException("Property access requires a string name.");

            if (targetExpression is Symbol { } targetSymbol && ReferenceEquals(targetSymbol, JsSymbols.Super))
            {
                var binding = ExpectSuperBinding(environment);
                if (binding.TryGetProperty(propertyName, out var superValue))
                {
                    return (superValue, binding.ThisValue);
                }

                return (null, binding.ThisValue);
            }

            var target = EvaluateExpression(targetExpression, environment);
            if (TryGetPropertyValue(target, propertyName, out var value))
            {
                return (value, target);
            }

            return (null, target);
        }

        if (calleeExpression is Cons { Head: Symbol { } indexHead } indexCons && ReferenceEquals(indexHead, JsSymbols.GetIndex))
        {
            var targetExpression = indexCons.Rest.Head;
            var indexExpression = indexCons.Rest.Rest.Head;

            if (targetExpression is Symbol { } indexTargetSymbol && ReferenceEquals(indexTargetSymbol, JsSymbols.Super))
            {
                var binding = ExpectSuperBinding(environment);
                var superIndex = EvaluateExpression(indexExpression, environment);
                var superPropertyName = ToPropertyName(superIndex)
                    ?? throw new InvalidOperationException($"Unsupported index value '{superIndex}'.");

                if (binding.TryGetProperty(superPropertyName, out var superValue))
                {
                    return (superValue, binding.ThisValue);
                }

                return (null, binding.ThisValue);
            }

            var target = EvaluateExpression(targetExpression, environment);
            var index = EvaluateExpression(indexExpression, environment);

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

        return (EvaluateExpression(calleeExpression, environment), null);
    }

    private static object EvaluateArrayLiteral(Cons cons, Environment environment)
    {
        var array = new JsArray();
        foreach (var elementExpression in cons.Rest)
        {
            array.Push(EvaluateExpression(elementExpression, environment));
        }

        return array;
    }

    private static object EvaluateObjectLiteral(Cons cons, Environment environment)
    {
        var result = new JsObject();
        foreach (var propertyExpression in cons.Rest)
        {
            var propertyCons = ExpectCons(propertyExpression, "Expected property description in object literal.");
            if (propertyCons.Head is not Symbol { } propertyTag || !ReferenceEquals(propertyTag, JsSymbols.Property))
            {
                throw new InvalidOperationException("Object literal entries must begin with the 'prop' symbol.");
            }

            var propertyName = propertyCons.Rest.Head as string
                ?? throw new InvalidOperationException("Object literal property name must be a string.");

            var valueExpression = propertyCons.Rest.Rest.Head;
            var value = EvaluateExpression(valueExpression, environment);
            result.SetProperty(propertyName, value);
        }

        return result;
    }

    private static object? EvaluateGetProperty(Cons cons, Environment environment)
    {
        var targetExpression = cons.Rest.Head;
        var propertyName = cons.Rest.Rest.Head as string
            ?? throw new InvalidOperationException("Property access requires a string name.");

        if (targetExpression is Symbol { } superSymbol && ReferenceEquals(superSymbol, JsSymbols.Super))
        {
            var binding = ExpectSuperBinding(environment);
            if (binding.TryGetProperty(propertyName, out var superValue))
            {
                return superValue;
            }

            throw new InvalidOperationException($"Cannot read property '{propertyName}' from super prototype.");
        }

        var target = EvaluateExpression(targetExpression, environment);
        if (TryGetPropertyValue(target, propertyName, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Cannot read property '{propertyName}' from value '{target}'.");
    }

    private static object? EvaluateSetProperty(Cons cons, Environment environment)
    {
        var targetExpression = cons.Rest.Head;
        var propertyName = cons.Rest.Rest.Head as string
            ?? throw new InvalidOperationException("Property assignment requires a string name.");

        if (targetExpression is Symbol { } superSymbol && ReferenceEquals(superSymbol, JsSymbols.Super))
        {
            throw new InvalidOperationException("Assigning through super is not supported in this interpreter.");
        }

        var valueExpression = cons.Rest.Rest.Rest.Head;
        var target = EvaluateExpression(targetExpression, environment);
        var value = EvaluateExpression(valueExpression, environment);
        AssignPropertyValue(target, propertyName, value);
        return value;
    }

    private static object? EvaluateGetIndex(Cons cons, Environment environment)
    {
        var targetExpression = cons.Rest.Head;
        var indexExpression = cons.Rest.Rest.Head;

        if (targetExpression is Symbol { } superSymbol && ReferenceEquals(superSymbol, JsSymbols.Super))
        {
            var binding = ExpectSuperBinding(environment);
            var superIndexValue = EvaluateExpression(indexExpression, environment);
            var superPropertyName = ToPropertyName(superIndexValue)
                ?? throw new InvalidOperationException($"Unsupported index value '{superIndexValue}'.");

            if (binding.TryGetProperty(superPropertyName, out var superPropertyValue))
            {
                return superPropertyValue;
            }

            throw new InvalidOperationException($"Cannot read property '{superPropertyName}' from super prototype.");
        }

        var target = EvaluateExpression(targetExpression, environment);
        var indexValue = EvaluateExpression(indexExpression, environment);

        if (target is JsArray jsArray && TryConvertToIndex(indexValue, out var arrayIndex))
        {
            return jsArray.GetElement(arrayIndex);
        }

        var propertyName = ToPropertyName(indexValue)
            ?? throw new InvalidOperationException($"Unsupported index value '{indexValue}'.");

        if (TryGetPropertyValue(target, propertyName, out var propertyValue))
        {
            return propertyValue;
        }

        throw new InvalidOperationException($"Cannot read property '{propertyName}' from value '{target}'.");
    }

    private static object? EvaluateSetIndex(Cons cons, Environment environment)
    {
        var targetExpression = cons.Rest.Head;
        var indexExpression = cons.Rest.Rest.Head;
        var valueExpression = cons.Rest.Rest.Rest.Head;

        if (targetExpression is Symbol { } superSymbol && ReferenceEquals(superSymbol, JsSymbols.Super))
        {
            throw new InvalidOperationException("Assigning through super is not supported in this interpreter.");
        }

        var target = EvaluateExpression(targetExpression, environment);
        var indexValue = EvaluateExpression(indexExpression, environment);
        var value = EvaluateExpression(valueExpression, environment);

        if (target is JsArray jsArray && TryConvertToIndex(indexValue, out var arrayIndex))
        {
            jsArray.SetElement(arrayIndex, value);
            return value;
        }

        var propertyName = ToPropertyName(indexValue)
            ?? throw new InvalidOperationException($"Unsupported index value '{indexValue}'.");

        AssignPropertyValue(target, propertyName, value);
        return value;
    }

    private static object? EvaluateNew(Cons cons, Environment environment)
    {
        var constructorExpression = cons.Rest.Head;
        var constructor = EvaluateExpression(constructorExpression, environment);
        if (constructor is not IJsCallable callable)
        {
            throw new InvalidOperationException("Attempted to construct with a non-callable value.");
        }

        var instance = new JsObject();
        if (TryGetPropertyValue(constructor, "prototype", out var prototype) && prototype is JsObject prototypeObject)
        {
            instance.SetPrototype(prototypeObject);
        }

        var arguments = new List<object?>();
        foreach (var argumentExpression in cons.Rest.Rest)
        {
            arguments.Add(EvaluateExpression(argumentExpression, environment));
        }

        var result = callable.Invoke(arguments, instance);
        return result switch
        {
            JsObject jsObject => jsObject,
            IDictionary<string, object?> dictionary => dictionary,
            _ => instance
        };
    }

    private static object? EvaluateBinary(Cons cons, Environment environment, Symbol operatorSymbol)
    {
        var leftExpression = cons.Rest.Head;
        var rightExpression = cons.Rest.Rest.Head;
        var left = EvaluateExpression(leftExpression, environment);
        var operatorName = operatorSymbol.Name;

        switch (operatorName)
        {
            case "&&":
                return IsTruthy(left) ? EvaluateExpression(rightExpression, environment) : left;
            case "||":
                return IsTruthy(left) ? left : EvaluateExpression(rightExpression, environment);
            case "??":
                return left is null ? EvaluateExpression(rightExpression, environment) : left;
            case "===":
            {
                var rightStrict = EvaluateExpression(rightExpression, environment);
                return StrictEquals(left, rightStrict);
            }
            case "!==":
            {
                var rightStrict = EvaluateExpression(rightExpression, environment);
                return !StrictEquals(left, rightStrict);
            }
        }

        var right = EvaluateExpression(rightExpression, environment);

        return operatorName switch
        {
            "+" => Add(left, right),
            "-" => ToNumber(left) - ToNumber(right),
            "*" => ToNumber(left) * ToNumber(right),
            "/" => ToNumber(right) == 0 ? throw new DivideByZeroException() : ToNumber(left) / ToNumber(right),
            "==" => Equals(left, right),
            "!=" => !Equals(left, right),
            ">" => ToNumber(left) > ToNumber(right),
            ">=" => ToNumber(left) >= ToNumber(right),
            "<" => ToNumber(left) < ToNumber(right),
            "<=" => ToNumber(left) <= ToNumber(right),
            _ => throw new InvalidOperationException($"Unsupported operator '{operatorName}'.")
        };
    }

    private static IReadOnlyList<Symbol> ToSymbolList(Cons list)
    {
        var result = new List<Symbol>();
        foreach (var item in list)
        {
            result.Add(ExpectSymbol(item, "Expected symbol in parameter list."));
        }

        return result;
    }

    private static Symbol ExpectSymbol(object? value, string message)
        => value is Symbol symbol ? symbol : throw new InvalidOperationException(message);

    private static Cons ExpectCons(object? value, string message)
        => value is Cons cons ? cons : throw new InvalidOperationException(message);

    private static SuperBinding ExpectSuperBinding(Environment environment)
    {
        object? value;
        try
        {
            value = environment.Get(JsSymbols.Super);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException("Super is not available in this context.", ex);
        }

        if (value is not SuperBinding binding)
        {
            throw new InvalidOperationException("Super is not available in this context.");
        }

        return binding;
    }

    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        double d => Math.Abs(d) > double.Epsilon,
        string s => s.Length > 0,
        _ => true
    };

    private static double ToNumber(object? value) => value switch
    {
        null => 0,
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
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => throw new InvalidOperationException($"Cannot convert value '{value}' to a number.")
    };

    private static object Add(object? left, object? right)
    {
        if (left is string || right is string)
        {
            return ToDisplayString(left) + ToDisplayString(right);
        }

        return ToNumber(left) + ToNumber(right);
    }

    private static bool StrictEquals(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            if (left is double d && double.IsNaN(d))
            {
                return false; // mirror JavaScript's NaN behaviour
            }

            return true;
        }

        if (left is null || right is null)
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

        if (left.GetType() != right.GetType())
        {
            return false;
        }

        return Equals(left, right);
    }

    private static bool IsNumeric(object? value) => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static string ToDisplayString(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static bool TryGetPropertyValue(object? target, string propertyName, out object? value)
    {
        switch (target)
        {
            case JsArray jsArray when jsArray.TryGetProperty(propertyName, out value):
                return true;
            case JsObject jsObject when jsObject.TryGetProperty(propertyName, out value):
                return true;
            case JsFunction function when function.TryGetProperty(propertyName, out value):
                return true;
            case HostFunction hostFunction when hostFunction.TryGetProperty(propertyName, out value):
                return true;
            case IDictionary<string, object?> dictionary when dictionary.TryGetValue(propertyName, out value):
                return true;
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
                jsObject.SetProperty(propertyName, value);
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
            case int i when i >= 0:
                index = i;
                return true;
            case long l when l >= 0 && l <= int.MaxValue:
                index = (int)l;
                return true;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                var truncated = Math.Truncate(d);
                if (Math.Abs(d - truncated) < double.Epsilon && truncated >= 0 && truncated <= int.MaxValue)
                {
                    index = (int)truncated;
                    return true;
                }

                break;
            case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0:
                index = parsed;
                return true;
        }

        index = 0;
        return false;
    }

    private static string? ToPropertyName(object? value) => value switch
    {
        null => "null",
        string s => s,
        Symbol symbol => symbol.Name,
        bool b => b ? "true" : "false",
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d when !double.IsNaN(d) && !double.IsInfinity(d) => d.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };
}
