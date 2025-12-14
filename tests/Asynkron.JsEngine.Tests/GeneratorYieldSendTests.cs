using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

public class GeneratorYieldSendTests
{
    [Fact]
    public async Task YieldYieldForwardsSentValues()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function* g(){ yield yield; }
            var iter = g();
            var first = iter.next();
            var second = iter.next(123);
            var third = iter.next(456);
            [first, second, third];
        ");

        if (result is string error)
        {
            throw new Exception($"script error: {error}");
        }

        var steps = Assert.IsType<JsArray>(result);
        var first = Assert.IsType<JsObject>(steps.Items[0]);
        var second = Assert.IsType<JsObject>(steps.Items[1]);
        var third = Assert.IsType<JsObject>(steps.Items[2]);

        Assert.True(first.TryGetProperty("done", out var firstDone) && firstDone is bool { } firstDoneBool &&
                    firstDoneBool == false);

        Assert.True(second.TryGetProperty("value", out var secondValue));
        Assert.Equal(123d, secondValue);
        Assert.True(second.TryGetProperty("done", out var secondDone) && secondDone is bool { } secondDoneBool &&
                    secondDoneBool == false);

        Assert.True(third.TryGetProperty("done", out var thirdDone) && thirdDone is bool { } thirdDoneBool &&
                    thirdDoneBool);
        Assert.True(third.TryGetProperty("value", out _));
    }

    [Fact]
    public async Task YieldYieldSpreadSuspendsAcrossOperandAndOuterYield()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let steps;
            try {
              function* g(){ yield [...yield yield]; }
              const iter = g();
              steps = [iter.next(false), iter.next(['a','b','c']), iter.next('ignored'), iter.next()];
            } catch (e) {
              steps = 'err:' + e?.message;
            }
            steps;
        ");

        var steps = Assert.IsType<JsArray>(result);
        Assert.Equal(4, steps.Items.Count);

        var first = Assert.IsType<JsObject>(steps.Items[0]);
        var second = Assert.IsType<JsObject>(steps.Items[1]);
        var third = Assert.IsType<JsObject>(steps.Items[2]);
        var fourth = Assert.IsType<JsObject>(steps.Items[3]);

        Assert.True(first.TryGetProperty("done", out var firstDone) && firstDone is bool { } fd && fd == false);
        Assert.True(first.TryGetProperty("value", out var firstValue) && ReferenceEquals(firstValue, Symbol.Undefined));

        if (!second.TryGetProperty("value", out var secondValue))
        {
            throw new Exception($"second keys: {string.Join(",", second.Keys)}");
        }
        var secondArr = Assert.IsType<JsArray>(secondValue);
        Assert.Equal(new[] { "a", "b", "c" }, secondArr.Items.Select(v => v.ToObject()).ToArray());
        if (!(second.TryGetProperty("done", out var secondDone) && secondDone is bool { } sd && sd == false))
        {
            throw new Exception($"second keys: {string.Join(",", second.Keys)}");
        }

        Assert.True(third.TryGetProperty("value", out var thirdValue));
        var thirdArr = Assert.IsType<JsArray>(thirdValue);
        Assert.Equal(new[] { "i", "g", "n", "o", "r", "e", "d" }, thirdArr.Items.Select(v => v.ToObject()).ToArray());
        Assert.True(third.TryGetProperty("done", out var thirdDone) && thirdDone is bool { } td && td == false);

        if (!(fourth.TryGetProperty("done", out var fourthDone) && fourthDone is bool { } fod && fod))
        {
            throw new Exception($"fourth keys: {string.Join(",", fourth.Keys)} value={(fourth.TryGetProperty("value", out var v) ? v : null)}");
        }
    }
}
