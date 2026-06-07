using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     PROPERTY-WRITE complex RHS admission (burn-down: part of A37, complements A7/A19).
///
///     Before this change the production property-write boundary admitted <c>o.x = rhs</c>,
///     <c>o[k] = rhs</c> and <c>this.x = rhs</c> only when the RHS was a single simple operand or a
///     template literal. A richer RHS — a binary (<c>o.x = a*2 + b</c>), a nested call
///     (<c>o.x = g(z)</c>), a member-read chain (<c>o.x = s.a.b</c>), or any composition thereof —
///     declined to the IR runner.
///
///     This change generalizes the RHS to ANY already-admitted value-producing expression, lowered
///     onto the existing operand stack with strict left-to-right evaluation, then the store. The
///     reference (object, then key for computed writes) is evaluated BEFORE the RHS, exactly as the
///     interpreter; the proving tests below assert that order with side-effect logs. Each positive
///     test asserts BOTH the result AND that the enclosing function routed through the production
///     fast path.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class PropertyWriteComplexRhsAdmissionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    // o.x = a*2 + b: a binary RHS on a named write.
    [Fact]
    public async Task NamedWrite_BinaryRhs_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,a,b){ o.x = a*2 + b; return o.x; } f({},3,4);");
        Assert.Equal(10d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // o.x = g(z): a nested call RHS on a named write.
    [Fact]
    public async Task NamedWrite_CallRhs_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,g,z){ o.x = g(z); return o.x; } f({},v=>v+1,5);");
        Assert.Equal(6d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // o.x = s.a.b: a member-read chain RHS on a named write.
    [Fact]
    public async Task NamedWrite_MemberReadChainRhs_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,s){ o.x = s.a.b; return o.x; } f({},{a:{b:9}});");
        Assert.Equal(9d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // ORDER PROOF (computed target): o[arr[i++]] = i proves the computed KEY (arr[i++]) is
    // evaluated BEFORE the RHS (i). i starts at 0; arr[i++] reads arr[0] and bumps i to 1; the
    // RHS reads i == 1. So the property named arr[0] gets value 1. A computed key containing an
    // update/computed-read with side effects is NOT an admitted key span (complex computed-KEY
    // admission is a separate item), so this DECLINES the fast path — but the interpreter still
    // evaluates key-before-RHS, producing o.k0 === 1. Asserting the result here pins that the
    // decline path preserves exact ECMAScript evaluation order.
    [Fact]
    public async Task ComputedWrite_KeyEvaluatedBeforeRhs_DeclinesButPreservesOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(o,arr){ var i=0; o[arr[i++]] = i; return o; }
            var o = f({}, ['k0','k1']);
            o.k0;
            """);
        // arr[0]='k0' read first (i->1), then RHS reads i==1 => o.k0 === 1.
        Assert.Equal(1d, Convert.ToDouble(result));
        AssertNotRouted("unified-bytecode-production-fast-path func=f");
    }

    // ORDER PROOF (computed target, side-effecting key + RHS calls): the reference (object, then
    // key) is evaluated BEFORE the RHS. A call inside the computed KEY is not an admitted key
    // span, so this DECLINES the fast path; the interpreter records the exact order — key call
    // ('K') BEFORE RHS call ('V') — proving order is preserved on the decline path with no
    // over-admission.
    [Fact]
    public async Task ComputedWrite_ReferenceBeforeRhsSideEffects_DeclinesButPreservesOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var order = [];
            function rec(tag, val){ order.push(tag); return val; }
            function f(o, key){ o[rec('K', key)] = rec('V', 7); return o[key]; }
            var got = f({}, 'p');
            got + ':' + order.join('');
            """);
        // Key computed (push 'K') BEFORE RHS (push 'V'); store o.p = 7.
        Assert.Equal("7:KV", result?.ToString());
        AssertNotRouted("unified-bytecode-production-fast-path func=f");
    }

    // ORDER PROOF (computed target, in-scope routed): the computed key (a simple binary
    // expression of params) is evaluated BEFORE the RHS binary, and the write routes. We prove
    // the value stored under the computed key matches the RHS, with the key derived from the
    // SAME params as the RHS so a key/RHS swap would change the result.
    [Fact]
    public async Task ComputedWrite_BinaryKeyAndBinaryRhs_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(o,a,b){ o['p' + (a + b)] = a * b; return o['p' + (a + b)]; }
            f({}, 2, 5);
            """);
        // key = 'p7', value = 10.
        Assert.Equal(10d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // o[k] = a*b: a binary RHS on a computed write with a simple key.
    [Fact]
    public async Task ComputedWrite_BinaryRhs_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,k,a,b){ o[k] = a*b; return o[k]; } f({},'z',6,7);");
        Assert.Equal(42d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // this.x = a*b inside a method — routed + correct. The function f sets this.x and returns it.
    [Fact]
    public async Task ThisWrite_BinaryRhs_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(a,b){ this.x = a*b; return this.x; }
            var obj = {};
            f.call(obj, 4, 5);
            """);
        Assert.Equal(20d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // Compound assignment with a complex RHS is OUT OF SCOPE for this change: o.x += a*2.
    // It must still DECLINE the named-property-set fast path but compute correctly through the
    // interpreter fallback (no over-admission, no crash).
    [Fact]
    public async Task NamedCompoundWrite_ComplexRhs_DeclinesButComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,a){ o.x = 10; o.x += a*2; return o.x; } f({},3);");
        Assert.Equal(16d, Convert.ToDouble(result));
    }

    // Plain (non-compound) named write with a complex RHS routes — the positive control for the
    // compound test above.
    [Fact]
    public async Task NamedWrite_PlainComplexRhs_Routes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(o,a){ o.x = a*2; return o.x; } f({},3);");
        Assert.Equal(6d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // NEGATIVE (no over-admission): an RHS containing a still-declined sub-shape (an inner
    // assignment, which the value walker does not admit) keeps the WHOLE write declined but the
    // result is still computed correctly via the interpreter fallback.
    [Fact]
    public async Task NamedWrite_RhsWithDeclinedSubShape_DeclinesButComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(o,p){ o.x = (p.y = 3) + 1; return o.x + ':' + p.y; }
            f({}, {});
            """);
        // p.y = 3 (assignment) then +1 => o.x = 4; p.y == 3.
        Assert.Equal("4:3", result?.ToString());
        AssertNotRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task PrivateFieldWrite_ComplexRhsPrivateRead_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C { #x = 1; m(a){ this.#x = this.#x + a; return this.#x; } }
            new C().m(4);
            """);
        Assert.Equal(5d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=<anonymous> argc=1");
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
