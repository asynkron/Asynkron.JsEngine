using Asynkron.JsEngine.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Quick replication of Test262 failures to verify we see the same issue locally.
/// Based on the failing tests list, these should fail with the current implementation.
/// </summary>
public class CatchCompletionValueReplicationTest
{
    private readonly ITestOutputHelper _output;

    public CatchCompletionValueReplicationTest(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Replicates: language/statements/try/cptn-catch.js
    /// Tests completion value from catch block when catch is executed.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Replicate_CptnCatch()
    {
        var logger = new TestLogger(_output, "CptnCatch", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // Test 1: Empty catch block should return undefined
        var result1 = await engine.Evaluate("eval('1; try { throw null; } catch (err) { }')");
        _output.WriteLine($"Empty catch result: {result1} (expected: undefined)");
        Assert.True(result1 is null || ReferenceEquals(result1, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Empty catch should return undefined, got: {result1}");

        // Test 2: Catch block with expression should return that value
        var result2 = await engine.Evaluate("eval('2; try { throw null; } catch (err) { 3; }')");
        _output.WriteLine($"Catch with value result: {result2} (expected: 3)");
        Assert.Equal(3.0, result2);
    }

    /// <summary>
    /// Replicates: language/statements/try/cptn-try.js
    /// Tests completion value from try block when no exception is thrown.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Replicate_CptnTry()
    {
        var logger = new TestLogger(_output, "CptnTry", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // Test 1: Empty try block should return undefined
        var result1 = await engine.Evaluate("eval('1; try { } catch (e) { }')");
        _output.WriteLine($"Empty try result: {result1} (expected: undefined)");
        Assert.True(result1 is null || ReferenceEquals(result1, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Empty try should return undefined, got: {result1}");

        // Test 2: Try block with value, no exception - should return try's value
        var result2 = await engine.Evaluate("eval('2; try { 3; } catch (e) { }')");
        _output.WriteLine($"Try with value result: {result2} (expected: 3)");
        Assert.Equal(3.0, result2);

        // Test 3: Empty try, catch with value - catch not executed
        var result3 = await engine.Evaluate("eval('4; try { } catch (e) { 5; }')");
        _output.WriteLine($"Empty try (catch not executed) result: {result3} (expected: undefined)");
        Assert.True(result3 is null || ReferenceEquals(result3, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Empty try (catch not executed) should return undefined, got: {result3}");

        // Test 4: Try with value, catch with value - catch not executed
        var result4 = await engine.Evaluate("eval('6; try { 7; } catch (e) { 8; }')");
        _output.WriteLine($"Try with value (catch not executed) result: {result4} (expected: 7)");
        Assert.Equal(7.0, result4);
    }

    /// <summary>
    /// Replicates: language/statements/if/cptn-else-false-nrml.js
    /// Tests completion value from else branch when condition is false.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Replicate_CptnElseFalseNrml()
    {
        var logger = new TestLogger(_output, "CptnIf", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // Test 1: Empty if and else - should return undefined
        var result1 = await engine.Evaluate("eval('1; if (false) { } else { }')");
        _output.WriteLine($"Empty else result: {result1} (expected: undefined)");
        Assert.True(result1 is null || ReferenceEquals(result1, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Empty else should return undefined, got: {result1}");

        // Test 2: Else with value
        var result2 = await engine.Evaluate("eval('2; if (false) { } else { 3; }')");
        _output.WriteLine($"Else with value result: {result2} (expected: 3)");
        Assert.Equal(3.0, result2);

        // Test 3: If with value, empty else
        var result3 = await engine.Evaluate("eval('4; if (false) { 5; } else { }')");
        _output.WriteLine($"Empty else (if has value) result: {result3} (expected: undefined)");
        Assert.True(result3 is null || ReferenceEquals(result3, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Empty else (if has value) should return undefined, got: {result3}");

        // Test 4: Both have values
        var result4 = await engine.Evaluate("eval('6; if (false) { 7; } else { 8; }')");
        _output.WriteLine($"Else with value (both have values) result: {result4} (expected: 8)");
        Assert.Equal(8.0, result4);
    }

    /// <summary>
    /// Replicates: language/statements/for-of/continue-from-catch.js
    /// Tests that continue works correctly inside catch in for-of.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Replicate_ContinueFromCatch()
    {
        var logger = new TestLogger(_output, "ContinueFromCatch", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            var count = 0;
            for (var x of [1, 2, 3]) {
                try {
                    throw 'error';
                } catch (e) {
                    count++;
                    continue;
                }
                count = -1000; // Should never reach here
            }
            count;
        """);

        _output.WriteLine($"Continue from catch result: {result} (expected: 3)");
        Assert.Equal(3.0, result);
    }

    /// <summary>
    /// Replicates: language/statements/for-of/break-from-finally.js
    /// Tests that break works correctly inside finally in for-of.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Replicate_BreakFromFinally()
    {
        var logger = new TestLogger(_output, "BreakFromFinally", minLogLevel: LogLevel.Debug, maxLogCount:10000);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            var count = 0;
            for (var x of [1, 2, 3]) {
                try {
                    count++;
                } finally {
                    break;
                }
                count = -1000; // Should never reach here
            }
            count;
        """);

        _output.WriteLine($"Break from finally result: {result} (expected: 1)");
        Assert.Equal(1.0, result);
    }

    /// <summary>
    /// Replicates: language/statements/for/cptn-decl-expr-iter.js
    /// Tests completion value from for loop iterations.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Replicate_ForCptnDeclExprIter()
    {
        var logger = new TestLogger(_output, "ForCptn", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // for loop completion value should be last expression evaluated in body
        var result = await engine.Evaluate("eval('for (var i = 0; i < 3; i++) { i; }')");
        _output.WriteLine($"For loop completion result: {result} (expected: 2)");
        Assert.Equal(2.0, result);
    }

    /// <summary>
    /// Replicates: language/statements/switch/cptn-a-abrupt-empty.js
    /// Tests completion value from switch with abrupt completion.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Replicate_SwitchCptnAbruptEmpty()
    {
        var logger = new TestLogger(_output, "SwitchCptn", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // Break with empty value should preserve previous completion value
        var result = await engine.Evaluate("eval('1; switch (1) { case 1: break; }')");
        _output.WriteLine($"Switch abrupt empty result: {result} (expected: undefined)");
        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Switch abrupt empty should return undefined, got: {result}");
    }
}
