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
    public async Task BinaryComparisonFunction_UsesUnifiedBytecodeProductionFastPath()
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
        Assert.Contains(logRecords,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=isLess argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BranchBothArms_UseUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function pick(flag) {
                if (flag) {
                    return 1;
                }

                return 2;
            }

            pick(true) * 10 + pick(false);
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(12d, result);
        Assert.Equal(2, logRecords.Count(record =>
            record.Message.Contains("unified-bytecode-production-fast-path func=pick argc=1", StringComparison.Ordinal)));
    }

    [Fact(Timeout = 5000)]
    public async Task BranchJoinedLocalUpdates_UseUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function choose(pick) {
                var value = 1;
                if (pick) {
                    value = 2;
                } else {
                    value = 3;
                }

                return value;
            }

            choose(true) * 10 + choose(false);
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(23d, result);
        Assert.Equal(2, logRecords.Count(record =>
            record.Message.Contains("unified-bytecode-production-fast-path func=choose argc=1", StringComparison.Ordinal)));
    }

    [Theory(Timeout = 5000)]
    [InlineData(0, 0)]
    [InlineData(4, 10)]
    public async Task CanonicalWhileLoop_UsesUnifiedBytecodeProductionFastPath(int input, int expected)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($$"""
            function sumTo(n) {
                var total = 0;
                while (n > 0) {
                    total = total + n;
                    n = n - 1;
                }

                return total;
            }

            sumTo({{input}});
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal((double)expected, result);
        Assert.Contains(logRecords,
            record => record.Message.Contains("unified-bytecode-production-fast-path func=sumTo argc=1", StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task StringConcatenationBinary_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function concatWithSuffix(value) {
                return value + "!";
            }

            concatWithSuffix("ok");
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal("ok!", result?.ToString());
        Assert.Contains(logRecords,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=concatWithSuffix argc=1",
                StringComparison.Ordinal));
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(UnsupportedControlFlowFunctions))]
    public async Task UnsupportedControlFlowShapes_DeclineUnifiedBytecodeAndFallBack(
        string source,
        string invocation,
        double expected,
        string functionName)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($$"""
            {{source}}

            {{invocation}};
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(expected, result);
        Assert.DoesNotContain(logRecords,
            record => record.Message.Contains(
                $"unified-bytecode-production-fast-path func={functionName}",
                StringComparison.Ordinal));
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

    public static TheoryData<string, string, double, string> UnsupportedControlFlowFunctions =>
        new()
        {
            {
                """
                function labeled(n) {
                    outer: while (n > 0) {
                        n = n - 1;
                    }

                    return n;
                }
                """,
                "labeled(2)",
                0d,
                "labeled"
            },
            {
                """
                function breakLoop(n) {
                    while (n > 0) {
                        break;
                    }

                    return n;
                }
                """,
                "breakLoop(3)",
                3d,
                "breakLoop"
            },
            {
                """
                function continueLoop(n) {
                    var total = 0;
                    while (n > 0) {
                        n = n - 1;
                        continue;
                        total = total + 1;
                    }

                    return total;
                }
                """,
                "continueLoop(3)",
                0d,
                "continueLoop"
            },
            {
                """
                function nonCanonicalFor(n) {
                    var total = 0;
                    for (; n > 0; n = n - 1) {
                        total = total + n;
                    }

                    return total;
                }
                """,
                "nonCanonicalFor(3)",
                6d,
                "nonCanonicalFor"
            },
            {
                """
                function unsupportedBranchPayload(a, b, pick) {
                    if (pick) {
                        return Math.max(a, b);
                    }

                    return b;
                }
                """,
                "unsupportedBranchPayload(2, 3, true)",
                3d,
                "unsupportedBranchPayload"
            }
        };
}
