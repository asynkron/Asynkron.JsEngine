using System;
using System.Collections.Immutable;
using Asynkron.JsEngine.Lisp;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Experimental CPS (Continuation-Passing Style) transformer that works directly
/// on the typed AST. The goal is to explore what a typed-first transformation
/// would look like, not to replace the production S-expression implementation.
/// For now only simple async function declarations that immediately <c>return</c>
/// an <c>await</c> expression are supported.
/// </summary>
public sealed class TypedCpsTransformer
{
    private static readonly Symbol PromiseIdentifier = Symbol.Intern("Promise");
    private static readonly Symbol ResolveIdentifier = Symbol.Intern("__resolve");
    private static readonly Symbol RejectIdentifier = Symbol.Intern("__reject");
    private static readonly Symbol AwaitHelperIdentifier = Symbol.Intern("__awaitHelper");
    private static readonly Symbol AwaitValueIdentifier = Symbol.Intern("__value");
    private static readonly Symbol CatchIdentifier = Symbol.Intern("__error");
    private const string ThenPropertyName = "then";

    /// <summary>
    /// Returns true when the typed program contains async functions that would
    /// require CPS transformation. The current implementation only looks for
    /// function declarations because that's the only construct the transformer
    /// understands today.
    /// </summary>
    public static bool NeedsTransformation(ProgramNode program)
    {
        foreach (var statement in program.Body)
        {
            if (StatementNeedsTransformation(statement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatementNeedsTransformation(StatementNode statement)
    {
        switch (statement)
        {
            case FunctionDeclaration { Function.IsAsync: true }:
                return true;
            case FunctionDeclaration functionDeclaration:
                return FunctionNeedsTransformation(functionDeclaration.Function);
            case VariableDeclaration variableDeclaration:
                foreach (var declarator in variableDeclaration.Declarators)
                {
                    if (declarator.Initializer is not null && ExpressionNeedsTransformation(declarator.Initializer))
                    {
                        return true;
                    }
                }

                return false;
            case ExpressionStatement expressionStatement:
                return ExpressionNeedsTransformation(expressionStatement.Expression);
            case ReturnStatement { Expression: { } expression }:
                return ExpressionNeedsTransformation(expression);
            case BlockStatement block:
                foreach (var child in block.Statements)
                {
                    if (StatementNeedsTransformation(child))
                    {
                        return true;
                    }
                }

                return false;
            case IfStatement ifStatement:
                return ExpressionNeedsTransformation(ifStatement.Condition) ||
                       StatementNeedsTransformation(ifStatement.Then) ||
                       (ifStatement.Else is not null && StatementNeedsTransformation(ifStatement.Else));
            case WhileStatement whileStatement:
                return ExpressionNeedsTransformation(whileStatement.Condition) ||
                       StatementNeedsTransformation(whileStatement.Body);
            case DoWhileStatement doWhileStatement:
                return StatementNeedsTransformation(doWhileStatement.Body) ||
                       ExpressionNeedsTransformation(doWhileStatement.Condition);
            case ForStatement forStatement:
                return (forStatement.Initializer is not null && StatementNeedsTransformation(forStatement.Initializer)) ||
                       (forStatement.Condition is not null && ExpressionNeedsTransformation(forStatement.Condition)) ||
                       (forStatement.Increment is not null && ExpressionNeedsTransformation(forStatement.Increment)) ||
                       StatementNeedsTransformation(forStatement.Body);
            case ForEachStatement forEachStatement:
                return ExpressionNeedsTransformation(forEachStatement.Iterable) ||
                       StatementNeedsTransformation(forEachStatement.Body);
            case LabeledStatement labeledStatement:
                return StatementNeedsTransformation(labeledStatement.Statement);
            case TryStatement tryStatement:
                if (StatementNeedsTransformation(tryStatement.TryBlock))
                {
                    return true;
                }

                if (tryStatement.Catch is not null && StatementNeedsTransformation(tryStatement.Catch.Body))
                {
                    return true;
                }

                return tryStatement.Finally is not null && StatementNeedsTransformation(tryStatement.Finally);
            case SwitchStatement switchStatement:
                if (ExpressionNeedsTransformation(switchStatement.Discriminant))
                {
                    return true;
                }

                foreach (var switchCase in switchStatement.Cases)
                {
                    if (switchCase.Test is not null && ExpressionNeedsTransformation(switchCase.Test))
                    {
                        return true;
                    }

                    if (StatementNeedsTransformation(switchCase.Body))
                    {
                        return true;
                    }
                }

                return false;
        }

        return false;
    }

    private static bool CanRewriteStatement(StatementNode statement)
    {
        switch (statement)
        {
            case BlockStatement block:
                if (block.Statements.Length != 1)
                {
                    return false;
                }

                return CanRewriteStatement(block.Statements[0]);
            case ReturnStatement:
            case ExpressionStatement:
                return true;
            case VariableDeclaration:
                // Variable declarations are normalized to single declarators before
                // rewriting, so any awaits they contain are handled as expression
                // statements afterwards.
                return true;
            default:
                // Anything we don't explicitly support must not contain awaits.
                return !StatementNeedsTransformation(statement);
        }
    }

    private static bool FunctionNeedsTransformation(FunctionExpression function)
    {
        if (function.IsAsync)
        {
            return true;
        }

        return StatementNeedsTransformation(function.Body);
    }

    private static bool ExpressionNeedsTransformation(ExpressionNode expression)
    {
        switch (expression)
        {
            case AwaitExpression:
                return true;
            case FunctionExpression functionExpression:
                return functionExpression.IsAsync || StatementNeedsTransformation(functionExpression.Body);
            case BinaryExpression binaryExpression:
                return ExpressionNeedsTransformation(binaryExpression.Left) || ExpressionNeedsTransformation(binaryExpression.Right);
            case UnaryExpression unaryExpression:
                return ExpressionNeedsTransformation(unaryExpression.Operand);
            case ConditionalExpression conditionalExpression:
                return ExpressionNeedsTransformation(conditionalExpression.Test) ||
                       ExpressionNeedsTransformation(conditionalExpression.Consequent) ||
                       ExpressionNeedsTransformation(conditionalExpression.Alternate);
            case CallExpression callExpression:
                if (ExpressionNeedsTransformation(callExpression.Callee))
                {
                    return true;
                }

                foreach (var argument in callExpression.Arguments)
                {
                    if (ExpressionNeedsTransformation(argument.Expression))
                    {
                        return true;
                    }
                }

                return false;
            case NewExpression newExpression:
                if (ExpressionNeedsTransformation(newExpression.Constructor))
                {
                    return true;
                }

                foreach (var argument in newExpression.Arguments)
                {
                    if (ExpressionNeedsTransformation(argument))
                    {
                        return true;
                    }
                }

                return false;
            case MemberExpression memberExpression:
                return ExpressionNeedsTransformation(memberExpression.Target) ||
                       ExpressionNeedsTransformation(memberExpression.Property);
            case AssignmentExpression assignmentExpression:
                return ExpressionNeedsTransformation(assignmentExpression.Value);
            case PropertyAssignmentExpression propertyAssignmentExpression:
                return ExpressionNeedsTransformation(propertyAssignmentExpression.Value) ||
                       ExpressionNeedsTransformation(propertyAssignmentExpression.Target) ||
                       ExpressionNeedsTransformation(propertyAssignmentExpression.Property);
            case IndexAssignmentExpression indexAssignmentExpression:
                return ExpressionNeedsTransformation(indexAssignmentExpression.Value) ||
                       ExpressionNeedsTransformation(indexAssignmentExpression.Target) ||
                       ExpressionNeedsTransformation(indexAssignmentExpression.Index);
            case SequenceExpression sequenceExpression:
                return ExpressionNeedsTransformation(sequenceExpression.Left) ||
                       ExpressionNeedsTransformation(sequenceExpression.Right);
            case ArrayExpression arrayExpression:
                foreach (var element in arrayExpression.Elements)
                {
                    if (element.Expression is not null && ExpressionNeedsTransformation(element.Expression))
                    {
                        return true;
                    }
                }

                return false;
            case ObjectExpression objectExpression:
                foreach (var member in objectExpression.Members)
                {
                    if (member.Value is not null && ExpressionNeedsTransformation(member.Value))
                    {
                        return true;
                    }

                    if (member.Function is not null && FunctionNeedsTransformation(member.Function))
                    {
                        return true;
                    }
                }

                return false;
            case TemplateLiteralExpression templateLiteralExpression:
                foreach (var part in templateLiteralExpression.Parts)
                {
                    if (part.Expression is not null && ExpressionNeedsTransformation(part.Expression))
                    {
                        return true;
                    }
                }

                return false;
            case TaggedTemplateExpression taggedTemplateExpression:
                if (ExpressionNeedsTransformation(taggedTemplateExpression.Tag) ||
                    ExpressionNeedsTransformation(taggedTemplateExpression.StringsArray) ||
                    ExpressionNeedsTransformation(taggedTemplateExpression.RawStringsArray))
                {
                    return true;
                }

                foreach (var part in taggedTemplateExpression.Expressions)
                {
                    if (ExpressionNeedsTransformation(part))
                    {
                        return true;
                    }
                }

                return false;
        }

        return false;
    }

    /// <summary>
    /// Rewrites supported async functions in-place. Unsupported constructs are
    /// left untouched so callers can continue experimenting without risking the
    /// broader pipeline.
    /// </summary>
    public ProgramNode Transform(ProgramNode program)
    {
        var body = TransformImmutableArray(program.Body, TransformStatement, out var changed);
        return changed ? program with { Body = body } : program;
    }

    private StatementNode TransformStatement(StatementNode statement)
    {
        return statement switch
        {
            FunctionDeclaration declaration => TransformFunctionDeclaration(declaration),
            BlockStatement block => TransformBlock(block),
            ExpressionStatement expressionStatement => TransformExpressionStatement(expressionStatement),
            VariableDeclaration variableDeclaration => TransformVariableDeclaration(variableDeclaration),
            ReturnStatement returnStatement => TransformReturnStatement(returnStatement),
            IfStatement ifStatement => TransformIfStatement(ifStatement),
            WhileStatement whileStatement => TransformWhileStatement(whileStatement),
            DoWhileStatement doWhileStatement => TransformDoWhileStatement(doWhileStatement),
            ForStatement forStatement => TransformForStatement(forStatement),
            ForEachStatement forEachStatement => TransformForEachStatement(forEachStatement),
            LabeledStatement labeledStatement => TransformLabeledStatement(labeledStatement),
            TryStatement tryStatement => TransformTryStatement(tryStatement),
            SwitchStatement switchStatement => TransformSwitchStatement(switchStatement),
            _ => statement
        };
    }

    private StatementNode TransformFunctionDeclaration(FunctionDeclaration declaration)
    {
        var function = TransformFunctionExpression(declaration.Function);
        return ReferenceEquals(function, declaration.Function) ? declaration : declaration with { Function = function };
    }

    private BlockStatement TransformBlock(BlockStatement block)
    {
        var statements = TransformImmutableArray(block.Statements, TransformStatement, out var changed);
        return changed ? block with { Statements = statements } : block;
    }

    private ExpressionStatement TransformExpressionStatement(ExpressionStatement statement)
    {
        var expression = TransformExpression(statement.Expression);
        return ReferenceEquals(expression, statement.Expression) ? statement : statement with { Expression = expression };
    }

    private VariableDeclaration TransformVariableDeclaration(VariableDeclaration declaration)
    {
        if (declaration.Declarators.IsDefaultOrEmpty)
        {
            return declaration;
        }

        var builder = ImmutableArray.CreateBuilder<VariableDeclarator>(declaration.Declarators.Length);
        var changed = false;
        foreach (var declarator in declaration.Declarators)
        {
            var initializer = declarator.Initializer is null ? null : TransformExpression(declarator.Initializer);
            if (!ReferenceEquals(initializer, declarator.Initializer))
            {
                builder.Add(declarator with { Initializer = initializer });
                changed = true;
            }
            else
            {
                builder.Add(declarator);
            }
        }

        return changed ? declaration with { Declarators = builder.ToImmutable() } : declaration;
    }

    private ReturnStatement TransformReturnStatement(ReturnStatement statement)
    {
        if (statement.Expression is null)
        {
            return statement;
        }

        var expression = TransformExpression(statement.Expression);
        return ReferenceEquals(expression, statement.Expression) ? statement : statement with { Expression = expression };
    }

    private IfStatement TransformIfStatement(IfStatement statement)
    {
        var condition = TransformExpression(statement.Condition);
        var thenBranch = TransformStatement(statement.Then);
        var elseBranch = statement.Else is null ? null : TransformStatement(statement.Else);

        if (ReferenceEquals(condition, statement.Condition) && ReferenceEquals(thenBranch, statement.Then) &&
            ReferenceEquals(elseBranch, statement.Else))
        {
            return statement;
        }

        return statement with { Condition = condition, Then = thenBranch, Else = elseBranch };
    }

    private WhileStatement TransformWhileStatement(WhileStatement statement)
    {
        var condition = TransformExpression(statement.Condition);
        var body = TransformStatement(statement.Body);
        return ReferenceEquals(condition, statement.Condition) && ReferenceEquals(body, statement.Body)
            ? statement
            : statement with { Condition = condition, Body = body };
    }

    private DoWhileStatement TransformDoWhileStatement(DoWhileStatement statement)
    {
        var condition = TransformExpression(statement.Condition);
        var body = TransformStatement(statement.Body);
        return ReferenceEquals(condition, statement.Condition) && ReferenceEquals(body, statement.Body)
            ? statement
            : statement with { Condition = condition, Body = body };
    }

    private ForStatement TransformForStatement(ForStatement statement)
    {
        var initializer = statement.Initializer is null ? null : TransformStatement(statement.Initializer);
        var condition = statement.Condition is null ? null : TransformExpression(statement.Condition);
        var increment = statement.Increment is null ? null : TransformExpression(statement.Increment);
        var body = TransformStatement(statement.Body);

        if (ReferenceEquals(initializer, statement.Initializer) && ReferenceEquals(condition, statement.Condition) &&
            ReferenceEquals(increment, statement.Increment) && ReferenceEquals(body, statement.Body))
        {
            return statement;
        }

        return statement with { Initializer = initializer, Condition = condition, Increment = increment, Body = body };
    }

    private ForEachStatement TransformForEachStatement(ForEachStatement statement)
    {
        var iterable = TransformExpression(statement.Iterable);
        var body = TransformStatement(statement.Body);
        return ReferenceEquals(iterable, statement.Iterable) && ReferenceEquals(body, statement.Body)
            ? statement
            : statement with { Iterable = iterable, Body = body };
    }

    private LabeledStatement TransformLabeledStatement(LabeledStatement statement)
    {
        var inner = TransformStatement(statement.Statement);
        return ReferenceEquals(inner, statement.Statement) ? statement : statement with { Statement = inner };
    }

    private TryStatement TransformTryStatement(TryStatement statement)
    {
        var tryBlock = TransformBlock(statement.TryBlock);
        var catchClause = statement.Catch is null ? null : TransformCatchClause(statement.Catch);
        var finallyBlock = statement.Finally is null ? null : TransformBlock(statement.Finally);

        if (ReferenceEquals(tryBlock, statement.TryBlock) && ReferenceEquals(catchClause, statement.Catch) &&
            ReferenceEquals(finallyBlock, statement.Finally))
        {
            return statement;
        }

        return statement with { TryBlock = tryBlock, Catch = catchClause, Finally = finallyBlock };
    }

    private CatchClause TransformCatchClause(CatchClause clause)
    {
        var body = TransformBlock(clause.Body);
        return ReferenceEquals(body, clause.Body) ? clause : clause with { Body = body };
    }

    private SwitchStatement TransformSwitchStatement(SwitchStatement statement)
    {
        var discriminant = TransformExpression(statement.Discriminant);
        var builder = ImmutableArray.CreateBuilder<SwitchCase>(statement.Cases.Length);
        var changed = false;
        foreach (var switchCase in statement.Cases)
        {
            var test = switchCase.Test is null ? null : TransformExpression(switchCase.Test);
            var body = TransformBlock(switchCase.Body);
            if (!ReferenceEquals(test, switchCase.Test) || !ReferenceEquals(body, switchCase.Body))
            {
                builder.Add(switchCase with { Test = test, Body = body });
                changed = true;
            }
            else
            {
                builder.Add(switchCase);
            }
        }

        if (!changed)
        {
            return ReferenceEquals(discriminant, statement.Discriminant)
                ? statement
                : statement with { Discriminant = discriminant };
        }

        return statement with { Discriminant = discriminant, Cases = builder.ToImmutable() };
    }

    private FunctionExpression TransformFunctionExpression(FunctionExpression function)
    {
        if (!function.IsAsync)
        {
            return function;
        }

        if (function.IsGenerator)
        {
            throw new NotSupportedException("Typed CPS transformer does not handle async generators yet.");
        }

        // Only attempt the rewrite when the function body exclusively contains
        // statement shapes the experimental CPS rewriter understands. This
        // prevents us from clearing the async modifier when the body still
        // contains await expressions in unsupported control flow constructs.
        if (!CanRewriteStatement(function.Body))
        {
            throw new NotSupportedException(
                "Typed CPS transformer cannot rewrite async functions that contain awaits inside unsupported statements.");
        }

        var transformedBody = RewriteAsyncBody(function.Body);
        return function with { IsAsync = false, Body = transformedBody };
    }

    private ExpressionNode TransformExpression(ExpressionNode expression)
    {
        switch (expression)
        {
            case FunctionExpression functionExpression:
                return TransformFunctionExpression(functionExpression);
            case CallExpression callExpression:
                return TransformCallExpression(callExpression);
            case NewExpression newExpression:
                return TransformNewExpression(newExpression);
            case MemberExpression memberExpression:
                return TransformMemberExpression(memberExpression);
            case BinaryExpression binaryExpression:
                return TransformBinaryExpression(binaryExpression);
            case UnaryExpression unaryExpression:
                var operand = TransformExpression(unaryExpression.Operand);
                return ReferenceEquals(operand, unaryExpression.Operand)
                    ? unaryExpression
                    : unaryExpression with { Operand = operand };
            case ConditionalExpression conditionalExpression:
                var test = TransformExpression(conditionalExpression.Test);
                var consequent = TransformExpression(conditionalExpression.Consequent);
                var alternate = TransformExpression(conditionalExpression.Alternate);
                if (ReferenceEquals(test, conditionalExpression.Test) &&
                    ReferenceEquals(consequent, conditionalExpression.Consequent) &&
                    ReferenceEquals(alternate, conditionalExpression.Alternate))
                {
                    return conditionalExpression;
                }

                return conditionalExpression with { Test = test, Consequent = consequent, Alternate = alternate };
            case AssignmentExpression assignmentExpression:
                var assignedValue = TransformExpression(assignmentExpression.Value);
                return ReferenceEquals(assignedValue, assignmentExpression.Value)
                    ? assignmentExpression
                    : assignmentExpression with { Value = assignedValue };
            case PropertyAssignmentExpression propertyAssignmentExpression:
                var target = TransformExpression(propertyAssignmentExpression.Target);
                var property = TransformExpression(propertyAssignmentExpression.Property);
                var value = TransformExpression(propertyAssignmentExpression.Value);
                if (ReferenceEquals(target, propertyAssignmentExpression.Target) &&
                    ReferenceEquals(property, propertyAssignmentExpression.Property) &&
                    ReferenceEquals(value, propertyAssignmentExpression.Value))
                {
                    return propertyAssignmentExpression;
                }

                return propertyAssignmentExpression with { Target = target, Property = property, Value = value };
            case IndexAssignmentExpression indexAssignmentExpression:
                var indexTarget = TransformExpression(indexAssignmentExpression.Target);
                var index = TransformExpression(indexAssignmentExpression.Index);
                var indexValue = TransformExpression(indexAssignmentExpression.Value);
                if (ReferenceEquals(indexTarget, indexAssignmentExpression.Target) &&
                    ReferenceEquals(index, indexAssignmentExpression.Index) &&
                    ReferenceEquals(indexValue, indexAssignmentExpression.Value))
                {
                    return indexAssignmentExpression;
                }

                return indexAssignmentExpression with { Target = indexTarget, Index = index, Value = indexValue };
            case SequenceExpression sequenceExpression:
                var left = TransformExpression(sequenceExpression.Left);
                var right = TransformExpression(sequenceExpression.Right);
                return ReferenceEquals(left, sequenceExpression.Left) && ReferenceEquals(right, sequenceExpression.Right)
                    ? sequenceExpression
                    : sequenceExpression with { Left = left, Right = right };
            case ArrayExpression arrayExpression:
                return TransformArrayExpression(arrayExpression);
            case ObjectExpression objectExpression:
                return TransformObjectExpression(objectExpression);
            case TemplateLiteralExpression templateLiteralExpression:
                return TransformTemplateLiteral(templateLiteralExpression);
            case TaggedTemplateExpression taggedTemplateExpression:
                return TransformTaggedTemplate(taggedTemplateExpression);
            case DestructuringAssignmentExpression destructuringAssignmentExpression:
                var destructuredValue = TransformExpression(destructuringAssignmentExpression.Value);
                return ReferenceEquals(destructuredValue, destructuringAssignmentExpression.Value)
                    ? destructuringAssignmentExpression
                    : destructuringAssignmentExpression with { Value = destructuredValue };
            case AwaitExpression awaitExpression:
                var awaited = TransformExpression(awaitExpression.Expression);
                return ReferenceEquals(awaited, awaitExpression.Expression)
                    ? awaitExpression
                    : awaitExpression with { Expression = awaited };
            case IdentifierExpression:
            case LiteralExpression:
            case ThisExpression:
            case SuperExpression:
                return expression;
        }

        return expression;
    }

    private ExpressionNode TransformCallExpression(CallExpression expression)
    {
        var callee = TransformExpression(expression.Callee);
        var builder = ImmutableArray.CreateBuilder<CallArgument>(expression.Arguments.Length);
        var changed = !ReferenceEquals(callee, expression.Callee);
        foreach (var argument in expression.Arguments)
        {
            var value = TransformExpression(argument.Expression);
            if (!ReferenceEquals(value, argument.Expression))
            {
                builder.Add(argument with { Expression = value });
                changed = true;
            }
            else
            {
                builder.Add(argument);
            }
        }

        return changed ? expression with { Callee = callee, Arguments = builder.ToImmutable() } : expression;
    }

    private ExpressionNode TransformNewExpression(NewExpression expression)
    {
        var constructor = TransformExpression(expression.Constructor);
        var builder = ImmutableArray.CreateBuilder<ExpressionNode>(expression.Arguments.Length);
        var changed = !ReferenceEquals(constructor, expression.Constructor);
        foreach (var argument in expression.Arguments)
        {
            var value = TransformExpression(argument);
            if (!ReferenceEquals(value, argument))
            {
                builder.Add(value);
                changed = true;
            }
            else
            {
                builder.Add(argument);
            }
        }

        return changed ? expression with { Constructor = constructor, Arguments = builder.ToImmutable() } : expression;
    }

    private ExpressionNode TransformMemberExpression(MemberExpression expression)
    {
        var target = TransformExpression(expression.Target);
        var property = TransformExpression(expression.Property);
        return ReferenceEquals(target, expression.Target) && ReferenceEquals(property, expression.Property)
            ? expression
            : expression with { Target = target, Property = property };
    }

    private ExpressionNode TransformBinaryExpression(BinaryExpression expression)
    {
        var left = TransformExpression(expression.Left);
        var right = TransformExpression(expression.Right);
        return ReferenceEquals(left, expression.Left) && ReferenceEquals(right, expression.Right)
            ? expression
            : expression with { Left = left, Right = right };
    }

    private ExpressionNode TransformArrayExpression(ArrayExpression expression)
    {
        var builder = ImmutableArray.CreateBuilder<ArrayElement>(expression.Elements.Length);
        var changed = false;
        foreach (var element in expression.Elements)
        {
            if (element.Expression is null)
            {
                builder.Add(element);
                continue;
            }

            var value = TransformExpression(element.Expression);
            if (!ReferenceEquals(value, element.Expression))
            {
                builder.Add(element with { Expression = value });
                changed = true;
            }
            else
            {
                builder.Add(element);
            }
        }

        return changed ? expression with { Elements = builder.ToImmutable() } : expression;
    }

    private ExpressionNode TransformObjectExpression(ObjectExpression expression)
    {
        var builder = ImmutableArray.CreateBuilder<ObjectMember>(expression.Members.Length);
        var changed = false;
        foreach (var member in expression.Members)
        {
            var value = member.Value is null ? null : TransformExpression(member.Value);
            var function = member.Function is null ? null : TransformFunctionExpression(member.Function);
            if (!ReferenceEquals(value, member.Value) || !ReferenceEquals(function, member.Function))
            {
                builder.Add(member with { Value = value, Function = function });
                changed = true;
            }
            else
            {
                builder.Add(member);
            }
        }

        return changed ? expression with { Members = builder.ToImmutable() } : expression;
    }

    private ExpressionNode TransformTemplateLiteral(TemplateLiteralExpression expression)
    {
        var builder = ImmutableArray.CreateBuilder<TemplatePart>(expression.Parts.Length);
        var changed = false;
        foreach (var part in expression.Parts)
        {
            if (part.Expression is null)
            {
                builder.Add(part);
                continue;
            }

            var value = TransformExpression(part.Expression);
            if (!ReferenceEquals(value, part.Expression))
            {
                builder.Add(part with { Expression = value });
                changed = true;
            }
            else
            {
                builder.Add(part);
            }
        }

        return changed ? expression with { Parts = builder.ToImmutable() } : expression;
    }

    private ExpressionNode TransformTaggedTemplate(TaggedTemplateExpression expression)
    {
        var tag = TransformExpression(expression.Tag);
        var strings = TransformExpression(expression.StringsArray);
        var rawStrings = TransformExpression(expression.RawStringsArray);
        var builder = ImmutableArray.CreateBuilder<ExpressionNode>(expression.Expressions.Length);
        var changed = !ReferenceEquals(tag, expression.Tag) || !ReferenceEquals(strings, expression.StringsArray) ||
                      !ReferenceEquals(rawStrings, expression.RawStringsArray);
        foreach (var part in expression.Expressions)
        {
            var value = TransformExpression(part);
            if (!ReferenceEquals(value, part))
            {
                builder.Add(value);
                changed = true;
            }
            else
            {
                builder.Add(part);
            }
        }

        return changed
            ? expression with { Tag = tag, StringsArray = strings, RawStringsArray = rawStrings, Expressions = builder.ToImmutable() }
            : expression;
    }

    private BlockStatement RewriteAsyncBody(BlockStatement body)
    {
        var normalizedStatements = NormalizeStatements(body.Statements);
        var rewriter = new AsyncFunctionRewriter(this, body.IsStrict);
        var rewrittenStatements = EnsureResolvedReturns(rewriter.Rewrite(normalizedStatements));
        var tryBlock = new BlockStatement(null, rewrittenStatements, body.IsStrict);
        var catchBodyStatements = ImmutableArray.Create<StatementNode>(
            new ReturnStatement(null, CreateRejectCall(new IdentifierExpression(null, CatchIdentifier))));
        var catchBody = new BlockStatement(null, catchBodyStatements, body.IsStrict);
        var catchClause = new CatchClause(null, CatchIdentifier, catchBody);
        var tryStatement = new TryStatement(null, tryBlock, catchClause, null);
        var executorBody = new BlockStatement(null, ImmutableArray.Create<StatementNode>(tryStatement), body.IsStrict);
        var executor = new FunctionExpression(null, null,
            [
                new FunctionParameter(null, ResolveIdentifier, false, null, null),
                new FunctionParameter(null, RejectIdentifier, false, null, null)
            ],
            executorBody, false, false);
        var promise = new NewExpression(null, new IdentifierExpression(null, PromiseIdentifier),
            [executor]);
        var returnPromise = new ReturnStatement(null, promise);
        return body with { Statements = [returnPromise] };
    }

    private ImmutableArray<StatementNode> EnsureResolvedReturns(ImmutableArray<StatementNode> statements)
    {
        if (statements.IsDefaultOrEmpty)
        {
            return statements;
        }

        var builder = ImmutableArray.CreateBuilder<StatementNode>(statements.Length);
        var changed = false;
        foreach (var statement in statements)
        {
            var rewritten = EnsureResolvedReturn(statement);
            if (!ReferenceEquals(statement, rewritten))
            {
                changed = true;
            }

            builder.Add(rewritten);
        }

        return changed ? builder.ToImmutable() : statements;
    }

    private StatementNode EnsureResolvedReturn(StatementNode statement)
    {
        switch (statement)
        {
            case ReturnStatement returnStatement:
                if (IsResolveCall(returnStatement.Expression) || IsPromiseChain(returnStatement.Expression))
                {
                    return returnStatement;
                }

                var expression = returnStatement.Expression ?? new IdentifierExpression(null, JsSymbols.Undefined);
                return returnStatement with { Expression = CreateResolveCall(expression) };
            case BlockStatement block:
                var statements = EnsureResolvedReturns(block.Statements);
                return statements.Equals(block.Statements) ? block : block with { Statements = statements };
            case IfStatement ifStatement:
                var thenBranch = EnsureResolvedReturn(ifStatement.Then);
                var elseBranch = ifStatement.Else is null ? null : EnsureResolvedReturn(ifStatement.Else);
                if (ReferenceEquals(thenBranch, ifStatement.Then) && ReferenceEquals(elseBranch, ifStatement.Else))
                {
                    return ifStatement;
                }

                return ifStatement with { Then = thenBranch, Else = elseBranch };
            case WhileStatement whileStatement:
                var whileBody = EnsureResolvedReturn(whileStatement.Body);
                return ReferenceEquals(whileBody, whileStatement.Body) ? whileStatement : whileStatement with { Body = whileBody };
            case DoWhileStatement doWhileStatement:
                var doBody = EnsureResolvedReturn(doWhileStatement.Body);
                return ReferenceEquals(doBody, doWhileStatement.Body) ? doWhileStatement : doWhileStatement with { Body = doBody };
            case ForStatement forStatement:
                var forBody = EnsureResolvedReturn(forStatement.Body);
                return ReferenceEquals(forBody, forStatement.Body) ? forStatement : forStatement with { Body = forBody };
            case ForEachStatement forEachStatement:
                var forEachBody = EnsureResolvedReturn(forEachStatement.Body);
                return ReferenceEquals(forEachBody, forEachStatement.Body) ? forEachStatement : forEachStatement with { Body = forEachBody };
            case LabeledStatement labeledStatement:
                var labeled = EnsureResolvedReturn(labeledStatement.Statement);
                return ReferenceEquals(labeled, labeledStatement.Statement) ? labeledStatement : labeledStatement with { Statement = labeled };
            case TryStatement tryStatement:
                var tryBlock = (BlockStatement)EnsureResolvedReturn(tryStatement.TryBlock);
                var catchClause = tryStatement.Catch is null ? null : tryStatement.Catch with
                {
                    Body = (BlockStatement)EnsureResolvedReturn(tryStatement.Catch.Body)
                };
                var finallyBlock = tryStatement.Finally is null
                    ? null
                    : (BlockStatement)EnsureResolvedReturn(tryStatement.Finally);
                if (ReferenceEquals(tryBlock, tryStatement.TryBlock) && ReferenceEquals(catchClause, tryStatement.Catch) &&
                    ReferenceEquals(finallyBlock, tryStatement.Finally))
                {
                    return tryStatement;
                }

                return tryStatement with { TryBlock = tryBlock, Catch = catchClause, Finally = finallyBlock };
            case SwitchStatement switchStatement:
                var discriminant = switchStatement.Discriminant;
                var changed = false;
                var cases = ImmutableArray.CreateBuilder<SwitchCase>(switchStatement.Cases.Length);
                foreach (var switchCase in switchStatement.Cases)
                {
                    var caseBody = (BlockStatement)EnsureResolvedReturn(switchCase.Body);
                    if (!ReferenceEquals(caseBody, switchCase.Body))
                    {
                        cases.Add(switchCase with { Body = caseBody });
                        changed = true;
                    }
                    else
                    {
                        cases.Add(switchCase);
                    }
                }

                return changed ? switchStatement with { Cases = cases.ToImmutable() } : switchStatement;
        }

        return statement;
    }

    private static bool IsResolveCall(ExpressionNode? expression)
    {
        return expression is CallExpression { Callee: IdentifierExpression { Name: var name } } call &&
               ReferenceEquals(name, ResolveIdentifier);
    }

    private static bool IsPromiseChain(ExpressionNode? expression)
    {
        if (expression is not CallExpression { Callee: MemberExpression member } call)
        {
            return false;
        }

        return member.Property is LiteralExpression { Value: string propertyName } && propertyName == "catch";
    }

    private ImmutableArray<StatementNode> NormalizeStatements(ImmutableArray<StatementNode> statements)
    {
        if (statements.IsDefaultOrEmpty)
        {
            return statements;
        }

        var builder = ImmutableArray.CreateBuilder<StatementNode>();
        foreach (var statement in statements)
        {
            if (statement is VariableDeclaration { Declarators.Length: > 1 } declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    builder.Add(declaration with { Declarators = ImmutableArray.Create(declarator) });
                }

                continue;
            }

            builder.Add(statement);
        }

        return builder.ToImmutable();
    }

    private ExpressionNode EnsureSupportedAwaitOperand(ExpressionNode expression)
    {
        if (expression is AwaitExpression)
        {
            throw new NotSupportedException("Nested await expressions are not supported by the typed CPS prototype.");
        }

        return expression switch
        {
            FunctionExpression functionExpression => TransformFunctionExpression(functionExpression),
            _ => expression
        };
    }

    private ExpressionNode CreateAwaitHelperCall(ExpressionNode awaited)
    {
        var argument = new CallArgument(awaited.Source, awaited, false);
        return new CallExpression(null, new IdentifierExpression(null, AwaitHelperIdentifier),
            [argument], false);
    }

    private ExpressionNode CreateThenInvocation(ExpressionNode awaitCall, FunctionExpression? callback = null)
    {
        var callbackToUse = callback ?? CreateDefaultResolveCallback();
        var target = new MemberExpression(null, awaitCall,
            new LiteralExpression(null, ThenPropertyName), false, false);
        var callbackArgument = new CallArgument(null, callbackToUse, false);
        var thenArguments = ImmutableArray.Create(callbackArgument);
        return new CallExpression(null, target, thenArguments, false);
    }

    private FunctionExpression CreateDefaultResolveCallback()
    {
        var resolveCall = CreateResolveCall(new IdentifierExpression(null, AwaitValueIdentifier));
        var callbackBodyStatements = ImmutableArray.Create<StatementNode>(
            new ExpressionStatement(null, resolveCall));
        var callbackBody = new BlockStatement(null, callbackBodyStatements, false);
        return new FunctionExpression(null, null,
            [new FunctionParameter(null, AwaitValueIdentifier, false, null, null)],
            callbackBody, false, false);
    }

    private ExpressionNode AttachCatch(ExpressionNode expression)
    {
        var errorParameter = Symbol.Intern("__awaitError");
        var rejectCall = CreateRejectCall(new IdentifierExpression(null, errorParameter));
        var catchBodyStatements = ImmutableArray.Create<StatementNode>(
            new ReturnStatement(null, rejectCall));
        var catchBody = new BlockStatement(null, catchBodyStatements, false);
        var callback = new FunctionExpression(null, null,
            [new FunctionParameter(null, errorParameter, false, null, null)],
            catchBody, false, false);
        var member = new MemberExpression(null, expression, new LiteralExpression(null, "catch"), false, false);
        var argument = new CallArgument(null, callback, false);
        return new CallExpression(null, member, ImmutableArray.Create(argument), false);
    }

    private ExpressionNode CreateResolveCall(ExpressionNode value)
    {
        var argument = new CallArgument(value.Source, value, false);
        return new CallExpression(null, new IdentifierExpression(null, ResolveIdentifier),
            [argument], false);
    }

    private ExpressionNode CreateRejectCall(ExpressionNode value)
    {
        var argument = new CallArgument(value.Source, value, false);
        return new CallExpression(null, new IdentifierExpression(null, RejectIdentifier),
            [argument], false);
    }

    private sealed class AsyncFunctionRewriter
    {
        private readonly TypedCpsTransformer _owner;
        private readonly bool _isStrict;
        private int _temporaryId;

        public AsyncFunctionRewriter(TypedCpsTransformer owner, bool isStrict)
        {
            _owner = owner;
            _isStrict = isStrict;
        }

        public ImmutableArray<StatementNode> Rewrite(ImmutableArray<StatementNode> statements)
        {
            var rewritten = RewriteStatements(statements);
            if (rewritten.IsDefaultOrEmpty || rewritten[^1] is not ReturnStatement)
            {
                var undefinedValue = new IdentifierExpression(null, JsSymbols.Undefined);
                rewritten = rewritten.Add(new ReturnStatement(null, _owner.CreateResolveCall(undefinedValue)));
            }

            return rewritten;
        }

        private ImmutableArray<StatementNode> RewriteStatements(ImmutableArray<StatementNode> statements)
        {
            var builder = ImmutableArray.CreateBuilder<StatementNode>();

            for (var i = 0; i < statements.Length; i++)
            {
                var statement = statements[i];
                var remaining = statements[(i + 1)..];
                if (TryRewriteStatement(statement, remaining, out var rewritten, out var handledRemainder))
                {
                    builder.AddRange(rewritten);
                    if (handledRemainder)
                    {
                        return builder.ToImmutable();
                    }

                    continue;
                }

                builder.Add(statement);
            }

            return builder.ToImmutable();
        }

        private bool TryRewriteStatement(StatementNode statement, ImmutableArray<StatementNode> remaining,
            out ImmutableArray<StatementNode> rewritten, out bool handledRemainder)
        {
            switch (statement)
            {
                case ReturnStatement returnStatement:
                    var returnExpression = returnStatement.Expression ?? new IdentifierExpression(null, JsSymbols.Undefined);
                    rewritten = RewriteExpression(returnExpression, remaining,
                        expr => new ReturnStatement(returnStatement.Source, _owner.CreateResolveCall(expr)),
                        continueAfter: false,
                        inlineRemainder: false,
                        out _,
                        out _);
                    handledRemainder = true;
                    return true;
                case ExpressionStatement expressionStatement:
                    rewritten = RewriteExpression(expressionStatement.Expression, remaining,
                        expr => expressionStatement with { Expression = expr },
                        continueAfter: true,
                        inlineRemainder: false,
                        out handledRemainder,
                        out var encounteredAwait);
                    return encounteredAwait;
                case VariableDeclaration variableDeclaration when variableDeclaration.Declarators.Length == 1 &&
                                                                 variableDeclaration.Declarators[0].Initializer is { } initializer:
                    var declarator = variableDeclaration.Declarators[0];
                    rewritten = RewriteExpression(initializer, remaining,
                        expr => variableDeclaration with
                        {
                            Declarators = ImmutableArray.Create(declarator with { Initializer = expr })
                        },
                        continueAfter: true,
                        inlineRemainder: false,
                        out handledRemainder,
                        out var declarationAwait);
                    return declarationAwait;
            }

            rewritten = default;
            handledRemainder = false;
            return false;
        }

        private ImmutableArray<StatementNode> RewriteExpression(
            ExpressionNode expression,
            ImmutableArray<StatementNode> remaining,
            Func<ExpressionNode, StatementNode> createStatement,
            bool continueAfter,
            bool inlineRemainder,
            out bool handlesRemainder,
            out bool encounteredAwait)
        {
            if (!TryExtractAwait(expression, out var awaitExpression, out var rebuild))
            {
                var builder = ImmutableArray.CreateBuilder<StatementNode>();
                builder.Add(createStatement(expression));
                if (inlineRemainder && continueAfter)
                {
                    builder.AddRange(RewriteStatements(remaining));
                    handlesRemainder = true;
                }
                else
                {
                    handlesRemainder = false;
                }

                encounteredAwait = false;
                return builder.ToImmutable();
            }

            var awaited = _owner.EnsureSupportedAwaitOperand(awaitExpression.Expression);
            var awaitCall = _owner.CreateAwaitHelperCall(awaited);
            var tempSymbol = Symbol.Intern($"__awaitValue{_temporaryId++}");
            var placeholder = new IdentifierExpression(null, tempSymbol);
            var replaced = rebuild(placeholder);
            var callbackStatements = RewriteExpression(replaced, remaining, createStatement, continueAfter,
                inlineRemainder: true, out _, out _);
            var callbackBody = new BlockStatement(null, callbackStatements, _isStrict);
            var parameter = new FunctionParameter(null, tempSymbol, false, null, null);
            var callback = new FunctionExpression(null, null, ImmutableArray.Create(parameter), callbackBody, false, false);
            var thenCall = _owner.CreateThenInvocation(awaitCall, callback);
            var withCatch = _owner.AttachCatch(thenCall);
            handlesRemainder = true;
            encounteredAwait = true;
            return ImmutableArray.Create<StatementNode>(new ReturnStatement(null, withCatch));
        }

        private bool TryExtractAwait(ExpressionNode expression, out AwaitExpression awaitExpression,
            out Func<ExpressionNode, ExpressionNode> rebuild)
        {
            switch (expression)
            {
                case AwaitExpression direct:
                    awaitExpression = direct;
                    rebuild = value => value;
                    return true;
                case BinaryExpression binary when TryExtractAwait(binary.Left, out awaitExpression, out var leftRebuild):
                    rebuild = value => binary with { Left = leftRebuild(value) };
                    return true;
                case BinaryExpression binary when TryExtractAwait(binary.Right, out awaitExpression, out var rightRebuild):
                    rebuild = value => binary with { Right = rightRebuild(value) };
                    return true;
                case CallExpression call when TryExtractAwait(call.Callee, out awaitExpression, out var calleeRebuild):
                    rebuild = value => call with { Callee = calleeRebuild(value) };
                    return true;
                case CallExpression call:
                    for (var i = 0; i < call.Arguments.Length; i++)
                    {
                        var argument = call.Arguments[i];
                        if (!TryExtractAwait(argument.Expression, out awaitExpression, out var argumentRebuild))
                        {
                            continue;
                        }

                        rebuild = value =>
                        {
                            var args = call.Arguments.ToBuilder();
                            args[i] = argument with { Expression = argumentRebuild(value) };
                            return call with { Arguments = args.ToImmutable() };
                        };
                        return true;
                    }

                    break;
                case NewExpression newExpression when TryExtractAwait(newExpression.Constructor, out awaitExpression, out var ctorRebuild):
                    rebuild = value => newExpression with { Constructor = ctorRebuild(value) };
                    return true;
                case NewExpression newExpression:
                    for (var i = 0; i < newExpression.Arguments.Length; i++)
                    {
                        var argument = newExpression.Arguments[i];
                        if (!TryExtractAwait(argument, out awaitExpression, out var argumentRebuild))
                        {
                            continue;
                        }

                        rebuild = value =>
                        {
                            var args = newExpression.Arguments.ToBuilder();
                            args[i] = argumentRebuild(value);
                            return newExpression with { Arguments = args.ToImmutable() };
                        };
                        return true;
                    }

                    break;
                case MemberExpression member when TryExtractAwait(member.Target, out awaitExpression, out var targetRebuild):
                    rebuild = value => member with { Target = targetRebuild(value) };
                    return true;
                case MemberExpression member when TryExtractAwait(member.Property, out awaitExpression, out var propertyRebuild):
                    rebuild = value => member with { Property = propertyRebuild(value) };
                    return true;
                case ConditionalExpression conditional when TryExtractAwait(conditional.Test, out awaitExpression, out var testRebuild):
                    rebuild = value => conditional with { Test = testRebuild(value) };
                    return true;
                case ConditionalExpression conditional when TryExtractAwait(conditional.Consequent, out awaitExpression, out var consequentRebuild):
                    rebuild = value => conditional with { Consequent = consequentRebuild(value) };
                    return true;
                case ConditionalExpression conditional when TryExtractAwait(conditional.Alternate, out awaitExpression, out var alternateRebuild):
                    rebuild = value => conditional with { Alternate = alternateRebuild(value) };
                    return true;
                case SequenceExpression sequence when TryExtractAwait(sequence.Left, out awaitExpression, out var leftSequence):
                    rebuild = value => sequence with { Left = leftSequence(value) };
                    return true;
                case SequenceExpression sequence when TryExtractAwait(sequence.Right, out awaitExpression, out var rightSequence):
                    rebuild = value => sequence with { Right = rightSequence(value) };
                    return true;
                case AssignmentExpression assignment when TryExtractAwait(assignment.Value, out awaitExpression, out var assignmentRebuild):
                    rebuild = value => assignment with { Value = assignmentRebuild(value) };
                    return true;
                case PropertyAssignmentExpression propertyAssignment when TryExtractAwait(propertyAssignment.Value, out awaitExpression, out var propertyValueRebuild):
                    rebuild = value => propertyAssignment with { Value = propertyValueRebuild(value) };
                    return true;
                case IndexAssignmentExpression indexAssignment when TryExtractAwait(indexAssignment.Value, out awaitExpression, out var indexValueRebuild):
                    rebuild = value => indexAssignment with { Value = indexValueRebuild(value) };
                    return true;
                case UnaryExpression unary when TryExtractAwait(unary.Operand, out awaitExpression, out var operandRebuild):
                    rebuild = value => unary with { Operand = operandRebuild(value) };
                    return true;
                case ArrayExpression arrayExpression:
                    for (var i = 0; i < arrayExpression.Elements.Length; i++)
                    {
                        var element = arrayExpression.Elements[i];
                        if (element.Expression is null ||
                            !TryExtractAwait(element.Expression, out awaitExpression, out var elementRebuild))
                        {
                            continue;
                        }

                        rebuild = value =>
                        {
                            var elements = arrayExpression.Elements.ToBuilder();
                            elements[i] = element with { Expression = elementRebuild(value) };
                            return arrayExpression with { Elements = elements.ToImmutable() };
                        };
                        return true;
                    }

                    break;
                case ObjectExpression objectExpression:
                    for (var i = 0; i < objectExpression.Members.Length; i++)
                    {
                        var member = objectExpression.Members[i];
                        if (member.Value is null ||
                            !TryExtractAwait(member.Value, out awaitExpression, out var memberValueRebuild))
                        {
                            continue;
                        }

                        rebuild = value =>
                        {
                            var members = objectExpression.Members.ToBuilder();
                            members[i] = member with { Value = memberValueRebuild(value) };
                            return objectExpression with { Members = members.ToImmutable() };
                        };
                        return true;
                    }

                    break;
                case TemplateLiteralExpression templateLiteral:
                    for (var i = 0; i < templateLiteral.Parts.Length; i++)
                    {
                        var part = templateLiteral.Parts[i];
                        if (part.Expression is null ||
                            !TryExtractAwait(part.Expression, out awaitExpression, out var partRebuild))
                        {
                            continue;
                        }

                        rebuild = value =>
                        {
                            var parts = templateLiteral.Parts.ToBuilder();
                            parts[i] = part with { Expression = partRebuild(value) };
                            return templateLiteral with { Parts = parts.ToImmutable() };
                        };
                        return true;
                    }

                    break;
                case TaggedTemplateExpression taggedTemplate when TryExtractAwait(taggedTemplate.Tag, out awaitExpression, out var tagRebuild):
                    rebuild = value => taggedTemplate with { Tag = tagRebuild(value) };
                    return true;
                case TaggedTemplateExpression taggedTemplate:
                    for (var i = 0; i < taggedTemplate.Expressions.Length; i++)
                    {
                        var part = taggedTemplate.Expressions[i];
                        if (!TryExtractAwait(part, out awaitExpression, out var templateRebuild))
                        {
                            continue;
                        }

                        rebuild = value =>
                        {
                            var expressions = taggedTemplate.Expressions.ToBuilder();
                            expressions[i] = templateRebuild(value);
                            return taggedTemplate with { Expressions = expressions.ToImmutable() };
                        };
                        return true;
                    }

                    break;
            }

            awaitExpression = null!;
            rebuild = null!;
            return false;
        }
    }

    private static ImmutableArray<T> TransformImmutableArray<T>(ImmutableArray<T> source, Func<T, T> transformer,
        out bool changed)
    {
        if (source.IsDefaultOrEmpty)
        {
            changed = false;
            return source;
        }

        var builder = ImmutableArray.CreateBuilder<T>(source.Length);
        changed = false;
        foreach (var item in source)
        {
            var transformed = transformer(item);
            builder.Add(transformed);
            if (!ReferenceEquals(item, transformed))
            {
                changed = true;
            }
        }

        return changed ? builder.ToImmutable() : source;
    }
}
