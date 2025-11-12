using Xunit;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class TestSimpleVar
{
    [Fact]
    public async Task SimpleVarWithInitializer_ShouldWork()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var x = 5;
            x;
        ");
        Assert.Equal(5.0, result);
    }
    
    [Fact]
    public async Task SimpleVarWithoutInitializer_ShouldBeUndefined()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var y;
            y;
        ");
        Assert.Equal(JsSymbols.Undefined, result);
    }
}
