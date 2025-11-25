using System.Collections.Immutable;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Experimental CPS (Continuation-Passing Style) transformer that works directly
///     on the typed AST. The goal is to explore what a typed-first transformation
///     would look like, not to replace the production S-expression implementation.
///     For now only simple async function declarations that immediately <c>return</c>
///     an <c>await</c> expression are supported.
/// </summary>
public sealed class TypedCpsTransformer
{
    private const string ThenPropertyName = "then";
    private static readonly Symbol PromiseIdentifier = Symbol.Intern("Promise");
    private static readonly Symbol ResolveIdentifier = Symbol.Intern("__resolve");
    private static readonly Symbol RejectIdentifier = Symbol.Intern("__reject");
    private static readonly Symbol AwaitHelperIdentifier = Symbol.Intern("__awaitHelper");
    private static readonly Symbol AwaitValueIdentifier = Symbol.Intern("__value");
    private static readonly Symbol CatchIdentifier = Symbol.Intern("__error");

    /// <summary>
    ///     Returns true when the typed program contains async functions that would
    ///     require CPS transformation. The current implementation only looks for
    ///     function declarations because that's the only construct the transformer
    ///     understands today.
    /// </summary>
    public static bool NeedsTransformation(ProgramNode program)
    {
        return Enumerable.Any(program.Body, StatementNeedsTransformation);
    }

    private static bool StatementNeedsTransformation(StatementNode statement)
    {
        if (AstShapeAnalyzer.StatementContainsAwait(statement, true))
        {
            return true;
        }

        while (true)
        {
            switch (statement)
            {
                case FunctionDeclaration { Function: { IsAsync: true, IsGenerator: false } }:
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

                    break;
                case ExpressionStatement expressionStatement:
                    return ExpressionNeedsTransformation(expressionStatement.Expression);
                case ReturnStatement { Expression: { } expression }:
                    return ExpressionNeedsTransformation(expression);
                case BlockStatement block:
                    return Enumerable.Any(block.Statements, StatementNeedsTransformation);

                case IfStatement ifStatement:
                    return ExpressionNeedsTransformation(ifStatement.Condition) ||
                           StatementNeedsTransformation(ifStatement.Then) || (ifStatement.Else is not null &&
                                                                              StatementNeedsTransformation(ifStatement
                                                                                  .Else));
                case WhileStatement whileStatement:
                    return ExpressionNeedsTransformation(whileStatement.Condition) ||
                           StatementNeedsTransformation(whileStatement.Body);
                case DoWhileStatement doWhileStatement:
                    return StatementNeedsTransformation(doWhileStatement.Body) ||
                           ExpressionNeedsTransformation(doWhileStatement.Condition);
                case ForStatement forStatement:
                    return (forStatement.Initializer is not null &&
                            StatementNeedsTransformation(forStatement.Initializer)) ||
                           (forStatement.Condition is not null &&
                            ExpressionNeedsTransformation(forStatement.Condition)) ||
                           (forStatement.Increment is not null &&
                            ExpressionNeedsTransformation(forStatement.Increment)) ||
                           StatementNeedsTransformation(forStatement.Body);
                case ForEachStatement forEachStatement:
                    if (forEachStatement.Kind == ForEachKind.AwaitOf)
                    {
                        return true;
                    }

                    return ExpressionNeedsTransformation(forEachStatement.Iterable) ||
                           StatementNeedsTransformation(forEachStatement.Body);
                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;
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

                    break;
            }

            return false;
        }
    }

    private static bool StatementNeedsAsyncHandling(StatementNode statement)
    {
        if (statement is ForEachStatement { Kind: ForEachKind.AwaitOf })
        {
            return true;
        }

        return StatementNeedsTransformation(statement);
    }

    private static bool FunctionNeedsTransformation(FunctionExpression function)
    {
        if (function is { IsAsync: true, IsGenerator: false })
        {
            return true;
        }

        return StatementNeedsTransformation(function.Body);
    }

    private static bool ExpressionNeedsTransformation(ExpressionNode expression)
    {
        if (AstShapeAnalyzer.ContainsAwait(expression, true))
        {
            return true;
        }

        while (true)
        {
            switch (expression)
            {
                case AwaitExpression:
                    return true;
                case FunctionExpression functionExpression:
                    return functionExpression is { IsAsync: true, IsGenerator: false } ||
                           StatementNeedsTransformation(functionExpression.Body);
                case BinaryExpression binaryExpression:
                    return ExpressionNeedsTransformation(binaryExpression.Left) ||
                           ExpressionNeedsTransformation(binaryExpression.Right);
                case UnaryExpression unaryExpression:
                    expression = unaryExpression.Operand;
                    continue;
                case ConditionalExpression conditionalExpression:
                    return ExpressionNeedsTransformation(conditionalExpression.Test) ||
                           ExpressionNeedsTransformation(conditionalExpression.Consequent) ||
                           ExpressionNeedsTransformation(conditionalExpression.Alternate);
                case CallExpression callExpression:
                    return ExpressionNeedsTransformation(callExpression.Callee) || Enumerable.Any(
                        callExpression.Arguments, argument => ExpressionNeedsTransformation(argument.Expression));

                case NewExpression newExpression:
                    if (ExpressionNeedsTransformation(newExpression.Constructor))
                    {
                        return true;
                    }

                    if (Enumerable.Any(newExpression.Arguments, ExpressionNeedsTransformation))
                    {
                        return true;
                    }

                    break;
                case MemberExpression memberExpression:
                    return ExpressionNeedsTransformation(memberExpression.Target) ||
                           ExpressionNeedsTransformation(memberExpression.Property);
                case AssignmentExpression assignmentExpression:
                    expression = assignmentExpression.Value;
                    continue;
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

                    break;
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

                    break;
                case TemplateLiteralExpression templateLiteralExpression:
                    foreach (var part in templateLiteralExpression.Parts)
                    {
                        if (part.Expression is not null && ExpressionNeedsTransformation(part.Expression))
                        {
                            return true;
                        }
                    }

                    break;
                case TaggedTemplateExpression taggedTemplateExpression:
                    if (ExpressionNeedsTransformation(taggedTemplateExpression.Tag) ||
                        ExpressionNeedsTransformation(taggedTemplateExpression.StringsArray) ||
                        ExpressionNeedsTransformation(taggedTemplateExpression.RawStringsArray))
                    {
                        return true;
                    }

                    return Enumerable.Any(taggedTemplateExpression.Expressions, ExpressionNeedsTransformation);
            }

            return false;
        }
    }

    /// <summary>
    ///     Rewrites supported async functions in-place. Unsupported constructs are
    ///     left untouched so callers can continue experimenting without risking the
    ///     broader pipeline.
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
        return ReferenceEquals(expression, statement.Expression)
            ? statement
            : statement with { Expression = expression };
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
        return ReferenceEquals(expression, statement.Expression)
            ? statement
            : statement with { Expression = expression };
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
        if (!function.IsAsync || function.IsGenerator)
        {
            return function;
        }

        var transformedBody = RewriteAsyncBody(function.Body);
        return function with { Body = transformedBody };
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
                return ReferenceEquals(left, sequenceExpression.Left) &&
                       ReferenceEquals(right, sequenceExpression.Right)
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
                break;
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
            ? expression with
            {
                Tag = tag, StringsArray = strings, RawStringsArray = rawStrings, Expressions = builder.ToImmutable()
            }
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
        var catchClause = new CatchClause(null, new IdentifierBinding(null, CatchIdentifier), catchBody);
        var tryStatement = new TryStatement(null, tryBlock, catchClause, null);
        var executorBody = new BlockStatement(null, [tryStatement], body.IsStrict);
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

                var expression = returnStatement.Expression ?? new IdentifierExpression(null, Symbols.Undefined);
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
                return ReferenceEquals(whileBody, whileStatement.Body)
                    ? whileStatement
                    : whileStatement with { Body = whileBody };
            case DoWhileStatement doWhileStatement:
                var doBody = EnsureResolvedReturn(doWhileStatement.Body);
                return ReferenceEquals(doBody, doWhileStatement.Body)
                    ? doWhileStatement
                    : doWhileStatement with { Body = doBody };
            case ForStatement forStatement:
                var forBody = EnsureResolvedReturn(forStatement.Body);
                return ReferenceEquals(forBody, forStatement.Body)
                    ? forStatement
                    : forStatement with { Body = forBody };
            case ForEachStatement forEachStatement:
                var forEachBody = EnsureResolvedReturn(forEachStatement.Body);
                return ReferenceEquals(forEachBody, forEachStatement.Body)
                    ? forEachStatement
                    : forEachStatement with { Body = forEachBody };
            case LabeledStatement labeledStatement:
                var labeled = EnsureResolvedReturn(labeledStatement.Statement);
                return ReferenceEquals(labeled, labeledStatement.Statement)
                    ? labeledStatement
                    : labeledStatement with { Statement = labeled };
            case TryStatement tryStatement:
                var tryBlock = (BlockStatement)EnsureResolvedReturn(tryStatement.TryBlock);
                var catchClause = tryStatement.Catch is null
                    ? null
                    : tryStatement.Catch with { Body = (BlockStatement)EnsureResolvedReturn(tryStatement.Catch.Body) };
                var finallyBlock = tryStatement.Finally is null
                    ? null
                    : (BlockStatement)EnsureResolvedReturn(tryStatement.Finally);
                if (ReferenceEquals(tryBlock, tryStatement.TryBlock) &&
                    ReferenceEquals(catchClause, tryStatement.Catch) &&
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
        return expression is CallExpression { Callee: IdentifierExpression { Name: var name } } &&
               ReferenceEquals(name, ResolveIdentifier);
    }

    private static bool IsPromiseChain(ExpressionNode? expression)
    {
        if (expression is not CallExpression { Callee: MemberExpression member })
        {
            return false;
        }

        return member.Property is LiteralExpression { Value: "catch" };
    }

    private static ImmutableArray<StatementNode> NormalizeStatements(ImmutableArray<StatementNode> statements)
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
                    builder.Add(declaration with { Declarators = [declarator] });
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

    private static ExpressionNode CreateAwaitHelperCall(ExpressionNode awaited)
    {
        var argument = new CallArgument(awaited.Source, awaited, false);
        return new CallExpression(null, new IdentifierExpression(null, AwaitHelperIdentifier),
            [argument], false);
    }

    private static ExpressionNode CreateThenInvocation(ExpressionNode awaitCall, FunctionExpression? callback = null)
    {
        var callbackToUse = callback ?? CreateDefaultResolveCallback();
        var target = new MemberExpression(null, awaitCall,
            new LiteralExpression(null, ThenPropertyName), false, false);
        var callbackArgument = new CallArgument(null, callbackToUse, false);
        var thenArguments = ImmutableArray.Create(callbackArgument);
        return new CallExpression(null, target, thenArguments, false);
    }

    private static FunctionExpression CreateDefaultResolveCallback()
    {
        var resolveCall = CreateResolveCall(new IdentifierExpression(null, AwaitValueIdentifier));
        var callbackBodyStatements = ImmutableArray.Create<StatementNode>(
            new ExpressionStatement(null, resolveCall));
        var callbackBody = new BlockStatement(null, callbackBodyStatements, false);
        return new FunctionExpression(null, null,
            [new FunctionParameter(null, AwaitValueIdentifier, false, null, null)],
            callbackBody, false, false);
    }

    private static ExpressionNode AttachCatch(ExpressionNode expression, Symbol? rejectIdentifier)
    {
        if (rejectIdentifier is null)
        {
            return expression;
        }

        var errorParameter = Symbol.Intern("__awaitError");
        var rejectTarget = new IdentifierExpression(null, rejectIdentifier);
        var rejectCall = new CallExpression(null, rejectTarget,
            [new CallArgument(null, new IdentifierExpression(null, errorParameter), false)], false);
        var catchBodyStatements = ImmutableArray.Create<StatementNode>(
            new ReturnStatement(null, rejectCall));
        var catchBody = new BlockStatement(null, catchBodyStatements, false);
        var callback = new FunctionExpression(null, null,
            [new FunctionParameter(null, errorParameter, false, null, null)],
            catchBody, false, false);
        var member = new MemberExpression(null, expression, new LiteralExpression(null, "catch"), false, false);
        var argument = new CallArgument(null, callback, false);
        return new CallExpression(null, member, [argument], false);
    }

    private static CallExpression CreateResolveCall(ExpressionNode value)
    {
        var argument = new CallArgument(value.Source, value, false);
        return new CallExpression(null, new IdentifierExpression(null, ResolveIdentifier),
            [argument], false);
    }

    private static CallExpression CreateRejectCall(ExpressionNode value)
    {
        var argument = new CallArgument(value.Source, value, false);
        return new CallExpression(null, new IdentifierExpression(null, RejectIdentifier),
            [argument], false);
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

    private sealed class AsyncFunctionRewriter(
        TypedCpsTransformer owner,
        bool isStrict,
        Symbol? resolveOverride = null,
        Symbol? rejectOverride = null)
    {
        private readonly Symbol _resolveIdentifier = resolveOverride ?? ResolveIdentifier;
        private Symbol? _currentLoopBreakSymbol;
        private Symbol? _rejectIdentifier = rejectOverride ?? RejectIdentifier;
        private int _temporaryId;

        public ImmutableArray<StatementNode> Rewrite(ImmutableArray<StatementNode> statements)
        {
            var rewritten = RewriteStatements(statements);
            if (rewritten.IsDefaultOrEmpty || rewritten[^1] is not ReturnStatement)
            {
                var undefinedValue = new IdentifierExpression(null, Symbols.Undefined);
                rewritten = rewritten.Add(new ReturnStatement(null, CreateInnerResolveCall(undefinedValue)));
            }

            return rewritten;
        }

        private CallExpression CreateInnerResolveCall(ExpressionNode value)
        {
            if (ReferenceEquals(_resolveIdentifier, ResolveIdentifier))
            {
                return CreateResolveCall(value);
            }

            if (value is CallExpression { Callee: IdentifierExpression { Name: var name } } existingCall &&
                ReferenceEquals(name, _resolveIdentifier))
            {
                return existingCall;
            }

            var argument = new CallArgument(value.Source, value, false);
            return new CallExpression(null, new IdentifierExpression(null, _resolveIdentifier),
                [argument], false);
        }

        private ImmutableArray<StatementNode> RewriteStatements(ImmutableArray<StatementNode> statements)
        {
            var builder = ImmutableArray.CreateBuilder<StatementNode>();

            for (var i = 0; i < statements.Length; i++)
            {
                var statement = RewriteNestedStatement(statements[i]);
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

        private StatementNode RewriteNestedStatement(StatementNode statement)
        {
            switch (statement)
            {
                case BlockStatement block:
                    var blockStatements = RewriteStatements(block.Statements);
                    return blockStatements.SequenceEqual(block.Statements)
                        ? block
                        : block with { Statements = blockStatements };
                case IfStatement ifStatement:
                    var thenBranch = RewriteNestedStatement(ifStatement.Then);
                    var elseBranch = ifStatement.Else is null ? null : RewriteNestedStatement(ifStatement.Else);
                    if (ReferenceEquals(thenBranch, ifStatement.Then) && ReferenceEquals(elseBranch, ifStatement.Else))
                    {
                        return ifStatement;
                    }

                    return ifStatement with { Then = thenBranch, Else = elseBranch };
                case TryStatement tryStatement:
                    var tryIsAsync = StatementNeedsAsyncHandling(tryStatement.TryBlock);
                    var catchIsAsync = tryStatement.Catch is { } tryCatch && StatementNeedsAsyncHandling(tryCatch.Body);
                    if (tryIsAsync || catchIsAsync)
                    {
                        return tryStatement;
                    }

                    var tryBlock = RewriteNestedBlock(tryStatement.TryBlock);
                    var catchClause = tryStatement.Catch is null ? null : RewriteNestedCatch(tryStatement.Catch);
                    var finallyBlock = tryStatement.Finally is null ? null : RewriteNestedBlock(tryStatement.Finally);
                    if (ReferenceEquals(tryBlock, tryStatement.TryBlock) &&
                        ReferenceEquals(catchClause, tryStatement.Catch) &&
                        ReferenceEquals(finallyBlock, tryStatement.Finally))
                    {
                        return tryStatement;
                    }

                    return tryStatement with { TryBlock = tryBlock, Catch = catchClause, Finally = finallyBlock };
                case LabeledStatement labeledStatement:
                    var inner = RewriteNestedStatement(labeledStatement.Statement);
                    return ReferenceEquals(inner, labeledStatement.Statement)
                        ? labeledStatement
                        : labeledStatement with { Statement = inner };
                case SwitchStatement switchStatement:
                    var cases = ImmutableArray.CreateBuilder<SwitchCase>(switchStatement.Cases.Length);
                    var changed = false;
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        var body = RewriteNestedBlock(switchCase.Body);
                        if (!ReferenceEquals(body, switchCase.Body))
                        {
                            cases.Add(switchCase with { Body = body });
                            changed = true;
                        }
                        else
                        {
                            cases.Add(switchCase);
                        }
                    }

                    if (!changed)
                    {
                        return switchStatement;
                    }

                    return switchStatement with { Cases = cases.ToImmutable() };
                default:
                    return statement;
            }
        }

        private BlockStatement RewriteNestedBlock(BlockStatement block)
        {
            var statements = RewriteStatements(block.Statements);
            return statements.SequenceEqual(block.Statements) ? block : block with { Statements = statements };
        }

        private CatchClause RewriteNestedCatch(CatchClause clause)
        {
            var body = RewriteNestedBlock(clause.Body);
            return ReferenceEquals(body, clause.Body) ? clause : clause with { Body = body };
        }

        private static bool IsCallToSymbol(ExpressionNode expression, Symbol symbol)
        {
            return expression is CallExpression { Callee: IdentifierExpression { Name: var name } } &&
                   ReferenceEquals(name, symbol);
        }

        private bool TryRewriteStatement(StatementNode statement, ImmutableArray<StatementNode> remaining,
            out ImmutableArray<StatementNode> rewritten, out bool handledRemainder)
        {
            switch (statement)
            {
                case ReturnStatement returnStatement:
                    var returnExpression =
                        returnStatement.Expression ?? new IdentifierExpression(null, Symbols.Undefined);
                    if (_currentLoopBreakSymbol is { } breakSymbol &&
                        IsCallToSymbol(returnExpression, breakSymbol))
                    {
                        rewritten = [returnStatement];
                        handledRemainder = true;
                        return true;
                    }

                    rewritten = RewriteExpression(returnExpression, remaining,
                        expr => new ReturnStatement(returnStatement.Source, CreateInnerResolveCall(expr)),
                        false,
                        false,
                        out _,
                        out _);
                    handledRemainder = true;
                    return true;
                case ExpressionStatement expressionStatement:
                    rewritten = RewriteExpression(expressionStatement.Expression, remaining,
                        expr => expressionStatement with { Expression = expr },
                        true,
                        false,
                        out handledRemainder,
                        out var encounteredAwait);
                    return encounteredAwait;
                case VariableDeclaration { Declarators: [{ Initializer: { } initializer }] } variableDeclaration:
                    var declarator = variableDeclaration.Declarators[0];
                    rewritten = RewriteExpression(initializer, remaining,
                        expr => variableDeclaration with { Declarators = [declarator with { Initializer = expr }] },
                        true,
                        false,
                        out handledRemainder,
                        out var declarationAwait);
                    return declarationAwait;
                case ForEachStatement forEachStatement when ShouldRewriteForEach(forEachStatement):
                    rewritten = RewriteForEachStatement(forEachStatement, remaining);
                    handledRemainder = true;
                    return true;
                case WhileStatement whileStatement when ShouldRewriteWhile(whileStatement):
                    rewritten = RewriteWhileStatement(whileStatement, remaining);
                    handledRemainder = true;
                    return true;
                case DoWhileStatement doWhileStatement when ShouldRewriteDoWhile(doWhileStatement):
                    rewritten = RewriteDoWhileStatement(doWhileStatement, remaining);
                    handledRemainder = true;
                    return true;
                case ForStatement forStatement when ShouldRewriteFor(forStatement):
                    rewritten = RewriteForStatement(forStatement, remaining);
                    handledRemainder = true;
                    return true;
                case TryStatement tryStatement:
                    if (TryRewriteTryStatement(tryStatement, remaining, out rewritten))
                    {
                        handledRemainder = true;
                        return true;
                    }

                    break;
            }

            rewritten = default;
            handledRemainder = false;
            return false;
        }

        private bool TryRewriteTryStatement(TryStatement statement, ImmutableArray<StatementNode> remaining,
            out ImmutableArray<StatementNode> rewritten)
        {
            rewritten = default;
            if (statement.Finally is not null)
            {
                return false;
            }

            var tryNeedsRewrite = StatementNeedsAsyncHandling(statement.TryBlock);
            var currentCatchClause = statement.Catch;
            var catchNeedsRewrite = currentCatchClause is { } clause && StatementNeedsAsyncHandling(clause.Body);

            if (!tryNeedsRewrite && !catchNeedsRewrite)
            {
                return false;
            }

            var previousReject = _rejectIdentifier;
            _rejectIdentifier = null;
            var tryStatements = RewriteStatements(statement.TryBlock.Statements);
            _rejectIdentifier = previousReject;
            var tryBlock = new BlockStatement(null, tryStatements, statement.TryBlock.IsStrict);
            var tryFunction = new FunctionExpression(null, null, ImmutableArray<FunctionParameter>.Empty, tryBlock,
                false, false);
            var tryInvocation = new CallExpression(null, tryFunction, ImmutableArray<CallArgument>.Empty, false);

            var continuationBlock = BuildAfterLoopBlock(remaining);
            var successHandler = new FunctionExpression(null, null, ImmutableArray<FunctionParameter>.Empty,
                continuationBlock, false, false);
            var thenCall = new CallExpression(null,
                new MemberExpression(null, tryInvocation, new LiteralExpression(null, ThenPropertyName), false, false),
                [new CallArgument(null, successHandler, false)], false);

            ExpressionNode finalExpression = thenCall;

            if (currentCatchClause is { } catchClause)
            {
                var catchStatements = RewriteStatements(catchClause.Body.Statements);
                var combinedBuilder = ImmutableArray.CreateBuilder<StatementNode>(
                    catchStatements.Length + continuationBlock.Statements.Length);
                combinedBuilder.AddRange(catchStatements);
                combinedBuilder.AddRange(continuationBlock.Statements);
                var catchBlock = new BlockStatement(null, combinedBuilder.ToImmutable(), catchClause.Body.IsStrict);
                var catchParameters = catchClause.Binding switch
                {
                    IdentifierBinding id => [new FunctionParameter(null, id.Name, false, null, null)],
                    null => ImmutableArray<FunctionParameter>.Empty,
                    _ => [new FunctionParameter(null, null, false, catchClause.Binding, null)]
                };
                var catchHandler = new FunctionExpression(null, null, catchParameters, catchBlock, false, false);
                finalExpression = new CallExpression(null,
                    new MemberExpression(null, thenCall, new LiteralExpression(null, "catch"), false, false),
                    [new CallArgument(null, catchHandler, false)], false);
            }

            rewritten = [new ReturnStatement(null, finalExpression)];
            return true;
        }

        private static bool ShouldRewriteForEach(ForEachStatement statement)
        {
            return statement.Kind switch
            {
                ForEachKind.In => false,
                ForEachKind.AwaitOf => true,
                _ => StatementNeedsTransformation(statement.Body)
            };
        }

        private static bool ShouldRewriteWhile(WhileStatement statement)
        {
            return StatementNeedsTransformation(statement);
        }

        private static bool ShouldRewriteDoWhile(DoWhileStatement statement)
        {
            return StatementNeedsTransformation(statement.Body) ||
                   ExpressionNeedsTransformation(statement.Condition);
        }

        private static bool ShouldRewriteFor(ForStatement statement)
        {
            var initializerNeedsRewrite =
                statement.Initializer is not null && StatementNeedsTransformation(statement.Initializer);
            var conditionNeedsRewrite =
                statement.Condition is not null && ExpressionNeedsTransformation(statement.Condition);
            var incrementNeedsRewrite =
                statement.Increment is not null && ExpressionNeedsTransformation(statement.Increment);
            return initializerNeedsRewrite ||
                   conditionNeedsRewrite ||
                   incrementNeedsRewrite ||
                   StatementNeedsTransformation(statement.Body);
        }

        private ImmutableArray<StatementNode> RewriteForEachStatement(ForEachStatement statement,
            ImmutableArray<StatementNode> remaining)
        {
            var iteratorSymbol = Symbol.Intern($"__iterator{_temporaryId++}");
            var resultSymbol = Symbol.Intern($"__result{_temporaryId++}");
            var loopCheckSymbol = Symbol.Intern($"__loopCheck{_temporaryId++}");
            var loopResolveSymbol = Symbol.Intern($"__loopResolve{_temporaryId++}");
            var loopBreakSymbol = Symbol.Intern($"__loopBreak{_temporaryId++}");

            var iteratorDeclaration = CreateIteratorDeclaration(statement, iteratorSymbol);
            var afterLoopBlock = BuildAfterLoopBlock(remaining);
            var loopBreakDeclaration = CreateLoopBreakFunction(loopBreakSymbol, afterLoopBlock);
            var loopBodyBlock = BuildLoopBodyBlock(statement, loopCheckSymbol, resultSymbol, loopResolveSymbol,
                loopBreakSymbol);
            var afterLoopContinuationBlock = CreateLoopBreakInvocationBlock(loopBreakSymbol);
            var loopCheckDeclaration = BuildLoopCheckFunction(iteratorSymbol, loopCheckSymbol,
                resultSymbol, afterLoopContinuationBlock, loopBodyBlock);

            var loopInvocation = new CallExpression(null, new IdentifierExpression(null, loopCheckSymbol),
                ImmutableArray<CallArgument>.Empty, false);
            var startCallExpression = AttachCatch(loopInvocation, _rejectIdentifier);
            var startCall = new ReturnStatement(null, startCallExpression);

            return [iteratorDeclaration, loopBreakDeclaration, loopCheckDeclaration, startCall];
        }

        private ImmutableArray<StatementNode> RewriteWhileStatement(WhileStatement statement,
            ImmutableArray<StatementNode> remaining)
        {
            var loopCheckSymbol = Symbol.Intern($"__whileCheck{_temporaryId++}");
            var loopResolveSymbol = Symbol.Intern($"__loopResolve{_temporaryId++}");
            var loopBreakSymbol = Symbol.Intern($"__loopBreak{_temporaryId++}");

            var afterLoopBlock = BuildAfterLoopBlock(remaining);
            var loopBreakDeclaration = CreateLoopBreakFunction(loopBreakSymbol, afterLoopBlock);
            var loopBodyStatements = ExtractBodyStatements(statement.Body);
            var loopBodyBlock = BuildLoopBodyBlockFromStatements(loopBodyStatements, loopCheckSymbol,
                loopResolveSymbol, loopBreakSymbol);
            var afterLoopContinuationBlock = CreateLoopBreakInvocationBlock(loopBreakSymbol);
            var loopCheckDeclaration = BuildWhileLoopCheckFunction(statement.Condition, loopCheckSymbol, loopBodyBlock,
                afterLoopContinuationBlock);

            var loopInvocation = new CallExpression(null, new IdentifierExpression(null, loopCheckSymbol),
                ImmutableArray<CallArgument>.Empty, false);
            var startCallExpression = AttachCatch(loopInvocation, _rejectIdentifier);
            var startCall = new ReturnStatement(null, startCallExpression);

            return [loopBreakDeclaration, loopCheckDeclaration, startCall];
        }

        private ImmutableArray<StatementNode> RewriteDoWhileStatement(DoWhileStatement statement,
            ImmutableArray<StatementNode> remaining)
        {
            var loopCheckSymbol = Symbol.Intern($"__doWhileCheck{_temporaryId++}");
            var loopResolveSymbol = Symbol.Intern($"__loopResolve{_temporaryId++}");
            var loopBreakSymbol = Symbol.Intern($"__loopBreak{_temporaryId++}");

            var afterLoopBlock = BuildAfterLoopBlock(remaining);
            var loopBreakDeclaration = CreateLoopBreakFunction(loopBreakSymbol, afterLoopBlock);
            var loopBodyStatements = ExtractBodyStatements(statement.Body);
            var loopBodyBlock = BuildLoopBodyBlockFromStatements(loopBodyStatements, loopCheckSymbol,
                loopResolveSymbol, loopBreakSymbol);
            var afterLoopContinuationBlock = CreateLoopBreakInvocationBlock(loopBreakSymbol);
            var loopCheckDeclaration = BuildWhileLoopCheckFunction(statement.Condition, loopCheckSymbol, loopBodyBlock,
                afterLoopContinuationBlock);

            var startCallExpression = CreateLoopBodyInvocation(loopBodyBlock);
            var startCallWithCatch = AttachCatch(startCallExpression, _rejectIdentifier);
            var startCall = new ReturnStatement(null, startCallWithCatch);

            return [loopBreakDeclaration, loopCheckDeclaration, startCall];
        }

        private ImmutableArray<StatementNode> RewriteForStatement(ForStatement statement,
            ImmutableArray<StatementNode> remaining)
        {
            var loopCheckSymbol = Symbol.Intern($"__forCheck{_temporaryId++}");
            var loopResolveSymbol = Symbol.Intern($"__loopResolve{_temporaryId++}");
            var loopBreakSymbol = Symbol.Intern($"__loopBreak{_temporaryId++}");

            var afterLoopBlock = BuildAfterLoopBlock(remaining);
            var loopBreakDeclaration = CreateLoopBreakFunction(loopBreakSymbol, afterLoopBlock);
            var incrementStatements = statement.Increment is null
                ? ImmutableArray<StatementNode>.Empty
                : [new ExpressionStatement(null, statement.Increment)];
            var loopBodyStatements = ExtractBodyStatements(statement.Body);
            var loopBodyBlock = BuildLoopBodyBlockFromStatements(loopBodyStatements, loopCheckSymbol,
                loopResolveSymbol, loopBreakSymbol, incrementStatements);
            var afterLoopContinuationBlock = CreateLoopBreakInvocationBlock(loopBreakSymbol);
            var condition = statement.Condition ?? new LiteralExpression(null, true);
            var loopCheckDeclaration = BuildWhileLoopCheckFunction(condition, loopCheckSymbol,
                loopBodyBlock, afterLoopContinuationBlock);

            var loopInvocation = new CallExpression(null, new IdentifierExpression(null, loopCheckSymbol),
                ImmutableArray<CallArgument>.Empty, false);
            var startCallExpression = AttachCatch(loopInvocation, _rejectIdentifier);
            var startCall = new ReturnStatement(null, startCallExpression);

            var statementsBuilder = ImmutableArray.CreateBuilder<StatementNode>();
            if (statement.Initializer is { } initializer)
            {
                if (StatementNeedsTransformation(initializer))
                {
                    throw new NotSupportedException(
                        "Await expressions inside for-loop initializers are not supported by the typed CPS transformer yet.");
                }

                var rewrittenInitializer = RewriteNestedStatement(initializer);
                statementsBuilder.Add(rewrittenInitializer);
            }

            statementsBuilder.Add(loopBreakDeclaration);
            statementsBuilder.Add(loopCheckDeclaration);
            statementsBuilder.Add(startCall);
            return statementsBuilder.ToImmutable();
        }

        private static VariableDeclaration CreateIteratorDeclaration(ForEachStatement statement, Symbol iteratorSymbol)
        {
            var initializer = statement.Kind == ForEachKind.AwaitOf
                ? BuildGetAsyncIteratorCall(statement.Iterable)
                : BuildGetIteratorCall(statement.Iterable);
            var binding = new IdentifierBinding(null, iteratorSymbol);
            var declarator = new VariableDeclarator(null, binding, initializer);
            return new VariableDeclaration(null, VariableKind.Let, [declarator]);
        }

        private static CallExpression BuildGetAsyncIteratorCall(ExpressionNode iterable)
        {
            var callee = new IdentifierExpression(null, Symbol.Intern("__getAsyncIterator"));
            var argument = new CallArgument(null, iterable, false);
            return new CallExpression(null, callee, [argument], false);
        }

        private static CallExpression BuildGetIteratorCall(ExpressionNode iterable)
        {
            var symbolIdentifier = new IdentifierExpression(null, Symbol.Intern("Symbol"));
            var iteratorProperty = new MemberExpression(null, symbolIdentifier, new LiteralExpression(null, "iterator"),
                false, false);
            var iteratorAccessor = new MemberExpression(null, iterable, iteratorProperty, true, false);
            return new CallExpression(null, iteratorAccessor, ImmutableArray<CallArgument>.Empty, false);
        }

        private BlockStatement BuildAfterLoopBlock(ImmutableArray<StatementNode> remaining)
        {
            var continuation = RewriteStatements(remaining);
            if (!continuation.IsDefaultOrEmpty)
            {
                return new BlockStatement(null, continuation, isStrict);
            }

            var undefinedValue = new IdentifierExpression(null, Symbols.Undefined);
            continuation = [new ReturnStatement(null, CreateInnerResolveCall(undefinedValue))];

            return new BlockStatement(null, continuation, isStrict);
        }

        private BlockStatement BuildLoopBodyBlock(ForEachStatement statement, Symbol loopCheckSymbol,
            Symbol resultSymbol,
            Symbol loopResolveSymbol, Symbol loopBreakSymbol)
        {
            var resultIdentifier = new IdentifierExpression(null, resultSymbol);
            var valueExpression = new MemberExpression(null, resultIdentifier, new LiteralExpression(null, "value"),
                false, false);
            var assignment = CreateLoopBindingAssignment(statement, valueExpression);
            var extracted = ExtractBodyStatements(statement.Body);
            var builder = ImmutableArray.CreateBuilder<StatementNode>(extracted.Length + 1);
            builder.Add(assignment);
            builder.AddRange(extracted);
            return BuildLoopBodyBlockFromStatements(builder.ToImmutable(), loopCheckSymbol, loopResolveSymbol,
                loopBreakSymbol);
        }

        private BlockStatement BuildLoopBodyBlockFromStatements(
            ImmutableArray<StatementNode> statements,
            Symbol loopCheckSymbol,
            Symbol loopResolveSymbol,
            Symbol loopBreakSymbol,
            ImmutableArray<StatementNode>? loopPostIterationStatements = null)
        {
            var normalized = NormalizeStatements(statements);
            var loopResolveDeclaration =
                CreateLoopResolveFunction(loopResolveSymbol, loopCheckSymbol, loopPostIterationStatements);
            var previousBreakSymbol = _currentLoopBreakSymbol;
            _currentLoopBreakSymbol = loopBreakSymbol;
            var normalizedWithLoopControl =
                RewriteLoopControlStatements(normalized, loopResolveSymbol, loopBreakSymbol, true,
                    true, out var loopControlChanged);
            _currentLoopBreakSymbol = previousBreakSymbol;
            var loopBodyStatements = loopControlChanged ? normalizedWithLoopControl : normalized;
            var bodyRewriter = new AsyncFunctionRewriter(owner, isStrict, loopResolveSymbol, _rejectIdentifier)
            {
                _currentLoopBreakSymbol = loopBreakSymbol
            };
            var rewritten = bodyRewriter.Rewrite(loopBodyStatements);
            var builder = ImmutableArray.CreateBuilder<StatementNode>(rewritten.Length + 1);
            builder.Add(loopResolveDeclaration);
            builder.AddRange(rewritten);
            return new BlockStatement(null, builder.ToImmutable(), isStrict);
        }

        private FunctionDeclaration CreateLoopResolveFunction(Symbol loopResolveSymbol, Symbol loopCheckSymbol,
            ImmutableArray<StatementNode>? preLoopStatements = null)
        {
            var parameter = new FunctionParameter(null, Symbol.Intern("__loopValue"), false, null, null);
            BlockStatement body;
            if (preLoopStatements is { IsDefaultOrEmpty: false })
            {
                var normalized = NormalizeStatements(preLoopStatements.Value);
                var continuationRewriter =
                    new AsyncFunctionRewriter(owner, isStrict, loopCheckSymbol, _rejectIdentifier);
                var rewritten = continuationRewriter.Rewrite(normalized);
                body = new BlockStatement(null, rewritten, isStrict);
            }
            else
            {
                var call = new CallExpression(null, new IdentifierExpression(null, loopCheckSymbol),
                    ImmutableArray<CallArgument>.Empty, false);
                var returnStatement = new ReturnStatement(null, call);
                body = new BlockStatement(null, [returnStatement], isStrict);
            }

            var functionExpression = new FunctionExpression(null, loopResolveSymbol,
                [parameter], body, false, false);
            return new FunctionDeclaration(null, loopResolveSymbol, functionExpression);
        }

        private static FunctionDeclaration CreateLoopBreakFunction(Symbol loopBreakSymbol,
            BlockStatement afterLoopBlock)
        {
            var loopBreakFunction = new FunctionExpression(null, loopBreakSymbol,
                ImmutableArray<FunctionParameter>.Empty, afterLoopBlock, false, false);
            return new FunctionDeclaration(null, loopBreakSymbol, loopBreakFunction);
        }

        private BlockStatement CreateLoopBreakInvocationBlock(Symbol loopBreakSymbol)
        {
            var call = new CallExpression(null, new IdentifierExpression(null, loopBreakSymbol),
                ImmutableArray<CallArgument>.Empty, false);
            var returnStatement = new ReturnStatement(null, call);
            return new BlockStatement(null, [returnStatement], isStrict);
        }

        private static CallExpression CreateLoopBodyInvocation(BlockStatement loopBodyBlock)
        {
            var startFunction = new FunctionExpression(null, null,
                ImmutableArray<FunctionParameter>.Empty, loopBodyBlock, false, false);
            return new CallExpression(null, startFunction, ImmutableArray<CallArgument>.Empty, false);
        }

        private static ReturnStatement CreateLoopContinueReturn(Symbol loopResolveSymbol)
        {
            var undefinedValue = new IdentifierExpression(null, Symbols.Undefined);
            var argument = new CallArgument(null, undefinedValue, false);
            var call = new CallExpression(null, new IdentifierExpression(null, loopResolveSymbol),
                [argument], false);
            return new ReturnStatement(null, call);
        }

        private static ReturnStatement CreateLoopBreakReturn(Symbol loopBreakSymbol)
        {
            var call = new CallExpression(null, new IdentifierExpression(null, loopBreakSymbol),
                ImmutableArray<CallArgument>.Empty, false);
            return new ReturnStatement(null, call);
        }

        private ImmutableArray<StatementNode> RewriteLoopControlStatements(
            ImmutableArray<StatementNode> statements,
            Symbol loopResolveSymbol,
            Symbol loopBreakSymbol,
            bool rewriteBreak,
            bool rewriteContinue,
            out bool changed)
        {
            if (statements.IsDefaultOrEmpty)
            {
                changed = false;
                return statements;
            }

            var builder = ImmutableArray.CreateBuilder<StatementNode>(statements.Length);
            changed = false;
            foreach (var statement in statements)
            {
                var rewritten = RewriteLoopControlStatement(statement, loopResolveSymbol, loopBreakSymbol,
                    rewriteBreak, rewriteContinue, out var statementChanged);
                builder.Add(rewritten);
                if (statementChanged)
                {
                    changed = true;
                }
            }

            return changed ? builder.ToImmutable() : statements;
        }

        private StatementNode RewriteLoopControlStatement(
            StatementNode statement,
            Symbol loopResolveSymbol,
            Symbol loopBreakSymbol,
            bool rewriteBreak,
            bool rewriteContinue,
            out bool changed)
        {
            switch (statement)
            {
                case BreakStatement { Label: null } when rewriteBreak:
                    changed = true;
                    return CreateLoopBreakReturn(loopBreakSymbol);
                case ContinueStatement { Label: null } when rewriteContinue:
                    changed = true;
                    return CreateLoopContinueReturn(loopResolveSymbol);
                case BlockStatement block:
                    var rewrittenBlock = RewriteLoopControlBlock(block, loopResolveSymbol, loopBreakSymbol,
                        rewriteBreak,
                        rewriteContinue, out var blockChanged);
                    changed = blockChanged;
                    return blockChanged ? rewrittenBlock : block;
                case IfStatement ifStatement:
                    var thenBranch = RewriteLoopControlStatement(ifStatement.Then, loopResolveSymbol, loopBreakSymbol,
                        rewriteBreak, rewriteContinue, out var thenChanged);
                    StatementNode? rewrittenElse = null;
                    var elseChanged = false;
                    if (ifStatement.Else is { } elseStatement)
                    {
                        rewrittenElse = RewriteLoopControlStatement(elseStatement, loopResolveSymbol, loopBreakSymbol,
                            rewriteBreak, rewriteContinue, out elseChanged);
                    }

                    if (thenChanged || elseChanged)
                    {
                        changed = true;
                        return ifStatement with
                        {
                            Then = thenChanged ? thenBranch : ifStatement.Then,
                            Else = elseChanged ? rewrittenElse : ifStatement.Else
                        };
                    }

                    changed = false;
                    return ifStatement;
                case TryStatement tryStatement:
                    var tryBlock = RewriteLoopControlBlock(tryStatement.TryBlock, loopResolveSymbol, loopBreakSymbol,
                        rewriteBreak, rewriteContinue, out var tryChanged);
                    var catchClause = tryStatement.Catch;
                    var catchChanged = false;
                    if (tryStatement.Catch is { } existingCatch)
                    {
                        catchClause = RewriteLoopControlCatch(existingCatch, loopResolveSymbol, loopBreakSymbol,
                            rewriteBreak, rewriteContinue, out catchChanged);
                    }

                    var finallyBlock = tryStatement.Finally;
                    var finallyChanged = false;
                    if (tryStatement.Finally is { } existingFinally)
                    {
                        finallyBlock = RewriteLoopControlBlock(existingFinally, loopResolveSymbol, loopBreakSymbol,
                            rewriteBreak, rewriteContinue, out finallyChanged);
                    }

                    if (tryChanged || catchChanged || finallyChanged)
                    {
                        changed = true;
                        return tryStatement with
                        {
                            TryBlock = tryChanged ? tryBlock : tryStatement.TryBlock,
                            Catch = catchChanged ? catchClause : tryStatement.Catch,
                            Finally = finallyChanged ? finallyBlock : tryStatement.Finally
                        };
                    }

                    changed = false;
                    return tryStatement;
                case LabeledStatement labeledStatement:
                    var inner = RewriteLoopControlStatement(labeledStatement.Statement, loopResolveSymbol,
                        loopBreakSymbol,
                        rewriteBreak, rewriteContinue, out var innerChanged);
                    if (innerChanged)
                    {
                        changed = true;
                        return labeledStatement with { Statement = inner };
                    }

                    changed = false;
                    return labeledStatement;
                case SwitchStatement switchStatement:
                    var cases = ImmutableArray.CreateBuilder<SwitchCase>(switchStatement.Cases.Length);
                    var casesChanged = false;
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        var caseBody = RewriteLoopControlBlock(switchCase.Body, loopResolveSymbol, loopBreakSymbol,
                            false, rewriteContinue, out var bodyChanged);
                        if (bodyChanged)
                        {
                            cases.Add(switchCase with { Body = caseBody });
                            casesChanged = true;
                        }
                        else
                        {
                            cases.Add(switchCase);
                        }
                    }

                    if (casesChanged)
                    {
                        changed = true;
                        return switchStatement with { Cases = cases.ToImmutable() };
                    }

                    changed = false;
                    return switchStatement;
                default:
                    changed = false;
                    return statement;
            }
        }

        private BlockStatement RewriteLoopControlBlock(
            BlockStatement block,
            Symbol loopResolveSymbol,
            Symbol loopBreakSymbol,
            bool rewriteBreak,
            bool rewriteContinue,
            out bool changed)
        {
            var statements = RewriteLoopControlStatements(block.Statements, loopResolveSymbol, loopBreakSymbol,
                rewriteBreak, rewriteContinue, out changed);
            return changed ? block with { Statements = statements } : block;
        }

        private CatchClause RewriteLoopControlCatch(
            CatchClause clause,
            Symbol loopResolveSymbol,
            Symbol loopBreakSymbol,
            bool rewriteBreak,
            bool rewriteContinue,
            out bool changed)
        {
            var body = RewriteLoopControlBlock(clause.Body, loopResolveSymbol, loopBreakSymbol, rewriteBreak,
                rewriteContinue, out changed);
            return changed ? clause with { Body = body } : clause;
        }

        private static ImmutableArray<StatementNode> ExtractBodyStatements(StatementNode body)
        {
            if (body is BlockStatement block)
            {
                return block.Statements;
            }

            return [body];
        }

        private static StatementNode CreateLoopBindingAssignment(ForEachStatement statement,
            ExpressionNode valueExpression)
        {
            if (statement.DeclarationKind is { } declarationKind)
            {
                var declarator = new VariableDeclarator(null, statement.Target, valueExpression);
                return new VariableDeclaration(null, declarationKind, [declarator]);
            }

            if (statement.Target is IdentifierBinding identifierBinding)
            {
                var assignment = new AssignmentExpression(null, identifierBinding.Name, valueExpression);
                return new ExpressionStatement(null, assignment);
            }

            var destructuring = new DestructuringAssignmentExpression(null, statement.Target, valueExpression);
            return new ExpressionStatement(null, destructuring);
        }

        private FunctionDeclaration BuildLoopCheckFunction(Symbol iteratorSymbol, Symbol loopCheckSymbol,
            Symbol resultSymbol, BlockStatement afterLoopBlock, BlockStatement loopBodyBlock)
        {
            var iteratorIdentifier = new IdentifierExpression(null, iteratorSymbol);
            var iteratorNextCallee = new IdentifierExpression(null, Symbol.Intern("__iteratorNext"));
            var iteratorNextCall = new CallExpression(null, iteratorNextCallee,
                [new CallArgument(null, iteratorIdentifier, false)], false);

            var thenTarget = new MemberExpression(null, iteratorNextCall, new LiteralExpression(null, ThenPropertyName),
                false, false);
            var parameter = new FunctionParameter(null, resultSymbol, false, null, null);
            var resultIdentifier = new IdentifierExpression(null, resultSymbol);
            var doneExpression =
                new MemberExpression(null, resultIdentifier, new LiteralExpression(null, "done"), false, false);
            var ifStatement = new IfStatement(null, doneExpression, afterLoopBlock, loopBodyBlock);
            var callbackBody = new BlockStatement(null, [ifStatement], isStrict);
            var callback = new FunctionExpression(null, null, [parameter], callbackBody, false, false);
            var thenCall = new CallExpression(null, thenTarget, [new CallArgument(null, callback, false)], false);
            var catchCall = AttachCatch(thenCall, _rejectIdentifier);
            var returnStatement = new ReturnStatement(null, catchCall);
            var body = new BlockStatement(null, [returnStatement], isStrict);
            var loopCheckFunction = new FunctionExpression(null, loopCheckSymbol,
                ImmutableArray<FunctionParameter>.Empty,
                body, false, false);
            return new FunctionDeclaration(null, loopCheckSymbol, loopCheckFunction);
        }

        private FunctionDeclaration BuildWhileLoopCheckFunction(ExpressionNode condition, Symbol loopCheckSymbol,
            BlockStatement loopBodyBlock, BlockStatement afterLoopContinuationBlock)
        {
            var conditionStatement = BuildWhileConditionStatement(condition, loopBodyBlock, afterLoopContinuationBlock);
            var body = conditionStatement as BlockStatement ?? new BlockStatement(null, [conditionStatement], isStrict);
            var functionExpression = new FunctionExpression(null, loopCheckSymbol,
                ImmutableArray<FunctionParameter>.Empty, body, false, false);
            return new FunctionDeclaration(null, loopCheckSymbol, functionExpression);
        }

        private StatementNode BuildWhileConditionStatement(ExpressionNode condition, BlockStatement loopBodyBlock,
            BlockStatement afterLoopContinuationBlock)
        {
            if (!TryExtractAwait(condition, out var awaitExpression, out var rebuild))
            {
                return new IfStatement(null, condition, loopBodyBlock, afterLoopContinuationBlock);
            }

            var awaited = owner.EnsureSupportedAwaitOperand(awaitExpression.Expression);
            var awaitCall = CreateAwaitHelperCall(awaited);
            var tempSymbol = Symbol.Intern($"__conditionValue{_temporaryId++}");
            var placeholder = new IdentifierExpression(null, tempSymbol);
            var rebuiltCondition = rebuild(placeholder);
            var innerStatement =
                BuildWhileConditionStatement(rebuiltCondition, loopBodyBlock, afterLoopContinuationBlock);
            var callbackBody = innerStatement is BlockStatement block
                ? block
                : new BlockStatement(null, [innerStatement], isStrict);
            var parameter = new FunctionParameter(null, tempSymbol, false, null, null);
            var callback = new FunctionExpression(null, null, [parameter], callbackBody, false, false);
            var thenTarget = new MemberExpression(null, awaitCall, new LiteralExpression(null, ThenPropertyName),
                false, false);
            var thenCall = new CallExpression(null, thenTarget, [new CallArgument(null, callback, false)], false);
            var catchCall = AttachCatch(thenCall, _rejectIdentifier);
            return new ReturnStatement(null, catchCall);
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

            var awaited = owner.EnsureSupportedAwaitOperand(awaitExpression.Expression);
            var awaitCall = CreateAwaitHelperCall(awaited);
            var tempSymbol = Symbol.Intern($"__awaitValue{_temporaryId++}");
            var placeholder = new IdentifierExpression(null, tempSymbol);
            var replaced = rebuild(placeholder);
            var callbackStatements = RewriteExpression(replaced, remaining, createStatement, continueAfter,
                true, out _, out _).ToBuilder();
            if (continueAfter)
            {
                var needsContinuation = callbackStatements.Count == 0 ||
                                        callbackStatements[^1] is not ReturnStatement;
                if (needsContinuation)
                {
                    var undefinedValue = new IdentifierExpression(null, Symbols.Undefined);
                    callbackStatements.Add(new ReturnStatement(null, CreateInnerResolveCall(undefinedValue)));
                }
            }

            var callbackBody = new BlockStatement(null, callbackStatements.ToImmutable(), isStrict);
            var parameter = new FunctionParameter(null, tempSymbol, false, null, null);
            var callback = new FunctionExpression(null, null, [parameter], callbackBody, false, false);
            var thenCall = CreateThenInvocation(awaitCall, callback);
            var withCatch = AttachCatch(thenCall, _rejectIdentifier);
            handlesRemainder = true;
            encounteredAwait = true;
            return [new ReturnStatement(null, withCatch)];
        }

        private static bool TryExtractAwait(ExpressionNode expression, out AwaitExpression awaitExpression,
            out Func<ExpressionNode, ExpressionNode> rebuild)
        {
            switch (expression)
            {
                case AwaitExpression direct:
                    awaitExpression = direct;
                    rebuild = value => value;
                    return true;
                case BinaryExpression binary
                    when TryExtractAwait(binary.Left, out awaitExpression, out var leftRebuild):
                    rebuild = value => binary with { Left = leftRebuild(value) };
                    return true;
                case BinaryExpression binary
                    when TryExtractAwait(binary.Right, out awaitExpression, out var rightRebuild):
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
                case NewExpression newExpression when TryExtractAwait(newExpression.Constructor, out awaitExpression,
                    out var ctorRebuild):
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
                case MemberExpression member
                    when TryExtractAwait(member.Target, out awaitExpression, out var targetRebuild):
                    rebuild = value => member with { Target = targetRebuild(value) };
                    return true;
                case MemberExpression member
                    when TryExtractAwait(member.Property, out awaitExpression, out var propertyRebuild):
                    rebuild = value => member with { Property = propertyRebuild(value) };
                    return true;
                case ConditionalExpression conditional
                    when TryExtractAwait(conditional.Test, out awaitExpression, out var testRebuild):
                    rebuild = value => conditional with { Test = testRebuild(value) };
                    return true;
                case ConditionalExpression conditional when TryExtractAwait(conditional.Consequent, out awaitExpression,
                    out var consequentRebuild):
                    rebuild = value => conditional with { Consequent = consequentRebuild(value) };
                    return true;
                case ConditionalExpression conditional when TryExtractAwait(conditional.Alternate, out awaitExpression,
                    out var alternateRebuild):
                    rebuild = value => conditional with { Alternate = alternateRebuild(value) };
                    return true;
                case SequenceExpression sequence
                    when TryExtractAwait(sequence.Left, out awaitExpression, out var leftSequence):
                    rebuild = value => sequence with { Left = leftSequence(value) };
                    return true;
                case SequenceExpression sequence
                    when TryExtractAwait(sequence.Right, out awaitExpression, out var rightSequence):
                    rebuild = value => sequence with { Right = rightSequence(value) };
                    return true;
                case AssignmentExpression assignment
                    when TryExtractAwait(assignment.Value, out awaitExpression, out var assignmentRebuild):
                    rebuild = value => assignment with { Value = assignmentRebuild(value) };
                    return true;
                case PropertyAssignmentExpression propertyAssignment when TryExtractAwait(propertyAssignment.Value,
                    out awaitExpression, out var propertyValueRebuild):
                    rebuild = value => propertyAssignment with { Value = propertyValueRebuild(value) };
                    return true;
                case IndexAssignmentExpression indexAssignment when TryExtractAwait(indexAssignment.Value,
                    out awaitExpression, out var indexValueRebuild):
                    rebuild = value => indexAssignment with { Value = indexValueRebuild(value) };
                    return true;
                case UnaryExpression unary
                    when TryExtractAwait(unary.Operand, out awaitExpression, out var operandRebuild):
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
                case TaggedTemplateExpression taggedTemplate
                    when TryExtractAwait(taggedTemplate.Tag, out awaitExpression, out var tagRebuild):
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
}
