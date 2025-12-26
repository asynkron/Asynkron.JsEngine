using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Tests.Helpers;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class LoopScopeAnalysisTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact]
    public void ForLoopWithLetInitializer_TracksPerIterationSlotsAndPlanBindings()
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

        Assert.True(forStatement.PerIterationScopeId >= 0);
        Assert.Equal(2, forStatement.PerIterationSlotCount);
        Assert.Equal([0, 1], forStatement.PerIterationSlotIndices.ToArray());

        var plan = ((IAstCacheable<LoopPlan>)forStatement).GetOrCreateCache();
        Assert.Equal(forStatement.PerIterationScopeId, plan.IterationScopeId);
        Assert.Equal(forStatement.PerIterationSlotCount, plan.IterationSlotCount);
        Assert.Equal(forStatement.PerIterationSlotIndices, plan.PerIterationSlotIndices);
        Assert.Equal(["i", "s"], plan.PerIterationBindings.Select(b => b.Name).ToArray());

        var forStatementAfterCps = AstTestHelpers.FindFirst<ForStatement>(pipeline.AfterCps);
        Assert.Equal(forStatement.PerIterationScopeId, forStatementAfterCps.PerIterationScopeId);
        Assert.Equal(forStatement.PerIterationSlotCount, forStatementAfterCps.PerIterationSlotCount);
        Assert.Equal(forStatement.PerIterationSlotIndices, forStatementAfterCps.PerIterationSlotIndices);
    }

    [Fact]
    public void ForInWithLetBinding_PropagatesPerIterationMetadataToIteratorPlan()
    {
        const string source = """
            let obj = { a: 1, b: 2, c: 3 };
            for (let key in obj) {
                obj[key];
            }
            """;

        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var forEach = AstTestHelpers.FindFirst<ForEachStatement>(pipeline.Analyzed);
        var keyIdentifier = AstTestHelpers.FindFirst<IdentifierExpression>(
            pipeline.Analyzed,
            id => string.Equals(id.Name.Name, "key", StringComparison.Ordinal));

        Assert.Equal(ForEachKind.In, forEach.Kind);
        Assert.Equal(VariableKind.Let, forEach.DeclarationKind);
        Assert.True(forEach.PerIterationScopeId >= 0);
        Assert.Equal(1, forEach.PerIterationSlotCount);
        Assert.Equal([0], forEach.PerIterationSlotIndices.ToArray());
        Assert.Equal(["key"], forEach.PerIterationBindings.Select(b => b.Name).ToArray());
        Assert.Equal(forEach.PerIterationScopeId, keyIdentifier.ScopeId);
        Assert.Equal(0, keyIdentifier.SlotIndex);

        var plan = ((IAstCacheable<IteratorDriverPlan>)forEach).GetOrCreateCache();
        Assert.Equal(forEach.PerIterationScopeId, plan.IterationScopeId);
        Assert.Equal(forEach.PerIterationSlotCount, plan.IterationSlotCount);
        Assert.Equal(forEach.PerIterationSlotIndices, plan.PerIterationSlotIndices);
        Assert.Equal(forEach.PerIterationBindings.Select(b => b.Name), plan.PerIterationBindings.Select(b => b.Name));

        var forEachAfterCps = AstTestHelpers.FindFirst<ForEachStatement>(pipeline.AfterCps);
        Assert.Equal(forEach.PerIterationScopeId, forEachAfterCps.PerIterationScopeId);
        Assert.Equal(forEach.PerIterationSlotCount, forEachAfterCps.PerIterationSlotCount);
        Assert.Equal(forEach.PerIterationSlotIndices, forEachAfterCps.PerIterationSlotIndices);
        Assert.Equal(forEach.PerIterationBindings.Select(b => b.Name), forEachAfterCps.PerIterationBindings.Select(b => b.Name));

        var keyAfterCps = AstTestHelpers.FindFirst<IdentifierExpression>(
            pipeline.AfterCps,
            id => string.Equals(id.Name.Name, "key", StringComparison.Ordinal));
        Assert.Equal(forEach.PerIterationScopeId, keyAfterCps.ScopeId);
        Assert.Equal(0, keyAfterCps.SlotIndex);
    }

    [Fact]
    public void ForOfWithDestructuringBinding_PreservesSlotOrdering()
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
        Assert.True(forEach.PerIterationScopeId >= 0);
        Assert.Equal(2, forEach.PerIterationSlotCount);
        Assert.Equal([0, 1], forEach.PerIterationSlotIndices.ToArray());
        Assert.Equal(["x", "y"], forEach.PerIterationBindings.Select(b => b.Name).ToArray());

        var plan = ((IAstCacheable<IteratorDriverPlan>)forEach).GetOrCreateCache();
        Assert.Equal(forEach.PerIterationScopeId, plan.IterationScopeId);
        Assert.Equal(forEach.PerIterationSlotCount, plan.IterationSlotCount);
        Assert.Equal(forEach.PerIterationSlotIndices, plan.PerIterationSlotIndices);
        Assert.Equal(forEach.PerIterationBindings.Select(b => b.Name), plan.PerIterationBindings.Select(b => b.Name));

        var forEachAfterCps = AstTestHelpers.FindFirst<ForEachStatement>(pipeline.AfterCps);
        Assert.Equal(forEach.PerIterationBindings.Select(b => b.Name), forEachAfterCps.PerIterationBindings.Select(b => b.Name));
        Assert.Equal(forEach.PerIterationSlotIndices, forEachAfterCps.PerIterationSlotIndices);
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

public class FastPathLoopScopeAnalysisTests(ITestOutputHelper output) : LoopScopeAnalysisTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class ReferenceLoopScopeAnalysisTests(ITestOutputHelper output) : LoopScopeAnalysisTestsBase(output)
{
    protected override bool EnableFastPaths => false;
}
