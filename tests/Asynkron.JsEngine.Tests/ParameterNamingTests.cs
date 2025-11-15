using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for parameter naming including contextual keywords
/// </summary>
public class ParameterNamingTests
{
    [Fact]
    public async Task FunctionParameter_Named_Set_ShouldWork()
    {
        var engine = new JsEngine();
        var code = @"
            function isInAstralSet(code, set) {
                return set[0];
            }
            isInAstralSet(100, [42]);
        ";
        
        var result = await engine.Evaluate(code);
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task FunctionParameter_Named_Get_ShouldWork()
    {
        var engine = new JsEngine();
        var code = @"
            function test(get, value) {
                return get + value;
            }
            test(10, 20);
        ";
        
        var result = await engine.Evaluate(code);
        Assert.Equal(30.0, result);
    }

    [Fact]
    public async Task ArrowFunction_Parameter_Named_Set_ShouldWork()
    {
        var engine = new JsEngine();
        var code = @"
            const fn = (code, set) => set[0];
            fn(100, [42]);
        ";
        
        var result = await engine.Evaluate(code);
        Assert.Equal(42.0, result);
    }
}
