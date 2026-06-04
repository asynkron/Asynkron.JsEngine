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

    // A46 — the FULL pure-BigInt binary operator surface routes through the sync production VM.
    // Probe finding (sync-cluster burn-down): every pure-BigInt arithmetic/bitwise/shift op routes;
    // the earlier "BigInt declines" reading was an artifact of wrapping the result in a `.toString()`
    // method call (the method call itself declines, not the BigInt op). Each body is kept pure-BigInt
    // (`(expr) === <bigint literal>`) so no method call masks the routing signal.
    [Theory]
    [InlineData("2n+3n", "5n")]
    [InlineData("7n-2n", "5n")]
    [InlineData("5n*4n", "20n")]
    [InlineData("8n/2n", "4n")]
    [InlineData("10n%3n", "1n")]
    [InlineData("2n**10n", "1024n")]
    [InlineData("2n<<3n", "16n")]
    [InlineData("16n>>2n", "4n")]
    [InlineData("6n&3n", "2n")]
    [InlineData("6n|1n", "7n")]
    [InlineData("6n^3n", "5n")]
    public async Task A46_PureBigIntBinaryOperators_Route(string expr, string expectedEq)
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate($"function f(){{ return ({expr}) === {expectedEq}; }} f();");
        Assert.Equal(true, r);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A46 — pure-BigInt relational and equality comparisons route and compute correctly.
    [Theory]
    [InlineData("2n<3n", true)]
    [InlineData("3n>2n", true)]
    [InlineData("2n<=2n", true)]
    [InlineData("2n>=3n", false)]
    [InlineData("2n===2n", true)]
    [InlineData("2n!==3n", true)]
    public async Task A46_PureBigIntComparisons_Route(string expr, bool expected)
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate($"function f(){{ return {expr}; }} f();");
        Assert.Equal(expected, r);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A46 — BigInt-MIXED arithmetic (`1n + 1`) is admitted to route but the VM throws the correct
    // TypeError at runtime (mixing BigInt with a Number is a spec error), matching the interpreter.
    [Fact]
    public async Task A46_MixedBigIntNumber_RoutesAndThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            async () => await engine.Evaluate("function f(){ return 1n + 1; } f();"));
        Assert.Contains("Cannot mix BigInt", ex.Message, StringComparison.Ordinal);
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

    // A49 — the "Activation slot metadata is required." decline arm is an unreachable defensive
    // backstop: ExecutionPlanBuilder ALWAYS populates a non-null ActivationSlotShape for every valid
    // compiled function/script plan (BuildActivationSlotShape is unconditional; the nested-function
    // restamp carries it forward). These minimal-but-valid shapes all route, standing proof that the
    // arm fires only for a genuinely-degenerate (never-produced) plan, not for any real input.
    [Theory]
    [InlineData("function f(){ return 1; } f();")]
    [InlineData("function f(a,b){ return a+b; } f(1,2);")]
    [InlineData("function f(){ var x=1; return x; } f();")]
    [InlineData("function f(){ for(;;){ break; } return 0; } f();")]
    [InlineData("function f(){ try { return 1; } finally {} } f();")]
    public async Task A49_MinimalValidShapes_HaveActivationSlotsAndRoute(string source)
    {
        await using var engine = CreateEngine();
        await engine.Evaluate(source);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }
}
