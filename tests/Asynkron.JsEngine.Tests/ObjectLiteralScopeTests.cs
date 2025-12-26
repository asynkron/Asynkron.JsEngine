using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Test that object literal methods can access variables from enclosing scope
/// </summary>
public abstract class ObjectLiteralScopeTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact(Timeout = 5000)]
    public async Task ObjectMethodCanAccessGlobalVariable()
    {
        Output.WriteLine("=== Test: Object method accessing global variable ===");

        await using var engine = CreateEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            Output.WriteLine($"LOG: {msg}");
            return JsValue.Null;
        });

        await engine.Evaluate(@"
            let globalVar = 'from-global';

            let obj = {
                next() {
                    log('next() called, globalVar = ' + globalVar);
                    return { value: 42, done: false };
                }
            };

            log('Calling obj.next():');
            let result = obj.next();
            log('Result: ' + JSON.stringify(result));
        ");

        await Task.Delay(500);
        Output.WriteLine("Test completed - method should have accessed global variable");
    }

    [Fact(Timeout = 5000)]
    public async Task ObjectMethodInAsyncFunction()
    {
        Output.WriteLine("=== Test: Object method in async function ===");

        await using var engine = CreateEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            Output.WriteLine($"LOG: {msg}");
            return JsValue.Null;
        });

        await engine.Evaluate(@"
            let globalVar = 'from-global';

            let obj = {
                next() {
                    log('next() in async context, globalVar = ' + globalVar);
                    return { value: 99, done: false };
                }
            };

            async function test() {
                log('In async function, calling obj.next()');
                let result = obj.next();
                log('Result: ' + JSON.stringify(result));
                return result;
            }

            test().then(r => log('Done: ' + JSON.stringify(r)));
        ");

        await Task.Delay(1000);
        Output.WriteLine("Test completed - method should work from async context");
    }
}

public class FastPathObjectLiteralScopeTests(ITestOutputHelper output) : ObjectLiteralScopeTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class ReferenceObjectLiteralScopeTests(ITestOutputHelper output) : ObjectLiteralScopeTestsBase(output)
{
    protected override bool EnableFastPaths => false;
}
