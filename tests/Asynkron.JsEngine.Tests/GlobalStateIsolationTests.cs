using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class GlobalStateIsolationTests(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact]
    public async Task PrototypeMutationsDoNotLeakBetweenEngines()
    {
        await using (var first = CreateEngine())
        {
            await first.Evaluate(@"Array.prototype.__leak__ = 123;");
        }

        await using var second = CreateEngine();
        var hasLeak = (bool)(await second.Evaluate(@"Array.prototype.hasOwnProperty('__leak__');"))!;

        Assert.False(hasLeak);
    }
}
