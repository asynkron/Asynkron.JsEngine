using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Covers admission of synchronous spread calls into the production unified bytecode
/// pipeline (gh2676): <c>f(...args)</c>, <c>f(...a, ...b)</c>, <c>obj.method(...args)</c>,
/// optional identifier spread calls, and mixed <c>f(a, ...b, c)</c>.
/// Super/direct-eval spread calls stay declined.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeProductionSpreadCallTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ProductionFastPathLog = "unified-bytecode-production-fast-path";

    [Fact(Timeout = 5000)]
    public async Task PureSpreadIdentifierCall_UsesProductionFastPathAndFlattensArguments()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sum(a, b, c) {
                return a + b + c;
            }

            function invoke(sum, args) {
                return sum(...args);
            }

            invoke(sum, [1, 2, 3]);
            """);

        Assert.Equal(6d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task MultiSpreadIdentifierCall_UsesProductionFastPathAndPreservesOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function join(a, b, c, d) {
                return "" + a + b + c + d;
            }

            function invoke(join, left, right) {
                return join(...left, ...right);
            }

            invoke(join, [1, 2], [3, 4]);
            """);

        Assert.Equal("1234", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task MixedSpreadIdentifierCall_FlattensInSourceOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function collect(a, b, c, d) {
                return "" + a + b + c + d;
            }

            function invoke(collect, middle) {
                return collect(0, ...middle, 3);
            }

            invoke(collect, [1, 2]);
            """);

        Assert.Equal("0123", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedMemberSpreadCall_UsesProductionFastPathAndPreservesThis()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = {
                base: 10,
                add(a, b) {
                    return this === box ? this.base + a + b : -1;
                }
            };

            function invoke(box, args) {
                return box.add(...args);
            }

            invoke(box, [20, 12]);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SpreadCall_IteratesSpreadSourceOnceAndLeftToRight()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sink() {
                return arguments.length;
            }

            function invoke(sink, source) {
                return sink(...source);
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

            var count = invoke(sink, source);
            count + ":" + log.join(",");
            """);

        Assert.Equal("3:0,1,2", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task OptionalIdentifierSpreadCall_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(fn, args) {
                return fn?.(...args);
            }

            function add(a, b) {
                return a + b;
            }

            invoke(add, [20, 22]);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task OptionalIdentifierSpreadCall_SkipsSpreadIterationWhenCalleeNullish()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(fn, args) {
                return fn?.(...args);
            }

            var log = [];
            var source = {
                [Symbol.iterator]() {
                    log.push("iterated");
                    return [][Symbol.iterator]();
                }
            };

            var value = invoke(null, source);
            String(value) + ":" + log.join(",");
            """);

        Assert.Equal("undefined:", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SpreadConstructResultNamedRead_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function Point(x, y) {
                this.x = x;
                this.y = y;
            }

            function invoke(Point, args) {
                return new Point(...args).x;
            }

            invoke(Point, [41, 1]);
            """);

        Assert.Equal(41d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }
}
