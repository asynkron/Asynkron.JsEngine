using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     A12 (burn-down): admit MEMBER/COMPUTED call-targets OUTSIDE the first invocation boundary
///     into the production sync VM.
///
///     A call whose TARGET is a member access on the RESULT of an earlier call — a chained method
///     call past the first invocation boundary (<c>a.b().c()</c>, <c>o.m().n()</c>,
///     <c>arr[i]().x()</c>) — previously declined (<c>CallDependency: Member call-target preparation
///     is outside the first production invocation boundary</c> for computed chains, or
///     <c>CallInvocationBoundary: Unsupported nested call in complex call argument</c> for named
///     chains mis-split by the specialized first-boundary receiver-chain appenders).
///
///     A12 admits these by routing the chained shape to the general per-op expression loop, which
///     lowers the entire receiver chain in source order onto the operand stack the VM maintains: the
///     inner call's result is left as the next call's receiver, and the final call applies with the
///     correct <c>this</c> = that receiver object. Each test asserts BOTH the result AND that the
///     enclosing function routed through the production fast path.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class MemberCallTargetBoundaryAdmissionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    // o.a().b(): named call-target on the result of an earlier named call (both zero-arg).
    [Fact]
    public async Task NamedChainedCall_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(o){ return o.a().b(); } f({a(){return {b(){return 7;}};}});");
        Assert.Equal(7d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // o.get().m(2): the inner call is zero-arg, the final call takes an argument.
    [Fact]
    public async Task NamedChainedCallWithArgument_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(o){ return o.get().m(2); } f({get(){return {m(x){return x*3;}};}});");
        Assert.Equal(6d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // o.a()[k](): computed call-target on the result of an earlier named call.
    [Fact]
    public async Task ComputedChainedCall_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(o,k){ return o.a()[k](); } f({a(){return {z(){return 9;}};}}, 'z');");
        Assert.Equal(9d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // THIS-BINDING: the final call's `this` must be the IMMEDIATE receiver (the result of o.a()),
    // not undefined. The inner method returns an object carrying its own state; the final method
    // reads `this.value` to prove the receiver record is correct.
    [Fact]
    public async Task ChainedCall_FinalThisIsImmediateReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(o){ return o.make(10).read(); }
            f({ make(n){ return { value: n + 5, read(){ return this.value; } }; } });
            """);
        // make(10) -> { value: 15, read }; read() with this = that object -> 15.
        Assert.Equal(15d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // THIS-BINDING (computed final): same, but the final call is computed o.make(10)[k]().
    [Fact]
    public async Task ComputedChainedCall_FinalThisIsImmediateReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(o,k){ return o.make(3)[k](); }
            f({ make(n){ return { value: n * 4, read(){ return this.value; } }; } }, 'read');
            """);
        Assert.Equal(12d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // EVAL ORDER: chained calls with side effects execute left-to-right. Each hop logs a tag before
    // returning the next link; the recorded order proves the receiver chain evaluates outer-to-inner
    // in source order (a, then b, then c).
    [Fact]
    public async Task ChainedCall_EvaluatesLeftToRight()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(o){ return o.a().b().c(); }
            var order = [];
            var leaf = { c(){ order.push('c'); return order.join(''); } };
            var mid = { b(){ order.push('b'); return leaf; } };
            var root = { a(){ order.push('a'); return mid; } };
            f(root);
            """);
        Assert.Equal("abc", result?.ToString());
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // Deeper named chain a.b().c().d() to prove the chaining generalizes past two hops.
    [Fact]
    public async Task DeepNamedChainedCall_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(o){ return o.a().b().c(); }
            f({ a(){ return { b(){ return { c(){ return 42; } }; } }; } });
            """);
        Assert.Equal(42d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // STRICT SELF-RECURSIVE MEMBER TAIL CALL must NOT StackOverflow at moderate depth. The VM has no
    // TCO; this shape recurses on `this.step(n-1)` in tail position. It is a FLAT member call (not a
    // chained call), so A12 does not change its routing — this guard confirms the chained-call
    // admission did not regress flat self-recursive member tail calls into an early crash, and that
    // bounded recursion terminates correctly exactly as on baseline.
    [Fact]
    public async Task SelfRecursiveMemberTailCall_DoesNotStackOverflow()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            "use strict";
            var counter = {
                step(n){ if (n <= 0) { return 0; } return this.step(n - 1); }
            };
            counter.step(20);
            """);
        Assert.Equal(0d, Convert.ToDouble(result));
    }

    private void AssertRouted(string expectedLog)
    {
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(expectedLog, StringComparison.Ordinal));
    }
}
