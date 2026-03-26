using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class ForInStrictVarTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task ForInVar_StrictMode_KeyShouldNotBeUndefined()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("'use strict'; var o={a:1}; for(var key in o){} key;");
        Output.WriteLine($"Strict result: {result}");
        Assert.Equal("a", result);
    }

    [Fact]
    public async Task ForInVar_NonStrictMode_KeyShouldNotBeUndefined()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("var o={a:1}; for(var key in o){} key;");
        Output.WriteLine($"Non-strict result: {result}");
        Assert.Equal("a", result);
    }
}
