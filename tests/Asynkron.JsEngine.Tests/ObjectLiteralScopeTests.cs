using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Test that object literal methods can access variables from enclosing scope
/// </summary>
public class ObjectLiteralScopeTests(ITestOutputHelper output)
{
    [Fact(Timeout = 5000)]
    public async Task ObjectMethodCanAccessGlobalVariable()
    {
        output.WriteLine("=== Test: Object method accessing global variable ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
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
        output.WriteLine("✅ Test completed - method should have accessed global variable");
    }

    [Fact(Timeout = 5000)]
    public async Task ObjectMethodInAsyncFunction()
    {
        output.WriteLine("=== Test: Object method in async function ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
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
        output.WriteLine("✅ Test completed - method should work from async context");
    }
}
