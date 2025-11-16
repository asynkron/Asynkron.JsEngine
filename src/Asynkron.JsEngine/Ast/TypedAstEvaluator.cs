using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
            SequenceExpression sequence => EvaluateSequence(sequence, environment, context),
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
        var value = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return value;
        }

        environment.Assign(expression.Target, value);
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
        var callee = EvaluateExpression(expression.Callee, environment, context);
        if (context.ShouldStopEvaluation)
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
                throw new NotSupportedException("Spread arguments are not yet supported by the typed evaluator.");
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
            return callable.Invoke(arguments.MoveToImmutable(), thisValue: null);
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
