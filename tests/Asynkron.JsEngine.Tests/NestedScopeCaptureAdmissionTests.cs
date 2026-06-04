using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Option A (collision-only guard) adversarial battery for the nested-scope captured-closure (A1) and
///     arrow (A6) production-VM admissions. The guard was narrowed from "no nested scope at all"
///     (<c>HasOnlyRootFlatSlotMappings</c>) to "no captured name collides with a nested-scope binding"
///     (<c>HasNoCapturedNameShadowedByNestedScope</c>). The hazard (NestedFunctionScopeRegressionTests) is
///     PROVABLY collision-specific: the only flat-slot path for a captured enclosing name is the unscoped
///     <c>SlotAssignmentRewriter.TryResolve</c> fallback, which fires ONLY when a nested scope declares the
///     same name. Non-colliding nested scopes are therefore SAFE to admit; colliding ones must still decline
///     and compute correctly via the IR runner.
///
///     See docs/plans/nested-scope-capture-resolution-design.md (Option A) and §5 risk matrix.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
[Category(TestCategories.ScopeAnalysis)]
public sealed class NestedScopeCaptureAdmissionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ProdLog = "unified-bytecode-production-fast-path func=";

    private void AssertRouted(string funcSuffix)
    {
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            x => x.Message.Contains(ProdLog + funcSuffix, StringComparison.Ordinal));
    }

    private void AssertNotRouted(string funcSuffix)
    {
        // Asserts the SPECIFIC function did not route. The enclosing function (which has no collision)
        // legitimately routes; only the inner colliding closure/arrow must decline.
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            x => x.Message.Contains(ProdLog + funcSuffix, StringComparison.Ordinal));
    }

    // ── Non-colliding nested-scope CLOSURE → now routes + correct ───────────────────────────────────
    [Fact]
    public async Task NonCollidingNestedScopeClosure_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        // `inc` captures `acc` and reads it across statements; the nested `if` block declares its own
        // `let step` which collides with nothing captured. Under Option A this NOW routes.
        var r = await engine.Evaluate("""
            function mk(base){
                let acc = 0;
                function inc(n){
                    if (n > 0) { let step = n * 2; acc += step; }
                    return acc;
                }
                return inc;
            }
            var f = mk(0);
            '' + f(1) + ',' + f(2);
            """);
        Assert.Equal("2,6", r);
        AssertRouted("inc");
    }

    // ── Non-colliding nested-scope ARROW → now routes + correct ─────────────────────────────────────
    [Fact]
    public async Task NonCollidingNestedScopeArrow_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        // The arrow captures the enclosing `base`; its nested `if` block declares `let bonus` (disjoint
        // from any captured name). Routes under Option A.
        var r = await engine.Evaluate("""
            function mk(base){
                return (n) => {
                    if (n > 0) { let bonus = n * 10; return base + bonus; }
                    return base;
                };
            }
            var f = mk(5);
            '' + f(0) + ',' + f(2);
            """);
        Assert.Equal("5,25", r);
        AssertRouted("<anonymous>");
    }

    // ── Colliding nested-scope CLOSURE → STILL declines + correct (regression shapes) ───────────────
    [Fact]
    public async Task CollidingNestedScopeClosure_CapturesOuterLetAfterReturn_DeclinesAndComputes()
    {
        await using var engine = CreateEngine();
        // The inner `if` block declares `let baseValue` shadowing the captured `baseValue`. This is the
        // miscompile hazard, so Option A must DECLINE — the captured read after the block must observe the
        // enclosing value, not the block-local one.
        var r = await engine.Evaluate("""
            function outer(seed){
                let baseValue = seed;
                function inner(flag){
                    if (flag) { let baseValue = seed + 100; return baseValue; }
                    return baseValue;
                }
                return inner;
            }
            var fn = outer(10);
            '' + fn(false) + ',' + fn(true) + ',' + fn(false);
            """);
        Assert.Equal("10,110,10", r);
        // The enclosing `outer` may route; the colliding inner closure must NOT.
        AssertNotRouted("inner");
    }

    [Fact]
    public async Task CollidingNestedScopeClosure_ShadowingInsideInner_DeclinesAndComputes()
    {
        await using var engine = CreateEngine();
        // Partial collision: captures `outer` (collides with nested `let outer`) and `captured` (no
        // collision). The presence of the `outer` collision is the decline trigger; the value must remain
        // correct via the IR runner.
        var r = await engine.Evaluate("""
            function make(){
                const captured = 2;
                let outer = 5;
                function shadower(flag){
                    if (flag) { let outer = 99; return outer + captured; }
                    return outer + captured;
                }
                return shadower;
            }
            var fn = make();
            '' + fn(false) + ',' + fn(true) + ',' + fn(false);
            """);
        Assert.Equal("7,101,7", r);
        // `captured` (no collision) must still resolve; the `outer` collision declines the inner closure.
        AssertNotRouted("shadower");
    }

    // ── Multi-level non-colliding nesting → routes + correct ────────────────────────────────────────
    [Fact]
    public async Task MultiLevelNonCollidingNesting_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        // Block inside block inside the closure; both nested locals (`mid`, `deep`) are disjoint from the
        // captured `acc`. The captured-name set is gathered across the WHOLE instruction stream, so a deep
        // disjoint nesting still routes.
        var r = await engine.Evaluate("""
            function mk(){
                let acc = 1;
                function go(n){
                    if (n > 0) {
                        let mid = n + 1;
                        if (mid > 1) {
                            let deep = mid * 2;
                            acc += deep;
                        }
                    }
                    return acc;
                }
                return go;
            }
            var f = mk();
            '' + f(1) + ',' + f(2);
            """);
        // f(1): mid=2, deep=4, acc=1+4=5 → 5; f(2): mid=3, deep=6, acc=5+6=11 → 11
        Assert.Equal("5,11", r);
        AssertRouted("go");
    }

    // ── Multi-level collision at the DEEPEST level only → declines + correct (§5.4) ─────────────────
    [Fact]
    public async Task DeepCollidingNesting_DeclinesAndComputes()
    {
        await using var engine = CreateEngine();
        // Captured `base`; the collision (`let base = 7`) is TWO blocks deep. The captured-name set is
        // gathered across the whole instruction stream, so the deep collision is still detected and the
        // shape declines — the outer captured read must observe 100.
        var r = await engine.Evaluate("""
            function mk(base){
                function deep(c){
                    if (c) {
                        let mid = 1;
                        if (mid > 0) { let base = 7; return base; }
                    }
                    return base;
                }
                return deep;
            }
            var f = mk(100);
            '' + f(true) + ',' + f(false);
            """);
        Assert.Equal("7,100", r);
        AssertNotRouted("deep");
    }

    // ── Loop nested `let` (per-iteration) non-colliding capture → correct (route per predicate) ─────
    [Fact]
    public async Task LoopPerIterationNonCollidingCapture_Computes()
    {
        await using var engine = CreateEngine();
        // The closure captures `total`; the loop body declares a per-iteration `let cur` (disjoint). The
        // predicate decides routing; correctness is asserted regardless.
        var r = await engine.Evaluate("""
            function mk(){
                let total = 0;
                function run(arr){
                    for (let i = 0; i < arr.length; i++) {
                        let cur = arr[i] * 2;
                        total += cur;
                    }
                    return total;
                }
                return run;
            }
            var f = mk();
            '' + f([1,2]) + ',' + f([3]);
            """);
        // f([1,2]): 2+4=6; f([3]): 6+6=12
        Assert.Equal("6,12", r);
    }

    // ── `const` in nested block (non-colliding) → routes + correct; reassign throws TypeError ───────
    [Fact]
    public async Task NonCollidingNestedConst_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("""
            function mk(){
                let acc = 0;
                function add(n){
                    if (n > 0) { const factor = 3; acc += n * factor; }
                    return acc;
                }
                return add;
            }
            var f = mk();
            '' + f(2) + ',' + f(1);
            """);
        // f(2): acc=6; f(1): acc=9
        Assert.Equal("6,9", r);
        AssertRouted("add");
    }

    [Fact]
    public async Task NonCollidingNestedConst_ReassignmentThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var error = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("""
                function f(n){
                    if (n > 0) { const factor = 3; factor = 4; return factor; }
                    return 0;
                }
                f(1);
                """));
        // Const reassignment is a TypeError ("Assignment to constant variable").
        Assert.Contains("constant variable", error.Message, StringComparison.Ordinal);
        Assert.Contains("TypeError", error.Message, StringComparison.Ordinal);
    }

    // ── TDZ in nested block → ReferenceError, correct ───────────────────────────────────────────────
    [Fact]
    public async Task TdzInNestedBlock_ThrowsReferenceError()
    {
        await using var engine = CreateEngine();
        var error = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("""
                function f(n){
                    if (n > 0) { let v = use; let use = 1; return v; }
                    return 0;
                }
                f(1);
                """));
        // Reading `use` before its initializer is a ReferenceError (TDZ).
        Assert.Contains("before initialization", error.Message, StringComparison.Ordinal);
        Assert.Contains("ReferenceError", error.Message, StringComparison.Ordinal);
    }

    // ── Mixed: shadow collision where the nested name is `const` → declines + correct ───────────────
    [Fact]
    public async Task CollidingNestedConst_DeclinesAndComputes()
    {
        await using var engine = CreateEngine();
        // The closure captures `limit`; a nested block declares `const limit` that SHADOWS it. The
        // collision (regardless of const-ness) is the decline trigger. The captured `limit` read outside
        // the block must observe the enclosing value (50), not the block-local 7.
        var r = await engine.Evaluate("""
            function mk(limit){
                function clamp(flag){
                    if (flag) { const limit = 7; return limit; }
                    return limit;
                }
                return clamp;
            }
            var f = mk(50);
            '' + f(true) + ',' + f(false);
            """);
        Assert.Equal("7,50", r);
        AssertNotRouted("clamp");
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────

    // ── Re-entrancy / permanent-decline cache is not poisoned across distinct plans (§5.9) ──────────
    [Fact]
    public async Task DistinctNonCollidingClosure_NotPoisonedByPriorCollidingDecline()
    {
        await using var engine = CreateEngine();
        // First evaluate a COLLIDING closure (declines, may mark its own plan permanently). Then a
        // DISTINCT non-colliding closure must still ROUTE — the decline is plan-scoped, not name-set
        // global state.
        await engine.Evaluate("""
            function mkBad(base){
                return function(flag){
                    if (flag) { let base = 1; return base; }
                    return base;
                };
            }
            mkBad(9)(false);
            """);

        var r = await engine.Evaluate("""
            function mkGood(){
                let acc = 0;
                function inc(n){
                    if (n > 0) { let step = n; acc += step; }
                    return acc;
                }
                return inc;
            }
            var g = mkGood();
            '' + g(1) + ',' + g(2);
            """);
        Assert.Equal("1,3", r);
        AssertRouted("inc");
    }
}
