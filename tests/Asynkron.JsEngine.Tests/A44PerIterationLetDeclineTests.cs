using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     A44 (burn-down) — DECLINED, investigated. Per-iteration <c>let</c>/<c>const</c> loop bindings that are
///     CAPTURED by a closure (so each iteration must produce a fresh binding the closure observes) are NOT
///     eligible for the production unified-bytecode VM and stay on the IR runner.
///
///     Evidence (this run, on a relaxed eligibility gate):
///       • The non-captured cases (per-iteration sum, for-of over flat slots) already route through
///         production today — they live entirely in the flat-slot model.
///       • A captured per-iteration <c>let</c> emits a PushEnvironment whose synthetic per-iteration scope
///         has NO flat-slot mapping (the binding lives in a heap environment the closure captures), so
///         <c>IsSupportedPushEnvironment</c> declines it (UnsupportedPlanShape, ~line 615 /
///         UnifiedBytecodeProductionEligibility.IsSupportedPushEnvironment).
///       • Relaxing that gate exposes a deeper blocker: the production compiler rejects the lexical
///         declaration target ("requires dynamic identifier operations"), and when forced onto the
///         dynamic-identifier path the VM throws a SPURIOUS
///         <c>ReferenceError: Cannot access 'i' before initialization</c> — the per-iteration heap-env
///         binding the closure captures is left in its TDZ (uninitialized) because the
///         DeclareDynamicLexical / MirrorDynamicLexicalToFlatSlot path mirrors flat↔call-env but not the
///         freshly-created per-iteration scope environment the closure captured.
///
///     Admitting A44 cleanly therefore requires per-iteration heap-environment binding init + value copy +
///     TDZ initialization mirrored into the captured environment — a block-environment change with blast
///     radius across all lexical block bindings (the same class of work the A43 decline calls out). Correct
///     decline &gt; fragile admission: the IR runner keeps every shape below correct today.
///
///     These tests are a STANDING TRIPWIRE: they assert the CORRECT runtime results (per-iteration capture
///     semantics) AND that the enclosing function does NOT silently route through the production VM. A future
///     careless relaxation of the PushEnvironment gate that reintroduces the TDZ bug will turn one of these
///     red.
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

    // --- DECLINED shapes: correct per-iteration capture semantics, NOT routed through production. ---

    [Fact]
    public async Task PerIterLet_CapturedClosure_CorrectButDeclined()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate(
            "function f(){ var g; for (let i=0;i<3;i++){ if(i===1){ g=()=>i; } } return g; } f()();");
        Assert.Equal(1d, r);
        Assert.False(RoutedFunction("f"), "captured per-iteration let must NOT route through production VM");
    }

    [Fact]
    public async Task PerIterLet_CapturedViaDynamicPath_CorrectButDeclined()
    {
        // f is itself a captured closure (captures enclosing 'tag') => it would otherwise take the
        // dynamic-identifier production path. The per-iteration let captured by an inner arrow must
        // still NOT route, and the captured 'i' must read 1 (per-iteration), not throw a TDZ error.
        await using var engine = CreateEngine();
        var r = await engine.Evaluate(
            "function mk(tag){ return function f(){ var g; for (let i=0;i<3;i++){ if(i===tag){ g=()=>i; } } return g; }; } mk(1)()();");
        Assert.Equal(1d, r);
        Assert.False(RoutedFunction("f"), "captured per-iteration let on the dynamic path must NOT route");
    }

    [Fact]
    public async Task PerIterConst_OfMultiCaptured_CorrectViaInterpreter()
    {
        // TWO closures capture DIFFERENT iterations of `for (const x of ...)`. Per-iteration semantics
        // require c0()===10 and c1()===20 (NOT both 20). This is the shape that decisively needs a fresh
        // per-iteration binding. It must produce the correct values; it is the multi-capture variant A44
        // targets. (Stored into a 2-slot container without a member call inside the loop.)
        await using var engine = CreateEngine();
        var r = await engine.Evaluate(
            "function f(){ var c0,c1; var n=0; for (const x of [10,20]){ if(n===0){ c0=()=>x; } else { c1=()=>x; } n++; } return c0()+''+c1(); } f();");
        Assert.Equal("1020", r);
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
