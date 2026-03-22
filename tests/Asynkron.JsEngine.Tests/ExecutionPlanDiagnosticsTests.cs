using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
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

    [Fact]
    public async Task DetailedSnapshot_BucketsUnsupportedScriptExpressionPrograms_ByExpressionFailureCode()
    {
        ExecutionPlanDiagnostics.Reset();
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("tag`value`;");
        var result = ExecutionPlanBuildResult.FailureResult(
            ExecutionPlanFailureCode.UnsupportedExpressionProgram,
            "Expression bytecode does not yet support optional tagged templates.",
            ExpressionProgramFailureCode.OptionalTaggedTemplate);

        ExecutionPlanDiagnostics.ReportScriptResult(program, result);

        var snapshot = ExecutionPlanDiagnostics.DetailedSnapshot();
        Assert.Equal(0, snapshot.Functions.Attempts);
        Assert.Equal(1, snapshot.Scripts.Attempts);
        Assert.Equal(0, snapshot.Scripts.Succeeded);
        Assert.Equal(1, snapshot.Scripts.Failed);
        Assert.True(snapshot.FailureCodes.TryGetValue(ExecutionPlanFailureCode.UnsupportedExpressionProgram, out var failureCount));
        Assert.Equal(1, failureCount);
        Assert.True(snapshot.ExpressionFailureCodes.TryGetValue(ExpressionProgramFailureCode.OptionalTaggedTemplate, out var expressionCount));
        Assert.Equal(1, expressionCount);
        Assert.Equal(ExpressionProgramFailureCode.OptionalTaggedTemplate, ExecutionPlanDiagnostics.LastExpressionFailureCode);
    }

    [Fact]
    public async Task ScriptSmokeProbe_CommonStrictScriptPoisoners_DoNotFailPlanBuild()
    {
        await using var engine = CreateEngine();

        var cases = new (string Name, string Source)[]
        {
            ("function expression", "const fn = function(value) { return value + 1; }; fn(41);"),
            ("class expression", "const Box = class { value() { return 42; } }; new Box().value();"),
            ("object method", "const obj = { value() { return 42; } }; obj.value();"),
            ("object accessor", "const obj = { get value() { return 42; }, set value(next) { this._value = next; } }; obj.value;"),
            ("immutable assignment", "const value = 1; value = 2;")
        };

        foreach (var testCase in cases)
        {
            var program = engine.ParseProgram(testCase.Source);
            try
            {
                await engine.Evaluate(program);
            }
            catch
            {
                // The smoke probe cares about plan build, not runtime completion.
            }

            var cache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
            Assert.True(cache.Succeeded, $"{testCase.Name} should build an IR script plan. Failure: {cache.FailureReason}");
        }
    }
}
