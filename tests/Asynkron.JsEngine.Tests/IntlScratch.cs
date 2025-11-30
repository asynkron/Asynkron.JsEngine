using Asynkron.JsEngine.JsTypes;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class IntlScratch
{
    [Fact]
    public async Task InspectSupportedValuesCoercion()
    {
        await using var engine = new JsEngine();
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
        Assert.Equal("object", array.Items[0]);
        Assert.False((bool)array.Items[1]!);
        Assert.False((bool)array.Items[2]!);
        Assert.True((double)array.Items[3]! > 0);
        Assert.Equal("object", array.Items[4]);
        Assert.False((bool)array.Items[5]!);
        Assert.False((bool)array.Items[6]!);
        Assert.True((double)array.Items[7]! > 0);
        Assert.Equal("object", array.Items[8]);
        Assert.False((bool)array.Items[9]!);
        Assert.False((bool)array.Items[10]!);
        Assert.True((double)array.Items[11]! > 0);
    }
}
