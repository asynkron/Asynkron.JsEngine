using Asynkron.JsEngine.JsTypes;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class TemporalTests(ITestOutputHelper output)
{
    private async Task<JsValue> Eval(string code)
    {
        var engine = new JsEngine();
        var result = await engine.EvaluateAsync(code);
        return result.JsValue;
    }

    [Fact]
    public async Task Temporal_Object_Exists()
    {
        var result = await Eval("typeof Temporal === 'object'");
        Assert.True(result.ToBoolean());
    }

    [Fact]
    public async Task Temporal_Now_Exists()
    {
        var result = await Eval("typeof Temporal.Now === 'object'");
        Assert.True(result.ToBoolean());
    }

    [Fact]
    public async Task Temporal_Now_Instant_Works()
    {
        var result = await Eval("Temporal.Now.instant() !== undefined");
        Assert.True(result.ToBoolean());
    }

    [Fact]
    public async Task Temporal_Now_TimeZoneId_Returns_String()
    {
        var result = await Eval("typeof Temporal.Now.timeZoneId() === 'string'");
        Assert.True(result.ToBoolean());
    }

    [Fact]
    public async Task Temporal_Instant_Exists()
    {
        var result = await Eval("typeof Temporal.Instant === 'function'");
        Assert.True(result.ToBoolean());
    }

    [Fact]
    public async Task Temporal_Duration_Exists()
    {
        var result = await Eval("typeof Temporal.Duration === 'function'");
        Assert.True(result.ToBoolean());
    }

    [Fact]
    public async Task Temporal_PlainDate_Exists()
    {
        var result = await Eval("typeof Temporal.PlainDate === 'function'");
        Assert.True(result.ToBoolean());
    }

    [Fact]
    public async Task Temporal_PlainTime_Exists()
    {
        var result = await Eval("typeof Temporal.PlainTime === 'function'");
        Assert.True(result.ToBoolean());
    }

    [Fact]
    public async Task Temporal_PlainDateTime_Exists()
    {
        var result = await Eval("typeof Temporal.PlainDateTime === 'function'");
        Assert.True(result.ToBoolean());
    }

    [Fact]
    public async Task Temporal_Instant_FromEpochMilliseconds()
    {
        var result = await Eval("Temporal.Instant.fromEpochMilliseconds(0).epochMilliseconds");
        Assert.Equal(0d, result.GetNumber());
    }

    [Fact]
    public async Task Temporal_Duration_From_Object()
    {
        var result = await Eval("Temporal.Duration.from({hours: 1, minutes: 30}).hours");
        Assert.Equal(1d, result.GetNumber());
    }

    [Fact]
    public async Task Temporal_PlainDate_Constructor()
    {
        var result = await Eval("new Temporal.PlainDate(2024, 12, 25).year");
        Assert.Equal(2024d, result.GetNumber());
    }

    [Fact]
    public async Task Temporal_PlainTime_Constructor()
    {
        var result = await Eval("new Temporal.PlainTime(10, 30, 0).hour");
        Assert.Equal(10d, result.GetNumber());
    }

    [Fact]
    public async Task Temporal_PlainDateTime_Constructor()
    {
        var result = await Eval("new Temporal.PlainDateTime(2024, 12, 25, 10, 30).month");
        Assert.Equal(12d, result.GetNumber());
    }

    [Fact]
    public async Task Temporal_PlainDate_ToString()
    {
        var result = await Eval("new Temporal.PlainDate(2024, 12, 25).toString()");
        Assert.Equal("2024-12-25", result.GetString());
    }

    [Fact]
    public async Task Temporal_PlainTime_ToString()
    {
        var result = await Eval("new Temporal.PlainTime(10, 30, 45).toString()");
        Assert.Equal("10:30:45", result.GetString());
    }

    [Fact]
    public async Task Temporal_PlainDateTime_ToString()
    {
        var result = await Eval("new Temporal.PlainDateTime(2024, 12, 25, 10, 30).toString()");
        Assert.Equal("2024-12-25T10:30:00", result.GetString());
    }

    [Fact]
    public async Task Temporal_Duration_ToString()
    {
        var result = await Eval("Temporal.Duration.from({hours: 1, minutes: 30}).toString()");
        Assert.Equal("PT1H30M", result.GetString());
    }

    [Fact]
    public async Task Temporal_Instant_ToString()
    {
        var result = await Eval("Temporal.Instant.fromEpochMilliseconds(0).toString()");
        Assert.Equal("1970-01-01T00:00:00Z", result.GetString());
    }
}
