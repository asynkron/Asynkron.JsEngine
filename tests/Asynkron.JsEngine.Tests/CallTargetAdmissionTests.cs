using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     A9/A10 (burn-down): admit the IDENTIFIER CALL-TARGET cluster into the production sync VM.
///
///     A10 — FREE identifier call target (<c>helper(x)</c> where <c>helper</c> is a global/free name,
///     not an activation slot). The call target lowers to a dynamic-identifier load that walks the
///     threaded environment chain; the call then applies with a <c>this</c> of undefined (a free call
///     has no receiver). The resumable route already admits this shape; the sync route mirrors it.
///
///     A9 — identifier call-target OUTSIDE the first invocation boundary. The SECOND+ identifier call
///     in a body (<c>a(); return b();</c> — <c>b()</c> is past the first call boundary) previously
///     declined as <c>CallDependency</c>. Subsequent identifier calls are now admitted.
///
///     Each test asserts BOTH the observable result AND that the enclosing function routed through the
///     production fast path (or, for negatives, that it did not).
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class CallTargetAdmissionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    // A10: free/global identifier call target — helper is not an activation slot of f.
    [Fact]
    public async Task FreeIdentifierCallTarget_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function helper(x){ return x+1; } function f(){ return helper(4); } f();");
        Assert.Equal(5d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A10: a free function call passes NO receiver, so the callee's `this` is bound per the callee's
    // own strict/sloppy mode. The callee `probe` here is SLOPPY, so `this` is coerced to the global
    // object (not undefined). The routed call must reproduce the interpreter's sloppy free-call `this`
    // exactly: `this === globalThis` inside probe, and `this !== undefined`.
    [Fact]
    public async Task FreeIdentifierCall_SloppyThisIsGlobal()
    {
        await using var engine = CreateEngine();
        var isGlobal = await engine.Evaluate(
            "var sawGlobal; function probe(){ sawGlobal = (this === globalThis); } " +
            "function f(){ probe(); return sawGlobal; } f();");
        Assert.Equal(true, isGlobal);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A10: a free call into a STRICT callee binds `this` to undefined (no sloppy coercion). The routed
    // call must reproduce this — proves the receiver pushed for a free callee is genuinely undefined
    // and is only coerced by the callee's own mode, never by the production VM call site.
    [Fact]
    public async Task FreeIdentifierCall_StrictCalleeThisIsUndefined()
    {
        await using var engine = CreateEngine();
        var isUndefined = await engine.Evaluate(
            "var sawUndefined; function probe(){ 'use strict'; sawUndefined = (this === undefined); } " +
            "function f(){ probe(); return sawUndefined; } f();");
        Assert.Equal(true, isUndefined);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A10 negative-semantics: an undeclared free call target throws ReferenceError, exactly as the
    // interpreter would. Admission must not silently swallow the missing binding.
    [Fact]
    public async Task UndeclaredFreeCallTarget_ThrowsReferenceError()
    {
        await using var engine = CreateEngine();
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("function f(){ return missingFn(1); } f();"));
        var thrown = ex.ThrownValue.ToObject();
        var jsObject = Assert.IsType<JsTypes.JsObject>(thrown);
        Assert.True(jsObject.TryGetValue("message", out var message));
        Assert.Contains("missingFn", message.ToString(), StringComparison.Ordinal);
    }

    // A9: second identifier call past the first invocation boundary (a(); return b();).
    [Fact]
    public async Task SecondIdentifierCall_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function a(){ return 1; } function b(){ return 2; } " +
            "function f(){ a(); return b(); } f();");
        Assert.Equal(2d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A9 ORDER PROOF: multiple sequential free calls each push a tag — assert exact order preserved.
    [Fact]
    public async Task MultipleSequentialCalls_PreserveOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "var log=''; " +
            "function p(t){ log += t; } " +
            "function f(){ p('a'); p('b'); p('c'); return log; } f();");
        Assert.Equal("abc", result?.ToString());
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A9 + A10: a free call whose argument is itself a free call (compose with A11 complex args).
    [Fact]
    public async Task FreeCallWithNestedFreeCallArgument_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function g(x){ return x*2; } function helper(x){ return x+1; } " +
            "function f(){ return helper(g(2)); } f();");
        // g(2)=4, helper(4)=5
        Assert.Equal(5d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // A9: a slot-resolved (parameter) call target used as the SECOND call past the first boundary.
    [Fact]
    public async Task SecondSlotCall_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(g,h){ g(); return h(); } f(()=>0, ()=>7);");
        Assert.Equal(7d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // NEGATIVE — a shape that should still decline stays declined and still computes via the
    // interpreter fallback. An assignment as the call argument is a non-admitted value shape.
    [Fact]
    public async Task AssignmentArgumentFreeCall_StillDeclinesButComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function helper(x){ return x+1; } var box={v:0}; " +
            "function f(){ return helper(box.v = 7); } f() + ':' + box.v;");
        Assert.Equal("8:7", result?.ToString());
        AssertNotRouted("unified-bytecode-production-fast-path func=f");
    }

    // SAFETY BOUNDARY (regression guard): a per-iteration `const` (TDZ) inside a for-of body with a
    // `continue` that skips the const initializer hits a pre-existing VM loop-environment limitation in
    // the materialized-call-environment mode (the mode the free-call-target / free-read paths force).
    // This per-iteration const + continue shape under the dynamic-name path used to be DECLINED to the IR
    // runner because the production VM left the per-iteration const's flat slot in TDZ (the dynamic-lexical
    // init wrote only the materialized-env binding). The VM now mirrors dynamic-lexical declare/init into
    // the bound flat slot (UnifiedBytecodeVirtualMachine.MirrorDynamicLexicalToFlatSlot), so this shape
    // routes through production AND computes correctly. We assert BOTH.
    [Fact]
    public async Task PerIterationConstWithContinueAndFreeCall_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function t(){ 'use strict'; const rows=[]; " +
            "for (let inner of [10,20,30]) { if (inner===20) continue; " +
            "const value = inner; rows.push(String(value)); } return rows.join(','); } t();");
        Assert.Equal("10,30", result?.ToString());
        AssertRouted("unified-bytecode-production-fast-path func=t");
    }

    // Same loop/continue/const shape but the callee is a SLOT (parameter) — no free name, so the
    // materialized-env path is NOT forced and the const slot is reset directly. This shape is safe and
    // continues to route (or at least compute correctly); it isolates the bug to the dynamic-name path.
    [Fact]
    public async Task PerIterationConstWithContinueAndSlotCall_Computes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function t(s){ 'use strict'; const rows=[]; " +
            "for (let inner of [10,20,30]) { if (inner===20) continue; " +
            "const value = inner; rows.push(s(value)); } return rows.join(','); } t(String);");
        Assert.Equal("10,30", result?.ToString());
    }

    // Same loop/continue/const shape admitted via a free READ (LoadDynamicIdentifier) — also forces the
    // materialized-env path. With the dynamic-lexical-to-flat-slot mirror in place the production VM keeps
    // the per-iteration const slot consistent with its env binding, so this routes AND computes correctly.
    [Fact]
    public async Task PerIterationConstWithContinueAndFreeRead_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "var bump=100; function t(){ 'use strict'; const rows=[]; " +
            "for (let inner of [10,20,30]) { if (inner===20) continue; " +
            "const value = inner + bump; rows.push(value); } return rows.join(','); } t();");
        Assert.Equal("110,130", result?.ToString());
        AssertRouted("unified-bytecode-production-fast-path func=t");
    }

    // BOUNDARY-DOESN'T-OVERREACH: a free call target in a loop body WITHOUT a per-iteration const must
    // still route — the guard keys on the const+continue shape, not on loops-with-free-names generally.
    [Fact]
    public async Task FreeCallInLoopWithoutPerIterationConst_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function add(a,b){ return a+b; } " +
            "function f(){ var s=0; for (var i=0;i<3;i++){ if(i===1) continue; s=add(s,i); } return s; } f();");
        Assert.Equal(2d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // TAIL-CALL SAFETY BOUNDARY (regression guard): a STRICT self-recursive tail call by identifier name
    // (return countdown(n-1)) is exactly the shape the IR runner tail-call-optimizes (flat native stack).
    // The production VM has NO TCO, so admitting the A9/A10 identifier call-target cluster must NOT route
    // this shape to the VM — otherwise deep recursion overflows the native stack and crashes the host. The
    // eligibility guard declines it so it stays on the IR runner. We assert it runs DEEP without
    // overflowing AND that countdown did NOT route through the production VM.
    [Fact(Timeout = 15000)]
    public async Task StrictSelfRecursiveTailCall_StaysFlatAndDeclines()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function countdown(n, acc) { 'use strict'; if (n === 0) { return acc; } " +
            "return countdown(n - 1, acc + 1); } countdown(100000, 0);");
        Assert.Equal(100000d, Convert.ToDouble(result));
        AssertNotRouted("unified-bytecode-production-fast-path func=countdown");
    }

    // Strict self-recursive tail call in the CONDITIONAL (ternary) tail position — same boundary, the call
    // sits in a branch of the returned expression. Must stay flat and decline.
    [Fact(Timeout = 15000)]
    public async Task StrictSelfRecursiveTernaryTailCall_StaysFlatAndDeclines()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function countdown(n, acc) { 'use strict'; " +
            "return n === 0 ? acc : countdown(n - 1, acc + 1); } countdown(100000, 0);");
        Assert.Equal(100000d, Convert.ToDouble(result));
        AssertNotRouted("unified-bytecode-production-fast-path func=countdown");
    }

    // A SLOPPY tail-position identifier call is NOT tail-call-optimized anywhere (the IR runner's restart
    // requires strict mode), so the VM is no worse for it — it stays ADMITTED. This proves the guard is
    // scoped to strict mode and does not over-decline the headline A10 shape (sloppy `return helper(4)`).
    [Fact]
    public async Task SloppyTailPositionIdentifierCall_StillRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function helper(x){ return x+1; } function f(){ return helper(4); } f();");
        Assert.Equal(5d, Convert.ToDouble(result));
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
