using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     A44 (burn-down) — captured per-iteration <c>let</c>/<c>const</c> loop bindings route through the
///     production unified-bytecode VM while preserving fresh per-iteration closure semantics.
///
///     Evidence:
///       • The non-captured cases (per-iteration sum, for-of over flat slots) already route through
///         production today — they live entirely in the flat-slot model.
///       • Captured per-iteration <c>let</c>/<c>const</c> shapes need both a flat mapping for the
///         PushEnvironment scope and a CreatePerIterationEnvironment-style copy before the VM rebinds the
///         fresh scope environment.
///
///     These tests assert both the runtime values and the production route so a future PushEnvironment
///     regression cannot silently fall back to the IR runner.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class A44PerIterationLetDeclineTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private bool RoutedFunction(string func)
    {
        foreach (var rec in CurrentLogger!.Collector.Snapshot())
        {
            if (rec.Message.Contains(
                    $"unified-bytecode-production-fast-path func={func}",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // --- Admitted captured shapes: correct per-iteration capture semantics and routed production. ---

    [Fact]
    public async Task PerIterLet_CapturedClosure_RoutesThroughProduction()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate(
            "function f(){ var g; for (let i=0;i<3;i++){ if(i===1){ g=()=>i; } } return g; } f()();");
        Assert.Equal(1d, r);
        Assert.True(RoutedFunction("f"), "captured per-iteration let must route through production VM");
    }

    [Fact]
    public async Task PerIterLet_CapturedViaDynamicPath_RoutesThroughProduction()
    {
        // f is itself a captured closure (captures enclosing 'tag') => it would otherwise take the
        // dynamic-identifier production path. The per-iteration let captured by an inner arrow must route,
        // and the captured 'i' must read 1 (per-iteration), not throw a TDZ error.
        await using var engine = CreateEngine();
        var r = await engine.Evaluate(
            "function mk(tag){ return function f(){ var g; for (let i=0;i<3;i++){ if(i===tag){ g=()=>i; } } return g; }; } mk(1)()();");
        Assert.Equal(1d, r);
        Assert.True(RoutedFunction("f"), "captured per-iteration let on the dynamic path must route");
    }

    [Fact]
    public async Task PerIterConst_OfMultiCaptured_RoutesThroughProduction()
    {
        // TWO closures capture DIFFERENT iterations of `for (const x of ...)`. Per-iteration semantics
        // require c0()===10 and c1()===20 (NOT both 20). This is the shape that decisively needs a fresh
        // per-iteration binding. (Stored into a 2-slot container without a member call inside the loop.)
        await using var engine = CreateEngine();
        var r = await engine.Evaluate(
            "function f(){ var c0,c1; var n=0; for (const x of [10,20]){ if(n===0){ c0=()=>x; } else { c1=()=>x; } n++; } return c0()+''+c1(); } f();");
        Assert.Equal("1020", r);
        Assert.True(RoutedFunction("f"), "captured per-iteration const for-of must route through production");
    }

    // --- ADMITTED shapes (already routing) — regression guard that the decline did not over-reach. ---

    [Fact]
    public async Task PerIterLet_Sum_NoCapture_RoutesThroughProduction()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate(
            "function f(){ var s=0; for (let i=0;i<5;i++){ s+=i; } return s; } f()");
        Assert.Equal(10d, r);
        Assert.True(RoutedFunction("f"), "non-captured per-iteration let sum must keep routing through production");
    }
}
