using System;
using System.Threading.Tasks;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class DateBuiltinsTests
{
    [Theory(Timeout = 2000)]
    [InlineData("new Date(Date.UTC(2001, 1, 3, 4, 5, 6, 789))",
        2001, 1, 3, 4, 5, 6, 789,
        "2001-02-03T04:05:06.789Z",
        "Sat, 03 Feb 2001 04:05:06 GMT")]
    [InlineData("new Date(Date.UTC(1970, 0, 1, 0, 0, 0, 0))",
        1970, 0, 1, 0, 0, 0, 0,
        "1970-01-01T00:00:00.000Z",
        "Thu, 01 Jan 1970 00:00:00 GMT")]
    [InlineData("new Date(Date.UTC(1999, 11, 31, 23, 59, 59, 999))",
        1999, 11, 31, 23, 59, 59, 999,
        "1999-12-31T23:59:59.999Z",
        "Fri, 31 Dec 1999 23:59:59 GMT")]
    public async Task Date_UtcAccessorsAndFormatting_MatchNode(
        string ctorExpression,
        int expectedYear,
        int expectedMonthZeroBased,
        int expectedDay,
        int expectedHour,
        int expectedMinute,
        int expectedSecond,
        int expectedMs,
        string expectedIso,
        string expectedUtcString)
    {
        await using var engine = new JsEngine();

        await engine.Evaluate($"var d = {ctorExpression};");

        var year = await engine.Evaluate("d.getUTCFullYear();");
        var month = await engine.Evaluate("d.getUTCMonth();");
        var date = await engine.Evaluate("d.getUTCDate();");
        var hour = await engine.Evaluate("d.getUTCHours();");
        var minute = await engine.Evaluate("d.getUTCMinutes();");
        var second = await engine.Evaluate("d.getUTCSeconds();");
        var ms = await engine.Evaluate("d.getUTCMilliseconds();");
        var iso = await engine.Evaluate("d.toISOString();");
        var utc = await engine.Evaluate("d.toUTCString();");
        var json = await engine.Evaluate("d.toJSON();");
        var valueOf = await engine.Evaluate("d.valueOf();");
        var getTime = await engine.Evaluate("d.getTime();");

        Assert.Equal((double)expectedYear, year);
        Assert.Equal((double)expectedMonthZeroBased, month);
        Assert.Equal((double)expectedDay, date);
        Assert.Equal((double)expectedHour, hour);
        Assert.Equal((double)expectedMinute, minute);
        Assert.Equal((double)expectedSecond, second);
        Assert.Equal((double)expectedMs, ms);
        Assert.Equal(expectedIso, iso);
        Assert.Equal(expectedUtcString, utc);
        Assert.Equal(expectedIso, json);

        // valueOf() and getTime() should both expose the underlying time value in ms.
        Assert.IsType<double>(valueOf);
        Assert.IsType<double>(getTime);
        Assert.Equal((double)getTime, valueOf);
    }

    [Theory(Timeout = 2000)]
    [InlineData(2001, 1, 3, 4, 5, 6, 789, 981173106789d)]
    [InlineData(1970, 0, 1, 0, 0, 0, 0, 0d)]
    [InlineData(1999, 11, 31, 23, 59, 59, 999, 946684799999d)]
    [InlineData(99, 0, 1, 0, 0, 0, 0, 915148800000d)]
    public async Task Date_UTC_MatchesNode(
        int year,
        int monthZeroBased,
        int day,
        int hour,
        int minute,
        int second,
        int ms,
        double expectedMs)
    {
        await using var engine = new JsEngine();

        var expr =
            $"Date.UTC({year}, {monthZeroBased}, {day}, {hour}, {minute}, {second}, {ms});";
        var result = await engine.Evaluate(expr);

        Assert.Equal(expectedMs, result);
    }
}
