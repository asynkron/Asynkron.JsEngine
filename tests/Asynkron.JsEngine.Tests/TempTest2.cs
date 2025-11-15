using Xunit;
namespace Asynkron.JsEngine.Tests;

public class TempTest2
{
    [Fact(Timeout = 2000)]
    public async Task Test_All_Five()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""
            function test(async, await, yield, get, set) {
                return async + await + yield + get + set;
            }
            test(1, 2, 3, 4, 5);
            """);
        Assert.Equal(15.0, result);
    }
}
