using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class CheckTransformTest
{
    private readonly ITestOutputHelper _output;

    public CheckTransformTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task CheckIfTransformed()
    {
        var engine = new JsEngine();
        var logs = new List<string>();
        
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            logs.Add(msg);
            _output.WriteLine($"LOG: {msg}");
            return null;
        });
        
        // Simple case - async function with just one await, no loop
        await engine.Run(@"
            let result = """";
            
            async function test1() {
                log(""test1: before await"");
                let x = await Promise.resolve(""hello"");
                log(""test1: after await, x="" + x);
                result = x;
            }
            
            log(""0: calling test1"");
            test1();
        ");
        
        var result = engine.Evaluate("result;");
        _output.WriteLine($"Result: '{result}'");
        _output.WriteLine($"Logs: {string.Join(" | ", logs)}");
        
        Assert.Equal("hello", result);
    }
}
