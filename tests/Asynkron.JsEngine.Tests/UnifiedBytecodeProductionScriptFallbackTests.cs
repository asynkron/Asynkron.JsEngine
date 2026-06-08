using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeProductionScriptFallbackTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ClassifiedScriptIrFallbackLog =
        "classified-script-ir-fallback reason=production-unified-bytecode-declined";
    private const string ProductionScriptFastPathLog = "unified-bytecode-production-fast-path script";

    [Fact(Timeout = 5000)]
    public async Task DirectEvalInjectedBinding_TopLevelScript_UsesClassifiedIrFallbackWithDeclineCode()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            eval("var injected = 1");
            injected;
            """);

        Assert.Equal(1d, result);
        var snapshot = CurrentLogger!.Collector.Snapshot();
        Assert.DoesNotContain(snapshot,
            static record => record.Message.Contains(
                ProductionScriptFastPathLog,
                StringComparison.Ordinal));
        Assert.Contains(snapshot,
            static record =>
                record.Message.Contains(ClassifiedScriptIrFallbackLog, StringComparison.Ordinal) &&
                record.Message.Contains("code=CallDependency", StringComparison.Ordinal) &&
                record.Message.Contains("terminalDynamicResidue=True", StringComparison.Ordinal) &&
                record.Message.Contains("eval", StringComparison.OrdinalIgnoreCase));
    }
}
