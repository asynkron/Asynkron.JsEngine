using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Asynkron.JsEngine;
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

    private static object? EvaluateStatement(StatementNode statement, JsEnvironment environment, EvaluationContext context)
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
            WhileStatement whileStatement => EvaluateWhile(whileStatement, environment, context),
            DoWhileStatement doWhileStatement => EvaluateDoWhile(doWhileStatement, environment, context),
            ForStatement forStatement => EvaluateFor(forStatement, environment, context),
            BreakStatement breakStatement => EvaluateBreak(breakStatement, context),
            ContinueStatement continueStatement => EvaluateContinue(continueStatement, context),
            LabeledStatement labeledStatement => EvaluateLabeled(labeledStatement, environment, context),
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
            ? JsSymbols.Undefined
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
        Symbol? loopLabel = null)
    {
        object? result = JsSymbols.Undefined;
        while (true)
        {
            var test = EvaluateExpression(statement.Condition, environment, context);
            if (context.ShouldStopEvaluation || !IsTruthy(test))
            {
                break;
            }

            result = EvaluateStatement(statement.Body, environment, context);
            if (context.TryClearContinue(loopLabel))
            {
                continue;
            }

            if (context.TryClearBreak(loopLabel))
            {
                break;
            }

            if (context.IsReturn || context.IsThrow)
            {
                break;
            }
        }

        return result;
    }

    private static object? EvaluateDoWhile(DoWhileStatement statement, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel = null)
    {
        object? result = JsSymbols.Undefined;
        do
        {
            result = EvaluateStatement(statement.Body, environment, context);
            if (context.TryClearContinue(loopLabel))
            {
                // Evaluate the condition before continuing.
            }
            else if (context.TryClearBreak(loopLabel))
            {
                break;
            }
            else if (context.IsReturn || context.IsThrow)
            {
                break;
            }

            var test = EvaluateExpression(statement.Condition, environment, context);
            if (context.ShouldStopEvaluation || !IsTruthy(test))
            {
                break;
            }
        }
        while (true);

        return result;
    }

    private static object? EvaluateFor(ForStatement statement, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel = null)
    {
        var loopEnvironment = new JsEnvironment(environment, creatingExpression: null, description: "for loop");
        object? result = JsSymbols.Undefined;

        if (statement.Initializer is not null)
        {
            EvaluateStatement(statement.Initializer, loopEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }
        }

        while (true)
        {
            if (statement.Condition is not null)
            {
                var test = EvaluateExpression(statement.Condition, loopEnvironment, context);
                if (context.ShouldStopEvaluation || !IsTruthy(test))
                {
                    break;
                }
            }

            result = EvaluateStatement(statement.Body, loopEnvironment, context);

            if (context.TryClearContinue(loopLabel))
            {
                if (statement.Increment is not null)
                {
                    EvaluateExpression(statement.Increment, loopEnvironment, context);
                }

                continue;
            }

            if (context.TryClearBreak(loopLabel))
            {
                break;
            }

            if (context.IsReturn || context.IsThrow)
            {
                break;
            }

            if (statement.Increment is not null)
            {
                EvaluateExpression(statement.Increment, loopEnvironment, context);
            }
        }

        return result;
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
        if (declarator.Target is not IdentifierBinding identifier)
        {
            throw new NotSupportedException("Destructuring bindings are not supported by the typed evaluator yet.");
        }

        var value = declarator.Initializer is null
            ? JsSymbols.Undefined
            : EvaluateExpression(declarator.Initializer, environment, context);

        if (context.ShouldStopEvaluation)
        {
            return;
        }

        switch (kind)
        {
            case VariableKind.Var:
                environment.DefineFunctionScoped(identifier.Name, value, declarator.Initializer is not null);
                break;
            case VariableKind.Let:
                environment.Define(identifier.Name, value);
                break;
            case VariableKind.Const:
                environment.Define(identifier.Name, value, isConst: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
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
            var handledAsLoop = false;
            object? result;
            switch (statement.Statement)
            {
                case ForStatement forStatement:
                    handledAsLoop = true;
                    result = EvaluateFor(forStatement, environment, context, statement.Label);
                    break;
                case WhileStatement whileStatement:
                    handledAsLoop = true;
                    result = EvaluateWhile(whileStatement, environment, context, statement.Label);
                    break;
                case DoWhileStatement doWhileStatement:
                    handledAsLoop = true;
                    result = EvaluateDoWhile(doWhileStatement, environment, context, statement.Label);
                    break;
                default:
                    result = EvaluateStatement(statement.Statement, environment, context);
                    break;
            }

            if (context.CurrentSignal is BreakSignal)
            {
                context.TryClearBreak(statement.Label);
            }

            if (handledAsLoop && context.CurrentSignal is ContinueSignal)
            {
                context.TryClearContinue(statement.Label);
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
            MemberExpression member => EvaluateMember(member, environment, context).Value,
            NewExpression newExpression => EvaluateNew(newExpression, environment, context),
            ArrayExpression arrayExpression => EvaluateArray(arrayExpression, environment, context),
            ObjectExpression objectExpression => EvaluateObject(objectExpression, environment, context),
            PropertyAssignmentExpression propertyAssignment =>
                EvaluatePropertyAssignment(propertyAssignment, environment, context),
            IndexAssignmentExpression indexAssignment => EvaluateIndexAssignment(indexAssignment, environment, context),
            SequenceExpression sequence => EvaluateSequence(sequence, environment, context),
            ThisExpression => environment.Get(JsSymbols.This),
            TemplateLiteralExpression template => EvaluateTemplateLiteral(template, environment, context),
            TaggedTemplateExpression tagged => EvaluateTaggedTemplate(tagged, environment, context),
            UnknownExpression unknown => throw new NotSupportedException(
                $"Typed evaluator does not yet understand the '{unknown.Node.Head}' expression form."),
            _ => throw new NotSupportedException(
                $"Typed evaluator does not yet support '{expression.GetType().Name}'.")
        };
    }

    private static object? EvaluateAssignment(AssignmentExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var value = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return value;
        }

        environment.Assign(expression.Target, value);
        return value;
    }

    private static object? EvaluatePropertyAssignment(PropertyAssignmentExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var target = EvaluateExpression(expression.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var propertyName = EvaluatePropertyName(expression.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var value = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return value;
        }

        if (propertyName is null)
        {
            throw new InvalidOperationException("Property assignment requires a valid property name.");
        }

        JsEvaluator.AssignPropertyValue(target, propertyName, value);
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

        var indexValue = EvaluateExpression(expression.Index, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var propertyName = JsEvaluator.ToPropertyName(indexValue)
                            ?? throw new InvalidOperationException("Index access requires a valid property name.");

        var value = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return value;
        }

        JsEvaluator.AssignPropertyValue(target, propertyName, value);
        return value;
    }

    private static MemberLookupResult EvaluateMember(MemberExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var target = EvaluateExpression(expression.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return new MemberLookupResult(JsSymbols.Undefined, target);
        }

        if (expression.IsOptional && IsNullish(target))
        {
            return new MemberLookupResult(JsSymbols.Undefined, null);
        }

        var propertyName = EvaluatePropertyName(expression.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return new MemberLookupResult(JsSymbols.Undefined, target);
        }

        if (IsNullish(target))
        {
            throw new InvalidOperationException("Cannot access properties on null or undefined.");
        }

        if (propertyName is null)
        {
            throw new InvalidOperationException("Property access requires a valid property name.");
        }

        return JsEvaluator.TryGetPropertyValue(target, propertyName, out var value)
            ? new MemberLookupResult(value, target)
            : new MemberLookupResult(JsSymbols.Undefined, target);
    }

    private static string? EvaluatePropertyName(ExpressionNode expression, JsEnvironment environment,
        EvaluationContext context)
    {
        object? propertyValue = expression is LiteralExpression literal
            ? literal.Value
            : EvaluateExpression(expression, environment, context);

        if (context.ShouldStopEvaluation)
        {
            return null;
        }

        return JsEvaluator.ToPropertyName(propertyValue);
    }

    private static object? EvaluateSequence(SequenceExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        _ = EvaluateExpression(expression.Left, environment, context);
        return context.ShouldStopEvaluation
            ? JsSymbols.Undefined
            : EvaluateExpression(expression.Right, environment, context);
    }

    private static ImmutableArray<object?> EvaluateCallArguments(ImmutableArray<CallArgument> arguments,
        JsEnvironment environment, EvaluationContext context)
    {
        var builder = ImmutableArray.CreateBuilder<object?>();
        foreach (var argument in arguments)
        {
            if (argument.IsSpread)
            {
                var spreadValue = EvaluateExpression(argument.Expression, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return ImmutableArray<object?>.Empty;
                }

                if (spreadValue is JsArray spreadArray)
                {
                    foreach (var item in spreadArray.Items)
                    {
                        builder.Add(item);
                    }

                    continue;
                }

                throw new InvalidOperationException("Spread operator can only be applied to arrays.");
            }

            builder.Add(EvaluateExpression(argument.Expression, environment, context));
            if (context.ShouldStopEvaluation)
            {
                return ImmutableArray<object?>.Empty;
            }
        }

        return builder.ToImmutable();
    }

    private static object EvaluateArray(ArrayExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        var array = new JsArray();
        foreach (var element in expression.Elements)
        {
            if (element.IsSpread)
            {
                var spreadValue = element.Expression is null
                    ? JsSymbols.Undefined
                    : EvaluateExpression(element.Expression, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return array;
                }

                if (spreadValue is JsArray spreadArray)
                {
                    foreach (var item in spreadArray.Items)
                    {
                        array.Push(item);
                    }

                    continue;
                }

                throw new InvalidOperationException("Spread operator can only be applied to arrays.");
            }

            var value = element.Expression is null
                ? JsSymbols.Undefined
                : EvaluateExpression(element.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return array;
            }

            array.Push(value);
        }

        StandardLibrary.AddArrayMethods(array);
        return array;
    }

    private static object EvaluateObject(ObjectExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var obj = new JsObject();
        foreach (var member in expression.Members)
        {
            switch (member.Kind)
            {
                case ObjectMemberKind.Property or ObjectMemberKind.Field:
                {
                    if (member.Value is null)
                    {
                        continue;
                    }

                    var key = ResolveObjectKey(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return obj;
                    }

                    var value = EvaluateExpression(member.Value, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return obj;
                    }

                    obj.SetProperty(key, value);
                    break;
                }
                case ObjectMemberKind.Method:
                {
                    if (member.Function is null)
                    {
                        throw new InvalidOperationException("Method member requires a function expression.");
                    }

                    var key = ResolveObjectKey(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return obj;
                    }

                    var function = new TypedFunction(member.Function, environment);
                    obj.SetProperty(key, function);
                    break;
                }
                case ObjectMemberKind.Getter:
                {
                    if (member.Function is null)
                    {
                        throw new InvalidOperationException("Getter member requires a function expression.");
                    }

                    var key = ResolveObjectKey(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return obj;
                    }

                    obj.SetGetter(key, new TypedFunction(member.Function, environment));
                    break;
                }
                case ObjectMemberKind.Setter:
                {
                    if (member.Function is null)
                    {
                        throw new InvalidOperationException("Setter member requires a function expression.");
                    }

                    var key = ResolveObjectKey(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return obj;
                    }

                    obj.SetSetter(key, new TypedFunction(member.Function, environment));
                    break;
                }
                case ObjectMemberKind.Spread:
                {
                    if (member.Value is null)
                    {
                        continue;
                    }

                    var spreadValue = EvaluateExpression(member.Value, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return obj;
                    }

                    SpreadProperties(obj, spreadValue);
                    break;
                }
            }
        }

        return obj;
    }

    private static string ResolveObjectKey(ObjectMember member, JsEnvironment environment, EvaluationContext context)
    {
        if (member.IsComputed)
        {
            if (member.Key is not ExpressionNode expression)
            {
                throw new InvalidOperationException("Computed property must provide an expression for the key.");
            }

            var evaluated = EvaluateExpression(expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return string.Empty;
            }

            return JsEvaluator.ToPropertyName(evaluated) ?? string.Empty;
        }

        return JsEvaluator.ToPropertyName(member.Key) ?? string.Empty;
    }

    private static void SpreadProperties(JsObject target, object? source)
    {
        switch (source)
        {
            case JsObject jsObject:
            {
                foreach (var key in jsObject.GetOwnPropertyNames())
                {
                    if (jsObject.TryGetProperty(key, out var value))
                    {
                        target.SetProperty(key, value);
                    }
                }

                break;
            }
            case JsArray jsArray:
            {
                for (var i = 0; i < jsArray.Items.Count; i++)
                {
                    target.SetProperty(i.ToString(CultureInfo.InvariantCulture), jsArray.GetElement(i));
                }

                target.SetProperty("length", (double)jsArray.Length);
                break;
            }
            case null:
                break;
            default:
                throw new InvalidOperationException("Spread operator can only be applied to objects or arrays.");
        }
    }

    private static object EvaluateTemplateLiteral(TemplateLiteralExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var builder = new StringBuilder();
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
                break;
            }

            builder.Append(ConvertToString(value));
        }

        return builder.ToString();
    }

    private static object? EvaluateTaggedTemplate(TaggedTemplateExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var tagValue = EvaluateExpression(expression.Tag, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (tagValue is not IJsCallable callable)
        {
            throw new InvalidOperationException("Tag in tagged template must be callable.");
        }

        if (EvaluateExpression(expression.StringsArray, environment, context) is not JsArray strings)
        {
            throw new InvalidOperationException("Tagged template requires a strings array.");
        }

        if (EvaluateExpression(expression.RawStringsArray, environment, context) is not JsArray rawStrings)
        {
            throw new InvalidOperationException("Tagged template requires a raw strings array.");
        }

        var template = new JsObject();
        for (var i = 0; i < strings.Items.Count; i++)
        {
            template[i.ToString(CultureInfo.InvariantCulture)] = strings.Items[i];
        }

        template.SetProperty("length", (double)strings.Items.Count);
        template.SetProperty("raw", rawStrings);

        var argsBuilder = ImmutableArray.CreateBuilder<object?>();
        argsBuilder.Add(template);
        foreach (var expr in expression.Expressions)
        {
            argsBuilder.Add(EvaluateExpression(expr, environment, context));
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        try
        {
            return callable.Invoke(argsBuilder.ToImmutable(), null);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
    }

    private static string ConvertToString(object? value)
    {
        return value switch
        {
            null => "null",
            string s => s,
            bool b => b ? "true" : "false",
            double d => d.ToString(CultureInfo.InvariantCulture),
            IJsCallable => "function() { [native code] }",
            _ => value?.ToString() ?? string.Empty
        };
    }

    private static object? EvaluateNew(NewExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        var constructorValue = EvaluateExpression(expression.Constructor, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (constructorValue is not IJsCallable callable)
        {
            throw new InvalidOperationException(
                $"Attempted to construct with a non-callable value of type '{constructorValue?.GetType().Name ?? "null"}'.");
        }

        var instance = new JsObject();
        if (JsEvaluator.TryGetPropertyValue(constructorValue, "prototype", out var prototype) &&
            prototype is JsObject prototypeObject)
        {
            instance.SetPrototype(prototypeObject);
        }

        JsEvaluator.InitializePrivateFields(constructorValue, instance, environment, context);

        var arguments = EvaluateExpressionList(expression.Arguments, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        try
        {
            var result = callable.Invoke(arguments, instance);
            return result switch
            {
                JsArray or JsObject or JsMap or JsSet or JsWeakMap or JsWeakSet or JsArrayBuffer or JsDataView or
                    TypedArrayBase => result,
                IDictionary<string, object?> => result,
                _ => instance
            };
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
    }

    private static ImmutableArray<object?> EvaluateExpressionList(ImmutableArray<ExpressionNode> expressions,
        JsEnvironment environment, EvaluationContext context)
    {
        var builder = ImmutableArray.CreateBuilder<object?>(expressions.Length);
        foreach (var expression in expressions)
        {
            builder.Add(EvaluateExpression(expression, environment, context));
            if (context.ShouldStopEvaluation)
            {
                return builder.ToImmutable();
            }
        }

        return builder.ToImmutable();
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
        var operand = EvaluateExpression(expression.Operand, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        return expression.Operator switch
        {
            "!" => !IsTruthy(operand),
            "+" => operand.ToNumber(),
            "-" => -operand.ToNumber(),
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
            "-" => left.ToNumber() - right.ToNumber(),
            "*" => left.ToNumber() * right.ToNumber(),
            "/" => left.ToNumber() / right.ToNumber(),
            "%" => left.ToNumber() % right.ToNumber(),
            "**" => Math.Pow(left.ToNumber(), right.ToNumber()),
            "==" => LooseEquals(left, right),
            "!=" => !LooseEquals(left, right),
            "===" => StrictEquals(left, right),
            "!==" => !StrictEquals(left, right),
            "<" => left.ToNumber() < right.ToNumber(),
            "<=" => left.ToNumber() <= right.ToNumber(),
            ">" => left.ToNumber() > right.ToNumber(),
            ">=" => left.ToNumber() >= right.ToNumber(),
            _ => throw new NotSupportedException($"Operator '{expression.Operator}' is not supported yet.")
        };
    }

    private static object? EvaluateCall(CallExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        object? thisValue = null;
        object? callee;

        if (expression.Callee is MemberExpression member)
        {
            var lookup = EvaluateMember(member, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }

            callee = lookup.Value;
            thisValue = lookup.Receiver;
        }
        else
        {
            callee = EvaluateExpression(expression.Callee, environment, context);
        }

        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (expression.IsOptional && IsNullish(callee))
        {
            return JsSymbols.Undefined;
        }

        if (expression.Callee is MemberExpression memberCallee && memberCallee.IsOptional && IsNullish(callee))
        {
            return JsSymbols.Undefined;
        }

        if (callee is not IJsCallable callable)
        {
            throw new InvalidOperationException(
                $"Attempted to call a non-callable value of type '{callee?.GetType().Name ?? "null"}'.");
        }

        var arguments = EvaluateCallArguments(expression.Arguments, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (callable is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
        }

        if (callable is DebugAwareHostFunction debugFunction)
        {
            debugFunction.CurrentJsEnvironment = environment;
            debugFunction.CurrentContext = context;
        }

        try
        {
            return callable.Invoke(arguments, thisValue);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
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
        if (left is string leftString || right is string rightString)
        {
            return $"{left}{right}";
        }

        return left.ToNumber() + right.ToNumber();
    }

    private static bool LooseEquals(object? left, object? right)
    {
        if (IsNullish(left) && IsNullish(right))
        {
            return true;
        }

        if (left is double or float or int or uint or long or ulong or short or ushort or byte or sbyte ||
            right is double or float or int or uint or long or ulong or short or ushort or byte or sbyte)
        {
            return left.ToNumber() == right.ToNumber();
        }

        return Equals(left, right);
    }

    private readonly record struct MemberLookupResult(object? Value, object? Receiver);

    private static bool StrictEquals(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.GetType() != right.GetType())
        {
            return false;
        }

        return left switch
        {
            double leftDouble => leftDouble.Equals((double)right),
            float leftFloat => leftFloat.Equals((float)right),
            bool leftBool => leftBool == (bool)right,
            string leftString => leftString == (string)right,
            Symbol leftSymbol => ReferenceEquals(leftSymbol, right),
            _ => Equals(left, right)
        };
    }

    private sealed class TypedFunction : IJsEnvironmentAwareCallable
    {
        private readonly FunctionExpression _function;
        private readonly JsEnvironment _closure;

        public TypedFunction(FunctionExpression function, JsEnvironment closure)
        {
            if (function.IsAsync || function.IsGenerator)
            {
                throw new NotSupportedException("Async and generator functions are not supported by the typed evaluator yet.");
            }

            _function = function;
            _closure = closure;
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
                if (parameter.Pattern is not null)
                {
                    throw new NotSupportedException("Destructuring parameters are not supported by the typed evaluator yet.");
                }

                if (parameter.IsRest)
                {
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

                if ((value is null || value is Symbol s && ReferenceEquals(s, JsSymbols.Undefined)) &&
                    parameter.DefaultValue is not null)
                {
                    value = EvaluateExpression(parameter.DefaultValue, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }

                if (parameter.Name is null)
                {
                    throw new InvalidOperationException("Parameter must have an identifier when no pattern is provided.");
                }

                environment.Define(parameter.Name, value);
            }
        }
    }
}
