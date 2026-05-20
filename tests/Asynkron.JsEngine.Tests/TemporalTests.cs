using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibTemporal)]
public sealed class TemporalTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task Temporal_Object_Exists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof Temporal === 'object'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_Now_Exists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof Temporal.Now === 'object'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_Now_Instant_Works()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Temporal.Now.instant() !== undefined");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_Now_TimeZoneId_Returns_String()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof Temporal.Now.timeZoneId() === 'string'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_Instant_Exists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof Temporal.Instant === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_Duration_Exists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof Temporal.Duration === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_PlainDate_Exists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof Temporal.PlainDate === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_PlainTime_Exists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof Temporal.PlainTime === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_PlainDateTime_Exists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof Temporal.PlainDateTime === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_Instant_FromEpochMilliseconds()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Temporal.Instant.fromEpochMilliseconds(0).epochMilliseconds");
        Assert.Equal(0d, result);
    }

    [Fact]
    public async Task Temporal_Duration_From_Object()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Temporal.Duration.from({hours: 1, minutes: 30}).hours");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Temporal_PlainDate_Constructor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainDate(2024, 12, 25).year");
        Assert.Equal(2024d, result);
    }

    [Fact]
    public async Task Temporal_PlainTime_Constructor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainTime(10, 30, 0).hour");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Temporal_PlainDateTime_Constructor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainDateTime(2024, 12, 25, 10, 30).month");
        Assert.Equal(12d, result);
    }

    [Fact]
    public async Task Temporal_PlainDate_ToString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainDate(2024, 12, 25).toString()");
        Assert.Equal("2024-12-25", result);
    }

    [Fact]
    public async Task Temporal_PlainTime_ToString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainTime(10, 30, 45).toString()");
        Assert.Equal("10:30:45", result);
    }

    [Fact]
    public async Task Temporal_PlainDateTime_ToString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainDateTime(2024, 12, 25, 10, 30).toString()");
        Assert.Equal("2024-12-25T10:30:00", result);
    }

    [Fact]
    public async Task Temporal_Duration_ToString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Temporal.Duration.from({hours: 1, minutes: 30}).toString()");
        Assert.Equal("PT1H30M", result);
    }

    [Fact]
    public async Task Temporal_Duration_Total_ZonedDateTime_NegativeCalendarFractions()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const relativeTo = new Temporal.ZonedDateTime(0n, 'UTC');
            const oneDayBack = new Temporal.Duration(0, 0, 0, -1);
            const values = [
                [oneDayBack.total({ unit: 'week', relativeTo }), -1 / 7],
                [oneDayBack.total({ unit: 'month', relativeTo }), -1 / 31],
                [oneDayBack.total({ unit: 'year', relativeTo }), -1 / 365],
            ];

            for (const [actual, expected] of values) {
                if (Math.abs(actual - expected) > 1e-12) {
                    throw new Error(actual + ' !== ' + expected);
                }
            }

            'ok';
        ");

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task Temporal_Instant_ToString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Temporal.Instant.fromEpochMilliseconds(0).toString()");
        Assert.Equal("1970-01-01T00:00:00Z", result);
    }

    // ZonedDateTime tests
    [Fact]
    public async Task Temporal_ZonedDateTime_Exists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof Temporal.ZonedDateTime === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_ZonedDateTime_Constructor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.ZonedDateTime(BigInt(0), 'UTC').epochMilliseconds");
        Assert.Equal(0d, result);
    }

    [Fact]
    public async Task Temporal_ZonedDateTime_Properties()
    {
        await using var engine = CreateEngine();
        // Use BigInt constructor to avoid parsing issues
        var year = await engine.Evaluate("new Temporal.ZonedDateTime(BigInt(0), 'UTC').year");
        var month = await engine.Evaluate("new Temporal.ZonedDateTime(BigInt(0), 'UTC').month");
        var day = await engine.Evaluate("new Temporal.ZonedDateTime(BigInt(0), 'UTC').day");
        Assert.Equal(1970d, year);
        Assert.Equal(1d, month);
        Assert.Equal(1d, day);
    }

    [Fact]
    public async Task Temporal_ZonedDateTime_TimeZoneId()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.ZonedDateTime(BigInt(0), 'UTC').timeZoneId");
        Assert.Equal("UTC", result);
    }

    // PlainYearMonth tests
    [Fact]
    public async Task Temporal_PlainYearMonth_Exists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof Temporal.PlainYearMonth === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_PlainYearMonth_Constructor()
    {
        await using var engine = CreateEngine();
        var year = await engine.Evaluate("new Temporal.PlainYearMonth(2024, 12).year");
        var month = await engine.Evaluate("new Temporal.PlainYearMonth(2024, 12).month");
        Assert.Equal(2024d, year);
        Assert.Equal(12d, month);
    }

    [Fact]
    public async Task Temporal_PlainYearMonth_ToString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainYearMonth(2024, 12).toString()");
        Assert.Equal("2024-12", result);
    }

    [Fact]
    public async Task Temporal_PlainYearMonth_DaysInMonth()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainYearMonth(2024, 2).daysInMonth");
        Assert.Equal(29d, result); // 2024 is a leap year
    }

    [Fact]
    public async Task Temporal_PlainYearMonth_InLeapYear()
    {
        await using var engine = CreateEngine();
        var leap = await engine.Evaluate("new Temporal.PlainYearMonth(2024, 1).inLeapYear");
        var notLeap = await engine.Evaluate("new Temporal.PlainYearMonth(2023, 1).inLeapYear");
        Assert.Equal(true, leap);
        Assert.Equal(false, notLeap);
    }

    // PlainMonthDay tests
    [Fact]
    public async Task Temporal_PlainMonthDay_Exists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof Temporal.PlainMonthDay === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_PlainMonthDay_Constructor()
    {
        await using var engine = CreateEngine();
        var monthCode = await engine.Evaluate("new Temporal.PlainMonthDay(12, 25).monthCode");
        var day = await engine.Evaluate("new Temporal.PlainMonthDay(12, 25).day");
        Assert.Equal("M12", monthCode);
        Assert.Equal(25d, day);
    }

    [Fact]
    public async Task Temporal_PlainMonthDay_ToString()
    {
        await using var engine = CreateEngine();
        // Per Temporal spec TemporalMonthDayToString, ISO calendar uses MM-DD format
        var result = await engine.Evaluate("new Temporal.PlainMonthDay(12, 25).toString()");
        Assert.Equal("12-25", result);
    }

    [Fact]
    public async Task Temporal_PlainMonthDay_MonthCode()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainMonthDay(1, 15).monthCode");
        Assert.Equal("M01", result);
    }

    [Fact]
    public async Task Temporal_PlainMonthDay_ToPlainDate()
    {
        await using var engine = CreateEngine();
        // toPlainDate returns an object, verify it's callable
        var result = await engine.Evaluate("typeof new Temporal.PlainMonthDay(12, 25).toPlainDate({year: 2024})");
        Assert.Equal("object", result);
    }

    // Tests for newly added prototype methods

    // PlainDate methods
    [Fact]
    public async Task Temporal_PlainDate_With()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainDate(2024, 1, 15).with({month: 6}).toString()");
        Assert.Equal("2024-06-15", result);
    }

    [Fact]
    public async Task Temporal_PlainDate_Until()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainDate(2024, 1, 1).until(new Temporal.PlainDate(2024, 1, 11)).days");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Temporal_PlainDate_Until_UsesNonIsoCalendarMonthsAcrossLeapMonth()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const year2000 = new Temporal.PlainDate(2000, 3, 1).withCalendar('chinese').year;
            const year2001 = new Temporal.PlainDate(2001, 3, 1).withCalendar('chinese').year;
            const one = Temporal.PlainDate.from({ year: year2000, month: 6, day: 1, calendar: 'chinese' });
            const two = Temporal.PlainDate.from({ year: year2001, month: 6, day: 1, calendar: 'chinese' });

            [
                one.inLeapYear,
                one.monthCode,
                two.inLeapYear,
                two.monthCode,
                one.until(two, { largestUnit: 'years' }).toString(),
                one.until(two, { largestUnit: 'months' }).toString(),
                one.until(two, { largestUnit: 'weeks' }).toString(),
                one.until(two, { largestUnit: 'days' }).toString(),
            ].join('|');
        ");

        Assert.Equal("false|M06|true|M05|P12M|P12M|P50W4D|P354D", result);
    }

    [Fact]
    public async Task Temporal_PlainDate_Since()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainDate(2024, 1, 11).since(new Temporal.PlainDate(2024, 1, 1)).days");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Temporal_PlainDate_ToPlainDateTime()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainDate(2024, 12, 25).toPlainDateTime({hour: 10, minute: 30}).toString()");
        Assert.Equal("2024-12-25T10:30:00", result);
    }

    [Fact]
    public async Task Temporal_PlainDate_ToPlainYearMonth()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainDate(2024, 12, 25).toPlainYearMonth().toString()");
        Assert.Equal("2024-12", result);
    }

    [Fact]
    public async Task Temporal_PlainDate_ToPlainMonthDay()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainDate(2024, 12, 25).toPlainMonthDay().toString()");
        Assert.Equal("12-25", result);
    }

    // PlainTime methods
    [Fact]
    public async Task Temporal_PlainTime_With()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainTime(10, 30, 0).with({hour: 14}).toString()");
        Assert.Equal("14:30:00", result);
    }

    [Fact]
    public async Task Temporal_PlainTime_Round()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainTime(10, 30, 45, 500).round('second').toString()");
        Assert.Equal("10:30:46", result);
    }

    // PlainDateTime methods
    [Fact]
    public async Task Temporal_PlainDateTime_Add()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainDateTime(2024, 1, 1, 10, 0).add({days: 5}).day");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task Temporal_PlainDateTime_Subtract()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainDateTime(2024, 1, 10, 10, 0).subtract({days: 5}).day");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Temporal_PlainDateTime_With()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.PlainDateTime(2024, 1, 1, 10, 0).with({hour: 15}).hour");
        Assert.Equal(15d, result);
    }

    // Duration methods
    [Fact]
    public async Task Temporal_Duration_With()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Temporal.Duration.from({hours: 1, minutes: 30}).with({hours: 2}).hours");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task Temporal_Duration_Blank()
    {
        await using var engine = CreateEngine();
        // Per spec: Duration.from({}) throws TypeError (no duration properties)
        // Use new Temporal.Duration() instead for zero duration
        var blank = await engine.Evaluate("new Temporal.Duration().blank");
        var notBlank = await engine.Evaluate("Temporal.Duration.from({hours: 1}).blank");
        Assert.Equal(true, blank);
        Assert.Equal(false, notBlank);
    }

    // Instant methods
    [Fact]
    public async Task Temporal_Instant_Add()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Temporal.Instant.fromEpochMilliseconds(0).add({hours: 1}).epochMilliseconds");
        Assert.Equal(3600000d, result);
    }

    [Fact]
    public async Task Temporal_Instant_Subtract()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Temporal.Instant.fromEpochMilliseconds(3600000).subtract({hours: 1}).epochMilliseconds");
        Assert.Equal(0d, result);
    }

    // ZonedDateTime methods
    [Fact]
    public async Task Temporal_ZonedDateTime_Add()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Temporal.ZonedDateTime(BigInt(0), 'UTC').add({hours: 1}).epochMilliseconds");
        Assert.Equal(3600000d, result);
    }

    [Fact]
    public async Task Temporal_ZonedDateTime_StartOfDay()
    {
        await using var engine = CreateEngine();
        var hour = await engine.Evaluate("new Temporal.ZonedDateTime(BigInt(3600000000000), 'UTC').startOfDay().hour");
        var minute = await engine.Evaluate("new Temporal.ZonedDateTime(BigInt(3600000000000), 'UTC').startOfDay().minute");
        Assert.Equal(0d, hour);
        Assert.Equal(0d, minute);
    }

    [Fact]
    public async Task Temporal_ZonedDateTime_Equals()
    {
        await using var engine = CreateEngine();
        var eq = await engine.Evaluate("new Temporal.ZonedDateTime(BigInt(0), 'UTC').equals(new Temporal.ZonedDateTime(BigInt(0), 'UTC'))");
        var neq = await engine.Evaluate("new Temporal.ZonedDateTime(BigInt(0), 'UTC').equals(new Temporal.ZonedDateTime(BigInt(1000000000), 'UTC'))");
        Assert.Equal(true, eq);
        Assert.Equal(false, neq);
    }

[Fact]
    public async Task Temporal_Now_ZonedDateTimeISO()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof Temporal.Now.zonedDateTimeISO() === 'object'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task PlainMonthDay_From_ShortFormat()
    {
        await using var engine = CreateEngine();
        // Both MM-DD and --MM-DD should be accepted
        var r1 = await engine.Evaluate("Temporal.PlainMonthDay.from('01-15').day === 15");
        Assert.Equal(true, r1);

        var r2 = await engine.Evaluate("Temporal.PlainMonthDay.from('--01-15').day === 15");
        Assert.Equal(true, r2);

        // Check month
        var r3 = await engine.Evaluate("Temporal.PlainMonthDay.from('01-15').monthCode");
        Assert.Equal("M01", r3);
    }

}
