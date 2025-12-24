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
        var first = Assert.IsAssignableFrom<IJsObjectLike>(steps.Items[0].ToObject());
        var second = Assert.IsAssignableFrom<IJsObjectLike>(steps.Items[1].ToObject());
        var third = Assert.IsAssignableFrom<IJsObjectLike>(steps.Items[2].ToObject());

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

        var first = Assert.IsAssignableFrom<IJsObjectLike>(steps.Items[0].ToObject());
        var second = Assert.IsAssignableFrom<IJsObjectLike>(steps.Items[1].ToObject());
        var third = Assert.IsAssignableFrom<IJsObjectLike>(steps.Items[2].ToObject());
        var fourth = Assert.IsAssignableFrom<IJsObjectLike>(steps.Items[3].ToObject());

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

    [Fact]
    public async Task YieldInDestructuringDefaultReceivesSentValue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var x;
            var vals = {};
            var err;

            var iterator = {
              next: function() {
                return { done: false, value: undefined };
              }
            };
            var iterable = {};
            iterable[Symbol.iterator] = function() {
              return iterator;
            };

            function* g() {
              try {
                [ x = yield ] = iterable;
              } catch (e) {
                err = 'caught: ' + e;
              }
            }

            var iter = g();
            var first = iter.next();
            var second = iter.next(86);
            ({ first, second, x, err });
        ");

        if (result is string error)
        {
            throw new Exception($"script error: {error}");
        }

        var obj = Assert.IsAssignableFrom<IJsObjectLike>(result);
        if (obj.TryGetProperty("err", out var errProp) && !errProp.IsUndefined)
        {
            throw new Exception($"JS error: {errProp}");
        }
        Assert.True(obj.TryGetProperty("first", out var firstResult));
        var first = Assert.IsAssignableFrom<IJsObjectLike>(firstResult.ToObject());
        Assert.True(first.TryGetProperty("done", out var firstDone));
        Assert.False(firstDone.AsBoolean());

        Assert.True(obj.TryGetProperty("second", out var secondResult));
        var second = Assert.IsAssignableFrom<IJsObjectLike>(secondResult.ToObject());
        Assert.True(second.TryGetProperty("done", out var secondDone));
        Assert.True(secondDone.AsBoolean());

        Assert.True(obj.TryGetProperty("x", out var xValue));
        Assert.Equal(86d, xValue.AsDouble());
    }
}
