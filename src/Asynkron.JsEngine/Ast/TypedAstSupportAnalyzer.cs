namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Lightweight static analysis pass that determines whether the current typed AST
/// only relies on language constructs supported by <see cref="TypedAstEvaluator"/>.
/// The runtime no longer uses this to decide between evaluators (the typed
/// interpreter always runs), but tooling can still query it to flag unsupported
/// constructs ahead of time.
/// </summary>
internal static class TypedAstSupportAnalyzer
{
    public static bool Supports(ProgramNode program, out string? reason)
    {
        var visitor = new SupportVisitor();
        visitor.VisitProgram(program);
        reason = visitor.UnsupportedReason;
        return reason is null;
    }

    private sealed class SupportVisitor
    {
        public string? UnsupportedReason { get; private set; }

        public void VisitProgram(ProgramNode program)
        {
            foreach (var statement in program.Body)
            {
                if (!VisitStatement(statement))
                {
                    return;
                }
            }
        }

        private bool VisitStatement(StatementNode statement)
        {
            while (true)
            {
                if (UnsupportedReason is not null)
                {
                    return false;
                }

                switch (statement)
                {
                    case BlockStatement block:
                        foreach (var child in block.Statements)
                        {
                            if (!VisitStatement(child))
                            {
                                return false;
                            }
                        }

                        return true;
                    case ExpressionStatement expressionStatement:
                        return VisitExpression(expressionStatement.Expression);
                    case ReturnStatement returnStatement:
                        return returnStatement.Expression is null || VisitExpression(returnStatement.Expression);
                    case ThrowStatement throwStatement:
                        return VisitExpression(throwStatement.Expression);
                    case VariableDeclaration declaration:
                        foreach (var declarator in declaration.Declarators)
                        {
                            if (!IsSupportedBinding(declarator.Target))
                            {
                                return false;
                            }

                            if (declarator.Initializer is not null && !VisitExpression(declarator.Initializer))
                            {
                                return false;
                            }
                        }

                        return true;
                    case FunctionDeclaration functionDeclaration:
                        return VisitFunction(functionDeclaration.Function);
                    case IfStatement ifStatement:
                        return VisitExpression(ifStatement.Condition) && VisitStatement(ifStatement.Then) && (ifStatement.Else is null || VisitStatement(ifStatement.Else));
                    case WhileStatement whileStatement:
                        return VisitExpression(whileStatement.Condition) && VisitStatement(whileStatement.Body);
                    case DoWhileStatement doWhileStatement:
                        return VisitStatement(doWhileStatement.Body) && VisitExpression(doWhileStatement.Condition);
                    case ForStatement forStatement:
                        if (forStatement.Initializer is not null && !VisitStatement(forStatement.Initializer))
                        {
                            return false;
                        }

                        if (forStatement.Condition is not null && !VisitExpression(forStatement.Condition))
                        {
                            return false;
                        }

                        if (forStatement.Increment is not null && !VisitExpression(forStatement.Increment))
                        {
                            return false;
                        }

                        statement = forStatement.Body;
                        continue;
                    case ForEachStatement forEach:
                        return IsSupportedBinding(forEach.Target) && VisitExpression(forEach.Iterable) && VisitStatement(forEach.Body);
                    case LabeledStatement labeled:
                        statement = labeled.Statement;
                        continue;
                    case TryStatement tryStatement:
                        if (!VisitBlock(tryStatement.TryBlock))
                        {
                            return false;
                        }

                        if (tryStatement.Catch is not null && !VisitCatch(tryStatement.Catch))
                        {
                            return false;
                        }

                        if (tryStatement.Finally is not null && !VisitBlock(tryStatement.Finally))
                        {
                            return false;
                        }

                        return true;
                    case SwitchStatement switchStatement:
                        if (!VisitExpression(switchStatement.Discriminant))
                        {
                            return false;
                        }

                        foreach (var switchCase in switchStatement.Cases)
                        {
                            if (switchCase.Test is not null && !VisitExpression(switchCase.Test))
                            {
                                return false;
                            }

                            if (!VisitBlock(switchCase.Body))
                            {
                                return false;
                            }
                        }

                        return true;
                    case BreakStatement:
                    case ContinueStatement:
                    case EmptyStatement:
                        return true;
                    case ClassDeclaration classDeclaration:
                        return VisitClass(classDeclaration);
                    case ModuleStatement:
                        return Fail("module import/export statements are not supported by the typed evaluator yet.");
                    default:
                        return Fail($"Statement '{statement.GetType().Name}' is not supported by the typed evaluator yet.");
                }

                break;
            }
        }

        private bool VisitClass(ClassDeclaration classDeclaration)
        {
            return VisitClassDefinition(classDeclaration.Definition);
        }

        private bool VisitClassDefinition(ClassDefinition definition)
        {
            if (definition.Extends is { } extends && !VisitExpression(extends))
            {
                return false;
            }

            if (!VisitFunction(definition.Constructor))
            {
                return false;
            }

            foreach (var member in definition.Members)
            {
                if (!VisitFunction(member.Function))
                {
                    return false;
                }
            }

            foreach (var field in definition.Fields)
            {
                if (field.Initializer is not null && !VisitExpression(field.Initializer))
                {
                    return false;
                }
            }

            return true;
        }

        private bool VisitBlock(BlockStatement block)
        {
            foreach (var statement in block.Statements)
            {
                if (!VisitStatement(statement))
                {
                    return false;
                }
            }

            return true;
        }

        private bool VisitCatch(CatchClause clause)
        {
            return VisitBlock(clause.Body);
        }

        private bool VisitExpression(ExpressionNode expression)
        {
            while (true)
            {
                if (UnsupportedReason is not null)
                {
                    return false;
                }

                switch (expression)
                {
                    case LiteralExpression:
                    case IdentifierExpression:
                    case ThisExpression:
                        return true;
                    case BinaryExpression binary:
                        return VisitExpression(binary.Left) && VisitExpression(binary.Right);
                    case UnaryExpression unary:
                        expression = unary.Operand;
                        continue;
                    case ConditionalExpression conditional:
                        return VisitExpression(conditional.Test) && VisitExpression(conditional.Consequent) && VisitExpression(conditional.Alternate);
                    case CallExpression call:
                        if (!VisitExpression(call.Callee))
                        {
                            return false;
                        }

                        foreach (var argument in call.Arguments)
                        {
                            if (!VisitExpression(argument.Expression))
                            {
                                return false;
                            }
                        }

                        return true;
                    case FunctionExpression function:
                        return VisitFunction(function);
                    case AssignmentExpression assignment:
                        expression = assignment.Value;
                        continue;
                    case PropertyAssignmentExpression propertyAssignment:
                        return VisitExpression(propertyAssignment.Target) && VisitExpression(propertyAssignment.Property) && VisitExpression(propertyAssignment.Value);
                    case IndexAssignmentExpression indexAssignment:
                        return VisitExpression(indexAssignment.Target) && VisitExpression(indexAssignment.Index) && VisitExpression(indexAssignment.Value);
                    case SequenceExpression sequence:
                        return VisitExpression(sequence.Left) && VisitExpression(sequence.Right);
                    case MemberExpression member:
                        return VisitExpression(member.Target) && VisitExpression(member.Property);
                    case NewExpression newExpression:
                        if (!VisitExpression(newExpression.Constructor))
                        {
                            return false;
                        }

                        foreach (var argument in newExpression.Arguments)
                        {
                            if (!VisitExpression(argument))
                            {
                                return false;
                            }
                        }

                        return true;
                    case ArrayExpression arrayExpression:
                        foreach (var element in arrayExpression.Elements)
                        {
                            if (element.Expression is not null && !VisitExpression(element.Expression))
                            {
                                return false;
                            }
                        }

                        return true;
                    case ObjectExpression objectExpression:
                        foreach (var member in objectExpression.Members)
                        {
                            if (!VisitObjectMember(member))
                            {
                                return false;
                            }
                        }

                        return true;
                    case ClassExpression classExpression:
                        return VisitClassDefinition(classExpression.Definition);
                    case TemplateLiteralExpression template:
                        foreach (var part in template.Parts)
                        {
                            if (part.Expression is not null && !VisitExpression(part.Expression))
                            {
                                return false;
                            }
                        }

                        return true;
                    case TaggedTemplateExpression taggedTemplate:
                        if (!VisitExpression(taggedTemplate.Tag) ||
                            !VisitExpression(taggedTemplate.StringsArray) ||
                            !VisitExpression(taggedTemplate.RawStringsArray))
                        {
                            return false;
                        }

                        foreach (var expr in taggedTemplate.Expressions)
                        {
                            if (!VisitExpression(expr))
                            {
                                return false;
                            }
                        }

                        return true;
                    case DestructuringAssignmentExpression destructuringAssignment:
                        return IsSupportedBinding(destructuringAssignment.Target) &&
                               VisitExpression(destructuringAssignment.Value);
                    case AwaitExpression:
                        return Fail("await expressions are not supported by the typed evaluator yet.");
                    case YieldExpression yieldExpression:
                        if (yieldExpression.IsDelegated)
                        {
                            return Fail("Delegated yield expressions are not supported by the typed evaluator yet.");
                        }

                        return VisitExpression(yieldExpression.Expression);
                    case SuperExpression:
                        return Fail("super expressions are not supported by the typed evaluator yet.");
                    default:
                        return Fail($"Expression '{expression.GetType().Name}' is not supported by the typed evaluator yet.");
                }

                break;
            }
        }

        private bool VisitObjectMember(ObjectMember member)
        {
            if (member.Kind == ObjectMemberKind.Unknown)
            {
                return Fail("Object literal member kind is not recognised by the typed evaluator.");
            }

            if (member.Value is not null && !VisitExpression(member.Value))
            {
                return false;
            }

            if (member.Function is not null && !VisitFunction(member.Function))
            {
                return false;
            }

            if (member.Key is ExpressionNode keyExpression && !VisitExpression(keyExpression))
            {
                return false;
            }

            return true;
        }

        private bool VisitFunction(FunctionExpression function)
        {
            if (function.IsAsync)
            {
                return Fail("Async functions are not supported by the typed evaluator yet.");
            }

            foreach (var parameter in function.Parameters)
            {
                if (parameter.Pattern is not null && !IsSupportedBinding(parameter.Pattern))
                {
                    return false;
                }

                if (parameter.DefaultValue is not null && !VisitExpression(parameter.DefaultValue))
                {
                    return false;
                }
            }

            return VisitBlock(function.Body);
        }

        private bool IsSupportedBinding(BindingTarget target)
        {
            switch (target)
            {
                case IdentifierBinding:
                    return true;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null && !IsSupportedBinding(element.Target))
                        {
                            return false;
                        }

                        if (element.DefaultValue is not null && !VisitExpression(element.DefaultValue))
                        {
                            return false;
                        }
                    }

                    if (arrayBinding.RestElement is not null && !IsSupportedBinding(arrayBinding.RestElement))
                    {
                        return false;
                    }

                    return true;
                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        if (!IsSupportedBinding(property.Target))
                        {
                            return false;
                        }

                        if (property.DefaultValue is not null && !VisitExpression(property.DefaultValue))
                        {
                            return false;
                        }
                    }

                    if (objectBinding.RestElement is not null && !IsSupportedBinding(objectBinding.RestElement))
                    {
                        return false;
                    }

                    return true;
                default:
                    return Fail("Binding target type is not supported by the typed evaluator.");
            }
        }

        private bool Fail(string reason)
        {
            UnsupportedReason ??= reason;
            return false;
        }
    }
}
