using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine;

internal static class PrivateNameValidator
{
    internal static string? FindInvalidPrivateName(
        ImmutableArray<StatementNode> statements,
        ImmutableArray<PrivateNameScope>? availableScopes)
    {
        foreach (var statement in statements)
        {
            var invalid = FindInvalidPrivateNameInStatement(statement, availableScopes);
            if (invalid is not null)
            {
                return invalid;
            }
        }

        return null;
    }

    private static string? FindInvalidPrivateNameInStatement(
        StatementNode statement,
        ImmutableArray<PrivateNameScope>? availableScopes)
    {
        while (true)
        {
            switch (statement)
            {
                case ExpressionStatement expressionStatement:
                    return FindInvalidPrivateNameInExpression(expressionStatement.Expression, availableScopes);

                case BlockStatement block:
                    return FindInvalidPrivateName(block.Statements, availableScopes);

                case IfStatement ifStatement:
                    var thenResult = FindInvalidPrivateNameInStatement(ifStatement.Then, availableScopes);
                    if (thenResult is not null)
                    {
                        return thenResult;
                    }

                    if (ifStatement.Else is not null)
                    {
                        statement = ifStatement.Else;
                        continue;
                    }

                    return null;

                case WhileStatement whileStatement:
                    var whileCondition = FindInvalidPrivateNameInExpression(whileStatement.Condition, availableScopes);
                    if (whileCondition is not null)
                    {
                        return whileCondition;
                    }

                    statement = whileStatement.Body;
                    continue;

                case DoWhileStatement doWhileStatement:
                    var doBody = FindInvalidPrivateNameInStatement(doWhileStatement.Body, availableScopes);
                    return doBody ?? FindInvalidPrivateNameInExpression(doWhileStatement.Condition, availableScopes);

                case ForStatement forStatement:
                    if (forStatement.Initializer is ExpressionStatement initExpr)
                    {
                        var initResult = FindInvalidPrivateNameInExpression(initExpr.Expression, availableScopes);
                        if (initResult is not null)
                        {
                            return initResult;
                        }
                    }
                    else if (forStatement.Initializer is VariableDeclaration initVar)
                    {
                        var initVarResult = FindInvalidPrivateNameInStatement(initVar, availableScopes);
                        if (initVarResult is not null)
                        {
                            return initVarResult;
                        }
                    }

                    if (forStatement.Condition is not null)
                    {
                        var condResult = FindInvalidPrivateNameInExpression(forStatement.Condition, availableScopes);
                        if (condResult is not null)
                        {
                            return condResult;
                        }
                    }

                    if (forStatement.Increment is not null)
                    {
                        var incResult = FindInvalidPrivateNameInExpression(forStatement.Increment, availableScopes);
                        if (incResult is not null)
                        {
                            return incResult;
                        }
                    }

                    statement = forStatement.Body;
                    continue;

                case ForEachStatement forEachStatement:
                    var iterableResult = FindInvalidPrivateNameInExpression(forEachStatement.Iterable, availableScopes);
                    if (iterableResult is not null)
                    {
                        return iterableResult;
                    }

                    statement = forEachStatement.Body;
                    continue;

                case SwitchStatement switchStatement:
                    var switchExpr = FindInvalidPrivateNameInExpression(switchStatement.Discriminant, availableScopes);
                    if (switchExpr is not null)
                    {
                        return switchExpr;
                    }

                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (switchCase.Test is not null)
                        {
                            var testResult = FindInvalidPrivateNameInExpression(switchCase.Test, availableScopes);
                            if (testResult is not null)
                            {
                                return testResult;
                            }
                        }

                        var caseBody = FindInvalidPrivateNameInStatement(switchCase.Body, availableScopes);
                        if (caseBody is not null)
                        {
                            return caseBody;
                        }
                    }

                    return null;

                case TryStatement tryStatement:
                    var tryBody = FindInvalidPrivateNameInStatement(tryStatement.TryBlock, availableScopes);
                    if (tryBody is not null)
                    {
                        return tryBody;
                    }

                    if (tryStatement.Catch is { Body: not null } catchClause)
                    {
                        var catchBody = FindInvalidPrivateNameInStatement(catchClause.Body, availableScopes);
                        if (catchBody is not null)
                        {
                            return catchBody;
                        }
                    }

                    if (tryStatement.Finally is null)
                    {
                        return null;
                    }

                    statement = tryStatement.Finally;
                    continue;

                case WithStatement withStatement:
                    var withObj = FindInvalidPrivateNameInExpression(withStatement.Object, availableScopes);
                    if (withObj is not null)
                    {
                        return withObj;
                    }

                    statement = withStatement.Body;
                    continue;

                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;

                case ReturnStatement { Expression: not null } returnStatement:
                    return FindInvalidPrivateNameInExpression(returnStatement.Expression, availableScopes);

                case ThrowStatement throwStatement:
                    return FindInvalidPrivateNameInExpression(throwStatement.Expression, availableScopes);

                case VariableDeclaration varDecl:
                    foreach (var declarator in varDecl.Declarators)
                    {
                        if (declarator.Initializer is not null)
                        {
                            var initResult =
                                FindInvalidPrivateNameInExpression(declarator.Initializer, availableScopes);
                            if (initResult is not null)
                            {
                                return initResult;
                            }
                        }
                    }

                    return null;

                case FunctionDeclaration functionDeclaration:
                    return FindInvalidPrivateNameInFunction(functionDeclaration.Function, availableScopes);

                case ClassDeclaration classDeclaration:
                    if (classDeclaration.Definition.Extends is not null)
                    {
                        var heritage =
                            FindInvalidPrivateNameInExpression(classDeclaration.Definition.Extends, availableScopes);
                        if (heritage is not null)
                        {
                            return heritage;
                        }
                    }

                    foreach (var member in classDeclaration.Definition.Members)
                    {
                        if (member is { IsComputed: true, ComputedName: { } computedKey })
                        {
                            var keyResult = FindInvalidPrivateNameInExpression(computedKey, availableScopes);
                            if (keyResult is not null)
                            {
                                return keyResult;
                            }
                        }
                    }

                    return null;

                default:
                    return null;
            }
        }
    }

    private static string? FindInvalidPrivateNameInExpression(
        ExpressionNode expression,
        ImmutableArray<PrivateNameScope>? availableScopes)
    {
        while (true)
        {
            switch (expression)
            {
                case MemberExpression memberExpression:
                    var targetResult = FindInvalidPrivateNameInExpression(memberExpression.Target, availableScopes);
                    if (targetResult is not null)
                    {
                        return targetResult;
                    }

                    if (memberExpression is
                        { IsComputed: false, Property: LiteralExpression { Value.IsString: true } propLit })
                    {
                        var propName = propLit.Value.AsString()!;
                        if (propName.IsPrivateName())
                        {
                            return IsPrivateNameValid(propName, availableScopes) ? null : propName;
                        }
                    }

                    expression = memberExpression.Property;
                    continue;

                case PrivateIdentifierExpression privateId:
                    var privateName = "#" + privateId.Name;
                    return IsPrivateNameValid(privateName, availableScopes) ? null : privateName;

                case BinaryExpression binary:
                    var leftResult = FindInvalidPrivateNameInExpression(binary.Left, availableScopes);
                    if (leftResult is not null)
                    {
                        return leftResult;
                    }

                    expression = binary.Right;
                    continue;

                case UnaryExpression unary:
                    expression = unary.Operand;
                    continue;

                case ConditionalExpression conditional:
                    var testResult = FindInvalidPrivateNameInExpression(conditional.Test, availableScopes);
                    if (testResult is not null)
                    {
                        return testResult;
                    }

                    var consequent = FindInvalidPrivateNameInExpression(conditional.Consequent, availableScopes);
                    if (consequent is not null)
                    {
                        return consequent;
                    }

                    expression = conditional.Alternate;
                    continue;

                case CallExpression call:
                    var calleeResult = FindInvalidPrivateNameInExpression(call.Callee, availableScopes);
                    if (calleeResult is not null)
                    {
                        return calleeResult;
                    }

                    foreach (var argument in call.Arguments)
                    {
                        var argResult = FindInvalidPrivateNameInExpression(argument.Expression, availableScopes);
                        if (argResult is not null)
                        {
                            return argResult;
                        }
                    }

                    return null;

                case NewExpression newExpression:
                    var ctorResult = FindInvalidPrivateNameInExpression(newExpression.Constructor, availableScopes);
                    if (ctorResult is not null)
                    {
                        return ctorResult;
                    }

                    foreach (var argument in newExpression.Arguments)
                    {
                        var argResult = FindInvalidPrivateNameInExpression(argument.Expression, availableScopes);
                        if (argResult is not null)
                        {
                            return argResult;
                        }
                    }

                    return null;

                case AssignmentExpression assignment:
                    expression = assignment.Value;
                    continue;

                case PropertyAssignmentExpression propertyAssignment:
                    var paTarget = FindInvalidPrivateNameInExpression(propertyAssignment.Target, availableScopes);
                    if (paTarget is not null)
                    {
                        return paTarget;
                    }

                    var paProp = FindInvalidPrivateNameInExpression(propertyAssignment.Property, availableScopes);
                    if (paProp is not null)
                    {
                        return paProp;
                    }

                    expression = propertyAssignment.Value;
                    continue;

                case IndexAssignmentExpression indexAssignment:
                    var iaTarget = FindInvalidPrivateNameInExpression(indexAssignment.Target, availableScopes);
                    if (iaTarget is not null)
                    {
                        return iaTarget;
                    }

                    var iaIndex = FindInvalidPrivateNameInExpression(indexAssignment.Index, availableScopes);
                    if (iaIndex is not null)
                    {
                        return iaIndex;
                    }

                    expression = indexAssignment.Value;
                    continue;

                case SequenceExpression sequence:
                    var seqLeft = FindInvalidPrivateNameInExpression(sequence.Left, availableScopes);
                    if (seqLeft is not null)
                    {
                        return seqLeft;
                    }

                    expression = sequence.Right;
                    continue;

                case DestructuringAssignmentExpression destructuring:
                    expression = destructuring.Value;
                    continue;

                case ArrayExpression arrayExpression:
                    foreach (var element in arrayExpression.Elements)
                    {
                        if (element.Expression is not null)
                        {
                            var elemResult = FindInvalidPrivateNameInExpression(element.Expression, availableScopes);
                            if (elemResult is not null)
                            {
                                return elemResult;
                            }
                        }
                    }

                    return null;

                case ObjectExpression objectExpression:
                    foreach (var member in objectExpression.Members)
                    {
                        if (member is { IsComputed: true, Key: ExpressionNode computedKey })
                        {
                            var keyResult = FindInvalidPrivateNameInExpression(computedKey, availableScopes);
                            if (keyResult is not null)
                            {
                                return keyResult;
                            }
                        }

                        if (member.Value is not null)
                        {
                            var valueResult = FindInvalidPrivateNameInExpression(member.Value, availableScopes);
                            if (valueResult is not null)
                            {
                                return valueResult;
                            }
                        }

                        if (member.Function is not null)
                        {
                            var funcResult = FindInvalidPrivateNameInFunction(member.Function, availableScopes);
                            if (funcResult is not null)
                            {
                                return funcResult;
                            }
                        }
                    }

                    return null;

                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null)
                        {
                            var partResult = FindInvalidPrivateNameInExpression(part.Expression, availableScopes);
                            if (partResult is not null)
                            {
                                return partResult;
                            }
                        }
                    }

                    return null;

                case TaggedTemplateExpression tagged:
                    var tagResult = FindInvalidPrivateNameInExpression(tagged.Tag, availableScopes);
                    if (tagResult is not null)
                    {
                        return tagResult;
                    }

                    foreach (var expr in tagged.Expressions)
                    {
                        var exprResult = FindInvalidPrivateNameInExpression(expr, availableScopes);
                        if (exprResult is not null)
                        {
                            return exprResult;
                        }
                    }

                    return null;

                case YieldExpression { Expression: not null } yieldExpression:
                    expression = yieldExpression.Expression;
                    continue;

                case AwaitExpression awaitExpression:
                    expression = awaitExpression.Expression;
                    continue;

                case FunctionExpression functionExpression:
                    return FindInvalidPrivateNameInFunction(functionExpression, availableScopes);

                case ClassExpression classExpression:
                    if (classExpression.Definition.Extends is not null)
                    {
                        var heritage =
                            FindInvalidPrivateNameInExpression(classExpression.Definition.Extends, availableScopes);
                        if (heritage is not null)
                        {
                            return heritage;
                        }
                    }

                    foreach (var member in classExpression.Definition.Members)
                    {
                        if (member is { IsComputed: true, ComputedName: { } computedKey })
                        {
                            var keyResult = FindInvalidPrivateNameInExpression(computedKey, availableScopes);
                            if (keyResult is not null)
                            {
                                return keyResult;
                            }
                        }
                    }

                    return null;

                case LiteralExpression:
                case IdentifierExpression:
                case ThisExpression:
                case SuperExpression:
                case NewTargetExpression:
                    return null;

                default:
                    return null;
            }
        }
    }

    private static string? FindInvalidPrivateNameInFunction(
        FunctionExpression function,
        ImmutableArray<PrivateNameScope>? availableScopes)
    {
        foreach (var param in function.Parameters)
        {
            if (param.DefaultValue is not null)
            {
                var defaultResult = FindInvalidPrivateNameInExpression(param.DefaultValue, availableScopes);
                if (defaultResult is not null)
                {
                    return defaultResult;
                }
            }
        }

        return FindInvalidPrivateName(function.Body.Statements, availableScopes);
    }

    private static bool IsPrivateNameValid(
        string privateName,
        ImmutableArray<PrivateNameScope>? availableScopes)
    {
        if (!availableScopes.HasValue || availableScopes.Value.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var scope in availableScopes.Value)
        {
            if (scope.TryGetKey(privateName, out _))
            {
                return true;
            }
        }

        return false;
    }
}
