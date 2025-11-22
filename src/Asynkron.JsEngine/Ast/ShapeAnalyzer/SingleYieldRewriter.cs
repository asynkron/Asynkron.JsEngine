using System.Collections.Immutable;

namespace Asynkron.JsEngine.Ast.ShapeAnalyzer;

public sealed class SingleYieldRewriter(Symbol replacementSymbol)
{
    public YieldExpression? FoundYield { get; private set; }

    public ExpressionNode Rewrite(ExpressionNode expression)
    {
        return expression switch
        {
            YieldExpression yieldExpression => RewriteYield(yieldExpression),
            BinaryExpression binary => binary with { Left = Rewrite(binary.Left), Right = Rewrite(binary.Right) },
            ConditionalExpression conditional => conditional with
            {
                Test = Rewrite(conditional.Test),
                Consequent = Rewrite(conditional.Consequent),
                Alternate = Rewrite(conditional.Alternate)
            },
            CallExpression call => call with
            {
                Callee = Rewrite(call.Callee), Arguments = RewriteArguments(call.Arguments)
            },
            NewExpression @new => @new with
            {
                Constructor = Rewrite(@new.Constructor), Arguments = RewriteExpressions(@new.Arguments)
            },
            MemberExpression member => member with
            {
                Target = Rewrite(member.Target), Property = Rewrite(member.Property)
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
                Left = Rewrite(sequence.Left), Right = Rewrite(sequence.Right)
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
        return new IdentifierExpression(yieldExpression.Source, replacementSymbol);
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
