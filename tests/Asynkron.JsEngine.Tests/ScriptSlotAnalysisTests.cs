using Xunit.Abstractions;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
public sealed class ScriptSlotAnalysisTests : InternalTestBase
{
    public ScriptSlotAnalysisTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task ScriptSlotAnalysis_SimpleScriptUsesIrByDefault()
    {
        const string script = """
            let a = 1;
            let b = 2;
            a + b;
            """;

        await using var engine = CreateEngine();

        var result = await engine.Evaluate(script);

        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task ScriptSlotAnalysis_DirectEvalWithoutCapturedWith_UsesIrPath()
    {
        var logger = new TestLogger(Output, "ScriptDirectEval", minLogLevel: LogLevel.Debug);
        const string script = """
            var x = 1;
            eval("x = 5");
            x;
            """;

        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate(script);
        Assert.Equal(5d, result);
        Assert.DoesNotContain(
            logger.Collector.Snapshot(),
            static record => record.Message.Contains(
                "Executing script via dynamic-scope executor",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScriptSlotAnalysis_WithStatementUsesNoSlotIrPath()
    {
        const string script = """
            var obj = { value: 7 };
            var result = 0;
            with (obj) {
                result = value;
            }
            result;
            """;

        await using var engine = CreateEngine();

        var result = await engine.Evaluate(script);
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task ScriptSlotAnalysis_TopLevelWithWithoutCapturedWith_UsesIrPath()
    {
        var logger = new TestLogger(Output, "ScriptTopLevelWith", minLogLevel: LogLevel.Debug);
        const string script = """
            var scope = { value: 7 };
            var result = 0;
            with (scope) {
                result = value;
            }
            result;
            """;

        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate(script);
        Assert.Equal(7d, result);
        Assert.DoesNotContain(
            logger.Collector.Snapshot(),
            static record => record.Message.Contains(
                "Executing script via dynamic-scope executor",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScriptSlotAnalysis_EvalWithWithoutCapturedWith_UsesIrPath()
    {
        var logger = new TestLogger(Output, "EvalTopLevelWith", minLogLevel: LogLevel.Debug);
        const string script = """
            var result = 0;
            eval("with ({ value: 11 }) { result = value; }");
            result;
            """;

        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate(script);
        Assert.Equal(11d, result);
        Assert.DoesNotContain(
            logger.Collector.Snapshot(),
            static record => record.Message.Contains(
                "Executing script via dynamic-scope executor",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScriptSlotAnalysis_DirectEvalInsideWithWithoutCapturedClosure_UsesIrPath()
    {
        var logger = new TestLogger(Output, "EvalInsideWith", minLogLevel: LogLevel.Debug);
        const string script = """
            const scope = { value: 41 };
            with (scope) {
                eval("value += 1;");
            }
            scope.value;
            """;

        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate(script);
        Assert.Equal(42d, result);
        Assert.DoesNotContain(
            logger.Collector.Snapshot(),
            static record => record.Message.Contains(
                "Executing script via dynamic-scope executor",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScriptSlotAnalysis_DirectEvalVarDeclarationInsideWithWithoutCapturedClosure_UsesIrPath()
    {
        var logger = new TestLogger(Output, "EvalVarInsideWith", minLogLevel: LogLevel.Debug);
        const string script = """
            const scope = { seed: 41 };
            with (scope) {
                eval("var created = seed + 1;");
            }
            created;
            """;

        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate(script);
        Assert.Equal(42d, result);
        Assert.DoesNotContain(
            logger.Collector.Snapshot(),
            static record => record.Message.Contains(
                "Executing script via dynamic-scope executor",
                StringComparison.Ordinal));
    }
}
