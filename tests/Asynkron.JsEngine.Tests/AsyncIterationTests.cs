using Xunit;

namespace Asynkron.JsEngine.Tests;

public class AsyncIterationTests
{
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
    
    [Fact(Skip = "Async iteration with generators requires CPS transformation support - see docs/LARGE_FEATURES_NOT_IMPLEMENTED.md")]
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
        var engine = new JsEngine();
        
        await engine.Run(@"
            let result = """";
            
            // Array of promises
            let promises = [
                Promise.resolve(""a""),
                Promise.resolve(""b""),
                Promise.resolve(""c"")
            ];
            
            async function test() {
                for await (let item of promises) {
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
}
