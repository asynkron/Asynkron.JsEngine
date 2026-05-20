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

    [Fact]
    public async Task PlainDateTimeToLocaleString_PadsNumericH23HourWithResolvedTimeZone()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            new Temporal.PlainDateTime(2021, 8, 4, 0, 30, 45, 123, 456, 789)
                .toLocaleString("en", {
                    timeZone: "Pacific/Apia",
                    year: "numeric",
                    month: "numeric",
                    day: "numeric",
                    hour: "numeric",
                    minute: "numeric",
                    second: "numeric",
                    hourCycle: "h23"
                });
            """);

        Assert.Equal("8/4/2021, 00:30:45", result);
    }

    [Fact]
    public async Task PlainYearMonthToLocaleString_UsesCalendarAwareDateStyleMonthNames()
    {
        await using var engine = CreateEngine();

        var gregoryLong = await engine.Evaluate("""
            Temporal.PlainYearMonth
                .from({ year: 2024, monthCode: "M03", calendar: "gregory" })
                .toLocaleString("en-u-ca-gregory", { dateStyle: "long" });
            """);
        var gregoryShort = await engine.Evaluate("""
            Temporal.PlainYearMonth
                .from({ year: 2024, monthCode: "M03", calendar: "gregory" })
                .toLocaleString("en-u-ca-gregory", { dateStyle: "short" });
            """);
        var islamicLong = await engine.Evaluate("""
            Temporal.PlainYearMonth
                .from({ year: 1445, monthCode: "M09", calendar: "islamic-tbla" })
                .toLocaleString("en-u-ca-islamic-tbla", { dateStyle: "long" });
            """);
        var islamicShort = await engine.Evaluate("""
            Temporal.PlainYearMonth
                .from({ year: 1445, monthCode: "M09", calendar: "islamic-tbla" })
                .toLocaleString("en-u-ca-islamic-tbla", { dateStyle: "short" });
            """);

        Assert.Contains("March", Assert.IsType<string>(gregoryLong));
        Assert.DoesNotContain("March", Assert.IsType<string>(gregoryShort));
        Assert.Contains("Ramadan", Assert.IsType<string>(islamicLong));
        Assert.DoesNotContain("Ramadan", Assert.IsType<string>(islamicShort));
    }

    [Fact]
    public async Task PlainYearMonthToLocaleString_DateStyleDoesNotFormatReferenceDay()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            new Temporal.PlainYearMonth(2024, 5, "gregory", 31)
                .toLocaleString("en", { dateStyle: "full" });
            """);

        Assert.DoesNotContain("31", Assert.IsType<string>(result));
    }

    [Fact]
    public async Task PlainYearMonthToLocaleString_UndefinedDateStyleMatchesOmittedDateStyle()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const yearMonth = Temporal.PlainYearMonth.from({
                year: 2024,
                monthCode: "M03",
                calendar: "gregory"
            });
            yearMonth.toLocaleString("en-u-ca-gregory", { dateStyle: undefined }) ===
                yearMonth.toLocaleString("en-u-ca-gregory", {});
            """);

        Assert.Equal(true, result);
    }
}
