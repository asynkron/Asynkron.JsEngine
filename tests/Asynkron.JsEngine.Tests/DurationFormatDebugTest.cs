namespace Asynkron.JsEngine.Tests;

public class DurationFormatDebugTest
{
    [Fact(Timeout = 2000)]
    public async Task ResolvedOptionsCheck()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var df = new Intl.DurationFormat('en');
            var opts = df.resolvedOptions();
            var keys = Object.keys(opts);
            var result = '';
            for (var i = 0; i < keys.length; i++) {
              result += keys[i] + '=' + opts[keys[i]] + '\n';
            }
            result;
        ");
        var str = Assert.IsType<string>(result.ToString());
        Assert.Contains("years=short", str);
    }

    [Fact(Timeout = 2000)]
    public async Task FormatDefaultStyle()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var df = new Intl.DurationFormat('en');
            var duration = {years: 1, months: 2, weeks: 3, days: 3, hours: 4, minutes: 5, seconds: 6, milliseconds: 7, microseconds: 8, nanoseconds: 9};
            df.format(duration);
        ");
        var str = Assert.IsType<string>(result.ToString());
        Assert.NotEmpty(str);
    }

    [Fact(Timeout = 2000)]
    public async Task OutOfRangeSeconds()
    {
        await using var engine = new JsEngine();
        // seconds = 2^53 should throw RangeError
        var result = await engine.Evaluate(@"
            var df = new Intl.DurationFormat();
            var info = '';
            try {
                var r = df.format({seconds: 9007199254740992});
                info = 'no throw, result=' + r;
            } catch (e) {
                info = 'threw ' + e.constructor.name + ': ' + e.message;
            }
            info;
        ");
        var str = result?.ToString();
        Assert.Contains("threw RangeError", str);
    }

    [Fact(Timeout = 2000)]
    public async Task UnicodeGroupName()
    {
        await using var engine = new JsEngine();

        // Test supplementary plane chars in group names
        var result = await engine.Evaluate(@"
            var m = 'fox'.match(/(?<\u{1d453}\u{1d45c}\u{1d465}>fox)/);
            m !== null ? 'ok' : 'null';
        ");
        Assert.Equal("ok", result?.ToString());

        // Test ZWNJ in group names
        var result2 = await engine.Evaluate(@"
            /(?<_\u200C>a)/.exec('bab').groups._\u200C;
        ");
        Assert.Equal("a", result2?.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task HarnessSimulation()
    {
        await using var engine = new JsEngine();
        // Simulate what the test harness does
        var result = await engine.Evaluate(@"
            var df = new Intl.DurationFormat('en');
            var duration = {years: 1, months: 2, weeks: 3, days: 3, hours: 4, minutes: 5, seconds: 6, milliseconds: 7, microseconds: 8, nanoseconds: 9};
            var options = df.resolvedOptions();

            var units = ['years', 'months', 'weeks', 'days', 'hours', 'minutes', 'seconds', 'milliseconds', 'microseconds', 'nanoseconds'];
            var results = [];
            for (var i = 0; i < units.length; i++) {
                var unit = units[i];
                var value = duration[unit] || 0;
                var style = options[unit];
                var display = options[unit + 'Display'];

                if (value !== 0 || display !== 'auto') {
                    var nfOpts = Object.create(null);
                    nfOpts.numberingSystem = options.numberingSystem;

                    if (style !== 'numeric' && style !== '2-digit') {
                        nfOpts.style = 'unit';
                        nfOpts.unit = unit.slice(0, -1);
                        nfOpts.unitDisplay = style;
                    } else {
                        nfOpts.useGrouping = false;
                    }

                    var nf = new Intl.NumberFormat('en', nfOpts);
                    results.push(unit + ': style=' + style + ' formatted=' + nf.format(value));
                }
            }
            results.join('\\n');
        ");
        var str = Assert.IsType<string>(result.ToString());
        Assert.Contains("years:", str);
    }

    [Fact(Timeout = 2000)]
    public async Task FormatLongStyleMatchesIntlHarnessPattern()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function durationToFractional(duration, exponent) {
              let q = 0;
              let r = 0;
              if (exponent === 9) {
                q = (duration.seconds ?? 0) * 1_000_000_000 + (duration.milliseconds ?? 0) * 1_000_000 + (duration.microseconds ?? 0) * 1_000 + (duration.nanoseconds ?? 0);
              } else if (exponent === 6) {
                q = (duration.milliseconds ?? 0) * 1_000_000 + (duration.microseconds ?? 0) * 1_000 + (duration.nanoseconds ?? 0);
              } else {
                q = (duration.microseconds ?? 0) * 1_000 + (duration.nanoseconds ?? 0);
              }
              let negative = q < 0;
              if (negative) q = -q;
              r = String(q % Math.pow(10, exponent)).padStart(exponent, '0');
              q = String(Math.trunc(q / Math.pow(10, exponent)));
              return (negative ? '-' : '') + q + '.' + r;
            }

            function formatDurationFormatPattern(durationFormat, duration) {
              const units = [
                'years', 'months', 'weeks', 'days', 'hours', 'minutes',
                'seconds', 'milliseconds', 'microseconds', 'nanoseconds',
              ];
              const options = durationFormat.resolvedOptions();
              const locale = 'en';
              const numberingSystem = 'latn';
              const timeSeparator = ':';
              const result = [];
              let needSeparator = false;
              let displayNegativeSign = true;

              for (let unit of units) {
                let value = duration[unit] ?? 0;
                let style = options[unit];
                let display = options[unit + 'Display'];
                let numberFormatUnit = unit.slice(0, -1);
                let nfOpts = Object.create(null);
                let done = false;

                if ((unit === 'seconds' || unit === 'milliseconds' || unit === 'microseconds')) {
                  let nextStyle = options[units[units.indexOf(unit) + 1]];
                  if (nextStyle === 'numeric') {
                    if (unit === 'seconds') value = durationToFractional(duration, 9);
                    else if (unit === 'milliseconds') value = durationToFractional(duration, 6);
                    else value = durationToFractional(duration, 3);
                    nfOpts.maximumFractionDigits = options.fractionalDigits ?? 9;
                    nfOpts.minimumFractionDigits = options.fractionalDigits ?? 0;
                    nfOpts.roundingMode = 'trunc';
                    done = true;
                  }
                }

                let displayRequired = false;
                if (unit === 'minutes' && needSeparator) {
                  displayRequired = options.secondsDisplay === 'always' ||
                    (duration.seconds ?? 0) !== 0 || (duration.milliseconds ?? 0) !== 0 ||
                    (duration.microseconds ?? 0) !== 0 || (duration.nanoseconds ?? 0) !== 0;
                }

                if (value !== 0 || display !== 'auto' || displayRequired) {
                  if (displayNegativeSign) {
                    displayNegativeSign = false;
                    if (value === 0 && units.some(unit => (duration[unit] ?? 0) < 0)) {
                      value = -0;
                    }
                  } else {
                    nfOpts.signDisplay = 'never';
                  }

                  nfOpts.numberingSystem = options.numberingSystem;
                  if (style === '2-digit') {
                    nfOpts.minimumIntegerDigits = 2;
                  }
                  if (style !== 'numeric' && style !== '2-digit') {
                    nfOpts.style = 'unit';
                    nfOpts.unit = numberFormatUnit;
                    nfOpts.unitDisplay = style;
                  } else {
                    nfOpts.useGrouping = false;
                  }

                  let nf = new Intl.NumberFormat(locale, nfOpts);
                  let list = needSeparator ? result[result.length - 1] : [];
                  if (needSeparator) {
                    list.push({ type: 'literal', value: timeSeparator });
                  }

                  let parts = nf.formatToParts(value);
                  for (let part of parts) {
                    list.push({ type: part.type, value: part.value, unit: numberFormatUnit });
                  }

                  if (!needSeparator) {
                    if (style === '2-digit' || style === 'numeric') {
                      needSeparator = true;
                    }
                    result.push(list);
                  }
                }

                if (done) {
                  break;
                }
              }

              let listStyle = options.style === 'digital' ? 'short' : options.style;
              let lf = new Intl.ListFormat(locale, { type: 'unit', style: listStyle });
              let strings = [];
              for (let parts of result) {
                let string = '';
                for (let part of parts) string += part.value;
                strings.push(string);
              }
              let flattened = [];
              for (let { type, value } of lf.formatToParts(strings)) {
                if (type === 'element') {
                  flattened.push(...result.shift());
                } else {
                  flattened.push({ type, value });
                }
              }
              return flattened.reduce((acc, e) => acc + e.value, '');
            }

            const duration = {
              years: 1, months: 2, weeks: 3, days: 3, hours: 4,
              minutes: 5, seconds: 6, milliseconds: 7, microseconds: 8, nanoseconds: 9,
            };
            const negativeDuration = {
              years: -1, months: -2, weeks: -3, days: -3, hours: -4,
              minutes: -5, seconds: -6, milliseconds: -7, microseconds: -8, nanoseconds: -9,
            };
            const df = new Intl.DurationFormat('en', { style: 'long' });
            [df.format(duration), formatDurationFormatPattern(df, duration),
             df.format(negativeDuration), formatDurationFormatPattern(df, negativeDuration)].join('\\n');
        ");
        var str = Assert.IsType<string>(result.ToString());
        var lines = str.Split('\n');
        Assert.Equal(lines[1], lines[0]);
        Assert.Equal(lines[3], lines[2]);
    }
}
