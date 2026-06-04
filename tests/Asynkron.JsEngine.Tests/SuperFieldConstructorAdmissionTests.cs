using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Derived-constructor (super-call) and instance-field-initializer class constructors now route through
///     the production sync VM — an emergent, VERIFIED consequence of admitting complex property-write RHS
///     (the body-shape blocker that previously kept these declined). This battery locks the correctness of
///     that admission across super/field edge cases. Super-PROPERTY access (`super.m()`) and private names
///     still correctly decline (A27 / PrivateFieldDependency) — guarded elsewhere.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class SuperFieldConstructorAdmissionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private bool Routed(string fn) =>
        CurrentLogger!.Collector.Snapshot().Any(r =>
            r.Message.Contains("unified-bytecode-production-fast-path func=" + fn, StringComparison.Ordinal));

    [Fact]
    public async Task DerivedSuperWithField_RoutesAndIsCorrect()
    {
        await using var e = CreateEngine();
        var r = await e.Evaluate("class B{constructor(x){this.x=x;}} class D extends B{ z=3; constructor(){super(5); this.w=this.x+this.z;} } new D().w;");
        Assert.Equal(8d, r);
        Assert.True(Routed("D"));
    }

    [Fact]
    public async Task SuperWithComplexArg_RoutesAndIsCorrect()
    {
        await using var e = CreateEngine();
        var r = await e.Evaluate("class B{constructor(x){this.x=x;}} class D extends B{constructor(a,b){super(a+b);}} new D(2,3).x;");
        Assert.Equal(5d, r);
        Assert.True(Routed("D"));
    }

    [Fact]
    public async Task FieldWithCallInit_RoutesAndIsCorrect()
    {
        await using var e = CreateEngine();
        var r = await e.Evaluate("function init(){return 9;} class C{f=init(); constructor(){this.g=this.f;}} new C().g;");
        Assert.Equal(9d, r);
        Assert.True(Routed("C"));
    }

    [Fact]
    public async Task MultipleFields_RoutesAndIsCorrect()
    {
        await using var e = CreateEngine();
        var r = await e.Evaluate("class C{a=1;b=2; constructor(){this.s=this.a+this.b;}} new C().s;");
        Assert.Equal(3d, r);
        Assert.True(Routed("C"));
    }

    [Fact]
    public async Task FieldReferencingEarlierField_RoutesAndIsCorrect()
    {
        await using var e = CreateEngine();
        var r = await e.Evaluate("class C{a=1; b=this.a+1; constructor(){this.c=this.b;}} new C().c;");
        Assert.Equal(2d, r);
        Assert.True(Routed("C"));
    }

    [Fact]
    public async Task FieldInitializationOrder_IsPreserved()
    {
        await using var e = CreateEngine();
        var r = await e.Evaluate("var log=''; class C{ a=(log+='a'); b=(log+='b'); constructor(){log+='c';} } new C(); log;");
        Assert.Equal("abc", r);
    }

    [Fact]
    public async Task ThisBeforeSuper_ThrowsReferenceError()
    {
        await using var e = CreateEngine();
        // The classic derived-ctor TDZ trap: reading/writing `this` before super() must throw ReferenceError —
        // the admitted path must enforce it, not silently miscompile.
        var r = await e.Evaluate("class B{} class D extends B{constructor(){ this.x=1; super(); }} try{ new D(); 'NO-THROW'; }catch(err){ err.constructor.name; }");
        Assert.Equal("ReferenceError", r);
    }

    [Fact]
    public async Task SuperPropertyAccessInCtor_StillDeclines()
    {
        await using var e = CreateEngine();
        // super.m() (super-PROPERTY access, A27) still correctly declines to the IR runner; result correct.
        var r = await e.Evaluate("class B{m(){return 4;}} class D extends B{constructor(){super(); this.v=super.m();}} new D().v;");
        Assert.Equal(4d, r);
        Assert.False(Routed("D"), "super-property access (A27) still declines");
    }
}
