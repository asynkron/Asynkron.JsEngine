using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Multi-statement arrow admission (burn-down A6, Stage 0): an arrow function with a MULTI-STATEMENT
///     body (no longer restricted to a single <c>return &lt;expr&gt;;</c>) now routes through the production
///     unified-bytecode VM. Arrows inherit <c>this</c> and <c>new.target</c> LEXICALLY from the enclosing
///     scope; that threading (TryExecuteProductionUnifiedBytecode) is body-shape-agnostic and continues to
///     flow for multi-statement bodies. Under Option B (Stage 5) the nested-scope collision guard has been
///     retired — an arrow whose body opens a nested lexical scope (an <c>if</c>/loop block with its own
///     <c>let</c>/<c>const</c>), whether or not the nested name SHADOWS a captured enclosing name, now routes
///     AND computes correctly, because SlotAssignmentRewriter no longer mis-stamps the captured read to the
///     off-stack block slot. Probes the correctness edges the design flagged.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class MultiStatementArrowAdmissionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ProdLog = "unified-bytecode-production-fast-path func=";

    [Fact]
    public async Task MultiStatementArrow_LocalConstThenReturn_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("const f = (a,b) => { const s = a+b; return s*2; }; f(3,4);");
        Assert.Equal(14d, r);
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            x => x.Message.Contains(ProdLog, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultiStatementArrow_LexicalThis_ReadsEnclosingReceiver()
    {
        await using var engine = CreateEngine();
        // The arrow is created inside method m(); it captures `this` lexically (the object o). A
        // multi-statement body must still resolve this.x to the enclosing receiver's property.
        var r = await engine.Evaluate("""
            var o = {
                x: 10,
                m: function () {
                    var f = () => { var bump = 5; return this.x + bump; };
                    return f();
                }
            };
            o.m();
            """);
        Assert.Equal(15d, r);
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            x => x.Message.Contains(ProdLog, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultiStatementArrow_LexicalNewTarget_IsUndefined()
    {
        await using var engine = CreateEngine();
        // Arrows never have their own new.target; they inherit it lexically. Called as a plain function,
        // the inherited new.target is undefined. A multi-statement body must observe that correctly.
        var r = await engine.Evaluate("""
            const f = () => { var n = new.target; return n === undefined ? 'undef' : 'defined'; };
            f();
            """);
        Assert.Equal("undef", r);
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            x => x.Message.Contains(ProdLog, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultiStatementArrow_CapturesEnclosingLocal_AcrossStatements()
    {
        await using var engine = CreateEngine();
        // The arrow captures the enclosing local `base` and reads it across multiple body statements.
        var r = await engine.Evaluate("""
            function mk(base) {
                return (k) => { var doubled = base * 2; var total = doubled + k; return total; };
            }
            var f = mk(100);
            '' + f(1) + ',' + f(2);
            """);
        Assert.Equal("201,202", r);
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            x => x.Message.Contains(ProdLog, StringComparison.Ordinal));
    }

    [Fact]
    public async Task NestedScopeArrow_DeclaresNonCollidingLetInNestedBlock_Routes()
    {
        await using var engine = CreateEngine();
        // The body opens a nested lexical scope (the `if` block declares its own `let y`). The nested
        // `let y` does not collide with any captured name; this ROUTES through the production VM and
        // computes correctly.
        var r = await engine.Evaluate("""
            const f = (c) => { if (c) { let y = 1; return y; } return 0; };
            '' + f(true) + ',' + f(false);
            """);
        Assert.Equal("1,0", r);
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            x => x.Message.Contains(ProdLog, StringComparison.Ordinal));
    }

    [Fact]
    public async Task NestedScopeArrow_ShadowsCapturedNameInNestedBlock_RoutesUnderOptionB()
    {
        await using var engine = CreateEngine();
        // COLLIDING counterpart: the arrow captures the enclosing local `base` AND a nested `if` block
        // declares its own `let base`. Under Option B the rewriter no longer mis-stamps the captured read
        // to the block-local slot, so the colliding arrow ROUTES and computes correctly: the captured
        // `base` read after the block observes the enclosing value (100), not the block-local 7.
        var r = await engine.Evaluate("""
            function mk(base) {
                return (c) => {
                    if (c) { let base = 7; return base; }
                    return base;
                };
            }
            var f = mk(100);
            '' + f(true) + ',' + f(false);
            """);
        Assert.Equal("7,100", r);
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            x => x.Message.Contains(ProdLog + "<anonymous>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SingleReturnArrow_StillRoutes_RegressionGuard()
    {
        await using var engine = CreateEngine();
        // The pre-existing single-return arrow fast path must keep routing.
        var r = await engine.Evaluate("const f = (a,b) => a + b; f(20,22);");
        Assert.Equal(42d, r);
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            x => x.Message.Contains(ProdLog, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultiStatementArrow_MultipleLocalsAndBranchlessFlow_Computes()
    {
        await using var engine = CreateEngine();
        // Several flat root-scope locals, sequential mutation, no nested block — admitted.
        var r = await engine.Evaluate("""
            const f = (n) => {
                var a = n + 1;
                var b = a * a;
                a = a + b;
                return a;
            };
            f(3);
            """);
        Assert.Equal(20d, r); // a=4, b=16, a=20
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            x => x.Message.Contains(ProdLog, StringComparison.Ordinal));
    }
}
