using System.Collections.Generic;
using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Builds generator IR for a subset of JavaScript constructs. The builder currently supports linear statement lists,
/// blocks, expression statements, variable declarations, simple returns, and top-level <c>yield</c> expressions.
/// More complex control flow (if/loops/try/yield inside expressions) is detected and reported as unsupported so the
/// engine can fall back to the legacy replay runner.
/// </summary>
internal sealed class GeneratorIrBuilder
{
    private readonly List<GeneratorInstruction> _instructions = new();

    private GeneratorIrBuilder()
    {
    }

    public static bool TryBuild(FunctionExpression function, out GeneratorPlan plan)
    {
        var builder = new GeneratorIrBuilder();
        return builder.TryBuildInternal(function, out plan);
    }

    private bool TryBuildInternal(FunctionExpression function, out GeneratorPlan plan)
    {
        // Always append an implicit "return undefined" instruction. Statement lists fall through to this index.
        var implicitReturnIndex = Append(new ReturnInstruction(null));
        if (!TryBuildStatementList(function.Body.Statements, implicitReturnIndex, out var entryIndex))
        {
            plan = default!;
            return false;
        }

        plan = new GeneratorPlan(_instructions.ToImmutableArray(), entryIndex);
        return true;
    }

    private bool TryBuildStatementList(ImmutableArray<StatementNode> statements, int nextIndex, out int entryIndex)
    {
        var currentNext = nextIndex;
        for (var i = statements.Length - 1; i >= 0; i--)
        {
            if (!TryBuildStatement(statements[i], currentNext, out currentNext))
            {
                entryIndex = -1;
                return false;
            }
        }

        entryIndex = currentNext;
        return true;
    }

    private bool TryBuildStatement(StatementNode statement, int nextIndex, out int entryIndex)
    {
        switch (statement)
        {
            case BlockStatement block:
                return TryBuildStatementList(block.Statements, nextIndex, out entryIndex);

            case EmptyStatement:
                entryIndex = nextIndex;
                return true;

            case ExpressionStatement { Expression: YieldExpression yieldExpression }:
                if (yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression))
                {
                    entryIndex = -1;
                    return false;
                }

                entryIndex = Append(new YieldInstruction(nextIndex, yieldExpression.Expression));
                return true;

            case ExpressionStatement expressionStatement:
                if (ContainsYield(expressionStatement.Expression))
                {
                    entryIndex = -1;
                    return false;
                }

                entryIndex = Append(new StatementInstruction(nextIndex, expressionStatement));
                return true;

            case VariableDeclaration declaration:
                if (DeclarationContainsYield(declaration))
                {
                    entryIndex = -1;
                    return false;
                }

                entryIndex = Append(new StatementInstruction(nextIndex, declaration));
                return true;

            case ReturnStatement returnStatement:
                if (returnStatement.Expression is not null && ContainsYield(returnStatement.Expression))
                {
                    entryIndex = -1;
                    return false;
                }

                entryIndex = Append(new ReturnInstruction(returnStatement.Expression));
                return true;

            default:
                entryIndex = -1;
                return false;
        }
    }

    private static bool DeclarationContainsYield(VariableDeclaration declaration)
    {
        foreach (var declarator in declaration.Declarators)
        {
            if (declarator.Initializer is not null && ContainsYield(declarator.Initializer))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsYield(ExpressionNode? expression)
    {
        while (true)
        {
            switch (expression)
            {
                case null:
                    return false;
                case YieldExpression:
                    return true;
                case BinaryExpression binary:
                    return ContainsYield(binary.Left) || ContainsYield(binary.Right);
                case ConditionalExpression conditional:
                    return ContainsYield(conditional.Test) || ContainsYield(conditional.Consequent) || ContainsYield(conditional.Alternate);
                case CallExpression call:
                    if (ContainsYield(call.Callee))
                    {
                        return true;
                    }

                    foreach (var argument in call.Arguments)
                    {
                        if (ContainsYield(argument.Expression))
                        {
                            return true;
                        }
                    }

                    return false;
                case NewExpression @new:
                    if (ContainsYield(@new.Constructor))
                    {
                        return true;
                    }

                    foreach (var argument in @new.Arguments)
                    {
                        if (ContainsYield(argument))
                        {
                            return true;
                        }
                    }

                    return false;
                case MemberExpression member:
                    return ContainsYield(member.Target) || ContainsYield(member.Property);
                case AssignmentExpression assignment:
                    expression = assignment.Value;
                    continue;
                case PropertyAssignmentExpression propertyAssignment:
                    return ContainsYield(propertyAssignment.Target) || ContainsYield(propertyAssignment.Property) || ContainsYield(propertyAssignment.Value);
                case IndexAssignmentExpression indexAssignment:
                    return ContainsYield(indexAssignment.Target) || ContainsYield(indexAssignment.Index) || ContainsYield(indexAssignment.Value);
                case SequenceExpression sequence:
                    return ContainsYield(sequence.Left) || ContainsYield(sequence.Right);
                case UnaryExpression unary:
                    expression = unary.Operand;
                    continue;
                case ArrayExpression array:
                    foreach (var element in array.Elements)
                    {
                        if (element.Expression is not null && ContainsYield(element.Expression))
                        {
                            return true;
                        }
                    }

                    return false;
                case ObjectExpression obj:
                    foreach (var member in obj.Members)
                    {
                        if (member.Value is not null && ContainsYield(member.Value))
                        {
                            return true;
                        }
                    }

                    return false;
                case FunctionExpression:
                case ClassExpression:
                    // Nested functions/classes can capture yield expressions that should be handled within their own scope.
                    return false;
                default:
                    return false;
            }

            break;
        }
    }

    private int Append(GeneratorInstruction instruction)
    {
        var index = _instructions.Count;
        _instructions.Add(instruction);
        return index;
    }
}
