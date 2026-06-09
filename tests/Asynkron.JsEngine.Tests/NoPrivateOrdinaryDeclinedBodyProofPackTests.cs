using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class NoPrivateOrdinaryDeclinedBodyProofPackTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string OrdinarySyncFallbackLog =
        "classified-ordinary-sync-function-fallback reason=production-unified-bytecode-declined";

    private const string ProductionFastPathLog = "unified-bytecode-production-fast-path";

    [Fact(Timeout = 5000)]
    public async Task GlobalPropertyReadPayloads_PreserveSetViewAndOptionalChainSemantics()
    {
        await AssertOrdinaryDeclinedBodyAsync(
            """
            globalThis.payloadSet = new Set(["a", "b"]);
            globalThis.payloadView = new DataView(new ArrayBuffer(4));
            payloadView.setInt8(0, 7);

            function globalPropertyPayload(caseKey) {
                const values = [
                    payloadSet.size,
                    payloadView.getInt8(0),
                    globalThis?.payloadSet?.has(caseKey)
                ];

                return values.join("|");
            }

            globalPropertyPayload("b");
            """,
            "2|7|true",
            "globalPropertyPayload");
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedLogicalPropertyWrites_PreserveCurrentFallbackSemantics()
    {
        await AssertOrdinaryDeclinedBodyAsync(
            """
            function computedLogicalWrite(box, key, value) {
                eval("var forcedDecline = 1;");
                box[key] ||= value;
                box[key + "2"] ??= value + forcedDecline;
                return [box[key], box[key + "2"]].join("|");
            }

            computedLogicalWrite({ hit: 0, hit2: null }, "hit", 5);
            """,
            "5|6",
            "computedLogicalWrite");
    }

    [Fact(Timeout = 5000)]
    public async Task DirectEvalFunctionCode_PreservesDeclarationAndArgumentsBinding()
    {
        await AssertOrdinaryDeclinedBodyAsync(
            """
            function directEvalFunctionCode(seed) {
                var local = seed;
                eval("var injected = local + 2;");
                return local + ":" + injected + ":" + arguments.length;
            }

            directEvalFunctionCode(40);
            """,
            "40:42:1",
            "directEvalFunctionCode");
    }

    [Fact(Timeout = 5000)]
    public async Task CreateArrayOperandPayloads_PreserveSparseArrayAndNestedArrayOperands()
    {
        await AssertOrdinaryDeclinedBodyAsync(
            """
            function createArrayOperandPayload(sink, value) {
                eval("'keep ordinary body declined'");
                return sink([value, , value + 1], { nested: [value * 2] }).join("|");
            }

            createArrayOperandPayload(function(values, record) {
                return [
                    values.length,
                    1 in values,
                    values[2],
                    record.nested[0]
                ];
            }, 7);
            """,
            "3|false|8|14",
            "createArrayOperandPayload");
    }

    [Fact(Timeout = 5000)]
    public async Task NestedOrdinaryCalls_PreserveStackAndReturnFlow()
    {
        await AssertOrdinaryDeclinedBodyAsync(
            """
            function nestedOrdinaryCallStack(seed) {
                function addOne(value) {
                    return value + 1;
                }

                function combine(value) {
                    return addOne(value) * 2;
                }

                eval("var forcedDecline = 1;");
                return combine(seed) - forcedDecline;
            }

            nestedOrdinaryCallStack(20);
            """,
            "41",
            "nestedOrdinaryCallStack");
    }

    private async Task AssertOrdinaryDeclinedBodyAsync(string source, string expectedResult, string functionName)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(source);

        Assert.Equal(expectedResult, result?.ToString());

        var snapshot = CurrentLogger!.Collector.Snapshot();
        Assert.Contains(
            snapshot,
            record => record.Message.Contains(
                $"{OrdinarySyncFallbackLog} func={functionName}",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            snapshot,
            record => record.Message.Contains(
                $"{ProductionFastPathLog} func={functionName}",
                StringComparison.Ordinal));
    }
}
