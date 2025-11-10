using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Debug tests to diagnose and fix async iterable test failures.
/// Following the pattern of adding __debug() calls to understand execution flow.
/// </summary>
public class AsyncIterableDebugTests
{
    private readonly ITestOutputHelper _output;

    public AsyncIterableDebugTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ForAwaitOf_WithString_Debug()
    {
        // Debug version of the failing string test
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {message}");
            return null;
        });
        
        await engine.Run(@"
            let result = """";
            
            log(""Before async function"");
            
            async function test() {
                log(""Inside test function"");
                log(""About to start for-await-of"");
                
                for await (let char of ""hello"") {
                    log(""In loop, char: "" + char);
                    __debug();
                    result = result + char;
                    log(""After append, result: "" + result);
                }
                
                log(""After loop, final result: "" + result);
                __debug();
            }
            
            log(""About to call test()"");
            test();
            log(""After test() call"");
        ");
        
        var result = engine.Evaluate("result;");
        _output.WriteLine($"Final result: '{result}'");
        
        // Collect debug messages
        var debugMessages = new List<DebugMessage>();
        while (await engine.DebugMessages().WaitToReadAsync())
        {
            if (engine.DebugMessages().TryRead(out var msg))
            {
                debugMessages.Add(msg);
                _output.WriteLine($"Debug message {debugMessages.Count}: {msg.Variables.Count} variables");
                foreach (var kvp in msg.Variables)
                {
                    _output.WriteLine($"  {kvp.Key} = {kvp.Value}");
                }
            }
            else
            {
                break;
            }
        }
        
        _output.WriteLine($"Total debug messages: {debugMessages.Count}");
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task ForAwaitOf_WithString_ShowTransformation()
    {
        // Show the transformation of the for-await-of with string
        var source = @"
            async function test() {
                let result = """";
                for await (let char of ""hello"") {
                    result = result + char;
                }
            }
        ";

        var engine = new JsEngine();
        
        // Parse without transformation
        var originalSexpr = engine.ParseWithoutTransformation(source);
        _output.WriteLine("=== ORIGINAL S-EXPRESSION ===");
        _output.WriteLine(originalSexpr.ToString());
        _output.WriteLine("");

        // Parse with transformation
        var transformedSexpr = engine.Parse(source);
        _output.WriteLine("=== TRANSFORMED S-EXPRESSION ===");
        _output.WriteLine(transformedSexpr.ToString());
    }

    [Fact]
    public async Task ForAwaitOf_WithArray_CompareWithString()
    {
        // This test PASSES - let's see what's different
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {message}");
            return null;
        });
        
        await engine.Run(@"
            let result = """";
            let arr = [""h"", ""e"", ""l"", ""l"", ""o""];
            
            log(""Before async function"");
            
            async function test() {
                log(""Inside test function"");
                log(""About to start for-await-of"");
                
                for await (let char of arr) {
                    log(""In loop, char: "" + char);
                    result = result + char;
                    log(""After append, result: "" + result);
                }
                
                log(""After loop, final result: "" + result);
            }
            
            log(""About to call test()"");
            test();
            log(""After test() call"");
        ");
        
        var result = engine.Evaluate("result;");
        _output.WriteLine($"Final result: '{result}'");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ForAwaitOf_WithString_NoAsync()
    {
        // Test without async function wrapper - as shown in ForAwaitOf_RequiresAsyncFunction
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {message}");
            return null;
        });
        
        var result = engine.Evaluate(@"
            let result = """";
            log(""Before for-await"");
            for await (let char of ""hello"") {
                log(""In loop, char: "" + char);
                result = result + char;
            }
            log(""After for-await"");
            result;
        ");
        
        _output.WriteLine($"Final result: '{result}'");
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task SimpleString_Iterator_Test()
    {
        // Test that strings have an iterator in the first place
        var engine = new JsEngine();
        
        var result = await engine.Run(@"
            let str = ""hello"";
            let hasIterator = typeof str[Symbol.iterator] === ""function"";
            hasIterator;
        ");
        
        _output.WriteLine($"String has iterator: {result}");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task SimpleString_ManualIteration_Test()
    {
        // Test manual iteration over a string
        var engine = new JsEngine();
        
        await engine.Run(@"
            let str = ""hello"";
            let result = """";
            let iterator = str[Symbol.iterator]();
            
            let iterResult = iterator.next();
            while (!iterResult.done) {
                result = result + iterResult.value;
                iterResult = iterator.next();
            }
        ");
        
        var result = engine.Evaluate("result;");
        _output.WriteLine($"Result: '{result}'");
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task ForAwaitOf_WithString_ManualAsyncIteration()
    {
        // Test manual async iteration over a string
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {message}");
            return null;
        });
        
        await engine.Run(@"
            let str = ""hello"";
            let result = """";
            
            async function test() {
                log(""Getting iterator"");
                let iterator = str[Symbol.asyncIterator] || str[Symbol.iterator]();
                log(""Got iterator: "" + (typeof iterator));
                
                if (typeof iterator === ""function"") {
                    log(""iterator is a function, calling it"");
                    iterator = iterator();
                }
                
                log(""Calling next()"");
                let iterResult = await iterator.next();
                log(""First next result: "" + JSON.stringify(iterResult));
                
                while (!iterResult.done) {
                    log(""Value: "" + iterResult.value);
                    result = result + iterResult.value;
                    iterResult = await iterator.next();
                    log(""Next result: "" + JSON.stringify(iterResult));
                }
                
                log(""Done iterating"");
            }
            
            test();
        ");
        
        var result = engine.Evaluate("result;");
        _output.WriteLine($"Result: '{result}'");
        Assert.Equal("hello", result);
    }
}
