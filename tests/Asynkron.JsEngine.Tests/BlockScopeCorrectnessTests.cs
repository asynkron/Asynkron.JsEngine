using Asynkron.JsEngine.JsTypes;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class BlockScopeCorrectnessTests : InternalTestBase
{
    public BlockScopeCorrectnessTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task LetShadowingAcrossBlocksResolvesCorrectly()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let results = [];
            function run() {
                let x = 1;
                {
                    let x = 2;
                    results.push(x);
                }
                results.push(x);
            }
            run();
            results.join(",");
            """);

        Assert.Equal("2,1", result);
    }

    [Fact]
    public async Task InnerFunctionCapturesBlockScopedLet()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function make() {
                const funcs = [];
                for (let i = 0; i < 2; i++) {
                    {
                        let x = i + 10;
                        funcs.push(() => x);
                    }
                }
                return funcs.map(f => f());
            }
            make();
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(10.0, array.GetElement(0).AsDouble());
        Assert.Equal(11.0, array.GetElement(1).AsDouble());
    }

    [Fact]
    public async Task NestedBlocksWithSameIdentifierResolveCorrectly()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function test() {
                let res = [];
                {
                    let a = 1;
                    {
                        let a = 2;
                        res.push(a);
                    }
                    res.push(a);
                }
                return res.join(",");
            }
            test();
            """);

        Assert.Equal("2,1", result);
    }
}
