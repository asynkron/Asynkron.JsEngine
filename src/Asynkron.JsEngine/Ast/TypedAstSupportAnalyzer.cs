namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Lightweight static analysis pass that determines whether the current typed AST
/// only relies on language constructs supported by <see cref="TypedAstEvaluator"/>.
/// This allows callers to detect unsupported features before evaluation begins so
/// we can safely fall back to the legacy cons-based interpreter without executing
/// half the program twice.
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
                    case ForEachStatement forEach when forEach.Kind == ForEachKind.AwaitOf:
                        return Fail("for await...of loops are not supported by the typed evaluator yet.");
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
                    case ClassDeclaration:
                        return Fail("class declarations are not yet translated to the typed evaluator.");
                    case ModuleStatement:
                        return Fail("module import/export statements are not supported by the typed evaluator yet.");
                    case UnknownStatement unknown:
                        return Fail($"Unknown statement form '{unknown.Node.Head}'.");
                    default:
                        return Fail($"Statement '{statement.GetType().Name}' is not supported by the typed evaluator yet.");
                }

                break;
            }
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
                    case TemplateLiteralExpression template:
                        foreach (var part in template.Parts)
                        {
                            if (part.Expression is not null && !VisitExpression(part.Expression))
                            {
                                return false;
                            }
                        }

                        return true;
                    case TaggedTemplateExpression:
                        return Fail("Tagged template literals are not supported by the typed evaluator yet.");
                    case DestructuringAssignmentExpression:
                        return Fail("Destructuring assignments are not supported by the typed evaluator yet.");
                    case AwaitExpression:
                        return Fail("await expressions are not supported by the typed evaluator yet.");
                    case YieldExpression:
                        return Fail("yield expressions are not supported by the typed evaluator yet.");
                    case SuperExpression:
                        return Fail("super expressions are not supported by the typed evaluator yet.");
                    case UnknownExpression unknown:
                        return Fail($"Unknown expression form '{unknown.Node.Head}'.");
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
            if (function.IsAsync || function.IsGenerator)
            {
                return Fail("Async or generator functions are not supported by the typed evaluator yet.");
            }

            foreach (var parameter in function.Parameters)
            {
                if (parameter.Pattern is not null)
                {
                    return Fail("Destructuring parameters are not supported by the typed evaluator yet.");
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
            if (target is IdentifierBinding)
            {
                return true;
            }

            return Fail("Destructuring bindings are not supported by the typed evaluator yet.");
        }

        private bool Fail(string reason)
        {
            UnsupportedReason ??= reason;
            return false;
        }
    }
}
