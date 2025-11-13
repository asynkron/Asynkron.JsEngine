using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class ParameterShadowingTest
{
    [Fact]
    public async Task Parameter_ShadowsFunctionName()
    {
        var engine = new JsEngine();
        
        // Parameter 'foo' should shadow function name 'foo'
        var result = await engine.Evaluate(@"
            function foo(foo) {
                return foo * 2;
            }
            foo(5)
        ");
        Assert.Equal(10.0, result);
    }
    
    [Fact]
    public async Task Parameter_ShadowsGlobalVariable()
    {
        var engine = new JsEngine();
        
        var result = await engine.Evaluate(@"
            var x = 100;
            function test(x) {
                return x * 2;
            }
            test(5)
        ");
        Assert.Equal(10.0, result);
    }
}
