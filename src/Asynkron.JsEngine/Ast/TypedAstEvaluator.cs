using System;
using System.Collections;
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
            ForEachStatement forEachStatement => EvaluateForEach(forEachStatement, environment, context),
            BreakStatement breakStatement => EvaluateBreak(breakStatement, context),
            ContinueStatement continueStatement => EvaluateContinue(continueStatement, context),
            LabeledStatement labeledStatement => EvaluateLabeled(labeledStatement, environment, context),
            TryStatement tryStatement => EvaluateTry(tryStatement, environment, context),
            SwitchStatement switchStatement => EvaluateSwitch(switchStatement, environment, context),
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

    private static object? EvaluateWhile(WhileStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        object? lastValue = JsSymbols.Undefined;
        var loopLabel = context.CurrentLabel;

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

    private static object? EvaluateDoWhile(DoWhileStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        object? lastValue = JsSymbols.Undefined;
        var loopLabel = context.CurrentLabel;

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

    private static object? EvaluateFor(ForStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var loopEnvironment = new JsEnvironment(environment, creatingExpression: null, description: "for-loop");
        var loopLabel = context.CurrentLabel;
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
        EvaluationContext context)
    {
        if (statement.Kind == ForEachKind.AwaitOf)
        {
            throw new NotSupportedException("for await...of is not yet supported by the typed evaluator.");
        }

        var iterable = EvaluateExpression(statement.Iterable, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var loopEnvironment = new JsEnvironment(environment, creatingExpression: null, description: "for-each-loop");
        var loopLabel = context.CurrentLabel;
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

            AssignLoopBinding(statement, value, loopEnvironment, environment);

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

    private static void AssignLoopBinding(ForEachStatement statement, object? value, JsEnvironment loopEnvironment,
        JsEnvironment outerEnvironment)
    {
        if (statement.DeclarationKind is null)
        {
            AssignBindingTarget(statement.Target, value, outerEnvironment);
            return;
        }

        switch (statement.DeclarationKind)
        {
            case VariableKind.Var:
                DefineOrAssignVar(statement.Target, value, loopEnvironment);
                break;
            case VariableKind.Let:
            case VariableKind.Const:
                DefineBindingTarget(statement.Target, value, loopEnvironment,
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
            _ = EvaluateBlock(statement.Finally, environment, context);
            if (context.CurrentSignal is null)
            {
                RestoreSignal(context, savedSignal);
            }
        }

        return result;
    }

    private static object? EvaluateSwitch(SwitchStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var discriminant = EvaluateExpression(statement.Discriminant, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        object? lastValue = JsSymbols.Undefined;
        var loopLabel = context.CurrentLabel;
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
            if (context.TryClearBreak(loopLabel))
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
            return EvaluateStatement(statement.Statement, environment, context);
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

        var propertyName = ToPropertyName(property)
                           ?? throw new InvalidOperationException("Property name cannot be null.");
        var value = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        AssignPropertyValue(target, propertyName, value);
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

        var propertyName = ToPropertyName(index)
                           ?? throw new InvalidOperationException("Property name cannot be null.");
        var value = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        AssignPropertyValue(target, propertyName, value);
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
                ? currentValue.ToNumber() + 1
                : currentValue.ToNumber() - 1;
            reference.SetValue(updatedValue);
            return expression.IsPrefix ? updatedValue : currentValue;
        }

        if (expression.Operator == "delete")
        {
            return EvaluateDelete(expression.Operand, environment, context);
        }

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
            "~" => ~JsNumericConversions.ToInt32(operand.ToNumber()),
            "typeof" => GetTypeofString(operand),
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
            "&" => JsNumericConversions.ToInt32(left.ToNumber()) & JsNumericConversions.ToInt32(right.ToNumber()),
            "|" => JsNumericConversions.ToInt32(left.ToNumber()) | JsNumericConversions.ToInt32(right.ToNumber()),
            "^" => JsNumericConversions.ToInt32(left.ToNumber()) ^ JsNumericConversions.ToInt32(right.ToNumber()),
            "<<" => JsNumericConversions.ToInt32(left.ToNumber()) << (JsNumericConversions.ToInt32(right.ToNumber()) & 0x1F),
            ">>" => JsNumericConversions.ToInt32(left.ToNumber()) >> (JsNumericConversions.ToInt32(right.ToNumber()) & 0x1F),
            ">>>" => (int)(JsNumericConversions.ToUInt32(left.ToNumber()) >> (int)(JsNumericConversions.ToUInt32(right.ToNumber()) & 0x1F)),
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

        try
        {
            return callable.Invoke(arguments.MoveToImmutable(), thisValue);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
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

            var propertyName = ToPropertyName(propertyValue)
                               ?? throw new InvalidOperationException("Property name cannot be null.");
            return DeletePropertyValue(target, propertyName);
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

            builder.Append(value);
        }

        return builder.ToString();
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
        if (left is string || right is string)
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

    private static string? ToPropertyName(object? value)
    {
        return value switch
        {
            null => "null",
            string s => s,
            Symbol symbol => symbol.Name,
            JsSymbol jsSymbol => $"@@symbol:{jsSymbol.GetHashCode()}",
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d when !double.IsNaN(d) && !double.IsInfinity(d) => d.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
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

    private static void AssignPropertyValue(object? target, string propertyName, object? value)
    {
        if (target is IJsPropertyAccessor accessor)
        {
            accessor.SetProperty(propertyName, value);
            return;
        }

        throw new InvalidOperationException($"Cannot assign property '{propertyName}' on value '{target}'.");
    }

    private static bool DeletePropertyValue(object? target, string propertyName)
    {
        if (target is JsObject jsObject)
        {
            return jsObject.Remove(propertyName);
        }

        return false;
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

        if (value is JsSymbol)
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
            JsFunction or HostFunction => "function",
            _ => "object"
        };
    }

    private static void AssignBindingTarget(BindingTarget target, object? value, JsEnvironment environment)
    {
        if (target is IdentifierBinding identifier)
        {
            environment.Assign(identifier.Name, value);
            return;
        }

        throw new NotSupportedException("Destructuring bindings are not yet supported by the typed evaluator.");
    }

    private static void DefineBindingTarget(BindingTarget target, object? value, JsEnvironment environment, bool isConst)
    {
        if (target is IdentifierBinding identifier)
        {
            environment.Define(identifier.Name, value, isConst);
            return;
        }

        throw new NotSupportedException("Destructuring bindings are not yet supported by the typed evaluator.");
    }

    private static void DefineOrAssignVar(BindingTarget target, object? value, JsEnvironment environment)
    {
        if (target is IdentifierBinding identifier)
        {
            environment.DefineFunctionScoped(identifier.Name, value, true);
            return;
        }

        throw new NotSupportedException("Destructuring bindings are not yet supported by the typed evaluator.");
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

        var propertyName = ToPropertyName(propertyValue)
                           ?? throw new InvalidOperationException("Property name cannot be null.");

        return new AssignmentReference(
            () => TryGetPropertyValue(target, propertyName, out var value) ? value : JsSymbols.Undefined,
            newValue => AssignPropertyValue(target, propertyName, newValue));
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
