using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibIntl)]
public sealed class IntlDateTimeFormatProlepticGregorianTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task FormatToParts_UsesProlepticGregorianComponentsOutsideDateTimeOffsetRange()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const formatter = new Intl.DateTimeFormat('en-US', {
                timeZone: 'UTC',
                year: 'numeric',
                month: '2-digit',
                day: '2-digit'
            });
            formatter.formatToParts(new Date(8640000000000000))
                .filter(part => part.type !== 'literal')
                .map(part => `${part.type}:${part.value}`)
                .join('|');
            """);

        Assert.Equal("month:09|day:13|year:275760", result);
    }

    [Fact]
    public async Task FormatRange_UsesProlepticGregorianComponentsOutsideDateTimeOffsetRange()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const formatter = new Intl.DateTimeFormat('en-US', {
                timeZone: 'UTC',
                year: 'numeric',
                month: '2-digit',
                day: '2-digit'
            });
            const date = new Date(8640000000000000);
            [
                formatter.formatRange(date, date),
                formatter.formatRangeToParts(date, date)
                    .filter(part => part.type !== 'literal')
                    .map(part => `${part.source}:${part.type}:${part.value}`)
                    .join('|')
            ].join('\n');
            """);

        Assert.Equal("09/13/275760\nshared:month:09|shared:day:13|shared:year:275760", result);
    }
}
