using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Proves that computed property accesses whose key is a control-expression
/// (`obj[cond ? a : b]`, `obj[a && b]`, `obj[a ?? b]`) now enter the production
/// unified-bytecode fast path for reads, writes, updates, and deletes. The
/// `unified-bytecode-production-fast-path` log assertion fails if an interpreter
/// fallback handles the body instead.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeProductionConditionalComputedKeyTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ProductionFastPathLog = "unified-bytecode-production-fast-path";

    private void AssertRouted(string functionName, int argc)
    {
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ProductionFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NestedTernaryComputedPropertyWrite_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function writeNested(box, cond, value) {
                return box.child[cond ? "a" : "b"] = value;
            }

            var box = { child: { a: 0, b: 0 } };
            writeNested(box, true, 42);
            writeNested(box, false, 7);
            box.child.a + box.child.b;
            """);

        Assert.Equal(49d, result);
        AssertRouted("writeNested", 3);
    }

    [Fact(Timeout = 5000)]
    public async Task DirectTernaryComputedPropertyWrite_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function write(obj, cond, value) {
                return obj[cond ? "hit" : "miss"] = value;
            }

            var obj = { hit: 0, miss: 0 };
            write(obj, true, 11);
            obj.hit;
            """);

        Assert.Equal(11d, result);
        AssertRouted("write", 3);
    }

    [Fact(Timeout = 5000)]
    public async Task TernaryComputedPropertyUpdate_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function bump(box, cond) {
                return ++box.child[cond ? "a" : "b"];
            }

            var box = { child: { a: 41, b: 100 } };
            bump(box, true);
            """);

        Assert.Equal(42d, result);
        AssertRouted("bump", 2);
    }

    [Fact(Timeout = 5000)]
    public async Task TernaryComputedPropertyDelete_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function drop(obj, cond) {
                return delete obj[cond ? "a" : "b"];
            }

            var obj = { a: 1, b: 2 };
            var removed = drop(obj, true);
            removed + ":" + ("a" in obj) + ":" + ("b" in obj);
            """);

        Assert.Equal("true:false:true", result);
        AssertRouted("drop", 2);
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalAndComputedKeyWrite_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function write(obj, left, right, value) {
                return obj[left && right] = value;
            }

            var obj = {};
            write(obj, "x", "y", 5);
            obj.y;
            """);

        Assert.Equal(5d, result);
        AssertRouted("write", 4);
    }

    [Fact(Timeout = 5000)]
    public async Task NullishCoalesceComputedKeyWrite_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function write(obj, primary, fallback, value) {
                return obj[primary ?? fallback] = value;
            }

            var obj = {};
            write(obj, null, "fb", 9);
            obj.fb;
            """);

        Assert.Equal(9d, result);
        AssertRouted("write", 4);
    }

    [Fact(Timeout = 5000)]
    public async Task NestedTernaryComputedKey_OnlyEvaluatesSelectedBranchValue()
    {
        // Adversarial: the key must short-circuit to the taken branch. A ternary
        // whose branches are distinct string keys must write to exactly one slot.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function write(obj, a, b, value) {
                return obj[a ? "left" : (b ? "mid" : "right")] = value;
            }

            var obj = { left: 0, mid: 0, right: 0 };
            write(obj, false, true, 3);
            obj.left + "," + obj.mid + "," + obj.right;
            """);

        Assert.Equal("0,3,0", result);
        AssertRouted("write", 4);
    }

    [Fact(Timeout = 5000)]
    public async Task TernaryComputedKeyWrite_PreservesStrictNonWritableFailure()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function write(obj, cond) {
                "use strict";
                return obj[cond ? "locked" : "open"] = 2;
            }

            var obj = {};
            Object.defineProperty(obj, "locked", { value: 1, writable: false });
            try {
                write(obj, true);
                "no throw";
            } catch (error) {
                error.name;
            }
            """);

        Assert.Equal("TypeError", result);
        AssertRouted("write", 2);
    }
}
