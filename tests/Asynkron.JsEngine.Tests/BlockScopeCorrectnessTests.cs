using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
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

    [Fact]
    public async Task BlockFunctionDeclarationConflictingWithVarDeclarationThrowsParseException()
    {
        await using var engine = CreateEngine();

        await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate("""
                function x() {
                    {
                        function* f() {}
                        var f;
                    }
                }
                """);
        });
    }

    [Fact]
    public void BlockDeclarationConflictCheckDoesNotEnterNestedFunctionBodies()
    {
        using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function x() {
                {
                    let f;
                    function nested() {
                        var f;
                    }
                }
            }
            """);

        Assert.NotNull(program);
    }

    [Fact]
    public void FunctionBodyFunctionDeclarationMayShareVarName()
    {
        using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function x() {
                function f() {}
                var f;
            }
            """);

        Assert.NotNull(program);
    }

    [Fact]
    public void ClassStaticBlockFunctionDeclarationMayShareVarName()
    {
        using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            class C {
                static {
                    function f() {}
                    var f;
                }
            }
            """);

        Assert.NotNull(program);
    }

    [Fact]
    public void ClassStaticBlockLexicalDeclarationConflictingWithVarDeclarationThrowsParseException()
    {
        using var engine = CreateEngine();

        Assert.Throws<ParseException>(() => engine.ParseProgram("""
            class C {
                static {
                    let f;
                    var f;
                }
            }
            """));
    }
}
