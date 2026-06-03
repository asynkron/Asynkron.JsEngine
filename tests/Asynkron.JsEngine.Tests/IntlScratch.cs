using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibIntl)]
public sealed class IntlScratchTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task RelativeTimeFormatFormatToPartsAppliesToNumber()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function () {
                var rtf = new Intl.RelativeTimeFormat("en", { numeric: "always" });
                var pairs = [
                    [null, 0],
                    [false, 0],
                    [true, 1],
                    ["2", 2],
                    ["-0", -0],
                    [" \t\n3 ", 3],
                    [{ toString() { return "4"; } }, 4],
                    [{ valueOf() { return -5; } }, -5]
                ];

                function sameParts(left, right) {
                    var leftParts = rtf.formatToParts(left, "second");
                    var rightParts = rtf.formatToParts(right, "second");
                    if (leftParts.length !== rightParts.length) {
                        return false;
                    }

                    for (var i = 0; i < leftParts.length; i++) {
                        if (leftParts[i].type !== rightParts[i].type ||
                            leftParts[i].value !== rightParts[i].value ||
                            leftParts[i].unit !== rightParts[i].unit) {
                            return false;
                        }
                    }

                    return true;
                }

                for (var i = 0; i < pairs.length; i++) {
                    if (!sameParts(pairs[i][0], pairs[i][1])) {
                        return i;
                    }
                }

                return true;
            })();
            """);

        Assert.True(Assert.IsType<bool>(result));
    }
}
