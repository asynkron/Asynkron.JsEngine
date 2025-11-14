using Xunit;
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
        
        var engine = new JsEngine();
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

        await System.Threading.Tasks.Task.Delay(500);
        output.WriteLine("✅ Baseline test: Direct call should work");
    }

    [Fact(Timeout = 5000)]
    public async Task IteratorCallFromAsyncFunction_NoPromiseWrapper()
    {
        output.WriteLine("=== Test 2: Call iterator from inside async function (but not in Promise chain) ===");
        
        var engine = new JsEngine();
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

        await System.Threading.Tasks.Task.Delay(1000);
        output.WriteLine("Test if async function context affects iterator call");
    }

    [Fact(Timeout = 5000)]
    public async Task IteratorCallFromPromiseCallback()
    {
        output.WriteLine("=== Test 3: Call iterator from inside Promise.then() callback ===");
        
        var engine = new JsEngine();
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

        await System.Threading.Tasks.Task.Delay(1000);
        output.WriteLine("Test if Promise executor context affects iterator call");
    }

    [Fact(Timeout = 5000)]
    public async Task IteratorCallFromNestedPromiseChain()
    {
        output.WriteLine("=== Test 4: Call iterator from nested Promise.then() chain (mimics CPS) ===");
        
        var engine = new JsEngine();
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

        await System.Threading.Tasks.Task.Delay(1000);
        output.WriteLine("Test if nested Promise chain affects iterator call");
    }

    [Fact(Timeout = 5000)]
    public async Task IteratorWithClosureVariables_GlobalScope()
    {
        output.WriteLine("=== Test 5: Iterator with closure variables (like real iterator) ===");
        
        var engine = new JsEngine();
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

        await System.Threading.Tasks.Task.Delay(1000);
        output.WriteLine("Test if closure variables in iterator work from Promise chain");
    }

    [Fact(Timeout = 5000)]
    public async Task UseActualHelpers_GlobalIterator()
    {
        output.WriteLine("=== Test 6: Use actual __getAsyncIterator and __iteratorNext ===");
        
        var engine = new JsEngine();
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

        await System.Threading.Tasks.Task.Delay(1000);
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
            localLogs.AppendLine(msg);
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

        await System.Threading.Tasks.Task.Delay(1000);
        
        // Test GLOBAL scope
        var engine2 = new JsEngine();
        engine2.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[GLOBAL] {msg}");
            globalLogs.AppendLine(msg);
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

        await System.Threading.Tasks.Task.Delay(1000);
        
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
        
        var engine = new JsEngine();
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

        await System.Threading.Tasks.Task.Delay(1000);
        output.WriteLine("Instrumented test complete - check logs for exact failure point");
    }
}
