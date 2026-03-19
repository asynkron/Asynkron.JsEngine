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

        // Test each pattern from the test262 non-unicode-property-names.js
        var result = await engine.Evaluate(@"
            var results = [];
            try { results.push('pi=' + /(?<π>a)/.exec('bab').groups.π); } catch(e) { results.push('pi:err=' + e.message); }
            try { results.push('dollar=' + /(?<$>a)/.exec('bab').groups.$); } catch(e) { results.push('dollar:err=' + e.message); }
            try { results.push('under=' + /(?<_>a)/.exec('bab').groups._); } catch(e) { results.push('under:err=' + e.message); }
            try { results.push('zwnj=' + /(?<_\u200C>a)/.exec('bab').groups._\u200C); } catch(e) { results.push('zwnj:err=' + e.message); }
            try { results.push('kannada=' + /(?<ಠ_ಠ>a)/.exec('bab').groups.ಠ_ಠ); } catch(e) { results.push('kannada:err=' + e.message); }
            try { results.push('esc1=' + /(?<\u0041>.)/.test('a')); } catch(e) { results.push('esc1:err=' + e.message); }
            try { results.push('esc2=' + RegExp('(?<\u{0041}>.)').test('a')); } catch(e) { results.push('esc2:err=' + e.message); }
            try { results.push('esc3=' + RegExp('(?<\\u0041>.)').test('a')); } catch(e) { results.push('esc3:err=' + e.message); }
            results.join('\n');
        ");
        Assert.Fail($"Results:\n{result?.ToString()}");
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
