using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableForOfTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    [Fact(Timeout = 5000)]
    public async Task GeneratorForOfDriverAcrossYield_RoutesResumableAndYieldsValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* values(items) {
                for (var value of items) {
                    yield value;
                }
            }

            var iterator = values([4]);
            iterator.next().value + "|" + iterator.next().done;
            """);

        Assert.Equal("4|true", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func=values argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncForOfAwaitedSource_RoutesResumableAndReadsValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function collect(sourcePromise) {
                var last = 0;
                for (var value of await sourcePromise) {
                    last = value;
                }

                return last;
            }

            collect(Promise.resolve([7]))
                .then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(7.0, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=collect argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncForAwaitOfCustomAsyncIterator_RoutesResumableAndAwaitsNextResults()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function collect(values) {
                var total = "";
                for await (var value of values) {
                    total = total + value;
                }

                return total;
            }

            var asyncIterable = {
                [Symbol.asyncIterator]() {
                    var current = 0;
                    return {
                        next() {
                            current = current + 1;
                            if (current > 2) {
                                return { done: true };
                            }

                            return { value: current * 3, done: false };
                        }
                    };
                }
            };

            collect(asyncIterable).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal("36", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=collect argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncForAwaitOfReturn_AwaitsAsyncIteratorReturnBeforeSettling()
    {
        await using var engine = CreateEngine();
        AsyncTestHelpers.RegisterDelayHelper(engine);

        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            var events = "";

            async function collect(values) {
                for await (var value of values) {
                    events = events + "body";
                    return events;
                }

                return "unreachable";
            }

            var asyncIterable = {
                [Symbol.asyncIterator]() {
                    return {
                        next() {
                            return { value: 1, done: false };
                        },
                        return() {
                            return __delay(1, { done: true }).then(function(result) {
                                events = events + "|closed";
                                return result;
                            });
                        }
                    };
                }
            };

            collect(asyncIterable).then(value => asyncResult = events + "|value:" + value);
            asyncResult;
            """);

        Assert.Equal("body|closed|value:body", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=collect argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncForAwaitOfSyncIterableYieldingPromises_RoutesResumableAndAwaitsValues()
    {
        await using var engine = CreateEngine();
        AsyncTestHelpers.RegisterDelayHelper(engine);

        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;

            async function collect(values) {
                var total = 0;
                for await (var value of values) {
                    total = total + value;
                }

                return total;
            }

            var syncIterable = {
                [Symbol.iterator]() {
                    var current = 0;
                    return {
                        next() {
                            current = current + 1;
                            if (current > 2) {
                                return { done: true };
                            }

                            return {
                                value: __delay(1, current * 5),
                                done: false
                            };
                        }
                    };
                }
            };

            collect(syncIterable).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(15.0, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=collect argc=1",
                StringComparison.Ordinal));
    }
}
