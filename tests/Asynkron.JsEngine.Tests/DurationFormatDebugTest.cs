using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

public class DurationFormatDebugTest
{
    [Fact]
    public async Task ResolvedOptionsCheck()
    {
        var engine = new JsEngine();
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
        var str = result.ToString();
        Assert.Contains("years=short", str);
    }

    [Fact]
    public async Task FormatDefaultStyle()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var df = new Intl.DurationFormat('en');
            var duration = {years: 1, months: 2, weeks: 3, days: 3, hours: 4, minutes: 5, seconds: 6, milliseconds: 7, microseconds: 8, nanoseconds: 9};
            df.format(duration);
        ");
        var str = result.ToString();
        Assert.NotEmpty(str);
    }

    [Fact]
    public async Task OutOfRangeSeconds()
    {
        var engine = new JsEngine();
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

    [Fact]
    public async Task DecimalConversionCheck()
    {
        // Test what Number.MAX_SAFE_INTEGER + 1 evaluates to
        var engine = new JsEngine();
        var result = await engine.Evaluate("'' + (Number.MAX_SAFE_INTEGER + 1)");
        Assert.Equal("9007199254740992", result?.ToString());
    }

    [Fact]
    public async Task UnicodeGroupName()
    {
        var engine = new JsEngine();

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

    [Fact]
    public async Task HarnessSimulation()
    {
        var engine = new JsEngine();
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
        var str = result.ToString();
        Assert.Contains("years:", str);
    }
}
