using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Lisp;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class TypedConstantExpressionTransformerTests
{
    [Fact]
    public void Folds_nested_binary_expressions()
    {
        var transformer = new TypedConstantExpressionTransformer();
        var expression = new BinaryExpression(
            Source: null,
            Operator: "+",
            Left: new LiteralExpression(null, 1d),
            Right: new BinaryExpression(null, "*", new LiteralExpression(null, 2d), new LiteralExpression(null, 3d)));
        var program = new ProgramNode(null,
            ImmutableArray.Create<StatementNode>(new ExpressionStatement(null, expression)), false);

        var transformed = transformer.Transform(program);

        var statement = Assert.IsType<ExpressionStatement>(transformed.Body[0]);
        var literal = Assert.IsType<LiteralExpression>(statement.Expression);
        Assert.Equal(7d, literal.Value);
    }

    [Fact]
    public void Leaves_non_constant_trees_untouched()
    {
        var transformer = new TypedConstantExpressionTransformer();
        var program = new ProgramNode(
            Source: null,
            Body: ImmutableArray.Create<StatementNode>(
                new ExpressionStatement(null,
                    new BinaryExpression(null, "+",
                        new IdentifierExpression(null, Symbol.Intern("value")),
                        new LiteralExpression(null, 1d)))),
            IsStrict: false);

        var transformed = transformer.Transform(program);

        Assert.Same(program, transformed);
    }
}
