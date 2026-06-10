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

    [Fact(Timeout = 5000)]
    public async Task GeneratorThrowingCallInsideTry_RoutesResumableAndBindsCatch()
    {
        // Pins the resumable VM's call-boundary throw dispatch: a throw raised INSIDE a
        // callee (surfacing as context flow at the CallInvocationBoundary) must unwind to
        // the generator body's own try frames instead of completing the resumable with an
        // escaping Throw step.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function boom() {
                throw new TypeError("boom");
            }

            function* recover() {
                yield "ready";
                var outcome;
                try {
                    boom();
                    outcome = "no-throw";
                } catch (error) {
                    outcome = error instanceof TypeError ? "caught:" + error.message : "other";
                }
                yield outcome;
            }

            var iterator = recover();
            iterator.next();
            iterator.next().value;
            """);

        Assert.Equal("caught:boom", result);
        AssertGeneratorFastPath("recover", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorThrowingConstructInsideTry_RoutesResumableAndBindsCatch()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function Boom() {
                throw new RangeError("ctor");
            }

            function* recover() {
                yield "ready";
                var outcome;
                try {
                    new Boom();
                    outcome = "no-throw";
                } catch (error) {
                    outcome = error instanceof RangeError ? "caught:" + error.message : "other";
                }
                yield outcome;
            }

            var iterator = recover();
            iterator.next();
            iterator.next().value;
            """);

        Assert.Equal("caught:ctor", result);
        AssertGeneratorFastPath("recover", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorThrowingPropertyReadInsideTry_RoutesResumableAndBindsCatch()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* recover(box) {
                yield "ready";
                var outcome;
                try {
                    var value = box.missing.deeper;
                    outcome = "no-throw:" + value;
                } catch (error) {
                    outcome = error instanceof TypeError ? "caught" : "other";
                }
                yield outcome;
            }

            var iterator = recover({});
            iterator.next();
            iterator.next().value;
            """);

        Assert.Equal("caught", result);
        AssertGeneratorFastPath("recover", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorThrowingCallInsideTryFinally_RunsCleanupAndBindsCatch()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function boom() {
                throw "inner";
            }

            function* recover() {
                yield "ready";
                var log = "";
                try {
                    try {
                        boom();
                    } finally {
                        log = log + "finally";
                    }
                } catch (error) {
                    log = log + "|caught:" + error;
                }
                yield log;
            }

            var iterator = recover();
            iterator.next();
            iterator.next().value;
            """);

        Assert.Equal("finally|caught:inner", result);
        AssertGeneratorFastPath("recover", argc: 0);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));
}
