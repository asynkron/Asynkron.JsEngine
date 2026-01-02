// Run as: dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~NestedFunctionInBlockScope"
using Asynkron.JsEngine.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class ClosureCaptureTest(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task NestedFunctionInBlockScope_SimpleCase()
    {
        // First test a simpler case - just capturing from the enclosing block
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { DebugMode = true, Logger = logger });

        // Even simpler - no block, just function scope let
        var result = await engine.Evaluate("""
            (function() {
              let z = 42;
              function f() {
                return z;
              }
              return f();
            })();
            """);

        foreach (var entry in logger.Collector.Snapshot())
        {
            Output.WriteLine(entry.Message);
        }

        Output.WriteLine($"Result: {result}");
        Output.WriteLine($"Result Type: {result?.GetType().Name}");

        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task NestedFunctionInBlockScope_DirectAccess()
    {
        // Direct access to block-scoped variable should work
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            (function() {
              {
                let z = 42;
                return z;  // Direct access
              }
            })();
            """);

        foreach (var entry in logger.Collector.Snapshot())
        {
            Output.WriteLine(entry.Message);
        }

        Output.WriteLine($"Result: {result}");
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task NestedFunctionInBlockScope_ArrowFunction()
    {
        // Test arrow function in block - simpler case
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            (function() {
              {
                let z = 42;
                var f = () => z;
                return f();
              }
            })();
            """);

        foreach (var entry in logger.Collector.Snapshot())
        {
            Output.WriteLine(entry.Message);
        }

        Output.WriteLine($"Result: {result}");
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task NestedFunctionInBlockScope_WithBlock()
    {
        // Test with function declaration in block
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { DebugMode = true, Logger = logger });

        try
        {
            var result = await engine.Evaluate("""
                (function() {
                  {
                    let z = 42;
                    function f() {
                      return z;
                    }
                    return f();
                  }
                })();
                """);

            Output.WriteLine($"Result: {result}");
            Output.WriteLine($"Result Type: {result?.GetType().Name}");

            Assert.Equal(42.0, result);
        }
        catch (Exception ex)
        {
            // Log all debug messages
            foreach (var entry in logger.Collector.Snapshot())
            {
                Output.WriteLine(entry.Message);
            }
            Output.WriteLine($"Exception: {ex.Message}");
            throw;
        }
    }

    [Fact]
    public async Task NestedFunctionInBlockScope_CapturesBlockVariables()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            function fn(one) {
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
            fn(1);
            """);

        foreach (var entry in logger.Collector.Snapshot())
        {
            Output.WriteLine(entry.Message);
        }

        Output.WriteLine($"Result: {result}");
        Output.WriteLine($"Result Type: {result?.GetType().Name}");

        var array = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal(1.0, array.GetElement(0).AsDouble()); // one
        Assert.Equal(2.0, array.GetElement(1).AsDouble()); // x = one + 1
        Assert.Equal(3.0, array.GetElement(2).AsDouble()); // y = one + 2
        Assert.Equal(4.0, array.GetElement(3).AsDouble()); // z = one + 3
        Assert.Equal(5.0, array.GetElement(4).AsDouble()); // u = one + 4
        Assert.Equal(6.0, array.GetElement(5).AsDouble()); // v = one + 5
    }
}
