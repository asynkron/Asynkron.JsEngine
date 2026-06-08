using System.Text.RegularExpressions;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
public sealed partial class ScopeIdentityTests
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
    public async Task DeclinedAsyncFunction_DoesNotUseExecutionPlanRunnerRootScopeId()
    {
        const string script = """
            var observed = "";
            async function run() {
                let x = 1;
                arguments.length;
                return x;
            }
            run().then(
                value => observed = "fulfilled:" + value,
                error => observed = String(error));
            observed;
            """;

        var logger = new TestLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.EvaluateAndAwait(script);
        var message = Assert.IsType<string>(result);
        Assert.Contains(
            "Async-function body 'run' is not eligible for unified bytecode execution:",
            message,
            StringComparison.Ordinal);

        var scopeIds = logger.Collector.Snapshot()
            .Select(r => ExtractScopeId(r.Message))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        Assert.Empty(scopeIds);
    }

    private static int? ExtractScopeId(string message)
    {
        var match = MyRegex().Match(message);
        return match.Success && int.TryParse(match.Groups["id"].Value, out var value) ? value : null;
    }

    [GeneratedRegex(@"scopeId=(?<id>-?\d+)")]
    private static partial Regex MyRegex();
}
