using Xunit;
namespace Asynkron.JsEngine.Tests;

public class TempTest5
{
    [Fact(Timeout = 2000)]
    public async Task Test_Normal_Four()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""
            function test(a, b, c, d) {
                return a + b + c + d;
            }
            test(1, 2, 3, 4);
            """);
        Assert.Equal(10.0, result);
    }
}
