using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     A35 (burn-down): COMPLEX object-literal members admitted to the production sync VM.
///     Two gaps closed:
///       1. a property VALUE that is a bare-identifier call — `{x: g()}`, `{a: g(arg)}`
///          (the member-call value form `{a: o.m()}` already routed);
///       2. a shorthand-method / accessor member followed by a later member — most importantly a
///          trailing object-spread `{m(){}, ...o}` / `{get a(){}, ...o}` — which previously
///          terminated the literal-span measurement at the method/accessor define.
///     Correctness AND routing are asserted; property-definition evaluation order is proven.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class A35ComplexObjectLiteralTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private bool RoutedFunction(string func)
    {
        foreach (var rec in CurrentLogger!.Collector.Snapshot())
        {
            if (rec.Message.Contains(
                    $"unified-bytecode-production-fast-path func={func}",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // --- Gap 1: bare-identifier call as a property value. ---

    [Fact]
    public async Task ValueIsIdentifierCall_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("function f(g,z){ return {x: g(z)}.x; } f(v=>v+1,5);");
        Assert.Equal(6d, r);
        Assert.True(RoutedFunction("f"));
    }

    [Fact]
    public async Task ValueIsZeroArgIdentifierCall_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("function f(g){ var o={a: g(), b: 2}; return o.a + o.b; } f(()=>10);");
        Assert.Equal(12d, r);
        Assert.True(RoutedFunction("f"));
    }

    [Fact]
    public async Task ComputedKeyCallAndValueCall_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("function f(g){ var o={[g()]: g()}; return o.k; } f(()=>'k');");
        Assert.Equal("k", r);
        Assert.True(RoutedFunction("f"));
    }

    // --- Gap 2: trailing spread after method / accessor members. ---

    [Fact]
    public async Task SpreadAfterMethod_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("function f(o){ var x={ m(){return 1;}, ...o }; return x.m() + x.z; } f({z:9});");
        Assert.Equal(10d, r);
        Assert.True(RoutedFunction("f"));
    }

    [Fact]
    public async Task SpreadAfterGetter_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("function f(o){ var x={ get a(){return 1;}, ...o }; return x.a + x.z; } f({z:9});");
        Assert.Equal(10d, r);
        Assert.True(RoutedFunction("f"));
    }

    [Fact]
    public async Task AllMemberKinds_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate(
            "function f(k,o){ var x={ x:1, [k]:2, m(){return 3;}, get g(){return 4;}, set s(v){}, ...o }; " +
            "return x.x + x.y + x.m() + x.g + x.z; } f('y',{z:9});");
        Assert.Equal(19d, r); // 1 + 2 + 3 + 4 + 9
        Assert.True(RoutedFunction("f"));
    }

    // --- Property-definition evaluation order: computed keys + values run left-to-right. ---

    [Fact]
    public async Task PropertyDefinitionOrder_LeftToRight_RoutesAndPreservesOrder()
    {
        await using var engine = CreateEngine();
        // log records every key/value side effect in source order. Spec: for each member, the computed
        // key is evaluated, then the value, before moving to the next member.
        var r = await engine.Evaluate(
            "function f(log){ " +
            "  function k(n){ log.push('k'+n); return n; } " +
            "  function v(n){ log.push('v'+n); return n; } " +
            "  var o = { [k('a')]: v(1), [k('b')]: v(2) }; " +
            "  return log.join(','); } " +
            "f([]);");
        Assert.Equal("ka,v1,kb,v2", r);
        Assert.True(RoutedFunction("f"));
    }

    [Fact]
    public async Task ValueCallOrder_LeftToRight_RoutesAndPreservesOrder()
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate(
            "function f(log){ " +
            "  function v(n){ log.push(n); return n; } " +
            "  var o = { a: v(1), b: v(2), c: v(3) }; " +
            "  return log.join(','); } " +
            "f([]);");
        Assert.Equal("1,2,3", r);
        Assert.True(RoutedFunction("f"));
    }

    // --- __proto__ literal-key special case must keep setting the prototype, not an own property. ---

    [Fact]
    public async Task ProtoLiteralKey_SetsPrototype_RoutesAndComputes()
    {
        // f BUILDS the `{ __proto__: base, a: 1 }` literal and returns a simple member read off it;
        // greet() resolves through the prototype chain. The literal build is the routed shape — kept
        // on a simple return so the routing assertion isolates the object-literal admission. (A complex
        // return like `o.greet() + (...)` declines for return-shape reasons unrelated to A35.)
        await using var engine = CreateEngine();
        var greet = await engine.Evaluate(
            "function f(base){ var o={ __proto__: base, a: 1 }; return o.greet(); } f({greet(){return 'hi';}});");
        Assert.Equal("hi", greet);
        Assert.True(RoutedFunction("f"));

        await using var ownCheck = CreateEngine();
        var ownProp = await ownCheck.Evaluate(
            "function f(base){ var o={ __proto__: base, a: 1 }; return o.hasOwnProperty('__proto__'); } f({});");
        Assert.Equal(false, ownProp); // __proto__ set the prototype, did NOT become an own property
    }

    [Fact]
    public async Task ProtoLiteralKeyNull_SetsNullPrototype_RoutesAndComputes()
    {
        await using var engine = CreateEngine();
        var a = await engine.Evaluate("function f(){ var o={ __proto__: null, a:1 }; return o.a; } f();");
        Assert.Equal(1d, a);
        Assert.True(RoutedFunction("f"));

        await using var protoCheck = CreateEngine();
        var proto = await protoCheck.Evaluate(
            "function f(){ var o={ __proto__: null, a:1 }; return Object.getPrototypeOf(o); } f();");
        Assert.Null(proto); // prototype is genuinely null, not Object.prototype
    }

    // --- NEGATIVE: a member with a still-declined sub-shape keeps the WHOLE literal declined. ---

    [Fact]
    public async Task ValueWithDeclinedSubShape_StillDeclines_ButComputesCorrectly()
    {
        await using var engine = CreateEngine();
        // A direct-eval call as a value is a declined sub-shape (eval is context-sensitive); the literal
        // must NOT route, and must still compute correctly.
        var r = await engine.Evaluate("function f(){ var o={x: eval('1+2')}; return o.x; } f();");
        Assert.Equal(3d, r);
        Assert.False(RoutedFunction("f"), "object literal with a direct-eval value must stay declined");
    }
}
