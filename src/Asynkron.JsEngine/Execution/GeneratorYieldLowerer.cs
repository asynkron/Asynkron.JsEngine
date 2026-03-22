#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Parser;

#endregion

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

        private TryStatement RewriteTryStatement(TryStatement tryStatement)
        {
            var rewrittenTryBlock = RewriteBlock(tryStatement.TryBlock);
            var rewrittenCatch = tryStatement.Catch;
            if (tryStatement.Catch is { Body: { } catchBody })
            {
                var rewrittenCatchBody = RewriteBlock(catchBody);
                if (!ReferenceEquals(rewrittenCatchBody, catchBody))
                {
                    rewrittenCatch = tryStatement.Catch with { Body = rewrittenCatchBody };
                }
            }

            var rewrittenFinally = tryStatement.Finally;
            if (tryStatement.Finally is { } finallyBlock)
            {
                var rewrittenFinallyBlock = RewriteBlock(finallyBlock);
                if (!ReferenceEquals(rewrittenFinallyBlock, finallyBlock))
                {
                    rewrittenFinally = rewrittenFinallyBlock;
                }
            }

            if (ReferenceEquals(rewrittenTryBlock, tryStatement.TryBlock) &&
                ReferenceEquals(rewrittenCatch, tryStatement.Catch) &&
                ReferenceEquals(rewrittenFinally, tryStatement.Finally))
            {
                return tryStatement;
            }

            return tryStatement with
            {
                TryBlock = rewrittenTryBlock,
                Catch = rewrittenCatch,
                Finally = rewrittenFinally
            };
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

                if (statement is TryStatement tryStatement)
                {
                    var rewrittenTry = RewriteTryStatement(tryStatement);
                    builder.Add(rewrittenTry);
                    changed |= !ReferenceEquals(rewrittenTry, tryStatement);
                    continue;
                }

                if (TryRewriteClassExpressionUsage(statement, out var classRewrite))
                {
                    builder.AddRange(classRewrite);
                    changed = true;
                    continue;
                }

                if (TryRewriteObjectLiteralUsage(statement, out var objectRewrite))
                {
                    builder.AddRange(objectRewrite);
                    changed = true;
                    continue;
                }

                if (TryRewriteClassDeclaration(statement, out var classDeclarationRewrite))
                {
                    builder.AddRange(classDeclarationRewrite);
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

                if (TryRewriteForEachWithYield(statement, isStrict, out var forEachRewrite))
                {
                    builder.AddRange(forEachRewrite);
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

                if (TryRewriteAssignmentToDestructuringWithYield(statement, isStrict, out var assignmentDestructuringRewrite))
                {
                    builder.AddRange(assignmentDestructuringRewrite);
                    changed = true;
                    continue;
                }

                if (TryRewriteDestructuringWithYieldDefaults(statement, isStrict, out var destructuringRewrite))
                {
                    builder.AddRange(destructuringRewrite);
                    changed = true;
                    continue;
                }

                if (TryRewriteVariableDeclaration(statement, out var replacement))
                {
                    builder.AddRange(replacement);
                    changed = true;
                    continue;
                }

                builder.Add(statement);
            }

            return changed ? builder.ToImmutable() : statements;
        }

        private bool TryRewriteClassExpressionUsage(StatementNode statement,
            out ImmutableArray<StatementNode> replacement)
        {
            replacement = default;

            return statement switch
            {
                VariableDeclaration declaration => TryRewriteClassExpressionDeclaration(declaration, out replacement),
                ExpressionStatement expressionStatement => TryRewriteClassExpressionExpression(expressionStatement,
                    out replacement),
                _ => false
            };
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

            if (statement.Expression is AssignmentExpression { Value: ClassExpression classValue } assignment &&
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

        private bool TryRewriteObjectLiteralUsage(StatementNode statement,
            out ImmutableArray<StatementNode> replacement)
        {
            replacement = default;

            return statement switch
            {
                VariableDeclaration declaration => TryRewriteObjectLiteralDeclaration(declaration, out replacement),
                ExpressionStatement expressionStatement => TryRewriteObjectLiteralExpression(expressionStatement,
                    out replacement),
                _ => false
            };
        }

        private bool TryRewriteObjectLiteralDeclaration(
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
                if (declarator.Initializer is ObjectExpression objectExpression &&
                    TryRewriteObjectExpression(objectExpression, out var rewrittenObject, out var prefix))
                {
                    prefixStatements.AddRange(prefix);
                    rewrittenDeclarators.Add(declarator with { Initializer = rewrittenObject });
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

        private bool TryRewriteObjectLiteralExpression(
            ExpressionStatement statement,
            out ImmutableArray<StatementNode> replacement)
        {
            replacement = default;

            if (statement.Expression is ObjectExpression objectExpression &&
                TryRewriteObjectExpression(objectExpression, out var rewrittenObject, out var prefix))
            {
                var rewrittenStatement = statement with { Expression = rewrittenObject };
                var builder = ImmutableArray.CreateBuilder<StatementNode>();
                builder.AddRange(prefix);
                builder.Add(rewrittenStatement);
                replacement = builder.ToImmutable();
                return true;
            }

            if (statement.Expression is AssignmentExpression { Value: ObjectExpression objectValue } assignment &&
                TryRewriteObjectExpression(objectValue, out var rewrittenValue, out var valuePrefix))
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

        private bool TryRewriteObjectExpression(
            ObjectExpression objectExpression,
            out ObjectExpression rewritten,
            out ImmutableArray<StatementNode> prefixStatements)
        {
            var prefixBuilder = ImmutableArray.CreateBuilder<StatementNode>();
            var membersBuilder = ImmutableArray.CreateBuilder<ObjectMember>(objectExpression.Members.Length);
            var changed = false;

            foreach (var member in objectExpression.Members)
            {
                var key = member.Key;
                if (member is { IsComputed: true, Key: ExpressionNode keyExpression } &&
                    AstShapeAnalyzer.ContainsYield(keyExpression))
                {
                    var keyChanged = false;
                    var rewrittenKey = RewriteExpressionForComplexYields(keyExpression, prefixBuilder, ref keyChanged);
                    if (keyChanged)
                    {
                        key = rewrittenKey;
                        changed = true;
                    }
                }

                var value = member.Value;
                if (value is not null && AstShapeAnalyzer.ContainsYield(value))
                {
                    var valueChanged = false;
                    var rewrittenValue = RewriteExpressionForComplexYields(value, prefixBuilder, ref valueChanged);
                    if (valueChanged)
                    {
                        value = rewrittenValue;
                        changed = true;
                    }
                }

                membersBuilder.Add(member with { Key = key, Value = value });
            }

            if (!changed)
            {
                rewritten = objectExpression;
                prefixStatements = ImmutableArray<StatementNode>.Empty;
                return false;
            }

            rewritten = objectExpression with { Members = membersBuilder.ToImmutable() };
            prefixStatements = prefixBuilder.ToImmutable();
            return prefixStatements.Length > 0;
        }

        private bool TryRewriteClassDeclaration(
            StatementNode statement,
            out ImmutableArray<StatementNode> replacement)
        {
            replacement = default;

            if (statement is not ClassDeclaration classDeclaration)
            {
                return false;
            }

            var prefixBuilder = ImmutableArray.CreateBuilder<StatementNode>();
            if (!TryRewriteClassDefinition(classDeclaration.Definition, prefixBuilder, out var rewrittenDefinition))
            {
                return false;
            }

            var rewrittenClass = classDeclaration with { Definition = rewrittenDefinition };
            prefixBuilder.Add(rewrittenClass);
            replacement = prefixBuilder.ToImmutable();
            return true;
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
            var rewrittenExtends = definition.Extends;

            // Handle extends clause containing yield
            if (definition.Extends is not null && AstShapeAnalyzer.ContainsYield(definition.Extends))
            {
                var extendsChanged = false;
                rewrittenExtends =
                    RewriteExpressionForComplexYields(definition.Extends, prefixStatements, ref extendsChanged);
                changed |= extendsChanged;
            }

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (member is { IsComputed: true, ComputedName: not null } &&
                    AstShapeAnalyzer.ContainsYield(member.ComputedName))
                {
                    var memberChanged = false;
                    var rewrittenName =
                        RewriteExpressionForComplexYields(member.ComputedName, prefixStatements, ref memberChanged);
                    members[i] = member with { ComputedName = rewrittenName };
                    changed |= memberChanged;
                }
            }

            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (field is { IsComputed: true, ComputedName: not null } &&
                    AstShapeAnalyzer.ContainsYield(field.ComputedName))
                {
                    var fieldChanged = false;
                    var rewrittenName =
                        RewriteExpressionForComplexYields(field.ComputedName, prefixStatements, ref fieldChanged);
                    fields[i] = field with { ComputedName = rewrittenName };
                    changed |= fieldChanged;
                }
            }

            if (!changed)
            {
                rewritten = definition;
                return false;
            }

            rewritten = definition with
            {
                Extends = rewrittenExtends,
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

            if (!AstShapeAnalyzer.ContainsYield(expressionStatement.Expression))
            {
                return false;
            }

            // If the statement is a bare `yield <expr>` and the operand itself contains
            // yields, lower the operand into temporaries so the outer yield can proceed.
            if (expressionStatement.Expression is YieldExpression yieldExpression &&
                AstShapeAnalyzer.ContainsYield(yieldExpression.Expression))
            {
                var nestedPrefix = ImmutableArray.CreateBuilder<StatementNode>();
                var nestedChanged = false;
                var rewrittenOperand =
                    RewriteExpressionForComplexYields(yieldExpression.Expression, nestedPrefix, ref nestedChanged);
                if (nestedChanged)
                {
                    var rewrittenYield = yieldExpression with { Expression = rewrittenOperand };
                    var nestedResult = ImmutableArray.CreateBuilder<StatementNode>();
                    nestedResult.AddRange(nestedPrefix);
                    nestedResult.Add(expressionStatement with { Expression = rewrittenYield });
                    replacement = nestedResult.ToImmutable();
                    return true;
                }
            }

            // Handle conditional expression with yields in branches as an expression statement.
            // Convert: (yield) ? yield : yield; --> if (__test) { yield; } else { yield; }
            if (expressionStatement.Expression is ConditionalExpression conditionalExpr &&
                (AstShapeAnalyzer.ContainsYield(conditionalExpr.Consequent) ||
                 AstShapeAnalyzer.ContainsYield(conditionalExpr.Alternate)))
            {
                var prefixBuilder = ImmutableArray.CreateBuilder<StatementNode>();

                // Extract yield from the test expression if present
                var testExpr = conditionalExpr.Test;
                if (AstShapeAnalyzer.ContainsYield(conditionalExpr.Test))
                {
                    var testChanged = false;
                    testExpr = RewriteExpressionForComplexYields(conditionalExpr.Test, prefixBuilder, ref testChanged);
                }

                // Convert consequent to statement
                StatementNode consequentStmt =
                    new ExpressionStatement(conditionalExpr.Source, conditionalExpr.Consequent);

                // Convert alternate to statement
                StatementNode alternateStmt =
                    new ExpressionStatement(conditionalExpr.Source, conditionalExpr.Alternate);

                var ifStatement = new IfStatement(
                    expressionStatement.Source,
                    testExpr,
                    consequentStmt,
                    alternateStmt);

                prefixBuilder.Add(ifStatement);
                replacement = prefixBuilder.ToImmutable();
                return true;
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
                        var right = RewriteExpressionForComplexYields(binaryExpression.Right, prefixStatements,
                            ref changed);
                        if (!ReferenceEquals(left, binaryExpression.Left) ||
                            !ReferenceEquals(right, binaryExpression.Right))
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
                        // Only rewrite the test expression. The consequent and alternate should NOT
                        // be rewritten here because only one of them will execute. If we extract yields
                        // from both branches, we'd execute yields that shouldn't run.
                        var test =
                            RewriteExpressionForComplexYields(conditionalExpression.Test, prefixStatements, ref changed);

                        if (!ReferenceEquals(test, conditionalExpression.Test))
                        {
                            return conditionalExpression with { Test = test };
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
                                Target = target,
                                Property = property,
                                Value = value
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
                            return indexAssignmentExpression with { Target = target, Index = index, Value = value };
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
                        var argsBuilder = ImmutableArray.CreateBuilder<CallArgument>(newExpression.Arguments.Length);
                        var argsChanged = false;
                        foreach (var argument in newExpression.Arguments)
                        {
                            var rewrittenArgument =
                                RewriteExpressionForComplexYields(argument.Expression, prefixStatements, ref changed);
                            argsChanged |= !ReferenceEquals(argument.Expression, rewrittenArgument);
                            argsBuilder.Add(argument with { Expression = rewrittenArgument });
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
                        var left = RewriteExpressionForComplexYields(sequenceExpression.Left, prefixStatements,
                            ref changed);
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
                            var key = member.Key;
                            if (member is { IsComputed: true, Key: ExpressionNode keyExpression })
                            {
                                var rewrittenKey =
                                    RewriteExpressionForComplexYields(keyExpression, prefixStatements, ref changed);
                                if (!ReferenceEquals(rewrittenKey, keyExpression))
                                {
                                    key = rewrittenKey;
                                    membersChanged = true;
                                }
                            }

                            var value = member.Value;
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

                            membersBuilder.Add(member with { Key = key, Value = value });
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

                case DestructuringAssignmentExpression destructuringAssignment:
                    {
                        // Check if the binding target has yields in default values or in
                        // AssignmentTargetBinding expressions (computed property accesses).
                        // If so, we cannot safely extract those yields:
                        // - Defaults are conditional (only evaluated when value is undefined)
                        // - AssignmentTargetBinding yields must happen AFTER the iterator is opened,
                        //   otherwise iterator close semantics break (the iterator won't exist yet)
                        // Let the expression pass through unchanged so the IR builder can wrap it
                        // in a StatementInstruction for proper handling.
                        if (AstShapeAnalyzer.BindingTargetContainsYieldInDefaultValue(destructuringAssignment.Target) ||
                            BindingTargetContainsYieldInAssignmentTarget(destructuringAssignment.Target))
                        {
                            return destructuringAssignment;
                        }

                        // No yields in defaults or assignment targets - safe to extract yields from:
                        // 1. The binding target (computed properties in object keys)
                        // 2. The value expression
                        var targetChanged = false;
                        var rewrittenTarget = RewriteBindingTargetForExtractableYields(
                            destructuringAssignment.Target, prefixStatements, ref targetChanged);

                        var valueChanged = false;
                        var rewrittenValue = RewriteExpressionForComplexYields(
                            destructuringAssignment.Value, prefixStatements, ref valueChanged);

                        if (targetChanged || valueChanged)
                        {
                            changed = true;
                            return destructuringAssignment with { Target = rewrittenTarget, Value = rewrittenValue };
                        }

                        return destructuringAssignment;
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
            // If the yield operand contains nested yields, extract them first
            var operand = yieldExpression.Expression;
            if (operand is not null && AstShapeAnalyzer.ContainsYield(operand))
            {
                var operandChanged = false;
                operand = RewriteExpressionForComplexYields(operand, prefixStatements, ref operandChanged);
            }

            var tempBinding = CreateResumeIdentifier();
            var rewrittenYield = operand is null || ReferenceEquals(operand, yieldExpression.Expression)
                ? yieldExpression
                : yieldExpression with { Expression = operand };
            prefixStatements.Add(CreateYieldDeclaration(yieldExpression.Source, tempBinding, rewrittenYield));
            changed = true;
            return new IdentifierExpression(yieldExpression.Source, tempBinding.Name);
        }

        private static VariableDeclaration CreateYieldDeclaration(
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

        private BindingTarget RewriteBindingTarget(
            BindingTarget target,
            ImmutableArray<StatementNode>.Builder prefixStatements,
            ref bool changed)
        {
            switch (target)
            {
                case ArrayBinding arrayBinding:
                    {
                        var elementsBuilder =
                            ImmutableArray.CreateBuilder<ArrayBindingElement>(arrayBinding.Elements.Length);
                        var elementsChanged = false;

                        foreach (var element in arrayBinding.Elements)
                        {
                            var rewrittenElement = RewriteArrayBindingElement(element, prefixStatements, ref changed);
                            elementsChanged |= !ReferenceEquals(rewrittenElement, element);
                            elementsBuilder.Add(rewrittenElement);
                        }

                        // Handle rest element if it has nested bindings
                        var rest = arrayBinding.RestElement;
                        if (rest is not null)
                        {
                            rest = RewriteBindingTarget(rest, prefixStatements, ref changed);
                        }

                        if (elementsChanged || !ReferenceEquals(rest, arrayBinding.RestElement))
                        {
                            changed = true;
                            return arrayBinding with { Elements = elementsBuilder.ToImmutable(), RestElement = rest };
                        }

                        return arrayBinding;
                    }

                case ObjectBinding objectBinding:
                    {
                        var propsBuilder =
                            ImmutableArray.CreateBuilder<ObjectBindingProperty>(objectBinding.Properties.Length);
                        var propsChanged = false;

                        foreach (var prop in objectBinding.Properties)
                        {
                            var rewrittenProp = RewriteObjectBindingProperty(prop, prefixStatements, ref changed);
                            propsChanged |= !ReferenceEquals(rewrittenProp, prop);
                            propsBuilder.Add(rewrittenProp);
                        }

                        // Handle rest element if present
                        var rest = objectBinding.RestElement;
                        if (rest is not null)
                        {
                            rest = RewriteBindingTarget(rest, prefixStatements, ref changed);
                        }

                        if (propsChanged || !ReferenceEquals(rest, objectBinding.RestElement))
                        {
                            changed = true;
                            return objectBinding with { Properties = propsBuilder.ToImmutable(), RestElement = rest };
                        }

                        return objectBinding;
                    }

                case AssignmentTargetBinding assignmentTarget:
                    {
                        // AssignmentTargetBinding has an Expression (e.g., a.b or a[yield])
                        // Check if the expression contains yields and rewrite if needed
                        if (AstShapeAnalyzer.ContainsYield(assignmentTarget.Expression))
                        {
                            var exprChanged = false;
                            var rewrittenExpr = RewriteExpressionForComplexYields(
                                assignmentTarget.Expression, prefixStatements, ref exprChanged);
                            if (exprChanged)
                            {
                                changed = true;
                                return assignmentTarget with { Expression = rewrittenExpr };
                            }
                        }

                        return assignmentTarget;
                    }

                default:
                    return target;
            }
        }

        /// <summary>
        /// Rewrites a binding target to extract yields ONLY from extractable positions:
        /// - Computed property accesses (e.g., a[yield])
        /// - Nested computed expressions
        /// Does NOT extract yields from default values (they're conditional).
        /// </summary>
        private BindingTarget RewriteBindingTargetForExtractableYields(
            BindingTarget target,
            ImmutableArray<StatementNode>.Builder prefixStatements,
            ref bool changed)
        {
            switch (target)
            {
                case ArrayBinding arrayBinding:
                    {
                        var elementsBuilder =
                            ImmutableArray.CreateBuilder<ArrayBindingElement>(arrayBinding.Elements.Length);
                        var elementsChanged = false;

                        foreach (var element in arrayBinding.Elements)
                        {
                            var rewrittenElement = RewriteArrayBindingElementForExtractableYields(
                                element, prefixStatements, ref changed);
                            elementsChanged |= !ReferenceEquals(rewrittenElement, element);
                            elementsBuilder.Add(rewrittenElement);
                        }

                        // Handle rest element if it has nested bindings
                        var rest = arrayBinding.RestElement;
                        if (rest is not null)
                        {
                            rest = RewriteBindingTargetForExtractableYields(rest, prefixStatements, ref changed);
                        }

                        if (elementsChanged || !ReferenceEquals(rest, arrayBinding.RestElement))
                        {
                            changed = true;
                            return arrayBinding with { Elements = elementsBuilder.ToImmutable(), RestElement = rest };
                        }

                        return arrayBinding;
                    }

                case ObjectBinding objectBinding:
                    {
                        var propsBuilder =
                            ImmutableArray.CreateBuilder<ObjectBindingProperty>(objectBinding.Properties.Length);
                        var propsChanged = false;

                        foreach (var prop in objectBinding.Properties)
                        {
                            var rewrittenProp = RewriteObjectBindingPropertyForExtractableYields(
                                prop, prefixStatements, ref changed);
                            propsChanged |= !ReferenceEquals(rewrittenProp, prop);
                            propsBuilder.Add(rewrittenProp);
                        }

                        // Handle rest element if present
                        var rest = objectBinding.RestElement;
                        if (rest is not null)
                        {
                            rest = RewriteBindingTargetForExtractableYields(rest, prefixStatements, ref changed);
                        }

                        if (propsChanged || !ReferenceEquals(rest, objectBinding.RestElement))
                        {
                            changed = true;
                            return objectBinding with { Properties = propsBuilder.ToImmutable(), RestElement = rest };
                        }

                        return objectBinding;
                    }

                case AssignmentTargetBinding assignmentTarget:
                    {
                        // AssignmentTargetBinding has an Expression (e.g., a.b or a[yield])
                        // Check if the expression contains yields and rewrite if needed
                        if (AstShapeAnalyzer.ContainsYield(assignmentTarget.Expression))
                        {
                            var exprChanged = false;
                            var rewrittenExpr = RewriteExpressionForComplexYields(
                                assignmentTarget.Expression, prefixStatements, ref exprChanged);
                            if (exprChanged)
                            {
                                changed = true;
                                return assignmentTarget with { Expression = rewrittenExpr };
                            }
                        }

                        return assignmentTarget;
                    }

                default:
                    return target;
            }
        }

        private ArrayBindingElement RewriteArrayBindingElementForExtractableYields(
            ArrayBindingElement element,
            ImmutableArray<StatementNode>.Builder prefixStatements,
            ref bool changed)
        {
            var target = element.Target;

            // Recursively handle nested bindings (extracts yields from computed properties)
            if (target is not null)
            {
                target = RewriteBindingTargetForExtractableYields(target, prefixStatements, ref changed);
            }

            // NOTE: Do NOT rewrite yields in default values - they're conditional
            // and must be handled via StatementInstruction

            if (!ReferenceEquals(target, element.Target))
            {
                changed = true;
                return element with { Target = target };
            }

            return element;
        }

        private ObjectBindingProperty RewriteObjectBindingPropertyForExtractableYields(
            ObjectBindingProperty prop,
            ImmutableArray<StatementNode>.Builder prefixStatements,
            ref bool changed)
        {
            var target = prop.Target;

            // Recursively handle nested bindings (extracts yields from computed properties)
            target = RewriteBindingTargetForExtractableYields(target, prefixStatements, ref changed);

            // NOTE: Do NOT rewrite yields in default values - they're conditional
            // and must be handled via StatementInstruction

            // Handle computed property keys (NameExpression) - these ARE extractable
            var nameExpr = prop.NameExpression;
            if (nameExpr is not null && AstShapeAnalyzer.ContainsYield(nameExpr))
            {
                nameExpr = RewriteExpressionForComplexYields(nameExpr, prefixStatements, ref changed);
            }

            if (!ReferenceEquals(target, prop.Target) ||
                !ReferenceEquals(nameExpr, prop.NameExpression))
            {
                changed = true;
                return prop with { Target = target, NameExpression = nameExpr };
            }

            return prop;
        }

        private ArrayBindingElement RewriteArrayBindingElement(
            ArrayBindingElement element,
            ImmutableArray<StatementNode>.Builder prefixStatements,
            ref bool changed)
        {
            var target = element.Target;
            var defaultValue = element.DefaultValue;

            // Recursively handle nested bindings
            if (target is not null)
            {
                target = RewriteBindingTarget(target, prefixStatements, ref changed);
            }

            // Rewrite yields in default value
            if (defaultValue is not null && AstShapeAnalyzer.ContainsYield(defaultValue))
            {
                defaultValue = RewriteExpressionForComplexYields(defaultValue, prefixStatements, ref changed);
            }

            if (!ReferenceEquals(target, element.Target) || !ReferenceEquals(defaultValue, element.DefaultValue))
            {
                changed = true;
                return element with { Target = target, DefaultValue = defaultValue };
            }

            return element;
        }

        private ObjectBindingProperty RewriteObjectBindingProperty(
            ObjectBindingProperty prop,
            ImmutableArray<StatementNode>.Builder prefixStatements,
            ref bool changed)
        {
            var target = prop.Target;
            var defaultValue = prop.DefaultValue;

            // Recursively handle nested bindings
            target = RewriteBindingTarget(target, prefixStatements, ref changed);

            // Rewrite yields in default value
            if (defaultValue is not null && AstShapeAnalyzer.ContainsYield(defaultValue))
            {
                defaultValue = RewriteExpressionForComplexYields(defaultValue, prefixStatements, ref changed);
            }

            // Also handle computed property keys (NameExpression)
            var nameExpr = prop.NameExpression;
            if (nameExpr is not null && AstShapeAnalyzer.ContainsYield(nameExpr))
            {
                nameExpr = RewriteExpressionForComplexYields(nameExpr, prefixStatements, ref changed);
            }

            if (!ReferenceEquals(target, prop.Target) ||
                !ReferenceEquals(defaultValue, prop.DefaultValue) ||
                !ReferenceEquals(nameExpr, prop.NameExpression))
            {
                changed = true;
                return prop with { Target = target, DefaultValue = defaultValue, NameExpression = nameExpr };
            }

            return prop;
        }

        private bool TryRewriteReturnWithYield(StatementNode statement,
            out ImmutableArray<StatementNode> replacement)
        {
            if (statement is not ReturnStatement { Expression: not null } returnStatement)
            {
                replacement = default;
                return false;
            }

            // General case: return expression contains yields somewhere inside.
            if (returnStatement.Expression is not YieldExpression &&
                AstShapeAnalyzer.ContainsYield(returnStatement.Expression))
            {
                var prefix = ImmutableArray.CreateBuilder<StatementNode>();
                var changed = false;
                var rewritten =
                    RewriteExpressionForComplexYields(returnStatement.Expression, prefix, ref changed);
                if (changed)
                {
                    prefix.Add(returnStatement with { Expression = rewritten });
                    replacement = prefix.ToImmutable();
                    return true;
                }
            }

            if (returnStatement.Expression is not YieldExpression yieldExpression)
            {
                replacement = default;
                return false;
            }

            var resumeIdentifier = CreateResumeIdentifier();
            var operand = yieldExpression.Expression;
            if (AstShapeAnalyzer.ContainsYield(operand))
            {
                var nestedPrefix = ImmutableArray.CreateBuilder<StatementNode>();
                var nestedChanged = false;
                operand = RewriteExpressionForComplexYields(operand, nestedPrefix, ref nestedChanged);
                if (nestedChanged)
                {
                    var loweredReturn = BuildReturnWithYield(yieldExpression with { Expression = operand },
                        resumeIdentifier,
                        statement.Source);
                    nestedPrefix.AddRange(loweredReturn);
                    replacement = nestedPrefix.ToImmutable();
                    return true;
                }
            }

            replacement = BuildReturnWithYield(yieldExpression, resumeIdentifier, statement.Source);
            return true;
        }

        private static ImmutableArray<StatementNode> BuildReturnWithYield(
            YieldExpression yieldExpression,
            IdentifierBinding resumeIdentifier,
            SourceReference? returnSource)
        {
            var declareResume = new VariableDeclaration(returnSource, VariableKind.Let,
                [new VariableDeclarator(returnSource, resumeIdentifier, null)]);
            var assignResume = new ExpressionStatement(yieldExpression.Source,
                new AssignmentExpression(yieldExpression.Source, resumeIdentifier.Name,
                    new YieldExpression(yieldExpression.Source, yieldExpression.Expression,
                        yieldExpression.IsDelegated)));
            var loweredReturn = new ReturnStatement(returnSource,
                new IdentifierExpression(yieldExpression.Source, resumeIdentifier.Name));
            return [declareResume, assignResume, loweredReturn];
        }

        private bool TryRewriteYieldingDeclaration(StatementNode statement,
            out ImmutableArray<StatementNode> replacement)
        {
            if (statement is not VariableDeclaration
                {
                    Declarators: [{ Initializer: YieldExpression yieldExpression } declarator]
                } declaration)
            {
                replacement = default;
                return false;
            }

            var initializer = yieldExpression.Expression;
            if (AstShapeAnalyzer.ContainsYield(initializer))
            {
                var nestedPrefix = ImmutableArray.CreateBuilder<StatementNode>();
                var nestedChanged = false;
                initializer = RewriteExpressionForComplexYields(initializer, nestedPrefix, ref nestedChanged);
                if (nestedChanged)
                {
                    var nestedResume = CreateResumeIdentifier();
                    var nestedDeclarator = declarator with
                    {
                        Initializer = new IdentifierExpression(yieldExpression.Source, nestedResume.Name)
                    };
                    var nestedDeclaration = declaration with { Declarators = [nestedDeclarator] };

                    var declareResume = new VariableDeclaration(statement.Source, VariableKind.Let,
                        [new VariableDeclarator(statement.Source, nestedResume, null)]);
                    var assignResume = new ExpressionStatement(yieldExpression.Source,
                        new AssignmentExpression(yieldExpression.Source, nestedResume.Name,
                            new YieldExpression(yieldExpression.Source, initializer, yieldExpression.IsDelegated)));

                    nestedPrefix.Add(declareResume);
                    nestedPrefix.Add(assignResume);
                    nestedPrefix.Add(nestedDeclaration);
                    replacement = nestedPrefix.ToImmutable();
                    return true;
                }
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

        private bool TryRewriteVariableDeclaration(StatementNode statement,
            out ImmutableArray<StatementNode> replacement)
        {
            if (statement is not VariableDeclaration { Declarators.Length: 1 } declaration)
            {
                replacement = default;
                return false;
            }

            var declarator = declaration.Declarators[0];
            if (declarator.Target is not IdentifierBinding ||
                declarator.Initializer is not BinaryExpression
                {
                    Left: YieldExpression leftYield, Right: YieldExpression rightYield
                } binary)
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

        /// <summary>
        /// Rewrites variable declarations with destructuring patterns that contain yields in default values.
        /// Transforms: let [a = yield 1, b = yield 2] = arr;
        /// Into:
        ///   let __temp = arr;
        ///   let __iter = __temp[Symbol.iterator]();
        ///   let __next0 = __iter.next();
        ///   let __val0 = __next0.done ? undefined : __next0.value;
        ///   let a;
        ///   if (__val0 === undefined) { a = yield 1; } else { a = __val0; }
        ///   ... (similar for b)
        /// </summary>
        private bool TryRewriteDestructuringWithYieldDefaults(
            StatementNode statement,
            bool isStrict,
            out ImmutableArray<StatementNode> replacement)
        {
            replacement = default;
            BindingTarget target;
            ExpressionNode initializer;
            VariableKind? varKind;
            SourceReference? source;

            switch (statement)
            {
                case VariableDeclaration { Declarators.Length: 1 } declaration:
                    {
                        var declarator = declaration.Declarators[0];
                        if (declarator.Target is not (ArrayBinding or ObjectBinding) ||
                            declarator.Initializer is null)
                        {
                            return false;
                        }

                        target = declarator.Target;
                        initializer = declarator.Initializer;
                        varKind = declaration.Kind;
                        source = declaration.Source;
                        break;
                    }

                case ExpressionStatement { Expression: DestructuringAssignmentExpression assignment }:
                    {
                        if (assignment.Target is not (ArrayBinding or ObjectBinding))
                        {
                            return false;
                        }

                        target = assignment.Target;
                        initializer = assignment.Value;
                        varKind = null;
                        source = statement.Source;
                        break;
                    }

                default:
                    return false;
            }

            if (!AstShapeAnalyzer.BindingTargetContainsYieldInDefaultValue(target))
            {
                return false;
            }

            var statements = ImmutableArray.CreateBuilder<StatementNode>();

            // Step 1: Evaluate the initializer into a temp variable
            // let __temp = arr;
            var tempSymbol = CreateResumeIdentifier();
            statements.Add(new VariableDeclaration(
                source,
                VariableKind.Let,
                [new VariableDeclarator(source, tempSymbol, initializer)]));

            // Step 2: Handle the destructuring pattern
            switch (target)
            {
                case ArrayBinding arrayBinding:
                    if (!TryLowerArrayBindingWithYieldDefaults(
                            arrayBinding,
                            varKind,
                            tempSymbol,
                            statements,
                            isStrict))
                    {
                        return false;
                    }

                    break;

                case ObjectBinding objectBinding:
                    if (!TryLowerObjectBindingWithYieldDefaults(
                            objectBinding,
                            varKind,
                            tempSymbol,
                            statements,
                            isStrict))
                    {
                        return false;
                    }

                    break;

                default:
                    return false;
            }

            replacement = statements.ToImmutable();
            return true;
        }

        private bool TryRewriteAssignmentToDestructuringWithYield(
            StatementNode statement,
            bool isStrict,
            out ImmutableArray<StatementNode> replacement)
        {
            replacement = default;

            if (statement is not ExpressionStatement
                {
                    Expression: AssignmentExpression
                    {
                        Value: DestructuringAssignmentExpression destructuringAssignment
                    } outerAssignment
                } expressionStatement)
            {
                return false;
            }

            if (destructuringAssignment.Target is not (ArrayBinding or ObjectBinding))
            {
                return false;
            }

            if (!AstShapeAnalyzer.BindingTargetContainsYieldInDefaultValue(destructuringAssignment.Target) &&
                !BindingTargetContainsYieldInAssignmentTarget(destructuringAssignment.Target))
            {
                return false;
            }

            var statements = ImmutableArray.CreateBuilder<StatementNode>();
            var tempSymbol = CreateResumeIdentifier();
            statements.Add(new VariableDeclaration(
                expressionStatement.Source,
                VariableKind.Let,
                [new VariableDeclarator(expressionStatement.Source, tempSymbol, destructuringAssignment.Value)]));

            switch (destructuringAssignment.Target)
            {
                case ArrayBinding arrayBinding:
                    if (!TryLowerArrayBindingWithYieldDefaults(arrayBinding, null, tempSymbol, statements, isStrict))
                    {
                        return false;
                    }

                    break;

                case ObjectBinding objectBinding:
                    if (!TryLowerObjectBindingWithYieldDefaults(objectBinding, null, tempSymbol, statements, isStrict))
                    {
                        return false;
                    }

                    break;

                default:
                    return false;
            }

            statements.Add(new ExpressionStatement(
                expressionStatement.Source,
                outerAssignment with
                {
                    Value = new IdentifierExpression(expressionStatement.Source, tempSymbol.Name)
                }));

            replacement = statements.ToImmutable();
            return true;
        }

        /// <summary>
        /// Lowers an array binding pattern with yield defaults into explicit iterator operations.
        /// </summary>
        /// <param name="arrayBinding">The array binding pattern to lower.</param>
        /// <param name="varKind">The variable kind for declarations, or null for assignment context.</param>
        /// <param name="sourceSymbol">The source value symbol.</param>
        /// <param name="statements">The statement builder to add lowered statements to.</param>
        /// <param name="isStrict">Whether this is in strict mode.</param>
        private bool TryLowerArrayBindingWithYieldDefaults(
            ArrayBinding arrayBinding,
            VariableKind? varKind,
            IdentifierBinding sourceSymbol,
            ImmutableArray<StatementNode>.Builder statements,
            bool isStrict)
        {
            // Step 1: Create the iterator
            // let __iter = __temp[Symbol.iterator]();
            var iterSymbol = CreateResumeIdentifier();
            // Symbol.iterator access: get the iterator well-known symbol
            var symbolIteratorAccess = new MemberExpression(
                null,
                new IdentifierExpression(null, Symbol.Intern("Symbol")),
                new LiteralExpression(null, JsValue.FromString("iterator")),
                false,
                false);
            var getIteratorCall = new CallExpression(
                null,
                new MemberExpression(
                    null,
                    new IdentifierExpression(null, sourceSymbol.Name),
                    symbolIteratorAccess,
                    true,  // computed: __temp[Symbol.iterator]
                    false),
                ImmutableArray<CallArgument>.Empty,
                false);

            statements.Add(new VariableDeclaration(
                null,
                VariableKind.Let,
                [new VariableDeclarator(null, iterSymbol, getIteratorCall)]));

            // Step 2: Process each element
            for (var i = 0; i < arrayBinding.Elements.Length; i++)
            {
                var element = arrayBinding.Elements[i];

                // Handle elision (empty slot like [,a])
                if (element.Target is null)
                {
                    // Just advance the iterator
                    // let __skipN = __iter.next();
                    var skipSymbol = CreateResumeIdentifier();
                    var skipNextCall = new CallExpression(
                        null,
                        new MemberExpression(
                            null,
                            new IdentifierExpression(null, iterSymbol.Name),
                            new LiteralExpression(null, JsValue.FromString("next")),
                            false,
                            false),
                        ImmutableArray<CallArgument>.Empty,
                        false);
                    statements.Add(new VariableDeclaration(
                        null,
                        VariableKind.Let,
                        [new VariableDeclarator(null, skipSymbol, skipNextCall)]));
                    continue;
                }

                // Step 2a: Call iterator.next()
                // let __nextN = __iter.next();
                var nextSymbol = CreateResumeIdentifier();
                var nextCall = new CallExpression(
                    null,
                    new MemberExpression(
                        null,
                        new IdentifierExpression(null, iterSymbol.Name),
                        new LiteralExpression(null, JsValue.FromString("next")),
                        false,
                        false),
                    ImmutableArray<CallArgument>.Empty,
                    false);
                statements.Add(new VariableDeclaration(
                    null,
                    VariableKind.Let,
                    [new VariableDeclarator(null, nextSymbol, nextCall)]));

                // Step 2b: Extract value: __valN = __nextN.done ? undefined : __nextN.value
                var valSymbol = CreateResumeIdentifier();
                var doneAccess = new MemberExpression(
                    null,
                    new IdentifierExpression(null, nextSymbol.Name),
                    new LiteralExpression(null, JsValue.FromString("done")),
                    false,
                    false);
                var valueAccess = new MemberExpression(
                    null,
                    new IdentifierExpression(null, nextSymbol.Name),
                    new LiteralExpression(null, JsValue.FromString("value")),
                    false,
                    false);
                var valConditional = new ConditionalExpression(
                    null,
                    doneAccess,
                    new IdentifierExpression(null, Symbol.Intern("undefined")),
                    valueAccess);
                statements.Add(new VariableDeclaration(
                    null,
                    VariableKind.Let,
                    [new VariableDeclarator(null, valSymbol, valConditional)]));

                // Step 2c: Handle the binding target with default value
                if (!TryLowerBindingTargetWithOptionalDefault(
                        element.Target,
                        element.DefaultValue,
                        varKind,
                        valSymbol,
                        statements,
                        isStrict))
                {
                    return false;
                }
            }

            // Step 3: Handle rest element if present
            if (arrayBinding.RestElement is not null)
            {
                // For rest element, collect remaining values into an array
                // This is more complex - for now, return false to fall back to AST evaluation
                // TODO: Implement rest element lowering
                return false;
            }

            return true;
        }

        /// <summary>
        /// Lowers an object binding pattern with yield defaults.
        /// </summary>
        /// <param name="objectBinding">The object binding pattern to lower.</param>
        /// <param name="varKind">The variable kind for declarations, or null for assignment context.</param>
        /// <param name="sourceSymbol">The source value symbol.</param>
        /// <param name="statements">The statement builder to add lowered statements to.</param>
        /// <param name="isStrict">Whether this is in strict mode.</param>
        private bool TryLowerObjectBindingWithYieldDefaults(
            ObjectBinding objectBinding,
            VariableKind? varKind,
            IdentifierBinding sourceSymbol,
            ImmutableArray<StatementNode>.Builder statements,
            bool isStrict)
        {
            // Process each property
            foreach (var prop in objectBinding.Properties)
            {
                // Step 1: Get the property value
                // let __propVal = __temp.propertyName;
                var propValSymbol = CreateResumeIdentifier();
                // Use computed access for dynamic names, static access for literal names
                var isComputed = prop.NameExpression is not null;
                var propExpr = isComputed
                    ? prop.NameExpression!
                    : new LiteralExpression(null, JsValue.FromString(prop.Name));
                var propertyAccess = new MemberExpression(
                    null,
                    new IdentifierExpression(null, sourceSymbol.Name),
                    propExpr,
                    isComputed,
                    false);

                statements.Add(new VariableDeclaration(
                    null,
                    VariableKind.Let,
                    [new VariableDeclarator(null, propValSymbol, propertyAccess)]));

                // Step 2: Handle the binding target with default value
                if (!TryLowerBindingTargetWithOptionalDefault(
                        prop.Target,
                        prop.DefaultValue,
                        varKind,
                        propValSymbol,
                        statements,
                        isStrict))
                {
                    return false;
                }
            }

            // Handle rest element if present
            if (objectBinding.RestElement is not null)
            {
                // For rest element in objects, we need to collect remaining enumerable own properties
                // This is complex - for now, return false to fall back to AST evaluation
                // TODO: Implement rest element lowering
                return false;
            }

            return true;
        }

        /// <summary>
        /// Lowers a binding target with an optional default value.
        /// If the default value contains a yield, generates:
        ///   let targetName;
        ///   if (__val === undefined) { targetName = yield expr; } else { targetName = __val; }
        /// Otherwise, generates:
        ///   let targetName = __val === undefined ? defaultExpr : __val;
        /// For assignment context (varKind is null), uses assignment instead of declaration.
        /// </summary>
        /// <param name="target">The binding target.</param>
        /// <param name="defaultValue">Optional default value expression.</param>
        /// <param name="varKind">The variable kind for declarations, or null for assignment context.</param>
        /// <param name="valueSymbol">The symbol holding the value to destructure.</param>
        /// <param name="statements">The statement builder.</param>
        /// <param name="isStrict">Whether in strict mode.</param>
        private bool TryLowerBindingTargetWithOptionalDefault(
            BindingTarget target,
            ExpressionNode? defaultValue,
            VariableKind? varKind,
            IdentifierBinding valueSymbol,
            ImmutableArray<StatementNode>.Builder statements,
            bool isStrict)
        {
            // Handle nested destructuring patterns recursively
            switch (target)
            {
                case IdentifierBinding identifierBinding:
                    return TryLowerIdentifierBindingWithDefault(
                        identifierBinding,
                        defaultValue,
                        varKind,
                        valueSymbol,
                        statements,
                        isStrict);

                case ArrayBinding nestedArray:
                    // For nested arrays, we need to first check if value is undefined and apply default
                    // then destructure the result
                    if (defaultValue is not null && AstShapeAnalyzer.ContainsYield(defaultValue))
                    {
                        return TryHandleNestedBindingWithYieldDefault(
                            defaultValue,
                            valueSymbol,
                            statements,
                            isStrict,
                            resolved => TryLowerArrayBindingWithYieldDefaults(
                                nestedArray,
                                varKind,
                                resolved,
                                statements,
                                isStrict));
                    }
                    else
                    {
                        // No yield in default - apply default inline if needed, then destructure
                        var sourceForNested = PrepareDefaultValueSource(defaultValue, valueSymbol, statements);
                        return TryLowerArrayBindingWithYieldDefaults(
                            nestedArray,
                            varKind,
                            sourceForNested,
                            statements,
                            isStrict);
                    }

                case ObjectBinding nestedObject:
                    // Similar handling for nested objects
                    if (defaultValue is not null && AstShapeAnalyzer.ContainsYield(defaultValue))
                    {
                        return TryHandleNestedBindingWithYieldDefault(
                            defaultValue,
                            valueSymbol,
                            statements,
                            isStrict,
                            resolved => TryLowerObjectBindingWithYieldDefaults(
                                nestedObject,
                                varKind,
                                resolved,
                                statements,
                                isStrict));
                    }
                    else
                    {
                        var sourceForNested = PrepareDefaultValueSource(defaultValue, valueSymbol, statements);
                        return TryLowerObjectBindingWithYieldDefaults(
                            nestedObject,
                            varKind,
                            sourceForNested,
                            statements,
                            isStrict);
                    }

                case AssignmentTargetBinding assignmentTarget:
                    if (varKind is not null)
                    {
                        return false;
                    }

                    return TryLowerAssignmentTargetBindingWithDefault(
                        assignmentTarget,
                        defaultValue,
                        valueSymbol,
                        statements,
                        isStrict);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Lowers an identifier binding with an optional default value.
        /// </summary>
        /// <param name="identifierBinding">The identifier to bind to.</param>
        /// <param name="defaultValue">Optional default value expression.</param>
        /// <param name="varKind">The variable kind for declarations, or null for assignment context.</param>
        /// <param name="valueSymbol">The symbol holding the source value.</param>
        /// <param name="statements">The statement builder.</param>
        /// <param name="isStrict">Whether in strict mode.</param>
        private bool TryLowerIdentifierBindingWithDefault(
            IdentifierBinding identifierBinding,
            ExpressionNode? defaultValue,
            VariableKind? varKind,
            IdentifierBinding valueSymbol,
            ImmutableArray<StatementNode>.Builder statements,
            bool isStrict)
        {
            // Assignment context (varKind is null): use assignment expressions instead of declarations
            if (varKind is null)
            {
                return TryLowerIdentifierBindingWithDefaultAssignment(
                    identifierBinding,
                    defaultValue,
                    valueSymbol,
                    statements,
                    isStrict);
            }

            if (defaultValue is null)
            {
                // No default: just assign the value
                // let targetName = __val;
                statements.Add(new VariableDeclaration(
                    null,
                    varKind.Value,
                    [new VariableDeclarator(
                        null,
                        identifierBinding,
                        new IdentifierExpression(null, valueSymbol.Name))]));
                return true;
            }

            if (!AstShapeAnalyzer.ContainsYield(defaultValue))
            {
                // No yield in default: use conditional expression
                // let targetName = __val === undefined ? defaultExpr : __val;
                var conditional = new ConditionalExpression(
                    null,
                    new BinaryExpression(
                        null,
                        BinaryOperator.StrictEqual,
                        new IdentifierExpression(null, valueSymbol.Name),
                        new IdentifierExpression(null, Symbol.Intern("undefined"))),
                    defaultValue,
                    new IdentifierExpression(null, valueSymbol.Name));

                statements.Add(new VariableDeclaration(
                    null,
                    varKind.Value,
                    [new VariableDeclarator(null, identifierBinding, conditional)]));
                return true;
            }

            // Has yield in default: use if statement
            // let targetName;
            // Note: Always use Let for uninitialized temporaries (const x; is invalid JS)
            statements.Add(new VariableDeclaration(
                null,
                VariableKind.Let,
                [new VariableDeclarator(null, identifierBinding, null)]));

            // if (__val === undefined) { targetName = yield expr; } else { targetName = __val; }
            return EmitYieldDefaultConditional(
                identifierBinding,
                defaultValue,
                valueSymbol,
                statements,
                isStrict);
        }

        /// <summary>
        /// Lowers an identifier binding with default in assignment context (no new declaration).
        /// </summary>
        private bool TryLowerIdentifierBindingWithDefaultAssignment(
            IdentifierBinding identifierBinding,
            ExpressionNode? defaultValue,
            IdentifierBinding valueSymbol,
            ImmutableArray<StatementNode>.Builder statements,
            bool isStrict)
        {
            if (defaultValue is null)
            {
                // No default: just assign the value
                // targetName = __val;
                statements.Add(new ExpressionStatement(
                    null,
                    new AssignmentExpression(
                        null,
                        identifierBinding.Name,
                        new IdentifierExpression(null, valueSymbol.Name))));
                return true;
            }

            if (!AstShapeAnalyzer.ContainsYield(defaultValue))
            {
                // No yield in default: use conditional expression
                // targetName = __val === undefined ? defaultExpr : __val;
                var conditional = new ConditionalExpression(
                    null,
                    new BinaryExpression(
                        null,
                        BinaryOperator.StrictEqual,
                        new IdentifierExpression(null, valueSymbol.Name),
                        new IdentifierExpression(null, Symbol.Intern("undefined"))),
                    defaultValue,
                    new IdentifierExpression(null, valueSymbol.Name));

                statements.Add(new ExpressionStatement(
                    null,
                    new AssignmentExpression(
                        null,
                        identifierBinding.Name,
                        conditional)));
                return true;
            }

            // Has yield in default: use if statement (no declaration needed - variable already exists)
            // if (__val === undefined) { targetName = yield expr; } else { targetName = __val; }
            return EmitYieldDefaultConditional(
                identifierBinding,
                defaultValue,
                valueSymbol,
                statements,
                isStrict);
        }

        private bool TryLowerAssignmentTargetBindingWithDefault(
            AssignmentTargetBinding assignmentTarget,
            ExpressionNode? defaultValue,
            IdentifierBinding valueSymbol,
            ImmutableArray<StatementNode>.Builder statements,
            bool isStrict)
        {
            if (defaultValue is null)
            {
                return TryEmitAssignmentTargetBindingAssignment(
                    assignmentTarget,
                    new IdentifierExpression(null, valueSymbol.Name),
                    statements);
            }

            if (!AstShapeAnalyzer.ContainsYield(defaultValue))
            {
                var resolvedSymbol = CreateResumeIdentifier();
                var conditional = new ConditionalExpression(
                    null,
                    new BinaryExpression(
                        null,
                        BinaryOperator.StrictEqual,
                        new IdentifierExpression(null, valueSymbol.Name),
                        new IdentifierExpression(null, Symbol.Intern("undefined"))),
                    defaultValue,
                    new IdentifierExpression(null, valueSymbol.Name));

                statements.Add(new VariableDeclaration(
                    null,
                    VariableKind.Let,
                    [new VariableDeclarator(null, resolvedSymbol, conditional)]));

                return TryEmitAssignmentTargetBindingAssignment(
                    assignmentTarget,
                    new IdentifierExpression(null, resolvedSymbol.Name),
                    statements);
            }

            var resolvedYieldSymbol = CreateResumeIdentifier();
            statements.Add(new VariableDeclaration(
                null,
                VariableKind.Let,
                [new VariableDeclarator(null, resolvedYieldSymbol, null)]));

            if (!EmitYieldDefaultConditional(
                    resolvedYieldSymbol,
                    defaultValue,
                    valueSymbol,
                    statements,
                    isStrict))
            {
                return false;
            }

            return TryEmitAssignmentTargetBindingAssignment(
                assignmentTarget,
                new IdentifierExpression(null, resolvedYieldSymbol.Name),
                statements);
        }

        private bool TryEmitAssignmentTargetBindingAssignment(
            AssignmentTargetBinding assignmentTarget,
            ExpressionNode valueExpression,
            ImmutableArray<StatementNode>.Builder statements)
        {
            var prefixStatements = ImmutableArray.CreateBuilder<StatementNode>();
            var changed = false;
            var rewrittenTarget =
                RewriteExpressionForComplexYields(assignmentTarget.Expression, prefixStatements, ref changed);

            if (AstShapeAnalyzer.ContainsYield(rewrittenTarget))
            {
                return false;
            }

            statements.AddRange(prefixStatements);
            statements.Add(new ExpressionStatement(
                null,
                CreateAssignmentExpressionFromLhs(rewrittenTarget, valueExpression)));
            return true;
        }

        private IdentifierBinding PrepareDefaultValueSource(
            ExpressionNode? defaultValue,
            IdentifierBinding valueSymbol,
            ImmutableArray<StatementNode>.Builder statements)
        {
            var sourceForNested = valueSymbol;
            if (defaultValue is not null)
            {
                var resolvedSymbol = CreateResumeIdentifier();
                var conditional = new ConditionalExpression(
                    null,
                    new BinaryExpression(
                        null,
                        BinaryOperator.StrictEqual,
                        new IdentifierExpression(null, valueSymbol.Name),
                        new IdentifierExpression(null, Symbol.Intern("undefined"))),
                defaultValue,
                    new IdentifierExpression(null, valueSymbol.Name));
                statements.Add(new VariableDeclaration(
                    null,
                    VariableKind.Let,
                    [new VariableDeclarator(null, resolvedSymbol, conditional)]));
                sourceForNested = resolvedSymbol;
            }

            return sourceForNested;
        }

        private bool TryHandleNestedBindingWithYieldDefault(
            ExpressionNode defaultValue,
            IdentifierBinding valueSymbol,
            ImmutableArray<StatementNode>.Builder statements,
            bool isStrict,
            Func<IdentifierBinding, bool> nestedLowering)
        {
            var resolvedSymbol = CreateResumeIdentifier();
            statements.Add(new VariableDeclaration(
                null,
                VariableKind.Let,
                [new VariableDeclarator(null, resolvedSymbol, null)]));

            if (!EmitYieldDefaultConditional(
                    resolvedSymbol,
                    defaultValue,
                    valueSymbol,
                    statements,
                    isStrict))
            {
                return false;
            }

            return nestedLowering(resolvedSymbol);
        }

        /// <summary>
        /// Emits an if statement that handles a yield in a default value:
        /// if (__val === undefined) { targetName = yield expr; } else { targetName = __val; }
        /// </summary>
        private bool EmitYieldDefaultConditional(
            IdentifierBinding targetBinding,
            ExpressionNode defaultValue,
            IdentifierBinding valueSymbol,
            ImmutableArray<StatementNode>.Builder statements,
            bool isStrict)
        {
            return EmitYieldDefaultConditional(
                new IdentifierExpression(null, targetBinding.Name),
                defaultValue,
                valueSymbol,
                statements,
                isStrict);
        }

        private bool EmitYieldDefaultConditional(
            ExpressionNode targetExpression,
            ExpressionNode defaultValue,
            IdentifierBinding valueSymbol,
            ImmutableArray<StatementNode>.Builder statements,
            bool isStrict)
        {
            // Handle yields in the default expression by extracting them
            var defaultPrefixStatements = ImmutableArray.CreateBuilder<StatementNode>();
            var changed = false;
            var rewrittenDefault = RewriteExpressionForComplexYields(defaultValue, defaultPrefixStatements, ref changed);

            // Build the consequent (then) branch
            var consequentStatements = ImmutableArray.CreateBuilder<StatementNode>();
            consequentStatements.AddRange(defaultPrefixStatements);
            consequentStatements.Add(new ExpressionStatement(
                null,
                CreateAssignmentExpressionFromLhs(targetExpression, rewrittenDefault)));

            var consequentBlock = new BlockStatement(null, consequentStatements.ToImmutable(), isStrict);

            // Build the alternate (else) branch
            var alternateBlock = new BlockStatement(
                null,
                [
                    new ExpressionStatement(
                        null,
                        CreateAssignmentExpressionFromLhs(
                            targetExpression,
                            new IdentifierExpression(null, valueSymbol.Name)))
                ],
                isStrict);

            // Build the condition: __val === undefined
            var condition = new BinaryExpression(
                null,
                BinaryOperator.StrictEqual,
                new IdentifierExpression(null, valueSymbol.Name),
                new IdentifierExpression(null, Symbol.Intern("undefined")));

            // Create the if statement
            statements.Add(new IfStatement(null, condition, consequentBlock, alternateBlock));
            return true;
        }

        private static ExpressionNode CreateAssignmentExpressionFromLhs(ExpressionNode lhs, ExpressionNode value)
        {
            return lhs switch
            {
                IdentifierExpression id => new AssignmentExpression(lhs.Source, id.Name, value),
                MemberExpression member => new PropertyAssignmentExpression(
                    lhs.Source,
                    member.Target,
                    member.Property,
                    value,
                    member.IsComputed),
                _ => throw new NotSupportedException(
                    $"Unsupported destructuring assignment target '{lhs.GetType().Name}'.")
            };
        }

        private bool TryRewriteYieldingAssignment(StatementNode statement,
            out ImmutableArray<StatementNode> replacement)
        {
            if (statement is not ExpressionStatement
                {
                    Expression: AssignmentExpression { Value: YieldExpression yieldExpression } assignment
                } expressionStatement)
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
                            Condition = rewrittenCondition,
                            Then = rewrittenThen,
                            Else = rewrittenElse
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

                    if (incrementHasYield &&
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
                    [new VariableDeclarator(conditionYield.Source, conditionResumeIdentifier!, null)]));
            }

            if (incrementHasYield && incrementYield is not null)
            {
                statements.Add(new VariableDeclaration(incrementYield.Source, VariableKind.Let,
                    [new VariableDeclarator(incrementYield.Source, incrementResumeIdentifier!, null)]));
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
                    new UnaryExpression(forStatement.Source, UnaryOperator.LogicalNot, rewrittenCondition!, true),
                    new BreakStatement(forStatement.Source, null),
                    null));
            }
            else if (forStatement.Condition is not null)
            {
                loopStatements.Add(new IfStatement(forStatement.Source,
                    new UnaryExpression(forStatement.Source, UnaryOperator.LogicalNot, forStatement.Condition, true),
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

        /// <summary>
        ///     Rewrites for-of/for-await-of statements that have yields in the binding target.
        ///     For yields in default values (e.g., for (let [x = yield 1] of iter)):
        ///       for (let __iterTemp of iter) {
        ///         let __iter = __iterTemp[Symbol.iterator]();
        ///         let __next0 = __iter.next();
        ///         let __val0 = __next0.done ? undefined : __next0.value;
        ///         let x;
        ///         if (__val0 === undefined) { x = yield 1; } else { x = __val0; }
        ///         // original body
        ///       }
        ///     For yields in non-default positions, yields are extracted to prefix statements.
        /// </summary>
        private bool TryRewriteForEachWithYield(
            StatementNode statement,
            bool isStrict,
            out ImmutableArray<StatementNode> replacement)
        {
            replacement = default;

            if (statement is not ForEachStatement forEachStatement)
            {
                return false;
            }

            // Check if the binding target contains yields
            if (!BindingTargetContainsYield(forEachStatement.Target))
            {
                return false;
            }

            // If yields are in assignment target expressions (like [ ...{}[yield] ]), we
            // cannot extract them. The yield must happen AFTER the for-of's outer iterator
            // begins so that iterator close semantics work correctly.
            if (BindingTargetContainsYieldInAssignmentTarget(forEachStatement.Target))
            {
                return false;
            }

            // Check if yields are in default values - requires full destructuring lowering
            var hasYieldInDefaults = AstShapeAnalyzer.BindingTargetContainsYieldInDefaultValue(forEachStatement.Target);

            // For for-of loops, we can't safely lower nested binding patterns (ArrayBinding/ObjectBinding)
            // that have yields in their defaults, because the iterator close semantics are complex.
            // When generator.return() is called while suspended at a yield, both the for-of iterator
            // AND any nested destructuring iterators need to be closed properly.
            // The AST evaluation path handles this correctly via state-saving.
            // Example: for ([ {} = yield ] of [iterable]) - the {} creates a nested iterator
            if (hasYieldInDefaults && BindingHasNestedPatternWithYieldInDefault(forEachStatement.Target))
            {
                return false;
            }

            // Create a temporary identifier for the iteration value
            var iterTemp = CreateResumeIdentifier();

            // Build the new loop body statements
            var newBodyStatements = ImmutableArray.CreateBuilder<StatementNode>();

            if (hasYieldInDefaults)
            {
                // For yields in defaults, we need to fully lower the destructuring using
                // the same approach as TryRewriteDestructuringWithYieldDefaults.
                // This generates explicit iterator/property access with conditional yields.
                // Pass null for varKind when this is an assignment (non-declaration) for-of,
                // so that TryLowerIdentifierBindingWithDefault uses assignment instead of declaration.
                var varKind = forEachStatement.DeclarationKind;

                switch (forEachStatement.Target)
                {
                    case ArrayBinding arrayBinding:
                        if (!TryLowerArrayBindingWithYieldDefaults(
                                arrayBinding,
                                varKind,
                                iterTemp,
                                newBodyStatements,
                                isStrict))
                        {
                            return false;
                        }

                        break;

                    case ObjectBinding objectBinding:
                        if (!TryLowerObjectBindingWithYieldDefaults(
                                objectBinding,
                                varKind,
                                iterTemp,
                                newBodyStatements,
                                isStrict))
                        {
                            return false;
                        }

                        break;

                    default:
                        // IdentifierBinding with yield in default is not possible
                        return false;
                }
            }
            else
            {
                // Extract yields from the binding target and rewrite the target
                var prefixStatements = ImmutableArray.CreateBuilder<StatementNode>();
                var changed = false;
                var rewrittenTarget = RewriteBindingTarget(forEachStatement.Target, prefixStatements, ref changed);

                if (!changed)
                {
                    return false;
                }

                // Add the prefix statements that extract yields
                newBodyStatements.AddRange(prefixStatements);

                // Add destructuring assignment: let [x = __yield0] = __iterTemp;
                // or for non-declaration for-of: [x = __yield0] = __iterTemp;
                if (forEachStatement.DeclarationKind is not null)
                {
                    // For 'for (let/const/var [x = yield] of ...)' - use variable declaration
                    var destructuringDeclarator = new VariableDeclarator(
                        forEachStatement.Source,
                        rewrittenTarget,
                        new IdentifierExpression(forEachStatement.Source, iterTemp.Name));
                    newBodyStatements.Add(new VariableDeclaration(
                        forEachStatement.Source,
                        forEachStatement.DeclarationKind.Value,
                        [destructuringDeclarator]));
                }
                else
                {
                    // For 'for ([x = yield] of ...)' - use destructuring assignment expression
                    var destructuringAssignment = new DestructuringAssignmentExpression(
                        forEachStatement.Source,
                        rewrittenTarget,
                        new IdentifierExpression(forEachStatement.Source, iterTemp.Name));
                    newBodyStatements.Add(new ExpressionStatement(forEachStatement.Source, destructuringAssignment));
                }
            }

            // Add the original body
            if (forEachStatement.Body is BlockStatement originalBlock)
            {
                var rewrittenBlock = RewriteBlock(originalBlock);
                newBodyStatements.AddRange(rewrittenBlock.Statements);
            }
            else
            {
                var rewrittenBody = RewriteStatements([forEachStatement.Body], isStrict);
                newBodyStatements.AddRange(rewrittenBody);
            }

            // Create the new loop with a simple identifier target
            var newBody = new BlockStatement(
                forEachStatement.Source,
                newBodyStatements.ToImmutable(),
                isStrict);

            var newTarget = new IdentifierBinding(forEachStatement.Source, iterTemp.Name);
            var newForEach = forEachStatement with
            {
                Target = newTarget,
                Body = newBody,
                DeclarationKind = VariableKind.Let // Always use let for the temp binding
            };

            replacement = [newForEach];
            return true;
        }

        private static bool BindingTargetContainsYield(BindingTarget target)
        {
            switch (target)
            {
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(element.DefaultValue))
                        {
                            return true;
                        }

                        if (element.Target is not null && BindingTargetContainsYield(element.Target))
                        {
                            return true;
                        }
                    }

                    return arrayBinding.RestElement is not null && BindingTargetContainsYield(arrayBinding.RestElement);

                case ObjectBinding objectBinding:
                    foreach (var prop in objectBinding.Properties)
                    {
                        if (prop.DefaultValue is not null && AstShapeAnalyzer.ContainsYield(prop.DefaultValue))
                        {
                            return true;
                        }

                        if (prop.NameExpression is not null && AstShapeAnalyzer.ContainsYield(prop.NameExpression))
                        {
                            return true;
                        }

                        if (BindingTargetContainsYield(prop.Target))
                        {
                            return true;
                        }
                    }

                    return objectBinding.RestElement is not null && BindingTargetContainsYield(objectBinding.RestElement);

                case AssignmentTargetBinding assignmentTarget:
                    // Check if the expression (e.g., x[yield] or x[yield + 1]) contains a yield
                    return AstShapeAnalyzer.ContainsYield(assignmentTarget.Expression);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Checks if a binding target contains yields in AssignmentTargetBinding expressions.
        /// These yields cannot be safely extracted because they must execute AFTER the iterator
        /// is opened during destructuring. Extracting them would cause the yield to happen
        /// before the iterator exists, breaking iterator close semantics.
        /// Example: [ obj[ yield ] ] = iterable  - the yield must happen after iterable's iterator is created
        /// </summary>
        private static bool BindingTargetContainsYieldInAssignmentTarget(BindingTarget target)
        {
            switch (target)
            {
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        // Recursively check nested bindings
                        if (element.Target is not null && BindingTargetContainsYieldInAssignmentTarget(element.Target))
                        {
                            return true;
                        }
                    }

                    return arrayBinding.RestElement is not null &&
                        BindingTargetContainsYieldInAssignmentTarget(arrayBinding.RestElement);

                case ObjectBinding objectBinding:
                    foreach (var prop in objectBinding.Properties)
                    {
                        // Recursively check nested bindings
                        if (BindingTargetContainsYieldInAssignmentTarget(prop.Target))
                        {
                            return true;
                        }
                    }

                    return objectBinding.RestElement is not null &&
                        BindingTargetContainsYieldInAssignmentTarget(objectBinding.RestElement);

                case AssignmentTargetBinding assignmentTarget:
                    // Check if this assignment target's expression contains a yield
                    return AstShapeAnalyzer.ContainsYield(assignmentTarget.Expression);

                default:
                    // IdentifierBinding doesn't have expressions
                    return false;
            }
        }

        /// <summary>
        /// Checks if a binding target has nested patterns (ArrayBinding/ObjectBinding) where
        /// the element or property has a yield in its default value.
        /// Example: [ {} = yield ] - the {} is an ObjectBinding with a yield default
        /// These cases are complex for iterator close semantics in for-of loops and should
        /// fall back to AST evaluation.
        /// </summary>
        private static bool BindingHasNestedPatternWithYieldInDefault(BindingTarget target)
        {
            switch (target)
            {
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        // Check if this element has a nested pattern with yield in default
                        if (element.Target is (ArrayBinding or ObjectBinding) &&
                            element.DefaultValue is not null &&
                            AstShapeAnalyzer.ContainsYield(element.DefaultValue))
                        {
                            return true;
                        }

                        // Recursively check nested bindings
                        if (element.Target is not null && BindingHasNestedPatternWithYieldInDefault(element.Target))
                        {
                            return true;
                        }
                    }

                    return arrayBinding.RestElement is not null &&
                        BindingHasNestedPatternWithYieldInDefault(arrayBinding.RestElement);

                case ObjectBinding objectBinding:
                    foreach (var prop in objectBinding.Properties)
                    {
                        // Check if this property has a nested pattern with yield in default
                        if (prop.Target is (ArrayBinding or ObjectBinding) &&
                            prop.DefaultValue is not null &&
                            AstShapeAnalyzer.ContainsYield(prop.DefaultValue))
                        {
                            return true;
                        }

                        // Recursively check nested bindings
                        if (BindingHasNestedPatternWithYieldInDefault(prop.Target))
                        {
                            return true;
                        }
                    }

                    return objectBinding.RestElement is not null &&
                        BindingHasNestedPatternWithYieldInDefault(objectBinding.RestElement);

                default:
                    // IdentifierBinding and AssignmentTargetBinding don't have nested patterns
                    return false;
            }
        }

        private static bool TryRewriteIncrementWithTwoYields(ExpressionNode expression,
            out YieldExpression leftYield, out YieldExpression rightYield, out BinaryExpression incrementBinary,
            out Symbol? assignmentTarget)
        {
            leftYield = null!;
            rightYield = null!;
            incrementBinary = null!;
            assignmentTarget = null;

            BinaryExpression? binary;
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
                        new UnaryExpression(plan.Body.Source, UnaryOperator.LogicalNot, conditionCheck, true),
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
