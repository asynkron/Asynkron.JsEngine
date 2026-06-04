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

    // A23: computed receiver-prefix property update (`box[k1].child[k2]++`).

    [Fact(Timeout = 5000)]
    public async Task ComputedPrefixComputedPropertyUpdate_PostfixReturnsOldValue_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function update(box, k1, k2) {
                return box[k1].child[k2]++;
            }

            var box = { a: { child: { x: 41 } } };
            var old = update(box, "a", "x");
            old + ":" + box.a.child.x;
            """);

        Assert.Equal("41:42", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=update argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedPrefixComputedPropertyUpdate_PrefixReturnsNewValue_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function update(box, k1, k2) {
                return --box[k1].child[k2];
            }

            var box = { a: { child: { x: 10 } } };
            var fresh = update(box, "a", "x");
            fresh + ":" + box.a.child.x;
            """);

        Assert.Equal("9:9", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=update argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedPrefixNamedPropertyUpdate_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function update(box, k1) {
                return box[k1].child++;
            }

            var box = { a: { child: 7 } };
            var old = update(box, "a");
            old + ":" + box.a.child;
            """);

        Assert.Equal("7:8", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=update argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedPrefixComputedPropertyUpdate_ResolvesPrefixAndKeyExactlyOnce_UsesProductionFastPath()
    {
        // The receiver prefix is evaluated once (`box.a` getter runs once). A getter on the
        // updated property runs once (read), the setter once (write) -- postfix returns the
        // numeric coercion of the getter's old value.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function update(box, k1, k2) {
                return box[k1].child[k2]++;
            }

            var prefixReads = 0;
            var getCount = 0;
            var setCount = 0;
            var backing = 41;
            var child = {};
            Object.defineProperty(child, "x", {
                get() { getCount++; return backing; },
                set(v) { setCount++; backing = v; },
                configurable: true,
            });
            var box = {};
            Object.defineProperty(box, "a", {
                get() { prefixReads++; return { child: child }; },
                configurable: true,
            });

            var old = update(box, "a", "x");
            [old, prefixReads, getCount, setCount, backing].join(",");
            """);

        Assert.Equal("41,1,1,1,42", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=update argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedPrefixComputedPropertyUpdate_ThrowsWhenPrefixHopIsNullish_UsesProductionFastPath()
    {
        // A nullish intermediate prefix hop must throw a TypeError on the production fast path,
        // matching interpreter semantics (no silent short-circuit).
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function update(box, k1, k2) {
                return box[k1].child[k2]++;
            }

            var box = { a: null };
            try {
                update(box, "a", "x");
                "no throw";
            } catch (error) {
                error.name;
            }
            """);

        Assert.Equal("TypeError", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=update argc=3",
                StringComparison.Ordinal));
    }
}
