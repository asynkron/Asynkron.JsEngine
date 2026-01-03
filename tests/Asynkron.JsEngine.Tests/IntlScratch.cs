using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibIntl)]
public sealed class IntlScratchTests(ITestOutputHelper output) : InternalTestBase(output)
{
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
}
