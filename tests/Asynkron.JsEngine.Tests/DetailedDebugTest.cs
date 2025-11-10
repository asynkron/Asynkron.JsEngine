using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class DetailedDebugTest
{
    private readonly ITestOutputHelper _output;

    public DetailedDebugTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TraceLoopExecution()
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
        
        await engine.Run(@"
            let result = """";
            let arr = [""x""];
            
            async function test() {
                log(""1: entering test function"");
                for (let item of arr) {
                    log(""2: in loop body, item="" + item);
                    let value = await Promise.resolve(item);
                    log(""3: after await, value="" + value);
                    result = result + value;
                }
                log(""4: after loop"");
            }
            
            log(""0: calling test"");
            test();
        ");
        
        var result = engine.Evaluate("result;");
        _output.WriteLine($"Result: '{result}'");
        _output.WriteLine($"Logs: {string.Join(" | ", logs)}");
        
        Assert.Equal("x", result);
    }
}
