using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
public sealed class AstShapeAnalyzerSingleYieldTests
{
    [Fact]
    public void TryFindSingleYield_IgnoresYieldInsideClassExtendsExpression()
    {
        var returnExpression = ParseGeneratorReturnExpression("""
            class Derived extends (yield "heritage") {}
            """);

        var found = AstShapeAnalyzer.TryFindSingleYield(returnExpression, out var yieldExpression);

        Assert.False(found);
        Assert.Null(yieldExpression);
    }

    [Fact]
    public void TryFindSingleYield_IgnoresYieldInsideClassComputedMemberName()
    {
        var returnExpression = ParseGeneratorReturnExpression("""
            class Named {
                [yield "name"]() {}
            }
            """);

        var found = AstShapeAnalyzer.TryFindSingleYield(returnExpression, out var yieldExpression);

        Assert.False(found);
        Assert.Null(yieldExpression);
    }

    private static ExpressionNode ParseGeneratorReturnExpression(string expressionSource)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze($$"""
            function* probe() {
                return {{expressionSource}};
            }
            """);
        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(pipeline.Analyzed.Body));
        var returnStatement = Assert.IsType<ReturnStatement>(Assert.Single(declaration.Function.Body.Statements));
        Assert.NotNull(returnStatement.Expression);
        return returnStatement.Expression;
    }
}
