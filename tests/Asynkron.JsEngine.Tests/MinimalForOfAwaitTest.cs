using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class MinimalForOfAwaitTest
{
    private readonly ITestOutputHelper _output;

    public MinimalForOfAwaitTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task MinimalForOfAwait()
    {
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {msg}");
            return null;
        });
        
        // Absolute minimum - just the loop with await, no other statements
        await engine.Run(@"
            let result = """";
            let arr = [""x""];
            
            async function test() {
                for (let item of arr) {
                    let value = await Promise.resolve(item);
                    result = value;
                }
            }
            
            log(""calling test"");
            test();
            log(""returned from test"");
        ");
        
        var result = engine.Evaluate("result;");
        _output.WriteLine($"Result: '{result}'");
        Assert.Equal("x", result);
    }
}
