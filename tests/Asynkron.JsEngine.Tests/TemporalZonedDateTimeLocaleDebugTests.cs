using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

public class TemporalZonedDateTimeLocaleDebugTests
{
    [Fact]
    public async Task ToLocaleString_DefaultIncludesTimeAndTimeZoneName()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const epochMs = 1735213600_321;
            const epochNs = BigInt(epochMs) * 1_000_000n;
            const legacyDate = new Date(epochMs);
            const zonedDateTime = new Temporal.ZonedDateTime(epochNs, 'UTC');
            const left = zonedDateTime.toLocaleString('en');
            const right = legacyDate.toLocaleString('en', {
              year: 'numeric',
              month: 'numeric',
              day: 'numeric',
              hour: 'numeric',
              minute: 'numeric',
              second: 'numeric',
              timeZoneName: 'short',
              timeZone: 'UTC',
            });
            left + '|' + right;
        ");

        var text = result?.ToString();
        Assert.NotNull(text);
        var parts = text!.Split('|');
        Assert.Equal(parts[1], parts[0]);
    }

    [Fact]
    public async Task ToLocaleString_RejectsTimeZoneOption()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const datetime = new Temporal.ZonedDateTime(0n, 'UTC');
            try {
              datetime.toLocaleString('en', { timeZone: 'Asia/Tokyo' });
              return 'no throw';
            } catch (e) {
              return e.constructor.name;
            }
        ");

        Assert.Equal("TypeError", result?.ToString());
    }

    [Fact]
    public async Task ToLocaleString_UsesCanonicalizedInstanceTimeZone()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const datetime1 = new Temporal.ZonedDateTime(0n, 'Asia/Kolkata');
            const datetime2 = new Temporal.ZonedDateTime(0n, 'Asia/Calcutta');
            String(datetime1.toLocaleString() === datetime2.toLocaleString());
        ");

        Assert.Equal("true", result?.ToString());
    }

    [Fact]
    public async Task ToLocaleString_ThrowsOnCalendarMismatch()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const localeCalendar = new Intl.DateTimeFormat().resolvedOptions().calendar;
            const calendars = new Set(Intl.supportedValuesOf('calendar'));
            calendars.delete('iso8601');
            calendars.delete(localeCalendar);
            const differentCalendar = calendars.values().next().value;
            const differentCalendarInstance = new Temporal.ZonedDateTime(0n, 'UTC', differentCalendar);
            try {
              differentCalendarInstance.toLocaleString();
              return 'no throw';
            } catch (e) {
              return e.constructor.name;
            }
        ");

        Assert.Equal("RangeError", result?.ToString());
    }
}
