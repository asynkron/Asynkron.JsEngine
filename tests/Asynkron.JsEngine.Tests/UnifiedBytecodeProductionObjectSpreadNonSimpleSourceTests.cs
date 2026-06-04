using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// A34: Covers admission of object-literal spreads whose source is NON-simple into the
/// production unified bytecode pipeline:
/// <list type="bullet">
///   <item><description><c>{...f()}</c> / <c>{...gen()}</c> — bare identifier call source</description></item>
///   <item><description><c>{...f().items}</c> / <c>{...f().a.b}</c> — named property read off a call source</description></item>
///   <item><description><c>{x, ...f(), y}</c> — non-simple spread mixed with normal properties</description></item>
/// </list>
/// Each routing test asserts that the production fast-path log line is emitted
/// (interpreter fallback would fail the assertion) and the value is semantically correct,
/// including getter invocation order, non-enumerable skipping, the null/undefined no-op,
/// and throw-in-chain propagation.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeProductionObjectSpreadNonSimpleSourceTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private void AssertRouted(string funcName, int argc)
    {
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"unified-bytecode-production-fast-path func={funcName} argc={argc}",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task IdentifierCallSpreadSource_UsesProductionFastPathAndCopiesOwnProps()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(f) {
                return {...f()};
            }

            var o = build(() => ({ a: 1, b: 2 }));
            o.a + "," + o.b;
            """);

        Assert.Equal("1,2", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPropertyOfCallSpreadSource_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(f) {
                return {...f().inner};
            }

            var o = build(() => ({ inner: { x: 7, y: 8 } }));
            o.x + "," + o.y;
            """);

        Assert.Equal("7,8", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task DeepNamedPropertyOfCallSpreadSource_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(f) {
                return {...f().a.b};
            }

            var o = build(() => ({ a: { b: { p: 4, q: 5 } } }));
            o.p + "," + o.q;
            """);

        Assert.Equal("4,5", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task MemberCallSpreadSource_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(o) {
                return {...o.make()};
            }

            var r = build({ make() { return { a: 9, b: 10 }; } });
            r.a + "," + r.b;
            """);

        Assert.Equal("9,10", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task MixedNormalAndNonSimpleSpread_UsesProductionFastPathAndPreservesOverrideOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(f) {
                return { a: 0, ...f(), c: 3 };
            }

            // The spread's `a` overrides the leading `a:0`; the trailing `c:3` overrides the spread's `c`.
            var o = build(() => ({ a: 1, b: 2, c: 99 }));
            o.a + "," + o.b + "," + o.c;
            """);

        Assert.Equal("1,2,3", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task NonSimpleSpreadSource_InvokesGettersInSourceOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var log = [];
            function source() {
                return {
                    get a() { log.push("a"); return 1; },
                    get b() { log.push("b"); return 2; }
                };
            }

            function build(f) {
                return {...f()};
            }

            var o = build(source);
            o.a + "," + o.b + ":" + log.join(",");
            """);

        Assert.Equal("1,2:a,b", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task NonSimpleSpreadSource_SkipsNonEnumerableProperties()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function source() {
                var o = { visible: 1 };
                Object.defineProperty(o, "hidden", { value: 2, enumerable: false });
                return o;
            }

            function build(f) {
                return {...f()};
            }

            var o = build(source);
            o.visible + "," + (o.hidden === undefined ? "skipped" : o.hidden);
            """);

        Assert.Equal("1,skipped", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task NonSimpleSpreadSource_NullIsNoOp()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(f) {
                return { x: 1, ...f() };
            }

            var o = build(() => null);
            o.x + ":" + Object.keys(o).length;
            """);

        Assert.Equal("1:1", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task NonSimpleSpreadSource_UndefinedIsNoOp()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(f) {
                return { x: 1, ...f() };
            }

            var o = build(() => undefined);
            o.x + ":" + Object.keys(o).length;
            """);

        Assert.Equal("1:1", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task NonSimpleSpreadSourceThrowInChain_PropagatesThrow()
    {
        await using var engine = CreateEngine();

        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("""
                function build(f) {
                    return {...f().inner};
                }

                build(() => { throw new RangeError("boom"); });
                """));

        Assert.Contains("RangeError", exception.Message, StringComparison.Ordinal);
        Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
        AssertRouted("build", 1);
    }
}
