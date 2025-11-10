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

    [Fact(Timeout = 1000)]
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
        
        // Collect debug messages - don't wait forever, just get what's available
        var debugMessages = new List<DebugMessage>();
        while (engine.DebugMessages().TryRead(out var msg))
        {
            debugMessages.Add(msg);
            _output.WriteLine($"Debug message {debugMessages.Count}: {msg.Variables.Count} variables");
            foreach (var kvp in msg.Variables)
            {
                _output.WriteLine($"  {kvp.Key} = {kvp.Value}");
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
    public void Test_OR_Expression_Parsing()
    {
        // Test to see the parsed S-expression for the OR expression
        var engine = new JsEngine();
        
        // Test simple OR first
        var simpleOr = @"let result = a || b;";
        var simpleSexpr = engine.Parse(simpleOr);
        _output.WriteLine("=== SIMPLE OR ===");
        _output.WriteLine(simpleSexpr.ToString());
        _output.WriteLine("");
        
        // Test OR with function call on right
        var orWithCall = @"let result = a || b();";
        var orWithCallSexpr = engine.Parse(orWithCall);
        _output.WriteLine("=== OR WITH FUNCTION CALL ===");
        _output.WriteLine(orWithCallSexpr.ToString());
        _output.WriteLine("");
        
        // Test the actual problematic expression
        var problematicExpr = @"let iterator = str[Symbol.asyncIterator] || str[Symbol.iterator]();";
        var problematicSexpr = engine.Parse(problematicExpr);
        _output.WriteLine("=== PROBLEMATIC OR EXPRESSION ===");
        _output.WriteLine(problematicSexpr.ToString());
        _output.WriteLine("");
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

    [Fact]
    public async Task ForAwaitOf_WithBreak_Debug()
    {
        // Debug version of the failing break test
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {message}");
            return null;
        });
        
        await engine.Run(@"
            let count = 0;
            let arr = [1, 2, 3, 4, 5];
            
            async function test() {
                log(""Starting loop"");
                for await (let item of arr) {
                    log(""Item: "" + item);
                    log(""Count before increment: "" + count);
                    count = count + 1;
                    log(""Count after increment: "" + count);
                    log(""About to check if item === 3, item is: "" + item);
                    if (item === 3) {
                        log(""Breaking at item 3"");
                        break;
                    }
                    log(""Continuing to next item"");
                }
                log(""After loop, count: "" + count);
            }
            
            test();
        ");
        
        var result = engine.Evaluate("count;");
        _output.WriteLine($"Final count: '{result}'");
        Assert.Equal(3.0, result);
    }

    [Fact]
    public async Task ForAwaitOf_WithBreak_ShowTransformation()
    {
        // Show the transformation of for-await-of with break
        var source = @"
            async function test() {
                let count = 0;
                let arr = [1, 2, 3];
                for await (let item of arr) {
                    count = count + 1;
                    if (item === 3) {
                        break;
                    }
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
    public async Task ForAwaitOf_WithContinue_Debug()
    {
        // Debug version of the failing continue test
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {message}");
            return null;
        });
        
        await engine.Run(@"
            let sum = 0;
            let arr = [1, 2, 3, 4, 5];
            
            async function test() {
                log(""Starting loop"");
                for await (let item of arr) {
                    log(""Item: "" + item);
                    if (item === 3) {
                        log(""Skipping item 3"");
                        continue;
                    }
                    log(""Adding item: "" + item);
                    sum = sum + item;
                    log(""Sum after add: "" + sum);
                }
                log(""After loop, sum: "" + sum);
            }
            
            test();
        ");
        
        var result = engine.Evaluate("sum;");
        _output.WriteLine($"Final sum: '{result}'");
        Assert.Equal(12.0, result); // 1 + 2 + 4 + 5 = 12
    }

    [Fact]
    public async Task ForAwaitOf_WithGenerator_Debug()
    {
        // Debug version of the failing generator test
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {message}");
            return null;
        });
        
        await engine.Run(@"
            let sum = 0;
            
            function* generator() {
                log(""Generator: yielding 1"");
                yield 1;
                log(""Generator: yielding 2"");
                yield 2;
                log(""Generator: yielding 3"");
                yield 3;
                log(""Generator: done"");
            }
            
            async function test() {
                log(""Starting loop"");
                for await (let num of generator()) {
                    log(""Got num: "" + num);
                    sum = sum + num;
                    log(""Sum after add: "" + sum);
                }
                log(""After loop, sum: "" + sum);
            }
            
            test();
        ");
        
        var result = engine.Evaluate("sum;");
        _output.WriteLine($"Final sum: '{result}'");
        Assert.Equal(6.0, result);
    }

    [Fact]
    public async Task ForAwaitOf_SimpleNoConditions_Debug()
    {
        // Test for-await-of without any break/continue/conditions to isolate the basic iteration
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {message}");
            return null;
        });
        
        await engine.Run(@"
            let count = 0;
            let arr = [""a"", ""b"", ""c""];
            
            async function test() {
                log(""Starting loop"");
                for await (let item of arr) {
                    log(""Item: "" + item);
                    count = count + 1;
                    log(""Count: "" + count);
                }
                log(""After loop, count: "" + count);
            }
            
            test();
        ");
        
        var result = engine.Evaluate("count;");
        _output.WriteLine($"Final count: '{result}'");
        Assert.Equal(3.0, result);
    }

    [Fact]
    public async Task ForAwaitOf_WithIfNoBreak_Debug()
    {
        // Test with an if statement but no break to see if if statements work correctly
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("log", args =>
        {
            var message = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            _output.WriteLine($"LOG: {message}");
            return null;
        });
        
        await engine.Run(@"
            let count = 0;
            let arr = [1, 2, 3, 4, 5];
            
            async function test() {
                log(""Starting loop"");
                for await (let item of arr) {
                    log(""Item: "" + item);
                    if (item === 3) {
                        log(""Found item 3"");
                    }
                    count = count + 1;
                    log(""Count: "" + count);
                }
                log(""After loop, count: "" + count);
            }
            
            test();
        ");
        
        var result = engine.Evaluate("count;");
        _output.WriteLine($"Final count: '{result}'");
        Assert.Equal(5.0, result);
    }

    [Fact]
    public async Task ForAwaitOf_WithIfNoBreak_ShowTransformation()
    {
        // Show the transformation for if without break
        var source = @"
            async function test() {
                let count = 0;
                let arr = [1, 2, 3];
                for await (let item of arr) {
                    if (item === 2) {
                        log(""item is 2"");
                    }
                    count = count + 1;
                }
            }
        ";

        var engine = new JsEngine();
        var transformedSexpr = engine.Parse(source);
        _output.WriteLine("=== TRANSFORMED S-EXPRESSION ===");
        _output.WriteLine(transformedSexpr.ToString());
    }
}
