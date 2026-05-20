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

            if (gregory.year !== 0 || gregory.era !== 'gregory-inverse' || gregory.eraYear !== 1) {
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
    public async Task PlainYearMonthWith_PreservesNonIsoCalendarMonthCodeDefaults()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const instance = Temporal.PlainYearMonth.from({ calendar: 'hebrew', year: 5784, monthCode: 'M11' });

            const resultYear = instance.with({ year: 5783 });
            if (resultYear.year !== 5783 || resultYear.month !== 11 || resultYear.monthCode !== 'M11') {
                throw new Error('bad year override: ' + resultYear.year + '/' + resultYear.month + '/' + resultYear.monthCode);
            }

            const resultMonth = instance.with({ month: 13 });
            if (resultMonth.year !== 5784 || resultMonth.month !== 13 || resultMonth.monthCode !== 'M12') {
                throw new Error('bad month override: ' + resultMonth.year + '/' + resultMonth.month + '/' + resultMonth.monthCode);
            }

            const resultMonthCode = instance.with({ monthCode: 'M10' });
            if (resultMonthCode.year !== 5784 || resultMonthCode.month !== 11 || resultMonthCode.monthCode !== 'M10') {
                throw new Error('bad monthCode override: ' + resultMonthCode.year + '/' + resultMonthCode.month + '/' + resultMonthCode.monthCode);
            }

            'ok';
        ");

        Assert.Equal("ok", result?.ToString());
    }

    [Fact]
    public async Task PlainYearMonthWith_AllowsMinimumGregorianYearMonth()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const apr2000 = new Temporal.PlainYearMonth(2000, 4, 'gregory');
            const result = apr2000.with({ year: -271821 });

            if (result.year !== -271821 || result.month !== 4 || result.monthCode !== 'M04' ||
                result.toString({ calendarName: 'always' }) !== '-271821-04-01[u-ca=gregory]') {
                throw new Error('bad minimum gregory PlainYearMonth: ' + result.toString({ calendarName: 'always' }));
            }

            'ok';
        ");

        Assert.Equal("ok", result?.ToString());
    }

    [Fact]
    public async Task ZonedDateTimeFrom_CanonicalizesGregorianEraCodes()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const ce = Temporal.ZonedDateTime.from({
              calendar: 'gregory',
              era: 'ce',
              eraYear: 2024,
              year: 2024,
              month: 1,
              day: 1,
              timeZone: 'UTC',
            });

            if (ce.era !== 'gregory' || ce.eraYear !== 2024) {
              throw new Error('unexpected CE era: ' + ce.era + '/' + ce.eraYear);
            }

            const bce = Temporal.ZonedDateTime.from({
              calendar: 'gregory',
              era: 'bce',
              eraYear: 44,
              year: -43,
              month: 3,
              day: 15,
              timeZone: 'Europe/Rome',
            });

            if (bce.era !== 'gregory-inverse' || bce.eraYear !== 44) {
              throw new Error('unexpected BCE era: ' + bce.era + '/' + bce.eraYear);
            }

            'ok';
        ");

        Assert.Equal("ok", result?.ToString());
    }

    [Fact]
    public async Task ZonedDateTimeFrom_UsesCompatibleDisambiguationByDefault()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const timeZone = 'America/Vancouver';
            const ambiguous = Temporal.ZonedDateTime.from({
              timeZone,
              year: 2000,
              month: 10,
              day: 29,
              hour: 1,
              minute: 34,
              second: 56,
              millisecond: 987,
              microsecond: 654,
              nanosecond: 321,
            });
            if (ambiguous.epochNanoseconds !== 972808496987654321n) {
              throw new Error('unexpected ambiguous instant: ' + ambiguous.epochNanoseconds);
            }

            const skipped = Temporal.ZonedDateTime.from({
              timeZone,
              year: 2000,
              month: 4,
              day: 2,
              hour: 2,
              minute: 34,
              second: 56,
              millisecond: 987,
              microsecond: 654,
              nanosecond: 321,
            });
            if (skipped.epochNanoseconds !== 954671696987654321n) {
              throw new Error('unexpected skipped instant: ' + skipped.epochNanoseconds);
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

    [Fact]
    public async Task PlainYearMonthWith_HebrewLeapReceiverYearOnlyOverride_ConstrainDoesNotThrow()
    {
        // Hebrew year 5782 is a leap year (monthCode M05L = Adar I exists).
        // Dynamically find a non-leap target year using from() reject to let the BCL
        // HebrewCalendar decide what years are non-leap (avoids formula discrepancies).
        // Changing only the year to a non-leap year with the default overflow ("constrain")
        // must NOT throw. Regression for pre-options leap-month resolution trap per ADR 0067.
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const leap = Temporal.PlainYearMonth.from({ calendar: 'hebrew', year: 5782, monthCode: 'M05L' });
            if (leap.monthCode !== 'M05L') throw new Error('expected leap receiver monthCode M05L, got: ' + leap.monthCode);

            // Find a non-leap year by asking the engine: years where M05L rejects
            let nonLeapYear = null;
            for (let y = 5783; y <= 5800; y++) {
                try {
                    Temporal.PlainYearMonth.from({ calendar: 'hebrew', year: y, monthCode: 'M05L' }, { overflow: 'reject' });
                    // from() succeeded: y is a leap year, try next
                } catch (e) {
                    if (e instanceof RangeError) { nonLeapYear = y; break; }
                }
            }
            if (nonLeapYear === null) throw new Error('could not find a non-leap Hebrew year between 5783 and 5800');

            let constrained;
            try {
                constrained = leap.with({ year: nonLeapYear });
            } catch (e) {
                throw new Error('constrain should not throw for Hebrew leap receiver + non-leap target year ' + nonLeapYear + ': ' + e.message);
            }
            if (constrained.year !== nonLeapYear) throw new Error('expected year ' + nonLeapYear + ', got: ' + constrained.year);
            if (constrained.monthCode === 'M05L') throw new Error('constrained monthCode should not be M05L for non-leap year ' + nonLeapYear);
            'ok';
        ");

        Assert.Equal("ok", result?.ToString());
    }

    [Fact]
    public async Task PlainYearMonthWith_HebrewLeapReceiverYearOnlyOverride_RejectThrows()
    {
        // Same dynamic-discovery approach as the constrain test.
        // For the non-leap target year, overflow: "reject" must throw RangeError
        // because M05L is not valid for that year.
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            const leap = Temporal.PlainYearMonth.from({ calendar: 'hebrew', year: 5782, monthCode: 'M05L' });

            let nonLeapYear = null;
            for (let y = 5783; y <= 5800; y++) {
                try {
                    Temporal.PlainYearMonth.from({ calendar: 'hebrew', year: y, monthCode: 'M05L' }, { overflow: 'reject' });
                } catch (e) {
                    if (e instanceof RangeError) { nonLeapYear = y; break; }
                }
            }
            if (nonLeapYear === null) throw new Error('could not find a non-leap Hebrew year between 5783 and 5800');

            let threw = false;
            try {
                leap.with({ year: nonLeapYear }, { overflow: 'reject' });
            } catch (e) {
                if (e instanceof RangeError) threw = true;
                else throw new Error('expected RangeError, got: ' + e);
            }
            if (!threw) throw new Error('expected RangeError for M05L with { year: ' + nonLeapYear + ', overflow: reject }');
            'ok';
        ");

        Assert.Equal("ok", result?.ToString());
    }
}
