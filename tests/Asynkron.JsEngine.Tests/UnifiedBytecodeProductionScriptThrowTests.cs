using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Regression tests guarding that unhandled top-level throws still propagate when an admitted
/// script runs through the production unified-bytecode VM. The VM records an unhandled throw in
/// the evaluation context and returns <c>undefined</c> as the completion value; the script
/// admission wrapper must re-raise it so behaviour matches the IR script path. See the script
/// fast-path in <c>TryRunScriptViaProductionUnifiedBytecode</c>.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeProductionScriptThrowTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ProductionScriptFastPathLog = "unified-bytecode-production-fast-path script";

    [Fact(Timeout = 5000)]
    public async Task ConstReassignment_TopLevelScript_ThrowsTypeErrorOnProductionFastPath()
    {
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            const value = 1;
            value = 2;
            """);

        var error = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate(program));
        Assert.Contains("constant variable", error.Message, StringComparison.Ordinal);
        AssertRanOnProductionScriptFastPath();
    }

    [Fact(Timeout = 5000)]
    public async Task ThrowStatement_TopLevelScript_PropagatesOnProductionFastPath()
    {
        await using var engine = CreateEngine();

        var error = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("throw 42;"));
        Assert.Contains("42", error.Message, StringComparison.Ordinal);
        AssertRanOnProductionScriptFastPath();
    }

    [Fact(Timeout = 5000)]
    public async Task NullPropertyAccess_TopLevelScript_ThrowsTypeErrorOnProductionFastPath()
    {
        await using var engine = CreateEngine();

        var error = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("null.x;"));
        Assert.Contains("null or undefined", error.Message, StringComparison.Ordinal);
        AssertRanOnProductionScriptFastPath();
    }

    private void AssertRanOnProductionScriptFastPath()
    {
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                ProductionScriptFastPathLog,
                StringComparison.Ordinal));
    }
}
