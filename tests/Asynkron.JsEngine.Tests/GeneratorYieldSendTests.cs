using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Xunit;

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
}
