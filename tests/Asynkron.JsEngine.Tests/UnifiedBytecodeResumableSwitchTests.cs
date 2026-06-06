using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableSwitchTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";

    [Fact(Timeout = 5000)]
    public async Task GeneratorSwitchBreakReturnAndDefault_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* gen(n) {
                yield "start";
                switch (n) {
                    case 1:
                        yield "one";
                        break;
                    case 2:
                        return "two";
                    default:
                        yield "other";
                }

                yield "done";
            }

            var one = gen(1);
            var oneResult = one.next().value + "|" +
                one.next().value + "|" +
                one.next().value + "|" +
                one.next().done;

            var two = gen(2);
            var twoFirst = two.next().value;
            var twoSecond = two.next();
            var twoResult = twoFirst + "|" + twoSecond.value + "|" + twoSecond.done;

            var other = gen(3);
            var otherResult = other.next().value + "|" +
                other.next().value + "|" +
                other.next().value + "|" +
                other.next().done;

            oneResult + ";" + twoResult + ";" + otherResult;
            """);

        Assert.Equal("start|one|done|true;start|two|true;start|other|done|true", result);
        AssertGeneratorFastPath("gen", argc: 1);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));
}
