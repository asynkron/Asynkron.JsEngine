using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using System.Text;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests to compare behavior of for-await-of with iterables in different scopes.
/// This helps diagnose why global scope iterables fail while local scope iterables work.
/// 
/// DETAILED FINDINGS: See ../../ASYNC_ITERABLE_SCOPE_COMPARISON.md
/// 
/// Key Discovery: The for-await-of loop works correctly when the iterable is declared
/// in LOCAL scope (inside the async function), but FAILS when the iterable is declared
/// in GLOBAL scope (outside the async function). The S-expression transformation is
/// correct in both cases, but execution differs.
/// </summary>
public class AsyncIterableScopeComparisonTests(ITestOutputHelper output)
{
    [Fact(Timeout = 5000)]
    public async Task CompareGlobalVsLocalScope_SExpression()
    {
        // Test 1: Iterable in LOCAL scope (works)
        var localScopeCode = @"
            async function test() {
                let localIterable = {
                    [Symbol.iterator]() {
                        let values = ['x', 'y', 'z'];
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
                
                let result = '';
                for await (let item of localIterable) {
                    result = result + item;
                }
                return result;
            }
        ";

        // Test 2: Iterable in GLOBAL scope (fails)
        var globalScopeCode = @"
            let globalIterable = {
                [Symbol.iterator]() {
                    let values = ['x', 'y', 'z'];
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
            
            async function test() {
                let result = '';
                for await (let item of globalIterable) {
                    result = result + item;
                }
                return result;
            }
        ";

        var engine1 = new JsEngine();
        var engine2 = new JsEngine();

        // Parse both versions to get S-expressions
        var localParsed = engine1.Parse(localScopeCode);
        var globalParsed = engine2.Parse(globalScopeCode);

        output.WriteLine("=== LOCAL SCOPE S-EXPRESSION ===");
        output.WriteLine(localParsed.ToString());
        output.WriteLine("");
        output.WriteLine("=== GLOBAL SCOPE S-EXPRESSION ===");
        output.WriteLine(globalParsed.ToString());
        output.WriteLine("");

        // Now compare the for-await-of transformation
        output.WriteLine("=== COMPARISON NOTES ===");
        output.WriteLine("The S-expressions should show how the for-await-of loop is transformed.");
        output.WriteLine("Key things to look for:");
        output.WriteLine("1. How 'localIterable' vs 'globalIterable' appears in the transformed code");
        output.WriteLine("2. The __getAsyncIterator call and how it references the iterable");
        output.WriteLine("3. The __loopCheck function and variable capture");
    }

    [Fact(Timeout = 5000)]
    public async Task CompareGlobalVsLocalScope_WithDebug()
    {
        var localResult = new StringBuilder();
        var globalResult = new StringBuilder();

        // Test with LOCAL scope iterable
        var engine1 = new JsEngine();
        engine1.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[LOCAL] {msg}");
            localResult.AppendLine($"[LOCAL] {msg}");
            return null;
        });

        await engine1.Run(@"
            async function test() {
                log('=== LOCAL SCOPE TEST ===');
                
                let localIterable = {
                    [Symbol.iterator]() {
                        log('LOCAL: Symbol.iterator called');
                        let values = ['x', 'y', 'z'];
                        let index = 0;
                        return {
                            next() {
                                log('LOCAL: next() called, index=' + index);
                                if (index < values.length) {
                                    let val = values[index++];
                                    log('LOCAL: returning value=' + val);
                                    return { value: val, done: false };
                                }
                                log('LOCAL: returning done=true');
                                return { done: true };
                            }
                        };
                    }
                };
                
                log('LOCAL: About to start for-await-of');
                __debug(); // Capture state before loop
                
                let result = '';
                for await (let item of localIterable) {
                    log('LOCAL: In loop, item=' + item);
                    __debug(); // Capture state during iteration
                    result = result + item;
                }
                
                log('LOCAL: After loop, result=' + result);
                __debug(); // Capture state after loop
                return result;
            }
            
            test();
        ");

        // Collect debug messages from local scope test
        var localDebugMessages = new List<DebugMessage>();
        while (engine1.DebugMessages().TryRead(out var msg))
        {
            localDebugMessages.Add(msg);
        }

        await System.Threading.Tasks.Task.Delay(1000);
        var localFinalResult = await engine1.Evaluate("test()");
        output.WriteLine($"[LOCAL] Final result: '{localFinalResult}'");
        localResult.AppendLine($"[LOCAL] Final result: '{localFinalResult}'");

        // Test with GLOBAL scope iterable
        var engine2 = new JsEngine();
        engine2.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[GLOBAL] {msg}");
            globalResult.AppendLine($"[GLOBAL] {msg}");
            return null;
        });

        await engine2.Run(@"
            log('=== GLOBAL SCOPE TEST ===');
            
            let globalIterable = {
                [Symbol.iterator]() {
                    log('GLOBAL: Symbol.iterator called');
                    let values = ['x', 'y', 'z'];
                    let index = 0;
                    return {
                        next() {
                            log('GLOBAL: next() called, index=' + index);
                            if (index < values.length) {
                                let val = values[index++];
                                log('GLOBAL: returning value=' + val);
                                return { value: val, done: false };
                            }
                            log('GLOBAL: returning done=true');
                            return { done: true };
                        }
                    };
                }
            };
            
            async function test() {
                log('GLOBAL: About to start for-await-of');
                __debug(); // Capture state before loop
                
                let result = '';
                for await (let item of globalIterable) {
                    log('GLOBAL: In loop, item=' + item);
                    __debug(); // Capture state during iteration
                    result = result + item;
                }
                
                log('GLOBAL: After loop, result=' + result);
                __debug(); // Capture state after loop
                return result;
            }
            
            test();
        ");

        // Collect debug messages from global scope test
        var globalDebugMessages = new List<DebugMessage>();
        while (engine2.DebugMessages().TryRead(out var msg))
        {
            globalDebugMessages.Add(msg);
        }

        await System.Threading.Tasks.Task.Delay(1000);
        var globalFinalResult = await engine2.Evaluate("test()");
        output.WriteLine($"[GLOBAL] Final result: '{globalFinalResult}'");
        globalResult.AppendLine($"[GLOBAL] Final result: '{globalFinalResult}'");

        // Compare debug messages
        output.WriteLine("");
        output.WriteLine("=== DEBUG MESSAGE COMPARISON ===");
        output.WriteLine($"Local scope debug messages: {localDebugMessages.Count}");
        output.WriteLine($"Global scope debug messages: {globalDebugMessages.Count}");

        if (localDebugMessages.Count > 0)
        {
            output.WriteLine("");
            output.WriteLine("LOCAL SCOPE DEBUG MESSAGES:");
            for (int i = 0; i < localDebugMessages.Count; i++)
            {
                output.WriteLine($"  Message {i + 1}:");
                foreach (var kvp in localDebugMessages[i].Variables)
                {
                    output.WriteLine($"    {kvp.Key} = {kvp.Value}");
                }
            }
        }

        if (globalDebugMessages.Count > 0)
        {
            output.WriteLine("");
            output.WriteLine("GLOBAL SCOPE DEBUG MESSAGES:");
            for (int i = 0; i < globalDebugMessages.Count; i++)
            {
                output.WriteLine($"  Message {i + 1}:");
                foreach (var kvp in globalDebugMessages[i].Variables)
                {
                    output.WriteLine($"    {kvp.Key} = {kvp.Value}");
                }
            }
        }

        // Write summary
        output.WriteLine("");
        output.WriteLine("=== SUMMARY ===");
        output.WriteLine($"Local scope works: {localFinalResult?.ToString() == "xyz"}");
        output.WriteLine($"Global scope works: {globalFinalResult?.ToString() == "xyz"}");
        
        // This test documents the difference - we expect local to work and global to fail
        // So we don't assert, we just document the behavior
    }

    [Fact(Timeout = 5000)]
    public async Task InspectIteratorObject_GlobalVsLocal()
    {
        output.WriteLine("=== INSPECTING ITERATOR OBJECTS ===");
        
        // Test 1: Local scope - manually get iterator and inspect
        var engine1 = new JsEngine();
        engine1.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[LOCAL] {msg}");
            return null;
        });

        await engine1.Run(@"
            async function test() {
                let localIterable = {
                    [Symbol.iterator]() {
                        return {
                            next() {
                                return { value: 'x', done: false };
                            }
                        };
                    }
                };
                
                log('Getting iterator with __getAsyncIterator');
                let iter = __getAsyncIterator(localIterable);
                log('Iterator type: ' + typeof iter);
                log('Iterator has next: ' + (typeof iter.next));
                log('Iterator constructor: ' + iter.constructor.name);
                
                log('Calling __iteratorNext');
                let promise = __iteratorNext(iter);
                log('Promise type: ' + typeof promise);
                log('Promise has then: ' + (typeof promise.then));
                
                promise.then(result => {
                    log('Promise resolved, result: ' + JSON.stringify(result));
                });
            }
            
            test();
        ");

        await System.Threading.Tasks.Task.Delay(1000);

        // Test 2: Global scope - manually get iterator and inspect
        var engine2 = new JsEngine();
        engine2.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[GLOBAL] {msg}");
            return null;
        });

        await engine2.Run(@"
            let globalIterable = {
                [Symbol.iterator]() {
                    return {
                        next() {
                            return { value: 'x', done: false };
                        }
                    };
                }
            };
            
            async function test() {
                log('Getting iterator with __getAsyncIterator');
                let iter = __getAsyncIterator(globalIterable);
                log('Iterator type: ' + typeof iter);
                log('Iterator has next: ' + (typeof iter.next));
                log('Iterator constructor: ' + iter.constructor.name);
                
                log('Calling __iteratorNext');
                let promise = __iteratorNext(iter);
                log('Promise type: ' + typeof promise);
                log('Promise has then: ' + (typeof promise.then));
                
                promise.then(result => {
                    log('Promise resolved, result: ' + JSON.stringify(result));
                });
            }
            
            test();
        ");

        await System.Threading.Tasks.Task.Delay(1000);
    }

    [Fact(Timeout = 5000)]
    public async Task DebugInsideLoopBody_GlobalVsLocal()
    {
        output.WriteLine("=== DEBUG INSIDE LOOP BODY COMPARISON ===");
        output.WriteLine("");
        
        // Test with LOCAL scope iterable - __debug() INSIDE loop body
        var engine1 = new JsEngine();
        engine1.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[LOCAL] {msg}");
            return null;
        });

        await engine1.Run(@"
            async function test() {
                log('=== LOCAL SCOPE - Debug Inside Loop ===');
                
                let localIterable = {
                    [Symbol.iterator]() {
                        log('LOCAL: Symbol.iterator called');
                        let values = ['x', 'y', 'z'];
                        let index = 0;
                        return {
                            next() {
                                log('LOCAL: next() called, index=' + index);
                                if (index < values.length) {
                                    let val = values[index++];
                                    log('LOCAL: returning value=' + val);
                                    return { value: val, done: false };
                                }
                                log('LOCAL: returning done=true');
                                return { done: true };
                            }
                        };
                    }
                };
                
                log('LOCAL: Before for-await-of');
                let result = '';
                for await (let item of localIterable) {
                    __debug(); // INSIDE loop body
                    log('LOCAL: Inside loop, item=' + item);
                    result = result + item;
                }
                log('LOCAL: After loop, result=' + result);
                return result;
            }
            
            test();
        ");

        // Collect debug messages from local scope test
        var localDebugMessages = new List<DebugMessage>();
        while (engine1.DebugMessages().TryRead(out var msg))
        {
            localDebugMessages.Add(msg);
        }

        await System.Threading.Tasks.Task.Delay(1000);
        var localFinalResult = await engine1.Evaluate("test()");

        output.WriteLine("");
        output.WriteLine($"[LOCAL] Final result: '{localFinalResult}'");
        output.WriteLine($"[LOCAL] Debug messages captured: {localDebugMessages.Count}");
        
        if (localDebugMessages.Count > 0)
        {
            output.WriteLine("");
            output.WriteLine("LOCAL SCOPE DEBUG MESSAGES (from inside loop):");
            for (int i = 0; i < localDebugMessages.Count; i++)
            {
                output.WriteLine($"  Message {i + 1} - Variables count: {localDebugMessages[i].Variables.Count}");
                // Show key variables
                if (localDebugMessages[i].Variables.ContainsKey("item"))
                    output.WriteLine($"    item = {localDebugMessages[i].Variables["item"]}");
                if (localDebugMessages[i].Variables.ContainsKey("result"))
                    output.WriteLine($"    result = {localDebugMessages[i].Variables["result"]}");
                if (localDebugMessages[i].Variables.ContainsKey("localIterable"))
                    output.WriteLine($"    localIterable = {localDebugMessages[i].Variables["localIterable"]}");
            }
        }
        
        output.WriteLine("");
        output.WriteLine("===========================================");
        output.WriteLine("");

        // Test with GLOBAL scope iterable - __debug() INSIDE loop body
        var engine2 = new JsEngine();
        engine2.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[GLOBAL] {msg}");
            return null;
        });

        await engine2.Run(@"
            log('=== GLOBAL SCOPE - Debug Inside Loop ===');
            
            let globalIterable = {
                [Symbol.iterator]() {
                    log('GLOBAL: Symbol.iterator called');
                    let values = ['x', 'y', 'z'];
                    let index = 0;
                    return {
                        next() {
                            log('GLOBAL: next() called, index=' + index);
                            if (index < values.length) {
                                let val = values[index++];
                                log('GLOBAL: returning value=' + val);
                                return { value: val, done: false };
                            }
                            log('GLOBAL: returning done=true');
                            return { done: true };
                        }
                    };
                }
            };
            
            async function test() {
                log('GLOBAL: Before for-await-of');
                let result = '';
                for await (let item of globalIterable) {
                    __debug(); // INSIDE loop body
                    log('GLOBAL: Inside loop, item=' + item);
                    result = result + item;
                }
                log('GLOBAL: After loop, result=' + result);
                return result;
            }
            
            test();
        ");

        // Collect debug messages from global scope test
        var globalDebugMessages = new List<DebugMessage>();
        while (engine2.DebugMessages().TryRead(out var msg))
        {
            globalDebugMessages.Add(msg);
        }

        await System.Threading.Tasks.Task.Delay(1000);
        var globalFinalResult = await engine2.Evaluate("test()");

        output.WriteLine("");
        output.WriteLine($"[GLOBAL] Final result: '{globalFinalResult}'");
        output.WriteLine($"[GLOBAL] Debug messages captured: {globalDebugMessages.Count}");
        
        if (globalDebugMessages.Count > 0)
        {
            output.WriteLine("");
            output.WriteLine("GLOBAL SCOPE DEBUG MESSAGES (from inside loop):");
            for (int i = 0; i < globalDebugMessages.Count; i++)
            {
                output.WriteLine($"  Message {i + 1} - Variables count: {globalDebugMessages[i].Variables.Count}");
                // Show key variables
                if (globalDebugMessages[i].Variables.ContainsKey("item"))
                    output.WriteLine($"    item = {globalDebugMessages[i].Variables["item"]}");
                if (globalDebugMessages[i].Variables.ContainsKey("result"))
                    output.WriteLine($"    result = {globalDebugMessages[i].Variables["result"]}");
                if (globalDebugMessages[i].Variables.ContainsKey("globalIterable"))
                    output.WriteLine($"    globalIterable = {globalDebugMessages[i].Variables["globalIterable"]}");
            }
        }
        else
        {
            output.WriteLine("");
            output.WriteLine("*** NO DEBUG MESSAGES CAPTURED - Loop body never executed! ***");
        }

        output.WriteLine("");
        output.WriteLine("=== CONCLUSION ===");
        output.WriteLine($"Local scope: {(localDebugMessages.Count > 0 ? "✅ Loop body executed" : "❌ Loop body NOT executed")}");
        output.WriteLine($"Global scope: {(globalDebugMessages.Count > 0 ? "✅ Loop body executed" : "❌ Loop body NOT executed")}");
    }

    [Fact(Timeout = 5000)]
    public async Task ManualIterationComparison_GlobalVsLocal()
    {
        output.WriteLine("=== MANUAL ITERATION COMPARISON ===");
        output.WriteLine("Testing if manually creating the loop structure makes a difference");
        output.WriteLine("");
        
        // Test with LOCAL scope - manual iteration
        var engine1 = new JsEngine();
        engine1.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[LOCAL-MANUAL] {msg}");
            return null;
        });

        await engine1.Run(@"
            async function test() {
                log('=== LOCAL SCOPE - Manual Iteration ===');
                
                let localIterable = {
                    [Symbol.iterator]() {
                        log('Symbol.iterator called');
                        let values = ['x', 'y', 'z'];
                        let index = 0;
                        return {
                            next() {
                                log('next() called, index=' + index);
                                if (index < values.length) {
                                    let val = values[index++];
                                    return { value: val, done: false };
                                }
                                return { done: true };
                            }
                        };
                    }
                };
                
                log('Getting iterator manually');
                let iterator = localIterable[Symbol.iterator]();
                log('Got iterator: ' + typeof iterator);
                
                log('Calling next() manually in loop');
                let result = '';
                let iterResult = iterator.next();
                while (!iterResult.done) {
                    log('Manual loop iteration, value=' + iterResult.value);
                    __debug();
                    result = result + iterResult.value;
                    iterResult = iterator.next();
                }
                
                log('Manual loop done, result=' + result);
                return result;
            }
            
            test();
        ");

        var localManualDebugMessages = new List<DebugMessage>();
        while (engine1.DebugMessages().TryRead(out var msg))
        {
            localManualDebugMessages.Add(msg);
        }

        await System.Threading.Tasks.Task.Delay(1000);
        var localManualResult = await engine1.Evaluate("test()");
        
        output.WriteLine($"[LOCAL-MANUAL] Result: '{localManualResult}'");
        output.WriteLine($"[LOCAL-MANUAL] Debug messages: {localManualDebugMessages.Count}");
        output.WriteLine("");

        // Test with GLOBAL scope - manual iteration
        var engine2 = new JsEngine();
        engine2.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[GLOBAL-MANUAL] {msg}");
            return null;
        });

        await engine2.Run(@"
            log('=== GLOBAL SCOPE - Manual Iteration ===');
            
            let globalIterable = {
                [Symbol.iterator]() {
                    log('Symbol.iterator called');
                    let values = ['x', 'y', 'z'];
                    let index = 0;
                    return {
                        next() {
                            log('next() called, index=' + index);
                            if (index < values.length) {
                                let val = values[index++];
                                return { value: val, done: false };
                            }
                            return { done: true };
                        }
                    };
                }
            };
            
            async function test() {
                log('Getting iterator manually');
                let iterator = globalIterable[Symbol.iterator]();
                log('Got iterator: ' + typeof iterator);
                
                log('Calling next() manually in loop');
                let result = '';
                let iterResult = iterator.next();
                while (!iterResult.done) {
                    log('Manual loop iteration, value=' + iterResult.value);
                    __debug();
                    result = result + iterResult.value;
                    iterResult = iterator.next();
                }
                
                log('Manual loop done, result=' + result);
                return result;
            }
            
            test();
        ");

        var globalManualDebugMessages = new List<DebugMessage>();
        while (engine2.DebugMessages().TryRead(out var msg))
        {
            globalManualDebugMessages.Add(msg);
        }

        await System.Threading.Tasks.Task.Delay(1000);
        var globalManualResult = await engine2.Evaluate("test()");
        
        output.WriteLine($"[GLOBAL-MANUAL] Result: '{globalManualResult}'");
        output.WriteLine($"[GLOBAL-MANUAL] Debug messages: {globalManualDebugMessages.Count}");
        
        output.WriteLine("");
        output.WriteLine("=== MANUAL ITERATION CONCLUSION ===");
        output.WriteLine($"Local scope manual: {(localManualResult?.ToString() == "xyz" ? "✅ Works" : "❌ Failed")}");
        output.WriteLine($"Global scope manual: {(globalManualResult?.ToString() == "xyz" ? "✅ Works" : "❌ Failed")}");
        output.WriteLine("");
        output.WriteLine("If manual iteration works for both, the issue is specific to for-await-of transformation.");
    }
}
