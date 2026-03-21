using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.Debugging)]
public sealed class ExecutionPlanDiagnosticsTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public void FunctionPlanCache_Reads_DoNotInflateBuildCounters()
    {
        ExecutionPlanDiagnostics.Reset();

        var pipeline = AstTestHelpers.ParseAndAnalyze("""
            function add(a, b) {
                return a + b;
            }
            """);
        var add = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body[0]).Function;

        var first = ((IAstCacheable<ExecutionPlanCache>)add).GetOrCreateCache();
        var second = ((IAstCacheable<ExecutionPlanCache>)add).GetOrCreateCache();

        Assert.True(first.Succeeded, first.FailureReason);
        Assert.Same(first.Plan, second.Plan);

        var snapshot = ExecutionPlanDiagnostics.DetailedSnapshot();
        Assert.Equal(1, snapshot.Functions.Attempts);
        Assert.Equal(1, snapshot.Functions.Succeeded);
        Assert.Equal(0, snapshot.Functions.Failed);
        Assert.Equal(1, snapshot.FunctionCacheHits);
    }

    [Fact]
    public async Task DetailedSnapshot_TracksScriptBuilds_Separately()
    {
        ExecutionPlanDiagnostics.Reset();
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let a = 1;
            let b = 2;
            a + b;
            """);

        Assert.Equal(3d, result);

        var snapshot = ExecutionPlanDiagnostics.DetailedSnapshot();
        Assert.Equal(0, snapshot.Functions.Attempts);
        Assert.Equal(1, snapshot.Scripts.Attempts);
        Assert.Equal(1, snapshot.Scripts.Succeeded);
        Assert.Equal(0, snapshot.Scripts.Failed);
        Assert.Empty(snapshot.FailureCodes);
    }

    [Fact]
    public async Task DetailedSnapshot_BucketsUnsupportedBuilds_ByFailureCode()
    {
        ExecutionPlanDiagnostics.Reset();
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function broken() {
                break;
            }
            """);
        var broken = Assert.IsType<FunctionDeclaration>(program.Body[0]).Function;

        var result = ExecutionPlanBuilder.Build(broken);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);

        var snapshot = ExecutionPlanDiagnostics.DetailedSnapshot();
        Assert.Equal(1, snapshot.Functions.Attempts);
        Assert.Equal(0, snapshot.Scripts.Attempts);
        Assert.Equal(0, snapshot.Scripts.Succeeded);
        Assert.Equal(0, snapshot.Scripts.Failed);
        Assert.Equal(0, snapshot.Functions.Succeeded);
        Assert.Equal(1, snapshot.Functions.Failed);
        Assert.True(snapshot.FailureCodes.TryGetValue(result.Failure!.Code, out var count));
        Assert.Equal(1, count);
        Assert.Equal(result.Failure.Code, ExecutionPlanDiagnostics.LastFailureCode);
        Assert.Equal("broken", ExecutionPlanDiagnostics.LastFunctionDescription);
    }
}
