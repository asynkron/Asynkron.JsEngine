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
