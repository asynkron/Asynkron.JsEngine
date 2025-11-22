namespace Asynkron.JsEngine.Ast.ShapeAnalyzer;

public sealed class ShapeCounter(bool includeNestedFunctions)
{
    public int AwaitCount;
    public int DelegatedYieldCount;
    public int YieldCount;
    public bool YieldOperandContainsYield;

    public void VisitStatement(StatementNode? statement)
    {
        while (statement is not null)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var child in block.Statements)
                    {
                        VisitStatement(child);
                    }

                    return;
                case ExpressionStatement expressionStatement:
                    VisitExpression(expressionStatement.Expression);
                    return;
                case ReturnStatement returnStatement:
                    VisitExpression(returnStatement.Expression);
                    return;
                case ThrowStatement throwStatement:
                    VisitExpression(throwStatement.Expression);
                    return;
                case VariableDeclaration declaration:
                    foreach (var declarator in declaration.Declarators)
                    {
                        VisitExpression(declarator.Initializer);
                    }

                    return;
                case FunctionDeclaration functionDeclaration:
                    if (includeNestedFunctions)
                    {
                        VisitFunction(functionDeclaration.Function);
                    }

                    return;
                case IfStatement ifStatement:
                    VisitExpression(ifStatement.Condition);
                    VisitStatement(ifStatement.Then);
                    if (ifStatement.Else is not null)
                    {
                        VisitStatement(ifStatement.Else);
                    }

                    return;
                case WhileStatement whileStatement:
                    VisitExpression(whileStatement.Condition);
                    statement = whileStatement.Body;
                    continue;
                case DoWhileStatement doWhileStatement:
                    VisitExpression(doWhileStatement.Condition);
                    statement = doWhileStatement.Body;
                    continue;
                case ForStatement forStatement:
                    if (forStatement.Initializer is not null)
                    {
                        VisitStatement(forStatement.Initializer);
                    }

                    VisitExpression(forStatement.Condition);
                    VisitExpression(forStatement.Increment);
                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    VisitExpression(forEachStatement.Iterable);
                    statement = forEachStatement.Body;
                    continue;
                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;
                case TryStatement tryStatement:
                    VisitStatement(tryStatement.TryBlock);
                    if (tryStatement.Catch is not null)
                    {
                        VisitStatement(tryStatement.Catch.Body);
                    }

                    if (tryStatement.Finally is not null)
                    {
                        VisitStatement(tryStatement.Finally);
                    }

                    return;
                case SwitchStatement switchStatement:
                    VisitExpression(switchStatement.Discriminant);
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (switchCase.Test is not null)
                        {
                            VisitExpression(switchCase.Test);
                        }

                        VisitStatement(switchCase.Body);
                    }

                    return;
                case ClassDeclaration classDeclaration:
                    if (!includeNestedFunctions)
                    {
                        return;
                    }

                    VisitExpression(classDeclaration.Definition.Extends);
                    VisitFunction(classDeclaration.Definition.Constructor);
                    foreach (var member in classDeclaration.Definition.Members)
                    {
                        VisitFunction(member.Function);
                    }

                    foreach (var field in classDeclaration.Definition.Fields)
                    {
                        VisitExpression(field.Initializer);
                    }

                    return;
                case ModuleStatement:
                case BreakStatement:
                case ContinueStatement:
                case EmptyStatement:
                    return;
                default:
                    return;
            }
        }
    }

    public void VisitFunction(FunctionExpression function)
    {
        foreach (var parameter in function.Parameters)
        {
            if (parameter.DefaultValue is not null)
            {
                VisitExpression(parameter.DefaultValue);
            }

            if (parameter.Pattern is BindingTarget { } pattern)
            {
                VisitBinding(pattern);
            }
        }

        VisitStatement(function.Body);
    }

    public void VisitBinding(BindingTarget binding)
    {
        while (true)
        {
            switch (binding)
            {
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null)
                        {
                            VisitBinding(element.Target);
                        }

                        if (element.DefaultValue is not null)
                        {
                            VisitExpression(element.DefaultValue);
                        }
                    }

                    if (arrayBinding.RestElement is null)
                    {
                        return;
                    }

                    binding = arrayBinding.RestElement;
                    continue;

                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        VisitBinding(property.Target);
                        if (property.DefaultValue is not null)
                        {
                            VisitExpression(property.DefaultValue);
                        }
                    }

                    if (objectBinding.RestElement is null)
                    {
                        return;
                    }

                    binding = objectBinding.RestElement;
                    continue;
            }

            break;
        }
    }

    public void VisitExpression(ExpressionNode? expression)
    {
        while (expression is not null)
        {
            switch (expression)
            {
                case LiteralExpression:
                case IdentifierExpression:
                case ThisExpression:
                case SuperExpression:
                    return;
                case YieldExpression yieldExpression:
                    YieldCount++;
                    if (yieldExpression.IsDelegated)
                    {
                        DelegatedYieldCount++;
                    }

                    var before = YieldCount;
                    VisitExpression(yieldExpression.Expression);
                    if (YieldCount > before)
                    {
                        YieldOperandContainsYield = true;
                    }

                    return;
                case AwaitExpression awaitExpression:
                    AwaitCount++;
                    expression = awaitExpression.Expression;
                    continue;
                case BinaryExpression binary:
                    VisitExpression(binary.Left);
                    expression = binary.Right;
                    continue;
                case ConditionalExpression conditional:
                    VisitExpression(conditional.Test);
                    VisitExpression(conditional.Consequent);
                    expression = conditional.Alternate;
                    continue;
                case CallExpression call:
                    VisitExpression(call.Callee);
                    foreach (var argument in call.Arguments)
                    {
                        VisitExpression(argument.Expression);
                    }

                    return;
                case NewExpression @new:
                    VisitExpression(@new.Constructor);
                    foreach (var argument in @new.Arguments)
                    {
                        VisitExpression(argument);
                    }

                    return;
                case MemberExpression member:
                    VisitExpression(member.Target);
                    expression = member.Property;
                    continue;
                case AssignmentExpression assignment:
                    expression = assignment.Value;
                    continue;
                case PropertyAssignmentExpression propertyAssignment:
                    VisitExpression(propertyAssignment.Target);
                    VisitExpression(propertyAssignment.Property);
                    expression = propertyAssignment.Value;
                    continue;
                case IndexAssignmentExpression indexAssignment:
                    VisitExpression(indexAssignment.Target);
                    VisitExpression(indexAssignment.Index);
                    expression = indexAssignment.Value;
                    continue;
                case SequenceExpression sequence:
                    VisitExpression(sequence.Left);
                    expression = sequence.Right;
                    continue;
                case UnaryExpression unary:
                    expression = unary.Operand;
                    continue;
                case ArrayExpression array:
                    foreach (var element in array.Elements)
                    {
                        VisitExpression(element.Expression);
                    }

                    return;
                case ObjectExpression obj:
                    foreach (var member in obj.Members)
                    {
                        if (member.Value is not null)
                        {
                            VisitExpression(member.Value);
                        }

                        if (member.Key is ExpressionNode keyExpression)
                        {
                            VisitExpression(keyExpression);
                        }

                        if (member.Function is not null && includeNestedFunctions)
                        {
                            VisitFunction(member.Function);
                        }
                    }

                    return;
                case FunctionExpression functionExpression:
                    if (includeNestedFunctions)
                    {
                        VisitFunction(functionExpression);
                    }

                    return;
                case ClassExpression classExpression:
                    if (includeNestedFunctions)
                    {
                        VisitExpression(classExpression.Definition.Extends);
                        VisitFunction(classExpression.Definition.Constructor);
                        foreach (var member in classExpression.Definition.Members)
                        {
                            VisitFunction(member.Function);
                        }

                        foreach (var field in classExpression.Definition.Fields)
                        {
                            VisitExpression(field.Initializer);
                        }
                    }

                    return;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null)
                        {
                            VisitExpression(part.Expression);
                        }
                    }

                    return;
                case TaggedTemplateExpression taggedTemplate:
                    VisitExpression(taggedTemplate.Tag);
                    VisitExpression(taggedTemplate.StringsArray);
                    VisitExpression(taggedTemplate.RawStringsArray);
                    foreach (var expr in taggedTemplate.Expressions)
                    {
                        VisitExpression(expr);
                    }

                    return;
                case DestructuringAssignmentExpression destructuringAssignment:
                    VisitBinding(destructuringAssignment.Target);
                    expression = destructuringAssignment.Value;
                    continue;
                default:
                    return;
            }
        }
    }
}
