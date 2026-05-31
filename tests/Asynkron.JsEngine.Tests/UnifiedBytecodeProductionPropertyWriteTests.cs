using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeProductionPropertyWriteTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ProductionFastPathLog = "unified-bytecode-production-fast-path";

    [Fact(Timeout = 5000)]
    public async Task NestedNamedPropertyWrite_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function writeNested(box, value) {
                return box.child.value = value;
            }

            var box = { child: { value: 1 } };
            writeNested(box, 42) + box.child.value;
            """);

        Assert.Equal(84d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=writeNested argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NestedNamedPropertyUpdate_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function updateNested(box) {
                return ++box.child.value;
            }

            var box = { child: { value: 41 } };
            updateNested(box);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=updateNested argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NestedNamedPropertyWrite_PreservesStrictNonWritableFailureOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function writeNested(box) {
                "use strict";
                return box.child.value = 2;
            }

            var child = {};
            Object.defineProperty(child, "value", { value: 1, writable: false });
            try {
                writeNested({ child: child });
                "no throw";
            } catch (error) {
                error.name;
            }
            """);

        Assert.Equal("TypeError", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=writeNested argc=1",
                StringComparison.Ordinal));
    }
}
