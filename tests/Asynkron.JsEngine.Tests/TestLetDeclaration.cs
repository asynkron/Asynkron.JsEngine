using Xunit;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class TestLetDeclaration
{
    [Fact]
    public async Task LetWithoutInitializer_ShouldBeUndefined()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let x;
            x;
        ");
        Assert.Equal(JsSymbols.Undefined, result);
    }

    [Fact]
    public async Task LetWithInitializer_ShouldWork()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let y = 42;
            y;
        ");
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task MultipleLetDeclarationsWithMixedInitializers_ShouldWork()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let a, b = 5, c;
            a === undefined && b === 5 && c === undefined;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task LetWithoutInitializer_CanBeAssignedLater()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let x;
            x = 10;
            x;
        ");
        Assert.Equal(10.0, result);
    }
}
