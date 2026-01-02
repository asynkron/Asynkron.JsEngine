using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class TestSimpleVar(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task SimpleVarWithInitializer_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var x = 5;
            x;
        ");
        Assert.Equal(5.0, result);
    }

    [Fact]
    public async Task SimpleVarWithoutInitializer_ShouldBeUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var y;
            y;
        ");
        Assert.Equal(Symbol.Undefined, result);
    }
}
