using System.Text.RegularExpressions;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
public class ScopeIdentityTests
{
    [Fact]
    public void RootScopeIds_AreUniquePerFunctionPlan()
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze("""
            function outer() {
                function inner() { return 1; }
                return inner;
            }
            function other() { return 2; }
            """);

        var functions = AstTestHelpers.Walk(pipeline.Analyzed, includeSelf: true)
            .OfType<FunctionDeclaration>()
            .ToArray();

        var rootScopeIds = functions
            .Select(fd => ((IAstCacheable<ExecutionPlanCache>)fd.Function).GetOrCreateCache().Plan?.RootScopeId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToArray();

        Assert.True(rootScopeIds.Length >= 2);
        Assert.Equal(rootScopeIds.Length, rootScopeIds.Distinct().Count());
        Assert.All(rootScopeIds, id => Assert.True(id > 0));
    }

    [Fact]
    public async Task ExecutionPlanRunner_UsesConsistentRootScopeId()
    {
        const string script = """
            async function run() {
                let x = 1;
                return x;
            }
            run();
            """;

        var logger = new TestLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate(script);

        var scopeIds = logger.Collector.Snapshot()
            .Select(r => ExtractScopeId(r.Message))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        Assert.Single(scopeIds);
        Assert.True(scopeIds[0] > 0);
    }

    private static int? ExtractScopeId(string message)
    {
        var match = Regex.Match(message, @"scopeId=(?<id>-?\d+)");
        return match.Success && int.TryParse(match.Groups["id"].Value, out var value) ? value : null;
    }
}
