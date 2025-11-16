namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for labeled break and continue statements with runtime execution.
/// </summary>
public class LabeledBreakContinueTests
{
    [Fact(Timeout = 2000)]
    public async Task LabeledBreakExitsOuterLoop()
    {
        var source = @"
            var result = '';
            outer: for (var i = 0; i < 3; i++) {
                for (var j = 0; j < 3; j++) {
                    result += i + '' + j + ',';
                    if (i === 1 && j === 1) {
                        break outer;
                    }
                }
            }
            result;
        ";

        await using var engine = new JsEngine();
        var result = await engine.Evaluate(source);

        // Should be: 00, 01, 02, 10, 11 (breaks at i=1, j=1)
        Assert.Equal("00,01,02,10,11,", result);
    }

    [Fact(Timeout = 2000)]
    public async Task LabeledContinueSkipsToOuterLoop()
    {
        var source = @"
            var result = '';
            outer: for (var i = 0; i < 2; i++) {
                for (var j = 0; j < 3; j++) {
                    if (j === 1) {
                        continue outer;
                    }
                    result += i + '' + j + ',';
                }
            }
            result;
        ";

        await using var engine = new JsEngine();
        var result = await engine.Evaluate(source);

        // Should be: 00, (skips 01, 02), 10, (skips 11, 12)
        Assert.Equal("00,10,", result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnlabeledBreakOnlyExitsInnerLoop()
    {
        var source = @"
            var result = '';
            outer: for (var i = 0; i < 2; i++) {
                for (var j = 0; j < 3; j++) {
                    if (j === 1) {
                        break; // No label - only breaks inner loop
                    }
                    result += i + '' + j + ',';
                }
                result += 'X,';
            }
            result;
        ";

        await using var engine = new JsEngine();
        var result = await engine.Evaluate(source);

        // Should be: 00, X, 10, X (break only exits inner loop)
        Assert.Equal("00,X,10,X,", result);
    }

    [Fact(Timeout = 2000)]
    public async Task LabeledBreakWithWhileLoop()
    {
        var source = @"
            var result = '';
            var i = 0;
            outer: while (i < 3) {
                var j = 0;
                while (j < 3) {
                    result += i + '' + j + ',';
                    if (i === 1 && j === 1) {
                        break outer;
                    }
                    j++;
                }
                i++;
            }
            result;
        ";

        await using var engine = new JsEngine();
        var result = await engine.Evaluate(source);

        // Should break out of outer loop at i=1, j=1
        Assert.Equal("00,01,02,10,11,", result);
    }

    [Fact(Timeout = 2000)]
    public async Task LabeledContinueWithWhileLoop()
    {
        var source = @"
            var result = '';
            var i = 0;
            outer: while (i < 2) {
                var j = 0;
                while (j < 3) {
                    if (j === 1) {
                        i++;
                        continue outer;
                    }
                    result += i + '' + j + ',';
                    j++;
                }
                i++;
            }
            result;
        ";

        await using var engine = new JsEngine();
        var result = await engine.Evaluate(source);

        // Should continue outer loop when j=1
        Assert.Equal("00,10,", result);
    }

    [Fact(Timeout = 2000)]
    public async Task LabeledBreakWithBlock()
    {
        var source = @"
            var result = '';
            outer: {
                result += 'a,';
                inner: {
                    result += 'b,';
                    break outer;
                    result += 'c,';
                }
                result += 'd,';
            }
            result += 'e,';
            result;
        ";

        await using var engine = new JsEngine();
        var result = await engine.Evaluate(source);

        // Should break out of outer block
        Assert.Equal("a,b,e,", result);
    }

    [Fact(Timeout = 2000)]
    public async Task NestedLabeledBreaks()
    {
        var source = @"
            var result = '';
            a: for (var i = 0; i < 2; i++) {
                b: for (var j = 0; j < 2; j++) {
                    c: for (var k = 0; k < 2; k++) {
                        result += i + '' + j + '' + k + ',';
                        if (i === 0 && j === 1 && k === 0) {
                            break b;
                        }
                        if (i === 1 && j === 0 && k === 1) {
                            break a;
                        }
                    }
                }
            }
            result;
        ";

        await using var engine = new JsEngine();
        var result = await engine.Evaluate(source);

        // 000, 001, 010 (break b), 100, 101 (break a)
        Assert.Equal("000,001,010,100,101,", result);
    }

    [Fact(Timeout = 2000)]
    public async Task LabeledBreakInForOfLoop()
    {
        var source = @"
            var result = '';
            var items = ['a', 'b', 'c'];
            outer: for (var item of items) {
                for (var i = 0; i < 2; i++) {
                    result += item + i + ',';
                    if (item === 'b' && i === 0) {
                        break outer;
                    }
                }
            }
            result;
        ";

        await using var engine = new JsEngine();
        var result = await engine.Evaluate(source);

        // a0, a1, b0 (break outer)
        Assert.Equal("a0,a1,b0,", result);
    }

    [Fact(Timeout = 2000)]
    public async Task LabeledContinueInForInLoop()
    {
        var source = @"
            var result = '';
            var obj = {x: 1, y: 2, z: 3};
            outer: for (var key in obj) {
                for (var i = 0; i < 2; i++) {
                    if (i === 1) {
                        continue outer;
                    }
                    result += key + i + ',';
                }
            }
            result;
        ";

        await using var engine = new JsEngine();
        var result = await engine.Evaluate(source);

        // x0, y0, z0 (skips i=1 each time)
        Assert.Equal("x0,y0,z0,", result);
    }

    [Fact(Timeout = 2000)]
    public async Task LabeledDoWhileBreak()
    {
        var source = @"
            var result = '';
            var i = 0;
            outer: do {
                var j = 0;
                do {
                    result += i + '' + j + ',';
                    if (i === 1 && j === 1) {
                        break outer;
                    }
                    j++;
                } while (j < 3);
                i++;
            } while (i < 3);
            result;
        ";

        await using var engine = new JsEngine();
        var result = await engine.Evaluate(source);

        // 00, 01, 02, 10, 11 (break outer)
        Assert.Equal("00,01,02,10,11,", result);
    }
}
