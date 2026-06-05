using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     A8 (burn-down): admit a call RETURNED FROM INSIDE a <c>finally</c> block into the production sync VM,
///     scoped to NON-STRICT (sloppy) functions only.
///
///     Spec: a <c>return f();</c> inside a finally block is a TAIL POSITION — the finally completion overrides
///     the protected block's completion. The production unified-bytecode VM has NO same-function tail-call
///     optimization. The IR runner (SyncFunctionInvoker.TryGetLegacySameFunctionTailRestartTarget) DOES
///     tail-call-optimize deep STRICT same-function identifier recursion onto a flat native stack, and that
///     restart fires for a finally-region return exactly as for a try-body return. So:
///
///     - STRICT finally-return calls STAY DECLINED — routing a strict self-recursive finally tail call to the
///       VM would re-enter the native stack each iteration and overflow it (uncatchable StackOverflow).
///     - NON-STRICT finally-return calls are ADMITTED — the restart requires strict mode, so a sloppy
///       finally-return is never tail-call-optimized anywhere and the VM is no worse than the IR runner.
///
///     Each test asserts BOTH the observable result (finally-return overrides try-return/throw, exactly per
///     spec) AND the routing decision (admitted sloppy shapes route through the production fast path; strict
///     self-recursion stays flat and declines).
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class FinallyReturnCallAdmissionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    // A8/A45 headline (sloppy finally-return overrides the protected try-return). The FREE-identifier callee
    // sits inside the finally region, so the with-depth reachability analysis must traverse the finally entry
    // when deciding whether ordinary dynamic-name lowering is required.
    [Fact]
    public async Task SloppyFinallyReturnFreeCall_OverridesTryReturnAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function g(){ return 7; } function f(){ try { return 1; } finally { return g(); } } f();");
        Assert.Equal(7d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A8 routing-positive for a FREE-identifier finally callee: once the REACHABLE region carries a dynamic
    // dependency (here a free READ of `seed` in the try body) the ordinary-dynamic-name path is enabled and the
    // sloppy free finally-return call routes through the production VM. Proves the strict-only narrowing admits
    // the free-callee shape whenever the gating path is already on, while finally-return still overrides.
    [Fact]
    public async Task SloppyFinallyReturnFreeCall_WithReachableDynamicDep_OverridesTryReturnAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "var seed = 3; function g(){ return 7; } " +
            "function f(){ try { return seed; } finally { return g(); } } f();");
        Assert.Equal(7d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A8: a finally-return MEMBER call (o.m()) — member callees are NEVER tail-call-optimized anywhere
    // (the IR restart requires an identifier callee), so this is admitted even though it is in tail position.
    [Fact]
    public async Task SloppyFinallyReturnMemberCall_OverridesTryReturnAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(o){ try { return 1; } finally { return o.m(); } } f({ m(){ return 9; } });");
        Assert.Equal(9d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A8: a finally-return call overrides a THROW from the protected block — the finally's abrupt return
    // completion replaces the pending throw, so no exception escapes. Routes (sloppy).
    [Fact]
    public async Task SloppyFinallyReturnCall_OverridesThrowAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function g(){ return 5; } function f(){ try { throw new Error('boom'); } finally { return g(); } } f();");
        Assert.Equal(5d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A8 SAFETY BOUNDARY (regression guard): a STRICT self-recursive tail call returned from inside a finally
    // (return countdown(n-1)) is exactly the shape the IR runner tail-call-optimizes onto a flat native stack.
    // The production VM has NO TCO, so admitting A8 must NOT route this strict shape to the VM — otherwise deep
    // recursion overflows the native stack and crashes the host. It must run DEEP without overflowing AND must
    // NOT route through the production VM.
    [Fact(Timeout = 15000)]
    public async Task StrictSelfRecursiveFinallyReturnTailCall_StaysFlatAndDeclines()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "let callCount = 0; " +
            "function countdown(n) { 'use strict'; " +
            "if (n === 0) { callCount++; return 0; } " +
            "try { } finally { return countdown(n - 1); } } " +
            "countdown(100000); callCount;");
        Assert.Equal(1d, Convert.ToDouble(result));
        AssertNotRouted("unified-bytecode-production-fast-path func=countdown");
    }

    // A8 SAFETY BOUNDARY: a strict finally-return FREE (non-recursive) identifier call also stays declined.
    // The eligibility guard is conservative — it cannot prove statically that the callee is not the same
    // function, so any strict finally-return call declines. It still computes correctly via the IR runner.
    [Fact]
    public async Task StrictFinallyReturnFreeCall_DeclinesButComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function g(){ return 7; } function f(){ 'use strict'; try { return 1; } finally { return g(); } } f();");
        Assert.Equal(7d, Convert.ToDouble(result));
        AssertNotRouted("unified-bytecode-production-fast-path func=f");
    }

    // A8: a NON-STRICT (sloppy) self-recursive finally-return call is NEVER tail-call-optimized anywhere (the
    // IR runner's restart requires strict mode), so the strict-only A8 guard no longer force-declines it.
    // Shallow depth so neither path overflows.
    [Fact(Timeout = 15000)]
    public async Task SloppySelfRecursiveFinallyReturnCall_Computes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function countdown(n) { if (n === 0) { return 0; } " +
            "try { } finally { return countdown(n - 1); } } countdown(50);");
        Assert.Equal(0d, Convert.ToDouble(result));
    }

    // BOUNDARY-DOESN'T-OVERREACH: a finally that CALLS a function without RETURNING it (a statement call in
    // the finally body) is not a tail position and was never declined — it must keep routing. The try-return
    // result is preserved; the finally side effect runs.
    [Fact]
    public async Task FinallyStatementCallWithoutReturn_StillRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "var log=''; function side(){ log+='x'; } " +
            "function f(){ try { return log + '1'; } finally { side(); } } f() + ':' + log;");
        Assert.Equal("1:x", result?.ToString());
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    private void AssertRouted(string expectedLog)
    {
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(expectedLog, StringComparison.Ordinal));
    }

    private void AssertNotRouted(string unexpectedLog)
    {
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(unexpectedLog, StringComparison.Ordinal));
    }
}
