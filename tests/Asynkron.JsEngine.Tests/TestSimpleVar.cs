using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Tests;

public class TestSimpleVar
{
    [Fact]
    public async Task SimpleVarWithInitializer_ShouldWork()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var x = 5;
            x;
        ");
        Assert.Equal(5.0, result);
    }

    [Fact]
    public async Task SimpleVarWithoutInitializer_ShouldBeUndefined()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var y;
            y;
        ");
        Assert.Equal(Symbol.Undefined, result);
    }
}
