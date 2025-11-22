namespace Asynkron.JsEngine.Ast.ShapeAnalyzer;

public sealed class SingleYieldLocator
{
    public YieldExpression? FoundYield { get; private set; }

    public void VisitExpression(ExpressionNode? expression)
    {
        while (expression is not null && FoundYield is null)
        {
            switch (expression)
            {
                case YieldExpression yieldExpression:
                    FoundYield = yieldExpression;
                    return;
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
                        if (FoundYield is not null)
                        {
                            return;
                        }
                    }

                    return;
                case NewExpression @new:
                    VisitExpression(@new.Constructor);
                    foreach (var argument in @new.Arguments)
                    {
                        VisitExpression(argument);
                        if (FoundYield is not null)
                        {
                            return;
                        }
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
                        if (FoundYield is not null)
                        {
                            return;
                        }
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

                        if (FoundYield is not null)
                        {
                            return;
                        }
                    }

                    return;
                case FunctionExpression:
                case ClassExpression:
                    return;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is null)
                        {
                            continue;
                        }

                        VisitExpression(part.Expression);
                        if (FoundYield is not null)
                        {
                            return;
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
                        if (FoundYield is not null)
                        {
                            return;
                        }
                    }

                    return;
                case DestructuringAssignmentExpression destructuringAssignment:
                    VisitExpression(destructuringAssignment.Value);
                    return;
                default:
                    return;
            }
        }
    }
}
