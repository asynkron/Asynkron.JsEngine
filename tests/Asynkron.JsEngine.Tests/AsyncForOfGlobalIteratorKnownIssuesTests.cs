using Asynkron.JsEngine.Parser;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class AsyncForOfGlobalIteratorKnownIssuesTests(ITestOutputHelper output)
{
    /// <summary>
    /// Known failure due to the global-scope sync iterable fallback bug; see AsyncForOf global issue tracker.
    /// </summary>
    [Fact(Timeout = 2000)]
    [Trait("Category", "AsyncForOfGlobalKnownFailure")]
    public async Task ForAwaitOf_FallbackToSyncIterator()
    {
        await using var engine = new JsEngine();

        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {message}");
            return null;
        });

        await engine.Run("""
                                     let result = "";

                                     // Object with only sync iterator (Symbol.iterator)
                                     let syncIterable = {
                                         [Symbol.iterator]() {
                                             log("Symbol.iterator called");
                                             let values = ["x", "y", "z"];
                                             let index = 0;
                                             return {
                                                 next() {
                                                     log("next() called, index: " + index);
                                                     if (index < values.length) {
                                                         let value = values[index++];
                                                         log("Returning value: " + value);
                                                         return { value: value, done: false };
                                                     }
                                                     log("Done iterating");
                                                     return { done: true };
                                                 }
                                             };
                                         }
                                     };

                                     async function test() {
                                         log("Starting test function");
                                         log("Starting for-await-of loop");
                                         for await (let item of syncIterable) {
                                             log("Got item: " + item);
                                             result = result + item;
                                             log("Result so far: " + result);
                                         }
                                         log("After loop, result: " + result);
                                     }

                                     test();

                         """);

        var result = await engine.Evaluate("result;");
        output.WriteLine($"Final result: '{result}'");
        Assert.Equal("xyz", result);
    }

    /// <summary>
    /// Known failure due to the global-scope sync iterable fallback bug; see AsyncForOf global issue tracker.
    /// </summary>
    [Fact(Timeout = 2000)]
    [Trait("Category", "AsyncForOfGlobalKnownFailure")]
    public async Task ForAwaitOf_WithSyncIteratorNoAsync()
    {
        await using var engine = new JsEngine();

        output.WriteLine("=== ForAwaitOf_WithSyncIteratorNoAsync ===");

        await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate("""

                                       let result = "";

                                       // Object with only sync iterator (Symbol.iterator)
                                       let syncIterable = {
                                           [Symbol.iterator]() {
                                               let values = ["x", "y", "z"];
                                               let index = 0;
                                               return {
                                                   next() {
                                                       if (index < values.length) {
                                                           return { value: values[index++], done: false };
                                                       }
                                                       return { done: true };
                                                   }
                                               };
                                           }
                                       };

                                       for await (let item of syncIterable) {
                                           result = result + item;
                                       }

                                       result;

                               """);
        });

        output.WriteLine("For-await-of without async context correctly throws a parse error.");
    }

    /// <summary>
    /// Known failure due to the global-scope sync iterable fallback bug; see AsyncForOf global issue tracker.
    /// </summary>
    [Fact(Timeout = 5000)]
    [Trait("Category", "AsyncForOfGlobalKnownFailure")]
    public async Task TestL_MethodShorthandInForAwaitOf()
    {
        output.WriteLine("=== Test L: Method Shorthand in for-await-of ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('Creating global iterable with method shorthand next()');
            let globalIterable = {
                [Symbol.iterator]() {
                    log('Symbol.iterator() called');
                    let count = 0;
                    return {
                        next() {
                            log('next() called, count=' + count);
                            if (count < 2) {
                                return { value: count++, done: false };
                            }
                            return { done: true };
                        }
                    };
                }
            };

            async function testAsync() {
                log('Starting async function');
                let results = [];
                log('Entering for-await-of loop');
                for await (let item of globalIterable) {
                    log('Loop body: got item = ' + item);
                    results.push(item);
                }
                log('Exited loop, results: ' + JSON.stringify(results));
                return results;
            }

            log('Calling testAsync()');
            testAsync()
                .then(r => log('SUCCESS: ' + JSON.stringify(r)))
                .catch(e => log('ERROR: ' + e));
        ");

        await Task.Delay(1000);

        var exceptions = new List<ExceptionInfo>();
        while (engine.Exceptions().TryRead(out var ex))
        {
            exceptions.Add(ex);
        }

        output.WriteLine("");
        output.WriteLine($"=== EXCEPTIONS: {exceptions.Count} ===");
        foreach (var ex in exceptions)
        {
            output.WriteLine($"  - {ex.Message} (Context: {ex.Context})");
        }

        output.WriteLine("");
        output.WriteLine("This is the EXACT scenario from the failing test!");
    }

    /// <summary>
    /// Known failure due to the global-scope sync iterable fallback bug; see AsyncForOf global issue tracker.
    /// </summary>
    [Fact(Timeout = 5000)]
    [Trait("Category", "AsyncForOfGlobalKnownFailure")]
    public async Task TestF_ActualForAwaitOf_WithLogging()
    {
        output.WriteLine("=== Test F: Actual for-await-of with extensive logging ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('Creating global iterable');
            let globalIterable = {
                [Symbol.iterator]() {
                    log('!!! Symbol.iterator called !!!');
                    let index = 0;
                    let iterObj = {
                        next: function() {
                            log('!!! next() called, index=' + index + ' !!!');
                            if (index < 2) {
                                let val = index++;
                                log('!!! Returning value=' + val + ' !!!');
                                return { value: val, done: false };
                            }
                            log('!!! Returning done=true !!!');
                            return { done: true };
                        }
                    };
                    log('!!! Returning iterator object !!!');
                    return iterObj;
                }
            };

            async function test() {
                log('>>> Starting test function');
                let result = [];

                log('>>> About to enter for-await-of');
                try {
                    for await (let item of globalIterable) {
                        log('>>> INSIDE LOOP, item=' + item);
                        result.push(item);
                    }
                    log('>>> After loop, result=' + JSON.stringify(result));
                } catch (e) {
                    log('>>> Exception in loop: ' + e);
                    throw e;
                }

                log('>>> Returning result');
                return result;
            }

            log('Calling test()');
            test()
                .then(r => log('FINAL: ' + JSON.stringify(r)))
                .catch(e => log('ERROR: ' + e));
        ");

        await Task.Delay(1000);
        output.WriteLine("Actual for-await-of test with extensive logging to pinpoint failure");
    }

    /// <summary>
    /// Known failure due to the global-scope sync iterable fallback bug; see AsyncForOf global issue tracker.
    /// </summary>
    [Fact(Timeout = 5000)]
    [Trait("Category", "AsyncForOfGlobalKnownFailure")]
    public async Task TestG_CaptureExceptionsWithChannel()
    {
        output.WriteLine("=== Test G: Capture Exceptions via Exception Channel ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('Creating global iterable with iterator');
            let globalIterable = {
                [Symbol.iterator]() {
                    log('Symbol.iterator called');
                    let index = 0;
                    return {
                        next: function() {
                            log('next() called, index=' + index);
                            if (index < 2) {
                                return { value: index++, done: false };
                            }
                            return { done: true };
                        }
                    };
                }
            };

            async function test() {
                log('Starting test');
                let result = [];

                try {
                    log('Entering for-await-of');
                    for await (let item of globalIterable) {
                        log('Got item: ' + item);
                        result.push(item);
                    }
                    log('Loop completed, result: ' + JSON.stringify(result));
                } catch (e) {
                    log('Caught exception: ' + e);
                }

                return result;
            }

            test().then(r => log('Done: ' + JSON.stringify(r)))
                .catch(e => log('Failed: ' + e));
        ");

        await Task.Delay(1000);

        var exceptions = new List<ExceptionInfo>();
        while (engine.Exceptions().TryRead(out var ex))
        {
            exceptions.Add(ex);
        }

        output.WriteLine("");
        output.WriteLine($"=== EXCEPTIONS CAPTURED: {exceptions.Count} ===");
        foreach (var ex in exceptions)
        {
            output.WriteLine($"Exception: {ex.ExceptionType}");
            output.WriteLine($"Message: {ex.Message}");
            output.WriteLine($"Context: {ex.Context}");
            output.WriteLine($"Call Stack:");
            foreach (var frame in ex.CallStack)
            {
                output.WriteLine($"  - {frame.Description}");
            }
            output.WriteLine("");
        }

        if (exceptions.Count > 0)
        {
            output.WriteLine("✅ SUCCESS: Captured exceptions that explain the failure!");
        }
        else
        {
            output.WriteLine("⚠️ No exceptions captured - exception may be swallowed elsewhere");
        }
    }
}
