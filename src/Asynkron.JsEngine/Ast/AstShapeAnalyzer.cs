using System.Collections.Immutable;

namespace Asynkron.JsEngine.Ast;

internal static class AstShapeAnalyzer
{
    internal readonly record struct ShapeSummary(
        int YieldCount,
        int DelegatedYieldCount,
        int AwaitCount,
        bool YieldOperandContainsYield)
    {
        public bool HasYield => YieldCount > 0;
        public bool HasAwait => AwaitCount > 0;
    }

    public static ShapeSummary AnalyzeExpression(ExpressionNode? expression, bool includeNestedFunctions = false)
    {
        var counter = new ShapeCounter(includeNestedFunctions);
        counter.VisitExpression(expression);
        return new ShapeSummary(
            counter.YieldCount,
            counter.DelegatedYieldCount,
            counter.AwaitCount,
            counter.YieldOperandContainsYield);
    }

    public static ShapeSummary AnalyzeStatement(StatementNode statement, bool includeNestedFunctions = false)
    {
        var counter = new ShapeCounter(includeNestedFunctions);
        counter.VisitStatement(statement);
        return new ShapeSummary(
            counter.YieldCount,
            counter.DelegatedYieldCount,
            counter.AwaitCount,
            counter.YieldOperandContainsYield);
    }

    public static bool ContainsYield(ExpressionNode? expression, bool includeNestedFunctions = false) =>
        AnalyzeExpression(expression, includeNestedFunctions).HasYield;

    public static bool ContainsAwait(ExpressionNode? expression, bool includeNestedFunctions = false) =>
        AnalyzeExpression(expression, includeNestedFunctions).HasAwait;

    public static bool StatementContainsYield(StatementNode statement, bool includeNestedFunctions = false) =>
        AnalyzeStatement(statement, includeNestedFunctions).HasYield;

    public static bool StatementContainsAwait(StatementNode statement, bool includeNestedFunctions = false) =>
        AnalyzeStatement(statement, includeNestedFunctions).HasAwait;

    public static bool TryFindSingleYield(ExpressionNode expression, out YieldExpression yieldExpression)
    {
        var summary = AnalyzeExpression(expression);
        if (summary.YieldCount != 1)
        {
            yieldExpression = null!;
            return false;
        }

        var locator = new SingleYieldLocator();
        locator.VisitExpression(expression);
        yieldExpression = locator.FoundYield!;
        return yieldExpression is not null;
    }

    public static bool TryRewriteSingleYield(
        ExpressionNode expression,
        Symbol replacementSymbol,
        out YieldExpression yieldExpression,
        out ExpressionNode rewritten)
    {
        var summary = AnalyzeExpression(expression);
        if (summary.YieldCount != 1)
        {
            yieldExpression = null!;
            rewritten = expression;
            return false;
        }

        var rewriter = new SingleYieldRewriter(replacementSymbol);
        rewritten = rewriter.Rewrite(expression);
        yieldExpression = rewriter.FoundYield!;
        return yieldExpression is not null;
    }

    private sealed class ShapeCounter
    {
        public int YieldCount;
        public int DelegatedYieldCount;
        public int AwaitCount;
        public bool YieldOperandContainsYield;

        private readonly bool _includeNestedFunctions;

        public ShapeCounter(bool includeNestedFunctions)
        {
            _includeNestedFunctions = includeNestedFunctions;
        }

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
                        if (_includeNestedFunctions)
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
                        if (_includeNestedFunctions)
                        {
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

                    if (arrayBinding.RestElement is not null)
                    {
                        VisitBinding(arrayBinding.RestElement);
                    }

                    return;
                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        VisitBinding(property.Target);
                        if (property.DefaultValue is not null)
                        {
                            VisitExpression(property.DefaultValue);
                        }
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        VisitBinding(objectBinding.RestElement);
                    }

                    return;
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

                            if (member.Function is not null && _includeNestedFunctions)
                            {
                                VisitFunction(member.Function);
                            }
                        }
                        return;
                    case FunctionExpression functionExpression:
                        if (_includeNestedFunctions)
                        {
                            VisitFunction(functionExpression);
                        }

                        return;
                    case ClassExpression classExpression:
                        if (_includeNestedFunctions)
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

    private sealed class SingleYieldLocator
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
                            if (part.Expression is not null)
                            {
                                VisitExpression(part.Expression);
                                if (FoundYield is not null)
                                {
                                    return;
                                }
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

    private sealed class SingleYieldRewriter
    {
        private readonly Symbol _replacementSymbol;
        public YieldExpression? FoundYield { get; private set; }

        public SingleYieldRewriter(Symbol replacementSymbol)
        {
            _replacementSymbol = replacementSymbol;
        }

        public ExpressionNode Rewrite(ExpressionNode expression)
        {
            return expression switch
            {
                YieldExpression yieldExpression => RewriteYield(yieldExpression),
                BinaryExpression binary => binary with
                {
                    Left = Rewrite(binary.Left),
                    Right = Rewrite(binary.Right)
                },
                ConditionalExpression conditional => conditional with
                {
                    Test = Rewrite(conditional.Test),
                    Consequent = Rewrite(conditional.Consequent),
                    Alternate = Rewrite(conditional.Alternate)
                },
                CallExpression call => call with
                {
                    Callee = Rewrite(call.Callee),
                    Arguments = RewriteArguments(call.Arguments)
                },
                NewExpression @new => @new with
                {
                    Constructor = Rewrite(@new.Constructor),
                    Arguments = RewriteExpressions(@new.Arguments)
                },
                MemberExpression member => member with
                {
                    Target = Rewrite(member.Target),
                    Property = Rewrite(member.Property)
                },
                AssignmentExpression assignment => assignment with { Value = Rewrite(assignment.Value) },
                PropertyAssignmentExpression propertyAssignment => propertyAssignment with
                {
                    Target = Rewrite(propertyAssignment.Target),
                    Property = Rewrite(propertyAssignment.Property),
                    Value = Rewrite(propertyAssignment.Value)
                },
                IndexAssignmentExpression indexAssignment => indexAssignment with
                {
                    Target = Rewrite(indexAssignment.Target),
                    Index = Rewrite(indexAssignment.Index),
                    Value = Rewrite(indexAssignment.Value)
                },
                SequenceExpression sequence => sequence with
                {
                    Left = Rewrite(sequence.Left),
                    Right = Rewrite(sequence.Right)
                },
                UnaryExpression unary => unary with { Operand = Rewrite(unary.Operand) },
                ArrayExpression array => array with { Elements = RewriteArrayElements(array.Elements) },
                ObjectExpression obj => obj with { Members = RewriteObjectMembers(obj.Members) },
                TemplateLiteralExpression template => template with { Parts = RewriteTemplateParts(template.Parts) },
                TaggedTemplateExpression taggedTemplate => taggedTemplate with
                {
                    Tag = Rewrite(taggedTemplate.Tag),
                    StringsArray = Rewrite(taggedTemplate.StringsArray),
                    RawStringsArray = Rewrite(taggedTemplate.RawStringsArray),
                    Expressions = RewriteExpressions(taggedTemplate.Expressions)
                },
                DestructuringAssignmentExpression destructuringAssignment => destructuringAssignment with
                {
                    Value = Rewrite(destructuringAssignment.Value)
                },
                _ => expression
            };
        }

        private ExpressionNode RewriteYield(YieldExpression yieldExpression)
        {
            FoundYield ??= yieldExpression;
            return new IdentifierExpression(yieldExpression.Source, _replacementSymbol);
        }

        private ImmutableArray<CallArgument> RewriteArguments(ImmutableArray<CallArgument> arguments)
        {
            if (arguments.IsDefaultOrEmpty)
            {
                return arguments;
            }

            var builder = ImmutableArray.CreateBuilder<CallArgument>(arguments.Length);
            foreach (var argument in arguments)
            {
                builder.Add(argument with { Expression = Rewrite(argument.Expression) });
            }

            return builder.ToImmutable();
        }

        private ImmutableArray<ExpressionNode> RewriteExpressions(ImmutableArray<ExpressionNode> expressions)
        {
            if (expressions.IsDefaultOrEmpty)
            {
                return expressions;
            }

            var builder = ImmutableArray.CreateBuilder<ExpressionNode>(expressions.Length);
            foreach (var expr in expressions)
            {
                builder.Add(Rewrite(expr));
            }

            return builder.ToImmutable();
        }

        private ImmutableArray<ArrayElement> RewriteArrayElements(ImmutableArray<ArrayElement> elements)
        {
            if (elements.IsDefaultOrEmpty)
            {
                return elements;
            }

            var builder = ImmutableArray.CreateBuilder<ArrayElement>(elements.Length);
            foreach (var element in elements)
            {
                builder.Add(element.Expression is null
                    ? element
                    : element with { Expression = Rewrite(element.Expression) });
            }

            return builder.ToImmutable();
        }

        private ImmutableArray<ObjectMember> RewriteObjectMembers(ImmutableArray<ObjectMember> members)
        {
            if (members.IsDefaultOrEmpty)
            {
                return members;
            }

            var builder = ImmutableArray.CreateBuilder<ObjectMember>(members.Length);
            foreach (var member in members)
            {
                builder.Add(member with
                {
                    Value = member.Value is null ? null : Rewrite(member.Value),
                    Function = member.Function,
                    Key = member.Key is ExpressionNode keyExpr ? Rewrite(keyExpr) : member.Key
                });
            }

            return builder.ToImmutable();
        }

        private ImmutableArray<TemplatePart> RewriteTemplateParts(ImmutableArray<TemplatePart> parts)
        {
            if (parts.IsDefaultOrEmpty)
            {
                return parts;
            }

            var builder = ImmutableArray.CreateBuilder<TemplatePart>(parts.Length);
            foreach (var part in parts)
            {
                builder.Add(part.Expression is null
                    ? part
                    : part with { Expression = Rewrite(part.Expression) });
            }

            return builder.ToImmutable();
        }
    }
}
