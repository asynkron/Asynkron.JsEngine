using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Tests;

public class TestVariableDeclarations
{
    [Fact]
    public async Task VarWithoutInitializer_ShouldBeUndefined()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var x;
            x;
        ");
        Assert.Equal(JsSymbols.Undefined, result);
    }

    [Fact]
    public async Task VarWithInitializer_ShouldWork()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var y = 42;
            y;
        ");
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task ConstWithoutInitializer_ShouldThrowParseException()
    {
        await using var engine = new JsEngine();
        await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate(@"
                const z;
                z;
            ");
        });
    }

    [Fact]
    public async Task ConstWithInitializer_ShouldWork()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const w = 100;
            w;
        ");
        Assert.Equal(100.0, result);
    }

    [Fact]
    public async Task MultipleVarDeclarationsWithMixedInitializers_ShouldWork()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var a, b = 5, c;
            a === undefined && b === 5 && c === undefined;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task MixedLetVarDeclarations_ShouldWork()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let x;
            var y;
            x === undefined && y === undefined;
        ");
        Assert.Equal(true, result);
    }
}
