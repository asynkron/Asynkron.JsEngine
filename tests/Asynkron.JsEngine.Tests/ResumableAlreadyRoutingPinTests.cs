using System; using Xunit; using Xunit.Abstractions;
namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Phase B triage pins (audit a5b0c09). Resumable shapes the production resumable VM ALREADY routes +
///     computes correctly on main, whose ledger items were open/untested. Locks routing + result so a future
///     change can't silently regress them. Separate file from the ratchet to avoid the merge-conflict hotspot.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class ResumableAlreadyRoutingPinTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private void AssertRouted(string log) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(), r => r.Message.Contains(log, StringComparison.Ordinal));

    // B2: yield* over a LOCAL/PARAM iterable routes through the resumable generator VM (free iterable == B38, still open).
    [Fact]
    public async Task B2_YieldStarLocalIterable_Routes()
    {
        await using var e = CreateEngine();
        var r = await e.Evaluate("function* g(){ yield* [1,2,3]; } var it=g(); var s=''+it.next().value+it.next().value+it.next().value; s;");
        Assert.Equal("123", r);
        AssertRouted("unified-bytecode-resumable-generator-fast-path func=g");
    }

    [Fact]
    public async Task B2_YieldStarParamIterable_Routes()
    {
        await using var e = CreateEngine();
        var r = await e.Evaluate("function* g(arr){ yield* arr; } var it=g([4,5]); ''+it.next().value+it.next().value;");
        Assert.Equal("45", r);
        AssertRouted("unified-bytecode-resumable-generator-fast-path func=g");
    }

    // B32: try/FINALLY with a yield inside the try routes (the try/CATCH-across-suspension path is still open).
    [Fact]
    public async Task B32_TryFinallyWithYield_Routes()
    {
        await using var e = CreateEngine();
        var r = await e.Evaluate("function* g(){ try { yield 1; yield 2; } finally { } } var it=g(); ''+it.next().value+it.next().value;");
        Assert.Equal("12", r);
        AssertRouted("unified-bytecode-resumable-generator-fast-path func=g");
    }

    [Fact]
    public async Task B32_TryFinallyNonEmptyRunsFinally_Routes()
    {
        await using var e = CreateEngine();
        var r = await e.Evaluate("function* g(o){ try { yield 1; } finally { o.done = true; } } var o={done:false}; var it=g(o); it.next(); it.next(); o.done;");
        Assert.Equal(true, r);
        AssertRouted("unified-bytecode-resumable-generator-fast-path func=g");
    }
}
