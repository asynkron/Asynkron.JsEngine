using System;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class DynamicFunctionConstructorQuarantineTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task NewFunctionProducedBody_StaysOffProductionUnifiedBytecodeButOrdinaryFunctionStillRoutes()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function ordinaryAdd(a, b) {
                return a + b;
            }

            var add = new Function("a", "b", "return a + b;");
            ordinaryAdd(20, 22) + ":" + add(40, 2);
            """);

        Assert.Equal("42:42", result);
        AssertRouted("unified-bytecode-production-fast-path func=ordinaryAdd argc=2");
        AssertNotRouted("unified-bytecode-production-fast-path func=anonymous argc=2");
    }

    private void AssertRouted(string expectedLog)
    {
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(expectedLog, StringComparison.Ordinal));
    }

    private void AssertNotRouted(string unexpectedLog)
    {
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(unexpectedLog, StringComparison.Ordinal));
    }
}
