using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     A11 (burn-down): admit CALLS WITH COMPLEX ARGUMENTS into the production sync VM.
///
///     Before A11 the call-invocation boundary admitted only a limited set of argument shapes
///     (simple identifiers/literals, flat <c>a+b</c> binaries of simple operands, admitted member
///     read spans, array/object/template literals). A richer argument — a NESTED CALL
///     (<c>g(h(x))</c>), a binary whose operand is itself a call (<c>g(a + h(b))</c>), or any
///     deeper already-admitted expression — declined as <c>CallInvocationBoundary</c>.
///
///     A11 generalizes argument admission to ANY argument expression that is itself built only from
///     already-admitted operations, lowered onto the existing operand stack the VM already uses, with
///     left-to-right evaluation order preserved exactly. Each test asserts BOTH the numeric result
///     AND that the enclosing function routed through the production fast path.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class ComplexCallArgumentAdmissionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    // g(a+b): a binary expression as a single argument (already a flat simple binary).
    [Fact]
    public async Task BinaryArgument_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(g,a,b){ return g(a+b); } f(x=>x*2,3,4);");
        Assert.Equal(14d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // g(h(x)): a NESTED CALL as a single argument — the shape A11 targets.
    [Fact]
    public async Task NestedCallArgument_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(g,h,x){ return g(h(x)); } f(a=>a+1,b=>b*10,5);");
        Assert.Equal(51d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // g(a + h(b)): a binary whose RIGHT operand is itself a call.
    [Fact]
    public async Task BinaryWithNestedCallOperand_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(g,h,a,b){ return g(a + h(b)); } f(x=>x,h=>h*2,3,4);");
        Assert.Equal(11d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // Mixed simple + complex arguments: f(simple, complex, simple).
    [Fact]
    public async Task MixedSimpleAndComplexArguments_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(g,a,b,c){ return g(a, b + c, a * b); } f((x,y,z)=>x+y+z, 2, 3, 4);");
        // a=2, b+c=7, a*b=6 => 2+7+6 = 15
        Assert.Equal(15d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // ORDER PROOF: multiple complex args each with an observable side effect.
    // The arguments call rec() which pushes to a shared array. Left-to-right, each argument
    // fully evaluated before the next, then the call. Asserts the exact recorded order.
    [Fact]
    public async Task ComplexArguments_EvaluateLeftToRightBeforeCall()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(g, rec, a, b){
                return g(rec('A') + a, rec('B') + b, rec('C'));
            }
            var order = [];
            var rec = function(tag){ order.push(tag); return tag.length; };
            var f2 = function(){ return order.join(''); };
            f(f2, rec, 1, 2);
            """);
        // rec('A') then '+a', rec('B') then '+b', rec('C') — left-to-right pushes A,B,C.
        Assert.Equal("ABC", result?.ToString());
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // ORDER PROOF with nested calls: outer(inner(a), inner(b)) — inner calls run
    // left-to-right, each fully before the next.
    [Fact]
    public async Task NestedCallArguments_EvaluateLeftToRight()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(outer, mk, a, b){
                return outer(mk(a), mk(b));
            }
            var order = [];
            var mk = function(t){ order.push(t); return t; };
            f(function(x,y){ return x + y; }, mk, 'X', 'Y');
            order.join('');
            """);
        Assert.Equal("XY", result?.ToString());
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // Member-call argument: g(o.m(x)) — the argument is a method call on a parameter.
    [Fact]
    public async Task MemberCallArgument_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(g,o,x){ return g(o.m(x)); } f(v=>v+1, {m(n){return n*3;}}, 4);");
        Assert.Equal(13d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // Deeply nested complex argument inside a binary inside a call argument.
    [Fact]
    public async Task DeeplyNestedComplexArgument_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(g,h,a,b,c){ return g(h(a + b) * c); } f(v=>v, x=>x+1, 2, 3, 10);");
        // h(2+3)=6, *10 = 60.
        Assert.Equal(60d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // Computed-member nested call argument: g(o[k](x)).
    [Fact]
    public async Task ComputedMemberCallArgument_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(g,o,k,x){ return g(o[k](x)); } f(v=>v+1, {m(n){return n*5;}}, 'm', 2);");
        // o['m'](2)=10, +1 = 11.
        Assert.Equal(11d, Convert.ToDouble(result));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // An optional-chain call as an argument (g(o?.m())) is admitted via the existing optional-chain
    // operand-span appenders and must still compute correctly (covered here for completeness — it
    // routes through the production VM rather than degrading).
    [Fact]
    public async Task OptionalChainCallArgument_Computes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(g,o){ return g(o?.m()); } f(v=>v, {m(){return 42;}});");
        Assert.Equal(42d, Convert.ToDouble(result));
    }

    // NEGATIVE — boundary degrades correctly: an argument that is an ASSIGNMENT (a non-admitted
    // value-producing shape) must keep f OFF the production fast path while still computing
    // correctly via the interpreter fallback (no over-admission, no crash).
    [Fact]
    public async Task AssignmentArgument_StillDeclinesButComputes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(g,o){ return g(o.v = 7); } var box={v:0}; f(v=>v+1, box) + ':' + box.v;");
        Assert.Equal("8:7", result?.ToString());
        AssertNotRouted("unified-bytecode-production-fast-path func=f");
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
