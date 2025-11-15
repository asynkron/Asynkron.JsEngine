using Xunit;
namespace Asynkron.JsEngine.Tests;

public class TempTest4
{
    [Fact(Timeout = 2000)]
    public async Task Test_Three()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""
            function test(async, await, yield) {
                return async + await + yield;
            }
            test(1, 2, 3);
            """);
        Assert.Equal(6.0, result);
    }
}
