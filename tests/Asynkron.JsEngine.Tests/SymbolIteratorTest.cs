using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class SymbolIteratorTest
{
    private readonly ITestOutputHelper _output;

    public SymbolIteratorTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TestSymbolIteratorAccess()
    {
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {msg}");
            return null;
        });
        
        await engine.Run(@"
            log(""1: start"");
            let arr = [""a"", ""b""];
            log(""2: created array"");
            
            let iteratorSymbol = Symbol.iterator;
            log(""3: got Symbol.iterator: "" + iteratorSymbol);
            
            let getIteratorMethod = arr[Symbol.iterator];
            log(""4: got iterator method: "" + getIteratorMethod);
            
            let iterator = arr[Symbol.iterator]();
            log(""5: got iterator: "" + iterator);
        ");
    }
}
