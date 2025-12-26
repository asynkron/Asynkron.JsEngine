using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class TestSimpleVarBase(ITestOutputHelper output) : FastPathTestBase(output)
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

public class FastPathTestSimpleVar(ITestOutputHelper output) : TestSimpleVarBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class ReferenceTestSimpleVar(ITestOutputHelper output) : TestSimpleVarBase(output)
{
    protected override bool EnableFastPaths => false;
}
