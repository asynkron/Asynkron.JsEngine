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

            if (ContainsYield(yieldExpression.Expression))
            {
                replacement = default;
                return false;
            }

            var resumeIdentifier = CreateResumeIdentifier();
            var declareResume = new VariableDeclaration(statement.Source, VariableKind.Let,
                [new VariableDeclarator(statement.Source, resumeIdentifier, null)]);
            var assignResume = new ExpressionStatement(yieldExpression.Source,
                new AssignmentExpression(yieldExpression.Source, resumeIdentifier.Name,
                    new YieldExpression(yieldExpression.Source, yieldExpression.Expression, yieldExpression.IsDelegated)));
            var loweredReturn = new ReturnStatement(statement.Source,
                new IdentifierExpression(yieldExpression.Source, resumeIdentifier.Name));

            replacement = ImmutableArray.Create<StatementNode>(declareResume, assignResume, loweredReturn);
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

            if (yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression))
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

            replacement = ImmutableArray.Create<StatementNode>(
                new VariableDeclaration(declaration.Source, VariableKind.Let,
                    [new VariableDeclarator(yieldExpression.Source, resumeIdentifier, null)]),
                new ExpressionStatement(yieldExpression.Source,
                    new AssignmentExpression(yieldExpression.Source, resumeIdentifier.Name,
                        new YieldExpression(yieldExpression.Source, yieldExpression.Expression, yieldExpression.IsDelegated))),
                rewrittenDeclaration);
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

        private bool TryRewriteYieldingAssignment(StatementNode statement,
            out ImmutableArray<StatementNode> replacement)
        {
            if (statement is not ExpressionStatement { Expression: AssignmentExpression assignment } expressionStatement ||
                assignment.Value is not YieldExpression yieldExpression)
            {
                replacement = default;
                return false;
            }

            if (yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression))
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

            replacement = ImmutableArray.Create<StatementNode>(
                new VariableDeclaration(
                    expressionStatement.Source,
                    VariableKind.Let,
                    [new VariableDeclarator(expressionStatement.Source, resumeIdentifier, null)]),
                new ExpressionStatement(yieldExpression.Source,
                    new AssignmentExpression(yieldExpression.Source, resumeIdentifier.Name,
                        new YieldExpression(yieldExpression.Source, yieldExpression.Expression, yieldExpression.IsDelegated))),
                rewrittenStatement);
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
                    if (!TryRewriteConditionWithSingleYield(ifStatement.Condition, out var yieldExpression,
                            out var rewrittenCondition))
                    {
                        return false;
                    }

                    if (yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression))
                    {
                        return false;
                    }

                    var resumeIdentifier = CreateResumeIdentifier();
                    var rewrittenThen = RewriteEmbedded(ifStatement.Then, isStrict);
                    var rewrittenElse = ifStatement.Else is null
                        ? null
                        : RewriteEmbedded(ifStatement.Else, isStrict);

                    var loweredIf = ifStatement with
                    {
                        Condition = SubstituteResumeIdentifier(rewrittenCondition, resumeIdentifier.Name),
                        Then = rewrittenThen,
                        Else = rewrittenElse
                    };

                    var declareResume = new VariableDeclaration(yieldExpression.Source, VariableKind.Let,
                        [new VariableDeclarator(yieldExpression.Source, resumeIdentifier, null)]);
                    var assignResume = new ExpressionStatement(yieldExpression.Source,
                        new AssignmentExpression(yieldExpression.Source, resumeIdentifier.Name,
                            new YieldExpression(yieldExpression.Source, yieldExpression.Expression, yieldExpression.IsDelegated)));

                    replacement = ImmutableArray.Create<StatementNode>(
                        declareResume,
                        assignResume,
                        loweredIf);
                    return true;
                }

                case WhileStatement whileStatement:
                {
                    if (!TryRewriteConditionWithSingleYield(whileStatement.Condition, out var yieldExpression,
                            out var rewrittenCondition))
                    {
                        return false;
                    }

                    if (yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression))
                    {
                        return false;
                    }

                    var resumeIdentifier = CreateResumeIdentifier();
                    var rewrittenBody = RewriteEmbedded(whileStatement.Body, isStrict);

                    var declareResume = new VariableDeclaration(yieldExpression.Source, VariableKind.Let,
                        [new VariableDeclarator(yieldExpression.Source, resumeIdentifier, null)]);

                    var assignResume = new ExpressionStatement(yieldExpression.Source,
                        new AssignmentExpression(yieldExpression.Source, resumeIdentifier.Name,
                            new YieldExpression(yieldExpression.Source, yieldExpression.Expression, yieldExpression.IsDelegated)));

                    var breakCheck = new IfStatement(whileStatement.Source,
                        new UnaryExpression(whileStatement.Source, "!",
                            SubstituteResumeIdentifier(rewrittenCondition, resumeIdentifier.Name), true),
                        new BreakStatement(whileStatement.Source, null),
                        null);

                    var loopBlock = new BlockStatement(whileStatement.Source,
                        ImmutableArray.Create<StatementNode>(assignResume, breakCheck, rewrittenBody),
                        isStrict);

                    var loweredWhile = new WhileStatement(whileStatement.Source,
                        new LiteralExpression(whileStatement.Source, true),
                        loopBlock);

                    replacement = ImmutableArray.Create<StatementNode>(declareResume, loweredWhile);
                    return true;
                }

                case DoWhileStatement doWhileStatement:
                {
                    if (!TryRewriteConditionWithSingleYield(doWhileStatement.Condition, out var yieldExpression,
                            out var rewrittenCondition))
                    {
                        return false;
                    }

                    if (yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression))
                    {
                        return false;
                    }

                    var resumeIdentifier = CreateResumeIdentifier();
                    var rewrittenBody = RewriteEmbedded(doWhileStatement.Body, isStrict);

                    var declareResume = new VariableDeclaration(yieldExpression.Source, VariableKind.Let,
                        [new VariableDeclarator(yieldExpression.Source, resumeIdentifier, null)]);
                    var assignResume = new ExpressionStatement(yieldExpression.Source,
                        new AssignmentExpression(yieldExpression.Source, resumeIdentifier.Name,
                            new YieldExpression(yieldExpression.Source, yieldExpression.Expression, yieldExpression.IsDelegated)));

                    var loopBodyStatements = ImmutableArray.Create<StatementNode>(
                        rewrittenBody,
                        assignResume);

                    var loweredBody = new BlockStatement(doWhileStatement.Source, loopBodyStatements, isStrict);

                    var loweredDoWhile = new DoWhileStatement(doWhileStatement.Source,
                        loweredBody,
                        SubstituteResumeIdentifier(rewrittenCondition, resumeIdentifier.Name));

                    replacement = ImmutableArray.Create<StatementNode>(declareResume, loweredDoWhile);

                    return true;
                }

                default:
                    return false;
            }
        }

        private static bool TryRewriteConditionWithSingleYield(ExpressionNode expression,
            out YieldExpression yieldExpression, out ExpressionNode rewrittenCondition)
        {
            yieldExpression = null!;
            rewrittenCondition = null!;
            YieldExpression? singleYield = null;
            var found = false;

            bool Rewrite(ExpressionNode? expr, out ExpressionNode rewritten)
            {
                switch (expr)
                {
                    case null:
                        rewritten = null!;
                        return true;
                    case YieldExpression y:
                        if (found)
                        {
                            rewritten = null!;
                            return false;
                        }

                        if (y.IsDelegated || ContainsYield(y.Expression))
                        {
                            rewritten = null!;
                            return false;
                        }

                        found = true;
                        singleYield = y;
                        rewritten = new IdentifierExpression(y.Source, PlaceholderSymbol);
                        return true;

                    case BinaryExpression binary:
                        if (!Rewrite(binary.Left, out var left) || !Rewrite(binary.Right, out var right))
                        {
                            rewritten = null!;
                            return false;
                        }

                        rewritten = ReferenceEquals(left, binary.Left) && ReferenceEquals(right, binary.Right)
                            ? binary
                            : binary with { Left = left, Right = right };
                        return true;

                    case ConditionalExpression conditional:
                        if (!Rewrite(conditional.Test, out var test) ||
                            !Rewrite(conditional.Consequent, out var cons) ||
                            !Rewrite(conditional.Alternate, out var alt))
                        {
                            rewritten = null!;
                            return false;
                        }

                        rewritten = ReferenceEquals(test, conditional.Test) &&
                                    ReferenceEquals(cons, conditional.Consequent) &&
                                    ReferenceEquals(alt, conditional.Alternate)
                        ? conditional
                        : conditional with { Test = test, Consequent = cons, Alternate = alt };
                        return true;

                    case CallExpression call:
                        if (!Rewrite(call.Callee, out var callee))
                        {
                            rewritten = null!;
                            return false;
                        }

                        var callArgs = call.Arguments;
                        if (call.Arguments.Length > 0)
                        {
                            var argBuilder = ImmutableArray.CreateBuilder<CallArgument>(call.Arguments.Length);
                            var changed = false;
                            foreach (var arg in call.Arguments)
                            {
                                if (!Rewrite(arg.Expression, out var argExpr))
                                {
                                    rewritten = null!;
                                    return false;
                                }

                                if (ReferenceEquals(argExpr, arg.Expression))
                                {
                                    argBuilder.Add(arg);
                                }
                                else
                                {
                                    argBuilder.Add(arg with { Expression = argExpr });
                                    changed = true;
                                }
                            }

                            if (changed || !ReferenceEquals(callee, call.Callee))
                            {
                                callArgs = argBuilder.ToImmutable();
                            }
                        }

                        rewritten = ReferenceEquals(callee, call.Callee) && ReferenceEquals(callArgs, call.Arguments)
                            ? call
                            : call with { Callee = callee, Arguments = callArgs };
                        return true;

                    case NewExpression @new:
                        if (!Rewrite(@new.Constructor, out var ctor))
                        {
                            rewritten = null!;
                            return false;
                        }

                        var newArgs = @new.Arguments;
                        if (@new.Arguments.Length > 0)
                        {
                            var argBuilder = ImmutableArray.CreateBuilder<ExpressionNode>(@new.Arguments.Length);
                            var changed = false;
                            foreach (var arg in @new.Arguments)
                            {
                                if (!Rewrite(arg, out var argExpr))
                                {
                                    rewritten = null!;
                                    return false;
                                }

                                if (ReferenceEquals(argExpr, arg))
                                {
                                    argBuilder.Add(arg);
                                }
                                else
                                {
                                    argBuilder.Add(argExpr);
                                    changed = true;
                                }
                            }

                            if (changed || !ReferenceEquals(ctor, @new.Constructor))
                            {
                                newArgs = argBuilder.ToImmutable();
                            }
                        }

                        rewritten = ReferenceEquals(ctor, @new.Constructor) && ReferenceEquals(newArgs, @new.Arguments)
                            ? @new
                            : @new with { Constructor = ctor, Arguments = newArgs };
                        return true;

                    case MemberExpression member:
                        if (!Rewrite(member.Target, out var target) ||
                            !Rewrite(member.Property, out var prop))
                        {
                            rewritten = null!;
                            return false;
                        }

                        rewritten = ReferenceEquals(target, member.Target) && ReferenceEquals(prop, member.Property)
                            ? member
                            : member with { Target = target, Property = prop };
                        return true;

                    case AssignmentExpression assignment:
                        if (!Rewrite(assignment.Value, out var valueExpr))
                        {
                            rewritten = null!;
                            return false;
                        }

                        rewritten = ReferenceEquals(valueExpr, assignment.Value)
                            ? assignment
                            : assignment with { Value = valueExpr };
                        return true;

                    case PropertyAssignmentExpression propertyAssignment:
                        if (!Rewrite(propertyAssignment.Target, out var patTarget) ||
                            !Rewrite(propertyAssignment.Property, out var patProp) ||
                            !Rewrite(propertyAssignment.Value, out var patValue))
                        {
                            rewritten = null!;
                            return false;
                        }

                        rewritten = ReferenceEquals(patTarget, propertyAssignment.Target) &&
                                    ReferenceEquals(patProp, propertyAssignment.Property) &&
                                    ReferenceEquals(patValue, propertyAssignment.Value)
                            ? propertyAssignment
                            : propertyAssignment with
                            {
                                Target = patTarget, Property = patProp, Value = patValue
                            };
                        return true;

                    case IndexAssignmentExpression indexAssignment:
                        if (!Rewrite(indexAssignment.Target, out var idxTarget) ||
                            !Rewrite(indexAssignment.Index, out var idxIndex) ||
                            !Rewrite(indexAssignment.Value, out var idxValue))
                        {
                            rewritten = null!;
                            return false;
                        }

                        rewritten = ReferenceEquals(idxTarget, indexAssignment.Target) &&
                                    ReferenceEquals(idxIndex, indexAssignment.Index) &&
                                    ReferenceEquals(idxValue, indexAssignment.Value)
                            ? indexAssignment
                            : indexAssignment with
                            {
                                Target = idxTarget, Index = idxIndex, Value = idxValue
                            };
                        return true;

                    case SequenceExpression sequence:
                        if (!Rewrite(sequence.Left, out var leftSeq) ||
                            !Rewrite(sequence.Right, out var rightSeq))
                        {
                            rewritten = null!;
                            return false;
                        }

                        rewritten = ReferenceEquals(leftSeq, sequence.Left) && ReferenceEquals(rightSeq, sequence.Right)
                            ? sequence
                            : sequence with { Left = leftSeq, Right = rightSeq };
                        return true;

                    case UnaryExpression unary:
                        if (!Rewrite(unary.Operand, out var operand))
                        {
                            rewritten = null!;
                            return false;
                        }

                        rewritten = ReferenceEquals(operand, unary.Operand)
                            ? unary
                            : unary with { Operand = operand };
                        return true;

                    case ArrayExpression array:
                        if (array.Elements.IsDefaultOrEmpty)
                        {
                            rewritten = array;
                            return true;
                        }

                        {
                            var builder = ImmutableArray.CreateBuilder<ArrayElement>(array.Elements.Length);
                            var changed = false;
                            foreach (var element in array.Elements)
                            {
                                if (element.Expression is null)
                                {
                                    builder.Add(element);
                                    continue;
                                }

                                if (!Rewrite(element.Expression, out var elemExpr))
                                {
                                    rewritten = null!;
                                    return false;
                                }

                                if (ReferenceEquals(elemExpr, element.Expression))
                                {
                                    builder.Add(element);
                                }
                                else
                                {
                                    builder.Add(element with { Expression = elemExpr });
                                    changed = true;
                                }
                            }

                            rewritten = changed
                                ? array with { Elements = builder.ToImmutable() }
                                : array;
                            return true;
                        }

                    case ObjectExpression obj:
                        if (obj.Members.IsDefaultOrEmpty)
                        {
                            rewritten = obj;
                            return true;
                        }

                        {
                            var builder = ImmutableArray.CreateBuilder<ObjectMember>(obj.Members.Length);
                            var changed = false;
                            foreach (var member in obj.Members)
                            {
                                ExpressionNode? memberValue = member.Value;
                                if (member.Value is not null)
                                {
                                    if (!Rewrite(member.Value, out var memberValueExpr))
                                    {
                                        rewritten = null!;
                                        return false;
                                    }

                                    memberValue = memberValueExpr;
                                }

                                if (ReferenceEquals(memberValue, member.Value))
                                {
                                    builder.Add(member);
                                }
                                else
                                {
                                    builder.Add(member with { Value = memberValue });
                                    changed = true;
                                }
                            }

                            rewritten = changed
                                ? obj with { Members = builder.ToImmutable() }
                                : obj;
                            return true;
                        }

                    case FunctionExpression:
                    case ClassExpression:
                        rewritten = expr;
                        return true;

                    default:
                        if (ContainsYield(expr))
                        {
                            rewritten = null!;
                            return false;
                        }

                        rewritten = expr;
                        return true;
                }
            }

            if (!Rewrite(expression, out var rewrittenConditionInternal))
            {
                return false;
            }

            if (!found || singleYield is null)
            {
                return false;
            }

            yieldExpression = singleYield;
            rewrittenCondition = rewrittenConditionInternal;
            return true;
        }

        private BlockStatement RewriteEmbedded(StatementNode statement, bool isStrict)
        {
            if (statement is BlockStatement block)
            {
                return RewriteBlock(block);
            }

            var rewrittenStatements = RewriteStatements(ImmutableArray.Create(statement), isStrict);
            if (rewrittenStatements.Length == 1 && rewrittenStatements[0] is BlockStatement singleBlock)
            {
                return singleBlock;
            }

            return new BlockStatement(statement.Source, rewrittenStatements, isStrict);
        }

        private static ExpressionNode SubstituteResumeIdentifier(ExpressionNode condition, Symbol resumeSymbol)
        {
            return ReplacePlaceholder(condition, resumeSymbol);
        }

        private static ExpressionNode ReplacePlaceholder(ExpressionNode expression, Symbol resumeSymbol)
        {
            switch (expression)
            {
                case IdentifierExpression { Name: { } name } when ReferenceEquals(name, PlaceholderSymbol):
                    return new IdentifierExpression(expression.Source, resumeSymbol);
                case BinaryExpression binary:
                    return binary with
                    {
                        Left = ReplacePlaceholder(binary.Left, resumeSymbol),
                        Right = ReplacePlaceholder(binary.Right, resumeSymbol)
                    };
                case ConditionalExpression conditional:
                    return conditional with
                    {
                        Test = ReplacePlaceholder(conditional.Test, resumeSymbol),
                        Consequent = ReplacePlaceholder(conditional.Consequent, resumeSymbol),
                        Alternate = ReplacePlaceholder(conditional.Alternate, resumeSymbol)
                    };
                case CallExpression call:
                {
                    var args = call.Arguments;
                    if (args.Length > 0)
                    {
                        var builder = ImmutableArray.CreateBuilder<CallArgument>(args.Length);
                        foreach (var arg in args)
                        {
                            builder.Add(arg with
                            {
                                Expression = ReplacePlaceholder(arg.Expression, resumeSymbol)
                            });
                        }

                        args = builder.ToImmutable();
                    }

                    return call with
                    {
                        Callee = ReplacePlaceholder(call.Callee, resumeSymbol),
                        Arguments = args
                    };
                }
                case NewExpression @new:
                {
                    var builder = ImmutableArray.CreateBuilder<ExpressionNode>(@new.Arguments.Length);
                    foreach (var arg in @new.Arguments)
                    {
                        builder.Add(ReplacePlaceholder(arg, resumeSymbol));
                    }

                    return @new with
                    {
                        Constructor = ReplacePlaceholder(@new.Constructor, resumeSymbol),
                        Arguments = builder.ToImmutable()
                    };
                }
                case MemberExpression member:
                    return member with
                    {
                        Target = ReplacePlaceholder(member.Target, resumeSymbol),
                        Property = ReplacePlaceholder(member.Property, resumeSymbol)
                    };
                case AssignmentExpression assignment:
                    return assignment with
                    {
                        Value = ReplacePlaceholder(assignment.Value, resumeSymbol)
                    };
                case PropertyAssignmentExpression propertyAssignment:
                    return propertyAssignment with
                    {
                        Target = ReplacePlaceholder(propertyAssignment.Target, resumeSymbol),
                        Property = ReplacePlaceholder(propertyAssignment.Property, resumeSymbol),
                        Value = ReplacePlaceholder(propertyAssignment.Value, resumeSymbol)
                    };
                case IndexAssignmentExpression indexAssignment:
                    return indexAssignment with
                    {
                        Target = ReplacePlaceholder(indexAssignment.Target, resumeSymbol),
                        Index = ReplacePlaceholder(indexAssignment.Index, resumeSymbol),
                        Value = ReplacePlaceholder(indexAssignment.Value, resumeSymbol)
                    };
                case SequenceExpression sequence:
                    return sequence with
                    {
                        Left = ReplacePlaceholder(sequence.Left, resumeSymbol),
                        Right = ReplacePlaceholder(sequence.Right, resumeSymbol)
                    };
                case UnaryExpression unary:
                    return unary with
                    {
                        Operand = ReplacePlaceholder(unary.Operand, resumeSymbol)
                    };
                case ArrayExpression array:
                {
                    var builder = ImmutableArray.CreateBuilder<ArrayElement>(array.Elements.Length);
                    foreach (var element in array.Elements)
                    {
                        builder.Add(element.Expression is null
                            ? element
                            : element with { Expression = ReplacePlaceholder(element.Expression, resumeSymbol) });
                    }

                    return array with { Elements = builder.ToImmutable() };
                }
                case ObjectExpression obj:
                {
                    var builder = ImmutableArray.CreateBuilder<ObjectMember>(obj.Members.Length);
                    foreach (var member in obj.Members)
                    {
                        builder.Add(member with
                        {
                            Value = member.Value is null ? null : ReplacePlaceholder(member.Value, resumeSymbol)
                        });
                    }

                    return obj with { Members = builder.ToImmutable() };
                }
                default:
                    return expression;
            }
        }

        private static readonly Symbol PlaceholderSymbol = Symbol.Intern("__yield_lower_placeholder");

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
