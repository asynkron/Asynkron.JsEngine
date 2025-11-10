using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class AsyncForOfNoAwaitTest
{
    private readonly ITestOutputHelper _output;

    public AsyncForOfNoAwaitTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AsyncForOfNoAwait()
    {
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {msg}");
            return null;
        });
        
        // for-of in async function, but NO await in loop
        await engine.Run(@"
            let result = """";
            let arr = [""x"", ""y""];
            
            async function test() {
                log(""before loop"");
                for (let item of arr) {
                    log(""in loop: "" + item);
                    result = result + item;
                }
                log(""after loop"");
            }
            
            test();
        ");
        
        var result = engine.Evaluate("result;");
        _output.WriteLine($"Result: '{result}'");
        Assert.Equal("xy", result);
    }
}
