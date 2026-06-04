using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Option B (Stage 5) adversarial battery for the durable nested-scope captured-name fix in
///     SlotAssignmentRewriter.TryResolve. These cover the per-use-site capture cases the design §5 risk
///     matrix flagged: a name that is a nested-block LOCAL at one read and CAPTURED at another within the
///     same inner function; collisions in catch bindings, per-iteration loop lets, multi-level blocks, and
///     const (TDZ + reassign). Each asserts the ECMAScript-correct value; the production VM (with the guard
///     retired) must match the IR runner.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
[Category(TestCategories.ScopeAnalysis)]
public sealed class NestedScopeCaptureOptionBAdversarialTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ProdLog = "unified-bytecode-production-fast-path func=";

    private void AssertRouted(string funcSuffix) =>
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            x => x.Message.Contains(ProdLog + funcSuffix, StringComparison.Ordinal));

    // ── Mixed per-use-site: same name is a nested-block LOCAL at one read and CAPTURED at another ────
    [Fact]
    public async Task MixedPerUseSite_NameLocalInBlockAndCapturedOutside_BothResolveCorrectly()
    {
        await using var engine = CreateEngine();
        // `v` is captured from the enclosing `mk` (value 3) at the `return v` site OUTSIDE the block, and is
        // a nested-block local (value 100) at the `return v` site INSIDE the block. Both reads must observe
        // their own binding.
        var r = await engine.Evaluate("""
            function mk(){
                let v = 3;
                function use(flag){
                    if (flag) { let v = 100; return v; }   // local v == 100
                    return v;                               // captured v == 3
                }
                return use;
            }
            var f = mk();
            '' + f(true) + ',' + f(false) + ',' + f(true);
            """);
        Assert.Equal("100,3,100", r);
        AssertRouted("use");
    }

    // ── Shadowing WRITE: the block-local write must not be observed by the captured read (§5.2) ──────
    [Fact]
    public async Task ShadowingWrite_BlockLocalWriteDoesNotLeakToCapturedRead()
    {
        await using var engine = CreateEngine();
        // The block mutates its OWN local `x`; the captured `x` (enclosing, value 1) must be unaffected.
        var r = await engine.Evaluate("""
            function mk(){
                let x = 1;
                function go(flag){
                    if (flag) { let x = 50; x = x + 1; return x; }  // local 51
                    return x;                                        // captured 1
                }
                return go;
            }
            var f = mk();
            '' + f(true) + ',' + f(false);
            """);
        Assert.Equal("51,1", r);
        AssertRouted("go");
    }

    // ── Collision in a CATCH binding shadowing a captured name (§5 catch case) ───────────────────────
    [Fact]
    public async Task CatchBindingShadowsCapturedName_ResolvesCorrectly()
    {
        await using var engine = CreateEngine();
        // Captured `e` (enclosing, value 7); the inner try/catch declares `catch (e)`. Inside the catch the
        // binding is the thrown value 99; the read after the catch must observe the captured 7.
        var r = await engine.Evaluate("""
            function mk(){
                let e = 7;
                function go(flag){
                    if (flag) {
                        try { throw 99; } catch (e) { return e; }   // catch binding 99
                    }
                    return e;                                         // captured 7
                }
                return go;
            }
            var f = mk();
            '' + f(true) + ',' + f(false);
            """);
        Assert.Equal("99,7", r);
    }

    // ── Collision in a per-iteration loop `let` shadowing a captured name (§5 loop probe) ────────────
    [Fact]
    public async Task LoopPerIterationLetShadowsCapturedName_ResolvesCorrectly()
    {
        await using var engine = CreateEngine();
        // Captured `i` (enclosing, value 1000); the loop head declares a per-iteration `let i`. The loop
        // body reads the per-iteration `i`; the read after the loop must observe the captured 1000.
        var r = await engine.Evaluate("""
            function mk(){
                let i = 1000;
                function go(){
                    let sum = 0;
                    for (let i = 0; i < 3; i++) { sum += i; }   // per-iteration i: 0+1+2 = 3
                    return sum + ':' + i;                        // captured i == 1000
                }
                return go;
            }
            var f = mk();
            f();
            """);
        Assert.Equal("3:1000", r);
    }

    // ── const shadow: TDZ inside block + reassign of captured const stays correct ────────────────────
    [Fact]
    public async Task ConstShadow_TdzInsideBlock_AndCapturedReadOutside()
    {
        await using var engine = CreateEngine();
        // Captured `k` (enclosing const, value 5); the block declares `const k = 9`. The read inside the
        // block observes 9; the read after the block observes the captured 5.
        var r = await engine.Evaluate("""
            function mk(){
                const k = 5;
                function go(flag){
                    if (flag) { const k = 9; return k; }   // block const 9
                    return k;                               // captured const 5
                }
                return go;
            }
            var f = mk();
            '' + f(true) + ',' + f(false);
            """);
        Assert.Equal("9,5", r);
    }

    // ── Reassigning a captured const through the closure throws TypeError (semantics preserved) ──────
    [Fact]
    public async Task ReassignCapturedConst_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var error = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("""
                function mk(){
                    const k = 5;
                    function go(){ k = 6; return k; }
                    return go;
                }
                mk()();
                """));
        Assert.Contains("constant variable", error.Message, StringComparison.Ordinal);
        Assert.Contains("TypeError", error.Message, StringComparison.Ordinal);
    }

    // ── Multi-level: collision two levels up AND at the deepest block, same captured name ────────────
    [Fact]
    public async Task MultiLevelDoubleShadow_EachReadObservesNearestEnclosingBinding()
    {
        await using var engine = CreateEngine();
        // Captured `n` (enclosing, value 1). Outer block shadows `let n = 2`; inner block shadows `let n=3`.
        // Each return observes its nearest lexically-enclosing binding.
        var r = await engine.Evaluate("""
            function mk(){
                let n = 1;
                function go(a, b){
                    if (a) {
                        let n = 2;
                        if (b) { let n = 3; return n; }   // deepest n == 3
                        return n;                          // middle n == 2
                    }
                    return n;                              // captured n == 1
                }
                return go;
            }
            var f = mk();
            '' + f(true,true) + ',' + f(true,false) + ',' + f(false,false);
            """);
        Assert.Equal("3,2,1", r);
        AssertRouted("go");
    }
}
