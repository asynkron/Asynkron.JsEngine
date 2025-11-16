using System.Threading.Tasks;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Lisp;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class TypedCpsTransformerTests
{
    [Fact]
    public async Task NeedsTransformation_ReturnsTrueForAsyncFunction()
    {
        var builder = new SExpressionAstBuilder();
        var js = "async function demo() { return await Promise.resolve(1); }";
        await using var engine = new JsEngine();
        var (_, constantFolded, _) = engine.ParseWithTransformationSteps(js);
        var program = builder.BuildProgram(constantFolded);

        Assert.True(TypedCpsTransformer.NeedsTransformation(program));
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
        var (_, constantFolded, _) = engine.ParseWithTransformationSteps(source);
        var builder = new SExpressionAstBuilder();
        var typedBefore = builder.BuildProgram(constantFolded);

        var transformer = new TypedCpsTransformer();
        var transformed = transformer.Transform(typedBefore);

        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(transformed.Body));
        Assert.False(declaration.Function.IsAsync);

        var returnStatement = Assert.IsType<ReturnStatement>(Assert.Single(declaration.Function.Body.Statements));
        var newExpression = Assert.IsType<NewExpression>(returnStatement.Expression);
        var constructor = Assert.IsType<IdentifierExpression>(newExpression.Constructor);
        Assert.Same(Symbol.Intern("Promise"), constructor.Name);

        var executor = Assert.IsType<FunctionExpression>(Assert.Single(newExpression.Arguments));
        var tryStatement = Assert.IsType<TryStatement>(Assert.Single(executor.Body.Statements));
        var expressionStatement = Assert.IsType<ExpressionStatement>(
            Assert.Single(tryStatement.TryBlock.Statements));
        var thenCall = Assert.IsType<CallExpression>(expressionStatement.Expression);
        var member = Assert.IsType<MemberExpression>(thenCall.Callee);
        var awaitHelperCall = Assert.IsType<CallExpression>(member.Target);
        var helperIdentifier = Assert.IsType<IdentifierExpression>(awaitHelperCall.Callee);
        Assert.Same(Symbol.Intern("__awaitHelper"), helperIdentifier.Name);

        var thenProperty = Assert.IsType<IdentifierExpression>(member.Property);
        Assert.Same(Symbol.Intern("then"), thenProperty.Name);

        var callback = Assert.IsType<FunctionExpression>(thenCall.Arguments[0].Expression);
        var callbackBody = Assert.IsType<BlockStatement>(callback.Body);
        var callbackReturn = Assert.IsType<ReturnStatement>(Assert.Single(callbackBody.Statements));
        var resolveCall = Assert.IsType<CallExpression>(callbackReturn.Expression);
        var resolveIdentifier = Assert.IsType<IdentifierExpression>(resolveCall.Callee);
        Assert.Same(Symbol.Intern("__resolve"), resolveIdentifier.Name);

        var rejectArgument = thenCall.Arguments[1].Expression as IdentifierExpression;
        Assert.NotNull(rejectArgument);
        Assert.Same(Symbol.Intern("__reject"), rejectArgument!.Name);
    }

    [Fact]
    public async Task AsyncFunctionWithMultipleStatements_IsRejected()
    {
        const string source = """
            async function fail() {
                let x = await Promise.resolve(1);
                return x;
            }
            """;

        await using var engine = new JsEngine();
        var (_, constantFolded, _) = engine.ParseWithTransformationSteps(source);
        var builder = new SExpressionAstBuilder();
        var typedBefore = builder.BuildProgram(constantFolded);
        var transformer = new TypedCpsTransformer();

        Assert.Throws<NotSupportedException>(() => transformer.Transform(typedBefore));
    }
}
