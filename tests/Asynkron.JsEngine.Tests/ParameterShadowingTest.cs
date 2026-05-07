using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
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

    [Fact(Timeout = 2000)]
    public async Task VarDeclaration_ShadowsNamedFunctionExpressionName()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function Route(path) {
                this.path = path;
            }

            var original = function route(path) {
                var route = new Route(path);
                return route.path;
            };

            original('/');
            """);

        Assert.Equal("/", result);
    }
}
