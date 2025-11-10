using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class ForOfSimpleTest
{
    private readonly ITestOutputHelper _output;

    public ForOfSimpleTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SimplestForOfWithNoAwait()
    {
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {msg}");
            return null;
        });
        
        // Even simpler - no async, just for-of
        await engine.Run(@"
            let result = """";
            let arr = [""x"", ""y""];
            
            log(""before loop"");
            for (let item of arr) {
                log(""in loop: "" + item);
                result = result + item;
            }
            log(""after loop"");
        ");
        
        var result = engine.Evaluate("result;");
        _output.WriteLine($"Result: '{result}'");
        Assert.Equal("xy", result);
    }
}
