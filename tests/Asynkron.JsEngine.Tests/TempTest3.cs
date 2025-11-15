using Xunit;
namespace Asynkron.JsEngine.Tests;

public class TempTest3
{
    [Fact(Timeout = 2000)]
    public async Task Test_Four()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""
            function test(async, await, yield, get) {
                return async + await + yield + get;
            }
            test(1, 2, 3, 4);
            """);
        Assert.Equal(10.0, result);
    }
}
