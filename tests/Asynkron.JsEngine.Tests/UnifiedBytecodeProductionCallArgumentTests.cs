using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeProductionCallArgumentTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    [Fact(Timeout = 5000)]
    public async Task IdentifierCallWithNamedPropertyReadArgument_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function identity(value) {
                return value;
            }

            function invoke(fn, box) {
                return fn(box.value);
            }

            invoke(identity, { value: 42 });
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task IdentifierCallWithComputedPropertyReadArgument_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function identity(value) {
                return value;
            }

            function invoke(fn, box) {
                return fn(box["value"]);
            }

            invoke(identity, { value: 42 });
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedMemberCallWithNamedPropertyReadArgument_PreservesThisAndUsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var sink = {
                base: 40,
                add(value) {
                    return this.base + value;
                }
            };

            function invoke(sink, box) {
                return sink.add(box.value);
            }

            invoke(sink, { value: 2 });
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=2",
                StringComparison.Ordinal));
    }
}
