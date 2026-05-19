using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibTemporal)]
public sealed class IntlDateTimeFormatTemporalTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task FormatRange_FormatsTemporalPlainDateOperands()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const formatter = new Intl.DateTimeFormat('en-US', {
                timeZone: 'UTC',
                year: 'numeric',
                month: '2-digit',
                day: '2-digit'
            });
            formatter.formatRange(
                new Temporal.PlainDate(2024, 5, 1),
                new Temporal.PlainDate(2024, 5, 2));
            """);

        Assert.Equal("05/01/2024 \u2013 05/02/2024", result);
    }

    [Fact]
    public async Task FormatRange_CollapsesMatchingTemporalPlainDateOperands()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const formatter = new Intl.DateTimeFormat('en-US', {
                timeZone: 'UTC',
                year: 'numeric',
                month: '2-digit',
                day: '2-digit'
            });
            const date = new Temporal.PlainDate(2024, 5, 1);
            formatter.formatRange(date, date) === formatter.format(date);
            """);

        Assert.Equal(true, result);
    }

    [Fact]
    public async Task FormatRange_RejectsDistinctTemporalOperandKinds()
    {
        await using var engine = CreateEngine();

        await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("""
            new Intl.DateTimeFormat('en-US').formatRange(
                new Temporal.PlainDate(2024, 5, 1),
                new Temporal.PlainTime(12, 30));
            """));
    }

    [Fact]
    public async Task FormatRange_RejectsTemporalOperandsWithNonOverlappingOptions()
    {
        await using var engine = CreateEngine();

        await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("""
            new Intl.DateTimeFormat('en-US', {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit'
            }).formatRange(
                new Temporal.PlainTime(12, 30),
                new Temporal.PlainTime(13, 30));
            """));
    }
}
