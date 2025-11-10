using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests to debug why the loop check function doesn't execute.
/// </summary>
public class LoopExecutionDebugTests
{
    private readonly ITestOutputHelper _output;

    public LoopExecutionDebugTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SimpleFunctionCall_InPromiseExecutor()
    {
        // Test if a simple function call in a Promise executor works
        var engine = new JsEngine();
        var called = false;
        
        engine.SetGlobalFunction("markCalled", args =>
        {
            called = true;
            _output.WriteLine("markCalled was called!");
            return null;
        });
        
        await engine.Run(@"
            async function test() {
                await new Promise(function(resolve, reject) {
                    function helper() {
                        markCalled();
                        resolve();
                    }
                    helper();
                });
            }
            
            test();
        ");
        
        _output.WriteLine($"called = {called}");
        Assert.True(called);
    }

    [Fact]
    public async Task FunctionCallAsExprStatement_InPromiseExecutor()
    {
        // Test if calling a function as an expression statement works
        var engine = new JsEngine();
        var called = false;
        
        engine.SetGlobalFunction("markCalled", args =>
        {
            called = true;
            _output.WriteLine("markCalled was called!");
            return null;
        });
        
        await engine.Run(@"
            function test() {
                return new Promise(function(resolve, reject) {
                    function helper() {
                        markCalled();
                        resolve();
                    }
                    
                    helper();
                });
            }
            
            test();
        ");
        
        _output.WriteLine($"called = {called}");
        Assert.True(called);
    }

    [Fact]
    public async Task NestedFunctionDefAndCall_InPromiseExecutor()
    {
        // Test nested function definition and call
        var engine = new JsEngine();
        var messages = new List<string>();
        
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            messages.Add(msg);
            _output.WriteLine($"LOG: {msg}");
            return null;
        });
        
        await engine.Run(@"
            function test() {
                return new Promise(function(resolve, reject) {
                    log(""before function def"");
                    
                    function helper() {
                        log(""inside helper"");
                        resolve();
                    }
                    
                    log(""before helper call"");
                    helper();
                    log(""after helper call"");
                });
            }
            
            test();
        ");
        
        foreach (var msg in messages)
        {
            _output.WriteLine($"  - {msg}");
        }
        
        Assert.Contains("before function def", messages);
        Assert.Contains("before helper call", messages);
        Assert.Contains("inside helper", messages);
        Assert.Contains("after helper call", messages);
    }
}
