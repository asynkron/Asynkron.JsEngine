using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibIntl)]
public sealed class IntlScratchTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task ListFormatExists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function() {
                var lf = new Intl.ListFormat("es-ES", { style: "short" });
                var opts = lf.resolvedOptions();
                var r2 = lf.format(["foo", "bar"]);
                var r3 = lf.format(["foo", "bar", "baz"]);
                return JSON.stringify({ locale: opts.locale, type: opts.type, style: opts.style, two: r2, three: r3 });
            })()
            """);
        Output.WriteLine($"Result: {result}");
    }

    [Fact]
    public async Task InspectSupportedValuesCoercion()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function () {
                var calendars = Intl.supportedValuesOf("calendar");
                var viaString = Intl.supportedValuesOf(new String("calendar"));
                var viaObject = Intl.supportedValuesOf({
                    toString() { return "calendar"; }
                });
                return [
                    typeof calendars,
                    calendars === null,
                    calendars === undefined,
                    calendars.length,
                    typeof viaString,
                    viaString === null,
                    viaString === undefined,
                    viaString && viaString.length,
                    typeof viaObject,
                    viaObject === null,
                    viaObject === undefined,
                    viaObject && viaObject.length
                ];
            })();
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("object", array.GetElement(0).ToObject());
        Assert.False(array.GetElement(1).AsBoolean());
        Assert.False(array.GetElement(2).AsBoolean());
        Assert.True(array.GetElement(3).AsDouble() > 0);
        Assert.Equal("object", array.GetElement(4).ToObject());
        Assert.False(array.GetElement(5).AsBoolean());
        Assert.False(array.GetElement(6).AsBoolean());
        Assert.True(array.GetElement(7).AsDouble() > 0);
        Assert.Equal("object", array.GetElement(8).ToObject());
        Assert.False(array.GetElement(9).AsBoolean());
        Assert.False(array.GetElement(10).AsBoolean());
        Assert.True(array.GetElement(11).AsDouble() > 0);
    }

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
