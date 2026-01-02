using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class TestMultipleVarDeclarations(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task MultipleVarDeclarations_ShouldWork()
    {
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function test() {
                var c, bi3b = 5;
                return c;
            }
            test();
        ");
        Assert.Equal(Symbol.Undefined, result);
    }
}
