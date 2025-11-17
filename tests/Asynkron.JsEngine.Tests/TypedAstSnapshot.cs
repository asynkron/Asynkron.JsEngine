using System.Text;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Tests;

internal static class TypedAstSnapshot
{
    public static string Create(ProgramNode program)
    {
        var builder = new StringBuilder();
        AppendProgram(program, builder);
        return builder.ToString();
    }

    private static void AppendProgram(ProgramNode program, StringBuilder builder)
    {
        builder.Append("(program");
        foreach (var statement in program.Body)
        {
            builder.Append(' ');
            AppendStatement(statement, builder);
        }

        builder.Append(')');
    }

    private static void AppendStatement(StatementNode statement, StringBuilder builder)
    {
        switch (statement)
        {
            case ExpressionStatement expressionStatement:
                builder.Append("(expr ");
                AppendExpression(expressionStatement.Expression, builder);
                builder.Append(')');
                break;
            case VariableDeclaration variableDeclaration:
                builder.Append("(var ");
                builder.Append(variableDeclaration.Kind);
                foreach (var declarator in variableDeclaration.Declarators)
                {
                    builder.Append(' ');
                    AppendVariableDeclarator(declarator, builder);
                }

                builder.Append(')');
                break;
            case ReturnStatement returnStatement:
                builder.Append("(return");
                if (returnStatement.Expression != null)
                {
                    builder.Append(' ');
                    AppendExpression(returnStatement.Expression, builder);
                }

                builder.Append(')');
                break;
            case BlockStatement blockStatement:
                builder.Append("(block");
                foreach (var inner in blockStatement.Statements)
                {
                    builder.Append(' ');
                    AppendStatement(inner, builder);
                }

                builder.Append(')');
                break;
            case FunctionDeclaration functionDeclaration:
                builder.Append("(function ");
                builder.Append(functionDeclaration.Name);
                builder.Append(' ');
                AppendFunctionExpression(functionDeclaration.Function, builder);
                builder.Append(')');
                break;
            case TryStatement tryStatement:
                builder.Append("(try ");
                AppendStatement(tryStatement.TryBlock, builder);
                if (tryStatement.Catch != null)
                {
                    builder.Append(' ');
                    builder.Append("(catch ");
                    builder.Append(tryStatement.Catch.Binding);
                    builder.Append(' ');
                    AppendStatement(tryStatement.Catch.Body, builder);
                    builder.Append(')');
                }

                if (tryStatement.Finally != null)
                {
                    builder.Append(' ');
                    builder.Append("(finally ");
                    AppendStatement(tryStatement.Finally, builder);
                    builder.Append(')');
                }

                builder.Append(')');
                break;
            case IfStatement ifStatement:
                builder.Append("(if ");
                AppendExpression(ifStatement.Condition, builder);
                builder.Append(' ');
                AppendStatement(ifStatement.Then, builder);
                if (ifStatement.Else != null)
                {
                    builder.Append(' ');
                    AppendStatement(ifStatement.Else, builder);
                }
                builder.Append(')');
                break;
            default:
                throw new NotSupportedException($"Snapshot does not handle statement '{statement.GetType().Name}'.");
        }
    }

    private static void AppendVariableDeclarator(VariableDeclarator declarator, StringBuilder builder)
    {
        builder.Append("(binding ");
        AppendBindingTarget(declarator.Target, builder);
        if (declarator.Initializer != null)
        {
            builder.Append(' ');
            AppendExpression(declarator.Initializer, builder);
        }

        builder.Append(')');
    }

    private static void AppendBindingTarget(BindingTarget target, StringBuilder builder)
    {
        switch (target)
        {
            case IdentifierBinding identifierBinding:
                builder.Append("(id-binding ");
                builder.Append(identifierBinding.Name);
                builder.Append(')');
                break;
            default:
                throw new NotSupportedException($"Snapshot does not handle binding target '{target.GetType().Name}'.");
        }
    }

    private static void AppendFunctionExpression(FunctionExpression functionExpression, StringBuilder builder)
    {
        builder.Append("(lambda");
        if (functionExpression.IsAsync)
        {
            builder.Append(" async");
        }

        if (functionExpression.IsGenerator)
        {
            builder.Append(" generator");
        }

        builder.Append(" (params");
        foreach (var parameter in functionExpression.Parameters)
        {
            builder.Append(' ');
            builder.Append(parameter.Name);
        }

        builder.Append(')');
        builder.Append(' ');
        AppendStatement(functionExpression.Body, builder);
        builder.Append(')');
    }

    private static void AppendExpression(ExpressionNode expression, StringBuilder builder)
    {
        switch (expression)
        {
            case LiteralExpression literalExpression:
                builder.Append("(literal ");
                AppendLiteralValue(literalExpression.Value, builder);
                builder.Append(')');
                break;
            case IdentifierExpression identifierExpression:
                builder.Append("(id ");
                builder.Append(identifierExpression.Name);
                builder.Append(')');
                break;
            case BinaryExpression binaryExpression:
                builder.Append("(binary ");
                builder.Append(binaryExpression.Operator);
                builder.Append(' ');
                AppendExpression(binaryExpression.Left, builder);
                builder.Append(' ');
                AppendExpression(binaryExpression.Right, builder);
                builder.Append(')');
                break;
            case CallExpression callExpression:
                builder.Append("(call ");
                AppendExpression(callExpression.Callee, builder);
                foreach (var argument in callExpression.Arguments)
                {
                    builder.Append(' ');
                    if (argument.IsSpread)
                    {
                        builder.Append("...");
                    }

                    AppendExpression(argument.Expression, builder);
                }

                builder.Append(')');
                break;
            case MemberExpression memberExpression:
                builder.Append("(member ");
                AppendExpression(memberExpression.Target, builder);
                builder.Append(' ');
                AppendExpression(memberExpression.Property, builder);
                builder.Append(')');
                break;
            case NewExpression newExpression:
                builder.Append("(new ");
                AppendExpression(newExpression.Constructor, builder);
                foreach (var argument in newExpression.Arguments)
                {
                    builder.Append(' ');
                    AppendExpression(argument, builder);
                }

                builder.Append(')');
                break;
            case FunctionExpression functionExpression:
                AppendFunctionExpression(functionExpression, builder);
                break;
            case ArrayExpression arrayExpression:
                AppendArrayExpression(arrayExpression, builder);
                break;
            case AssignmentExpression assignmentExpression:
                builder.Append("(assign ");
                builder.Append(assignmentExpression.Target);
                builder.Append(' ');
                AppendExpression(assignmentExpression.Value, builder);
                builder.Append(')');
                break;
            case UnaryExpression unaryExpression:
                builder.Append("(unary ");
                builder.Append(unaryExpression.Operator);
                builder.Append(' ');
                AppendExpression(unaryExpression.Operand, builder);
                builder.Append(')');
                break;
            default:
                throw new NotSupportedException($"Snapshot does not handle expression '{expression.GetType().Name}'.");
        }
    }

    private static void AppendArrayExpression(ArrayExpression arrayExpression, StringBuilder builder)
    {
        builder.Append("(array");
        foreach (var element in arrayExpression.Elements)
        {
            builder.Append(' ');
            if (element.Expression is null)
            {
                builder.Append("hole");
                continue;
            }

            if (element.IsSpread)
            {
                builder.Append("...");
            }

            AppendExpression(element.Expression, builder);
        }

        builder.Append(')');
    }

    private static void AppendLiteralValue(object? value, StringBuilder builder)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                break;
            case string s:
                builder.Append('"').Append(s).Append('"');
                break;
            default:
                builder.Append(value);
                break;
        }
    }
}
