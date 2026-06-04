using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Burn-down triage pins (audit a0a7078). Shapes that the production sync VM ALREADY routes on main
///     but whose ledger items were still open/untested. These lock the behavior with a route-hit + result
///     assertion so a future change cannot silently regress them back to an interpreter fallback. Each is a
///     non-dynamic shape — no source change was needed to admit it; the value here is the standing proof.
///     (Complements <see cref="ProductionRouteCoverageRatchetTests"/>; kept in a separate file to avoid the
///     ratchet's merge-conflict hotspot during the parallel burn-down.)
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class AlreadyRoutingShapePinTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private void AssertRouted(string expectedLog) =>
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(expectedLog, StringComparison.Ordinal));

    // A31 — optional short-circuit chain `o?.a?.b` (multi-hop) routes through the sync VM.
    [Fact]
    public async Task A31_OptionalChainMultiHop_Routes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("function f(o){ return o?.a?.b; } f({a:{b:9}});");
        Assert.Equal(9d, r);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task A31_OptionalChainShortCircuit_Routes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("function f(o){ return o?.a?.b; } f(null);");
        Assert.True(r is null || r.Equals(default(object)) || r.ToString() == "undefined" || r.ToString() == "");
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A35 — object literal with computed key, shorthand method, and getter all route.
    [Fact]
    public async Task A35_ComputedKeyObjectLiteral_Routes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("function f(k){ var o={[k]:7}; return o.a; } f('a');");
        Assert.Equal(7d, r);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task A35_MethodObjectLiteral_Routes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("function f(){ var o={ m(){ return 7; } }; return o.m(); } f();");
        Assert.Equal(7d, r);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task A35_GetterObjectLiteral_Routes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("function f(){ var o={ get a(){ return 5; } }; return o.a; } f();");
        Assert.Equal(5d, r);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A46 — pure BigInt exponentiation `2n**10n` routes and computes correctly.
    [Fact]
    public async Task A46_PureBigIntExponentiation_Routes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("function f(){ return (2n**10n) === 1024n; } f();");
        Assert.Equal(true, r);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A49 — trivial plans (top-level script expression, empty function body) route.
    [Fact]
    public async Task A49_TopLevelScriptExpression_RoutesScript()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("1 + 1;");
        Assert.Equal(2d, r);
        AssertRouted("unified-bytecode-production-fast-path script");
    }

    [Fact]
    public async Task A49_EmptyFunctionBody_Routes()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("function f(){} f();");
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }
}
