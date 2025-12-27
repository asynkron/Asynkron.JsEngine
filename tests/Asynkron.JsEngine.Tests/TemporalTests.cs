namespace Asynkron.JsEngine.Tests;

public class TemporalTests
{
    [Fact]
    public async Task Temporal_Object_Exists()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof Temporal === 'object'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_Now_Exists()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof Temporal.Now === 'object'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_Now_Instant_Works()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Temporal.Now.instant() !== undefined");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_Now_TimeZoneId_Returns_String()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof Temporal.Now.timeZoneId() === 'string'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_Instant_Exists()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof Temporal.Instant === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_Duration_Exists()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof Temporal.Duration === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_PlainDate_Exists()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof Temporal.PlainDate === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_PlainTime_Exists()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof Temporal.PlainTime === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_PlainDateTime_Exists()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof Temporal.PlainDateTime === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_Instant_FromEpochMilliseconds()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Temporal.Instant.fromEpochMilliseconds(0).epochMilliseconds");
        Assert.Equal(0d, result);
    }

    [Fact]
    public async Task Temporal_Duration_From_Object()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Temporal.Duration.from({hours: 1, minutes: 30}).hours");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Temporal_PlainDate_Constructor()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("new Temporal.PlainDate(2024, 12, 25).year");
        Assert.Equal(2024d, result);
    }

    [Fact]
    public async Task Temporal_PlainTime_Constructor()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("new Temporal.PlainTime(10, 30, 0).hour");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Temporal_PlainDateTime_Constructor()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("new Temporal.PlainDateTime(2024, 12, 25, 10, 30).month");
        Assert.Equal(12d, result);
    }

    [Fact]
    public async Task Temporal_PlainDate_ToString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("new Temporal.PlainDate(2024, 12, 25).toString()");
        Assert.Equal("2024-12-25", result);
    }

    [Fact]
    public async Task Temporal_PlainTime_ToString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("new Temporal.PlainTime(10, 30, 45).toString()");
        Assert.Equal("10:30:45", result);
    }

    [Fact]
    public async Task Temporal_PlainDateTime_ToString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("new Temporal.PlainDateTime(2024, 12, 25, 10, 30).toString()");
        Assert.Equal("2024-12-25T10:30:00", result);
    }

    [Fact]
    public async Task Temporal_Duration_ToString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Temporal.Duration.from({hours: 1, minutes: 30}).toString()");
        Assert.Equal("PT1H30M", result);
    }

    [Fact]
    public async Task Temporal_Instant_ToString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Temporal.Instant.fromEpochMilliseconds(0).toString()");
        Assert.Equal("1970-01-01T00:00:00Z", result);
    }

    // ZonedDateTime tests
    [Fact]
    public async Task Temporal_ZonedDateTime_Exists()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof Temporal.ZonedDateTime === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_ZonedDateTime_Constructor()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("new Temporal.ZonedDateTime(BigInt(0), 'UTC').epochMilliseconds");
        Assert.Equal(0d, result);
    }

    [Fact]
    public async Task Temporal_ZonedDateTime_Properties()
    {
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("new Temporal.ZonedDateTime(BigInt(0), 'UTC').timeZoneId");
        Assert.Equal("UTC", result);
    }

    // PlainYearMonth tests
    [Fact]
    public async Task Temporal_PlainYearMonth_Exists()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof Temporal.PlainYearMonth === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_PlainYearMonth_Constructor()
    {
        await using var engine = new JsEngine();
        var year = await engine.Evaluate("new Temporal.PlainYearMonth(2024, 12).year");
        var month = await engine.Evaluate("new Temporal.PlainYearMonth(2024, 12).month");
        Assert.Equal(2024d, year);
        Assert.Equal(12d, month);
    }

    [Fact]
    public async Task Temporal_PlainYearMonth_ToString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("new Temporal.PlainYearMonth(2024, 12).toString()");
        Assert.Equal("2024-12", result);
    }

    [Fact]
    public async Task Temporal_PlainYearMonth_DaysInMonth()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("new Temporal.PlainYearMonth(2024, 2).daysInMonth");
        Assert.Equal(29d, result); // 2024 is a leap year
    }

    [Fact]
    public async Task Temporal_PlainYearMonth_InLeapYear()
    {
        await using var engine = new JsEngine();
        var leap = await engine.Evaluate("new Temporal.PlainYearMonth(2024, 1).inLeapYear");
        var notLeap = await engine.Evaluate("new Temporal.PlainYearMonth(2023, 1).inLeapYear");
        Assert.Equal(true, leap);
        Assert.Equal(false, notLeap);
    }

    // PlainMonthDay tests
    [Fact]
    public async Task Temporal_PlainMonthDay_Exists()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof Temporal.PlainMonthDay === 'function'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Temporal_PlainMonthDay_Constructor()
    {
        await using var engine = new JsEngine();
        var month = await engine.Evaluate("new Temporal.PlainMonthDay(12, 25).month");
        var day = await engine.Evaluate("new Temporal.PlainMonthDay(12, 25).day");
        Assert.Equal(12d, month);
        Assert.Equal(25d, day);
    }

    [Fact]
    public async Task Temporal_PlainMonthDay_ToString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("new Temporal.PlainMonthDay(12, 25).toString()");
        Assert.Equal("--12-25", result);
    }

    [Fact]
    public async Task Temporal_PlainMonthDay_MonthCode()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("new Temporal.PlainMonthDay(1, 15).monthCode");
        Assert.Equal("M01", result);
    }

    [Fact]
    public async Task Temporal_PlainMonthDay_ToPlainDate()
    {
        await using var engine = new JsEngine();
        // toPlainDate returns an object, verify it's callable
        var result = await engine.Evaluate("typeof new Temporal.PlainMonthDay(12, 25).toPlainDate({year: 2024})");
        Assert.Equal("object", result);
    }
}
