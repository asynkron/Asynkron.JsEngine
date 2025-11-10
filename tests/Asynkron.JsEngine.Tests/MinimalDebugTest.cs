using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class MinimalDebugTest
{
    private readonly ITestOutputHelper _output;

    public MinimalDebugTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SimplestCase_FunctionDefAndCallInTryBlockInLambda()
    {
        // Manually construct what the CPS transformer creates
        var engine = new JsEngine();
        var called = false;
        
        engine.SetGlobalFunction("markCalled", args =>
        {
            called = true;
            _output.WriteLine("markCalled was called!");
            return null;
        });
        
        // This mimics the structure from CPS transformation
        await engine.Run(@"
            function test() {
                return new Promise(function(__resolve, __reject) {
                    try {
                        function helper() {
                            markCalled();
                        }
                        helper();
                    } catch (e) {
                        __reject(e);
                    }
                    __resolve();
                });
            }
            
            test();
        ");
        
        _output.WriteLine($"called = {called}");
        Assert.True(called);
    }

    [Fact]
    public async Task AsyncFunction_FunctionDefAndCallInTryBlockInLambda()
    {
        // Now test with an async function (CPS-transformed)
        var engine = new JsEngine();
        var called = false;
        
        engine.SetGlobalFunction("markCalled", args =>
        {
            called = true;
            _output.WriteLine("markCalled was called!");
            return null;
        });
        
        // This is what the CPS transformer creates for async function
        await engine.Run(@"
            async function test() {
                return new Promise(function(__resolve, __reject) {
                    try {
                        function helper() {
                            markCalled();
                        }
                        helper();
                    } catch (e) {
                        __reject(e);
                    }
                    __resolve();
                });
            }
            
            test();
        ");
        
        _output.WriteLine($"called = {called}");
        Assert.True(called);
    }
}
