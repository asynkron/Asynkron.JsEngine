using System.Collections.Generic;
using System.Collections.Immutable;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.AstTransformers;

namespace Asynkron.JsEngine.Tests;

public class TypedProgramExecutorTests
{
    [Fact]
    public void Evaluate_UsesTypedEvaluator_WhenAnalyzerSupportsProgram()
    {
        var parsed = BuildParsedProgram("let value = 1 + 2; value;");
        var executor = new TypedProgramExecutor();
        var fallbackReasons = new List<string>();
        executor.UnsupportedCallback = fallbackReasons.Add;

        var environment = new JsEnvironment(isFunctionScope: true);
        var result = executor.Evaluate(parsed, environment);

        Assert.Equal(3d, result);
        Assert.Empty(fallbackReasons);
    }

    [Fact]
    public void Evaluate_FallsBack_WhenAnalyzerRejectsProgram()
    {
        var parsed = BuildParsedProgram("let input = 41; input + 1;");
        var unsupportedBody = ImmutableArray.Create<StatementNode>(
            new UnknownStatement(parsed.Typed.Source, parsed.SExpression));
        var unsupportedProgram = parsed.Typed with { Body = unsupportedBody };
        var mutated = parsed with { Typed = unsupportedProgram };

        var executor = new TypedProgramExecutor();
        var fallbackReasons = new List<string>();
        executor.UnsupportedCallback = fallbackReasons.Add;

        var environment = new JsEnvironment(isFunctionScope: true);
        var result = executor.Evaluate(mutated, environment);

        Assert.Equal(42d, result);
        Assert.Single(fallbackReasons);
        Assert.Contains("Typed evaluator does not yet understand", fallbackReasons[0]);
    }

    private static ParsedProgram BuildParsedProgram(string source)
    {
        var parsed = JsEngine.ParseWithoutTransformation(source);
        var constantTransformer = new ConstantExpressionTransformer();
        var cpsTransformer = new CpsTransformer();

        var transformed = constantTransformer.Transform(parsed);
        if (CpsTransformer.NeedsTransformation(transformed))
        {
            transformed = cpsTransformer.Transform(transformed);
        }

        var builder = new SExpressionAstBuilder();
        var typed = builder.BuildProgram(transformed);
        return new ParsedProgram(transformed, typed);
    }
}
