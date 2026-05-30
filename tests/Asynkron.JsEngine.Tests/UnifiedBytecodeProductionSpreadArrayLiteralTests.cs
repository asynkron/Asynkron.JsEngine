using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Covers admission of array-spread literals into the production unified bytecode
/// pipeline (Batch 3): <c>[...a]</c>, <c>[a, ...b, c]</c>, and hole/spread mixes.
/// Each test asserts that the production fast-path log line is emitted and the
/// result value is semantically correct.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeProductionSpreadArrayLiteralTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    [Fact(Timeout = 5000)]
    public async Task PureSpreadArrayLiteral_UsesProductionFastPathAndFlattensElements()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(source) {
                return [...source];
            }

            var arr = build([1, 2, 3]);
            arr.join(",");
            """);

        Assert.Equal("1,2,3", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=build argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task MixedSpreadArrayLiteral_UsesProductionFastPathAndPreservesOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(a, b, c) {
                return [a, ...b, c];
            }

            var arr = build(0, [1, 2], 3);
            arr.join(",");
            """);

        Assert.Equal("0,1,2,3", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=build argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task HoleThenSpreadArrayLiteral_UsesProductionFastPathAndPreservesHole()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(source) {
                return [, ...source];
            }

            var arr = build([1, 2]);
            arr.length + ":" + arr[0] + "," + arr[1] + "," + arr[2];
            """);

        Assert.Equal("3:undefined,1,2", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=build argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SpreadArrayLiteral_IteratesSpreadSourceOnceAndLeftToRight()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(source) {
                return [...source];
            }

            var log = [];
            var source = {
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

            var arr = build(source);
            arr.join(",") + ":" + log.join(",");
            """);

        Assert.Equal("0,1,2:0,1,2", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=build argc=1",
                StringComparison.Ordinal));
    }
}
