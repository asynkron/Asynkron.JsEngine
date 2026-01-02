using Asynkron.JsEngine.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Tests;

public class BlockScopeShadowingDebugTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task SimpleBlockShadowing_DirectReturn()
    {
        // Simplest possible test case - no function, just a block
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            let x = 1;
            {
                let x = 2;
                x;
            }
            """);

        Output.WriteLine($"Result: {result}");
        // The last expression in the block should be 2 (the shadowed x)
        Assert.Equal(2.0, (double)result!);
    }

    [Fact]
    public async Task DebugBlockScopeIdStamping()
    {
        // Let's trace what happens with scope IDs
        var source = """
            let x = 1;
            {
                let x = 2;
                x;
            }
            """;

        await using var engine = new JsEngine();

        // Parse the program
        var program = engine.ParseProgram(source);

        // Find the BlockStatement and check its ScopeId/SlotMap
        Output.WriteLine("Program body:");
        foreach (var stmt in program.Body)
        {
            Output.WriteLine($"  {stmt.GetType().Name}");
            if (stmt is BlockStatement block)
            {
                Output.WriteLine($"    ScopeId={block.ScopeId} SlotCount={block.SlotCount}");
                Output.WriteLine($"    SlotMap count={block.SlotMap.Count}");
                foreach (var kv in block.SlotMap)
                {
                    Output.WriteLine($"    SlotMap[{kv.Key}]={kv.Value}");
                }
            }
        }

        // Execute the program
        var result = await engine.Evaluate(program);
        Output.WriteLine($"Result: {result}");
    }


    [Fact]
    public async Task InnerBlockShadowsOuterLetVariable()
    {
        // This is the exact pattern from Test262
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            (function() {
                function fn(a) {
                    let b = 1;
                    {
                        let a = 2;
                        let b = 2;
                        return [a, b];
                    }
                }
                return fn(1);
            })();
            """);

        Output.WriteLine($"Result: {result}");

        var array = Assert.IsType<JsTypes.JsArray>(result);
        var elem0 = array.GetElement(0).AsDouble();
        var elem1 = array.GetElement(1).AsDouble();

        Output.WriteLine($"elem0 = {elem0}, elem1 = {elem1}");

        // The inner block should shadow both 'a' and 'b'
        Assert.Equal(2.0, elem0);  // inner a = 2
        Assert.Equal(2.0, elem1);  // inner b = 2
    }

    [Fact]
    public async Task InnerBlockShadowsOuterLetVariableWithLogging()
    {
        // Same test but with debug logging to understand what's happening
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            (function() {
                function fn(a) {
                    let b = 1;
                    {
                        let a = 2;
                        let b = 2;
                        return [a, b];
                    }
                }
                return fn(1);
            })();
            """);

        Output.WriteLine($"Result: {result}");

        var array = Assert.IsType<JsTypes.JsArray>(result);
        var elem0 = array.GetElement(0).AsDouble();
        var elem1 = array.GetElement(1).AsDouble();

        Output.WriteLine($"elem0 = {elem0}, elem1 = {elem1}");

        Assert.Equal(2.0, elem0);
        Assert.Equal(2.0, elem1);
    }

    [Fact]
    public async Task SimpleBlockShadowing()
    {
        // Minimal test case to understand shadowing
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            (function() {
                let x = 1;
                {
                    let x = 2;
                    return x;
                }
            })();
            """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal(2.0, (double)result!);
    }

    [Fact]
    public async Task SimpleBlockShadowingInScript()
    {
        // Top-level script version (no wrapping IIFE)
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            let x = 1;
            let result;
            {
                let x = 2;
                result = x;
            }
            result;
            """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal(2.0, (double)result!);
    }

    [Fact]
    public async Task ClosureAccessesBlockScopedVariable()
    {
        // This is the exact Test262 failure case: closure inside a block
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            function outer() {
                let x = 1;
                {
                    let z = 2;
                    function inner() {
                        return z;  // closure should access block-scoped z
                    }
                    return inner();
                }
            }
            outer();
            """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal(2.0, (double)result!);
    }
}
