using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Parser;

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

                if (TryRewriteClassExpressionUsage(statement, out var classRewrite))
                {
                    builder.AddRange(classRewrite);
                    changed = true;
                    continue;
                }

                if (TryRewriteComplexYieldExpression(statement, out var complexYieldRewrite))
                {
                    builder.AddRange(complexYieldRewrite);
                    changed = true;
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

        private bool TryRewriteClassExpressionUsage(StatementNode statement, out ImmutableArray<StatementNode> replacement)
        {
            replacement = default;

            switch (statement)
            {
                case VariableDeclaration declaration:
                    return TryRewriteClassExpressionDeclaration(declaration, out replacement);
                case ExpressionStatement expressionStatement:
                    return TryRewriteClassExpressionExpression(expressionStatement, out replacement);
                default:
                    return false;
            }
        }

        private bool TryRewriteClassExpressionDeclaration(
            VariableDeclaration declaration,
            out ImmutableArray<StatementNode> replacement)
        {
            replacement = default;
            var declarators = declaration.Declarators;
            if (declarators.IsDefaultOrEmpty)
            {
                return false;
            }

            var rewrittenDeclarators = ImmutableArray.CreateBuilder<VariableDeclarator>(declarators.Length);
            var prefixStatements = ImmutableArray.CreateBuilder<StatementNode>();
            var changed = false;
            foreach (var declarator in declarators)
            {
                if (declarator.Initializer is ClassExpression classExpression &&
                    TryRewriteClassExpression(classExpression, out var rewrittenClass, out var prefix))
                {
                    prefixStatements.AddRange(prefix);
                    rewrittenDeclarators.Add(declarator with { Initializer = rewrittenClass });
                    changed = true;
                }
                else
                {
                    rewrittenDeclarators.Add(declarator);
                }
            }

            if (!changed)
            {
                return false;
            }

            var rewrittenDeclaration = declaration with { Declarators = rewrittenDeclarators.ToImmutable() };
            prefixStatements.Add(rewrittenDeclaration);
            replacement = prefixStatements.ToImmutable();
            return true;
        }

        private bool TryRewriteClassExpressionExpression(
            ExpressionStatement statement,
            out ImmutableArray<StatementNode> replacement)
        {
            replacement = default;

            if (statement.Expression is ClassExpression classExpression &&
                TryRewriteClassExpression(classExpression, out var rewrittenClass, out var prefix))
            {
                var rewrittenStatement = statement with { Expression = rewrittenClass };
                var builder = ImmutableArray.CreateBuilder<StatementNode>();
                builder.AddRange(prefix);
                builder.Add(rewrittenStatement);
                replacement = builder.ToImmutable();
                return true;
            }

            if (statement.Expression is AssignmentExpression assignment &&
                assignment.Value is ClassExpression classValue &&
                TryRewriteClassExpression(classValue, out var rewrittenValue, out var valuePrefix))
            {
                var rewrittenAssignment = assignment with { Value = rewrittenValue };
                var rewrittenStatement = statement with { Expression = rewrittenAssignment };
                var builder = ImmutableArray.CreateBuilder<StatementNode>();
                builder.AddRange(valuePrefix);
                builder.Add(rewrittenStatement);
                replacement = builder.ToImmutable();
                return true;
            }

            return false;
        }

        private bool TryRewriteClassExpression(
            ClassExpression classExpression,
            out ClassExpression rewritten,
            out ImmutableArray<StatementNode> prefixStatements)
        {
            var prefixBuilder = ImmutableArray.CreateBuilder<StatementNode>();
            if (!TryRewriteClassDefinition(classExpression.Definition, prefixBuilder, out var rewrittenDefinition))
            {
                rewritten = classExpression;
                prefixStatements = ImmutableArray<StatementNode>.Empty;
                return false;
            }

            rewritten = classExpression with { Definition = rewrittenDefinition };
            prefixStatements = prefixBuilder.ToImmutable();
            return prefixStatements.Length > 0;
        }

        private bool TryRewriteClassDefinition(
            ClassDefinition definition,
            ImmutableArray<StatementNode>.Builder prefixStatements,
            out ClassDefinition rewritten)
        {
            var members = definition.Members.ToBuilder();
            var fields = definition.Fields.ToBuilder();
            var changed = false;

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (member.IsComputed &&
                    member.ComputedName is YieldExpression computedYield)
                {
                    var tempBinding = CreateResumeIdentifier();
                    prefixStatements.Add(CreateYieldDeclaration(computedYield.Source, tempBinding, computedYield));
                    var replacement = new IdentifierExpression(computedYield.Source, tempBinding.Name);
                    members[i] = member with { ComputedName = replacement };
                    changed = true;
                }
            }

            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (field.IsComputed &&
                    field.ComputedName is YieldExpression computedYield)
                {
                    var tempBinding = CreateResumeIdentifier();
                    prefixStatements.Add(CreateYieldDeclaration(computedYield.Source, tempBinding, computedYield));
                    var replacement = new IdentifierExpression(computedYield.Source, tempBinding.Name);
                    fields[i] = field with { ComputedName = replacement };
                    changed = true;
                }
            }

            if (!changed)
            {
                rewritten = definition;
                return false;
            }

            rewritten = definition with
            {
                Members = members.ToImmutable(),
                Fields = fields.ToImmutable()
            };
            return true;
        }

        private bool TryRewriteComplexYieldExpression(
            StatementNode statement,
            out ImmutableArray<StatementNode> replacement)
        {
            replacement = default;
            if (statement is not ExpressionStatement expressionStatement)
            {
                return false;
            }

            if (!AstShapeAnalyzer.ContainsYield(expressionStatement.Expression) ||
                expressionStatement.Expression is YieldExpression)
            {
                return false;
            }

            var prefixStatements = ImmutableArray.CreateBuilder<StatementNode>();
            var changed = false;
            var rewrittenExpression =
                RewriteExpressionForComplexYields(expressionStatement.Expression, prefixStatements, ref changed);
            if (!changed)
            {
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<StatementNode>();
            builder.AddRange(prefixStatements);
            builder.Add(expressionStatement with { Expression = rewrittenExpression });
            replacement = builder.ToImmutable();
            return true;
        }

        private ExpressionNode RewriteExpressionForComplexYields(
            ExpressionNode expression,
            ImmutableArray<StatementNode>.Builder prefixStatements,
            ref bool changed)
        {
            switch (expression)
            {
                case YieldExpression yieldExpression:
                    return ReplaceYieldWithIdentifier(yieldExpression, prefixStatements, ref changed);

                case BinaryExpression binaryExpression:
                {
                    var left = RewriteExpressionForComplexYields(binaryExpression.Left, prefixStatements, ref changed);
                    var right = RewriteExpressionForComplexYields(binaryExpression.Right, prefixStatements, ref changed);
                    if (!ReferenceEquals(left, binaryExpression.Left) || !ReferenceEquals(right, binaryExpression.Right))
                    {
                        return binaryExpression with { Left = left, Right = right };
                    }

                    return binaryExpression;
                }

                case UnaryExpression unaryExpression:
                {
                    var operand =
                        RewriteExpressionForComplexYields(unaryExpression.Operand, prefixStatements, ref changed);
                    return ReferenceEquals(operand, unaryExpression.Operand)
                        ? unaryExpression
                        : unaryExpression with { Operand = operand };
                }

                case ConditionalExpression conditionalExpression:
                {
                    var test =
                        RewriteExpressionForComplexYields(conditionalExpression.Test, prefixStatements, ref changed);
                    var consequent = RewriteExpressionForComplexYields(
                        conditionalExpression.Consequent,
                        prefixStatements,
                        ref changed);
                    var alternate = RewriteExpressionForComplexYields(
                        conditionalExpression.Alternate,
                        prefixStatements,
                        ref changed);
                    if (!ReferenceEquals(test, conditionalExpression.Test) ||
                        !ReferenceEquals(consequent, conditionalExpression.Consequent) ||
                        !ReferenceEquals(alternate, conditionalExpression.Alternate))
                    {
                        return conditionalExpression with
                        {
                            Test = test, Consequent = consequent, Alternate = alternate
                        };
                    }

                    return conditionalExpression;
                }

                case AssignmentExpression assignmentExpression:
                {
                    var value =
                        RewriteExpressionForComplexYields(assignmentExpression.Value, prefixStatements, ref changed);
                    return ReferenceEquals(value, assignmentExpression.Value)
                        ? assignmentExpression
                        : assignmentExpression with { Value = value };
                }

                case PropertyAssignmentExpression propertyAssignmentExpression:
                {
                    var target =
                        RewriteExpressionForComplexYields(propertyAssignmentExpression.Target, prefixStatements,
                            ref changed);
                    var property =
                        RewriteExpressionForComplexYields(propertyAssignmentExpression.Property, prefixStatements,
                            ref changed);
                    var value =
                        RewriteExpressionForComplexYields(propertyAssignmentExpression.Value, prefixStatements,
                            ref changed);
                    if (!ReferenceEquals(target, propertyAssignmentExpression.Target) ||
                        !ReferenceEquals(property, propertyAssignmentExpression.Property) ||
                        !ReferenceEquals(value, propertyAssignmentExpression.Value))
                    {
                        return propertyAssignmentExpression with
                        {
                            Target = target, Property = property, Value = value
                        };
                    }

                    return propertyAssignmentExpression;
                }

                case IndexAssignmentExpression indexAssignmentExpression:
                {
                    var target =
                        RewriteExpressionForComplexYields(indexAssignmentExpression.Target, prefixStatements,
                            ref changed);
                    var index =
                        RewriteExpressionForComplexYields(indexAssignmentExpression.Index, prefixStatements,
                            ref changed);
                    var value =
                        RewriteExpressionForComplexYields(indexAssignmentExpression.Value, prefixStatements,
                            ref changed);
                    if (!ReferenceEquals(target, indexAssignmentExpression.Target) ||
                        !ReferenceEquals(index, indexAssignmentExpression.Index) ||
                        !ReferenceEquals(value, indexAssignmentExpression.Value))
                    {
                        return indexAssignmentExpression with
                        {
                            Target = target, Index = index, Value = value
                        };
                    }

                    return indexAssignmentExpression;
                }

                case CallExpression callExpression:
                {
                    var callee =
                        RewriteExpressionForComplexYields(callExpression.Callee, prefixStatements, ref changed);
                    var argsBuilder = ImmutableArray.CreateBuilder<CallArgument>(callExpression.Arguments.Length);
                    var argsChanged = false;
                    foreach (var argument in callExpression.Arguments)
                    {
                        var rewrittenArgument =
                            RewriteExpressionForComplexYields(argument.Expression, prefixStatements, ref changed);
                        argsChanged |= !ReferenceEquals(rewrittenArgument, argument.Expression);
                        argsBuilder.Add(argument with { Expression = rewrittenArgument });
                    }

                    if (!ReferenceEquals(callee, callExpression.Callee) || argsChanged)
                    {
                        return callExpression with { Callee = callee, Arguments = argsBuilder.ToImmutable() };
                    }

                    return callExpression;
                }

                case NewExpression newExpression:
                {
                    var ctor = RewriteExpressionForComplexYields(newExpression.Constructor, prefixStatements,
                        ref changed);
                    var argsBuilder = ImmutableArray.CreateBuilder<ExpressionNode>(newExpression.Arguments.Length);
                    var argsChanged = false;
                    foreach (var argument in newExpression.Arguments)
                    {
                        var rewrittenArgument =
                            RewriteExpressionForComplexYields(argument, prefixStatements, ref changed);
                        argsChanged |= !ReferenceEquals(argument, rewrittenArgument);
                        argsBuilder.Add(rewrittenArgument);
                    }

                    if (!ReferenceEquals(ctor, newExpression.Constructor) || argsChanged)
                    {
                        return newExpression with { Constructor = ctor, Arguments = argsBuilder.ToImmutable() };
                    }

                    return newExpression;
                }

                case MemberExpression memberExpression:
                {
                    var target =
                        RewriteExpressionForComplexYields(memberExpression.Target, prefixStatements, ref changed);
                    var property = memberExpression.IsComputed
                        ? RewriteExpressionForComplexYields(memberExpression.Property, prefixStatements, ref changed)
                        : memberExpression.Property;
                    if (!ReferenceEquals(target, memberExpression.Target) ||
                        !ReferenceEquals(property, memberExpression.Property))
                    {
                        return memberExpression with { Target = target, Property = property };
                    }

                    return memberExpression;
                }

                case SequenceExpression sequenceExpression:
                {
                    var left = RewriteExpressionForComplexYields(sequenceExpression.Left, prefixStatements, ref changed);
                    var right =
                        RewriteExpressionForComplexYields(sequenceExpression.Right, prefixStatements, ref changed);
                    if (!ReferenceEquals(left, sequenceExpression.Left) ||
                        !ReferenceEquals(right, sequenceExpression.Right))
                    {
                        return sequenceExpression with { Left = left, Right = right };
                    }

                    return sequenceExpression;
                }

                case ArrayExpression arrayExpression:
                {
                    var elementsBuilder = ImmutableArray.CreateBuilder<ArrayElement>(arrayExpression.Elements.Length);
                    var elementsChanged = false;
                    foreach (var element in arrayExpression.Elements)
                    {
                        if (element.Expression is null)
                        {
                            elementsBuilder.Add(element);
                            continue;
                        }

                        var rewrittenElement =
                            RewriteExpressionForComplexYields(element.Expression, prefixStatements, ref changed);
                        elementsChanged |= !ReferenceEquals(rewrittenElement, element.Expression);
                        elementsBuilder.Add(element with { Expression = rewrittenElement });
                    }

                    return elementsChanged
                        ? arrayExpression with { Elements = elementsBuilder.ToImmutable() }
                        : arrayExpression;
                }

                case ObjectExpression objectExpression:
                {
                    var membersBuilder = ImmutableArray.CreateBuilder<ObjectMember>(objectExpression.Members.Length);
                    var membersChanged = false;
                    foreach (var member in objectExpression.Members)
                    {
                        ExpressionNode? value = member.Value;
                        if (value is not null)
                        {
                            var rewrittenValue =
                                RewriteExpressionForComplexYields(value, prefixStatements, ref changed);
                            if (!ReferenceEquals(value, rewrittenValue))
                            {
                                value = rewrittenValue;
                                membersChanged = true;
                            }
                        }

                        membersBuilder.Add(member with { Value = value });
                    }

                    return membersChanged
                        ? objectExpression with { Members = membersBuilder.ToImmutable() }
                        : objectExpression;
                }

                case TemplateLiteralExpression templateLiteral:
                {
                    var partsBuilder = ImmutableArray.CreateBuilder<TemplatePart>(templateLiteral.Parts.Length);
                    var partsChanged = false;
                    foreach (var part in templateLiteral.Parts)
                    {
                        if (part.Expression is null)
                        {
                            partsBuilder.Add(part);
                            continue;
                        }

                        var rewrittenExpression =
                            RewriteExpressionForComplexYields(part.Expression, prefixStatements, ref changed);
                        if (!ReferenceEquals(part.Expression, rewrittenExpression))
                        {
                            partsChanged = true;
                            partsBuilder.Add(part with { Expression = rewrittenExpression });
                        }
                        else
                        {
                            partsBuilder.Add(part);
                        }
                    }

                    return partsChanged
                        ? templateLiteral with { Parts = partsBuilder.ToImmutable() }
                        : templateLiteral;
                }

                case TaggedTemplateExpression taggedTemplate:
                {
                    var tag = RewriteExpressionForComplexYields(taggedTemplate.Tag, prefixStatements, ref changed);
                    var stringsArray =
                        RewriteExpressionForComplexYields(taggedTemplate.StringsArray, prefixStatements, ref changed);
                    var rawStringsArray = RewriteExpressionForComplexYields(taggedTemplate.RawStringsArray,
                        prefixStatements,
                        ref changed);
                    var expressionsBuilder =
                        ImmutableArray.CreateBuilder<ExpressionNode>(taggedTemplate.Expressions.Length);
                    var expressionsChanged = false;
                    foreach (var expr in taggedTemplate.Expressions)
                    {
                        var rewrittenExpr = RewriteExpressionForComplexYields(expr, prefixStatements, ref changed);
                        expressionsChanged |= !ReferenceEquals(expr, rewrittenExpr);
                        expressionsBuilder.Add(rewrittenExpr);
                    }

                    if (!ReferenceEquals(tag, taggedTemplate.Tag) ||
                        !ReferenceEquals(stringsArray, taggedTemplate.StringsArray) ||
                        !ReferenceEquals(rawStringsArray, taggedTemplate.RawStringsArray) ||
                        expressionsChanged)
                    {
                        return taggedTemplate with
                        {
                            Tag = tag,
                            StringsArray = stringsArray,
                            RawStringsArray = rawStringsArray,
                            Expressions = expressionsBuilder.ToImmutable()
                        };
                    }

                    return taggedTemplate;
                }

                default:
                    return expression;
            }
        }

        private IdentifierExpression ReplaceYieldWithIdentifier(
            YieldExpression yieldExpression,
            ImmutableArray<StatementNode>.Builder prefixStatements,
            ref bool changed)
        {
            var tempBinding = CreateResumeIdentifier();
            prefixStatements.Add(CreateYieldDeclaration(yieldExpression.Source, tempBinding, yieldExpression));
            changed = true;
            return new IdentifierExpression(yieldExpression.Source, tempBinding.Name);
        }

        private VariableDeclaration CreateYieldDeclaration(
            SourceReference? source,
            IdentifierBinding tempBinding,
            YieldExpression yieldExpression)
        {
            var clonedYield = new YieldExpression(
                yieldExpression.Source,
                yieldExpression.Expression,
                yieldExpression.IsDelegated);
            var declarator = new VariableDeclarator(source, tempBinding, clonedYield);
            return new VariableDeclaration(source, VariableKind.Let, [declarator]);
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
