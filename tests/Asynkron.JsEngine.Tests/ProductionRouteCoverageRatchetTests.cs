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
    // Free-identifier (DynamicLookupDependency) family — a free name escaping the activation's slots
    // resolves as a dynamic-identifier op walking the threaded environment chain.
    // A13: free READ of a declared global (LoadDynamicIdentifier).
    [InlineData("var g=7; function f(){ return g; } f();", "unified-bytecode-production-fast-path func=f")]
    // A15: typeof of a free identifier (TypeOfDynamicIdentifier; never throws for an unbound name).
    [InlineData("var g=7; function f(){ return typeof g; } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(){ return typeof undeclaredX; } f();", "unified-bytecode-production-fast-path func=f")]
    // A22: identifier UPDATE on a free name (UpdateDynamicIdentifier) — postfix and prefix.
    [InlineData("var c=5; function f(){ return c++; } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("var c=5; function f(){ return ++c; } f();", "unified-bytecode-production-fast-path func=f")]
    // A24: delete of a free identifier (DeleteDynamicIdentifier).
    [InlineData("var g=1; function f(){ return delete g; } f();", "unified-bytecode-production-fast-path func=f")]
    // A16: computed-property delete with a FREE identifier as the key (free computed delete key).
    [InlineData("var k='a'; function f(o){ return delete o[k]; } f({a:1});", "unified-bytecode-production-fast-path func=f")]
    // A14: free identifier STORE (StoreDynamicIdentifier) — sloppy-mode global creation.
    [InlineData("function f(){ createdGlobalRatchet=99; return createdGlobalRatchet; } f();", "unified-bytecode-production-fast-path func=f")]
    // A1 (closure Stage 0): FLAT multi-statement closures that capture an enclosing activation
    // binding now route through the production VM — captured names lower to dynamic-identifier ops
    // over the threaded closure environment. Captured READ (config), captured WRITE (counter n++),
    // and a captured object property write. Nested-lexical-scope closures (shadowing hazard) are
    // intentionally NOT admitted yet (HasOnlyRootFlatSlotMappings guard) — no ratchet entry for them.
    [InlineData("function mk(){ let n=0; function inc(){ n++; return n; } return inc; } var f=mk(); f(); f();", "unified-bytecode-production-fast-path func=inc")]
    [InlineData("function mk(c){ function read(){ return c.x + c.y; } return read; } mk({x:1,y:2})();", "unified-bytecode-production-fast-path func=read")]
    [InlineData("function mk(o){ return function set(){ o.value=43; return o.value; }; } mk({value:42})();", "unified-bytecode-production-fast-path func=set")]
    // A46: the `**` exponentiation binary operator (BinaryOperator.Power) is in the production operator
    // subset (IsProductionBinaryOperator) and evaluates via JsOps.Exp. Integer base/exponent, the
    // right-associative chain (2**3**2 === 512), and the `**=` compound form all keep routing sync.
    [InlineData("function f(a,b){ return a**b; } f(2,10);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(){ return 2**3**2; } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(){ let x=3; x**=2; return x; } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(){ return 2 + 3 ** 2; } f();", "unified-bytecode-production-fast-path func=f")]
    // A6 (multi-statement arrow Stage 0): arrows with a FLAT multi-statement body now route through the
    // production VM (previously SimpleReturnProgram-only). Arrows log anonymously (func=<anonymous>).
    // Local-const-then-return, captured enclosing local across statements, and lexical `this` inside an
    // object method. Nested-lexical-scope arrows (shadowing hazard) are intentionally NOT admitted yet
    // (HasOnlyRootFlatSlotMappings guard) — no ratchet entry for them.
    [InlineData("const f = (a,b) => { const s = a+b; return s*2; }; f(3,4);", "unified-bytecode-production-fast-path func=<anonymous>")]
    [InlineData("function mk(base){ return (k) => { var t = base*2; return t+k; }; } mk(100)(1);", "unified-bytecode-production-fast-path func=<anonymous>")]
    [InlineData("var o={ x:10, m:function(){ var f=()=>{ var b=5; return this.x+b; }; return f(); } }; o.m();", "unified-bytecode-production-fast-path func=<anonymous>")]
    // A52: `debugger;` lowers to an EmptyStatement no-op, so functions/scripts containing it keep
    // routing through the production VM (in the body, after a side effect, and inside a loop body).
    [InlineData("function f(){ debugger; return 1; } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o){ o.a=1; debugger; return o.a; } f({});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(){ var s=0; for(var i=0;i<2;i++){ debugger; s+=i; } return s; } f();", "unified-bytecode-production-fast-path func=f")]
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

    // A52: a top-level `debugger;` no-op must keep the script on the production script route.
    [Fact]
    public async Task ScriptWithDebuggerStatement_StillRoutesThroughProductionScriptRoute()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("debugger; var x = 5; x;");
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
