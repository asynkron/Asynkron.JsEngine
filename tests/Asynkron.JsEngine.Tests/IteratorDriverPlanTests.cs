using System.Linq;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.IteratorRuntime)]
[Category(TestCategories.ScopeAnalysis)]
public sealed class IteratorDriverPlanTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public void ForOfEmitterUsesCachedIteratorPlan()
    {
        const string source = """
            function run(items) {
                let total = 0;
                for (let value of items) {
                    total += value;
                }
                return total;
            }
            """;

        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var forEach = AstTestHelpers.FindFirst<ForEachStatement>(pipeline.Analyzed);

        // Warm the cache to emulate earlier lookup sites.
        var cachedPlan = ((IAstCacheable<IteratorDriverPlan>)forEach).GetOrCreateCache();
        Assert.False(cachedPlan.PerIterationBindings.IsDefaultOrEmpty);
        Assert.NotEqual(-1, cachedPlan.IterationScopeId);

        var runDeclaration = AstTestHelpers.FindFirst<FunctionDeclaration>(pipeline.Analyzed);
        var planCache = ExecutionPlanCache.Build(runDeclaration.Function, reportDiagnostics: false);
        Assert.True(planCache.Succeeded);

        var finalPlan = ((IAstCacheable<IteratorDriverPlan>)forEach).GetOrCreateCache();

        var pushEnv = planCache.Plan!.Instructions
            .OfType<PushEnvironmentInstruction>()
            .Single(instr => !instr.PerIterationBindings.IsDefaultOrEmpty);

        Assert.Equal(finalPlan.IterationScopeId, pushEnv.ScopeId);
        Assert.Equal(finalPlan.IterationSlotCount, pushEnv.SlotCount);
        Assert.Same(finalPlan, ((IAstCacheable<IteratorDriverPlan>)forEach).GetOrCreateCache());
    }

    [Fact]
    public async Task ForOfPlanReusedAcrossRuns()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            const items = [[1, 2], [3, 4]];
            function run() {
                let sums = [];
                for (let [a, b] of items) {
                    sums.push(a + b);
                }
                return sums.join(",");
            }
            [run(), run()];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("3,7", array.GetElement(0).AsString());
        Assert.Equal("3,7", array.GetElement(1).AsString());
    }
}
