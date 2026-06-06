using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Closure / captured-activation admission (burn-down A1, Stage 0): a function with a MULTI-STATEMENT
///     body that captures a variable from an enclosing function activation now routes through the production
///     unified-bytecode VM (captured accesses lower to dynamic-identifier ops resolved through the threaded
///     closure environment). Probes the correctness edges the design flagged.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class ClosureCapturedActivationTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ProdLog = "unified-bytecode-production-fast-path func=";

    [Fact]
    public async Task CapturedLocal_MultiStatementBody_RoutesAndMutatesByReference()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate(
            "function mk(){ let n=0; function inc(){ n++; return n; } return inc; } var f=mk(); ''+f()+','+f()+','+f();");
        Assert.Equal("1,2,3", r);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(), x => x.Message.Contains(ProdLog + "inc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NestedThreeLevelCapture_InnermostMutatesOutermost()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("""
            function a(){
                let n = 0;
                function b(){ function c(){ n += 10; return n; } return c; }
                return { c: b(), read: function(){ return n; } };
            }
            var o = a();
            '' + o.c() + ',' + o.c() + ',' + o.read();
            """);
        Assert.Equal("10,20,20", r);
    }

    [Fact]
    public async Task CapturedConst_Write_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("""
            function mk(){ const k = 1; function w(){ try { k = 2; return 'no-throw'; } catch (e) { return e.constructor.name; } } return w; }
            mk()();
            """);
        Assert.Equal("TypeError", r);
    }

    [Fact]
    public async Task CapturedLet_ReadBeforeInitializer_ThrowsReferenceError()
    {
        await using var engine = CreateEngine();
        // The inner closure `r` captures the enclosing `let x` and is invoked DURING mk's body,
        // before `let x = 5` runs — so the captured read hits the temporal dead zone and must throw
        // ReferenceError (the captured load resolves the uninitialized enclosing slot).
        var r = await engine.Evaluate("""
            function mk(){
                function r(){ try { return '' + x; } catch (e) { return e.constructor.name; } }
                var early = r();
                let x = 5;
                return early;
            }
            mk();
            """);
        Assert.Equal("ReferenceError", r);
    }

    [Fact]
    public async Task MixedRoute_VmInnerCapturesFromOuter_AliasesSameBinding()
    {
        await using var engine = CreateEngine();
        // outer mutates n directly AND via the captured inner closure; both must see the same binding.
        var r = await engine.Evaluate("""
            function mk(){
                let n = 0;
                function inc(){ n++; return n; }
                n = 5;            // outer write
                var a = inc();    // 6
                n += 10;          // outer write -> 16
                var b = inc();    // 17
                return a + ',' + b + ',' + n;
            }
            mk();
            """);
        Assert.Equal("6,17,17", r);
    }

    [Fact]
    public async Task ClosureWithRetainedWithObjectRead_DeclinesProductionRoute_ButRunsCorrectly()
    {
        await using var engine = CreateEngine();
        // A closure whose retained enclosing chain has a live `with` object is residue: the production VM
        // owns active current-environment with lookups, not closure-retained with environments.
        var r = await engine.Evaluate("""
            function mk(o){ with (o) { function g(){ return x; } return g; } }
            var f = mk({ x: 42 });
            f();
            f();
            """);
        Assert.Equal(42d, r);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(), x => x.Message.Contains(ProdLog + "g", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ClosureWithRetainedWithObjectReceiverCall_DeclinesProductionRoute_ButRunsCorrectly()
    {
        await using var engine = CreateEngine();
        // Keep the receiver-sensitive retained-with shape pinned separately: if `finish` is supplied by
        // the retained with object, calling it from the closure must preserve that object as `this`.
        var r = await engine.Evaluate("""
            function mk(o){ with (o) { function g(){ return finish(); } return g; } }
            var holder = {
                x: 7,
                finish: function(){ return this === holder ? this.x * 2 : -1; }
            };
            var f = mk(holder);
            f();
            """);
        Assert.Equal(14d, r);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(), x => x.Message.Contains(ProdLog + "g", StringComparison.Ordinal));
    }
}
