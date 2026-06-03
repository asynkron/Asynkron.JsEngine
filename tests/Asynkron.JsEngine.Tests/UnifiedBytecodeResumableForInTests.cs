using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableForInTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    [Fact(Timeout = 5000)]
    public async Task GeneratorForInDriverAcrossYield_RoutesResumableAndYieldsKey()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* keys(obj) {
                for (var key in obj) {
                    yield key;
                }
            }

            var iterator = keys({ a: 1 });
            iterator.next().value + "|" + iterator.next().done;
            """);

        Assert.Equal("a|true", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func=keys argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncForInAwaitedSource_RoutesResumableAndEnumeratesKey()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function collect(sourcePromise) {
                var last = "";
                for (var key in await sourcePromise) {
                    last = key;
                }

                return last;
            }

            collect(Promise.resolve({ a: 1 }))
                .then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal("a", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=collect argc=1",
                StringComparison.Ordinal));
    }
}
