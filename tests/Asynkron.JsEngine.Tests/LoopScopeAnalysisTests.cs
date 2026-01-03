using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
public sealed class LoopScopeAnalysisTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public void ForLoopWithLetInitializer_TracksPerIterationBindingsInPlan()
    {
        const string source = """
            let total = 0;
            for (let i = 0, s = 0; i < 3; i++) {
                total += i;
            s = s + i;
        }
        total;
        """;

        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var forStatement = AstTestHelpers.FindFirst<ForStatement>(pipeline.Analyzed);

        // LoopNormalizer collects per-iteration bindings independently of ScopeAnalyzer
        var plan = ((IAstCacheable<LoopPlan>)forStatement).GetOrCreateCache();
        Assert.Equal(["i", "s"], plan.PerIterationBindings.Select(b => b.Name).ToArray());
    }

    [Fact]
    public void ForInWithLetBinding_PropagatesPerIterationBindingsToIteratorPlan()
    {
        const string source = """
            let obj = { a: 1, b: 2, c: 3 };
            for (let key in obj) {
                obj[key];
            }
            """;

        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var forEach = AstTestHelpers.FindFirst<ForEachStatement>(pipeline.Analyzed);

        Assert.Equal(ForEachKind.In, forEach.Kind);
        Assert.Equal(VariableKind.Let, forEach.DeclarationKind);

        // IteratorDriverFactory collects per-iteration bindings independently of ScopeAnalyzer
        var plan = ((IAstCacheable<IteratorDriverPlan>)forEach).GetOrCreateCache();
        Assert.Equal(["key"], plan.PerIterationBindings.Select(b => b.Name).ToArray());
    }

    [Fact]
    public void ForOfWithDestructuringBinding_PreservesBindingNamesInPlan()
    {
        const string source = """
            const pairs = [[1, 2], [3, 4]];
            for (let [x, y] of pairs) {
                x + y;
            }
            """;

        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var forEach = AstTestHelpers.FindFirst<ForEachStatement>(pipeline.Analyzed);

        Assert.Equal(ForEachKind.Of, forEach.Kind);

        // IteratorDriverFactory collects per-iteration bindings including destructured names
        var plan = ((IAstCacheable<IteratorDriverPlan>)forEach).GetOrCreateCache();
        Assert.Equal(["x", "y"], plan.PerIterationBindings.Select(b => b.Name).ToArray());
    }

    [Fact]
    public async Task ForInLetBinding_BindsActualKeyValueAtRuntime()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let obj = { a: 1, b: 2 };
            let seen = '';
            for (let key in obj) {
                seen = key + ':' + (typeof key);
                break;
            }
            seen;
            """);

        Assert.Equal("a:string", result);
    }

    [Fact]
    public async Task ForInLetBinding_AccumulatesKeys()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let obj = { a: 1, b: 2, c: 3 };
            let keys = '';
            for (let key in obj) {
                keys = keys + key;
            }
            keys;
            """);

        Assert.Equal("abc", result);
    }
}
