namespace Asynkron.JsEngine.Tests;

public class GlobalStateIsolationTests
{
    [Fact]
    public async Task PrototypeMutationsDoNotLeakBetweenEngines()
    {
        await using (var first = new JsEngine())
        {
            await first.Evaluate(@"Array.prototype.__leak__ = 123;");
        }

        await using var second = new JsEngine();
        var hasLeak = (bool)(await second.Evaluate(@"Array.prototype.hasOwnProperty('__leak__');"))!;

        Assert.False(hasLeak);
    }
}
