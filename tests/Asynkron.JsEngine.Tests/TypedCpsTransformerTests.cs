using Asynkron.JsEngine.Ast;
using Xunit.Sdk;

namespace Asynkron.JsEngine.Tests;

public class TypedCpsTransformerTests
{
    [Fact]
    public async Task NeedsTransformation_ReturnsTrueForAsyncFunction()
    {
        var js = "async function demo() { return await Promise.resolve(1); }";
        await using var engine = new JsEngine();
        var (_, typedConstant, _) = engine.ParseWithTransformationSteps(js);

        Assert.True(TypedCpsTransformer.NeedsTransformation(typedConstant));
    }

    [Fact]
    public async Task AsyncFunctionWithSingleAwait_RewritesToPromiseChain()
    {
        const string source = """
            async function getValue() {
                return await Promise.resolve(42);
            }
            """;

        await using var engine = new JsEngine();
        var (_, typedBefore, _) = engine.ParseWithTransformationSteps(source);

        var transformer = new TypedCpsTransformer();
        var transformed = transformer.Transform(typedBefore);

        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(transformed.Body));
        Assert.False(declaration.Function.IsAsync);

        var returnStatement = Assert.IsType<ReturnStatement>(Assert.Single(declaration.Function.Body.Statements));
        var newExpression = Assert.IsType<NewExpression>(returnStatement.Expression);
        AssertPromiseConstructor(newExpression.Constructor);

        var executor = Assert.IsType<FunctionExpression>(Assert.Single(newExpression.Arguments));
        var tryStatement = Assert.IsType<TryStatement>(Assert.Single(executor.Body.Statements));
        var promiseReturn = Assert.IsType<ReturnStatement>(Assert.Single(tryStatement.TryBlock.Statements));
        var catchCall = Assert.IsType<CallExpression>(promiseReturn.Expression);
        var catchMember = Assert.IsType<MemberExpression>(catchCall.Callee);
        var catchProperty = Assert.IsType<LiteralExpression>(catchMember.Property);
        Assert.Equal("catch", catchProperty.Value);

        var thenInvocation = Assert.IsType<CallExpression>(catchMember.Target);
        var thenMember = Assert.IsType<MemberExpression>(thenInvocation.Callee);
        var thenProperty = Assert.IsType<LiteralExpression>(thenMember.Property);
        Assert.Equal("then", thenProperty.Value);

        var awaitHelperCall = Assert.IsType<CallExpression>(thenMember.Target);
        AssertIdentifierOrMember(awaitHelperCall.Callee, Symbol.Intern("__awaitHelper"), "__awaitHelper");

        Assert.Single(thenInvocation.Arguments);
        var callback = Assert.IsType<FunctionExpression>(thenInvocation.Arguments[0].Expression);
        var callbackBody = Assert.IsType<BlockStatement>(callback.Body);
        var callbackReturn = Assert.IsType<ReturnStatement>(Assert.Single(callbackBody.Statements));
        var resolveCall = Assert.IsType<CallExpression>(callbackReturn.Expression);
        AssertIdentifierOrMember(resolveCall.Callee, Symbol.Intern("__resolve"), "__resolve");

        Assert.Single(catchCall.Arguments);
        var catchCallback = Assert.IsType<FunctionExpression>(catchCall.Arguments[0].Expression);
        var catchCallbackReturn = Assert.IsType<ReturnStatement>(Assert.Single(catchCallback.Body.Statements));
        var catchRejectCall = Assert.IsType<CallExpression>(catchCallbackReturn.Expression);
        AssertIdentifierOrMember(catchRejectCall.Callee, Symbol.Intern("__reject"), "__reject");
    }

    [Fact]
    public async Task AsyncFunctionWithMultipleStatements_Rewrites()
    {
        const string source = """
            async function fail() {
                let x = await Promise.resolve(1);
                return x;
            }
            """;

        await using var engine = new JsEngine();
        var (_, typedBefore, _) = engine.ParseWithTransformationSteps(source);
        var transformer = new TypedCpsTransformer();

        var transformed = transformer.Transform(typedBefore);

        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(transformed.Body));
        Assert.False(declaration.Function.IsAsync);

        var functionReturn = Assert.IsType<ReturnStatement>(Assert.Single(declaration.Function.Body.Statements));
        var newExpression = Assert.IsType<NewExpression>(functionReturn.Expression);
        AssertPromiseConstructor(newExpression.Constructor);

        var executor = Assert.IsType<FunctionExpression>(Assert.Single(newExpression.Arguments));
        var tryStatement = Assert.IsType<TryStatement>(Assert.Single(executor.Body.Statements));
        Assert.Contains(tryStatement.TryBlock.Statements, statement => statement is ReturnStatement);

        var catchClause = Assert.IsType<CatchClause>(tryStatement.Catch);
        var catchReturn = Assert.IsType<ReturnStatement>(Assert.Single(catchClause.Body.Statements));
        var rejectCall = Assert.IsType<CallExpression>(catchReturn.Expression);
        AssertIdentifierOrMember(rejectCall.Callee, Symbol.Intern("__reject"), "__reject");
    }

    [Fact]
    public async Task ForAwaitLoop_WithBreak_RewritesControlFlow()
    {
        const string source = """
            async function test() {
                let arr = [1, 2, 3];
                for await (let item of arr) {
                    if (item === 2) {
                        break;
                    }
                }
            }
            """;

        await using var engine = new JsEngine();
        var (_, typedBefore, _) = engine.ParseWithTransformationSteps(source);

        var transformer = new TypedCpsTransformer();
        var transformed = transformer.Transform(typedBefore);

        var snapshot = TypedAstSnapshot.Create(transformed);
        Console.WriteLine(snapshot);
        Assert.DoesNotContain(GetAllStatements(transformed), static statement => statement is BreakStatement);
    }

    // [Fact]
    // public void Typed_pipeline_matches_cons_pipeline_for_async_function()
    // {
    //     const string source = """
    //         async function demo() {
    //             return await Promise.resolve(1);
    //         }
    //         """;
    //
    //     var consOriginal = JsEngine.ParseWithoutTransformation(source);
    //     var consForTyped = TypedTransformerTestHelpers.CloneWithoutSourceReferences(consOriginal);
    //     var constantTransformer = new ConstantExpressionTransformer();
    //     var consConstant = constantTransformer.Transform(consOriginal);
    //     var cpsTransformer = new CpsTransformer();
    //     var consCps = CpsTransformer.NeedsTransformation(consConstant)
    //         ? cpsTransformer.Transform(consConstant)
    //         : consConstant;
    //     var builder = new SExpressionAstBuilder();
    //     var expected = builder.BuildProgram(
    //         TypedTransformerTestHelpers.CloneWithoutSourceReferences(consCps));
    //     var expectedSnapshot = TypedAstSnapshot.Create(expected);
    //
    //     var typedBuilder = new SExpressionAstBuilder();
    //     var typedProgram = typedBuilder.BuildProgram(consForTyped);
    //     var typedConstant = new TypedConstantExpressionTransformer().Transform(typedProgram);
    //     var typedTransformer = new TypedCpsTransformer();
    //     var typedCps = typedTransformer.Transform(typedConstant);
    //     var actualSnapshot = TypedAstSnapshot.Create(typedCps);
    //     Assert.Equal(expectedSnapshot, actualSnapshot);
    // }

    private static IEnumerable<StatementNode> GetAllStatements(ProgramNode program)
    {
        foreach (var statement in program.Body)
        {
            foreach (var nested in EnumerateStatements(statement))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<StatementNode> EnumerateStatements(StatementNode statement)
    {
        while (true)
        {
            yield return statement;
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var child in block.Statements)
                    {
                        foreach (var nested in EnumerateStatements(child))
                        {
                            yield return nested;
                        }
                    }

                    break;
                case IfStatement ifStatement:
                    foreach (var nested in EnumerateStatements(ifStatement.Then))
                    {
                        yield return nested;
                    }

                    if (ifStatement.Else is not null)
                    {
                        statement = ifStatement.Else;
                        continue;
                    }

                    break;
                case TryStatement tryStatement:
                    foreach (var nested in EnumerateStatements(tryStatement.TryBlock))
                    {
                        yield return nested;
                    }

                    if (tryStatement.Catch is not null)
                    {
                        foreach (var nested in EnumerateStatements(tryStatement.Catch.Body))
                        {
                            yield return nested;
                        }
                    }

                    if (tryStatement.Finally is not null)
                    {
                        statement = tryStatement.Finally;
                        continue;
                    }

                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        foreach (var nested in EnumerateStatements(switchCase.Body))
                        {
                            yield return nested;
                        }
                    }

                    break;
            }

            break;
        }
    }

    private static void AssertPromiseConstructor(ExpressionNode expression)
    {
        switch (expression)
        {
            case IdentifierExpression identifier:
                Assert.Same(Symbol.Intern("Promise"), identifier.Name);
                return;
            case MemberExpression member when member.Property is LiteralExpression literal &&
                                              literal.Value is string literalValue &&
                                              literalValue == "Promise":
                return;
            default:
                throw new XunitException("Expected Promise constructor.");
        }
    }

    private static void AssertIdentifierOrMember(ExpressionNode expression, Symbol symbol, string propertyName)
    {
        switch (expression)
        {
            case IdentifierExpression identifier:
                Assert.Same(symbol, identifier.Name);
                break;
            case MemberExpression member when MatchesMemberName(member.Property, symbol, propertyName):
                break;
            case MemberExpression member:
                var propertyLiteral = member.Property as LiteralExpression;
                var literalDetail = propertyLiteral?.Value is null
                    ? string.Empty
                    : $" value '{propertyLiteral.Value}'";
                var propertyType = propertyLiteral?.Value is not null
                    ? $"{member.Property.GetType().Name} ({propertyLiteral.Value.GetType().Name})"
                    : member.Property.GetType().Name;
                throw new XunitException(
                    $"Expected reference to '{propertyName}', but saw member property {propertyType}{literalDetail}.");
            default:
                throw new XunitException(
                    $"Expected reference to '{propertyName}', but saw {expression.GetType().Name}.");
        }
    }

    private static bool MatchesMemberName(ExpressionNode property, Symbol symbol, string propertyName)
    {
        return property switch
        {
            LiteralExpression literal when literal.Value is string literalValue &&
                                          literalValue == propertyName => true,
            LiteralExpression literal when literal.Value is Symbol literalSymbol &&
                                          ReferenceEquals(literalSymbol, symbol) => true,
            IdentifierExpression identifier when ReferenceEquals(identifier.Name, symbol) => true,
            _ => false
        };
    }
}
