using Xunit;
namespace Asynkron.JsEngine.Tests;

public class TempTest6
{
    [Fact(Timeout = 2000)]
    public async Task Test_Mix()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""
            function test(a, b, yield, get) {
                return a + b + yield + get;
            }
            test(1, 2, 3, 4);
            """);
        Assert.Equal(10.0, result);
    }
}
