using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Generators;

/// <summary>
/// Splits a generator body into sequential segments separated by yield expressions. Only handles
/// simple straight-line bodies where yields appear as standalone expression statements.
/// </summary>
internal sealed record SequentialGeneratorPlan(ImmutableArray<SequentialGeneratorSegment> Segments)
{
    public static bool TryBuild(FunctionExpression function, out SequentialGeneratorPlan? plan)
    {
        var builder = ImmutableArray.CreateBuilder<SequentialGeneratorSegment>();
        var statementBuffer = new List<StatementNode>();

        foreach (var statement in function.Body.Statements)
        {
            switch (statement)
            {
                case ExpressionStatement { Expression: YieldExpression yield }:
                    if (yield.IsDelegated)
                    {
                        plan = null;
                        return false;
                    }
                    builder.Add(new SequentialGeneratorSegment(
                        statementBuffer.ToImmutableArray(),
                        yield,
                        false,
                        null));
                    statementBuffer.Clear();
                    break;
                case ExpressionStatement expressionStatement when ContainsYield(expressionStatement.Expression):
                case ReturnStatement { Expression: { } expr } when ContainsYield(expr):
                    plan = null;
                    return false;
                case ReturnStatement returnStatement:
                    builder.Add(new SequentialGeneratorSegment(
                        statementBuffer.ToImmutableArray(),
                        null,
                        true,
                        returnStatement.Expression));
                    statementBuffer.Clear();
                    break;
                default:
                    if (ContainsYield(statement))
                    {
                        plan = null;
                        return false;
                    }

                    statementBuffer.Add(statement);
                    break;
            }
        }

        if (statementBuffer.Count > 0)
        {
            builder.Add(new SequentialGeneratorSegment(
                statementBuffer.ToImmutableArray(),
                null,
                true,
                null));
        }

        if (builder.Count == 0)
        {
            plan = null;
            return false;
        }

        plan = new SequentialGeneratorPlan(builder.ToImmutable());
        return true;
    }

    private static bool ContainsYield(StatementNode statement)
    {
        return statement switch
        {
            BlockStatement block => block.Statements.Any(ContainsYield),
            ExpressionStatement expressionStatement => ContainsYield(expressionStatement.Expression),
            VariableDeclaration declaration => declaration.Declarators.Any(d =>
                d.Initializer is not null && ContainsYield(d.Initializer)),
            _ => false
        };
    }

    private static bool ContainsYield(ExpressionNode expression)
    {
        return expression switch
        {
            YieldExpression => true,
            BinaryExpression binary => ContainsYield(binary.Left) || ContainsYield(binary.Right),
            ConditionalExpression conditional =>
                ContainsYield(conditional.Test) ||
                ContainsYield(conditional.Consequent) ||
                ContainsYield(conditional.Alternate),
            CallExpression call => ContainsYield(call.Callee) ||
                                   call.Arguments.Any(arg => ContainsYield(arg.Expression)),
            NewExpression @new => ContainsYield(@new.Constructor) ||
                                  @new.Arguments.Any(ContainsYield),
            MemberExpression member => ContainsYield(member.Target) || ContainsYield(member.Property),
            AssignmentExpression assignment => ContainsYield(assignment.Value),
            PropertyAssignmentExpression property =>
                ContainsYield(property.Target) || ContainsYield(property.Property) || ContainsYield(property.Value),
            IndexAssignmentExpression indexAssignment =>
                ContainsYield(indexAssignment.Target) ||
                ContainsYield(indexAssignment.Index) ||
                ContainsYield(indexAssignment.Value),
            SequenceExpression sequence => ContainsYield(sequence.Left) || ContainsYield(sequence.Right),
            UnaryExpression unary => ContainsYield(unary.Operand),
            ArrayExpression array => array.Elements.Any(e => e.Expression is not null && ContainsYield(e.Expression)),
            ObjectExpression obj => obj.Members.Any(m => m.Value is not null && ContainsYield(m.Value)),
            TaggedTemplateExpression tagged =>
                ContainsYield(tagged.Tag) ||
                ContainsYield(tagged.StringsArray) ||
                ContainsYield(tagged.RawStringsArray) ||
                tagged.Expressions.Any(ContainsYield),
            TemplateLiteralExpression template => template.Parts.Any(p => p.Expression is not null && ContainsYield(p.Expression)),
            FunctionExpression functionExpression => ContainsYield(functionExpression.Body),
            _ => false
        };
    }

    private static bool ContainsYield(BlockStatement block)
    {
        foreach (var statement in block.Statements)
        {
            if (ContainsYield(statement))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record SequentialGeneratorSegment(
    ImmutableArray<StatementNode> Statements,
    YieldExpression? YieldExpression,
    bool IsTerminal,
    ExpressionNode? ReturnExpression);
