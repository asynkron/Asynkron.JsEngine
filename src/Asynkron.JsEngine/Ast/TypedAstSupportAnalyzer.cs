using System.Collections.Immutable;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Scans a typed program node to determine if the typed evaluator can execute it.
/// When unsupported constructs are detected the analyzer reports a descriptive
/// reason that can be surfaced to logs or metrics.
/// </summary>
internal static class TypedAstSupportAnalyzer
{
    public static bool Supports(ProgramNode program, out string reason)
    {
        if (program is null)
        {
            reason = "Program node is null.";
            return false;
        }

        var visitor = new SupportVisitor();
        visitor.VisitProgram(program);
        reason = visitor.Reason ?? string.Empty;
        return visitor.IsSupported;
    }

    private sealed class SupportVisitor
    {
        private string? _reason;

        public bool IsSupported => _reason is null;

        public string? Reason => _reason;

        public void VisitProgram(ProgramNode program)
        {
            foreach (var statement in program.Body)
            {
                VisitStatement(statement);
                if (!IsSupported)
                {
                    break;
                }
            }
        }

        private void VisitStatement(StatementNode statement)
        {
            if (!IsSupported)
            {
                return;
            }

            switch (statement)
            {
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        VisitStatement(inner);
                        if (!IsSupported)
                        {
                            return;
                        }
                    }

                    break;
                case ExpressionStatement expressionStatement:
                    VisitExpression(expressionStatement.Expression);
                    break;
                case ReturnStatement returnStatement when returnStatement.Expression is not null:
                    VisitExpression(returnStatement.Expression);
                    break;
                case ThrowStatement throwStatement:
                    VisitExpression(throwStatement.Expression);
                    break;
                case VariableDeclaration declaration:
                    foreach (var declarator in declaration.Declarators)
                    {
                        VisitBindingTarget(declarator.Target);
                        if (declarator.Initializer is not null)
                        {
                            VisitExpression(declarator.Initializer);
                        }
                        if (!IsSupported)
                        {
                            return;
                        }
                    }

                    break;
                case FunctionDeclaration functionDeclaration:
                    VisitFunction(functionDeclaration.Function,
                        $"function declaration '{functionDeclaration.Name.Name}'");
                    break;
                case IfStatement ifStatement:
                    VisitExpression(ifStatement.Condition);
                    VisitStatement(ifStatement.Then);
                    if (ifStatement.Else is not null)
                    {
                        VisitStatement(ifStatement.Else);
                    }

                    break;
                case WhileStatement whileStatement:
                    VisitExpression(whileStatement.Condition);
                    VisitStatement(whileStatement.Body);
                    break;
                case DoWhileStatement doWhileStatement:
                    VisitStatement(doWhileStatement.Body);
                    VisitExpression(doWhileStatement.Condition);
                    break;
                case ForStatement forStatement:
                    if (forStatement.Initializer is not null)
                    {
                        VisitStatement(forStatement.Initializer);
                    }

                    if (forStatement.Condition is not null)
                    {
                        VisitExpression(forStatement.Condition);
                    }

                    if (forStatement.Increment is not null)
                    {
                        VisitExpression(forStatement.Increment);
                    }

                    VisitStatement(forStatement.Body);
                    break;
                case ForEachStatement forEachStatement:
                    VisitBindingTarget(forEachStatement.Target);
                    VisitExpression(forEachStatement.Iterable);
                    VisitStatement(forEachStatement.Body);
                    break;
                case BreakStatement:
                case ContinueStatement:
                case EmptyStatement:
                    break;
                case LabeledStatement labeledStatement:
                    VisitStatement(labeledStatement.Statement);
                    break;
                case TryStatement tryStatement:
                    VisitBlock(tryStatement.TryBlock);
                    if (tryStatement.Catch is not null)
                    {
                        VisitBlock(tryStatement.Catch.Body);
                    }

                    if (tryStatement.Finally is not null)
                    {
                        VisitBlock(tryStatement.Finally);
                    }

                    break;
                case SwitchStatement switchStatement:
                    VisitExpression(switchStatement.Discriminant);
                    foreach (var @case in switchStatement.Cases)
                    {
                        if (@case.Test is not null)
                        {
                            VisitExpression(@case.Test);
                        }

                        VisitBlock(@case.Body);
                        if (!IsSupported)
                        {
                            return;
                        }
                    }

                    break;
                case ClassDeclaration classDeclaration:
                    VisitClassDefinition(classDeclaration.Definition);
                    break;
                case ModuleStatement:
                    Fail("Module statements are not supported by the typed evaluator yet.");
                    break;
                case UnknownStatement unknownStatement:
                    var statementHead = unknownStatement.Node.Head switch
                    {
                        Symbol symbol => symbol.Name,
                        _ => unknownStatement.Node.Head?.ToString() ?? "unknown"
                    };
                    Fail($"Typed evaluator does not yet understand the '{statementHead}' statement form.");
                    break;
                default:
                    Fail($"Typed evaluator does not yet support '{statement.GetType().Name}'.");
                    break;
            }
        }

        private void VisitBlock(BlockStatement block)
        {
            foreach (var statement in block.Statements)
            {
                VisitStatement(statement);
                if (!IsSupported)
                {
                    return;
                }
            }
        }

        private void VisitExpression(ExpressionNode? expression)
        {
            if (!IsSupported || expression is null)
            {
                return;
            }

            switch (expression)
            {
                case LiteralExpression:
                case IdentifierExpression:
                case ThisExpression:
                case SuperExpression:
                    break;
                case BinaryExpression binaryExpression:
                    VisitExpression(binaryExpression.Left);
                    VisitExpression(binaryExpression.Right);
                    break;
                case UnaryExpression unaryExpression:
                    VisitExpression(unaryExpression.Operand);
                    break;
                case ConditionalExpression conditionalExpression:
                    VisitExpression(conditionalExpression.Test);
                    VisitExpression(conditionalExpression.Consequent);
                    VisitExpression(conditionalExpression.Alternate);
                    break;
                case CallExpression callExpression:
                    VisitExpression(callExpression.Callee);
                    foreach (var argument in callExpression.Arguments)
                    {
                        VisitExpression(argument.Expression);
                    }

                    break;
                case FunctionExpression functionExpression:
                    VisitFunction(functionExpression, "function expression");
                    break;
                case AssignmentExpression assignmentExpression:
                    VisitExpression(assignmentExpression.Value);
                    break;
                case DestructuringAssignmentExpression destructuringAssignment:
                    VisitBindingTarget(destructuringAssignment.Target);
                    VisitExpression(destructuringAssignment.Value);
                    break;
                case PropertyAssignmentExpression propertyAssignment:
                    VisitExpression(propertyAssignment.Target);
                    VisitExpression(propertyAssignment.Property);
                    VisitExpression(propertyAssignment.Value);
                    break;
                case IndexAssignmentExpression indexAssignment:
                    VisitExpression(indexAssignment.Target);
                    VisitExpression(indexAssignment.Index);
                    VisitExpression(indexAssignment.Value);
                    break;
                case SequenceExpression sequenceExpression:
                    VisitExpression(sequenceExpression.Left);
                    VisitExpression(sequenceExpression.Right);
                    break;
                case MemberExpression memberExpression:
                    VisitExpression(memberExpression.Target);
                    VisitExpression(memberExpression.Property);
                    break;
                case NewExpression newExpression:
                    VisitExpression(newExpression.Constructor);
                    foreach (var argument in newExpression.Arguments)
                    {
                        VisitExpression(argument);
                    }

                    break;
                case ArrayExpression arrayExpression:
                    foreach (var element in arrayExpression.Elements)
                    {
                        VisitExpression(element.Expression);
                    }

                    break;
                case ObjectExpression objectExpression:
                    VisitObjectMembers(objectExpression.Members);
                    break;
                case TemplateLiteralExpression templateLiteral:
                    foreach (var part in templateLiteral.Parts)
                    {
                        if (part.Expression is not null)
                        {
                            VisitExpression(part.Expression);
                        }
                    }

                    break;
                case TaggedTemplateExpression taggedTemplate:
                    VisitExpression(taggedTemplate.Tag);
                    VisitExpression(taggedTemplate.StringsArray);
                    VisitExpression(taggedTemplate.RawStringsArray);
                    foreach (var inner in taggedTemplate.Expressions)
                    {
                        VisitExpression(inner);
                    }

                    break;
                case YieldExpression yieldExpression when yieldExpression.IsDelegated:
                    Fail("Delegated yield expressions are not supported by the typed evaluator yet.");
                    break;
                case YieldExpression yieldExpression:
                    if (yieldExpression.Expression is not null)
                    {
                        VisitExpression(yieldExpression.Expression);
                    }

                    break;
                case AwaitExpression:
                    Fail("Await expressions are not supported by the typed evaluator yet.");
                    break;
                case UnknownExpression unknownExpression:
                    var expressionHead = unknownExpression.Node.Head switch
                    {
                        Symbol symbol => symbol.Name,
                        _ => unknownExpression.Node.Head?.ToString() ?? "unknown"
                    };
                    Fail($"Typed evaluator does not yet understand the '{expressionHead}' expression form.");
                    break;
                default:
                    Fail($"Typed evaluator does not yet support '{expression.GetType().Name}'.");
                    break;
            }
        }

        private void VisitObjectMembers(ImmutableArray<ObjectMember> members)
        {
            foreach (var member in members)
            {
                if (member.IsComputed && member.Key is ExpressionNode keyExpression)
                {
                    VisitExpression(keyExpression);
                }

                if (!IsSupported)
                {
                    return;
                }

                switch (member.Kind)
                {
                    case ObjectMemberKind.Property:
                    case ObjectMemberKind.Field:
                    case ObjectMemberKind.Spread:
                        if (member.Value is not null)
                        {
                            VisitExpression(member.Value);
                        }

                        break;
                    case ObjectMemberKind.Method:
                    case ObjectMemberKind.Getter:
                    case ObjectMemberKind.Setter:
                        if (member.Function is not null)
                        {
                            VisitFunction(member.Function,
                                $"object member '{DescribeObjectMember(member)}'");
                        }

                        break;
                    case ObjectMemberKind.Unknown:
                        Fail("Object literal contains unsupported member kind.");
                        return;
                    default:
                        Fail($"Object literal member '{member.Kind}' is not supported yet.");
                        return;
                }

                if (!IsSupported)
                {
                    return;
                }
            }
        }

        private void VisitClassDefinition(ClassDefinition definition)
        {
            if (definition.Extends is not null)
            {
                VisitExpression(definition.Extends);
            }

            VisitFunction(definition.Constructor, "class constructor");
            foreach (var member in definition.Members)
            {
                VisitFunction(member.Function, $"class member '{member.Name}'");
                if (!IsSupported)
                {
                    return;
                }
            }

            foreach (var field in definition.Fields)
            {
                if (field.Initializer is not null)
                {
                    VisitExpression(field.Initializer);
                }

                if (!IsSupported)
                {
                    return;
                }
            }
        }

        private void VisitFunction(FunctionExpression function, string description)
        {
            if (!IsSupported)
            {
                return;
            }

            if (function.IsAsync)
            {
                Fail($"Async functions are not supported ({description}).");
                return;
            }

            foreach (var parameter in function.Parameters)
            {
                if (parameter.Pattern is not null)
                {
                    VisitBindingTarget(parameter.Pattern);
                }

                if (parameter.DefaultValue is not null)
                {
                    VisitExpression(parameter.DefaultValue);
                }

                if (!IsSupported)
                {
                    return;
                }

                if (parameter.IsRest && parameter.Pattern is not null)
                {
                    Fail("Rest parameters cannot use destructuring patterns.");
                    return;
                }
            }

            VisitBlock(function.Body);
        }

        private void VisitBindingTarget(BindingTarget target)
        {
            if (!IsSupported)
            {
                return;
            }

            switch (target)
            {
                case IdentifierBinding:
                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        VisitArrayBindingElement(element);
                        if (!IsSupported)
                        {
                            return;
                        }
                    }

                    if (arrayBinding.RestElement is not null)
                    {
                        VisitBindingTarget(arrayBinding.RestElement);
                    }

                    break;
                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        VisitObjectBindingProperty(property);
                        if (!IsSupported)
                        {
                            return;
                        }
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        VisitBindingTarget(objectBinding.RestElement);
                    }

                    break;
                default:
                    Fail($"Binding target '{target.GetType().Name}' is not supported.");
                    break;
            }
        }

        private void VisitArrayBindingElement(ArrayBindingElement element)
        {
            if (element.Target is not null)
            {
                VisitBindingTarget(element.Target);
            }

            if (element.DefaultValue is not null)
            {
                VisitExpression(element.DefaultValue);
            }
        }

        private void VisitObjectBindingProperty(ObjectBindingProperty property)
        {
            VisitBindingTarget(property.Target);
            if (property.DefaultValue is not null)
            {
                VisitExpression(property.DefaultValue);
            }
        }

        private static string DescribeObjectMember(ObjectMember member)
        {
            return member.IsComputed
                ? "[computed]"
                : member.Key?.ToString() ?? "(unknown)";
        }

        private void Fail(string message)
        {
            _reason ??= message;
        }
    }
}
