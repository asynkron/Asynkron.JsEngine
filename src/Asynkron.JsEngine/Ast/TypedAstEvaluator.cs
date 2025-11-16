using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Evaluation;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Proof-of-concept evaluator that executes the new typed AST directly instead of walking cons cells.
/// The goal is to showcase the recommended shape: a dedicated evaluator with explicit pattern matching
/// rather than virtual methods on the node hierarchy. Only a focused subset of JavaScript semantics is
/// implemented for now so the skeleton stays approachable.
/// </summary>
public static class TypedAstEvaluator
{
    public static object? EvaluateProgram(ProgramNode program, JsEnvironment environment)
    {
        var context = new EvaluationContext { SourceReference = program.Source };
        var executionEnvironment = program.IsStrict ? new JsEnvironment(environment, true, true) : environment;

        object? result = JsSymbols.Undefined;
        foreach (var statement in program.Body)
        {
            result = EvaluateStatement(statement, executionEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return result;
    }

    private static object? EvaluateStatement(StatementNode statement, JsEnvironment environment, EvaluationContext context,
        Symbol? activeLabel = null)
    {
        context.SourceReference = statement.Source;

        return statement switch
        {
            BlockStatement block => EvaluateBlock(block, environment, context),
            ExpressionStatement expressionStatement => EvaluateExpression(expressionStatement.Expression, environment, context),
            ReturnStatement returnStatement => EvaluateReturn(returnStatement, environment, context),
            ThrowStatement throwStatement => EvaluateThrow(throwStatement, environment, context),
            VariableDeclaration declaration => EvaluateVariableDeclaration(declaration, environment, context),
            FunctionDeclaration functionDeclaration => EvaluateFunctionDeclaration(functionDeclaration, environment),
            IfStatement ifStatement => EvaluateIf(ifStatement, environment, context),
            WhileStatement whileStatement => EvaluateWhile(whileStatement, environment, context, activeLabel),
            DoWhileStatement doWhileStatement => EvaluateDoWhile(doWhileStatement, environment, context, activeLabel),
            ForStatement forStatement => EvaluateFor(forStatement, environment, context, activeLabel),
            ForEachStatement forEachStatement => EvaluateForEach(forEachStatement, environment, context, activeLabel),
            BreakStatement breakStatement => EvaluateBreak(breakStatement, context),
            ContinueStatement continueStatement => EvaluateContinue(continueStatement, context),
            LabeledStatement labeledStatement => EvaluateLabeled(labeledStatement, environment, context),
            TryStatement tryStatement => EvaluateTry(tryStatement, environment, context),
            SwitchStatement switchStatement => EvaluateSwitch(switchStatement, environment, context, activeLabel),
            EmptyStatement => JsSymbols.Undefined,
            UnknownStatement unknown => throw new NotSupportedException(
                $"Typed evaluator does not yet understand the '{unknown.Node.Head}' statement form."),
            _ => throw new NotSupportedException(
                $"Typed evaluator does not yet support '{statement.GetType().Name}'.")
        };
    }

    private static object? EvaluateBlock(BlockStatement block, JsEnvironment environment, EvaluationContext context)
    {
        var scope = new JsEnvironment(environment, false, block.IsStrict);
        object? result = JsSymbols.Undefined;

        foreach (var statement in block.Statements)
        {
            result = EvaluateStatement(statement, scope, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        return result;
    }

    private static object? EvaluateReturn(ReturnStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var value = statement.Expression is null
            ? null
            : EvaluateExpression(statement.Expression, environment, context);
        context.SetReturn(value);
        return value;
    }

    private static object? EvaluateThrow(ThrowStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var value = EvaluateExpression(statement.Expression, environment, context);
        context.SetThrow(value);
        return value;
    }

    private static object? EvaluateBreak(BreakStatement statement, EvaluationContext context)
    {
        context.SetBreak(statement.Label);
        return JsSymbols.Undefined;
    }

    private static object? EvaluateContinue(ContinueStatement statement, EvaluationContext context)
    {
        context.SetContinue(statement.Label);
        return JsSymbols.Undefined;
    }

    private static object? EvaluateIf(IfStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var test = EvaluateExpression(statement.Condition, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var branch = IsTruthy(test) ? statement.Then : statement.Else;
        return branch is null ? JsSymbols.Undefined : EvaluateStatement(branch, environment, context);
    }

    private static object? EvaluateWhile(WhileStatement statement, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel)
    {
        object? lastValue = JsSymbols.Undefined;

        while (true)
        {
            var test = EvaluateExpression(statement.Condition, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return lastValue;
            }

            if (!IsTruthy(test))
            {
                break;
            }

            lastValue = EvaluateStatement(statement.Body, environment, context);
            if (context.IsReturn || context.IsThrow)
            {
                break;
            }

            if (context.TryClearContinue(loopLabel))
            {
                continue;
            }

            if (context.TryClearBreak(loopLabel))
            {
                break;
            }

            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        return lastValue;
    }

    private static object? EvaluateDoWhile(DoWhileStatement statement, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel)
    {
        object? lastValue = JsSymbols.Undefined;

        do
        {
            lastValue = EvaluateStatement(statement.Body, environment, context);
            if (context.IsReturn || context.IsThrow)
            {
                break;
            }

            if (context.TryClearContinue(loopLabel))
            {
                // continue with next iteration
            }
            else if (context.TryClearBreak(loopLabel))
            {
                break;
            }
            else if (context.ShouldStopEvaluation)
            {
                break;
            }

            var test = EvaluateExpression(statement.Condition, environment, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }

            if (!IsTruthy(test))
            {
                break;
            }
        } while (true);

        return lastValue;
    }

    private static object? EvaluateFor(ForStatement statement, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel)
    {
        var loopEnvironment = new JsEnvironment(environment, creatingExpression: null, description: "for-loop");
        object? lastValue = JsSymbols.Undefined;

        if (statement.Initializer is not null)
        {
            EvaluateStatement(statement.Initializer, loopEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }
        }

        bool ContinueLoop()
        {
            if (statement.Condition is null)
            {
                return true;
            }

            var test = EvaluateExpression(statement.Condition, loopEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            return IsTruthy(test);
        }

        while (ContinueLoop())
        {
            lastValue = EvaluateStatement(statement.Body, loopEnvironment, context);
            if (context.IsReturn || context.IsThrow)
            {
                break;
            }

            if (context.TryClearContinue(loopLabel))
            {
                if (statement.Increment is not null)
                {
                    EvaluateExpression(statement.Increment, loopEnvironment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        break;
                    }
                }

                continue;
            }

            if (context.TryClearBreak(loopLabel))
            {
                break;
            }

            if (context.ShouldStopEvaluation)
            {
                break;
            }

            if (statement.Increment is not null)
            {
                EvaluateExpression(statement.Increment, loopEnvironment, context);
                if (context.ShouldStopEvaluation)
                {
                    break;
                }
            }
        }

        return lastValue;
    }

    private static object? EvaluateForEach(ForEachStatement statement, JsEnvironment environment,
        EvaluationContext context, Symbol? loopLabel)
    {
        if (statement.Kind == ForEachKind.AwaitOf)
        {
            return EvaluateForAwaitOf(statement, environment, context, loopLabel);
        }

        var iterable = EvaluateExpression(statement.Iterable, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var loopEnvironment = new JsEnvironment(environment, creatingExpression: null, description: "for-each-loop");
        object? lastValue = JsSymbols.Undefined;

        IEnumerable<object?> values = statement.Kind switch
        {
            ForEachKind.In => EnumeratePropertyKeys(iterable),
            ForEachKind.Of => EnumerateValues(iterable),
            _ => throw new ArgumentOutOfRangeException()
        };

        foreach (var value in values)
        {
            if (context.ShouldStopEvaluation)
            {
                break;
            }

            AssignLoopBinding(statement, value, loopEnvironment, environment, context);

            lastValue = EvaluateStatement(statement.Body, loopEnvironment, context);

            if (context.IsReturn || context.IsThrow)
            {
                break;
            }

            if (context.TryClearContinue(loopLabel))
            {
                continue;
            }

            if (context.TryClearBreak(loopLabel))
            {
                break;
            }
        }

        return lastValue;
    }

    private static object? EvaluateForAwaitOf(ForEachStatement statement, JsEnvironment environment,
        EvaluationContext context, Symbol? loopLabel)
    {
        var iterable = EvaluateExpression(statement.Iterable, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var loopEnvironment = new JsEnvironment(environment, creatingExpression: null, description: "for-await-of loop");
        object? lastValue = JsSymbols.Undefined;

        if (TryGetIteratorFromProtocols(iterable, out var iterator))
        {
            while (!context.ShouldStopEvaluation)
            {
                var nextResult = InvokeIteratorNext(iterator!);
                if (nextResult is not JsObject resultObj)
                {
                    break;
                }

                if (IsPromiseLike(resultObj))
                {
                    throw new InvalidOperationException(
                        "Async iteration with promises requires async function context. Use for await...of inside an async function.");
                }

                var done = resultObj.TryGetProperty("done", out var doneValue) && doneValue is bool completed && completed;
                if (done)
                {
                    break;
                }

                if (!resultObj.TryGetProperty("value", out var value))
                {
                    continue;
                }

                AssignLoopBinding(statement, value, loopEnvironment, environment, context);
                lastValue = EvaluateStatement(statement.Body, loopEnvironment, context);

                if (context.IsReturn || context.IsThrow)
                {
                    break;
                }

                if (context.TryClearContinue(loopLabel))
                {
                    continue;
                }

                if (context.TryClearBreak(loopLabel))
                {
                    break;
                }
            }

            return lastValue;
        }

        var values = CollectSynchronousIterableValues(iterable);
        foreach (var value in values)
        {
            if (context.ShouldStopEvaluation)
            {
                break;
            }

            AssignLoopBinding(statement, value, loopEnvironment, environment, context);
            lastValue = EvaluateStatement(statement.Body, loopEnvironment, context);

            if (context.IsReturn || context.IsThrow)
            {
                break;
            }

            if (context.TryClearContinue(loopLabel))
            {
                continue;
            }

            if (context.TryClearBreak(loopLabel))
            {
                break;
            }
        }

        return lastValue;
    }

    private static bool TryGetIteratorFromProtocols(object? iterable, out JsObject? iterator)
    {
        iterator = null;
        if (iterable is not JsObject jsObject)
        {
            return false;
        }

        if (TryInvokeSymbolMethod(jsObject, "Symbol.asyncIterator", out var asyncIterator) && asyncIterator is JsObject asyncObj)
        {
            iterator = asyncObj;
            return true;
        }

        if (TryInvokeSymbolMethod(jsObject, "Symbol.iterator", out var iteratorValue) && iteratorValue is JsObject iteratorObj)
        {
            iterator = iteratorObj;
            return true;
        }

        return false;
    }

    private static bool TryInvokeSymbolMethod(JsObject target, string symbolName, out object? result)
    {
        var symbol = TypedAstSymbol.For(symbolName);
        var propertyName = $"@@symbol:{symbol.GetHashCode()}";
        if (target.TryGetProperty(propertyName, out var candidate) && candidate is IJsCallable callable)
        {
            result = callable.Invoke(Array.Empty<object?>(), target);
            return true;
        }

        result = null;
        return false;
    }

    private static object? InvokeIteratorNext(JsObject iterator)
    {
        if (!iterator.TryGetProperty("next", out var nextValue) || nextValue is not IJsCallable callable)
        {
            throw new InvalidOperationException("Iterator must expose a 'next' method.");
        }

        return callable.Invoke(Array.Empty<object?>(), iterator);
    }

    private static bool IsPromiseLike(object? candidate)
    {
        return candidate is JsObject jsObject && jsObject.TryGetProperty("then", out var thenValue) &&
               thenValue is IJsCallable;
    }

    private static IEnumerable<object?> CollectSynchronousIterableValues(object? iterable)
    {
        switch (iterable)
        {
            case JsArray jsArray:
            {
                for (var i = 0; i < jsArray.Items.Count; i++)
                {
                    yield return jsArray.GetElement(i);
                }

                yield break;
            }
            case JsGenerator generator:
            {
                while (true)
                {
                    var next = generator.Next(null);
                    if (next is not JsObject nextObj)
                    {
                        yield break;
                    }

                    var done = nextObj.TryGetProperty("done", out var doneValue) && doneValue is bool completed && completed;
                    if (done)
                    {
                        yield break;
                    }

                    if (nextObj.TryGetProperty("value", out var value))
                    {
                        yield return value;
                    }
                }
            }
            case string s:
            {
                foreach (var ch in s)
                {
                    yield return ch.ToString();
                }

                yield break;
            }
        }

        throw new InvalidOperationException($"Cannot iterate over non-iterable value '{iterable}'.");
    }

    private static void AssignLoopBinding(ForEachStatement statement, object? value, JsEnvironment loopEnvironment,
        JsEnvironment outerEnvironment, EvaluationContext context)
    {
        if (statement.DeclarationKind is null)
        {
            AssignBindingTarget(statement.Target, value, outerEnvironment, context);
            return;
        }

        switch (statement.DeclarationKind)
        {
            case VariableKind.Var:
                DefineOrAssignVar(statement.Target, value, loopEnvironment, context);
                break;
            case VariableKind.Let:
            case VariableKind.Const:
                DefineBindingTarget(statement.Target, value, loopEnvironment, context,
                    statement.DeclarationKind == VariableKind.Const);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static IEnumerable<object?> EnumeratePropertyKeys(object? value)
    {
        if (value is JsObject jsObject)
        {
            foreach (var key in jsObject.GetOwnPropertyNames())
            {
                yield return key;
            }

            yield break;
        }

        if (value is JsArray array)
        {
            for (var i = 0; i < array.Items.Count; i++)
            {
                yield return i.ToString(CultureInfo.InvariantCulture);
            }

            yield break;
        }

        if (value is string s)
        {
            for (var i = 0; i < s.Length; i++)
            {
                yield return i.ToString(CultureInfo.InvariantCulture);
            }

            yield break;
        }

        throw new InvalidOperationException("Cannot iterate properties of non-object value.");
    }

    private static IEnumerable<object?> EnumerateValues(object? value)
    {
        switch (value)
        {
            case JsArray array:
                foreach (var item in array.Items)
                {
                    yield return item;
                }

                yield break;
            case string s:
                foreach (var ch in s)
                {
                    yield return ch.ToString();
                }

                yield break;
            case IEnumerable<object?> enumerable:
                foreach (var item in enumerable)
                {
                    yield return item;
                }

                yield break;
        }

        throw new InvalidOperationException("Value is not iterable.");
    }

    private static object? EvaluateTry(TryStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var result = EvaluateBlock(statement.TryBlock, environment, context);
        if (context.IsThrow && statement.Catch is not null)
        {
            var thrownValue = context.FlowValue;
            context.Clear();
            var catchEnv = new JsEnvironment(environment, creatingExpression: null, description: "catch");
            catchEnv.Define(statement.Catch.Binding, thrownValue);
            result = EvaluateBlock(statement.Catch.Body, catchEnv, context);
        }

        if (statement.Finally is not null)
        {
            var savedSignal = context.CurrentSignal;
            context.Clear();
            _ = EvaluateBlock(statement.Finally, environment, context);
            if (context.CurrentSignal is null)
            {
                RestoreSignal(context, savedSignal);
            }
        }

        return result;
    }

    private static object? EvaluateSwitch(SwitchStatement statement, JsEnvironment environment, EvaluationContext context,
        Symbol? targetLabel)
    {
        var discriminant = EvaluateExpression(statement.Discriminant, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        object? lastValue = JsSymbols.Undefined;
        var hasMatched = false;

        foreach (var switchCase in statement.Cases)
        {
            if (!hasMatched)
            {
                if (switchCase.Test is null)
                {
                    hasMatched = true;
                }
                else
                {
                    var test = EvaluateExpression(switchCase.Test, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return lastValue;
                    }

                    hasMatched = StrictEquals(discriminant, test);
                }

                if (!hasMatched)
                {
                    continue;
                }
            }

            lastValue = EvaluateBlock(switchCase.Body, environment, context);
            if (context.TryClearBreak(targetLabel))
            {
                break;
            }

            if (context.IsReturn || context.IsThrow)
            {
                break;
            }
        }

        return lastValue;
    }

    private static object? EvaluateVariableDeclaration(VariableDeclaration declaration, JsEnvironment environment,
        EvaluationContext context)
    {
        foreach (var declarator in declaration.Declarators)
        {
            EvaluateVariableDeclarator(declaration.Kind, declarator, environment, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        return JsSymbols.Undefined;
    }

    private static void EvaluateVariableDeclarator(VariableKind kind, VariableDeclarator declarator,
        JsEnvironment environment, EvaluationContext context)
    {
        var value = declarator.Initializer is null
            ? JsSymbols.Undefined
            : EvaluateExpression(declarator.Initializer, environment, context);

        if (context.ShouldStopEvaluation)
        {
            return;
        }

        var mode = kind switch
        {
            VariableKind.Var => BindingMode.DefineVar,
            VariableKind.Let => BindingMode.DefineLet,
            VariableKind.Const => BindingMode.DefineConst,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        ApplyBindingTarget(declarator.Target, value, environment, context, mode, declarator.Initializer is not null);
    }

    private static object? EvaluateFunctionDeclaration(FunctionDeclaration declaration, JsEnvironment environment)
    {
        var function = new TypedFunction(declaration.Function, environment);
        environment.Define(declaration.Name, function);
        return function;
    }

    private static object? EvaluateLabeled(LabeledStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        context.PushLabel(statement.Label);
        try
        {
            var result = EvaluateStatement(statement.Statement, environment, context, statement.Label);

            if (context.TryClearBreak(statement.Label))
            {
                return JsSymbols.Undefined;
            }

            return result;
        }
        finally
        {
            context.PopLabel();
        }
    }

    private static object? EvaluateExpression(ExpressionNode expression, JsEnvironment environment,
        EvaluationContext context)
    {
        context.SourceReference = expression.Source;

        return expression switch
        {
            LiteralExpression literal => literal.Value,
            IdentifierExpression identifier => environment.Get(identifier.Name),
            BinaryExpression binary => EvaluateBinary(binary, environment, context),
            UnaryExpression unary => EvaluateUnary(unary, environment, context),
            ConditionalExpression conditional => EvaluateConditional(conditional, environment, context),
            CallExpression call => EvaluateCall(call, environment, context),
            FunctionExpression functionExpression => new TypedFunction(functionExpression, environment),
            AssignmentExpression assignment => EvaluateAssignment(assignment, environment, context),
            PropertyAssignmentExpression propertyAssignment =>
                EvaluatePropertyAssignment(propertyAssignment, environment, context),
            IndexAssignmentExpression indexAssignment =>
                EvaluateIndexAssignment(indexAssignment, environment, context),
            SequenceExpression sequence => EvaluateSequence(sequence, environment, context),
            MemberExpression member => EvaluateMember(member, environment, context),
            NewExpression newExpression => EvaluateNew(newExpression, environment, context),
            ArrayExpression array => EvaluateArray(array, environment, context),
            ObjectExpression obj => EvaluateObject(obj, environment, context),
            TemplateLiteralExpression template => EvaluateTemplateLiteral(template, environment, context),
            TaggedTemplateExpression taggedTemplate =>
                EvaluateTaggedTemplate(taggedTemplate, environment, context),
            ThisExpression => environment.Get(JsSymbols.This),
            UnknownExpression unknown => throw new NotSupportedException(
                $"Typed evaluator does not yet understand the '{unknown.Node.Head}' expression form."),
            _ => throw new NotSupportedException(
                $"Typed evaluator does not yet support '{expression.GetType().Name}'.")
        };
    }

    private static object? EvaluateAssignment(AssignmentExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var targetValue = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return targetValue;
        }

        environment.Assign(expression.Target, targetValue);
        return targetValue;
    }

    private static object? EvaluatePropertyAssignment(PropertyAssignmentExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var target = EvaluateExpression(expression.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (expression.IsComputed && IsNullish(target))
        {
            throw new InvalidOperationException("Cannot set property on null or undefined.");
        }

        var property = EvaluateExpression(expression.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var value = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        AssignPropertyValue(target, property, value);
        return value;
    }

    private static object? EvaluateIndexAssignment(IndexAssignmentExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var target = EvaluateExpression(expression.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var index = EvaluateExpression(expression.Index, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var value = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        AssignPropertyValue(target, index, value);
        return value;
    }

    private static object? EvaluateSequence(SequenceExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        _ = EvaluateExpression(expression.Left, environment, context);
        return context.ShouldStopEvaluation
            ? JsSymbols.Undefined
            : EvaluateExpression(expression.Right, environment, context);
    }

    private static object? EvaluateMember(MemberExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var target = EvaluateExpression(expression.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (expression.IsOptional && IsNullish(target))
        {
            return JsSymbols.Undefined;
        }

        var propertyValue = EvaluateExpression(expression.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (TryGetPropertyValue(target, propertyValue, out var arrayValue))
        {
            return arrayValue;
        }

        var propertyName = ToPropertyName(propertyValue)
                           ?? throw new InvalidOperationException("Property name cannot be null.");

        return TryGetPropertyValue(target, propertyName, out var value)
            ? value
            : JsSymbols.Undefined;
    }

    private static object? EvaluateConditional(ConditionalExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var test = EvaluateExpression(expression.Test, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        return IsTruthy(test)
            ? EvaluateExpression(expression.Consequent, environment, context)
            : EvaluateExpression(expression.Alternate, environment, context);
    }

    private static object? EvaluateUnary(UnaryExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        if (expression.Operator is "++" or "--")
        {
            var reference = ResolveReference(expression.Operand, environment, context);
            var currentValue = reference.GetValue();
            var updatedValue = expression.Operator == "++"
                ? IncrementValue(currentValue)
                : DecrementValue(currentValue);
            reference.SetValue(updatedValue);
            return expression.IsPrefix ? updatedValue : currentValue;
        }

        if (expression.Operator == "delete")
        {
            return EvaluateDelete(expression.Operand, environment, context);
        }

        if (expression.Operator == "typeof")
        {
            if (expression.Operand is IdentifierExpression identifier &&
                !environment.TryGet(identifier.Name, out var value))
            {
                return "undefined";
            }

            var operandValue = EvaluateExpression(expression.Operand, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }

            return GetTypeofString(operandValue);
        }

        var operand = EvaluateExpression(expression.Operand, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        return expression.Operator switch
        {
            "!" => !IsTruthy(operand),
            "+" => operand is JsBigInt
                ? throw new Exception("Cannot convert a BigInt value to a number")
                : operand.ToNumber(),
            "-" => operand is JsBigInt bigInt ? (object)(-bigInt) : -operand.ToNumber(),
            "~" => BitwiseNot(operand),
            "void" => JsSymbols.Undefined,
            _ => throw new NotSupportedException($"Operator '{expression.Operator}' is not supported yet.")
        };
    }

    private static object? EvaluateBinary(BinaryExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        var left = EvaluateExpression(expression.Left, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        switch (expression.Operator)
        {
            case "&&":
                return IsTruthy(left)
                    ? EvaluateExpression(expression.Right, environment, context)
                    : left;
            case "||":
                return IsTruthy(left)
                    ? left
                    : EvaluateExpression(expression.Right, environment, context);
            case "??":
                return IsNullish(left)
                    ? EvaluateExpression(expression.Right, environment, context)
                    : left;
        }

        var right = EvaluateExpression(expression.Right, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        return expression.Operator switch
        {
            "+" => Add(left, right),
            "-" => Subtract(left, right),
            "*" => Multiply(left, right),
            "/" => Divide(left, right),
            "%" => Modulo(left, right),
            "**" => Power(left, right),
            "==" => LooseEquals(left, right),
            "!=" => !LooseEquals(left, right),
            "===" => StrictEquals(left, right),
            "!==" => !StrictEquals(left, right),
            "<" => LessThan(left, right),
            "<=" => LessThanOrEqual(left, right),
            ">" => GreaterThan(left, right),
            ">=" => GreaterThanOrEqual(left, right),
            "&" => BitwiseAnd(left, right),
            "|" => BitwiseOr(left, right),
            "^" => BitwiseXor(left, right),
            "<<" => LeftShift(left, right),
            ">>" => RightShift(left, right),
            ">>>" => UnsignedRightShift(left, right),
            "in" => InOperator(left, right),
            "instanceof" => InstanceofOperator(left, right),
            _ => throw new NotSupportedException($"Operator '{expression.Operator}' is not supported yet.")
        };
    }

    private static object? EvaluateCall(CallExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        var (callee, thisValue, skippedOptional) = EvaluateCallTarget(expression.Callee, environment, context);
        if (context.ShouldStopEvaluation || skippedOptional)
        {
            return JsSymbols.Undefined;
        }

        if (expression.IsOptional && IsNullish(callee))
        {
            return JsSymbols.Undefined;
        }

        if (callee is not IJsCallable callable)
        {
            throw new InvalidOperationException(
                $"Attempted to call a non-callable value of type '{callee?.GetType().Name ?? "null"}'.");
        }

        var arguments = ImmutableArray.CreateBuilder<object?>(expression.Arguments.Length);
        foreach (var argument in expression.Arguments)
        {
            if (argument.IsSpread)
            {
                var spreadValue = EvaluateExpression(argument.Expression, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsSymbols.Undefined;
                }

                foreach (var item in EnumerateSpread(spreadValue))
                {
                    arguments.Add(item);
                }

                continue;
            }

            arguments.Add(EvaluateExpression(argument.Expression, environment, context));
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }
        }

        if (callable is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
        }

        DebugAwareHostFunction? debugFunction = null;
        if (callable is DebugAwareHostFunction debugAware)
        {
            debugFunction = debugAware;
            debugFunction.CurrentJsEnvironment = environment;
            debugFunction.CurrentContext = context;
        }

        var frozenArguments = FreezeArguments(arguments);

        try
        {
            return callable.Invoke(frozenArguments, thisValue);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
        finally
        {
            if (debugFunction is not null)
            {
                debugFunction.CurrentJsEnvironment = null;
                debugFunction.CurrentContext = null;
            }
        }
    }

    private static ImmutableArray<object?> FreezeArguments(ImmutableArray<object?>.Builder builder)
    {
        return builder.Count == builder.Capacity
            ? builder.MoveToImmutable()
            : builder.ToImmutable();
    }

    private static (object? Callee, object? ThisValue, bool SkippedOptional) EvaluateCallTarget(ExpressionNode callee,
        JsEnvironment environment, EvaluationContext context)
    {
        if (callee is MemberExpression member)
        {
            var target = EvaluateExpression(member.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return (JsSymbols.Undefined, null, true);
            }

            if (member.IsOptional && IsNullish(target))
            {
                return (null, null, true);
            }

            var property = EvaluateExpression(member.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return (JsSymbols.Undefined, null, true);
            }

            var propertyName = ToPropertyName(property)
                               ?? throw new InvalidOperationException("Property name cannot be null.");
            if (!TryGetPropertyValue(target, propertyName, out var value))
            {
                return (JsSymbols.Undefined, target, false);
            }

            return (value, target, false);
        }

        var directCallee = EvaluateExpression(callee, environment, context);
        return (directCallee, null, false);
    }

    private static IEnumerable<object?> EnumerateSpread(object? value)
    {
        switch (value)
        {
            case null:
                yield break;
            case JsArray array:
                foreach (var item in array.Items)
                {
                    yield return item;
                }

                yield break;
            case string s:
                foreach (var ch in s)
                {
                    yield return ch.ToString();
                }

                yield break;
            case IEnumerable enumerable:
                foreach (var item in enumerable)
                {
                    yield return item;
                }

                yield break;
            default:
                throw new InvalidOperationException("Value is not iterable.");
        }
    }

    private static bool EvaluateDelete(ExpressionNode operand, JsEnvironment environment, EvaluationContext context)
    {
        if (operand is MemberExpression member)
        {
            var target = EvaluateExpression(member.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            var propertyValue = EvaluateExpression(member.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            return DeletePropertyValue(target, propertyValue);
        }

        // Deleting identifiers is a no-op in strict mode; return false to indicate failure.
        return false;
    }

    private static object? EvaluateNew(NewExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        var constructor = EvaluateExpression(expression.Constructor, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (constructor is not IJsCallable callable)
        {
            throw new InvalidOperationException("Attempted to construct a non-callable value.");
        }

        var instance = new JsObject();
        if (TryGetPropertyValue(constructor, "prototype", out var prototype) && prototype is JsObject proto)
        {
            instance.SetPrototype(proto);
        }

        var args = ImmutableArray.CreateBuilder<object?>(expression.Arguments.Length);
        foreach (var argument in expression.Arguments)
        {
            args.Add(EvaluateExpression(argument, environment, context));
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }
        }

        var result = callable.Invoke(args.MoveToImmutable(), instance);

        // In JavaScript, constructors can explicitly return an object to override the
        // default instance that `new` creates. Our host objects (Map, Set, custom
        // host functions, etc.) don't necessarily derive from JsObject, but they do
        // expose their members through IJsPropertyAccessor/IJsCallable. Treat any
        // such object-like result as the constructed value; otherwise fall back to
        // the auto-created instance.
        return result switch
        {
            IJsPropertyAccessor => result,
            IJsCallable => result,
            _ => instance
        };
    }

    private static object? EvaluateArray(ArrayExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var array = new JsArray();
        foreach (var element in expression.Elements)
        {
            if (element.IsSpread)
            {
                var spreadValue = EvaluateExpression(element.Expression!, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsSymbols.Undefined;
                }

                foreach (var item in EnumerateSpread(spreadValue))
                {
                    array.Push(item);
                }

                continue;
            }

            array.Push(element.Expression is null
                ? JsSymbols.Undefined
                : EvaluateExpression(element.Expression, environment, context));
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }
        }

        StandardLibrary.AddArrayMethods(array);
        return array;
    }

    private static object? EvaluateObject(ObjectExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var obj = new JsObject();
        foreach (var member in expression.Members)
        {
            switch (member.Kind)
            {
                case ObjectMemberKind.Property:
                {
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsSymbols.Undefined;
                    }

                    var value = member.Value is null
                        ? JsSymbols.Undefined
                        : EvaluateExpression(member.Value, environment, context);
                    obj.SetProperty(name, value);
                    break;
                }
                case ObjectMemberKind.Method:
                {
                    var method = new TypedFunction(member.Function!, environment);
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsSymbols.Undefined;
                    }

                    obj.SetProperty(name, method);
                    break;
                }
                case ObjectMemberKind.Getter:
                {
                    var getter = new TypedFunction(member.Function!, environment);
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsSymbols.Undefined;
                    }

                    obj.SetGetter(name, getter);
                    break;
                }
                case ObjectMemberKind.Setter:
                {
                    var setter = new TypedFunction(member.Function!, environment);
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsSymbols.Undefined;
                    }

                    obj.SetSetter(name, setter);
                    break;
                }
                case ObjectMemberKind.Field:
                {
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsSymbols.Undefined;
                    }

                    var value = member.Value is null
                        ? JsSymbols.Undefined
                        : EvaluateExpression(member.Value, environment, context);
                    obj.SetProperty(name, value);
                    break;
                }
                case ObjectMemberKind.Spread:
                {
                    var spreadValue = EvaluateExpression(member.Value!, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsSymbols.Undefined;
                    }

                    if (spreadValue is JsObject spreadObject)
                    {
                        foreach (var key in spreadObject.GetOwnPropertyNames())
                        {
                            var spreadPropertyValue = spreadObject.TryGetProperty(key, out var val)
                                ? val
                                : JsSymbols.Undefined;
                            obj.SetProperty(key, spreadPropertyValue);
                        }

                        break;
                    }

                    if (spreadValue is IDictionary<string, object?> dictionary)
                    {
                        foreach (var kvp in dictionary)
                        {
                            obj.SetProperty(kvp.Key, kvp.Value);
                        }

                        break;
                    }

                    throw new InvalidOperationException("Cannot spread value that is not an object.");
                }
            }
        }

        return obj;
    }

    private static string ResolveObjectMemberName(ObjectMember member, JsEnvironment environment,
        EvaluationContext context)
    {
        object? keyValue;

        if (member.IsComputed)
        {
            if (member.Key is not ExpressionNode keyExpression)
            {
                throw new InvalidOperationException("Computed property name must be an expression.");
            }

            keyValue = EvaluateExpression(keyExpression, environment, context);
        }
        else
        {
            keyValue = member.Key;
        }

        if (context.ShouldStopEvaluation)
        {
            return string.Empty;
        }

        return ToPropertyName(keyValue)
               ?? throw new InvalidOperationException("Property name cannot be null.");
    }

    private static object? EvaluateTemplateLiteral(TemplateLiteralExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var part in expression.Parts)
        {
            if (part.Text is not null)
            {
                builder.Append(part.Text);
                continue;
            }

            if (part.Expression is null)
            {
                continue;
            }

            var value = EvaluateExpression(part.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }

            builder.Append(ToJsString(value));
        }

        return builder.ToString();
    }

    private static object? EvaluateTaggedTemplate(TaggedTemplateExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var (tagValue, thisValue, skippedOptional) = EvaluateCallTarget(expression.Tag, environment, context);
        if (context.ShouldStopEvaluation || skippedOptional)
        {
            return JsSymbols.Undefined;
        }

        if (tagValue is not IJsCallable callable)
        {
            throw new InvalidOperationException("Tag in tagged template must be a function.");
        }

        var stringsArrayValue = EvaluateExpression(expression.StringsArray, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (stringsArrayValue is not JsArray stringsArray)
        {
            throw new InvalidOperationException("Tagged template strings array is invalid.");
        }

        var rawStringsArrayValue = EvaluateExpression(expression.RawStringsArray, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (rawStringsArrayValue is not JsArray rawStringsArray)
        {
            throw new InvalidOperationException("Tagged template raw strings array is invalid.");
        }

        var templateObject = CreateTemplateObject(stringsArray, rawStringsArray);

        var arguments = ImmutableArray.CreateBuilder<object?>(expression.Expressions.Length + 1);
        arguments.Add(templateObject);

        foreach (var expr in expression.Expressions)
        {
            arguments.Add(EvaluateExpression(expr, environment, context));
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }
        }

        if (callable is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
        }

        DebugAwareHostFunction? debugFunction = null;
        if (callable is DebugAwareHostFunction debugAware)
        {
            debugFunction = debugAware;
            debugFunction.CurrentJsEnvironment = environment;
            debugFunction.CurrentContext = context;
        }

        var frozenArguments = FreezeArguments(arguments);

        try
        {
            return callable.Invoke(frozenArguments, thisValue);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
        finally
        {
            if (debugFunction is not null)
            {
                debugFunction.CurrentJsEnvironment = null;
                debugFunction.CurrentContext = null;
            }
        }
    }

    private static JsObject CreateTemplateObject(JsArray stringsArray, JsArray rawStringsArray)
    {
        var templateObject = new JsObject();
        for (var i = 0; i < stringsArray.Items.Count; i++)
        {
            templateObject[i.ToString(CultureInfo.InvariantCulture)] = stringsArray.Items[i];
        }

        templateObject["length"] = (double)stringsArray.Items.Count;
        templateObject["raw"] = rawStringsArray;
        return templateObject;
    }

    private static string ToJsString(object? value)
    {
        return value switch
        {
            null => "null",
            Symbol sym when ReferenceEquals(sym, JsSymbols.Undefined) => "undefined",
            Symbol sym => sym.Name,
            string s => s,
            bool b => b ? "true" : "false",
            double d => d.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            JsBigInt bigInt => bigInt.ToString(),
            JsArray array => ArrayToString(array),
            JsObject => "[object Object]",
            IJsCallable => "function() { [native code] }",
            TypedAstSymbol jsSymbol => jsSymbol.ToString(),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

        static string ArrayToString(JsArray array)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < array.Items.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(ExpressionEvaluator.ToJsStringForArray(array.Items[i]));
            }

            return builder.ToString();
        }
    }

    private static bool IsNullish(object? value)
    {
        return value is null || value is Symbol symbol && ReferenceEquals(symbol, JsSymbols.Undefined);
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            Symbol sym when ReferenceEquals(sym, JsSymbols.Undefined) => false,
            bool b => b,
            double d => !double.IsNaN(d) && Math.Abs(d) > double.Epsilon,
            float f => !float.IsNaN(f) && Math.Abs(f) > float.Epsilon,
            string s => s.Length > 0,
            _ => true
        };
    }

    private static object? Add(object? left, object? right)
    {
        if (left is string || right is string || left is JsObject || left is JsArray || right is JsObject || right is JsArray)
        {
            return ExpressionEvaluator.ToJsString(left) + ExpressionEvaluator.ToJsString(right);
        }

        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt + rightBigInt;
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        return left.ToNumber() + right.ToNumber();
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

    private static object Power(object? left, object? right)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => JsBigInt.Pow(l, r),
            (l, r) => Math.Pow(l, r));
    }

    private static object PerformBigIntOrNumericOperation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<double, double, object> numericOp)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return bigIntOp(leftBigInt, rightBigInt);
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        return numericOp(left.ToNumber(), right.ToNumber());
    }

    private static bool LooseEquals(object? left, object? right)
    {
        while (true)
        {
            if (IsNullish(left) && IsNullish(right))
            {
                return true;
            }

            if (IsNullish(left) || IsNullish(right))
            {
                return false;
            }

            if (left?.GetType() == right?.GetType())
            {
                return StrictEquals(left, right);
            }

            if (left is JsBigInt leftBigInt && IsNumeric(right))
            {
                var rightNum = right.ToNumber();
                if (double.IsNaN(rightNum) || double.IsInfinity(rightNum))
                {
                    return false;
                }

                if (rightNum == Math.Floor(rightNum))
                {
                    return leftBigInt.Value == new BigInteger(rightNum);
                }

                return false;
            }

            if (IsNumeric(left) && right is JsBigInt rightBigInt)
            {
                var leftNum = left.ToNumber();
                if (double.IsNaN(leftNum) || double.IsInfinity(leftNum))
                {
                    return false;
                }

                if (leftNum == Math.Floor(leftNum))
                {
                    return new BigInteger(leftNum) == rightBigInt.Value;
                }

                return false;
            }

            switch (left)
            {
                case JsBigInt lbi when right is string str:
                    try
                    {
                        var converted = new JsBigInt(str.Trim());
                        return lbi == converted;
                    }
                    catch
                    {
                        return false;
                    }
                case string str2 when right is JsBigInt rbi:
                    try
                    {
                        var converted = new JsBigInt(str2.Trim());
                        return converted == rbi;
                    }
                    catch
                    {
                        return false;
                    }
            }

            if (IsNumeric(left) && right is string)
            {
                return left.ToNumber().Equals(right.ToNumber());
            }

            switch (left)
            {
                case string when IsNumeric(right):
                    return left.ToNumber().Equals(right.ToNumber());
                case bool:
                    left = left.ToNumber();
                    continue;
            }

            if (right is bool)
            {
                right = right.ToNumber();
                continue;
            }

            if (left is JsObject or JsArray)
            {
                if (IsNumeric(right))
                {
                    return left.ToNumber().Equals(right.ToNumber());
                }

                if (right is string rightString)
                {
                    return string.Equals(ToJsString(left), rightString, StringComparison.Ordinal);
                }
            }

            if (right is JsObject or JsArray)
            {
                if (IsNumeric(left))
                {
                    return left.ToNumber().Equals(right.ToNumber());
                }

                if (left is string leftString)
                {
                    return string.Equals(leftString, ToJsString(right), StringComparison.Ordinal);
                }
            }

            return StrictEquals(left, right);
        }
    }

    private static bool StrictEquals(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return left is not double d || !double.IsNaN(d);
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt == rightBigInt;
        }

        if ((left is JsBigInt && IsNumeric(right)) || (IsNumeric(left) && right is JsBigInt))
        {
            return false;
        }

        if (!IsNumeric(left) || !IsNumeric(right))
        {
            return left.GetType() == right.GetType() && Equals(left, right);
        }

        var leftNumber = left.ToNumber();
        var rightNumber = right.ToNumber();
        if (double.IsNaN(leftNumber) || double.IsNaN(rightNumber))
        {
            return false;
        }

        return leftNumber.Equals(rightNumber);
    }

    private static bool IsNumeric(object? value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
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

    private static bool PerformComparisonOperation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, bool> bigIntOp,
        Func<BigInteger, BigInteger, bool> mixedOp,
        Func<double, double, bool> numericOp)
    {
        switch (left)
        {
            case JsBigInt leftBigInt when right is JsBigInt rightBigInt:
                return bigIntOp(leftBigInt, rightBigInt);
            case JsBigInt lbi:
            {
                var rightNum = right.ToNumber();
                if (double.IsNaN(rightNum))
                {
                    return false;
                }

                return mixedOp(lbi.Value, new BigInteger(rightNum));
            }
        }

        switch (right)
        {
            case JsBigInt rbi:
            {
                var leftNum = left.ToNumber();
                if (double.IsNaN(leftNum))
                {
                    return false;
                }

                return mixedOp(new BigInteger(leftNum), rbi.Value);
            }
            default:
                return numericOp(left.ToNumber(), right.ToNumber());
        }
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
        if (operand is JsBigInt bigInt)
        {
            return ~bigInt;
        }

        return (double)(~ToInt32(operand));
    }

    private static object LeftShift(object? left, object? right)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw new InvalidOperationException("BigInt shift amount is too large");
            }

            return leftBigInt << (int)rightBigInt.Value;
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ToInt32(left);
        var rightInt = ToInt32(right) & 0x1F;
        return (double)(leftInt << rightInt);
    }

    private static object RightShift(object? left, object? right)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw new InvalidOperationException("BigInt shift amount is too large");
            }

            return leftBigInt >> (int)rightBigInt.Value;
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ToInt32(left);
        var rightInt = ToInt32(right) & 0x1F;
        return (double)(leftInt >> rightInt);
    }

    private static object UnsignedRightShift(object? left, object? right)
    {
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("BigInts have no unsigned right shift, use >> instead");
        }

        var leftUInt = ToUInt32(left);
        var rightInt = ToInt32(right) & 0x1F;
        return (double)(leftUInt >> rightInt);
    }

    private static object PerformBigIntOrInt32Operation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<int, int, int> int32Op)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return bigIntOp(leftBigInt, rightBigInt);
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ToInt32(left);
        var rightInt = ToInt32(right);
        return (double)int32Op(leftInt, rightInt);
    }

    private static int ToInt32(object? value)
    {
        return JsNumericConversions.ToInt32(value.ToNumber());
    }

    private static uint ToUInt32(object? value)
    {
        return JsNumericConversions.ToUInt32(value.ToNumber());
    }

    private static object IncrementValue(object? value)
    {
        return value switch
        {
            JsBigInt bigInt => new JsBigInt(bigInt.Value + BigInteger.One),
            _ => value.ToNumber() + 1
        };
    }

    private static object DecrementValue(object? value)
    {
        return value switch
        {
            JsBigInt bigInt => new JsBigInt(bigInt.Value - BigInteger.One),
            _ => value.ToNumber() - 1
        };
    }

    private static string? ToPropertyName(object? value)
    {
        return value switch
        {
            null => "null",
            string s => s,
            Symbol symbol => symbol.Name,
            TypedAstSymbol jsSymbol => $"@@symbol:{jsSymbol.GetHashCode()}",
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d when !double.IsNaN(d) && !double.IsInfinity(d) => d.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static bool TryResolveArrayIndex(object? candidate, out int index)
    {
        switch (candidate)
        {
            case int i when i >= 0:
                index = i;
                return true;
            case long l when l >= 0 && l <= int.MaxValue:
                index = (int)l;
                return true;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                if (d < 0)
                {
                    break;
                }

                var truncated = Math.Truncate(d);
                if (Math.Abs(truncated - d) > double.Epsilon)
                {
                    break;
                }

                if (truncated > int.MaxValue)
                {
                    break;
                }

                index = (int)truncated;
                return true;
            case JsBigInt bigInt when bigInt.Value >= BigInteger.Zero && bigInt.Value <= int.MaxValue:
                index = (int)bigInt.Value;
                return true;
            case string s when int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0:
                index = parsed;
                return true;
        }

        index = 0;
        return false;
    }

    private static bool TryGetPropertyValue(object? target, string propertyName, out object? value)
    {
        if (target is IJsPropertyAccessor propertyAccessor)
        {
            return propertyAccessor.TryGetProperty(propertyName, out value);
        }

        switch (target)
        {
            case double num:
                var numberWrapper = StandardLibrary.CreateNumberWrapper(num);
                if (numberWrapper.TryGetProperty(propertyName, out value))
                {
                    return true;
                }

                break;
            case string str:
                if (propertyName == "length")
                {
                    value = (double)str.Length;
                    return true;
                }

                if (int.TryParse(propertyName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                    index >= 0 && index < str.Length)
                {
                    value = str[index].ToString();
                    return true;
                }

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

    private static bool TryGetPropertyValue(object? target, object? propertyKey, out object? value)
    {
        if (TryGetArrayLikeValue(target, propertyKey, out value))
        {
            return true;
        }

        var propertyName = ToPropertyName(propertyKey);
        if (propertyName is null)
        {
            value = JsSymbols.Undefined;
            return true;
        }

        return TryGetPropertyValue(target, propertyName, out value);
    }

    private static bool TryGetArrayLikeValue(object? target, object? propertyKey, out object? value)
    {
        if (target is JsArray jsArray && TryResolveArrayIndex(propertyKey, out var arrayIndex))
        {
            value = jsArray.GetElement(arrayIndex);
            return true;
        }

        if (target is TypedArrayBase typedArray && TryResolveArrayIndex(propertyKey, out var typedIndex))
        {
            value = typedIndex >= 0 && typedIndex < typedArray.Length
                ? typedArray.GetElement(typedIndex)
                : JsSymbols.Undefined;
            return true;
        }

        value = null;
        return false;
    }

    private static void AssignPropertyValue(object? target, object? propertyKey, object? value)
    {
        if (TryAssignArrayLikeValue(target, propertyKey, value))
        {
            return;
        }

        var propertyName = ToPropertyName(propertyKey)
                           ?? throw new InvalidOperationException("Property name cannot be null.");

        AssignPropertyValueByName(target, propertyName, value);
    }

    private static void AssignPropertyValueByName(object? target, string propertyName, object? value)
    {
        if (target is IJsPropertyAccessor accessor)
        {
            accessor.SetProperty(propertyName, value);
            return;
        }

        throw new InvalidOperationException($"Cannot assign property '{propertyName}' on value '{target}'.");
    }

    private static bool TryAssignArrayLikeValue(object? target, object? propertyKey, object? value)
    {
        if (target is JsArray jsArray && TryResolveArrayIndex(propertyKey, out var index))
        {
            jsArray.SetElement(index, value);
            return true;
        }

        if (target is TypedArrayBase typedArray && TryResolveArrayIndex(propertyKey, out var typedIndex))
        {
            if (typedIndex < 0 || typedIndex >= typedArray.Length)
            {
                return true;
            }

            var numericValue = value switch
            {
                double d => d,
                int i => i,
                long l => l,
                float f => f,
                bool b => b ? 1.0 : 0.0,
                null => 0.0,
                _ => 0.0
            };

            typedArray.SetElement(typedIndex, numericValue);
            return true;
        }

        return false;
    }

    private static bool DeletePropertyValue(object? target, object? propertyKey)
    {
        if (target is JsArray jsArray)
        {
            if (TryResolveArrayIndex(propertyKey, out var arrayIndex))
            {
                return jsArray.DeleteElement(arrayIndex);
            }

            var propertyName = ToPropertyName(propertyKey);
            return propertyName is null || jsArray.DeleteProperty(propertyName);
        }

        if (target is TypedArrayBase typedArray)
        {
            if (TryResolveArrayIndex(propertyKey, out _))
            {
                return false;
            }

            var propertyName = ToPropertyName(propertyKey);
            return propertyName is null || typedArray.DeleteProperty(propertyName);
        }

        var resolvedName = ToPropertyName(propertyKey);
        if (resolvedName is null)
        {
            return true;
        }

        if (target is JsObject jsObject)
        {
            if (!jsObject.ContainsKey(resolvedName))
            {
                return true;
            }

            return jsObject.Remove(resolvedName);
        }

        // Deleting primitives or other non-object values is a no-op that succeeds
        return true;
    }

    private static void HoistVarDeclarations(BlockStatement block, JsEnvironment environment)
    {
        foreach (var statement in block.Statements)
        {
            HoistFromStatement(statement, environment);
        }
    }

    private static void HoistFromStatement(StatementNode statement, JsEnvironment environment)
    {
        while (true)
        {
            switch (statement)
            {
                case VariableDeclaration { Kind: VariableKind.Var } varDeclaration:
                    foreach (var declarator in varDeclaration.Declarators)
                    {
                        HoistFromBindingTarget(declarator.Target, environment);
                    }

                    break;
                case BlockStatement block:
                    HoistVarDeclarations(block, environment);
                    break;
                case IfStatement ifStatement:
                    HoistFromStatement(ifStatement.Then, environment);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        statement = elseBranch;
                        continue;
                    }

                    break;
                case WhileStatement whileStatement:
                    statement = whileStatement.Body;
                    continue;
                case DoWhileStatement doWhileStatement:
                    statement = doWhileStatement.Body;
                    continue;
                case ForStatement forStatement:
                    if (forStatement.Initializer is VariableDeclaration { Kind: VariableKind.Var } initVar)
                    {
                        HoistFromStatement(initVar, environment);
                    }

                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    if (forEachStatement.DeclarationKind == VariableKind.Var)
                    {
                        HoistFromBindingTarget(forEachStatement.Target, environment);
                    }

                    statement = forEachStatement.Body;
                    continue;
                case LabeledStatement labeled:
                    statement = labeled.Statement;
                    continue;
                case TryStatement tryStatement:
                    HoistVarDeclarations(tryStatement.TryBlock, environment);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        HoistVarDeclarations(catchClause.Body, environment);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        HoistVarDeclarations(finallyBlock, environment);
                    }

                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        HoistVarDeclarations(switchCase.Body, environment);
                    }

                    break;
                case FunctionDeclaration:
                case ClassDeclaration:
                case ModuleStatement:
                    break;
            }

            break;
        }
    }

    private static void HoistFromBindingTarget(BindingTarget target, JsEnvironment environment)
    {
        switch (target)
        {
            case IdentifierBinding identifier:
                environment.DefineFunctionScoped(identifier.Name, JsSymbols.Undefined, false);
                break;
            case ArrayBinding arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    if (element.Target is not null)
                    {
                        HoistFromBindingTarget(element.Target, environment);
                    }
                }

                if (arrayBinding.RestElement is not null)
                {
                    HoistFromBindingTarget(arrayBinding.RestElement, environment);
                }

                break;
            case ObjectBinding objectBinding:
                foreach (var property in objectBinding.Properties)
                {
                    HoistFromBindingTarget(property.Target, environment);
                }

                if (objectBinding.RestElement is not null)
                {
                    HoistFromBindingTarget(objectBinding.RestElement, environment);
                }

                break;
        }
    }

    private static bool InOperator(object? property, object? target)
    {
        var propertyName = ToPropertyName(property)
                           ?? throw new InvalidOperationException("Property name cannot be null.");
        return TryGetPropertyValue(target, propertyName, out _);
    }

    private static bool InstanceofOperator(object? left, object? right)
    {
        if (!TryGetPropertyValue(right, "prototype", out var prototype) || prototype is not JsObject prototypeObject)
        {
            throw new InvalidOperationException("Right-hand side of 'instanceof' is not a constructor.");
        }

        var current = left as JsObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, prototypeObject))
            {
                return true;
            }

            current = current.Prototype;
        }

        return false;
    }

    private static string GetTypeofString(object? value)
    {
        if (value is null)
        {
            return "object";
        }

        if (value is Symbol sym && ReferenceEquals(sym, JsSymbols.Undefined))
        {
            return "undefined";
        }

        if (value is TypedAstSymbol)
        {
            return "symbol";
        }

        if (value is JsBigInt)
        {
            return "bigint";
        }

        return value switch
        {
            bool => "boolean",
            double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte => "number",
            string => "string",
            JsFunction or HostFunction or TypedFunction => "function",
            _ => "object"
        };
    }

    private static void AssignBindingTarget(BindingTarget target, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        ApplyBindingTarget(target, value, environment, context, BindingMode.Assign);
    }

    private static void DefineBindingTarget(BindingTarget target, object? value, JsEnvironment environment,
        EvaluationContext context, bool isConst)
    {
        ApplyBindingTarget(target, value, environment, context,
            isConst ? BindingMode.DefineConst : BindingMode.DefineLet);
    }

    private static void DefineOrAssignVar(BindingTarget target, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        ApplyBindingTarget(target, value, environment, context, BindingMode.DefineVar);
    }

    private static void ApplyBindingTarget(BindingTarget target, object? value, JsEnvironment environment,
        EvaluationContext context, BindingMode mode, bool hasInitializer = true)
    {
        switch (target)
        {
            case IdentifierBinding identifier:
                ApplyIdentifierBinding(identifier, value, environment, mode, hasInitializer);
                break;
            case ArrayBinding arrayBinding:
                BindArrayPattern(arrayBinding, value, environment, context, mode);
                break;
            case ObjectBinding objectBinding:
                BindObjectPattern(objectBinding, value, environment, context, mode);
                break;
            default:
                throw new NotSupportedException($"Binding target '{target.GetType().Name}' is not supported.");
        }
    }

    private static void ApplyIdentifierBinding(IdentifierBinding identifier, object? value, JsEnvironment environment,
        BindingMode mode, bool hasInitializer)
    {
        switch (mode)
        {
            case BindingMode.Assign:
                environment.Assign(identifier.Name, value);
                break;
            case BindingMode.DefineLet:
                environment.Define(identifier.Name, value);
                break;
            case BindingMode.DefineConst:
                environment.Define(identifier.Name, value, isConst: true);
                break;
            case BindingMode.DefineVar:
                environment.DefineFunctionScoped(identifier.Name, value, hasInitializer);
                break;
            case BindingMode.DefineParameter:
                environment.Define(identifier.Name, value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    private static void BindArrayPattern(ArrayBinding binding, object? value, JsEnvironment environment,
        EvaluationContext context, BindingMode mode)
    {
        if (value is not JsArray array)
        {
            throw new InvalidOperationException("Cannot destructure non-array value.");
        }

        var index = 0;
        foreach (var element in binding.Elements)
        {
            if (element.Target is null)
            {
                index++;
                continue;
            }

            var elementValue = index < array.Items.Count ? array.Items[index] : JsSymbols.Undefined;
            if (IsNullOrUndefined(elementValue) && element.DefaultValue is not null)
            {
                elementValue = EvaluateExpression(element.DefaultValue, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

            ApplyBindingTarget(element.Target, elementValue, environment, context, mode);
            index++;
        }

        if (binding.RestElement is not null)
        {
            var restArray = new JsArray();
            for (; index < array.Items.Count; index++)
            {
                restArray.Push(array.Items[index]);
            }

            ApplyBindingTarget(binding.RestElement, restArray, environment, context, mode);
        }
    }

    private static void BindObjectPattern(ObjectBinding binding, object? value, JsEnvironment environment,
        EvaluationContext context, BindingMode mode)
    {
        if (value is not JsObject obj)
        {
            throw new InvalidOperationException("Cannot destructure non-object value.");
        }

        var usedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in binding.Properties)
        {
            usedKeys.Add(property.Name);
            var propertyValue = obj.TryGetProperty(property.Name, out var val) ? val : JsSymbols.Undefined;

            if (IsNullOrUndefined(propertyValue) && property.DefaultValue is not null)
            {
                propertyValue = EvaluateExpression(property.DefaultValue, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

            ApplyBindingTarget(property.Target, propertyValue, environment, context, mode);
        }

        if (binding.RestElement is not null)
        {
            var restObject = new JsObject();
            foreach (var kvp in obj)
            {
                if (!usedKeys.Contains(kvp.Key))
                {
                    restObject[kvp.Key] = kvp.Value;
                }
            }

            ApplyBindingTarget(binding.RestElement, restObject, environment, context, mode);
        }
    }

    private static bool IsNullOrUndefined(object? value)
    {
        return value is null || value is Symbol symbol && ReferenceEquals(symbol, JsSymbols.Undefined);
    }

    private enum BindingMode
    {
        Assign,
        DefineLet,
        DefineConst,
        DefineVar,
        DefineParameter
    }

    private static void RestoreSignal(EvaluationContext context, ISignal? signal)
    {
        switch (signal)
        {
            case null:
                return;
            case ReturnSignal returnSignal:
                context.SetReturn(returnSignal.Value);
                break;
            case BreakSignal breakSignal:
                context.SetBreak(breakSignal.Label);
                break;
            case ContinueSignal continueSignal:
                context.SetContinue(continueSignal.Label);
                break;
            case ThrowFlowSignal throwSignal:
                context.SetThrow(throwSignal.Value);
                break;
        }
    }

    private readonly record struct AssignmentReference(Func<object?> GetValue, Action<object?> SetValue);

    private static AssignmentReference ResolveReference(ExpressionNode expression, JsEnvironment environment,
        EvaluationContext context)
    {
        return expression switch
        {
            IdentifierExpression identifier => new AssignmentReference(
                () => environment.Get(identifier.Name),
                value => environment.Assign(identifier.Name, value)),
            MemberExpression member => ResolveMemberReference(member, environment, context),
            UnaryExpression { Operator: "++" or "--" } unary =>
                ResolveReference(unary.Operand, environment, context),
            _ => throw new NotSupportedException("Unsupported assignment target.")
        };
    }

    private static AssignmentReference ResolveMemberReference(MemberExpression member, JsEnvironment environment,
        EvaluationContext context)
    {
        var target = EvaluateExpression(member.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return new AssignmentReference(() => JsSymbols.Undefined, _ => { });
        }

        var propertyValue = EvaluateExpression(member.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return new AssignmentReference(() => JsSymbols.Undefined, _ => { });
        }

        if (target is JsArray jsArray && TryResolveArrayIndex(propertyValue, out var arrayIndex))
        {
            return new AssignmentReference(
                () => jsArray.GetElement(arrayIndex),
                newValue => jsArray.SetElement(arrayIndex, newValue));
        }

        if (target is TypedArrayBase typedArray && TryResolveArrayIndex(propertyValue, out var typedIndex))
        {
            return new AssignmentReference(
                () => typedIndex >= 0 && typedIndex < typedArray.Length
                    ? typedArray.GetElement(typedIndex)
                    : JsSymbols.Undefined,
                newValue =>
                {
                    if (typedIndex >= 0 && typedIndex < typedArray.Length)
                    {
                        typedArray.SetElement(typedIndex, newValue.ToNumber());
                    }
                });
        }

        var propertyName = ToPropertyName(propertyValue)
                           ?? throw new InvalidOperationException("Property name cannot be null.");

        return new AssignmentReference(
            () => TryGetPropertyValue(target, propertyName, out var value) ? value : JsSymbols.Undefined,
            newValue => AssignPropertyValueByName(target, propertyName, newValue));
    }

    private sealed class TypedFunction : IJsEnvironmentAwareCallable, IJsPropertyAccessor
    {
        private readonly FunctionExpression _function;
        private readonly JsEnvironment _closure;
        private readonly JsObject _properties = new();

        public TypedFunction(FunctionExpression function, JsEnvironment closure)
        {
            if (function.IsAsync || function.IsGenerator)
            {
                throw new NotSupportedException("Async and generator functions are not supported by the typed evaluator yet.");
            }

            _function = function;
            _closure = closure;

            // Functions expose a prototype object so instances created via `new` can inherit from it.
            _properties.SetProperty("prototype", new JsObject());
        }

        public JsEnvironment? CallingJsEnvironment { get; set; }

        public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
        {
            var context = new EvaluationContext();
            var description = _function.Name is { } name ? $"function {name.Name}" : "anonymous function";
            var environment = new JsEnvironment(_closure, true, _function.Body.IsStrict, description: description);

            // Bind `this`.
            environment.Define(JsSymbols.This, thisValue ?? new JsObject());

            // Named function expressions should see their name inside the body.
            if (_function.Name is { } functionName)
            {
                environment.Define(functionName, this);
            }

            HoistVarDeclarations(_function.Body, environment);

            BindParameters(arguments, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }

            var result = EvaluateBlock(_function.Body, environment, context);

            if (context.IsThrow)
            {
                var thrown = context.FlowValue;
                context.Clear();
                throw new ThrowSignal(thrown);
            }

            if (!context.IsReturn)
            {
                return JsSymbols.Undefined;
            }

            var value = context.FlowValue;
            context.ClearReturn();
            return value;

        }

        private void BindParameters(IReadOnlyList<object?> arguments, JsEnvironment environment, EvaluationContext context)
        {
            var argumentIndex = 0;

            foreach (var parameter in _function.Parameters)
            {
                if (parameter.IsRest)
                {
                    if (parameter.Pattern is not null)
                    {
                        throw new NotSupportedException("Rest parameters cannot use destructuring patterns.");
                    }

                    var restArray = new JsArray();
                    for (; argumentIndex < arguments.Count; argumentIndex++)
                    {
                        restArray.Push(arguments[argumentIndex]);
                    }

                    if (parameter.Name is null)
                    {
                        throw new InvalidOperationException("Rest parameter must have an identifier.");
                    }

                    environment.Define(parameter.Name, restArray);
                    continue;
                }

                var value = argumentIndex < arguments.Count ? arguments[argumentIndex] : JsSymbols.Undefined;
                argumentIndex++;

                if (IsNullOrUndefined(value) && parameter.DefaultValue is not null)
                {
                    value = EvaluateExpression(parameter.DefaultValue, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }

                if (parameter.Pattern is not null)
                {
                    ApplyBindingTarget(parameter.Pattern, value, environment, context, BindingMode.DefineParameter);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }

                    continue;
                }

                if (parameter.Name is null)
                {
                    throw new InvalidOperationException("Parameter must have an identifier when no pattern is provided.");
                }

                environment.Define(parameter.Name, value);
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
    }
}
