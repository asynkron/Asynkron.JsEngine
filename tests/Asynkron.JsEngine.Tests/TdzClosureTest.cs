using System.Globalization;
using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
public sealed class TdzClosureTest(ITestOutputHelper output)
{
    /// <summary>
    /// H0: Simple TDZ check - non-closure, same scope
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H0_SimpleTdz_ReadBeforeInit()
    {
        var testLogger = new TestLogger(output, "TdzTest", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        // Simple case: read `x` before let declaration
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate("""
                (function() {
                    console.log(x);  // Should throw ReferenceError
                    let x = 1;
                }());
            """);
        });

        output.WriteLine($"Caught: {ex.ThrownValue}");
        Assert.Contains("before initialization", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// H1: Simple TDZ check - non-closure, same scope, assignment
    /// This test is EXPECTED TO FAIL - it exposes the TDZ hoisting bug.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H1_SimpleTdz_AssignBeforeInit()
    {
        var testLogger = new TestLogger(output, "TdzTest", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        // Simple case: assign to `x` before let declaration
        // Per ECMAScript spec, `let x` should be hoisted with TDZ, so `x = 1` should throw ReferenceError.
        // BUG: Currently this doesn't throw - it creates a global variable instead.
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate("""
                (function() {
                    x = 1;  // Should throw ReferenceError since `let x` below is hoisted with TDZ
                    let x;
                }());
            """);
        });

        output.WriteLine($"Caught: {ex.ThrownValue}");
        Assert.Contains("before initialization", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// H2: Closure TDZ - closure in nested function, read
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H2_ClosureTdz_ReadBeforeInit()
    {
        var testLogger = new TestLogger(output, "TdzTest", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        // Closure case: read `x` from inner function before outer `let x` is initialized
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate("""
                (function() {
                    function f() { return x; }  // Closure captures x
                    f();  // Should throw ReferenceError
                    let x = 1;
                }());
            """);
        });

        output.WriteLine($"Caught: {ex.ThrownValue}");
        Assert.Contains("before initialization", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// H3: Closure TDZ - closure in nested function, assign
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H3_ClosureTdz_AssignBeforeInit()
    {
        var testLogger = new TestLogger(output, "TdzTest", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        // Closure case: assign `x` from inner function before outer `let x` is initialized
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate("""
                (function() {
                    function f() { x = 1; }  // Closure captures x
                    f();  // Should throw ReferenceError
                    let x;
                }());
            """);
        });

        output.WriteLine($"Caught: {ex.ThrownValue}");
        Assert.Contains("before initialization", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// H4: Let is hoisted, should see undefined without TDZ
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H4_VarIsHoisted_ShouldNotThrow()
    {
        var testLogger = new TestLogger(output, "TdzTest", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        // Var is hoisted, should not throw
        var result = await engine.Evaluate("""
            (function() {
                function f() { x = 1; }
                f();
                var x;
                return x;
            }());
        """);

        output.WriteLine($"Result: {result}");
        // Expected: 1 (var is hoisted and assignment succeeds)
        Assert.Equal(1.0, (double)result!);
    }

    [Fact(Timeout = 10000)]
    public async Task ClosureTdz_AssignBeforeInit_ShouldThrowReferenceError()
    {
        var testLogger = new TestLogger(output, "TdzTest", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        // Mimics the Test262 test function-local-closure-set-before-initialization.js
        var result = await engine.Evaluate("""
            var caught = false;
            var errorType = null;

            (function() {
              function f() { x = 1; }

              try {
                f();
              } catch (e) {
                caught = true;
                errorType = e.name;
              }

              let x;
            }());

            caught + "|" + errorType;
        """);

        output.WriteLine($"Result: {result}");
        // Expected: "true|ReferenceError" (exception should be caught)
        Assert.Equal("true|ReferenceError", result?.ToString());
    }

    /// <summary>
    /// H5: Block-local closure TDZ - bare block at script level
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H5_BlockLocalClosure_ReadBeforeInit()
    {
        var testLogger = new TestLogger(output, "TdzTest", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        // Block-local case: function reads x from block scope before let x
        var result = await engine.Evaluate("""
            var result = "not-caught";
            var fResult = "not-called";
            {
              function f() { return x + 1; }
              try { fResult = f(); } catch(e) { result = e.constructor.name; }
              let x;
            }
            result + "|" + fResult;
        """);

        output.WriteLine($"Result: {result}");
        // Expected: "ReferenceError|not-called"
        Assert.Equal("ReferenceError|not-called", result?.ToString());
    }

    /// <summary>
    /// Debug test: Check if x is accessible at all after let declaration
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Debug_LetXAfterDeclaration()
    {
        var testLogger = new TestLogger(output, "TdzTest", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        // Test that let x works normally - accessing after initialization
        var result = await engine.Evaluate("""
            (function() {
              let x = 42;
              return x;
            }());
        """);

        output.WriteLine($"Result: {result}");
        var val = result is JsValue jv ? jv.AsDouble() : Convert.ToDouble(result, CultureInfo.InvariantCulture);
        Assert.Equal(42.0, val);
    }

    /// <summary>
    /// Debug test: Check if inner function can see let binding after initialization
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Debug_InnerFunctionCanAccessLetAfterInit()
    {
        var testLogger = new TestLogger(output, "TdzTest", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        // This should work: function called AFTER let x is initialized
        var result = await engine.Evaluate("""
            (function() {
              let x = 42;
              function f() { return x; }
              return f();
            }());
        """);

        output.WriteLine($"Result: {result}");
        var val2 = result is JsValue jv2 ? jv2.AsDouble() : Convert.ToDouble(result, CultureInfo.InvariantCulture);
        Assert.Equal(42.0, val2);
    }

    /// <summary>
    /// Debug test: Check if inner function can assign to let binding after initialization
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Debug_InnerFunctionCanWriteLetAfterInit()
    {
        var testLogger = new TestLogger(output, "TdzTest", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        // This should work: function called AFTER let x is initialized, then writes to it
        var result = await engine.Evaluate("""
            (function() {
              let x = 42;
              function f() { x = 100; }
              f();
              return x;
            }());
        """);

        output.WriteLine($"Result: {result}");
        var val3 = result is JsValue jv3 ? jv3.AsDouble() : Convert.ToDouble(result, CultureInfo.InvariantCulture);
        Assert.Equal(100.0, val3);
    }

    [Fact(Timeout = 10000)]
    public async Task ClosureTdz_WithAssertThrowsPattern()
    {
        var testLogger = new TestLogger(output, "TdzTest", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        // Load Test262-like assert.throws pattern
        await engine.Evaluate("""
            function assert(condition, message) {
                if (!condition) throw new Error(message || 'Assertion failed');
            }
            assert.throws = function(expectedErrorConstructor, func, message) {
                var thrown = false;
                try {
                    func();
                } catch (e) {
                    thrown = true;
                    if (!(e instanceof expectedErrorConstructor)) {
                        throw new Error('Expected ' + expectedErrorConstructor.name + ' but got ' + e.name);
                    }
                }
                if (!thrown) {
                    throw new Error('Expected function to throw, but it did not');
                }
            };
        """);

        // Now test the same pattern as the Test262 test
        var result = await engine.Evaluate("""
            var testPassed = false;
            (function() {
              function f() { x = 1; }

              assert.throws(ReferenceError, function() {
                f();
              });
              testPassed = true;

              let x;
            }());
            testPassed;
        """);

        output.WriteLine($"Result: {result}");
        Assert.True((bool)result!);
    }
}
