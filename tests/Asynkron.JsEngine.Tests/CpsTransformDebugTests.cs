using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests to debug CPS transformation for loops with await.
/// These tests help understand what the transformer is producing.
/// </summary>
public class CpsTransformDebugTests(ITestOutputHelper output)
{
    [Fact(Timeout = 2000)]
    public async Task SimpleForOf_WithAwait_Debug()
    {
        // Simplest possible test case - single iteration
        await using var engine = new JsEngine();

        await engine.Run("""

                                     let result = "";
                                     let arr = ["x"];

                                     async function test() {
                                         for (let item of arr) {
                                             let value = await Promise.resolve(item);
                                             result = result + value;
                                         }
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("result;");
        output.WriteLine($"Result: '{result}'");
        Assert.Equal("x", result);
    }

    [Fact(Timeout = 2000)]
    public async Task VerySimpleForOf_NoAwaitInLoop_Debug()
    {
        // Control test - no await in loop body
        await using var engine = new JsEngine();

        await engine.Run("""

                                     let result = "";
                                     let arr = ["x"];

                                     async function test() {
                                         for (let item of arr) {
                                             result = result + item;
                                         }
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("result;");
        output.WriteLine($"Result: '{result}'");
        Assert.Equal("x", result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task ForOf_WithAwaitOutsideLoop_Debug()
    {
        // Test await before loop - should work
        await using var engine = new JsEngine();

        await engine.Run("""

                                     let result = "";
                                     let arr = ["x"];

                                     async function test() {
                                         let prefix = await Promise.resolve(">");
                                         for (let item of arr) {
                                             result = result + prefix + item;
                                         }
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("result;");
        output.WriteLine($"Result: '{result}'");
        Assert.Equal(">x", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOf_WithConsoleLog_Debug()
    {
        // Add logging to see if loop executes at all
        await using var engine = new JsEngine();
        var logMessages = new List<string>();

        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            logMessages.Add(message);
            output.WriteLine($"LOG: {message}");
            return null;
        });

        await engine.Run("""

                                     let result = "";
                                     let arr = ["a", "b"];

                                     async function test() {
                                         log("before loop");
                                         for (let item of arr) {
                                             log("in loop: " + item);
                                             let value = await Promise.resolve(item);
                                             log("after await: " + value);
                                             result = result + value;
                                         }
                                         log("after loop");
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("result;");
        output.WriteLine($"Result: '{result}'");
        output.WriteLine($"Log messages: {string.Join(", ", logMessages)}");

        // Let's see what actually gets logged
        foreach (var msg in logMessages)
        {
            output.WriteLine($"  - {msg}");
        }

        Assert.Equal("ab", result);
    }
}
