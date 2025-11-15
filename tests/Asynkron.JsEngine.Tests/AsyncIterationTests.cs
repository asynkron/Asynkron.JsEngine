using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;

namespace Asynkron.JsEngine.Tests;

public class AsyncIterationTests(ITestOutputHelper output)
{
    [Fact(Timeout = 2000)]
    public async Task RegularForOf_WithAwaitInBody()
    {
        // Test that regular for-of with await in body works
        await using var engine = new JsEngine();

        await engine.Run("""

                                     let result = "";
                                     let promises = [
                                         Promise.resolve("a"),
                                         Promise.resolve("b"),
                                         Promise.resolve("c")
                                     ];

                                     async function test() {
                                         for (let promise of promises) {
                                             let item = await promise;
                                             result = result + item;
                                         }
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("result;");
        Assert.Equal("abc", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForAwaitOf_WithArray()
    {
        await using var engine = new JsEngine();

        await engine.Run("""

                                     let result = "";
                                     let arr = ["a", "b", "c"];

                                     async function test() {
                                         for await (let item of arr) {
                                             result = result + item;
                                         }
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("result;");
        Assert.Equal("abc", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForAwaitOf_WithGenerator()
    {
        await using var engine = new JsEngine();

        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {message}");
            return null;
        });

        await engine.Run("""

                                     let sum = 0;

                                     function* generator() {
                                         log("Generator: yielding 1");
                                         yield 1;
                                         log("Generator: yielding 2");
                                         yield 2;
                                         log("Generator: yielding 3");
                                         yield 3;
                                         log("Generator: done");
                                     }

                                     async function test() {
                                         log("Starting loop");
                                         for await (let num of generator()) {
                                             log("Got num: " + num);
                                             sum = sum + num;
                                             log("Sum after add: " + sum);
                                         }
                                         log("After loop, sum: " + sum);
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("sum;");
        output.WriteLine($"Final sum: '{result}'");
        Assert.Equal(6.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForAwaitOf_WithString()
    {
        await using var engine = new JsEngine();

        await engine.Run("""

                                     let result = "";

                                     async function test() {
                                         for await (let char of "hello") {
                                             result = result + char;
                                         }
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("result;");
        Assert.Equal("hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForAwaitOf_WithBreak()
    {
        await using var engine = new JsEngine();

        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {message}");
            return null;
        });

        await engine.Run("""

                                     let count = 0;
                                     let arr = [1, 2, 3, 4, 5];

                                     async function test() {
                                         log("Starting loop");
                                         for await (let item of arr) {
                                             log("Item: " + item);
                                             log("Count before increment: " + count);
                                             count = count + 1;
                                             log("Count after increment: " + count);
                                             log("About to check if item === 3, item is: " + item);
                                             if (item === 3) {
                                                 log("Breaking at item 3");
                                                 break;
                                             }
                                             log("Continuing to next item");
                                         }
                                         log("After loop, count: " + count);
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("count;");
        output.WriteLine($"Final count: '{result}'");
        Assert.Equal(3.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForAwaitOf_WithContinue()
    {
        await using var engine = new JsEngine();

        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {message}");
            return null;
        });

        await engine.Run("""

                                     let sum = 0;
                                     let arr = [1, 2, 3, 4, 5];

                                     async function test() {
                                         log("Starting loop");
                                         for await (let item of arr) {
                                             log("Item: " + item);
                                             if (item === 3) {
                                                 log("Skipping item 3");
                                                 continue;
                                             }
                                             log("Adding item: " + item);
                                             sum = sum + item;
                                             log("Sum after add: " + sum);
                                         }
                                         log("After loop, sum: " + sum);
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("sum;");
        output.WriteLine($"Final sum: '{result}'");
        Assert.Equal(12.0, result); // 1 + 2 + 4 + 5 = 12
    }

    [Fact(Timeout = 2000)]
    public async Task ForAwaitOf_RequiresAsyncFunction()
    {
        await using var engine = new JsEngine();

        // for await...of must be used inside an async function
        // This should work in our current implementation even outside async
        // but in strict JavaScript it would require async context
        var result = await engine.Evaluate("""

                                                       let result = "";
                                                       for await (let item of ["x", "y"]) {
                                                           result = result + item;
                                                       }
                                                       result;

                                           """);

        Assert.Equal("xy", result);
    }

    [Fact(Timeout = 2000)]
    public async Task SymbolAsyncIterator_Exists()
    {
        await using var engine = new JsEngine();

        var result = await engine.Run("""

                                                  typeof Symbol.asyncIterator;

                                      """);

        Assert.Equal("symbol", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForAwaitOf_WithPromiseArray()
    {
        // NOTE: This test demonstrates a limitation - for-await-of with promises
        // in arrays requires CPS transformation support.
        // Currently, promises in arrays are treated as objects, not awaited.
        await using var engine = new JsEngine();

        await engine.Run("""

                                     let result = "";

                                     // For-await-of can iterate arrays, but won't automatically await promise values
                                     // This works if we await them manually in the loop body
                                     let promises = [
                                         Promise.resolve("a"),
                                         Promise.resolve("b"),
                                         Promise.resolve("c")
                                     ];

                                     async function test() {
                                         for await (let promise of promises) {
                                             // Need to manually await the promise
                                             let item = await promise;
                                             result = result + item;
                                         }
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("result;");
        Assert.Equal("abc", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForAwaitOf_WithCustomAsyncIterator()
    {
        await using var engine = new JsEngine();

        await engine.Run("""

                                     let result = "";

                                     // Custom object with async iterator
                                     let asyncIterable = {
                                         [Symbol.asyncIterator]() {
                                             let count = 0;
                                             return {
                                                 next() {
                                                     count = count + 1;
                                                     if (count <= 3) {
                                                         return Promise.resolve({ value: count, done: false });
                                                     } else {
                                                         return Promise.resolve({ done: true });
                                                     }
                                                 }
                                             };
                                         }
                                     };

                                     async function test() {
                                         for await (let num of asyncIterable) {
                                             result = result + num;
                                         }
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("result;");
        Assert.Equal("123", result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task ForAwaitOf_WithCustomSyncAsyncIterator()
    {
        // This test shows that Symbol.asyncIterator works when it returns synchronous values
        await using var engine = new JsEngine();

        await engine.Run("""

                                     let result = "";

                                     // Custom object with async iterator that returns sync values
                                     let asyncIterable = {
                                         [Symbol.asyncIterator]() {
                                             let count = 0;
                                             return {
                                                 next() {
                                                     count = count + 1;
                                                     if (count <= 3) {
                                                         return { value: count, done: false };
                                                     } else {
                                                         return { done: true };
                                                     }
                                                 }
                                             };
                                         }
                                     };

                                     async function test() {
                                         for await (let num of asyncIterable) {
                                             result = result + num;
                                         }
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("result;");
        Assert.Equal("123", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ForAwaitOf_ErrorPropagation()
    {
        await using var engine = new JsEngine();
        var errorCaught = false;

        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {message}");
            return null;
        });

        engine.SetGlobalFunction("markError", args =>
        {
            errorCaught = true;
            output.WriteLine("LOG: Error caught!");
            return null;
        });

        await engine.Run("""

                                     let asyncIterable = {
                                         [Symbol.asyncIterator]() {
                                             let count = 0;
                                             return {
                                                 next() {
                                                     count = count + 1;
                                                     log("Iterator next() called, count: " + count);
                                                     if (count === 2) {
                                                         log("Rejecting at count 2");
                                                         return Promise.reject("test error");
                                                     }
                                                     if (count <= 3) {
                                                         log("Resolving with value: " + count);
                                                         return Promise.resolve({ value: count, done: false });
                                                     }
                                                     log("Done iterating");
                                                     return Promise.resolve({ done: true });
                                                 }
                                             };
                                         }
                                     };

                                     async function test() {
                                         log("Starting test function");
                                         try {
                                             log("Starting for-await-of loop");
                                             for await (let num of asyncIterable) {
                                                 log("Got num in loop: " + num);
                                                 // Should throw on second iteration
                                             }
                                             log("Loop completed without error");
                                         } catch (e) {
                                             log("Caught error: " + e);
                                             markError();
                                         }
                                         log("Test function complete");
                                     }

                                     test();

                         """);

        output.WriteLine($"Error caught: {errorCaught}");
        Assert.True(errorCaught);
    }

    [Fact(Timeout = 2000)]
    public async Task ForAwaitOf_SyncErrorPropagation()
    {
        // Test error handling with synchronous iterators
        await using var engine = new JsEngine();
        var errorCaught = false;

        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {message}");
            return null;
        });

        engine.SetGlobalFunction("markError", args =>
        {
            errorCaught = true;
            output.WriteLine("LOG: Error caught!");
            return null;
        });

        await engine.Run("""

                                     let syncIterable = {
                                         [Symbol.iterator]() {
                                             let count = 0;
                                             return {
                                                 next() {
                                                     count = count + 1;
                                                     log("Iterator next() called, count: " + count);
                                                     if (count === 2) {
                                                         log("Throwing at count 2");
                                                         throw "test error";
                                                     }
                                                     if (count <= 3) {
                                                         log("Returning value: " + count);
                                                         return { value: count, done: false };
                                                     }
                                                     log("Done iterating");
                                                     return { done: true };
                                                 }
                                             };
                                         }
                                     };

                                     async function test() {
                                         log("Starting test function");
                                         try {
                                             log("Starting for-await-of loop");
                                             for await (let num of syncIterable) {
                                                 log("Got num in loop: " + num);
                                                 // Should throw on second iteration
                                             }
                                             log("Loop completed without error");
                                         } catch (e) {
                                             log("Caught error: " + e);
                                             markError();
                                         }
                                         log("Test function complete");
                                     }

                                     test();

                         """);

        output.WriteLine($"Error caught: {errorCaught}");
        Assert.True(errorCaught);
    }

    [Fact(Timeout = 2000)]
    public async Task RegularForOf_WithAwaitInBodyWithDebug()
    {
        // Test that regular for-of with await in body works, using __debug() to inspect state
        await using var engine = new JsEngine();

        await engine.Run("""

                                     let result = "";
                                     let promises = [
                                         Promise.resolve("a"),
                                         Promise.resolve("b"),
                                         Promise.resolve("c")
                                     ];

                                     async function test() {
                                         for (let promise of promises) {
                                             __debug(); // Before await
                                             let item = await promise;
                                             __debug(); // After await
                                             result = result + item;
                                         }
                                         __debug(); // After loop
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("result;");
        Assert.Equal("abc", result);

        // Verify we got debug messages - should have 7 total:
        // 3 iterations * 2 (before + after await) + 1 after loop = 7
        var debugMessages = new List<DebugMessage>();
        for (var i = 0; i < 7; i++) debugMessages.Add(await engine.DebugMessages().ReadAsync());

        Assert.Equal(7, debugMessages.Count);

        // Verify that the result accumulates correctly through the iterations
        // Messages 0,1: first iteration (before await, after await)
        // Messages 2,3: second iteration
        // Messages 4,5: third iteration
        // Message 6: after loop

        // After first await completes
        Assert.True(debugMessages[1].Variables.ContainsKey("item"));
        Assert.Equal("a", debugMessages[1].Variables["item"]);

        // After second await completes
        Assert.True(debugMessages[3].Variables.ContainsKey("item"));
        Assert.Equal("b", debugMessages[3].Variables["item"]);

        // After third await completes
        Assert.True(debugMessages[5].Variables.ContainsKey("item"));
        Assert.Equal("c", debugMessages[5].Variables["item"]);
    }

    [Fact(Timeout = 2000)]
    public async Task ForAwaitOf_WithArrayWithDebug()
    {
        // Test for-await-of with __debug() to inspect state during iteration
        await using var engine = new JsEngine();

        await engine.Run("""

                                     let result = "";
                                     let arr = ["x", "y", "z"];

                                     async function test() {
                                         for await (let item of arr) {
                                             __debug();
                                             result = result + item;
                                         }
                                         __debug();
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("result;");
        Assert.Equal("xyz", result);

        // Should have 4 debug messages (3 iterations + 1 after loop)
        var debugMessages = new List<DebugMessage>();
        for (var i = 0; i < 4; i++) debugMessages.Add(await engine.DebugMessages().ReadAsync());

        Assert.Equal(4, debugMessages.Count);

        // Verify item values in each iteration
        Assert.True(debugMessages[0].Variables.ContainsKey("item"));
        Assert.Equal("x", debugMessages[0].Variables["item"]);

        Assert.True(debugMessages[1].Variables.ContainsKey("item"));
        Assert.Equal("y", debugMessages[1].Variables["item"]);

        Assert.True(debugMessages[2].Variables.ContainsKey("item"));
        Assert.Equal("z", debugMessages[2].Variables["item"]);
    }
}
