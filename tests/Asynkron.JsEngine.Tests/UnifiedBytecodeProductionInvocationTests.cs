using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeProductionInvocationTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string UnifiedBytecodeProductionFastPathLog = "unified-bytecode-production-fast-path";
    private const string SimpleIrParameterNumberBinaryFastPathLog =
        "simple-ir-parameter-number-binary-fast-path";

    [Fact(Timeout = 5000)]
    public async Task LinearSlotReturnFunction_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function passThrough(x) {
                var y = x;
                return y;
            }

            passThrough(42);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=passThrough argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LocalLiteralReturnFunction_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readLocal() {
                var y = 7;
                return y;
            }

            readLocal();
            """);

        Assert.Equal(7d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readLocal argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task MissingArgument_InitializesParameterSlotToUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function passThrough(x) {
                var y = x;
                return y;
            }

            passThrough();
            """);

        Assert.Equal(Symbol.Undefined, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=passThrough argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task DirectBranchReturnFunction_UsesUnifiedBytecodeProductionFastPathForTrueAndFalseOutcomes()
    {
        await using var engine = CreateEngine();
        var trueResult = await engine.Evaluate("""
            function pick(flag) {
                var branch = flag;
                if (branch) {
                    return 1;
                }

                return 2;
            }

            pick(true);
            """);

        var falseResult = await engine.Evaluate("""
            pick(false);
            """);

        Assert.Equal(1d, trueResult);
        Assert.Equal(2d, falseResult);
        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Contains(logRecords,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=pick",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BinaryReturnFunction_KeepsExistingSpecializedFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function add(a, b) {
                return a + b;
            }

            add(20, 22);
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(42d, result);
        Assert.DoesNotContain(logRecords,
            static record => record.Message.Contains(UnifiedBytecodeProductionFastPathLog, StringComparison.Ordinal));
        Assert.Contains(logRecords,
            static record => record.Message.Contains(SimpleIrParameterNumberBinaryFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BinaryComparisonFunction_DoesNotUseUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function isLess(a, b) {
                return a < b;
            }

            isLess(20, 22);
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(true, result);
        Assert.DoesNotContain(logRecords,
            static record => record.Message.Contains(UnifiedBytecodeProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task CallExpressionFunction_DeclinesUnifiedBytecodeAndFallsBack()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(fn, x) {
                var y = fn(x);
                return y;
            }

            function id(x) {
                return x;
            }

            invoke(id, 42);
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }
}
