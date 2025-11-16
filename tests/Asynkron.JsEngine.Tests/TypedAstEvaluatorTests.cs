using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Tests;

public class TypedAstEvaluatorTests
{
    [Fact]
    public void EvaluateProgram_SupportsMemberAccessAndCalls()
    {
        const string source = @"
            let counter = {
                value: 0,
                inc: function() {
                    this.value = this.value + 1;
                }
            };
            counter.inc();
            counter.value;
        ";

        var program = BuildTypedProgram(source);
        var environment = new JsEnvironment(isFunctionScope: true);

        var result = TypedAstEvaluator.EvaluateProgram(program, environment);

        Assert.Equal(1d, result);
    }

    [Fact]
    public void EvaluateProgram_SupportsWhileLoops()
    {
        const string source = @"
            var i = 0;
            while (i < 3) {
                i = i + 1;
            }
            i;
        ";

        var program = BuildTypedProgram(source);
        var environment = new JsEnvironment(isFunctionScope: true);

        var result = TypedAstEvaluator.EvaluateProgram(program, environment);

        Assert.Equal(3d, result);
    }

    private static ProgramNode BuildTypedProgram(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens, source);
        var program = parser.ParseProgram();
        var builder = new SExpressionAstBuilder();
        return builder.BuildProgram(program);
    }
}
