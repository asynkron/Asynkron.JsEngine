using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Pre-pass for generator functions that can normalize complex <c>yield</c> placements
///     into a generator-friendly AST surface before IR is built. For now this acts as a
///     no-op scaffold so that future yield-lowering logic can live in a single, testable
///     place instead of being interleaved with IR code generation.
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

                if (TryRewriteConditionalWithYield(statement, isStrict, out var conditionalRewrite))
                {
                    builder.AddRange(conditionalRewrite);
                    changed = true;
                    continue;
                }

                if (TryRewriteForWithYield(statement, isStrict, out var forRewrite))
                {
                    builder.AddRange(forRewrite);
                    changed = true;
                    continue;
                }

                if (TryRewriteReturnWithYield(statement, out var returnRewrite))
                {
                    builder.AddRange(returnRewrite);
                    changed = true;
                    continue;
                }

                if (TryRewriteYieldingAssignment(statement, out var rewrittenAssignment))
                {
                    builder.AddRange(rewrittenAssignment);
                    changed = true;
                    continue;
                }

                if (TryRewriteYieldingDeclaration(statement, out var declarationRewrite))
                {
                    builder.AddRange(declarationRewrite);
                    changed = true;
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

        private bool TryRewriteReturnWithYield(StatementNode statement,
            out ImmutableArray<StatementNode> replacement)
        {
            if (statement is not ReturnStatement { Expression: YieldExpression yieldExpression })
            {
                replacement = default;
                return false;
            }

            if (AstShapeAnalyzer.ContainsYield(yieldExpression.Expression))
            {
                replacement = default;
                return false;
            }

            var resumeIdentifier = CreateResumeIdentifier();
            var declareResume = new VariableDeclaration(statement.Source, VariableKind.Let,
                [new VariableDeclarator(statement.Source, resumeIdentifier, null)]);
            var assignResume = new ExpressionStatement(yieldExpression.Source,
                new AssignmentExpression(yieldExpression.Source, resumeIdentifier.Name,
                    new YieldExpression(yieldExpression.Source, yieldExpression.Expression,
                        yieldExpression.IsDelegated)));
            var loweredReturn = new ReturnStatement(statement.Source,
                new IdentifierExpression(yieldExpression.Source, resumeIdentifier.Name));

            replacement = [declareResume, assignResume, loweredReturn];
            return true;
        }

        private bool TryRewriteYieldingDeclaration(StatementNode statement,
            out ImmutableArray<StatementNode> replacement)
        {
            if (statement is not VariableDeclaration { Declarators.Length: 1 } declaration ||
                declaration.Declarators[0] is not { } declarator ||
                declarator.Initializer is not YieldExpression yieldExpression)
            {
                replacement = default;
                return false;
            }

            if (AstShapeAnalyzer.ContainsYield(yieldExpression.Expression))
            {
                replacement = default;
                return false;
            }

            var resumeIdentifier = CreateResumeIdentifier();
            var rewrittenDeclarator = declarator with
            {
                Initializer = new IdentifierExpression(yieldExpression.Source, resumeIdentifier.Name)
            };
            var rewrittenDeclaration = declaration with { Declarators = [rewrittenDeclarator] };

            replacement =
            [
                new VariableDeclaration(declaration.Source, VariableKind.Let,
                    [new VariableDeclarator(yieldExpression.Source, resumeIdentifier, null)]),
                new ExpressionStatement(yieldExpression.Source,
                    new AssignmentExpression(yieldExpression.Source, resumeIdentifier.Name,
                        new YieldExpression(yieldExpression.Source, yieldExpression.Expression,
                            yieldExpression.IsDelegated))),
                rewrittenDeclaration
            ];
            return true;
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
                AstShapeAnalyzer.ContainsYield(leftYield.Expression) ||
                AstShapeAnalyzer.ContainsYield(rightYield.Expression))
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

            replacement =
            [
                declaration with { Declarators = [leftDeclarator], Kind = VariableKind.Let },
                declaration with { Declarators = [rightDeclarator], Kind = VariableKind.Let },
                declaration with { Declarators = [finalDeclarator] }
            ];
            return true;
        }

        private bool TryRewriteYieldingAssignment(StatementNode statement,
            out ImmutableArray<StatementNode> replacement)
        {
            if (statement is not ExpressionStatement
                {
                    Expression: AssignmentExpression assignment
                } expressionStatement ||
                assignment.Value is not YieldExpression yieldExpression)
            {
                replacement = default;
                return false;
            }

            if (yieldExpression.IsDelegated || AstShapeAnalyzer.ContainsYield(yieldExpression.Expression))
            {
                replacement = default;
                return false;
            }

            var resumeIdentifier = CreateResumeIdentifier();
            var rewrittenAssignment = assignment with
            {
                Value = new IdentifierExpression(yieldExpression.Source, resumeIdentifier.Name)
            };

            var rewrittenStatement = expressionStatement with { Expression = rewrittenAssignment };

            replacement =
            [
                new VariableDeclaration(
                    expressionStatement.Source,
                    VariableKind.Let,
                    [new VariableDeclarator(expressionStatement.Source, resumeIdentifier, null)]),
                new ExpressionStatement(yieldExpression.Source,
                    new AssignmentExpression(yieldExpression.Source, resumeIdentifier.Name,
                        new YieldExpression(yieldExpression.Source, yieldExpression.Expression,
                            yieldExpression.IsDelegated))),
                rewrittenStatement
            ];
            return true;
        }

        private bool TryRewriteConditionalWithYield(StatementNode statement, bool isStrict,
            out ImmutableArray<StatementNode> replacement)
        {
            replacement = default;

            switch (statement)
            {
                case IfStatement ifStatement:
                {
                    var resumeIdentifier = CreateResumeIdentifier();
                    if (!AstShapeAnalyzer.TryRewriteSingleYield(ifStatement.Condition, resumeIdentifier.Name,
                            out var yieldExpression, out var rewrittenCondition))
                    {
                        return false;
                    }

                    if (yieldExpression.IsDelegated || AstShapeAnalyzer.ContainsYield(yieldExpression.Expression))
                    {
                        return false;
                    }

                    var rewrittenThen = RewriteEmbedded(ifStatement.Then, isStrict);
                    var rewrittenElse = ifStatement.Else is null
                        ? null
                        : RewriteEmbedded(ifStatement.Else, isStrict);

                    var loweredIf = ifStatement with
                    {
                        Condition = rewrittenCondition, Then = rewrittenThen, Else = rewrittenElse
                    };

                    var declareResume = new VariableDeclaration(yieldExpression.Source, VariableKind.Let,
                        [new VariableDeclarator(yieldExpression.Source, resumeIdentifier, null)]);
                    var assignResume = new ExpressionStatement(yieldExpression.Source,
                        new AssignmentExpression(yieldExpression.Source, resumeIdentifier.Name,
                            new YieldExpression(yieldExpression.Source, yieldExpression.Expression,
                                yieldExpression.IsDelegated)));

                    replacement =
                    [
                        declareResume,
                        assignResume,
                        loweredIf
                    ];
                    return true;
                }

                case WhileStatement whileStatement:
                {
                    var resumeIdentifier = CreateResumeIdentifier();
                    if (!AstShapeAnalyzer.TryRewriteSingleYield(whileStatement.Condition, resumeIdentifier.Name,
                            out var yieldExpression, out var rewrittenCondition))
                    {
                        return false;
                    }

                    if (yieldExpression.IsDelegated || AstShapeAnalyzer.ContainsYield(yieldExpression.Expression))
                    {
                        return false;
                    }

                    if (!LoopNormalizer.TryNormalize(whileStatement, isStrict, out var plan, out _))
                    {
                        replacement = default;
                        return false;
                    }

                    replacement = BuildYieldedLoop(resumeIdentifier, yieldExpression, rewrittenCondition, plan,
                        isStrict);
                    return true;
                }

                case DoWhileStatement doWhileStatement:
                {
                    var resumeIdentifier = CreateResumeIdentifier();
                    if (!AstShapeAnalyzer.TryRewriteSingleYield(doWhileStatement.Condition, resumeIdentifier.Name,
                            out var yieldExpression, out var rewrittenCondition))
                    {
                        return false;
                    }

                    if (yieldExpression.IsDelegated || AstShapeAnalyzer.ContainsYield(yieldExpression.Expression))
                    {
                        return false;
                    }

                    if (!LoopNormalizer.TryNormalize(doWhileStatement, isStrict, out var plan, out _))
                    {
                        replacement = default;
                        return false;
                    }

                    replacement = BuildYieldedLoop(resumeIdentifier, yieldExpression, rewrittenCondition, plan,
                        isStrict);

                    return true;
                }

                default:
                    return false;
            }
        }

        private bool TryRewriteForWithYield(StatementNode statement, bool isStrict,
            out ImmutableArray<StatementNode> replacement)
        {
            if (statement is not ForStatement forStatement)
            {
                replacement = default;
                return false;
            }

            replacement = default;

            YieldExpression? conditionYield = null;
            ExpressionNode? rewrittenCondition = null;
            IdentifierBinding? conditionResumeIdentifier = null;
            var conditionHasYield = false;

            if (forStatement.Condition is not null)
            {
                if (AstShapeAnalyzer.TryFindSingleYield(forStatement.Condition, out conditionYield))
                {
                    conditionHasYield = true;
                    conditionResumeIdentifier = CreateResumeIdentifier();
                    if (!AstShapeAnalyzer.TryRewriteSingleYield(forStatement.Condition, conditionResumeIdentifier.Name,
                            out _, out rewrittenCondition))
                    {
                        return false;
                    }

                    if (conditionYield.IsDelegated || AstShapeAnalyzer.ContainsYield(conditionYield.Expression))
                    {
                        return false;
                    }
                }
                else if (AstShapeAnalyzer.ContainsYield(forStatement.Condition))
                {
                    return false;
                }
            }

            IdentifierBinding? incrementResumeIdentifier = null;
            IdentifierBinding? incrementResumeLeftIdentifier = null;
            IdentifierBinding? incrementResumeRightIdentifier = null;
            YieldExpression? incrementYield = null;
            ExpressionNode? rewrittenIncrement = null;
            YieldExpression? incrementYieldLeft = null;
            YieldExpression? incrementYieldRight = null;
            BinaryExpression? incrementBinary = null;
            Symbol? incrementAssignmentTarget = null;
            var incrementHasYield = false;
            var incrementHasTwoYields = false;

            if (forStatement.Increment is not null)
            {
                if (AstShapeAnalyzer.TryFindSingleYield(forStatement.Increment, out incrementYield))
                {
                    incrementResumeIdentifier = CreateResumeIdentifier();
                    incrementHasYield = AstShapeAnalyzer.TryRewriteSingleYield(forStatement.Increment,
                        incrementResumeIdentifier.Name, out _, out rewrittenIncrement);
                    if (!incrementHasYield)
                    {
                        return false;
                    }

                    if (incrementHasYield && incrementYield is not null &&
                        (incrementYield.IsDelegated || AstShapeAnalyzer.ContainsYield(incrementYield.Expression)))
                    {
                        return false;
                    }
                }

                if (!incrementHasYield &&
                    TryRewriteIncrementWithTwoYields(forStatement.Increment,
                        out incrementYieldLeft, out incrementYieldRight, out incrementBinary,
                        out incrementAssignmentTarget))
                {
                    incrementHasTwoYields = true;
                }

                if (AstShapeAnalyzer.ContainsYield(forStatement.Increment) && !incrementHasYield &&
                    !incrementHasTwoYields)
                {
                    return false;
                }
            }

            if (!conditionHasYield && !incrementHasYield && !incrementHasTwoYields)
            {
                return false;
            }

            var statements = ImmutableArray.CreateBuilder<StatementNode>();

            if (forStatement.Initializer is not null)
            {
                var rewrittenInitializer = RewriteStatements(
                    [forStatement.Initializer], isStrict);
                statements.AddRange(rewrittenInitializer);
            }

            if (conditionHasYield && conditionYield is not null)
            {
                statements.Add(new VariableDeclaration(conditionYield.Source, VariableKind.Let,
                    [new VariableDeclarator(conditionYield.Source, conditionResumeIdentifier, null)]));
            }

            if (incrementHasYield && incrementYield is not null)
            {
                statements.Add(new VariableDeclaration(incrementYield.Source, VariableKind.Let,
                    [new VariableDeclarator(incrementYield.Source, incrementResumeIdentifier, null)]));
            }
            else if (incrementHasTwoYields && incrementYieldLeft is not null && incrementYieldRight is not null)
            {
                incrementResumeLeftIdentifier = CreateResumeIdentifier();
                incrementResumeRightIdentifier = CreateResumeIdentifier();

                statements.Add(new VariableDeclaration(incrementYieldLeft.Source, VariableKind.Let,
                    [new VariableDeclarator(incrementYieldLeft.Source, incrementResumeLeftIdentifier, null)]));
                statements.Add(new VariableDeclaration(incrementYieldRight.Source, VariableKind.Let,
                    [new VariableDeclarator(incrementYieldRight.Source, incrementResumeRightIdentifier, null)]));
            }

            var loopStatements = ImmutableArray.CreateBuilder<StatementNode>();

            if (conditionHasYield && conditionYield is not null && conditionResumeIdentifier is not null)
            {
                loopStatements.Add(new ExpressionStatement(conditionYield.Source,
                    new AssignmentExpression(conditionYield.Source, conditionResumeIdentifier.Name,
                        new YieldExpression(conditionYield.Source, conditionYield.Expression,
                            conditionYield.IsDelegated))));

                loopStatements.Add(new IfStatement(forStatement.Source,
                    new UnaryExpression(forStatement.Source, "!", rewrittenCondition!, true),
                    new BreakStatement(forStatement.Source, null),
                    null));
            }
            else if (forStatement.Condition is not null)
            {
                loopStatements.Add(new IfStatement(forStatement.Source,
                    new UnaryExpression(forStatement.Source, "!", forStatement.Condition, true),
                    new BreakStatement(forStatement.Source, null),
                    null));
            }

            loopStatements.Add(RewriteEmbedded(forStatement.Body, isStrict));

            if (incrementHasYield && incrementYield is not null && incrementResumeIdentifier is not null)
            {
                loopStatements.Add(new ExpressionStatement(incrementYield.Source,
                    new AssignmentExpression(incrementYield.Source, incrementResumeIdentifier.Name,
                        new YieldExpression(incrementYield.Source, incrementYield.Expression,
                            incrementYield.IsDelegated))));

                loopStatements.Add(new ExpressionStatement(forStatement.Increment!.Source, rewrittenIncrement!));
            }
            else if (incrementHasTwoYields && incrementYieldLeft is not null && incrementYieldRight is not null &&
                     incrementBinary is not null && incrementResumeLeftIdentifier is not null &&
                     incrementResumeRightIdentifier is not null)
            {
                loopStatements.Add(new ExpressionStatement(incrementYieldLeft.Source,
                    new AssignmentExpression(incrementYieldLeft.Source, incrementResumeLeftIdentifier.Name,
                        new YieldExpression(incrementYieldLeft.Source, incrementYieldLeft.Expression,
                            incrementYieldLeft.IsDelegated))));

                loopStatements.Add(new ExpressionStatement(incrementYieldRight.Source,
                    new AssignmentExpression(incrementYieldRight.Source, incrementResumeRightIdentifier.Name,
                        new YieldExpression(incrementYieldRight.Source, incrementYieldRight.Expression,
                            incrementYieldRight.IsDelegated))));

                ExpressionNode substitutedIncrement = new BinaryExpression(incrementBinary.Source,
                    incrementBinary.Operator,
                    new IdentifierExpression(incrementYieldLeft.Source, incrementResumeLeftIdentifier.Name),
                    new IdentifierExpression(incrementYieldRight.Source, incrementResumeRightIdentifier.Name));

                if (incrementAssignmentTarget is not null)
                {
                    substitutedIncrement = new AssignmentExpression(forStatement.Increment!.Source,
                        incrementAssignmentTarget,
                        substitutedIncrement);
                }

                loopStatements.Add(new ExpressionStatement(forStatement.Increment!.Source, substitutedIncrement));
            }
            else if (forStatement.Increment is not null)
            {
                loopStatements.Add(new ExpressionStatement(forStatement.Increment.Source, forStatement.Increment));
            }

            var loopBlock = new BlockStatement(forStatement.Source, loopStatements.ToImmutable(), isStrict);
            var loweredLoop = new WhileStatement(forStatement.Source,
                new LiteralExpression(forStatement.Source, true),
                loopBlock);

            statements.Add(loweredLoop);
            replacement = statements.ToImmutable();
            return true;
        }

        private static bool TryRewriteIncrementWithTwoYields(ExpressionNode expression,
            out YieldExpression leftYield, out YieldExpression rightYield, out BinaryExpression incrementBinary,
            out Symbol? assignmentTarget)
        {
            leftYield = null!;
            rightYield = null!;
            incrementBinary = null!;
            assignmentTarget = null;

            BinaryExpression? binary = null;

            switch (expression)
            {
                case BinaryExpression asBinary:
                    binary = asBinary;
                    break;
                case AssignmentExpression { Value: BinaryExpression assignBinary, Target: not null } assignment:
                    assignmentTarget = assignment.Target;
                    binary = assignBinary;
                    break;
                default:
                    return false;
            }

            if (!AstShapeAnalyzer.TryFindSingleYield(binary.Left, out var left) ||
                !AstShapeAnalyzer.TryFindSingleYield(binary.Right, out var right))
            {
                return false;
            }

            if (left.IsDelegated || right.IsDelegated ||
                AstShapeAnalyzer.ContainsYield(left.Expression) || AstShapeAnalyzer.ContainsYield(right.Expression))
            {
                return false;
            }

            leftYield = left;
            rightYield = right;
            incrementBinary = binary;
            return true;
        }

        private BlockStatement RewriteEmbedded(StatementNode statement, bool isStrict)
        {
            if (statement is BlockStatement block)
            {
                return RewriteBlock(block);
            }

            var rewrittenStatements = RewriteStatements([statement], isStrict);
            if (rewrittenStatements is [BlockStatement singleBlock])
            {
                return singleBlock;
            }

            return new BlockStatement(statement.Source, rewrittenStatements, isStrict);
        }

        private IdentifierBinding CreateResumeIdentifier()
        {
            var symbol = Symbol.Intern($"__yield_lower_resume{_resumeCounter++}");
            return new IdentifierBinding(null, symbol);
        }

        private static ImmutableArray<StatementNode> BuildYieldedLoop(
            IdentifierBinding? resumeIdentifier,
            YieldExpression? yieldExpression,
            ExpressionNode? rewrittenCondition,
            LoopPlan plan,
            bool isStrict)
        {
            var statements = ImmutableArray.CreateBuilder<StatementNode>();

            if (resumeIdentifier is not null && yieldExpression is not null)
            {
                statements.Add(new VariableDeclaration(yieldExpression.Source, VariableKind.Let,
                    [new VariableDeclarator(yieldExpression.Source, resumeIdentifier, null)]));
            }

            if (!plan.LeadingStatements.IsDefaultOrEmpty)
            {
                statements.AddRange(plan.LeadingStatements);
            }

            var loopBlock = plan.Body;
            if (loopBlock.IsStrict != isStrict)
            {
                loopBlock = loopBlock with { IsStrict = isStrict };
            }

            // Build the per-iteration prologue that evaluates the yielded
            // condition and, for while-loops, performs the break check.
            var prologue = ImmutableArray.CreateBuilder<StatementNode>();
            if (resumeIdentifier is not null && yieldExpression is not null)
            {
                prologue.Add(new ExpressionStatement(yieldExpression.Source,
                    new AssignmentExpression(yieldExpression.Source, resumeIdentifier.Name,
                        new YieldExpression(yieldExpression.Source, yieldExpression.Expression,
                            yieldExpression.IsDelegated))));

                if (!plan.ConditionAfterBody)
                {
                    var conditionCheck = rewrittenCondition ?? plan.Condition;
                    prologue.Add(new IfStatement(plan.Body.Source,
                        new UnaryExpression(plan.Body.Source, "!", conditionCheck, true),
                        new BreakStatement(plan.Body.Source, null),
                        null));
                }
            }

            // Merge the prologue either before or after the loop body depending
            // on whether the condition is evaluated before or after the body.
            if (!plan.ConditionAfterBody)
            {
                var blockStatements = ImmutableArray.CreateBuilder<StatementNode>(
                    prologue.Count + plan.ConditionPrologue.Length + 1);
                blockStatements.AddRange(plan.ConditionPrologue);
                blockStatements.AddRange(prologue);
                blockStatements.Add(loopBlock);
                loopBlock = loopBlock with { Statements = blockStatements.ToImmutable() };
            }
            else
            {
                var blockStatements = ImmutableArray.CreateBuilder<StatementNode>(
                    loopBlock.Statements.Length + prologue.Count);
                blockStatements.AddRange(loopBlock.Statements);
                blockStatements.AddRange(prologue);
                loopBlock = loopBlock with { Statements = blockStatements.ToImmutable() };
            }

            StatementNode loweredLoop = plan.ConditionAfterBody
                ? new DoWhileStatement(plan.Body.Source, loopBlock, rewrittenCondition ?? plan.Condition)
                : new WhileStatement(plan.Body.Source, rewrittenCondition ?? plan.Condition, loopBlock);

            statements.Add(loweredLoop);
            return statements.ToImmutable();
        }
    }
}
