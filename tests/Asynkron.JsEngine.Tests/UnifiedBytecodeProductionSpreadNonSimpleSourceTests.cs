using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// A33: Covers admission of array-literal spreads whose source is NON-simple into the
/// production unified bytecode pipeline:
/// <list type="bullet">
///   <item><description><c>[...f()]</c> / <c>[...gen()]</c> — bare identifier call source</description></item>
///   <item><description><c>[...f().items]</c> / <c>[...f().a.b]</c> — named property read off a call source</description></item>
///   <item><description><c>[a, ...f(), c]</c> — non-simple spread mixed with normal elements</description></item>
/// </list>
/// Each routing test asserts that the production fast-path log line is emitted
/// (interpreter fallback would fail the assertion) and the value is semantically
/// correct, including the iterator protocol and throw-on-non-iterable edges.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeProductionSpreadNonSimpleSourceTests(ITestOutputHelper output)
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
    public async Task IdentifierCallSpreadSource_UsesProductionFastPathAndFlattens()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(items) {
                return [...items()];
            }

            build(() => [1, 2, 3]).join(",");
            """);

        Assert.Equal("1,2,3", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorCallSpreadSource_UsesProductionFastPathAndDrainsIterator()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(gen) {
                return [...gen()];
            }

            function* g() { yield 1; yield 2; yield 3; }
            build(g).join(",");
            """);

        Assert.Equal("1,2,3", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPropertyOfCallSpreadSource_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(f) {
                return [...f().items];
            }

            build(() => ({ items: [7, 8] })).join(",");
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
                return [...f().a.b];
            }

            build(() => ({ a: { b: [4, 5, 6] } })).join(",");
            """);

        Assert.Equal("4,5,6", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task SetSpreadFromCallSource_UsesProductionFastPathAndDedupes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(f) {
                return [...f()];
            }

            build(() => new Set([1, 1, 2, 3, 3])).join(",");
            """);

        Assert.Equal("1,2,3", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task MixedNormalAndNonSimpleSpread_UsesProductionFastPathAndPreservesOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(a, f, c) {
                return [a, ...f(), c];
            }

            build(0, () => [1, 2], 3).join(",");
            """);

        Assert.Equal("0,1,2,3", result?.ToString());
        AssertRouted("build", 3);
    }

    [Fact(Timeout = 5000)]
    public async Task NonSimpleSpreadSource_IteratesOnceLeftToRight()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var log = [];
            function source() {
                return {
                    [Symbol.iterator]() {
                        var i = 0;
                        return {
                            next() {
                                if (i < 3) {
                                    log.push(i);
                                    return { value: i++, done: false };
                                }

                                return { value: undefined, done: true };
                            }
                        };
                    }
                };
            }

            function build(f) {
                return [...f()];
            }

            var arr = build(source);
            arr.join(",") + ":" + log.join(",");
            """);

        Assert.Equal("0,1,2:0,1,2", result?.ToString());
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task NonSimpleSpreadSourceThatIsNotIterable_ThrowsTypeError()
    {
        await using var engine = CreateEngine();

        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("""
                function build(f) {
                    return [...f()];
                }

                build(() => 5);
                """));

        Assert.Contains("TypeError", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not iterable", exception.Message, StringComparison.Ordinal);
        AssertRouted("build", 1);
    }

    [Fact(Timeout = 5000)]
    public async Task NonSimpleSpreadSourceThrowInChain_PropagatesThrow()
    {
        await using var engine = CreateEngine();

        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("""
                function build(f) {
                    return [...f().items];
                }

                build(() => { throw new RangeError("boom"); });
                """));

        Assert.Contains("RangeError", exception.Message, StringComparison.Ordinal);
        Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
        AssertRouted("build", 1);
    }
}
