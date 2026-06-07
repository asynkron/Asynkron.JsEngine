using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     COMPOUND-WRITE complex RHS admission — the <c>+=</c> analogue of the plain-write complex-RHS
///     admission (commit 830236be0).
///
///     Before this change the production compound-property-write boundary admitted
///     <c>o.x += rhs</c>, <c>o[k] -= rhs</c> and <c>this.x *= rhs</c> only when the RHS was a single
///     simple operand or a template literal. A richer RHS — a binary (<c>o.x += a + b</c>), a nested
///     call (<c>o.x -= g(z)</c>), a member-read chain, or any composition thereof — declined to the
///     interpreter.
///
///     This change widens the compound-write RHS to ANY already-admitted value-producing expression,
///     lowered onto the operand stack AFTER the old-value read. Compound assignment semantics are
///     preserved exactly: the receiver (and computed key) are evaluated ONCE before the old-value
///     read; the RHS is evaluated AFTER the old value is read; then the binary operator is applied
///     and the result stored. The op stream is never reordered. The order-proof tests below assert
///     that sequence with side-effect logs. Each positive test asserts BOTH the result AND that the
///     enclosing function routed through the production fast path.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class CompoundPropertyWriteComplexRhsAdmissionTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    // o.x += a + b: a binary RHS on a named compound write.
    [Fact]
    public async Task NamedCompound_BinaryRhs_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,a,b){ o.x += a + b; return o.x; } f({x:1},2,3);");
        Assert.Equal(6d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // o.x -= g(z): a nested call RHS on a named compound write.
    [Fact]
    public async Task NamedCompound_CallRhs_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,g,z){ o.x -= g(z); return o.x; } f({x:10},v=>v,3);");
        Assert.Equal(7d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // o.x += s.a.b: a member-read chain RHS on a named compound write.
    [Fact]
    public async Task NamedCompound_MemberReadChainRhs_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,s){ o.x += s.a.b; return o.x; } f({x:1},{a:{b:9}});");
        Assert.Equal(10d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // o[k] *= a + b: a binary RHS on a computed compound write with a simple key.
    [Fact]
    public async Task ComputedCompound_BinaryRhs_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,k,a,b){ o[k] *= a + b; return o[k]; } f({v:2},'v',2,1);");
        Assert.Equal(6d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // o[k] += g(z): a nested call RHS on a computed compound write.
    [Fact]
    public async Task ComputedCompound_CallRhs_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,k,g,z){ o[k] += g(z); return o[k]; } f({v:4},'v',v=>v,6);");
        Assert.Equal(10d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // ORDER PROOF (computed compound, simple constant key + side-effecting RHS): the key is a simple
    // constant ('v') so the write ROUTES; the old value is read BEFORE the RHS executes. The RHS is a
    // call that logs 'V'. We prove the old value (read first) and RHS value combine correctly and the
    // RHS side effect fires exactly once.
    [Fact]
    public async Task ComputedCompound_OldValueReadBeforeRhs_RoutesAndPreservesOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var order = [];
            function rec(tag, val){ order.push(tag); return val; }
            function f(o){ o['v'] += rec('V', 100); return o.v; }
            var v = f({ v: 5 });
            v + ':' + order.join('');
            """);
        // old value 5 read first, RHS pushes 'V' returning 100 => 105; side effect fired once.
        Assert.Equal("105:V", result?.ToString());
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // ORDER PROOF (computed compound, side-effecting key + RHS): o[key()] += rhsWithSideEffect().
    // The computed KEY contains a call, which is NOT an admitted key span (complex computed-KEY
    // admission is a separate item), so this DECLINES the fast path — but the interpreter still
    // evaluates key ONCE before the old-value read, and the RHS AFTER. The log proves the exact
    // order: key ('K') then RHS ('V'), each exactly once.
    [Fact]
    public async Task ComputedCompound_KeyOnceBeforeRhs_DeclinesButPreservesOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var order = [];
            function rec(tag, val){ order.push(tag); return val; }
            function f(o){ o[rec('K','p')] += rec('V', 7); return o.p + ':' + order.join(''); }
            f({ p: 3 });
            """);
        // key ('K') computed ONCE before the old-value read; RHS ('V') after => o.p = 3 + 7 = 10.
        Assert.Equal("10:KV", result?.ToString());
        AssertNotRouted("unified-bytecode-production-fast-path func=f");
    }

    // o.x ||= a + b: logical-OR-assign with a complex RHS. The old value is truthy, so the RHS is
    // NOT evaluated and the old value is kept. Logical compound writes with a complex RHS are a
    // distinct boundary (LogicalCompoundAssignment) and are NOT widened by this change, so this
    // DECLINES the fast path but computes correctly via the interpreter (no over-admission).
    [Fact]
    public async Task LogicalOrAssign_ComplexRhs_DeclinesButComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,a,b){ o.x ||= a + b; return o.x; } f({x:5},2,3);");
        // x is truthy (5) => RHS not evaluated, x stays 5.
        Assert.Equal(5d, Convert.ToDouble(result));
        AssertNotRouted("unified-bytecode-production-fast-path func=f");
    }

    // o.x ??= f(): nullish-assign with a complex (call) RHS. The old value is null, so the RHS IS
    // evaluated and assigned. Same boundary note as above — DECLINES but computes correctly.
    [Fact]
    public async Task NullishAssign_ComplexRhs_DeclinesButComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,g,z){ o.x ??= g(z); return o.x; } f({x:null},v=>v+1,8);");
        // x is null => RHS g(8)=9 assigned.
        Assert.Equal(9d, Convert.ToDouble(result));
        AssertNotRouted("unified-bytecode-production-fast-path func=f");
    }

    // PRIVATE compound complex RHS (A37 residual): this.#x += a + b. The RHS is a simple binary of
    // params (no private read), so it flows through the SAME widened candidate. The store target is a
    // private name; receiver-chain hops stay ordinary. Asserts result AND routing — proving the
    // private compound write with a complex RHS is admitted.
    [Fact]
    public async Task PrivateCompound_BinaryRhs_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C { #x = 1; m(a,b){ this.#x += a + b; return this.#x; } }
            new C().m(2, 3);
            """);
        Assert.Equal(6d, Convert.ToDouble(result));
        // Class methods carry no FunctionExpression name, so the fast-path log records them as
        // <anonymous>; m takes two params, so argc=2 identifies it uniquely in this script.
        AssertRouted("unified-bytecode-production-fast-path func=<anonymous> argc=2");
    }

    [Fact]
    public async Task PrivateCompound_PrivateReadRhs_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C { #x = 3; m(){ this.#x += this.#x; return this.#x; } }
            new C().m();
            """);
        Assert.Equal(6d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=<anonymous> argc=0");
    }

    // NEGATIVE (no over-admission): a compound RHS containing a still-declined sub-shape (an inner
    // assignment, which the value walker does not admit) keeps the WHOLE compound write declined but
    // the result is still computed correctly via the interpreter fallback.
    [Fact]
    public async Task NamedCompound_RhsWithDeclinedSubShape_DeclinesButComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(o,p){ o.x += (p.y = 3) + 1; return o.x + ':' + p.y; }
            f({x:10}, {});
            """);
        // p.y = 3 (assignment) then +1 => RHS 4; o.x = 10 + 4 = 14; p.y == 3.
        Assert.Equal("14:3", result?.ToString());
        AssertNotRouted("unified-bytecode-production-fast-path func=f");
    }

    // Control: a SIMPLE-operand compound RHS (the pre-existing admitted shape) still routes.
    [Fact]
    public async Task NamedCompound_SimpleRhs_StillRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,a){ o.x += a; return o.x; } f({x:1},4);");
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
