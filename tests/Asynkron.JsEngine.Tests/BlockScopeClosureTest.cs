using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for closures accessing block-scoped variables.
/// This is the exact failing pattern from Test262 block-scope/shadowing/lookup-from-closure.js.
/// </summary>
public class BlockScopeClosureTest(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task NestedFunctionInBlock_CanAccessBlockScopedVariables()
    {
        // This is the exact Test262 failure case
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            function f5(one) {
              var x = one + 1;
              let y = one + 2;
              const u = one + 4;
              {
                let z = one + 3;
                const v = one + 5;
                function f() {
                  return [one, x, y, z, u, v];
                }
                return f();
              }
            }
            f5(1);
            """);

        Output.WriteLine($"Result: {result}");
        Output.WriteLine($"Result Type: {result?.GetType().Name}");

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(1.0, array.GetElement(0).AsDouble()); // one = 1
        Assert.Equal(2.0, array.GetElement(1).AsDouble()); // x = one + 1 = 2
        Assert.Equal(3.0, array.GetElement(2).AsDouble()); // y = one + 2 = 3
        Assert.Equal(4.0, array.GetElement(3).AsDouble()); // z = one + 3 = 4
        Assert.Equal(5.0, array.GetElement(4).AsDouble()); // u = one + 4 = 5
        Assert.Equal(6.0, array.GetElement(5).AsDouble()); // v = one + 5 = 6
    }

    [Fact]
    public async Task SimpleBlockShadowing_InnerBlockOverridesOuter()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            function fn(a) {
                let b = 1;
                {
                    let a = 2;
                    let b = 2;
                    return [a, b];
                }
            }
            fn(1);
            """);

        Output.WriteLine($"Result: {result}");

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(2.0, array.GetElement(0).AsDouble()); // inner a = 2
        Assert.Equal(2.0, array.GetElement(1).AsDouble()); // inner b = 2
    }

    [Fact]
    public async Task ClosureInBlock_AccessesBlockVariable()
    {
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true });

        var result = await engine.Evaluate("""
            function outer() {
                {
                    let z = 42;
                    function inner() {
                        return z;
                    }
                    return inner();
                }
            }
            outer();
            """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal(42.0, (double)result!);
    }

    [Fact]
    public async Task ClosureInFunctionScope_WorksWithoutBlock()
    {
        // Simpler case - no block scope, just function scope
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            function outer() {
                let z = 42;
                function inner() {
                    return z;
                }
                return inner();
            }
            outer();
            """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal(42.0, (double)result!);
    }

    [Fact]
    public async Task BlockShadowing_WithoutClosure()
    {
        // Test that block shadowing works without closures
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            let x = 1;
            {
                let x = 2;
                x;
            }
            """);

        Output.WriteLine($"Result: {result}");
        Assert.Equal(2.0, (double)result!);
    }
}
