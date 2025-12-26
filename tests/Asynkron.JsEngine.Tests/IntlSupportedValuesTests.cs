using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class IntlSupportedValuesTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact]
    public async Task SupportedValuesRejectSymbolKeys()
    {
        await using var engine = CreateEngine();
        await Assert.ThrowsAsync<ThrowSignal>(async () =>
            await engine.Evaluate("Intl.supportedValuesOf(Symbol('k'));"));
    }

    [Fact]
    public async Task CurrencyValuesAlignWithDisplayNamesAndNumberFormat()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function () {
                var currencies = Intl.supportedValuesOf("currency");
                if (currencies.length === 0) {
                    return [0];
                }

                var currency = currencies[0];
                var dn = new Intl.DisplayNames("en", { type: "currency", fallback: "none" });
                var name = dn.of(currency);
                var nf = new Intl.NumberFormat("en", { style: "currency", currency });
                var resolved = nf.resolvedOptions();
                var fallback = dn.of("ZZZ");
                return [currencies.length, currency, typeof name, resolved.currency, fallback === undefined];
            })();
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.True(array.Items.Count >= 1, "Result array should expose at least the length slot.");
        var length = Assert.IsType<double>(array.Items[0].ToObject()!);
        Assert.True(length >= 0, "Currency list length should be non-negative.");
        if (length == 0)
        {
            return;
        }

        Assert.Equal(5, array.Items.Count);
        var currencyCode = Assert.IsType<string>(array.Items[1].ToObject()!);
        Assert.Equal(3, currencyCode.Length);
        var displayType = Assert.IsType<string>(array.Items[2].ToObject()!);
        Assert.Equal("string", displayType);
        var resolvedCurrency = Assert.IsType<string>(array.Items[3].ToObject()!);
        Assert.Equal(currencyCode, resolvedCurrency);
        var fallbackUndefined = Assert.IsType<bool>(array.Items[4].ToObject()!);
        Assert.True(fallbackUndefined);
    }

    [Fact]
    public async Task SupportedValuesCoerceKeysWithToString()
    {
        await using var engine = CreateEngine();
        var baseline = Assert.IsType<JsArray>(await engine.Evaluate("Intl.supportedValuesOf('calendar');"));
        var viaStringObject =
            Assert.IsType<JsArray>(await engine.Evaluate("Intl.supportedValuesOf(new String('calendar'));"));
        var viaPlainObject = Assert.IsType<JsArray>(await engine.Evaluate("""
            Intl.supportedValuesOf({
                toString() {
                    return 'calendar';
                }
            });
            """));

        Assert.Equal(baseline.Items, viaStringObject.Items);
        Assert.Equal(baseline.Items, viaPlainObject.Items);
    }
}

public class FastPath_IntlSupportedValuesTests(ITestOutputHelper output) : IntlSupportedValuesTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class Reference_IntlSupportedValuesTests(ITestOutputHelper output) : IntlSupportedValuesTestsBase(output)
{
    protected override bool EnableFastPaths => false;
}
