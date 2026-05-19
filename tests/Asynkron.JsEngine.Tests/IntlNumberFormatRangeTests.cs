using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibIntl)]
public sealed class IntlNumberFormatRangeTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task FormatRangeToPartsUsesSameNumericAndRangeCompositionAsFormatRange()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            (function () {
                var nf = new Intl.NumberFormat("pt-PT", {
                    style: "currency",
                    currency: "EUR",
                    signDisplay: "always"
                });
                var range = nf.formatRange(2.9, 3.1);
                var parts = nf.formatRangeToParts(2.9, 3.1).map(function (part) {
                    return part.value;
                }).join("");

                var precise = new Intl.NumberFormat("en-US").formatRange(
                    "987654321987654321",
                    "987654321987654322");

                return parts === range &&
                    precise === "987,654,321,987,654,321–987,654,321,987,654,322";
            })();
            """);

        Assert.Equal(true, result);
    }

    [Fact]
    public async Task FormatRangeKeepsFullCurrencyEndpointsForMixedSigns()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            new Intl.NumberFormat("pt-PT", {
                style: "currency",
                currency: "EUR"
            }).formatRange(3, -5);
            """);

        Assert.Equal("3,00\u00A0€ - -5,00\u00A0€", result);
    }

    [Fact]
    public async Task PortugueseRangeSeparatorOverrideIsLimitedToPtPt()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            (function () {
                var range = new Intl.NumberFormat("pt-BR", {
                    style: "currency",
                    currency: "BRL",
                    maximumFractionDigits: 0
                }).formatRange(3, 5);

                return range.indexOf("\u2013") >= 0 && range.indexOf(" - ") < 0;
            })();
            """);

        Assert.Equal(true, result);
    }
}
