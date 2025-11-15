using Xunit;
namespace Asynkron.JsEngine.Tests;

public class TempTest
{
    [Fact(Timeout = 2000)]
    public async Task Test_Async_And_Yield()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""
            function test(async, yield) {
                return async + yield;
            }
            test(1, 2);
            """);
        Assert.Equal(3.0, result);
    }
}
