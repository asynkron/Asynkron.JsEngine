using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Pre-pass for generator functions that can normalize complex <c>yield</c> placements
/// into a generator-friendly AST surface before IR is built. For now this acts as a
/// no-op scaffold so that future yield-lowering logic can live in a single, testable
/// place instead of being interleaved with IR code generation.
/// </summary>
internal static class GeneratorYieldLowerer
{
    public static bool TryLowerToGeneratorFriendlyAst(
        FunctionExpression function,
        out FunctionExpression lowered,
        out string? failureReason)
    {
        var lowerer = new LoweringContext();
        var loweredBody = lowerer.RewriteBlock(function.Body);

        lowered = ReferenceEquals(loweredBody, function.Body)
            ? function
            : function with { Body = loweredBody };

        failureReason = null;
        return true;
    }

    private sealed class LoweringContext
    {
        private int _resumeCounter;

        public BlockStatement RewriteBlock(BlockStatement block)
        {
            var rewritten = RewriteStatements(block.Statements, block.IsStrict);
            if (ReferenceEquals(rewritten, block.Statements))
            {
                return block;
            }

            return block with { Statements = rewritten };
        }

        private ImmutableArray<StatementNode> RewriteStatements(ImmutableArray<StatementNode> statements, bool isStrict)
        {
            if (statements.IsDefaultOrEmpty)
            {
                return statements;
            }

            var builder = ImmutableArray.CreateBuilder<StatementNode>(statements.Length);
            var changed = false;

            foreach (var statement in statements)
            {
                if (statement is BlockStatement nestedBlock)
                {
                    var rewrittenBlock = RewriteBlock(nestedBlock);
                    builder.Add(rewrittenBlock);
                    changed |= !ReferenceEquals(rewrittenBlock, nestedBlock);
                    continue;
                }

                if (TryRewriteVariableDeclaration(statement, isStrict, out var replacement))
                {
                    builder.AddRange(replacement);
                    changed = true;
                    continue;
                }

                builder.Add(statement);
            }

            return changed ? builder.ToImmutable() : statements;
        }

        private bool TryRewriteVariableDeclaration(StatementNode statement, bool isStrict,
            out ImmutableArray<StatementNode> replacement)
        {
            if (statement is not VariableDeclaration { Declarators.Length: 1 } declaration)
            {
                replacement = default;
                return false;
            }

            var declarator = declaration.Declarators[0];
            if (declarator.Target is not IdentifierBinding identifierBinding ||
                declarator.Initializer is not BinaryExpression binary ||
                binary.Left is not YieldExpression leftYield ||
                binary.Right is not YieldExpression rightYield)
            {
                replacement = default;
                return false;
            }

            if (leftYield.IsDelegated || rightYield.IsDelegated ||
                ContainsYield(leftYield.Expression) || ContainsYield(rightYield.Expression))
            {
                replacement = default;
                return false;
            }

            // Normalize a binary initializer with two simple yields into a sequence of
            // single-yield declarations so the IR builder only needs to handle the
            // simple initializer shape.
            var leftResume = CreateResumeIdentifier();
            var rightResume = CreateResumeIdentifier();

            var leftDeclarator = new VariableDeclarator(declarator.Source, leftResume,
                new YieldExpression(leftYield.Source, leftYield.Expression, false));
            var rightDeclarator = new VariableDeclarator(declarator.Source, rightResume,
                new YieldExpression(rightYield.Source, rightYield.Expression, false));

            var rewrittenInitializer = binary with
            {
                Left = new IdentifierExpression(binary.Left.Source, leftResume.Name),
                Right = new IdentifierExpression(binary.Right.Source, rightResume.Name)
            };
            var finalDeclarator = declarator with { Initializer = rewrittenInitializer };

            replacement = ImmutableArray.Create<StatementNode>(
                declaration with { Declarators = [leftDeclarator] },
                declaration with { Declarators = [rightDeclarator] },
                declaration with { Declarators = [finalDeclarator] });
            return true;
        }

        private IdentifierBinding CreateResumeIdentifier()
        {
            var symbol = Symbol.Intern($"__yield_lower_resume{_resumeCounter++}");
            return new IdentifierBinding(null, symbol);
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
                        return ContainsYield(conditional.Test) ||
                               ContainsYield(conditional.Consequent) ||
                               ContainsYield(conditional.Alternate);
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
                    case MemberExpression member:
                        expression = member.Target;
                        continue;
                    case AssignmentExpression assignment:
                        expression = assignment.Value;
                        continue;
                    case PropertyAssignmentExpression propertyAssignment:
                        return ContainsYield(propertyAssignment.Target) ||
                               ContainsYield(propertyAssignment.Property) ||
                               ContainsYield(propertyAssignment.Value);
                    case IndexAssignmentExpression indexAssignment:
                        return ContainsYield(indexAssignment.Target) ||
                               ContainsYield(indexAssignment.Index) ||
                               ContainsYield(indexAssignment.Value);
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
                        // Nested scopes handle their own yields.
                        return false;
                    default:
                        return false;
                }
            }
        }
    }
}
