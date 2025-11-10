using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class InstrumentedLoopTest2
{
    private readonly ITestOutputHelper _output;

    public InstrumentedLoopTest2(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task InstrumentedLoop()
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
                log(""A: before loop"");
                for (let item of arr) {
                    log(""B: in loop, item="" + item);
                    let value = await Promise.resolve(item);
                    log(""C: after await, value="" + value);
                    result = result + value;
                }
                log(""D: after loop"");
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
