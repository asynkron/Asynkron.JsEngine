using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class ManualTransformTest
{
    private readonly ITestOutputHelper _output;

    public ManualTransformTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ManualCpsLoop()
    {
        // Manually write what the CPS transformer should create
        var engine = new JsEngine();
        var logs = new List<string>();
        
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            logs.Add(msg);
            _output.WriteLine($"LOG: {msg}");
            return null;
        });
        
        // This is what the transformation SHOULD create
        await engine.Run(@"
            let result = """";
            let arr = [""x""];
            
            function test() {
                return new Promise(function(__resolve, __reject) {
                    try {
                        log(""A: before loop"");
                        
                        // Get iterator
                        let __iterator = arr[Symbol.iterator]();
                        log(""got iterator"");
                        
                        // Define loop check function
                        function __loopCheck() {
                            log(""in __loopCheck"");
                            let __result = __iterator.next();
                            if (__result.done) {
                                log(""loop done"");
                                __resolve();
                            } else {
                                log(""loop not done, processing item"");
                                function __loopResolve() {
                                    return __loopCheck();
                                }
                                
                                let item = __result.value;
                                // Body with await
                                Promise.resolve(item).then(function(value) {
                                    log(""in then handler, value="" + value);
                                    result = result + value;
                                    __loopResolve();
                                });
                            }
                        }
                        
                        log(""calling __loopCheck"");
                        __loopCheck();
                        log(""after calling __loopCheck"");
                    } catch (__error) {
                        __reject(__error);
                    }
                });
            }
            
            test();
        ");
        
        var result = engine.EvaluateSync("result;");
        _output.WriteLine($"Result: '{result}'");
        _output.WriteLine($"Logs: {string.Join(" | ", logs)}");
        
        Assert.Equal("x", result);
    }
}
