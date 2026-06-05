using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableTryCatchTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";

    [Fact(Timeout = 5000)]
    public async Task GeneratorThrowStatementInsideTry_RoutesResumableAndBindsCatch()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* recover() {
                yield "start";
                try {
                    throw 40;
                } catch (e) {
                    yield e + 2;
                }
            }

            var iterator = recover();
            iterator.next().value + "|" + iterator.next().value + "|" + iterator.next().done;
            """);

        Assert.Equal("start|42|true", result);
        AssertGeneratorFastPath("recover", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorThrowResumeInsideTry_RoutesResumableAndBindsCatch()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* recover() {
                try {
                    yield "ready";
                } catch (e) {
                    yield "caught:" + e;
                }

                return "done";
            }

            var iterator = recover();
            var first = iterator.next().value;
            var second = iterator.throw("boom").value;
            var third = iterator.next().value;
            var done = iterator.next().done;
            first + "|" + second + "|" + third + "|" + done;
            """);

        Assert.Equal("ready|caught:boom|done|true", result);
        AssertGeneratorFastPath("recover", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorThrowResumeInsideTryCatchFinally_RunsCleanupBeforeCompletion()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* recover() {
                var log = "";
                try {
                    try {
                        yield "ready";
                    } catch (e) {
                        log = log + "catch:" + e;
                    } finally {
                        log = log + "|finally";
                    }
                } catch (outer) {
                    log = log + "|outer:" + outer;
                }

                yield log;
            }

            var iterator = recover();
            var first = iterator.next().value;
            var second = iterator.throw("boom").value;
            first + "|" + second + "|" + iterator.next().done;
            """);

        Assert.Equal("ready|catch:boom|finally|true", result);
        AssertGeneratorFastPath("recover", argc: 0);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));
}
