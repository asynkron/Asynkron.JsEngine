using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class ParameterShadowingTest(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task Parameter_ShadowsFunctionName()
    {
        await using var engine = CreateEngine();

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
        await using var engine = CreateEngine();

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
