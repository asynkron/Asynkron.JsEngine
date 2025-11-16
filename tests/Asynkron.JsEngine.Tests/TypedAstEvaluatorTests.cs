using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class TypedAstEvaluatorTests
{
    [Theory]
    [InlineData("let sum = 0; let i = 0; while (i < 4) { sum = sum + i; i = i + 1; } sum;", 6d)]
    [InlineData("let obj = { value: 3 }; obj.value = obj.value + 4; obj.value;", 7d)]
    [InlineData("let data = null; data?.value;", null)]
    [InlineData("let total = 0; for (const value of [1, 2, 3]) { total = total + value; } total;", 6d)]
    [InlineData("let captured = 0; try { throw 9; } catch (err) { captured = err; } captured;", 9d)]
    public void TypedEvaluator_matches_legacy_for_core_flows(string source, object? expected)
    {
        var (typed, legacy) = EvaluateBoth(source);
        Assert.Equal(legacy, typed);
        if (expected is null)
        {
            Assert.True(typed is null || ReferenceEquals(typed, JsSymbols.Undefined));
        }
        else
        {
            Assert.Equal(expected, typed);
        }
    }

    private static (object? Typed, object? Legacy) EvaluateBoth(string source)
    {
        var program = JsEngine.ParseWithoutTransformation(source);
        var builder = new SExpressionAstBuilder();
        var typedProgram = builder.BuildProgram(program);

        var typedEnv = new JsEnvironment(isFunctionScope: true);
        var legacyEnv = new JsEnvironment(isFunctionScope: true);

        var typed = TypedAstEvaluator.EvaluateProgram(typedProgram, typedEnv);
        var legacy = JsProgramEvaluator.EvaluateProgram(program, legacyEnv);
        return (typed, legacy);
    }
}
