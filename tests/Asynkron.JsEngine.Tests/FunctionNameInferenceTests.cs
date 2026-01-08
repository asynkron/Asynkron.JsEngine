using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for ES6 function name inference (ES spec 13.3.1.4).
/// When an anonymous function is assigned to a variable, the function's
/// name property should be set to the variable name.
/// </summary>
[Category(TestCategories.StdLibFunction)]
public sealed class FunctionNameInferenceTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task ArrowFunction_NameInference()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const arrow = () => {}; arrow.name");
        Assert.Equal("arrow", result?.ToString());
    }

    [Fact]
    public async Task FunctionExpression_NameInference()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const fn = function() {}; fn.name");
        Output.WriteLine($"fn.name = '{result}'");
        Assert.Equal("fn", result?.ToString());
    }

    [Fact]
    public async Task ClassExpression_NameInference()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const cls = class {}; cls.name");
        Output.WriteLine($"cls.name = '{result}'");
        Assert.Equal("cls", result?.ToString());
    }

    [Fact]
    public async Task GeneratorFunction_NameInference()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const gen = function*() {}; gen.name");
        Output.WriteLine($"gen.name = '{result}'");
        Assert.Equal("gen", result?.ToString());
    }
}
