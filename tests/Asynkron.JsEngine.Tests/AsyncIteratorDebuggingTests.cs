using Xunit.Abstractions;
using System.Text;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Detailed debugging tests to pinpoint why global scope iterators fail.
/// These tests progressively isolate the issue by testing iterator calls in different contexts.
/// </summary>
public class AsyncIteratorDebuggingTests(ITestOutputHelper output)
{
    [Fact(Timeout = 5000)]
    public async Task DirectIteratorCall_GlobalScope()
    {
        output.WriteLine("=== Test 1: Direct call to global iterator (no async, no promises) ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('Creating global iterator object');
            let globalIter = {
                next: function() {
                    log('next() called');
                    return { value: 1, done: false };
                }
            };

            log('Calling next() directly from global scope');
            let result = globalIter.next();
            log('Result: ' + JSON.stringify(result));
        ");

        await Task.Delay(500);
        output.WriteLine("✅ Baseline test: Direct call should work");
    }

    [Fact(Timeout = 5000)]
    public async Task IteratorCallFromAsyncFunction_NoPromiseWrapper()
    {
        output.WriteLine("=== Test 2: Call iterator from inside async function (but not in Promise chain) ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('Creating global iterator object');
            let globalIter = {
                next: function() {
                    log('next() called from async function context');
                    return { value: 2, done: false };
                }
            };

            async function test() {
                log('Inside async function, calling next()');
                let result = globalIter.next();
                log('Result: ' + JSON.stringify(result));
                return result;
            }

            test().then(r => log('Promise resolved: ' + JSON.stringify(r)));
        ");

        await Task.Delay(1000);
        output.WriteLine("Test if async function context affects iterator call");
    }

    [Fact(Timeout = 5000)]
    public async Task IteratorCallFromPromiseCallback()
    {
        output.WriteLine("=== Test 3: Call iterator from inside Promise.then() callback ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('Creating global iterator object');
            let globalIter = {
                next: function() {
                    log('next() called from Promise callback');
                    return { value: 3, done: false };
                }
            };

            async function test() {
                log('Creating a Promise');
                return new Promise((resolve) => {
                    log('Inside Promise executor, calling next()');
                    let result = globalIter.next();
                    log('Got result: ' + JSON.stringify(result));
                    resolve(result);
                });
            }

            test().then(r => log('Final result: ' + JSON.stringify(r)));
        ");

        await Task.Delay(1000);
        output.WriteLine("Test if Promise executor context affects iterator call");
    }

    [Fact(Timeout = 5000)]
    public async Task IteratorCallFromNestedPromiseChain()
    {
        output.WriteLine("=== Test 4: Call iterator from nested Promise.then() chain (mimics CPS) ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('Creating global iterator object with closure');
            let globalIter = {
                next: function() {
                    log('next() called from nested Promise chain');
                    return { value: 4, done: false };
                }
            };

            async function test() {
                log('Starting nested Promise chain');
                return new Promise((resolve1) => {
                    log('Promise 1 executor');
                    Promise.resolve(true).then(() => {
                        log('Promise 1 then callback, calling next()');
                        let result = globalIter.next();
                        log('Got result: ' + JSON.stringify(result));
                        resolve1(result);
                    }).catch(e => {
                        log('ERROR in Promise 1 then: ' + e);
                    });
                });
            }

            test().then(r => log('Final: ' + JSON.stringify(r))).catch(e => log('ERROR: ' + e));
        ");

        await Task.Delay(1000);
        output.WriteLine("Test if nested Promise chain affects iterator call");
    }

    [Fact(Timeout = 5000)]
    public async Task IteratorWithClosureVariables_GlobalScope()
    {
        output.WriteLine("=== Test 5: Iterator with closure variables (like real iterator) ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('Creating global iterator with closure variable');
            let globalIter = (function() {
                let index = 0;
                return {
                    next: function() {
                        log('next() called, index=' + index);
                        let result = { value: index++, done: false };
                        log('Returning: ' + JSON.stringify(result));
                        return result;
                    }
                };
            })();

            async function test() {
                return new Promise((resolve) => {
                    Promise.resolve().then(() => {
                        log('About to call next()');
                        let result = globalIter.next();
                        log('Result: ' + JSON.stringify(result));

                        // Try calling again
                        log('Calling next() second time');
                        let result2 = globalIter.next();
                        log('Result2: ' + JSON.stringify(result2));

                        resolve([result, result2]);
                    });
                });
            }

            test().then(r => log('Done: ' + JSON.stringify(r)));
        ");

        await Task.Delay(1000);
        output.WriteLine("Test if closure variables in iterator work from Promise chain");
    }

    [Fact(Timeout = 5000)]
    public async Task UseActualHelpers_GlobalIterator()
    {
        output.WriteLine("=== Test 6: Use actual __getAsyncIterator and __iteratorNext ===");

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
                    log('Symbol.iterator called');
                    let index = 0;
                    return {
                        next() {
                            log('next() called, index=' + index);
                            if (index < 3) {
                                let result = { value: index++, done: false };
                                log('Returning: ' + JSON.stringify(result));
                                return result;
                            }
                            log('Done iterating');
                            return { done: true };
                        }
                    };
                }
            };

            async function test() {
                log('Getting iterator with __getAsyncIterator');
                let iterator = __getAsyncIterator(globalIterable);
                log('Got iterator: ' + typeof iterator);

                return new Promise((resolve, reject) => {
                    log('About to call __iteratorNext');
                    try {
                        let promise = __iteratorNext(iterator);
                        log('Got promise: ' + typeof promise);

                        promise.then(result => {
                            log('Promise resolved with: ' + JSON.stringify(result));
                            resolve(result);
                        }).catch(error => {
                            log('Promise rejected: ' + error);
                            reject(error);
                        });
                    } catch (e) {
                        log('Exception calling __iteratorNext: ' + e);
                        reject(e);
                    }
                });
            }

            test().then(r => log('Test completed: ' + JSON.stringify(r)))
                .catch(e => log('Test failed: ' + e));
        ");

        await Task.Delay(1000);
        output.WriteLine("Test using actual helper functions");
    }

    [Fact(Timeout = 5000)]
    public async Task CompareLocalVsGlobal_MinimalCase()
    {
        output.WriteLine("=== Test 7: Minimal comparison - local vs global ===");

        var localLogs = new StringBuilder();
        var globalLogs = new StringBuilder();

        // Test LOCAL scope
        var engine1 = new JsEngine();
        engine1.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[LOCAL] {msg}");
            localLogs.Append(msg).AppendLine();
            return null;
        });

        await engine1.Run(@"
            async function test() {
                let localIter = {
                    next: () => {
                        log('LOCAL: next() called');
                        return { value: 1, done: false };
                    }
                };

                return new Promise(resolve => {
                    Promise.resolve().then(() => {
                        log('LOCAL: In nested Promise, calling next()');
                        try {
                            let result = localIter.next();
                            log('LOCAL: Got result: ' + JSON.stringify(result));
                            resolve(result);
                        } catch (e) {
                            log('LOCAL: Exception: ' + e);
                        }
                    });
                });
            }

            test();
        ");

        await Task.Delay(1000);

        // Test GLOBAL scope
        var engine2 = new JsEngine();
        engine2.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[GLOBAL] {msg}");
            globalLogs.Append(msg).AppendLine();
            return null;
        });

        await engine2.Run(@"
            let globalIter = {
                next: () => {
                    log('GLOBAL: next() called');
                    return { value: 1, done: false };
                }
            };

            async function test() {
                return new Promise(resolve => {
                    Promise.resolve().then(() => {
                        log('GLOBAL: In nested Promise, calling next()');
                        try {
                            let result = globalIter.next();
                            log('GLOBAL: Got result: ' + JSON.stringify(result));
                            resolve(result);
                        } catch (e) {
                            log('GLOBAL: Exception: ' + e);
                        }
                    });
                });
            }

            test();
        ");

        await Task.Delay(1000);

        output.WriteLine("");
        output.WriteLine("=== COMPARISON ===");
        output.WriteLine($"Local logs: {localLogs.Length} chars");
        output.WriteLine($"Global logs: {globalLogs.Length} chars");

        if (localLogs.ToString().Contains("next() called") && !globalLogs.ToString().Contains("next() called"))
        {
            output.WriteLine("❌ CONFIRMED: Global scope iterator's next() is NOT being called!");
            output.WriteLine("This is the exact failure point in minimal reproduction.");
        }
    }

    [Fact(Timeout = 5000)]
    public async Task InstrumentedIteratorNext_DetailedLogging()
    {
        output.WriteLine("=== Test 8: Create instrumented version of __iteratorNext ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            let globalIter = {
                next: function() {
                    log('USER: next() called from global iterator!');
                    return { value: 99, done: false };
                }
            };

            async function test() {
                log('TEST: Creating local reference to global next method');
                let iter = { next: globalIter.next };

                return new Promise((resolve) => {
                    log('TEST: About to call __iteratorNext');
                    try {
                        let promise = __iteratorNext(iter);
                        log('TEST: Got promise back from __iteratorNext');

                        promise.then(result => {
                            log('TEST: Promise resolved: ' + JSON.stringify(result));
                            resolve(result);
                        }).catch(err => {
                            log('TEST: Promise rejected: ' + err);
                        });
                    } catch (e) {
                        log('TEST: Exception calling __iteratorNext: ' + e);
                    }
                });
            }

            test().then(r => log('DONE: ' + JSON.stringify(r)))
                .catch(e => log('FAILED: ' + e));
        ");

        await Task.Delay(1000);
        output.WriteLine("Instrumented test complete - check logs for exact failure point");
    }

    [Fact(Timeout = 5000)]
    public async Task TestA_SymbolIteratorDirectCall()
    {
        output.WriteLine("=== Test A: Symbol.iterator() Direct Call on Global Object ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('Creating global iterable with Symbol.iterator');
            let globalIterable = {
                [Symbol.iterator]() {
                    log('Symbol.iterator method called');
                    let index = 0;
                    return {
                        next: function() {
                            log('Iterator next() called, index=' + index);
                            if (index < 3) {
                                return { value: index++, done: false };
                            }
                            return { done: true };
                        }
                    };
                }
            };

            async function test() {
                log('Inside async function');
                log('Calling Symbol.iterator directly');
                let iter = globalIterable[Symbol.iterator]();
                log('Got iterator: ' + typeof iter);
                log('Iterator has next: ' + (typeof iter.next));

                log('Calling next() on iterator');
                let result = iter.next();
                log('Result: ' + JSON.stringify(result));

                return result;
            }

            test().then(r => log('Done: ' + JSON.stringify(r)))
                .catch(e => log('Error: ' + e));
        ");

        await Task.Delay(1000);
        output.WriteLine("Test if Symbol.iterator() creates valid iterator from global object");
    }

    [Fact(Timeout = 5000)]
    public async Task TestB_GetAsyncIteratorDirectTest()
    {
        output.WriteLine("=== Test B: __getAsyncIterator on Global Object ===");

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
                    log('Symbol.iterator called');
                    let index = 0;
                    return {
                        next: function() {
                            log('next() called, index=' + index);
                            if (index < 3) {
                                return { value: index++, done: false };
                            }
                            return { done: true };
                        }
                    };
                }
            };

            async function test() {
                log('Calling __getAsyncIterator');
                let iter = __getAsyncIterator(globalIterable);
                log('Got iterator: ' + typeof iter);
                log('Iterator has next: ' + (typeof iter.next));

                return new Promise((resolve) => {
                    log('Inside Promise, calling next()');
                    Promise.resolve().then(() => {
                        log('Inside Promise.then, calling next()');
                        let result = iter.next();
                        log('Result: ' + JSON.stringify(result));
                        resolve(result);
                    });
                });
            }

            test().then(r => log('Done: ' + JSON.stringify(r)))
                .catch(e => log('Error: ' + e));
        ");

        await Task.Delay(1000);
        output.WriteLine("Test if __getAsyncIterator wrapper causes issues");
    }

    [Fact(Timeout = 5000)]
    public async Task TestC_RecursivePromiseChain()
    {
        output.WriteLine("=== Test C: Recursive Promise Chain (matching CPS loop) ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('Creating global iterator with state');
            let globalIter = (function() {
                let index = 0;
                return {
                    next: function() {
                        log('next() called, index=' + index);
                        if (index < 3) {
                            return { value: index++, done: false };
                        }
                        return { done: true };
                    }
                };
            })();

            async function test() {
                log('Starting recursive loop');
                let result = [];

                function loopCheck() {
                    log('loopCheck called');
                    return new Promise((resolve) => {
                        log('Promise executor');
                        Promise.resolve().then(() => {
                            log('In Promise.then, calling next()');
                            let iterResult = globalIter.next();
                            log('Got result: ' + JSON.stringify(iterResult));

                            if (iterResult.done) {
                                log('Done, resolving');
                                resolve(result);
                            } else {
                                result.push(iterResult.value);
                                log('Recursing, result so far: ' + JSON.stringify(result));
                                loopCheck().then(resolve);
                            }
                        }).catch(e => log('Error in then: ' + e));
                    });
                }

                return loopCheck();
            }

            test().then(r => log('Final: ' + JSON.stringify(r)))
                .catch(e => log('Error: ' + e));
        ");

        await Task.Delay(1000);
        output.WriteLine("Test if recursive Promise pattern (like CPS loop) works");
    }

    [Fact(Timeout = 5000)]
    public async Task TestD_CompareIteratorCreationMethods()
    {
        output.WriteLine("=== Test D: Compare Different Iterator Creation Methods ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('=== Method 1: Direct object with next ===');
            let iter1 = {
                next: function() {
                    log('iter1.next() called');
                    return { value: 1, done: false };
                }
            };

            log('=== Method 2: Via Symbol.iterator ===');
            let iterable2 = {
                [Symbol.iterator]() {
                    log('Symbol.iterator called for iter2');
                    return {
                        next: function() {
                            log('iter2.next() called');
                            return { value: 2, done: false };
                        }
                    };
                }
            };
            let iter2 = iterable2[Symbol.iterator]();

            log('=== Method 3: Via __getAsyncIterator ===');
            let iterable3 = {
                [Symbol.iterator]() {
                    log('Symbol.iterator called for iter3');
                    return {
                        next: function() {
                            log('iter3.next() called');
                            return { value: 3, done: false };
                        }
                    };
                }
            };
            let iter3 = __getAsyncIterator(iterable3);

            async function test() {
                log('Testing all three methods in Promise chain');

                return new Promise((resolve) => {
                    Promise.resolve().then(() => {
                        log('Calling iter1.next()');
                        let r1 = iter1.next();
                        log('iter1 result: ' + JSON.stringify(r1));

                        log('Calling iter2.next()');
                        let r2 = iter2.next();
                        log('iter2 result: ' + JSON.stringify(r2));

                        log('Calling iter3.next()');
                        let r3 = iter3.next();
                        log('iter3 result: ' + JSON.stringify(r3));

                        resolve([r1, r2, r3]);
                    });
                });
            }

            test().then(r => log('All results: ' + JSON.stringify(r)))
                .catch(e => log('Error: ' + e));
        ");

        await Task.Delay(1000);
        output.WriteLine("Compare if different iterator creation methods behave differently");
    }

    [Fact(Timeout = 5000)]
    public async Task TestE_ExceptionCaptureInIteratorNext()
    {
        output.WriteLine("=== Test E: Exception Capture - Does next() throw? ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            let globalIter = {
                next: function() {
                    log('next() called');
                    try {
                        log('Inside next(), about to return');
                        let result = { value: 1, done: false };
                        log('Created result object');
                        return result;
                    } catch (e) {
                        log('CAUGHT EXCEPTION in next(): ' + e);
                        throw e;
                    }
                }
            };

            async function test() {
                return new Promise((resolve, reject) => {
                    log('About to call __iteratorNext');
                    try {
                        let promise = __iteratorNext(globalIter);
                        log('__iteratorNext returned promise');

                        promise.then(result => {
                            log('Promise resolved: ' + JSON.stringify(result));
                            resolve(result);
                        }).catch(error => {
                            log('Promise rejected: ' + error);
                            reject(error);
                        });
                    } catch (e) {
                        log('Exception calling __iteratorNext: ' + e);
                        reject(e);
                    }
                });
            }

            test().then(r => log('Success: ' + JSON.stringify(r)))
                .catch(e => log('Failed: ' + e));
        ");

        await Task.Delay(1000);
        output.WriteLine("Check if exceptions are being thrown and caught");
    }


    [Fact(Timeout = 5000, Skip = "kills all other tests")]
    public async Task TestH_CheckPromiseRejectionHandling()
    {
        output.WriteLine("=== Test H: Check if Promise Rejections are Handled ===");

        await using var engine = new JsEngine();
        var rejectionsCaught = new List<string>();

        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        engine.SetGlobalFunction("onRejection", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"REJECTION: {msg}");
            rejectionsCaught.Add(msg);
            return null;
        });

        await engine.Run(@"
            log('Creating global iterable that will cause error');
            let globalIterable = {
                [Symbol.iterator]() {
                    log('Symbol.iterator called');
                    return {
                        next: function() {
                            log('next() will be called and should throw');
                            // This function body will be empty, causing exception
                        }
                    };
                }
            };

            async function test() {
                log('Starting test with explicit rejection handler');
                try {
                    for await (let item of globalIterable) {
                        log('In loop (should not reach here)');
                    }
                    log('Loop completed normally');
                } catch (e) {
                    log('Caught in try-catch: ' + e);
                    onRejection('try-catch: ' + e);
                }
                return 'done';
            }

            log('Calling test with .catch handler');
            test()
                .then(r => log('Resolved: ' + r))
                .catch(e => {
                    log('Caught in .catch: ' + e);
                    onRejection('promise-catch: ' + e);
                });
        ");

        await Task.Delay(1000);

        output.WriteLine("");
        output.WriteLine($"=== REJECTIONS CAUGHT: {rejectionsCaught.Count} ===");
        foreach (var rejection in rejectionsCaught)
        {
            output.WriteLine($"  - {rejection}");
        }

        // Check exceptions
        var exceptions = new List<ExceptionInfo>();
        while (engine.Exceptions().TryRead(out var ex))
        {
            exceptions.Add(ex);
        }

        output.WriteLine("");
        output.WriteLine($"=== EXCEPTIONS LOGGED: {exceptions.Count} ===");
        foreach (var ex in exceptions)
        {
            output.WriteLine($"  - {ex.Message} (Context: {ex.Context})");
        }

        output.WriteLine("");
        if (rejectionsCaught.Count > 0)
        {
            output.WriteLine("✅ Promise rejections ARE being caught by handlers!");
        }
        else
        {
            output.WriteLine("❌ Promise rejections are NOT being caught - this is the problem!");
            output.WriteLine("   The rejection is converted to a rejected promise but never bubbles up.");
        }
    }

    [Fact(Timeout = 5000)]
    public async Task TestI_InvestigateFunctionBodyCorruption()
    {
        output.WriteLine("=== Test I: Investigate Why Function Bodies Are Empty ===");

        await using var engine = new JsEngine();
        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"LOG: {msg}");
            return null;
        });

        await engine.Run(@"
            log('=== Test 1: Regular function in global object ===');
            let obj1 = {
                method: function() {
                    log('Regular function called');
                    return 'result1';
                }
            };
            log('Calling obj1.method: ' + obj1.method());

            log('');
            log('=== Test 2: Arrow function in global object ===');
            let obj2 = {
                method: () => {
                    log('Arrow function called');
                    return 'result2';
                }
            };
            log('Calling obj2.method: ' + obj2.method());

            log('');
            log('=== Test 3: Method shorthand in global object ===');
            let obj3 = {
                method() {
                    log('Method shorthand called');
                    return 'result3';
                }
            };
            log('Calling obj3.method: ' + obj3.method());

            log('');
            log('=== Test 4: Function in global object accessed from async ===');
            let obj4 = {
                method: function() {
                    log('Function called from async');
                    return 'result4';
                }
            };

            async function testAsync() {
                log('Inside async function, calling obj4.method');
                try {
                    let result = obj4.method();
                    log('Result: ' + result);
                    return result;
                } catch (e) {
                    log('ERROR: ' + e);
                    return 'error: ' + e;
                }
            }

            testAsync().then(r => log('Async result: ' + r));
        ");

        await Task.Delay(1000);

        // Check exceptions
        var exceptions = new List<ExceptionInfo>();
        while (engine.Exceptions().TryRead(out var ex))
        {
            exceptions.Add(ex);
        }

        output.WriteLine("");
        output.WriteLine($"=== EXCEPTIONS: {exceptions.Count} ===");
        foreach (var ex in exceptions)
        {
            output.WriteLine($"  - {ex.Message}");
        }

        output.WriteLine("");
        output.WriteLine("This test checks if the issue is specific to:");
        output.WriteLine("  1. How functions are defined (regular/arrow/shorthand)");
        output.WriteLine("  2. Calling from async context");
        output.WriteLine("  3. Something about Symbol.iterator specifically");
    }
}

