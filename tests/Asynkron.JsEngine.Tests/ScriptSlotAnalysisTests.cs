using Asynkron.JsEngine.JsTypes;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
public sealed class ScriptSlotAnalysisTests : InternalTestBase
{
    public ScriptSlotAnalysisTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task ScriptSlotAnalysisToggle_ProducesSameResult()
    {
        const string script = """
            let a = 1;
            let b = 2;
            a + b;
            """;

        await using var engineNoSlots = CreateEngine(() => new JsEngineOptions
        {
            AllowScriptSlotAnalysis = false
        });
        await using var engineWithSlots = CreateEngine(() => new JsEngineOptions
        {
            AllowScriptSlotAnalysis = true
        });

        var resultWithout = await engineNoSlots.Evaluate(script);
        var resultWith = await engineWithSlots.Evaluate(script);

        Assert.Equal(3d, resultWithout);
        Assert.Equal(3d, resultWith);
    }

    [Fact]
    public async Task ScriptSlotAnalysis_FallsBackOnDirectEval()
    {
        const string script = """
            var x = 1;
            eval("x = 5");
            x;
            """;

        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            AllowScriptSlotAnalysis = true
        });

        var result = await engine.Evaluate(script);
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task ScriptSlotAnalysis_DisabledWhenWithIsPresent()
    {
        const string script = """
            var obj = { value: 7 };
            var result = 0;
            with (obj) {
                result = value;
            }
            result;
            """;

        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            AllowScriptSlotAnalysis = true
        });

        var result = await engine.Evaluate(script);
        Assert.Equal(7d, result);
    }
}
