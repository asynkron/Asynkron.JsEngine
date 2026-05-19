using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibIntl)]
public sealed class IntlNumberFormatTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task FormatDecimalStringUsesEcmaWhitespace()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            (function () {
                var nf = new Intl.NumberFormat("en-US");
                return [
                    nf.format("\u00851"),
                    nf.format(NaN),
                    Number.isNaN(Number("\u00851"))
                ];
            })()
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(array.GetElement(1).ToJsString(), array.GetElement(0).ToJsString());
        Assert.True(array.GetElement(2).AsBoolean());
    }

    [Fact(Timeout = 2000)]
    public async Task FormatHugeDecimalStringExponentDoesNotExactScaleBigInteger()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            (function () {
                var nf = new Intl.NumberFormat("en-US");
                return nf.format("1e2147483647") === nf.format(Infinity);
            })()
            """);

        Assert.True(Assert.IsType<bool>(result));
    }
}
