using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

public class TemporalDebugTests
{
    [Fact]
    public async Task PlainYearMonthEquals_CanonicalizesCalendarAliases()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const instance = new Temporal.PlainYearMonth(2024, 6, 'islamic-civil', 8);
            const args = [
              '2024-06-08[u-ca=islamicc]',
              { year: 1445, month: 12, calendar: 'islamicc' },
            ];

            for (let i = 0; i < args.length; i++) {
              const arg = args[i];
              if (!instance.equals(arg)) {
                const converted = Temporal.PlainYearMonth.from(arg);
                throw new Error(
                  'calendar ID was not canonicalized for arg ' + i + ': ' +
                  converted.toString({ calendarName: 'always' }) + '/' +
                  converted.year + '/' + converted.month + '/' + converted.monthCode);
              }
            }

            'ok';
        ");

        Assert.Equal("ok", result?.ToString());
    }

    [Fact]
    public async Task PlainYearMonthFrom_AllowsIsoCalendarStringsInPropertyBag()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const calendars = [
              '2020-01-01',
              '2020-01-01[u-ca=iso8601]',
              '2020-01-01T00:00:00.000000000',
              '2020-01-01T00:00:00.000000000[u-ca=iso8601]',
              '01-01',
              '01-01[u-ca=iso8601]',
              '2020-01',
              '2020-01[u-ca=iso8601]',
            ];

            for (const calendar of calendars) {
              const value = Temporal.PlainYearMonth.from({ year: 2019, monthCode: 'M06', calendar });
              if (value.calendarId !== 'iso8601' || value.year !== 2019 || value.month !== 6 || value.monthCode !== 'M06') {
                throw new Error('bad PlainYearMonth calendar=' + calendar + ' => ' + value.calendarId + '/' + value.year + '/' + value.month + '/' + value.monthCode);
              }
            }

            'ok';
        ");

        Assert.Equal("ok", result?.ToString());
    }

    [Fact]
    public async Task ZonedDateTimeFrom_AllowsIsoCalendarStringsInPropertyBag()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const calendars = [
              '2020-01-01',
              '2020-01-01[u-ca=iso8601]',
              '2020-01-01T00:00:00.000000000',
              '2020-01-01T00:00:00.000000000[u-ca=iso8601]',
              '01-01',
              '01-01[u-ca=iso8601]',
              '2020-01',
              '2020-01[u-ca=iso8601]',
            ];

            for (const calendar of calendars) {
              const value = Temporal.ZonedDateTime.from({
                year: 1970,
                monthCode: 'M01',
                day: 1,
                timeZone: 'UTC',
                calendar,
              });
              if (value.calendarId !== 'iso8601') {
                throw new Error('bad ZonedDateTime calendar=' + calendar + ' => ' + value.calendarId);
              }
            }

            'ok';
        ");

        Assert.Equal("ok", result?.ToString());
    }

    [Fact]
    public async Task PlainYearMonthFrom_RequiresEraAndEraYearTogether()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const cases = [
              { year: 2000, month: 5, day: 2, era: 'ce', calendar: 'gregory' },
              { year: 2000, month: 5, day: 2, eraYear: 1, calendar: 'gregory' },
            ];

            for (const value of cases) {
              let threwTypeError = false;
              try {
                Temporal.PlainYearMonth.from(value);
              } catch (e) {
                threwTypeError = e instanceof TypeError;
              }

              if (!threwTypeError) {
                throw new Error('expected TypeError for ' + JSON.stringify(value));
              }
            }

            'ok';
        ");

        Assert.Equal("ok", result?.ToString());
    }

    [Fact]
    public async Task PlainYearMonthFrom_RemapsEraUsingMonthContext()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const gregory = Temporal.PlainYearMonth.from({
              calendar: 'gregory',
              era: 'ce',
              eraYear: 0,
              month: 4,
            });

            if (gregory.year !== 0 || gregory.era !== 'bce' || gregory.eraYear !== 1) {
              throw new Error('unexpected gregory remap: ' + gregory.year + '/' + gregory.era + '/' + gregory.eraYear);
            }

            const japanese = Temporal.PlainYearMonth.from({
              calendar: 'japanese',
              era: 'reiwa',
              eraYear: 1,
              month: 1,
            });

            if (japanese.year !== 2019 || japanese.era !== 'heisei' || japanese.eraYear !== 31) {
              throw new Error('unexpected japanese remap: ' + japanese.year + '/' + japanese.era + '/' + japanese.eraYear);
            }

            'ok';
        ");

        Assert.Equal("ok", result?.ToString());
    }

    [Fact]
    public async Task PlainDateFrom_IgnoresEraFieldsForCalendarsWithoutEraSupport()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const date = Temporal.PlainDate.from({
              calendar: 'hebrew',
              day: 2,
              get era() {
                throw new Error('era should not be read');
              },
              get eraYear() {
                throw new Error('eraYear should not be read');
              },
              monthCode: 'M01',
              year: 5780,
            });

            if (date.calendarId !== 'hebrew' || date.year !== 5780 || date.monthCode !== 'M01' || date.day !== 2) {
              throw new Error('unexpected PlainDate: ' + date.calendarId + '/' + date.year + '/' + date.monthCode + '/' + date.day);
            }

            'ok';
        ");

        Assert.Equal("ok", result?.ToString());
    }
}
