using Xunit;
using System.Collections.Generic;

namespace Asynkron.JsEngine.Tests;

public class AsyncIterationTests
{
    [Fact]
    public async Task RegularForOf_WithAwaitInBody()
    {
        // Test that regular for-of with await in body works
        var engine = new JsEngine();
        
        await engine.Run(@"
            let result = """";
            let promises = [
                Promise.resolve(""a""),
                Promise.resolve(""b""),
                Promise.resolve(""c"")
            ];
            
            async function test() {
                for (let promise of promises) {
                    let item = await promise;
                    result = result + item;
                }
            }
            
            test();
        ");
        
        var result = engine.Evaluate("result;");
        Assert.Equal("abc", result);
    }

    [Fact]
    public async Task ForAwaitOf_WithArray()
    {
        var engine = new JsEngine();
        
        await engine.Run(@"
            let result = """";
            let arr = [""a"", ""b"", ""c""];
            
            async function test() {
                for await (let item of arr) {
                    result = result + item;
                }
            }
            
            test();
        ");
        
        var result = engine.Evaluate("result;");
        Assert.Equal("abc", result);
    }
    
    [Fact]
    public async Task ForAwaitOf_WithGenerator()
    {
        var engine = new JsEngine();
        
        await engine.Run(@"
            let sum = 0;
            
            function* generator() {
                yield 1;
                yield 2;
                yield 3;
            }
            
            async function test() {
                for await (let num of generator()) {
                    sum = sum + num;
                }
            }
            
            test();
        ");
        
        var result = engine.Evaluate("sum;");
        Assert.Equal(6.0, result);
    }
    
    [Fact]
    public async Task ForAwaitOf_WithString()
    {
        var engine = new JsEngine();
        
        await engine.Run(@"
            let result = """";
            
            async function test() {
                for await (let char of ""hello"") {
                    result = result + char;
                }
            }
            
            test();
        ");
        
        var result = engine.Evaluate("result;");
        Assert.Equal("hello", result);
    }
    
    [Fact]
    public async Task ForAwaitOf_WithBreak()
    {
        var engine = new JsEngine();
        
        await engine.Run(@"
            let count = 0;
            let arr = [1, 2, 3, 4, 5];
            
            async function test() {
                for await (let item of arr) {
                    count = count + 1;
                    if (item === 3) {
                        break;
                    }
                }
            }
            
            test();
        ");
        
        var result = engine.Evaluate("count;");
        Assert.Equal(3.0, result);
    }
    
    [Fact]
    public async Task ForAwaitOf_WithContinue()
    {
        var engine = new JsEngine();
        
        await engine.Run(@"
            let sum = 0;
            let arr = [1, 2, 3, 4, 5];
            
            async function test() {
                for await (let item of arr) {
                    if (item === 3) {
                        continue;
                    }
                    sum = sum + item;
                }
            }
            
            test();
        ");
        
        var result = engine.Evaluate("sum;");
        Assert.Equal(12.0, result); // 1 + 2 + 4 + 5 = 12
    }
    
    [Fact]
    public void ForAwaitOf_RequiresAsyncFunction()
    {
        var engine = new JsEngine();
        
        // for await...of must be used inside an async function
        // This should work in our current implementation even outside async
        // but in strict JavaScript it would require async context
        var result = engine.Evaluate(@"
            let result = """";
            for await (let item of [""x"", ""y""]) {
                result = result + item;
            }
            result;
        ");
        
        Assert.Equal("xy", result);
    }
    
    [Fact]
    public async Task SymbolAsyncIterator_Exists()
    {
        var engine = new JsEngine();
        
        var result = await engine.Run(@"
            typeof Symbol.asyncIterator;
        ");
        
        Assert.Equal("symbol", result);
    }
    
    [Fact]
    public async Task ForAwaitOf_WithPromiseArray()
    {
        // NOTE: This test demonstrates a limitation - for-await-of with promises
        // in arrays requires CPS transformation support. 
        // Currently, promises in arrays are treated as objects, not awaited.
        var engine = new JsEngine();
        
        await engine.Run(@"
            let result = """";
            
            // For-await-of can iterate arrays, but won't automatically await promise values
            // This works if we await them manually in the loop body
            let promises = [
                Promise.resolve(""a""),
                Promise.resolve(""b""),
                Promise.resolve(""c"")
            ];
            
            async function test() {
                for await (let promise of promises) {
                    // Need to manually await the promise
                    let item = await promise;
                    result = result + item;
                }
            }
            
            test();
        ");
        
        var result = engine.Evaluate("result;");
        Assert.Equal("abc", result);
    }
    
    [Fact]
    public async Task ForAwaitOf_WithCustomAsyncIterator()
    {
        var engine = new JsEngine();
        
        await engine.Run(@"
            let result = """";
            
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
        ");
        
        var result = engine.Evaluate("result;");
        Assert.Equal("123", result);
    }
    
    [Fact]
    public async Task ForAwaitOf_WithCustomSyncAsyncIterator()
    {
        // This test shows that Symbol.asyncIterator works when it returns synchronous values
        var engine = new JsEngine();
        
        await engine.Run(@"
            let result = """";
            
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
        ");
        
        var result = engine.Evaluate("result;");
        Assert.Equal("123", result);
    }
    
    [Fact]
    public async Task ForAwaitOf_ErrorPropagation()
    {
        var engine = new JsEngine();
        var errorCaught = false;
        
        engine.SetGlobalFunction("markError", args =>
        {
            errorCaught = true;
            return null;
        });
        
        await engine.Run(@"
            let asyncIterable = {
                [Symbol.asyncIterator]() {
                    let count = 0;
                    return {
                        next() {
                            count = count + 1;
                            if (count === 2) {
                                return Promise.reject(""test error"");
                            }
                            if (count <= 3) {
                                return Promise.resolve({ value: count, done: false });
                            }
                            return Promise.resolve({ done: true });
                        }
                    };
                }
            };
            
            async function test() {
                try {
                    for await (let num of asyncIterable) {
                        // Should throw on second iteration
                    }
                } catch (e) {
                    markError();
                }
            }
            
            test();
        ");
        
        Assert.True(errorCaught);
    }
    
    [Fact]
    public async Task ForAwaitOf_SyncErrorPropagation()
    {
        // Test error handling with synchronous iterators
        var engine = new JsEngine();
        var errorCaught = false;
        
        engine.SetGlobalFunction("markError", args =>
        {
            errorCaught = true;
            return null;
        });
        
        await engine.Run(@"
            let syncIterable = {
                [Symbol.iterator]() {
                    let count = 0;
                    return {
                        next() {
                            count = count + 1;
                            if (count === 2) {
                                throw ""test error"";
                            }
                            if (count <= 3) {
                                return { value: count, done: false };
                            }
                            return { done: true };
                        }
                    };
                }
            };
            
            async function test() {
                try {
                    for await (let num of syncIterable) {
                        // Should throw on second iteration
                    }
                } catch (e) {
                    markError();
                }
            }
            
            test();
        ");
        
        Assert.True(errorCaught);
    }
    
    [Fact]
    public async Task ForAwaitOf_FallbackToSyncIterator()
    {
        var engine = new JsEngine();
        
        await engine.Run(@"
            let result = """";
            
            // Object with only sync iterator (Symbol.iterator)
            let syncIterable = {
                [Symbol.iterator]() {
                    let values = [""x"", ""y"", ""z""];
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
                for await (let item of syncIterable) {
                    result = result + item;
                }
            }
            
            test();
        ");
        
        var result = engine.Evaluate("result;");
        Assert.Equal("xyz", result);
    }
    
    [Fact]
    public void ForAwaitOf_WithSyncIteratorNoAsync()
    {
        var engine = new JsEngine();
        
        // Test without async function to isolate the issue
        var result = engine.Evaluate(@"
            let result = """";
            
            // Object with only sync iterator (Symbol.iterator)
            let syncIterable = {
                [Symbol.iterator]() {
                    let values = [""x"", ""y"", ""z""];
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
        ");
        
        Assert.Equal("xyz", result);
    }

    [Fact]
    public async Task RegularForOf_WithAwaitInBodyWithDebug()
    {
        // Test that regular for-of with await in body works, using __debug() to inspect state
        var engine = new JsEngine();
        
        await engine.Run(@"
            let result = """";
            let promises = [
                Promise.resolve(""a""),
                Promise.resolve(""b""),
                Promise.resolve(""c"")
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
        ");
        
        var result = engine.Evaluate("result;");
        Assert.Equal("abc", result);

        // Verify we got debug messages - should have 7 total:
        // 3 iterations * 2 (before + after await) + 1 after loop = 7
        var debugMessages = new List<DebugMessage>();
        for (int i = 0; i < 7; i++)
        {
            debugMessages.Add(await engine.DebugMessages().ReadAsync());
        }

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

    [Fact]
    public async Task ForAwaitOf_WithArrayWithDebug()
    {
        // Test for-await-of with __debug() to inspect state during iteration
        var engine = new JsEngine();
        
        await engine.Run(@"
            let result = """";
            let arr = [""x"", ""y"", ""z""];
            
            async function test() {
                for await (let item of arr) {
                    __debug();
                    result = result + item;
                }
                __debug();
            }
            
            test();
        ");
        
        var result = engine.Evaluate("result;");
        Assert.Equal("xyz", result);

        // Should have 4 debug messages (3 iterations + 1 after loop)
        var debugMessages = new List<DebugMessage>();
        for (int i = 0; i < 4; i++)
        {
            debugMessages.Add(await engine.DebugMessages().ReadAsync());
        }

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
