using Xunit;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class TestMultipleVarDeclarations
{
    [Fact]
    public async Task MultipleVarDeclarations_ShouldWork()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test() {
                var c, bi3b = 5;
                return bi3b;
            }
            test();
        ");
        Assert.Equal(5.0, result);
    }
    
    [Fact]
    public async Task MultipleVarDeclarationsUninitializedVariable_ShouldBeUndefined()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test() {
                var c, bi3b = 5;
                return c;
            }
            test();
        ");
        Assert.Equal(JsSymbols.Undefined, result);
    }
}
