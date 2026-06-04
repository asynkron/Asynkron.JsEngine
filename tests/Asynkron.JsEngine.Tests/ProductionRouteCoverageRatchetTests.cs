using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     STANDING COVERAGE RATCHET (burn-down Phase D / D5 seed). A canary corpus of shapes that are already
///     admitted to the production unified-bytecode VM (sync <c>Execute</c>, the script route, and the
///     resumable <c>ExecuteResumable</c> generator/async routes). Each entry asserts the shape still emits
///     its production/resumable fast-path log — i.e. it has NOT regressed back to an interpreter fallback.
///
///     This is a tripwire: when a shape is newly admitted, ADD it here; never remove an entry without a
///     deliberate, documented reason. A red test here means a previously-admitted shape silently fell back
///     (a coverage regression) — the class of bug a single per-PR test would miss because it only guards its
///     own slice. It complements (does not replace) the per-shape correctness tests and the
///     allowlist⊆ExecuteResumable drift guard.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class ProductionRouteCoverageRatchetTests(ITestOutputHelper output) : InternalTestBase(output)
{
    // Sync function route — `unified-bytecode-production-fast-path func=<name>`.
    [Theory]
    [InlineData("function f(a,b){ return a+b; } f(1,2);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o){ return o.a.b; } f({a:{b:1}});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,v){ o.a=v; return o.a; } f({},9);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o){ o.a++; return o.a; } f({a:1});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o){ return delete o.a; } f({a:1});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o){ return o?.a; } f(null);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,c){ o[c?'a':'b']=1; return o.a; } f({},true);", "unified-bytecode-production-fast-path func=f")]
    // A30: optional-computed-start member call (o?.[k]()) and double-optional named-then-computed
    // call (a?.b?.[k]()) — short-circuiting variants that must keep routing through the sync VM.
    [InlineData("function f(o,k){ return o?.[k](); } f(null,'m');", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,k){ var b={m(){return 7;}}; return o?.[k](); } f({m(){return 7;}},'m');", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(a,k){ return a?.b?.[k](); } f(null,'c');", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(a,k){ return a?.b?.[k](); } f({b:{c(){return 9;}}},'c');", "unified-bytecode-production-fast-path func=f")]
    // A33: array spread with a NON-simple source — bare identifier call ([...g()]),
    // named property read off a call ([...g().items]), and a mix with normal elements.
    [InlineData("function f(g){ return [...g()]; } f(()=>[1,2,3]);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(g){ return [...g().items]; } f(()=>({items:[4,5]}));", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(a,g,c){ return [a, ...g(), c]; } f(0,()=>[1,2],3);", "unified-bytecode-production-fast-path func=f")]
    // A34: object spread with a NON-simple source — bare identifier call ({...g()}),
    // named property read off a call ({...g().inner}), and a mix with normal properties.
    [InlineData("function f(g){ return {...g()}; } f(()=>({a:1,b:2}));", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(g){ return {...g().inner}; } f(()=>({inner:{x:4,y:5}}));", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(a,g){ return { a, ...g(), c: 3 }; } f(0,()=>({b:2}));", "unified-bytecode-production-fast-path func=f")]
    // A23: property UPDATE with a computed receiver prefix — computed-update terminal
    // (box[k1].child[k2]++), named-update terminal (box[k1].child++), and prefix decrement.
    [InlineData("function f(o,k1,k2){ return o[k1].child[k2]++; } f({a:{child:{x:1}}},'a','x');", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,k1){ return o[k1].child++; } f({a:{child:1}},'a');", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,k1,k2){ return --o[k1].child[k2]; } f({a:{child:{x:5}}},'a','x');", "unified-bytecode-production-fast-path func=f")]
    // A11: calls with COMPLEX arguments — nested call (g(h(x))), binary with a nested-call operand
    // (g(a + h(b))), member call argument (g(o.m(x))), and computed-member call argument
    // (g(o[k](x))). The argument expressions lower onto the existing operand stack with strict
    // left-to-right evaluation preserved.
    [InlineData("function f(g,h,x){ return g(h(x)); } f(a=>a+1,b=>b*10,5);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(g,h,a,b){ return g(a + h(b)); } f(x=>x,h=>h*2,3,4);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(g,o,x){ return g(o.m(x)); } f(v=>v+1, {m(n){return n*3;}}, 4);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(g,o,k,x){ return g(o[k](x)); } f(v=>v+1, {m(n){return n*5;}}, 'm', 2);", "unified-bytecode-production-fast-path func=f")]
    // A1 (closure Stage 0): FLAT multi-statement closures that capture an enclosing activation
    // binding now route through the production VM — captured names lower to dynamic-identifier ops
    // over the threaded closure environment. Captured READ (config), captured WRITE (counter n++),
    // and a captured object property write. Nested-lexical-scope closures (shadowing hazard) are
    // intentionally NOT admitted yet (HasOnlyRootFlatSlotMappings guard) — no ratchet entry for them.
    [InlineData("function mk(){ let n=0; function inc(){ n++; return n; } return inc; } var f=mk(); f(); f();", "unified-bytecode-production-fast-path func=inc")]
    [InlineData("function mk(c){ function read(){ return c.x + c.y; } return read; } mk({x:1,y:2})();", "unified-bytecode-production-fast-path func=read")]
    [InlineData("function mk(o){ return function set(){ o.value=43; return o.value; }; } mk({value:42})();", "unified-bytecode-production-fast-path func=set")]
    // Sync generator route — `unified-bytecode-resumable-generator-fast-path func=<name>`.
    [InlineData("function* g(o){ yield o.a; yield -o.b; } var it=g({a:1,b:2}); it.next(); it.next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("function* g(o){ o.x=1; yield o.x; } g({}).next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("function* g(){ yield {a:1}; } g().next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("function* g(){ yield [1,2]; } g().next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("function* g(){ yield new.target; } g().next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("function* g(){ yield /ab+c/gi; } g().next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    public async Task AdmittedShape_StillRoutesThroughProduction(string source, string expectedLog)
    {
        await using var engine = CreateEngine();
        await engine.Evaluate(source);
        AssertRouted(expectedLog);
    }

    // Top-level script route — `unified-bytecode-production-fast-path script`.
    [Fact]
    public async Task ScriptLoop_StillRoutesThroughProductionScriptRoute()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("var s=0; for (var i=0;i<3;i++){ s+=i; } s;");
        AssertRouted("unified-bytecode-production-fast-path script");
    }

    // Resumable async route — `unified-bytecode-resumable-async-fast-path func=<name>`.
    [Fact(Timeout = 5000)]
    public async Task AsyncAwait_StillRoutesThroughResumableAsync()
    {
        await using var engine = CreateEngine();
        await engine.EvaluateAndAwait("""
            var r = 0;
            async function run(o){ await o.gate; return o.v; }
            run({ gate: Promise.resolve(0), v: 7 }).then(x => r = x);
            r;
            """);
        AssertRouted("unified-bytecode-resumable-async-fast-path func=run");
    }

    private void AssertRouted(string expectedLog)
    {
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(expectedLog, StringComparison.Ordinal));
    }
}
