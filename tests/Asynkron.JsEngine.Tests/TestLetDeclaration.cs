using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class TestLetDeclaration(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task LetWithoutInitializer_ShouldBeUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let x;
            x;
        ");
        Assert.Equal(Symbol.Undefined, result);
    }

    [Fact]
    public async Task LetWithInitializer_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let y = 42;
            y;
        ");
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task MultipleLetDeclarationsWithMixedInitializers_ShouldWork()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let a, b = 5, c;
            a === undefined && b === 5 && c === undefined;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task LetWithoutInitializer_CanBeAssignedLater()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let x;
            x = 10;
            x;
        ");
        Assert.Equal(10.0, result);
    }
}
