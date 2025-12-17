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
        var first = Assert.IsType<JsObject>(steps.Items[0].ToObject());
        var second = Assert.IsType<JsObject>(steps.Items[1].ToObject());
        var third = Assert.IsType<JsObject>(steps.Items[2].ToObject());

        Assert.True(first.TryGetProperty("done", out var firstDone));
        Assert.False(firstDone.AsBoolean());

        Assert.True(second.TryGetProperty("value", out var secondValue));
        Assert.Equal(123d, secondValue.AsDouble());
        Assert.True(second.TryGetProperty("done", out var secondDone));
        Assert.False(secondDone.AsBoolean());

        Assert.True(third.TryGetProperty("done", out var thirdDone));
        Assert.True(thirdDone.AsBoolean());
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

        var first = Assert.IsType<JsObject>(steps.Items[0].ToObject());
        var second = Assert.IsType<JsObject>(steps.Items[1].ToObject());
        var third = Assert.IsType<JsObject>(steps.Items[2].ToObject());
        var fourth = Assert.IsType<JsObject>(steps.Items[3].ToObject());

        Assert.True(first.TryGetProperty("done", out var firstDone));
        Assert.False(firstDone.AsBoolean());
        Assert.True(first.TryGetProperty("value", out var firstValue) && firstValue.IsUndefined);

        if (!second.TryGetProperty("value", out var secondValue))
        {
            throw new Exception($"second keys: {string.Join(",", second.Keys)}");
        }
        var secondArr = Assert.IsType<JsArray>(secondValue.ToObject());
        Assert.Equal(new[] { "a", "b", "c" }, secondArr.Items.Select(v => v.ToObject()).ToArray());
        if (!second.TryGetProperty("done", out var secondDone))
        {
            throw new Exception($"second keys: {string.Join(",", second.Keys)}");
        }
        Assert.False(secondDone.AsBoolean());

        Assert.True(third.TryGetProperty("value", out var thirdValue));
        var thirdArr = Assert.IsType<JsArray>(thirdValue.ToObject());
        Assert.Equal(new[] { "i", "g", "n", "o", "r", "e", "d" }, thirdArr.Items.Select(v => v.ToObject()).ToArray());
        Assert.True(third.TryGetProperty("done", out var thirdDone));
        Assert.False(thirdDone.AsBoolean());

        if (!fourth.TryGetProperty("done", out var fourthDone))
        {
            throw new Exception($"fourth keys: {string.Join(",", fourth.Keys)} value={(fourth.TryGetProperty("value", out var v) ? v : JsValue.Null)}");
        }
        Assert.True(fourthDone.AsBoolean());
    }
}
